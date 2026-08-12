namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// <em>When</em> an effect fires — `18` §3's <c>trigger</c> block: a <see cref="TriggerKind"/> plus
/// the parameters that kind admits.
/// </summary>
/// <remarks>
/// <para>
/// One record with nullable parameters rather than 23 kind-specific types. The reason is the schema:
/// <c>game-data/schema/effect.schema.json</c> partitions the 23 kinds into thirteen closed
/// parameter shapes, so <c>{"kind":"ON_HIT","cooldown":3}</c> is a <em>validation</em> failure
/// before it is ever a C# value. Restating that partition as a type hierarchy would give the same
/// guarantee twice and leave two places to change when `18` grows a parameter.
/// </para>
/// <para>
/// ⚠️ Every parameter is <c>null</c> when the author did not write it. Nothing here supplies a
/// default — <c>game-data/README.md</c>: <em>"null means the design docs do not authorise a value
/// here… never coerce such a hole to a default at read time; fail loudly."</em> `18` §3 gives no
/// default for any of these, so M2-04 rules on each as it wires it.
/// </para>
/// </remarks>
public sealed record EffectTrigger
{
    /// <summary>Which of `18` §3's 23 kinds this is.</summary>
    public required TriggerKind Kind { get; init; }

    /// <summary><see cref="TriggerKind.ON_BATTLE_END"/> — fire only on a win.</summary>
    public bool? OnlyIfWon { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_ATTACK"/> / <see cref="TriggerKind.ON_KILL"/> — fire on every Nth
    /// occurrence. `18` §3: <c>ON_ATTACK</c> counters reset at battle start; <c>ON_KILL</c> counters
    /// <b>persist across battles for the run</b>.
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
    /// ⚠️ <b>Boolean, per `18` §7.4's <c>"once": true</c>.</b> `05` §3.1 describes the same field as
    /// a count — <em>"fire at most their authored <c>once</c> count per battle"</em> — which is a
    /// contradiction between the two documents, recorded as errata. The `18` spelling wins here
    /// because `18` owns the DSL and is the only one of the two that writes the field.
    /// </remarks>
    public bool? Once { get; init; }

    /// <summary><see cref="TriggerKind.PERIODIC"/> — seconds between firings.</summary>
    public double? Interval { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.PERIODIC"/> — seconds of battle time before the first firing.
    /// `05` §3.1's built-in <c>SYS_ENRAGE</c> is <c>{interval: 1.0, startDelay: 70.0}</c>.
    /// </summary>
    public double? StartDelay { get; init; }

    /// <summary><see cref="TriggerKind.ON_PHASE_ENTER"/> — the boss phase, 1..3 (`05` §6.3).</summary>
    public int? Phase { get; init; }

    /// <summary><see cref="TriggerKind.ON_TILE_RESOLVED"/> — a <c>TILE_*</c> id (`03`, `19`).</summary>
    public string? TileType { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_ROLL"/> — one of `04` §1's six <c>DieFaceKind</c> names. A string
    /// for the reason <see cref="DieFaceSpec.Kind"/> is.
    /// </summary>
    public string? FaceKind { get; init; }

    /// <summary>
    /// <see cref="TriggerKind.ON_PERK_TAKEN"/> — a perk category (`06` §2).
    /// </summary>
    /// <remarks>
    /// ⚠️ A string, not an enum: `06` §2 names the six categories in prose (<em>Offense, Defense,
    /// Sustain, Dice &amp; Board, Economy, Trigger / Synergy</em>) and §5's worked JSON writes only
    /// one of them as a token, <c>"OFFENSE"</c>. Inventing the other five spellings —
    /// <c>DICE_BOARD</c>? <c>DICE_AND_BOARD</c>? <c>TRIGGER_SYNERGY</c>? — would be exactly the
    /// plausible-looking fabrication `16` R6 forbids. The schema constrains the shape and M3's perk
    /// schema closes the set when `06` fixes the tokens.
    /// </remarks>
    public string? Category { get; init; }
}
