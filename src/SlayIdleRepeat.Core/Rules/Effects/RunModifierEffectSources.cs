using System.Collections.ObjectModel;
using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The three run-scoped collection sources of `18` §8 step 1: <c>SHRINE_BUFFS</c>, <c>RUN_BUFFS</c>
/// and <c>CURSES</c> — what a Shrine tile, a Shop's slot 3 and a Curse tile contribute to the hero.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>These are what make three of the fourteen tiles mean anything.</b> Before them a shrine
/// wrote an id onto the run and nothing read it: a player who took Sharpened Resolve fought exactly
/// as hard as one who walked past the shrine, and a Curse tile was strictly good — it paid its
/// reward and applied no debuff at all. The pool was authored, the aggregation pipeline was built,
/// and the wire between them was the missing piece, exactly as it was for perks.
/// </para>
/// <para>
/// 🔒 <b>Ordered by ACQUISITION, and that order is persisted.</b> The source contract requires an
/// order that is a function of the build and identical on client and server. A run stores these as
/// lists in the order they were taken, so unlike the perk source — which has only a dictionary to
/// read and falls back to catalogue order — these can honour the real one.
/// </para>
/// <para>
/// A holding a later content version no longer authors is skipped, not refused, on the perk and
/// loadout sources' precedent: a content rollback across a live run must not leave that run unable
/// to fight. What it costs the player is the buff; what refusing would cost them is the run.
/// </para>
/// </remarks>
internal static class RunModifierEffects
{
    /// <summary>
    /// The effect id for the <paramref name="occurrence"/>-th copy of one holding.
    /// </summary>
    /// <remarks>
    /// 🔒 The occurrence index is in the ID, not only in the instance, and that is load-bearing:
    /// `18` §8 sorts by effect id and two effects sharing one id fall to a source-position tiebreak,
    /// so three stacks of <c>SHR_ATK</c> under one id would be three effects the resolution order
    /// could only separate by accident. Stacking additively is what `03` §7a.5 asks for, and three
    /// distinct ids is how the pipeline expresses it.
    /// </remarks>
    internal static string EffectId(string prefix, string holdingId, int occurrence) =>
        prefix + ":" + holdingId + ":" + occurrence.ToString(CultureInfo.InvariantCulture);

    /// <summary>A percentage-move effect on one stat, in the shape the schema would accept.</summary>
    internal static EffectDefinition PctAdd(string effectId, StatId stat, double value) => new()
    {
        Id = effectId,
        Op = EffectOp.STAT_ADD_PCT,
        Stat = StatSelector.Of(stat),
        Trigger = EffectDefaults.Always,
        Target = EffectDefaults.AbsentTarget,
        Value = value,
    };

    /// <summary>A flat-add effect on one stat, in the shape the schema would accept.</summary>
    internal static EffectDefinition FlatAdd(string effectId, StatId stat, double value) => new()
    {
        Id = effectId,
        Op = EffectOp.STAT_ADD_FLAT,
        Stat = StatSelector.Of(stat),
        Trigger = EffectDefaults.Always,
        Target = EffectDefaults.AbsentTarget,
        Value = value,
    };
}

/// <summary>The <c>SHRINE_BUFFS</c> source: `03` §7a.5's pool, at the stacks this run has taken.</summary>
/// <remarks>
/// <para>
/// Every authored row but <c>SHR_HEAL</c> contributes one <c>STAT_ADD_PCT</c>; <c>SHR_HEAL</c>
/// authors <c>stat: null</c> on purpose — it is a heal, paid at the moment the option is taken, and
/// carries no permanent buff. A row with no stat contributes no effect rather than a zero-valued
/// one, so "took Spring of Mercy" and "took nothing" are the same thing to the fight, which they
/// are.
/// </para>
/// <para>
/// <c>SHR_HP</c> is the one row that is BOTH: an immediate heal and a permanent +18% Max HP. The
/// heal is <c>ShrineResolver</c>'s and happens once; the stat is this source's and is re-derived
/// every pass. Splitting them is what stops the heal being re-applied on every aggregation.
/// </para>
/// </remarks>
internal sealed class ShrineBuffEffectSource : IEffectSource
{
    /// <summary>The effect-id prefix and the instance-id prefix for a shrine buff holding.</summary>
    internal const string Prefix = "shrine";

    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads what the run's shrine buffs contribute.</summary>
    /// <param name="content">The version-stamped snapshot the pool is read from.</param>
    /// <param name="taken">The buff ids taken this run, in the order taken. Duplicates stack.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal ShrineBuffEffectSource(ContentSnapshot content, IReadOnlyList<string> taken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(taken);

        var effects = new List<SourcedEffect>(taken.Count);

        // A run that has taken no shrine buff does not read the pool at all, on the perk source's
        // argument: a hero screen outside a run is built against whatever content the caller loaded,
        // and demanding the shrine document from a build that could not use one would make an absent
        // pool fatal to callers that never visited a shrine.
        var pool = taken.Count == 0 ? null : ShrineTuning.Read(content);

        for (var i = 0; i < taken.Count; i++)
        {
            var row = Find(pool!, taken[i]);

            // Skipped, not refused — see the type remarks on a content rollback. Also covers
            // SHR_HEAL, which authors no stat and contributes nothing permanent.
            if (row is not { Stat: { } token, Magnitude: { } magnitude })
            {
                continue;
            }

            if (!RunStatTokens.TryParse(token, out var stat))
            {
                continue;
            }

            var effectId = RunModifierEffects.EffectId(Prefix, taken[i], i);

            effects.Add(new SourcedEffect(
                RunModifierEffects.PctAdd(effectId, stat, magnitude),
                EffectInstanceId.Of(effectId)));
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    private static ShrineBuffPoolEntry? Find(ShrineTuning pool, string id)
    {
        foreach (var row in pool.Buffs)
        {
            if (string.Equals(row.Id, id, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.SHRINE_BUFFS;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}

/// <summary>The <c>RUN_BUFFS</c> source: `03` §7's shop slot-3 buffs, at the stacks this run has bought.</summary>
/// <remarks>
/// 🔒 <b>Flat, not percentage, and that is what the document says</b> — "flat, permanent for this
/// run, applied as <c>FlatAdd</c> in the `05` §1.1 aggregation". The magnitudes double per chapter
/// (<c>base × chapterGrowth^(c-1)</c>) so they track the ×2 chapter power curve; Hawk's Eye's
/// <c>chapterGrowth</c> is authored as <c>1.0</c> because Crit Chance is a capped stat and the
/// document holds it chapter-invariant. The growth is read from the document rather than hard-coded,
/// so the exception is data, not a branch.
/// </remarks>
internal sealed class RunBuffEffectSource : IEffectSource
{
    /// <summary>The effect-id prefix and the instance-id prefix for a run buff holding.</summary>
    internal const string Prefix = "runbuff";

    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads what the run's bought run buffs contribute.</summary>
    /// <param name="content">The version-stamped snapshot the buff table is read from.</param>
    /// <param name="bought">The run buff ids bought this run, in the order bought. Duplicates stack.</param>
    /// <param name="chapterId">The chapter the run is being played in, for the magnitude curve.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal RunBuffEffectSource(ContentSnapshot content, IReadOnlyList<string> bought, int chapterId)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(bought);

        var effects = new List<SourcedEffect>(bought.Count);
        var shop = bought.Count == 0 ? null : ShopTuning.Read(content);

        for (var i = 0; i < bought.Count; i++)
        {
            var row = Find(shop!, bought[i]);

            if (row is not { } definition || !RunStatTokens.TryParse(definition.Stat, out var stat))
            {
                continue;
            }

            var effectId = RunModifierEffects.EffectId(Prefix, bought[i], i);

            effects.Add(new SourcedEffect(
                RunModifierEffects.FlatAdd(effectId, stat, MagnitudeAt(definition, chapterId)),
                EffectInstanceId.Of(effectId)));
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    /// <summary>
    /// One run buff's chapter-<paramref name="chapterId"/> magnitude: <c>base × chapterGrowth^(c-1)</c>.
    /// </summary>
    /// <remarks>
    /// Computed in <c>double</c> after one conversion rather than by repeated <c>decimal</c>
    /// multiplication: the result is an effect value, which is a <c>double</c>, and rounding it once
    /// at the end through the shared rounding is what makes client and server agree bit for bit.
    /// A chapter below 1 is clamped to the base rather than producing a fractional exponent — a
    /// chapter id is 1-based and there is no chapter 0 to author a magnitude for.
    /// </remarks>
    internal static double MagnitudeAt(ShopRunBuffDefinition definition, int chapterId)
    {
        var steps = Math.Max(0, chapterId - 1);

        return DeterminismRounding.Round(
            (double)definition.Base * Math.Pow((double)definition.ChapterGrowth, steps));
    }

    private static ShopRunBuffDefinition? Find(ShopTuning shop, string id)
    {
        foreach (var row in shop.RunBuffs)
        {
            if (string.Equals(row.Id, id, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.RUN_BUFFS;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}

/// <summary>The <c>CURSES</c> source: the debuffs of `19` Part E's curses this run carries.</summary>
/// <remarks>
/// <para>
/// Six of the twelve curses are a percentage move on one stat and contribute one effect each. The
/// other six do not, and this source contributes NOTHING for them rather than approximating one —
/// <see cref="CurseEffects.UnappliedReason"/> names the missing mechanism for each, and one of them
/// (<c>CUR_SLIPPERY</c>) is honoured by the board instead, in <c>Handlers.RollDice</c>.
/// </para>
/// <para>
/// The occurrence index is still in the effect id even though a run can never hold one curse twice
/// (`19` Part E gives curses no stacking, and <c>Run.ApplyCurse</c> enforces it): the id shape is
/// shared with its two sibling sources, which CAN stack, and one shape means one rule to reason
/// about rather than two that happen to agree today.
/// </para>
/// </remarks>
internal sealed class CurseEffectSource : IEffectSource
{
    /// <summary>The effect-id prefix and the instance-id prefix for a curse holding.</summary>
    internal const string Prefix = "curse";

    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads what the run's active curses contribute.</summary>
    /// <param name="active">The curse ids active on this run, in the order applied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="active"/> is null.</exception>
    /// <remarks>
    /// Takes no <c>ContentSnapshot</c>, and that is a real difference from its two siblings rather
    /// than an omission: the magnitudes live in <see cref="CurseEffects"/> because the curse
    /// document's own effect column is prose. The day those magnitudes become authored data, this
    /// constructor grows the parameter its siblings already have.
    /// </remarks>
    internal CurseEffectSource(IReadOnlyList<string> active)
    {
        ArgumentNullException.ThrowIfNull(active);

        var effects = new List<SourcedEffect>(active.Count);

        for (var i = 0; i < active.Count; i++)
        {
            if (CurseEffects.Stat(active[i]) is not { } move)
            {
                continue;
            }

            var effectId = RunModifierEffects.EffectId(Prefix, active[i], i);

            effects.Add(new SourcedEffect(
                RunModifierEffects.PctAdd(effectId, move.Stat, move.PctAdd),
                EffectInstanceId.Of(effectId)));
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.CURSES;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}
