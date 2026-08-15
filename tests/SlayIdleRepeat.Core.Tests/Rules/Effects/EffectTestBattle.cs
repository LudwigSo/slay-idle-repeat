using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>A battle stated literally: the actors, the clock and the run reading an evaluation reads.</summary>
/// <remarks>
/// The rosters are the ones the design documents actually describe rather than abstract fixtures, so a
/// failure says which clause broke. Deliberately not in <c>SlayIdleRepeat.Core</c>: <c>Core/Testing/</c>
/// is reserved for <c>InMemoryGame</c>, and a test fixture is not a shipped harness.
/// </remarks>
internal static class EffectTestBattle
{
    /// <summary>The 90 s timeout — the horizon of an ordinary fight.</summary>
    internal const double PveTimeoutSeconds = 90.0;

    /// <summary><c>SYS_ENRAGE</c>'s <c>startDelay</c> — bosses only.</summary>
    internal const double EnrageSeconds = 70.0;

    /// <summary><c>pvpMaxFightSeconds</c>.</summary>
    internal const double PvpTimeoutSeconds = 60.0;

    /// <summary>The hero: side <see cref="BattleSide.HERO"/>, index 0.</summary>
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

    /// <summary>A pet — an untargetable, unkillable ability module, in slot order.</summary>
    internal static EffectTestActor Pet(string id, int index, BattleSide side = BattleSide.HERO) =>
        new()
        {
            Id = id,
            Index = index,
            Side = side,
            Kind = EffectActorKind.PET,
        };

    /// <summary>An enemy at the given index.</summary>
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
    /// A run at the start of a run: stage 1 of chapter 1, nothing held. <see cref="RunStateReading"/>
    /// requires these two positional fields, so the fixture states them once, here.
    /// </summary>
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

    /// <summary>A duel: the same code path with two hero-shaped sides. Both heroes, one pet each, the 60 s cap, no run.</summary>
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

    /// <summary>A combat RNG stream keyed on the given battle seed.</summary>
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

    /// <summary>The statuses on this actor, by id.</summary>
    public IReadOnlyDictionary<string, int> Statuses { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <inheritdoc />
    public int StatusStacks(string statusId) =>
        Statuses.TryGetValue(statusId, out var stacks) ? stacks : 0;
}
