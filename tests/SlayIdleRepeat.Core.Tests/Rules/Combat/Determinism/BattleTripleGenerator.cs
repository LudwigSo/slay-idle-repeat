using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>
/// One <c>(seed, build, enemy)</c> triple, in the shape <c>CanonicalStateWriter</c> encodes.
/// </summary>
/// <param name="Index">The triple's ordinal, so a row is self-identifying in a failure message.</param>
/// <param name="BattleSeed">The seed the fight is simulated under — the <em>seed</em> of the triple.</param>
/// <param name="HeroLevel">The hero's Legend Level.</param>
/// <param name="HeroStats">The fourteen combat stats in <c>StatIds.Combat</c> order — the <em>build</em>.</param>
/// <param name="Chapter">Which chapter's pool the roster is drawn from.</param>
/// <param name="TierOrdinal">NORMAL / HEROIC / MYTHIC as 0 / 1 / 2.</param>
/// <param name="EnemyPowers">The roster's power budgets, one per body — the <em>enemy</em>.</param>
/// <param name="EliteIndex">Which roster slot is an elite, or <c>-1</c> for none.</param>
internal sealed record BattleTriple(
    int Index,
    ulong BattleSeed,
    int HeroLevel,
    IReadOnlyList<double> HeroStats,
    int Chapter,
    int TierOrdinal,
    IReadOnlyList<double> EnemyPowers,
    int EliteIndex);

/// <summary>
/// The 10 000 <c>(seed, build, enemy)</c> triples of `14` §8.2, generated from one committed seed.
/// </summary>
/// <remarks>
/// <para>
/// Every triple is a pure function of <see cref="BaselineSeed"/> and its own ordinal, drawn through
/// the project's own <see cref="Hash64"/> and <see cref="DeterministicRng"/> rather than any ambient
/// generator — so the corpus is reproducible on a runner nobody has logged into, which is the only
/// kind of corpus a cross-architecture comparison can use. The seed a fight runs under and the seed
/// the fight's SHAPE is drawn from are two different derivations of the same baseline, so the shape
/// draws and the combat draws cannot correlate.
/// </para>
/// <para>
/// 🔴 Stat values are drawn WIDE and deliberately over the shipped ceilings: a ratio stat is drawn on
/// <c>[0, 0.8)</c> while the six capped stats top out between 0.4 and 0.75 in
/// <c>content/combat_caps.json</c>. Nothing here transcribes those ceilings — over-cap draws are the
/// point, because the cap step is itself an accumulation point and a corpus that never reached it
/// would leave that arithmetic uncompared across architectures.
/// </para>
/// </remarks>
internal static class BattleTripleGenerator
{
    /// <summary>`14` §8.2's count, verbatim: ten thousand fixed triples.</summary>
    internal const int TripleCount = 10_000;

    /// <summary>How many triples one committed chunk hash covers.</summary>
    /// <remarks>
    /// 100 × 100. A committed table of ten thousand raw hashes would be unreviewable, and a single
    /// aggregate would say only "something moved"; a moved chunk localises a break to a hundred
    /// triples, which is a diff a person can read.
    /// </remarks>
    internal const int ChunkSize = 100;

    /// <summary>The corpus seed, committed and never drawn.</summary>
    /// <remarks>The ASCII of the task that authored it, in the high half, so a stray literal cannot be mistaken for it.</remarks>
    internal const ulong BaselineSeed = 0x4D35_3132_0000_0000UL;

    /// <summary>The lowest chapter the shipped enemy pools carry.</summary>
    internal const int FirstChapter = 1;

    /// <summary>How many chapters the roster draw spans, read as a span rather than a last chapter.</summary>
    /// <remarks>
    /// Deliberately smaller than <c>content/enemies/enemies.json</c>'s eight pools rather than derived
    /// from them: a corpus whose shape moves when content is retuned would re-baseline on a chapter
    /// being added, and the chapters this spans are the ones every difficulty tier is authored for.
    /// </remarks>
    internal const int ChapterSpan = 8;

    /// <summary>The largest roster a triple draws.</summary>
    internal const int MaxRoster = 4;

    /// <summary>One triple in eight carries an elite.</summary>
    internal const int EliteOdds = 8;

    /// <summary>
    /// The top of the band every ratio stat is drawn from — above the highest shipped ceiling on
    /// purpose, so the cap step is reached rather than assumed.
    /// </summary>
    internal const double RatioCeiling = 0.9;

    /// <summary>The weakest roster a build faces, as a multiple of its own attack value.</summary>
    internal const double PowerRatioFloor = 0.1;

    /// <summary>How many decades above that floor the roster's power ratio is drawn.</summary>
    internal const int PowerDecades = 4;

    /// <summary>One triple in thirty-two is a war of attrition neither side can win.</summary>
    /// <remarks>
    /// 🔒 The deliberate coverage arm. Every other draw produces a fight that ends when somebody dies,
    /// which is usually inside a few dozen ticks — and the accumulation drift `14` §8.2 exists to
    /// catch is at its most visible in a fight that runs the full <c>CombatLog.MaxTicks</c> bound,
    /// where eighteen hundred ticks of damage, mitigation and status arithmetic pile up before
    /// anything is read. Left to chance the corpus contains none of those at all.
    /// </remarks>
    internal const int AttritionOdds = 32;

    /// <summary>The fold label that derives a triple's battle seed from the baseline.</summary>
    /// <remarks>
    /// A <see cref="Hash64"/> argument, not an RNG stream name: <see cref="DeterministicRng"/>
    /// validates its stream against `14` §8.1's registry, and a corpus fixture is not a system that
    /// belongs in it. The two labels differ so the seed a fight runs under and the seed its shape is
    /// drawn from cannot correlate.
    /// </remarks>
    private const string BattleSeedLabel = "m5-12/battle";

    /// <summary>The fold label that derives a triple's shape seed from the baseline.</summary>
    private const string ShapeSeedLabel = "m5-12/shape";

    /// <summary>The battle seed of one triple.</summary>
    internal static ulong BattleSeedFor(int triple) =>
        Hash64.Of(BaselineSeed, BattleSeedLabel, (ulong)triple);

    /// <summary>The whole corpus, in ordinal order.</summary>
    internal static IReadOnlyList<BattleTriple> Corpus()
    {
        var triples = new List<BattleTriple>(TripleCount);
        for (var index = 0; index < TripleCount; index++)
        {
            triples.Add(TripleAt(index));
        }

        return triples;
    }

    /// <summary>One triple, derived from nothing but the baseline seed and the ordinal.</summary>
    internal static BattleTriple TripleAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        // RngStreams.Combat, not a name of this corpus's own: DeterministicRng refuses any stream the
        // registry does not carry, and the shape seed is a different fold from the battle seed, so
        // sharing the name costs nothing.
        var rng = new DeterministicRng(
            Hash64.Of(BaselineSeed, ShapeSeedLabel, (ulong)index), RngStreams.Combat);

        var attrition = rng.Range(0, AttritionOdds) == 0;
        var roster = rng.Range(1, MaxRoster + 1);
        var heroLevel = rng.Range(1, 201);
        var stats = DrawStats(rng, attrition, roster);
        var chapter = rng.Range(FirstChapter, FirstChapter + ChapterSpan);
        var tierOrdinal = rng.Range(0, Enum.GetValues<DifficultyTier>().Length);

        // Enemy power is drawn RELATIVE to the build it faces, not on an absolute range. An absolute
        // range lets the drawn attack value decide the fight before the seed does — a corpus whose
        // low end is one-shot by every build and whose high end one-shots every build is mostly
        // one-tick fights, and a one-tick fight accumulates none of the arithmetic `14` §8.2 compares.
        // A decade first and a mantissa inside it, because the interesting span is four decades wide:
        // a body worth a tenth of the build's attack loses instantly, one worth a thousand times it
        // wins instantly, and everything in between is a fight. Explicit multiplication rather than
        // Math.Pow, which §8.2 asks combat code to avoid and this fixture has no reason to introduce.
        var decade = attrition ? 0 : rng.Range(0, PowerDecades);
        var scale = 1.0;
        for (var step = 0; step < decade; step++)
        {
            scale *= 10.0;
        }

        var powerRatio = PowerRatioFloor * scale * (1.0 + (rng.NextDouble() * 9.0));
        var heroAttack = stats[ActorStats.SlotOf(StatId.ATK)];

        var powers = new List<double>(roster);
        for (var body = 0; body < roster; body++)
        {
            // An attrition body is derived far too tough for a hero swinging for one to kill, while
            // the hero's hit points — ten billion per body — outlast everything the roster deals back.
            powers.Add(attrition
                ? Rounded(5e7 + (rng.NextDouble() * 1.5e8))
                : Rounded(40.0 + (heroAttack * powerRatio)));
        }

        var eliteIndex = rng.Range(0, EliteOdds) == 0 ? rng.Range(0, roster) : -1;

        return new BattleTriple(
            index, BattleSeedFor(index), heroLevel, stats, chapter, tierOrdinal, powers, eliteIndex);
    }

    /// <summary>The triple's build, as the simulator's own argument type.</summary>
    internal static ActorStats StatsOf(BattleTriple triple)
    {
        ArgumentNullException.ThrowIfNull(triple);

        var values = new Dictionary<StatId, double>(StatIds.Combat.Count);
        for (var slot = 0; slot < StatIds.Combat.Count; slot++)
        {
            values[StatIds.Combat[slot]] = triple.HeroStats[slot];
        }

        return ActorStats.From(values);
    }

    /// <summary>
    /// The fourteen combat stats, in <c>StatIds.Combat</c> order and drawn per stat rather than from
    /// one shared range.
    /// </summary>
    /// <remarks>
    /// A single range would put MAX_HP and CRIT on the same scale, and a corpus in which no hero can
    /// survive a swing exercises one branch of the attack pipeline ten thousand times. The bands here
    /// are chosen so a fight is usually decided well inside <c>CombatLog.MaxTicks</c> and both
    /// outcomes occur — the budget assertion in the tests is what keeps that honest.
    /// </remarks>
    private static IReadOnlyList<double> DrawStats(DeterministicRng rng, bool attrition, int roster)
    {
        var slots = new double[StatIds.Combat.Count];
        for (var slot = 0; slot < slots.Length; slot++)
        {
            slots[slot] = StatIds.Combat[slot] switch
            {
                // The two stats the attrition arm overrides: everything else stays drawn, so those
                // fights are still different fights and not one repeated shape. The hit points scale
                // with the roster because every body swings, and a fight that ends when the hero dies
                // is not the fight this arm exists to produce.
                StatId.MAX_HP when attrition =>
                    Rounded((1e10 * roster) + (rng.NextDouble() * 1e10)),
                StatId.ATK when attrition => Rounded(0.5 + (rng.NextDouble() * 2.0)),
                StatId.MAX_HP => Rounded(400.0 + (rng.NextDouble() * 24_000.0)),
                StatId.ATK => Rounded(20.0 + (rng.NextDouble() * 1_400.0)),
                StatId.DEF => Rounded(rng.NextDouble() * 900.0),
                StatId.ASPD => Rounded(0.4 + (rng.NextDouble() * 2.1)),
                StatId.CDMG => Rounded(0.2 + (rng.NextDouble() * 1.8)),
                StatId.HEAL_PCT => Rounded(0.5 + (rng.NextDouble() * 1.5)),
                _ => DrawRatio(rng),
            };
        }

        return slots;
    }

    /// <summary>One of the eight ratio stats — crit, dodge, block, penetration, lifesteal, thorns and the two percentages.</summary>
    /// <remarks>
    /// The product of two unit draws rather than one, which pulls the mass towards the small values a
    /// real build actually carries while still reaching <see cref="RatioCeiling"/> often enough to put
    /// hundreds of over-cap draws in the corpus. Drawn flat across the whole band instead, every build
    /// ends up with four defensive rolls near their ceilings at once and the hero wins essentially
    /// every fight — which loses the hero-death arm entirely.
    /// </remarks>
    private static double DrawRatio(DeterministicRng rng) =>
        Rounded(rng.NextDouble() * rng.NextDouble() * RatioCeiling);

    /// <summary>
    /// The one rounding rule, applied to a drawn value.
    /// </summary>
    /// <remarks>
    /// A drawn double is 53 bits of mantissa and <c>ActorStats.From</c> refuses anything that is not
    /// already rounded — so the corpus rounds at the point that produces the value, exactly as the
    /// production accumulation points do, rather than being handed a rounding exemption for being a
    /// test.
    /// </remarks>
    private static double Rounded(double value) => DeterminismRounding.Round(value);
}
