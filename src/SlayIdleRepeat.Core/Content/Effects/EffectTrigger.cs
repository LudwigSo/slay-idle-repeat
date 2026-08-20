namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// <em>When</em> an effect fires — the <c>trigger</c> block: a <see cref="TriggerKind"/> plus the
/// parameters that kind admits.
/// </summary>
/// <remarks>
/// One record with nullable parameters rather than 23 kind-specific types: the schema partitions
/// the 23 kinds into thirteen closed parameter shapes, so a mismatched key is a validation failure
/// before it is ever a C# value. Restating that partition as a type hierarchy would give the same
/// guarantee twice. Every parameter is <c>null</c> when the author did not write it; nothing here
/// supplies a default.
/// </remarks>
public sealed record EffectTrigger
{
    /// <summary>Which of the 23 kinds this is.</summary>
    public required TriggerKind Kind { get; init; }

    /// <summary><see cref="TriggerKind.ON_BATTLE_END"/> — fire only on a win.</summary>
    public bool? OnlyIfWon { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_ATTACK"/> / <see cref="TriggerKind.ON_KILL"/> — fire on every Nth
    /// occurrence. <c>ON_ATTACK</c> counters reset at battle start; <c>ON_KILL</c> counters persist
    /// across battles for the run.
    /// </summary>
    public int? EveryNth { get; init; }

    /// <summary>0..1 probability, drawn from the deterministic RNG.</summary>
    public double? Chance { get; init; }

    /// <summary>Seconds of battle time before the trigger may fire again.</summary>
    public double? Cooldown { get; init; }

    /// <summary><see cref="TriggerKind.ON_LOW_HP"/> — the 0..1 HP fraction crossed downward.</summary>
    public double? Threshold { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_LOW_HP"/> / <see cref="TriggerKind.ON_LETHAL"/> — fire at most once
    /// per battle.
    /// </summary>
    /// <remarks>
    /// Boolean, not a count: another document describes the same field as a count, which is a
    /// contradiction between the two, recorded as errata. This DSL's own spelling wins here.
    /// </remarks>
    public bool? Once { get; init; }

    /// <summary><see cref="TriggerKind.PERIODIC"/> — seconds between firings.</summary>
    public double? Interval { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.PERIODIC"/> — seconds of battle time before the first firing.
    /// </summary>
    public double? StartDelay { get; init; }

    /// <summary><see cref="TriggerKind.ON_PHASE_ENTER"/> — the boss phase, 1..3.</summary>
    public int? Phase { get; init; }

    /// <summary><see cref="TriggerKind.ON_TILE_RESOLVED"/> — a <c>TILE_*</c> id.</summary>
    public string? TileType { get; init; }

    // ON_ROLL used to carry a FaceKind, so an effect could fire only on a particular face. The die
    // has no face kinds, so an ON_ROLL trigger fires on every roll and carries no filter.

    /// <summary><see cref="TriggerKind.ON_PERK_TAKEN"/> — a perk category.</summary>
    /// <remarks>
    /// A string, not an enum: only one of the six categories appears as a worked token so far, and
    /// inventing spellings for the other five would be plausible-looking fabrication. The schema
    /// constrains the shape, and a later perk schema closes the set.
    /// </remarks>
    public string? Category { get; init; }
}
