namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 The twelve parameters `18` §3 gives its trigger kinds, as a set — the C# statement of the
/// partition <c>game-data/schema/effect.schema.json</c> states in JSON.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Content.Effects.EffectTrigger"/> is one record with twelve nullable parameters, and its
/// own remarks say why: the schema already partitions the 23 kinds into closed parameter shapes, so
/// <c>{"kind":"ON_HIT","cooldown":3}</c> fails validation before it is ever a C# value. That is true
/// of <b>authored</b> triggers. It is not true of a trigger built in code — the balance harness
/// (`05` §9), a test, M2-08's built-in <c>SYS_ENRAGE</c> — and those are exactly the callers whose
/// mistakes no schema sees. <see cref="TriggerCatalogue.Validate"/> restates the partition for them.
/// </para>
/// <para>
/// ⚠️ <b>Two statements of one fact, deliberately, with a test that compares them.</b>
/// <c>EffectSchemaTests</c> pins the schema against <c>TriggerKind</c>;
/// <c>TriggerCatalogueTests</c> pins this against <c>EffectTrigger</c>'s property list. A partition
/// that lived only in JSON could not guard a code-built trigger, and one that lived only here could
/// not fail a content build.
/// </para>
/// </remarks>
[Flags]
internal enum TriggerParameter
{
    /// <summary>The kind takes no parameters at all.</summary>
    NONE = 0,

    /// <summary>`18` §3 — <c>ON_BATTLE_END</c>.</summary>
    ONLY_IF_WON = 1 << 0,

    /// <summary>`18` §3 — <c>ON_ATTACK</c> and <c>ON_KILL</c>.</summary>
    EVERY_NTH = 1 << 1,

    /// <summary>
    /// `18` §3 — <c>ON_HIT</c>, <c>ON_CRIT</c>, <c>ON_HIT_TAKEN</c>, and — by ruling R11 —
    /// <c>ON_ATTACK</c>. See <see cref="TriggerCatalogue"/> for the erratum.
    /// </summary>
    CHANCE = 1 << 2,

    /// <summary>`18` §3 — <c>ON_HIT_TAKEN</c>, <c>ON_DODGE</c>, <c>ON_BLOCK</c>.</summary>
    COOLDOWN = 1 << 3,

    /// <summary>`18` §3 — <c>ON_LOW_HP</c>.</summary>
    THRESHOLD = 1 << 4,

    /// <summary>`18` §3 — <c>ON_LOW_HP</c>, <c>ON_LETHAL</c>. A boolean (R2).</summary>
    ONCE = 1 << 5,

    /// <summary>`18` §3 — <c>PERIODIC</c>.</summary>
    INTERVAL = 1 << 6,

    /// <summary>`18` §3 — <c>PERIODIC</c>.</summary>
    START_DELAY = 1 << 7,

    /// <summary>`18` §3 — <c>ON_PHASE_ENTER</c>.</summary>
    PHASE = 1 << 8,

    /// <summary>`18` §3 — <c>ON_TILE_RESOLVED</c>.</summary>
    TILE_TYPE = 1 << 9,

    /// <summary>`18` §3 — <c>ON_ROLL</c>.</summary>
    FACE_KIND = 1 << 10,

    /// <summary>`18` §3 — <c>ON_PERK_TAKEN</c>.</summary>
    CATEGORY = 1 << 11,
}
