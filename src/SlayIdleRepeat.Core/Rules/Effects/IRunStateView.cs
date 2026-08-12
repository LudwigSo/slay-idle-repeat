namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 The read-only run-state contract behind the nine `18` §4 conditions that read the run rather
/// than the battle: <c>PERK_COUNT</c>, <c>DISTINCT_PERK_CATEGORIES</c>, <c>PET_COUNT</c>,
/// <c>DIE_FACE_COUNT</c>, <c>GOLD_HELD</c>, <c>BATTLES_WON_THIS_RUN</c>, <c>STAGE_INDEX</c>,
/// <c>CHAPTER</c> and <c>TIER</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is a cross-milestone contract.</b> The state it names lives on the <c>Run</c> aggregate
/// (M1-05) and on the run controller (M3), neither of which exists when this is written. The nine
/// conditions read through this interface so that the evaluator can be finished, tested and frozen
/// now, and so that the two later implementations have exactly one shape to satisfy rather than each
/// inventing one.
/// </para>
/// <para>
/// ⚠️ <b>The <c>Run</c> aggregate cannot implement this directly, and must not try.</b> `30` §11.4
/// states the internal layering as <c>Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives</c> and says in
/// terms that <em>"<c>Model</c> never references <c>Rules</c>"</em>;
/// <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> enforces it. A
/// <c>Run : IRunStateView</c> would be a <c>Model</c> type naming a <c>Rules</c> one and would fail
/// that rule. The contract is honoured the way `30` §11.5 prescribes for every other computation over
/// an aggregate — <em>"<c>PowerCalculator.Compute(player, content)</c>, not <c>player.Power</c>"</em>
/// — by a projection that lives on this side of the seam and reads the aggregate's getters.
/// <see cref="RunStateReading"/> is that shape, ready to be populated.
/// </para>
/// <para>
/// 🔒 <b>Narrow, and to stay narrow.</b> Nine members, one per condition, all read-only, with no path
/// back to the aggregate. `18` §4 needs no more, and a wider interface here becomes the forced shape
/// of M3's controller. Anything an <em>op</em> needs (M2-03/M2-04) is that op's business and does not
/// belong on the condition layer's view.
/// </para>
/// <para>
/// 🔒 <b>Not a port.</b> It declares no I/O and it is not registered in <c>Application/Ports/</c> —
/// that catalogue is M5-01's and a different layer (`23` §2.2). This is a read-only view of domain
/// state that happens not to be written yet.
/// </para>
/// <para>
/// ⚠️ An effect that reads any of these in a Ghost Duel is a content error, not a runtime one: `18`
/// §9.3 rules that non-combat clauses are <em>"simply skipped"</em> in PvP via the <c>IS_PVP</c>
/// condition. The evaluator therefore fails loudly rather than substituting a zero when a duel context
/// carries no run view — see <c>EffectContextException</c>.
/// </para>
/// </remarks>
internal interface IRunStateView
{
    /// <summary>
    /// <c>PERK_COUNT</c> — `18` §4: <em>"int, optionally by category"</em>.
    /// </summary>
    /// <param name="category">
    /// One of `06` §2's six categories (plus the hidden Cursed one), or <c>null</c> for every perk
    /// held. Compared ordinally; an unknown category is <c>0</c>, not an error, because a perk
    /// category the run happens to hold none of is a legitimate reading.
    /// </param>
    int PerkCount(string? category);

    /// <summary>
    /// <c>DISTINCT_PERK_CATEGORIES</c> — how many of `06` §2's categories the run holds at least one
    /// perk from. `PK_ARSENAL`'s <em>"+3% all stats per distinct perk category you own"</em>.
    /// </summary>
    int DistinctPerkCategories { get; }

    /// <summary>
    /// <c>PET_COUNT</c> — how many pets the run has equipped (0–3, `05` §3).
    /// </summary>
    /// <remarks>
    /// ⚠️ Distinct from the <c>ALL_PETS</c> target, which counts the pets present in the current
    /// battle's roster. They agree in an ordinary fight and are not the same question: this one is
    /// answerable outside a battle, and `18` §1.1 names <c>PET_COUNT</c> as a <c>valueScale</c>
    /// source, where it is read against run state.
    /// </remarks>
    int PetCount { get; }

    /// <summary>
    /// <c>DIE_FACE_COUNT</c> — `18` §4: <em>"int, by face kind"</em>. How many faces of the given
    /// `04` §1 kind the run's dice currently carry.
    /// </summary>
    /// <param name="faceKind">
    /// One of `04` §1's six names — <c>Pip · Star · Surge · Fortune · Void · Chain</c>. A string
    /// rather than an enum for the reason <see cref="Content.Effects.DieFaceSpec"/> records: the
    /// closed set is the dice system's vocabulary to declare, and restating it here would be two
    /// enums that must agree with nothing making them.
    /// </param>
    int DieFaceCount(string faceKind);

    /// <summary>
    /// <c>GOLD_HELD</c> — the Gold currently held. `PK_HOARD`'s <em>"+1% ATK per 100 Gold currently
    /// held"</em> (`18` §1.1), which is uncapped.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>long</c>, where `18` §4's table says <em>"int"</em>. The table is describing the
    /// <em>kind</em> of reading — a whole number, not a fraction — alongside rows that say
    /// <em>"0..1"</em> and <em>"bool"</em>; it is not a width declaration. A currency balance that
    /// silently wraps at 2,147,483,647 in a game with an uncapped gold-scaling perk is the kind of
    /// hole that surfaces once, in production, on somebody's best run.
    /// <para>
    /// ⚠️ <b>The real ceiling is the <c>double</c>, not the <c>long</c>.</b>
    /// <c>ConditionEvaluator.Read</c> returns every reading as a <c>double</c>. The widening never
    /// throws and never wraps, but it is exact only to about 1.4 × 10¹³ Gold — above that the 4 dp
    /// round-trip can return a non-integer, and above 2⁵³ the conversion itself collapses adjacent
    /// balances onto one reading. <c>PK_HOARD</c>'s <c>floor(gold / 100)</c> survives all of it; an
    /// exact <c>{"fn":"GOLD_HELD","op":"eq"}</c> against a large literal does not. Lossy, but
    /// identical on every platform, so it is a precision limit and not a determinism break.
    /// </para>
    /// </remarks>
    long GoldHeld { get; }

    /// <summary><c>BATTLES_WON_THIS_RUN</c> — battles won so far in the current run.</summary>
    int BattlesWonThisRun { get; }

    /// <summary><c>STAGE_INDEX</c> — `18` §4: <em>"1..3"</em>. The run's current stage (`02` §1.2).</summary>
    int StageIndex { get; }

    /// <summary><c>CHAPTER</c> — the run's chapter, as an int.</summary>
    int Chapter { get; }

    /// <summary>
    /// <c>TIER</c> — the run's difficulty tier.
    /// </summary>
    /// <remarks>
    /// ⚠️ `18` §4 types this row <em>"enum"</em>, and no tier enum exists anywhere in the repository
    /// yet — <c>02</c> §2's <c>runSeed</c> derivation is the only place <c>tierId</c> is even named.
    /// Steering S6 forbids inventing the members, so what is declared here is the tier's
    /// <b>ordinal</b>: the comparison a <c>ConditionTerm</c> performs is numeric in every case
    /// (<c>{"fn":"TIER","op":"gte","value":3}</c>), so the ordinal is what a condition can actually
    /// use. When the milestone that owns difficulty tiers declares the enum, this becomes its
    /// ordinal and nothing here changes.
    /// </remarks>
    int Tier { get; }
}
