using System.Globalization;
using System.Runtime.ExceptionServices;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>What one sweep of the expiry job did.</summary>
/// <param name="Examined">How many due messages it looked at.</param>
/// <param name="AutoGranted">How many it paid out before deleting.</param>
/// <param name="Deleted">How many it removed.</param>
/// <param name="Withholdings">
/// The messages it removed WITHOUT paying, each with the named refusal that stopped it. Never
/// silent: a reward that was owed and could not be paid is the one thing an operator has to see.
/// </param>
public sealed record InboxSweep(
    int Examined,
    int AutoGranted,
    int Deleted,
    IReadOnlyList<(MessageId Message, MailAttachmentRefusal Refusal)> Withholdings);

/// <summary>
/// The expiry sweep: unclaimed attachments are auto-granted when the message expires, never
/// destroyed. The message disappears; the reward does not.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>It grants through <c>GameRules.Apply</c> and a real <c>CLAIM_INBOX</c>, not through a
/// second grant path.</b> The Energy Reserve cascade, the wallet seam and the "all the attachments
/// or none" rule are the claim's, and a job with its own arithmetic beside them is the copy that
/// eventually pays a different amount than the button does. What makes this legal is that the job
/// draws nothing: it issues no <c>CommandSeed</c>, because it is not a command, and the claim rules
/// never ask for one — which is also exactly why a rolled reward (gear) cannot be an attachment.
/// </para>
/// <para>
/// 🔒 <b>Grant and stamp, then delete, in that order and never the other way.</b> The claim is
/// stamped before the row is removed, so a crash between them leaves a message that reads as paid
/// and is removed unpaid by the next sweep. Deleting first would destroy the reward outright, which
/// is the one thing the expiry rule exists to make impossible.
/// </para>
/// <para>
/// 🔒 <b>No MILESTONE message is sent.</b> The requirement is "auto-grants emit a MILESTONE message
/// only if the value is material", and nothing anywhere authors what material means. Rather than
/// guess a threshold, the feature is off and named — the register carries it with its owner.
/// </para>
/// <para>
/// ⚠️ <b>A message whose attachments this build cannot grant is removed without being paid</b>, and
/// that is reported rather than swallowed. Keeping it for ever would make the first ungrantable
/// attachment an inbox row nothing can remove; both outcomes are bad, and the visible one is better.
/// It cannot arise from a message this build sent — the send path refuses an ungrantable attachment
/// before writing anything — only from a row some other writer put there.
/// </para>
/// <para>
/// It holds no timer and no host type: WHEN it runs is the composition root's, and what it does is
/// this. That split is what lets a whole sweep be driven in a unit test at any instant.
/// </para>
/// </remarks>
public sealed class InboxExpiryJob
{
    private readonly IMessageRepository _messages;
    private readonly IPlayerRepository _players;
    private readonly IAnalyticsSinkPort _analytics;
    private readonly ContentSnapshot _content;

    /// <summary>Builds the sweep over the stores it reads and the sink it reports through.</summary>
    /// <param name="messages">The inbox store.</param>
    /// <param name="players">The player store the grants are paid into.</param>
    /// <param name="analytics">Where each auto-grant is reported.</param>
    /// <param name="content">The content set the rules are applied against.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public InboxExpiryJob(
        IMessageRepository messages,
        IPlayerRepository players,
        IAnalyticsSinkPort analytics,
        ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(content);

        _messages = messages;
        _players = players;
        _analytics = analytics;
        _content = content;
    }

    /// <summary>Sweeps one batch of due messages.</summary>
    /// <param name="asOfUtc">The instant to judge expiry against, and the instant the rules see.</param>
    /// <param name="limit">How many due messages to take. Positive.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>What the sweep did.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is not positive.</exception>
    public async Task<InboxSweep> SweepAsync(DateTimeOffset asOfUtc, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit,
                "A sweep that takes no messages grants nothing and deletes nothing, and reports a " +
                "clean run for an inbox filling up behind it.");
        }

        var due = await _messages.DequeueExpiringAsync(asOfUtc, limit, ct).ConfigureAwait(false);
        var granted = 0;
        var withheld = new List<(MessageId, MailAttachmentRefusal)>();
        var removable = new List<MessageId>(due.Count);
        List<Exception>? unpayable = null;

        foreach (var message in due)
        {
            var projection = message.ToProjection();

            if (projection.IsClaimed || message.Attachments.Count == 0)
            {
                removable.Add(message.Id);
                continue;
            }

            if (projection.FirstRefusal is { } refusal)
            {
                withheld.Add((message.Id, refusal));
                removable.Add(message.Id);
                continue;
            }

            try
            {
                await PayAsync(message, projection, asOfUtc, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception fault)
            {
                // 🔒 The message that could not be paid is isolated, not the batch. The read is
                // "oldest expiry first" and it removes nothing, so a throw that escaped before the
                // delete below left the batch intact — and tomorrow's sweep re-read the identical
                // rows in the identical order and died on the identical message. One corrupt player
                // row switched inbox expiry off permanently, for every player.
                //
                // It still stays: not added to `removable`, so nothing destroys its reward, and the
                // faults are rethrown below so the night's loop stops and the failure is reported
                // rather than counted as a clean pass.
                (unpayable ??= []).Add(fault);

                continue;
            }

            granted++;
            removable.Add(message.Id);
        }

        await _messages.DeleteAsync(removable, ct).ConfigureAwait(false);

        if (unpayable is { Count: 1 })
        {
            // Rethrown as itself, stack intact, because the diagnosis IS the message: it names the
            // message and the player, and that is what the sweep's contract promises a reader.
            ExceptionDispatchInfo.Capture(unpayable[0]).Throw();
        }

        if (unpayable is not null)
        {
            throw new AggregateException(
                "The sweep could not pay " + unpayable.Count.ToString(CultureInfo.InvariantCulture) +
                " of " + due.Count.ToString(CultureInfo.InvariantCulture) + " due messages. Those " +
                "rows are untouched and still owed; the rest of the batch was paid and removed.",
                unpayable);
        }

        return new InboxSweep(due.Count, granted, removable.Count, withheld);
    }

    private async Task PayAsync(
        PlayerMessage message, InboxMessage projection, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        var profile = await _players.GetAsync(message.Player, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Message " + message.Id + " is owed to player " + message.Player + ", who has no " +
                "stored row. Paying it would have to invent the account, and skipping it silently " +
                "would destroy a reward — so the sweep stops and says which message and which player.");

        var player = Player.Rehydrate(profile.Player, _content);

        if (player.IsFailure)
        {
            throw new InvalidOperationException(
                "The stored row for player " + message.Player + " does not load: " + player.Error +
                ". The sweep pays real rewards into it, so it stops here rather than granting into " +
                "a half-read account.");
        }

        var run = profile.ActiveRun is { } row ? Run.Rehydrate(row) : null;

        if (run is { IsFailure: true })
        {
            throw new InvalidOperationException(
                "The stored run of player " + message.Player + " does not load: " + run.Error +
                ". The claim rules are applied to the whole slice, so a run that cannot be read is a " +
                "slice that cannot be built.");
        }

        var slice = new WorldSlice(
            player.Value, run?.Value, new InboxView(new[] { projection }));

        // 🔒 CommandSeed is null, and that is the statement rather than an omission: a nightly job is
        // not a command, so it holds no per-command seed — which is precisely why no attachment kind
        // that needs a draw can ever be granted here.
        // 🔒 The kill switches are the "nothing killed" identity rather than the live config, and
        // that is the requirement rather than a shortcut: mail off hides the inbox and the expiry
        // job still auto-grants, so a switch thrown during an incident must not be what destroys the
        // rewards the incident is about to compensate. The entitlement is the unresolved one — no
        // session exists for a sweep, and the claim rules read entitlement for nothing.
        var context = new GameContext(
            asOfUtc,
            CommandSeed: null,
            _content,
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved());

        var result = GameRules.Apply(slice, new ClaimInboxCommand(new[] { message.Id.Value }), context);

        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                "The claim rules refused the auto-grant of message " + message.Id + " with " +
                result.Rejection + ". The sweep already asked whether the message was claimable, so a " +
                "refusal here means the two disagree — and continuing would delete the row with the " +
                "reward unpaid and nothing said about it.");
        }

        // Stamped before the row is removed, so a crash after the grant leaves a message that reads
        // as paid and the next sweep removes it without paying twice.
        await _messages
            .MarkClaimedAsync(message.Player, new[] { message.Id }, ct)
            .ConfigureAwait(false);

        await _players
            .SaveAsync(
                new PlayerProfile(result.NewState.Player.ToSnapshot(), result.NewState.Run?.ToSnapshot()),
                ct)
            .ConfigureAwait(false);

        _analytics.Track(message.Player, new AnalyticsEvent(
            AnalyticsVocabulary.MailExpiredAutogranted,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message_id"] = message.Id.Value,
                ["category"] = message.Category.ToString(),
                ["attachments"] =
                    message.Attachments.Count.ToString(CultureInfo.InvariantCulture),
            }));
    }
}
