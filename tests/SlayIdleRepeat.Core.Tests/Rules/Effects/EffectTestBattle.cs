using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// A battle stated literally: the actors, the clock and the run reading a `18` §4/§5 evaluation reads.
/// </summary>
/// <remarks>
/// The rosters are the ones the design documents actually describe rather than abstract fixtures, so a
/// failure says which clause broke.
/// <para>
/// ⚠️ Deliberately <b>not</b> in <c>SlayIdleRepeat.Core</c>: `30` §11.4 gives <c>Core/Testing/</c> to
/// <c>InMemoryGame</c>, and a test fixture is not a shipped harness.
/// </para>
/// </remarks>
internal static class EffectTestBattle
{
    /// <summary>`05` §3's 90 s timeout — the horizon of an ordinary fight.</summary>
    internal const double PveTimeoutSeconds = 90.0;

    /// <summary>`05` §3.1's <c>SYS_ENRAGE</c> <c>startDelay</c> — bosses only.</summary>
    internal const double EnrageSeconds = 70.0;

    /// <summary>`05` §3.3 / `11` §4.3's <c>pvpMaxFightSeconds</c>.</summary>
    internal const double PvpTimeoutSeconds = 60.0;

    /// <summary>The hero: side <see cref="BattleSide.HERO"/>, index 0 (`05` §3.1).</summary>
    internal static EffectTestActor Hero(double currentHp = 100, double maxHp = 100) =>
        new()
        {
            Id = "HERO",
            Index = 0,
            Side = BattleSide.HERO,
            Kind = EffectActorKind.HERO,
            CurrentHp = currentHp,
            MaxHp = maxHp,
        };

    /// <summary>A pet — `05` §3.2's untargetable, unkillable ability module, in slot order.</summary>
    internal static EffectTestActor Pet(string id, int index, BattleSide side = BattleSide.HERO) =>
        new()
        {
            Id = id,
            Index = index,
            Side = side,
            Kind = EffectActorKind.PET,
        };

    /// <summary>An enemy at the given `05` §3.1 index.</summary>
    internal static EffectTestActor Enemy(string id, int index, double currentHp = 100, double maxHp = 100) =>
        new()
        {
            Id = id,
            Index = index,
            Side = BattleSide.ENEMY,
            Kind = EffectActorKind.ENEMY,
            CurrentHp = currentHp,
            MaxHp = maxHp,
        };

    /// <summary>
    /// A run at the start of a run: stage 1 of chapter 1, nothing held.
    /// </summary>
    /// <remarks>
    /// <see cref="RunStateReading.StageIndex"/> and <see cref="RunStateReading.Chapter"/> are
    /// <c>required</c> — the type refuses to invent a position — so the fixture states them once,
    /// here, where the choice is visible, rather than every call site restating them.
    /// </remarks>
    internal static RunStateReading Run() => new() { StageIndex = 1, Chapter = 1 };

    /// <summary>
    /// The context for a PvE fight: the roster, the 90 s horizon, no enrage, and a run reading
    /// present.
    /// </summary>
    internal static EffectEvaluationContext Context(
        IEffectActorView holder,
        params IEffectActorView[] actors) =>
        new()
        {
            Holder = holder,
            Actors = actors,
            FightHorizonSeconds = PveTimeoutSeconds,
            Run = Run(),
        };

    /// <summary>
    /// `05` §3.3's duel: <em>"the same code path with two hero-shaped sides"</em>. Both heroes, one
    /// pet each, the 60 s cap, no run.
    /// </summary>
    internal static EffectEvaluationContext Duel()
    {
        var attacker = Hero() with { Id = "HERO_ATTACKER" };
        var attackerPet = Pet("PET_ATTACKER", 1);
        var defender = Hero() with { Id = "HERO_DEFENDER", Index = 2, Side = BattleSide.ENEMY };
        var defenderPet = Pet("PET_DEFENDER", 3, BattleSide.ENEMY);

        return new EffectEvaluationContext
        {
            Holder = attacker,
            CurrentTarget = defender,
            Actors = new IEffectActorView[] { attacker, attackerPet, defender, defenderPet },
            FightHorizonSeconds = PvpTimeoutSeconds,
            IsPvp = true,
            Run = null,
        };
    }

    /// <summary>A combat stream in the shape `14` §8.1 fixes: the battle seed, handed in.</summary>
    internal static DeterministicRng CombatRng(ulong battleSeed) =>
        new(battleSeed, RngStreams.Combat);
}

/// <summary>An actor stated literally — the test double for <see cref="IEffectActorView"/>.</summary>
internal sealed record EffectTestActor : IEffectActorView
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public int Index { get; init; }

    /// <inheritdoc />
    public BattleSide Side { get; init; } = BattleSide.ENEMY;

    /// <inheritdoc />
    public EffectActorKind Kind { get; init; } = EffectActorKind.ENEMY;

    /// <inheritdoc />
    public bool IsAlive { get; init; } = true;

    /// <inheritdoc />
    public double CurrentHp { get; init; } = 100;

    /// <inheritdoc />
    public double MaxHp { get; init; } = 100;

    /// <inheritdoc />
    public bool IsElite { get; init; }

    /// <inheritdoc />
    public bool IsBoss { get; init; }

    /// <inheritdoc />
    public bool IsSummon { get; init; }

    /// <inheritdoc />
    public string? OwnerId { get; init; }

    /// <summary>The `05` §5 statuses on this actor, by id.</summary>
    public IReadOnlyDictionary<string, int> Statuses { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <inheritdoc />
    public int StatusStacks(string statusId) =>
        Statuses.TryGetValue(statusId, out var stacks) ? stacks : 0;
}
