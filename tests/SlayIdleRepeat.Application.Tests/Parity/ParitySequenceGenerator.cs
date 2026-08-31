using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>One generated command sequence.</summary>
/// <param name="Index">The sequence's ordinal.</param>
/// <param name="Commands">The commands, in the order both hosts will receive them.</param>
internal sealed record ParitySequence(int Index, IReadOnlyList<GameCommand> Commands);

/// <summary>
/// `14` §13's 1 000 command sequences, generated from one committed seed by a walker that knows what
/// the domain will accept.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Legality-aware, and that is the whole design.</b> Most of the 55-command registry is refused
/// from most states — 34 rows are deferred to later milestones and answer <c>ILLEGAL_STATE</c>
/// unconditionally, and the phase gates refuse most of the rest — so a naive uniform walk would be a
/// thousand sequences of refusals, comparing the refusal path a thousand times and the accepted path
/// never. At each step the walker draws up to <see cref="CandidatesPerStep"/> candidates and takes
/// the first the domain accepts, keeping the last one if none is accepted: refusals stay in the
/// corpus, because a refusal is a state hash both hosts must agree on too, but they no longer crowd
/// out everything else.
/// </para>
/// <para>
/// ⚠️ <b>The walk is a heuristic, not a claim about either host.</b> It probes with
/// <c>GameRules.Apply</c> under a drawn per-command seed, while each host draws its own from its id
/// generator, so a meta command that consumes randomness moves the walker's reference state
/// somewhere neither host goes. That costs the walk some of its aim and nothing else: the assertion
/// is that the two hosts agree with EACH OTHER on the sequence, never that either agrees with the
/// walker.
/// </para>
/// <para>
/// 🔒 <b>The walker stands in for the Application layer, so it loads what that layer loads.</b> It
/// probes <c>GameRules.Apply</c> directly, which means the one step <c>ApplyCommandUseCase</c> takes
/// between loading a slice and calling the rules is the walker's to take too: a command that reads
/// the inbox is probed against a slice carrying one. The predicate and the projection are the
/// production ones rather than a second answer to "which commands need an inbox", so a later command
/// joining that set reaches this walk with no edit here.
/// </para>
/// <para>
/// 🔴 <c>START_RUN</c> is excluded. It is the one command the two hosts are DESIGNED to disagree on:
/// the gateway allocates the run id from its id generator, and the in-process host passes no
/// <c>AllocatedRunId</c> at all, so the two mint different run ids for the same command — and the run
/// id is inside the hashed projection. That disagreement is pinned by its own case rather than
/// hidden here, and the corpus instead starts from a run both sides already share.
/// </para>
/// </remarks>
internal static class ParitySequenceGenerator
{
    /// <summary>`14` §13's count, verbatim: one thousand command sequences.</summary>
    internal const int SequenceCount = 1_000;

    /// <summary>How many sequences one committed chunk hash covers.</summary>
    internal const int ChunkSize = 100;

    /// <summary>The corpus seed, committed and never drawn.</summary>
    internal const ulong BaselineSeed = 0x4D35_3132_5041_5259UL;

    /// <summary>The shortest sequence the walker emits.</summary>
    internal const int MinLength = 3;

    /// <summary>The longest sequence the walker emits.</summary>
    internal const int MaxLength = 8;

    /// <summary>How many candidates a step tries before it settles for a refusal.</summary>
    internal const int CandidatesPerStep = 6;

    /// <summary>The one command the corpus does not carry, and the reason it does not.</summary>
    internal const string ExcludedWireName = "START_RUN";

    /// <summary>The fold label that derives a sequence's draw seed from the baseline.</summary>
    private const string SequenceSeedLabel = "m5-12/parity";

    /// <summary>The command types the walker draws from, in wire-name order.</summary>
    /// <remarks>
    /// Ordered so the alphabet is a function of the registry's contents and not of its enumeration
    /// order, which a dictionary does not promise.
    /// </remarks>
    internal static IReadOnlyList<Type> Alphabet { get; } = GameRules.CommandTypesByWireName
        .Where(row => !row.Key.Equals(ExcludedWireName, StringComparison.Ordinal))
        .OrderBy(row => row.Key, StringComparer.Ordinal)
        .Select(row => row.Value)
        .ToArray();

    /// <summary>The whole corpus, in ordinal order.</summary>
    /// <param name="baseline">The state both hosts start every sequence from.</param>
    /// <param name="content">The content snapshot both hosts judge against.</param>
    /// <param name="nowUtc">The instant both hosts read from their shared clock.</param>
    /// <param name="entitlements">The entitlement ambience both hosts carry.</param>
    /// <param name="flags">The kill switches both hosts carry.</param>
    /// <param name="inbox">
    /// The baseline player's inbox, loaded through the production seam — <c>InboxView.Empty</c> for a
    /// player with no messages, never <c>null</c>, which is the miswired caller the rules refuse.
    /// </param>
    internal static IReadOnlyList<ParitySequence> Corpus(
        WorldSlice baseline,
        ContentSnapshot content,
        DateTimeOffset nowUtc,
        Entitlements entitlements,
        FeatureFlags flags,
        InboxView inbox)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(inbox);

        var sequences = new List<ParitySequence>(SequenceCount);
        for (var index = 0; index < SequenceCount; index++)
        {
            sequences.Add(SequenceAt(index, baseline, content, nowUtc, entitlements, flags, inbox));
        }

        return sequences;
    }

    /// <summary>One sequence, derived from nothing but the baseline seed and the ordinal.</summary>
    internal static ParitySequence SequenceAt(
        int index,
        WorldSlice baseline,
        ContentSnapshot content,
        DateTimeOffset nowUtc,
        Entitlements entitlements,
        FeatureFlags flags,
        InboxView inbox)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(inbox);

        // RngStreams.Draft, not a name of this corpus's own: DeterministicRng refuses any stream
        // `14` §8.1's registry does not carry, and a fixture is not a system that belongs in it.
        var rng = new DeterministicRng(
            Hash64.Of(BaselineSeed, SequenceSeedLabel, (ulong)index), RngStreams.Draft);

        var state = baseline;
        var length = rng.Range(MinLength, MaxLength + 1);
        var commands = new List<GameCommand>(length);

        for (var step = 0; step < length; step++)
        {
            GameCommand? settled = null;

            for (var attempt = 0; attempt < CandidatesPerStep; attempt++)
            {
                var candidate = ParityCommandFactory.Build(
                    Alphabet[rng.Range(0, Alphabet.Count)], rng, content.Version.Value);

                // A run command applied to a slice with no run THROWS rather than refusing, so the
                // one state in which that is reachable — a run the walk has already left — filters
                // the alphabet instead of being caught after the fact.
                if (state.Run is null && !GameRules.RequiresCommandSeed(candidate))
                {
                    continue;
                }

                settled = candidate;

                var context = new GameContext(
                    nowUtc,
                    GameRules.RequiresCommandSeed(candidate) ? rng.NextUInt() : null,
                    content,
                    entitlements,
                    flags);

                var result = GameRules.Apply(
                    InboxCommandSupport.Reads(candidate) ? state with { Inbox = inbox } : state,
                    candidate,
                    context);

                if (result.Accepted)
                {
                    // The inbox is a per-command loan the Application layer makes, never part of the
                    // stored slice, so the walk gives it back rather than carrying it into a step
                    // whose real dispatch would not have it.
                    state = result.NewState with { Inbox = null };
                    break;
                }
            }

            if (settled is null)
            {
                throw new InvalidOperationException(
                    $"Sequence {index} step {step} drew {CandidatesPerStep} candidates and every one " +
                    "of them was a run command against a slice with no run. The walk has nothing legal " +
                    "left to emit, which means the alphabet has lost its meta rows.");
            }

            commands.Add(settled);
        }

        return new ParitySequence(index, commands);
    }
}
