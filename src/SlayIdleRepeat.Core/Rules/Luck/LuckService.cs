using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// The one façade over every pity guarantee in the game: every grant that can be protected resolves
/// through here, and nothing outside this namespace may reach the guarantee primitives directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stateless by construction.</b> It takes the counters as an argument and answers the deltas; it
/// never holds them, never writes them and never decides whether the draw is kept. That is what lets
/// the same resolution be replayed against a stored counter map without a second source of truth for
/// the counters.
/// </para>
/// <para>
/// <b>It takes an already-opened draw stream and never opens one.</b> The caller chooses the stream
/// — a run stream for an in-run drop, the command's own for a container open — because the choice is
/// a seed-derivation decision and this type has no business making it. Opening a stream here would
/// also put a second, unregistered stream position in the game's most replayed path.
/// </para>
/// <para>
/// <b>Exactly one draw per resolution, whether or not a guarantee fired.</b> Forcing is expressed as
/// flooring the table, so the forced path and the natural path consume the same single draw index
/// and a resumed stream lands in the same place either way. A resolution that is refused — a class
/// with no ladder, a table a floor has emptied — is refused before drawing, so it consumes nothing.
/// </para>
/// <para>
/// <b>It serves the classes that author a rarity ladder, and refuses the rest by name.</b> Five
/// classes state their protection as hard rungs plus a soft curve and are drawn here. The other five
/// state theirs in another shape entirely, and a call for one of them throws with the class, the
/// block that holds its real rule and the task that wires it — rather than silently drawing against
/// a ladder that was never authored. Those five reach the primitives through their own resolvers,
/// which are still inside this namespace: the guarantee decision itself never leaves it.
/// </para>
/// </remarks>
internal static class LuckService
{
    /// <summary>
    /// Resolves one protected draw: applies the soft-pity ramp, decides whether a hard guarantee
    /// fires, draws once, and answers the outcome together with every counter the draw moved.
    /// </summary>
    /// <remarks>
    /// A caller-supplied <paramref name="floor"/> and a guarantee that fires are the same operation
    /// — both floor the table — so a guaranteed draw and a floored one advance and reset counters
    /// identically, and a floored draw that overshoots still resets the counter it overshot.
    /// </remarks>
    /// <param name="source">The grant source class this draw belongs to.</param>
    /// <param name="tuning">The pity registry, read from the snapshot the command is holding.</param>
    /// <param name="table">The class's weighted rarity table, before any floor or ramp.</param>
    /// <param name="counters">The player's counters as they stand before this draw.</param>
    /// <param name="draws">
    /// The already-opened draw stream, continued — never restarted. Exactly one index is consumed.
    /// </param>
    /// <param name="floor">
    /// An optional caller-imposed rarity floor, for a source that states one of its own. Null means
    /// only the class's own guarantees can floor the table.
    /// </param>
    /// <returns>The outcome, whether pity forced it, and the counter changes to apply.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> or <paramref name="floor"/> is not declared.</exception>
    /// <exception cref="InvalidOperationException">
    /// The class states its protection in a shape this path does not serve, or the floored table
    /// carries no weight at all. Neither consumes a draw index.
    /// </exception>
    internal static LuckResolution Resolve(
        SourceClass source,
        LuckTuning tuning,
        RarityTable table,
        PityCounters counters,
        DeterministicRng draws,
        Rarity? floor = null)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(draws);
        RequireDeclared(source, nameof(source));

        if (floor.HasValue)
        {
            RequireDeclared(floor.Value, nameof(floor));
        }

        var ladder = LadderOf(source, tuning);
        var keys = CounterKeys(source, tuning, ladder);
        var forced = HighestFiringGuarantee(counters, ladder, keys);

        var drawn = floor.HasValue ? table.FloorAt(floor.Value) : table;
        drawn = Ramped(drawn, source, tuning, counters, ladder);

        if (forced.HasValue)
        {
            drawn = drawn.FloorAt(forced.Value);
        }

        if (!drawn.HasPositiveWeight)
        {
            throw new InvalidOperationException(
                $"The {source} table carries no weight once its floor and ramp are applied, so there " +
                "is nothing to draw. Refused before the draw is taken, so the stream is left where " +
                "it stood.");
        }

        var outcome = draws.WeightedPick(Walk(drawn));

        return new LuckResolution(outcome, forced.HasValue, Moved(counters, ladder, keys, outcome));
    }

    /// <summary>
    /// Whether the next draw of this class would be forced to satisfy a given guarantee.
    /// </summary>
    /// <remarks>
    /// A read-only question over the same decision <see cref="Resolve"/> makes, so a client can show
    /// "1 more chest" without a second statement of the rule.
    /// </remarks>
    /// <param name="source">The grant source class.</param>
    /// <param name="tuning">The pity registry.</param>
    /// <param name="counters">The player's counters as they stand.</param>
    /// <param name="guarantee">The guarantee rarity whose rung is being asked about.</param>
    /// <returns><see langword="true"/> when the next draw would be forced.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum argument is not declared.</exception>
    /// <exception cref="InvalidOperationException">
    /// The class states no rung at this guarantee, or none at all.
    /// </exception>
    internal static bool GuaranteeFires(
        SourceClass source, LuckTuning tuning, PityCounters counters, Rarity guarantee)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(counters);
        RequireDeclared(source, nameof(source));
        RequireDeclared(guarantee, nameof(guarantee));

        var ladder = LadderOf(source, tuning);

        foreach (var rung in ladder.HardPity)
        {
            if (rung.GuaranteeRarityAtLeast == guarantee)
            {
                return HardPity.Fires(
                    counters.Get(tuning.CounterKey(source, guarantee)), rung.EveryNth);
            }
        }

        throw new InvalidOperationException(
            $"{source} states no rung guaranteeing {guarantee}. Answering false would read as 'not " +
            "yet' rather than 'never', and the client would show a counter that can never reach its " +
            "own guarantee.");
    }

    /// <summary>
    /// The multiplier the class's soft-pity curve currently puts on its target's draw weight, or 1
    /// where the class authors no curve.
    /// </summary>
    /// <param name="source">The grant source class.</param>
    /// <param name="tuning">The pity registry.</param>
    /// <param name="counters">The player's counters as they stand.</param>
    /// <returns>The weight multiplier. Never below 1.</returns>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is not declared.</exception>
    /// <exception cref="InvalidOperationException">
    /// The class states no rarity ladder, or its authored curve raises a token that is not a band on
    /// the rarity ladder — the reader keeps the authored token verbatim and it is refused here.
    /// </exception>
    internal static double SoftPityWeight(
        SourceClass source, LuckTuning tuning, PityCounters counters)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(counters);
        RequireDeclared(source, nameof(source));

        return Ramp(source, tuning, counters, LadderOf(source, tuning))?.Multiplier ?? Unramped;
    }

    /// <summary>
    /// The effective success rate of a failure-mercy source after a run of consecutive failures.
    /// </summary>
    /// <remarks>
    /// The additive ramp, not the weight multiplier — see <see cref="SoftPity"/> for why the two are
    /// separate. Exposed here as well so that a caller reaching for a mercy rate never has to name
    /// the primitive.
    /// </remarks>
    /// <param name="baseRate">The unmodified success probability, between 0 and 1.</param>
    /// <param name="consecutiveFailures">Failures since the last success. Never negative.</param>
    /// <param name="slope">The authored per-failure addition. Positive.</param>
    /// <param name="cap">The authored ceiling on the effective rate, between 0 and 1.</param>
    /// <returns>The effective rate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its stated range.</exception>
    internal static double MercyRate(
        double baseRate, int consecutiveFailures, double slope, double cap) =>
        SoftPity.RateWithMercy(baseRate, consecutiveFailures, slope, cap);

    /// <summary>The mercy bank after an accruing event.</summary>
    /// <param name="held">Tokens held before the event. Never negative.</param>
    /// <param name="grant">Tokens the event grants. Never negative; zero is legal.</param>
    /// <returns>Tokens held after the event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    /// <exception cref="OverflowException">The bank would exceed <see cref="int.MaxValue"/>.</exception>
    internal static int AccrueMercy(int held, int grant) => MercyAccrual.Accrue(held, grant);

    /// <summary>The mercy bank after a redemption.</summary>
    /// <param name="held">Tokens held. Never negative.</param>
    /// <param name="cost">The authored cost of the redemption. At least 1.</param>
    /// <returns>Tokens held after the redemption.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An argument is outside its stated range.</exception>
    /// <exception cref="InvalidOperationException">The bank does not cover the cost.</exception>
    internal static int RedeemMercy(int held, int cost) => MercyAccrual.Redeem(held, cost);

    /// <summary>The multiplier a class with no authored curve puts on its table.</summary>
    private const double Unramped = 1.0;

    /// <summary>
    /// The class's ladder, or a refusal in the façade's own vocabulary.
    /// </summary>
    /// <remarks>
    /// Restated as an <see cref="InvalidOperationException"/> because a caller of the rules is being
    /// refused by the rules, not by the content reader — but the reader's wording is reused rather
    /// than rewritten, so the block and the owning task are named in exactly one place.
    /// </remarks>
    private static PityLadder LadderOf(SourceClass source, LuckTuning tuning)
    {
        try
        {
            return tuning.Ladder(source);
        }
        catch (InvalidTunableException unserved)
        {
            throw new InvalidOperationException(unserved.Message, unserved);
        }
    }

    /// <summary>The counter id of every rung of a ladder, in the ladder's own order.</summary>
    /// <remarks>
    /// Formed once per resolution rather than once per rung per pass. The decision pass and the
    /// counter-movement pass address exactly the same counters, and forming one key is a scan of the
    /// registry plus an allocation on the path every grant in the game takes.
    /// </remarks>
    private static string[] CounterKeys(SourceClass source, LuckTuning tuning, PityLadder ladder)
    {
        var keys = new string[ladder.HardPity.Count];

        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = tuning.CounterKey(source, ladder.HardPity[i].GuaranteeRarityAtLeast);
        }

        return keys;
    }

    /// <summary>
    /// The highest guarantee any rung forces on this draw, or <see langword="null"/> when none does.
    /// </summary>
    /// <remarks>
    /// The <em>highest</em>, because a class runs its rungs simultaneously: the 40th standard chest
    /// satisfies the 10-rung and the 40-rung at once, and a draw floored at the lower of the two
    /// would leave the higher counter unreset and its guarantee unkept.
    /// </remarks>
    private static Rarity? HighestFiringGuarantee(
        PityCounters counters, PityLadder ladder, string[] keys)
    {
        Rarity? forced = null;

        for (var i = 0; i < keys.Length; i++)
        {
            var rung = ladder.HardPity[i];

            if (HardPity.Fires(counters.Get(keys[i]), rung.EveryNth) &&
                (forced is null || rung.GuaranteeRarityAtLeast > forced.Value))
            {
                forced = rung.GuaranteeRarityAtLeast;
            }
        }

        return forced;
    }

    /// <summary>The class's curve, resolved against the counter it ramps on.</summary>
    private static (Rarity Target, double Multiplier)? Ramp(
        SourceClass source, LuckTuning tuning, PityCounters counters, PityLadder ladder)
    {
        if (ladder.SoftPity is not { } curve)
        {
            return null;
        }

        if (!Enum.TryParse<Rarity>(curve.Target, ignoreCase: false, out var target) ||
            !Enum.IsDefined(target))
        {
            throw new InvalidOperationException(
                $"{source}'s curve targets '{curve.Target}', which is not a band on the rarity " +
                "ladder, so there is no counter to read it against and no row to widen. A class " +
                "whose curve targets something else states its rule in another shape entirely.");
        }

        return (target, SoftPity.WeightMultiplier(
            counters.Get(tuning.CounterKey(source, target)), curve.MissThreshold, curve.Slope));
    }

    /// <summary>The table with the class's soft-pity curve applied to its target's row.</summary>
    private static RarityTable Ramped(
        RarityTable table,
        SourceClass source,
        LuckTuning tuning,
        PityCounters counters,
        PityLadder ladder) =>
        Ramp(source, tuning, counters, ladder) is { } ramp
            ? table.Scale(ramp.Target, ramp.Multiplier)
            : table;

    /// <summary>
    /// Every counter the draw moved: reset where the outcome reached the rung's guarantee, advanced
    /// where it did not.
    /// </summary>
    /// <remarks>
    /// A natural draw that overshoots a guarantee resets it exactly as a forced one does — the
    /// player is never punished for good luck by having a guarantee taken away later.
    /// </remarks>
    private static IReadOnlyList<PityCounterChange> Moved(
        PityCounters counters, PityLadder ladder, string[] keys, Rarity outcome)
    {
        var changes = new PityCounterChange[keys.Length];

        for (var i = 0; i < changes.Length; i++)
        {
            changes[i] = new PityCounterChange(
                keys[i],
                outcome >= ladder.HardPity[i].GuaranteeRarityAtLeast
                    ? HardPity.Reset()
                    : HardPity.Advance(counters.Get(keys[i])));
        }

        return Array.AsReadOnly(changes);
    }

    /// <summary>The table as the draw stream's own weighted-walk shape.</summary>
    private static IReadOnlyList<(Rarity, double)> Walk(RarityTable table)
    {
        var rows = table.Rows;
        var walk = new (Rarity, double)[rows.Count];

        for (var i = 0; i < walk.Length; i++)
        {
            walk[i] = (rows[i].Rarity, rows[i].Weight);
        }

        return walk;
    }

    private static void RequireDeclared(SourceClass source, string parameter)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(
                parameter, source, "That is not a grant source class the registry declares.");
        }
    }

    private static void RequireDeclared(Rarity rarity, string parameter)
    {
        if (!Enum.IsDefined(rarity))
        {
            throw new ArgumentOutOfRangeException(
                parameter, rarity, "That is not a rarity on the ladder.");
        }
    }
}
