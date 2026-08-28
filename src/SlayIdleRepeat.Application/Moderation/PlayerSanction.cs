using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Moderation;

/// <summary>One sanction recorded against one account.</summary>
/// <param name="SanctionId">The sanction's identity.</param>
/// <param name="Subject">The sanctioned account.</param>
/// <param name="Kind">Which rung of the ladder.</param>
/// <param name="EntryId">The confirmed review entry this came out of. A sanction with no reviewed entry behind it is not a shape this record allows.</param>
/// <param name="AppliedBy">🔒 The unverified operator string — see <see cref="ReviewQueueEntry"/>: nothing authenticates a reviewer.</param>
/// <param name="AppliedAtUtc">When it took effect.</param>
/// <param name="LiftedAtUtc">When it stopped, or <c>null</c> while it stands.</param>
/// <remarks>
/// A sanction is never computed. It exists because a human confirmed a review entry and then
/// recorded this, which is why <paramref name="EntryId"/> is required rather than optional: the
/// ladder acts only on repeated, confirmed manipulation, and an entry id is the only thing that can
/// evidence the "confirmed" half.
/// </remarks>
public sealed record PlayerSanction(
    string SanctionId,
    PlayerId Subject,
    SanctionKind Kind,
    string EntryId,
    string AppliedBy,
    DateTimeOffset AppliedAtUtc,
    DateTimeOffset? LiftedAtUtc = null)
{
    /// <summary>Whether this sanction stands at <paramref name="instant"/>.</summary>
    /// <param name="instant">The moment being asked about.</param>
    /// <remarks>Applied-at is inclusive and lifted-at is exclusive, so a sanction lifted at <c>t</c> does not stand at <c>t</c>.</remarks>
    public bool IsActiveAt(DateTimeOffset instant) =>
        instant >= AppliedAtUtc && (LiftedAtUtc is not { } lifted || instant < lifted);
}

/// <summary>Whether an account may be served at all — the one wire-enforced sanction, and nothing else.</summary>
/// <remarks>
/// 🔒 The mapping is deliberately narrow: an active <see cref="SanctionKind.ACCOUNT_ACTION"/> is
/// HTTP 403 and every other kind is HTTP nothing. A shadow ladder exclusion that refused requests
/// would stop being shadow; a rating reset or a name reset is a one-off write, not a standing
/// refusal. Widening this is the job of the milestone that owns the surface, not of this type.
/// </remarks>
public static class AccountStanding
{
    /// <summary>The status a locked or sanctioned account is refused with. The client shows the account-state screen.</summary>
    public const int LockedHttpStatus = 403;

    /// <summary>The refusal status this account's sanctions demand, or <c>null</c> when it may be served.</summary>
    /// <param name="sanctions">Every sanction recorded against the account, active or not.</param>
    /// <param name="instant">The moment being asked about.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sanctions"/> is null.</exception>
    public static int? RefusalStatusFor(IEnumerable<PlayerSanction> sanctions, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(sanctions);

        return sanctions.Any(sanction => Locks(sanction, instant)) ? LockedHttpStatus : null;
    }

    /// <summary>Whether one sanction locks its account at this instant.</summary>
    /// <param name="sanction">The sanction.</param>
    /// <param name="instant">The moment being asked about.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sanction"/> is null.</exception>
    public static bool Locks(PlayerSanction sanction, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(sanction);

        return sanction.Kind == SanctionKind.ACCOUNT_ACTION && sanction.IsActiveAt(instant);
    }
}

/// <summary>The synchronous question the request path asks before it reads a command: is this account locked?</summary>
/// <remarks>
/// Synchronous because the principal seam it decorates is, and because a database round trip on
/// every request to answer "no" for every honest player would spend the whole command latency
/// budget on the rarest possible answer. The shipped implementation is therefore a snapshot the
/// moderation background service refreshes — see <see cref="AccountStandingSnapshot"/>.
/// </remarks>
public interface IAccountStandingSource
{
    /// <summary>Whether this account carries an active account action.</summary>
    /// <param name="player">The account.</param>
    bool IsAccountActioned(PlayerId player);
}

/// <summary>The locked-account set as of the last refresh — an immutable set swapped whole.</summary>
/// <remarks>
/// 🔒 <b>Deliberately stale, and stale in the safe direction.</b> A newly recorded account action
/// takes effect at the next refresh, not instantly; a lifted one likewise. Sanctions are applied by
/// hand, minutes to days apart, so a refresh interval measured in minutes costs nothing real —
/// whereas a per-request lookup would put the moderation store on the hot path of every command.
/// An empty snapshot (a process that has not refreshed yet, or one running with no moderation
/// store) locks nobody, which is the only safe default: refusing service on absent data would take
/// the whole game down when the moderation store did.
/// </remarks>
public sealed class AccountStandingSnapshot : IAccountStandingSource
{
    private IReadOnlySet<PlayerId> _locked = new HashSet<PlayerId>();

    /// <summary>Replaces the locked set with the accounts these sanctions lock at <paramref name="instant"/>.</summary>
    /// <param name="sanctions">Every known sanction.</param>
    /// <param name="instant">The moment to evaluate them at.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sanctions"/> is null.</exception>
    public void Replace(IEnumerable<PlayerSanction> sanctions, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(sanctions);

        // Built whole and swapped in one write: a set mutated in place would let a request read it
        // half-updated and refuse — or serve — an account on a state that never existed.
        var locked = sanctions
            .Where(sanction => AccountStanding.Locks(sanction, instant))
            .Select(sanction => sanction.Subject)
            .ToHashSet();

        Volatile.Write(ref _locked, locked);
    }

    /// <inheritdoc/>
    public bool IsAccountActioned(PlayerId player) => Volatile.Read(ref _locked).Contains(player);

    /// <summary>How many accounts the current snapshot locks.</summary>
    public int LockedAccounts => Volatile.Read(ref _locked).Count;
}
