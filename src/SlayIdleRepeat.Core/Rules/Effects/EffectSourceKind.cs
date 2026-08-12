namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 The <b>ten</b> sources `18` §8 step 1 collects from, in the order it names them.
/// </summary>
/// <remarks>
/// <para>
/// `18` §8 step 1, verbatim: <em>"Collect all active effects from: gear → affixes → set bonuses →
/// talents → pet auras → mount → run buffs → shrine buffs → curses → perks (in draft order)"</em>.
/// </para>
/// <para>
/// 🔒 <b>R5 — this list is the <em>collection</em> order, not the application order.</b> §8 closes
/// with <em>"effect-id order means the ascending lexicographic order of effect IDs, not draft
/// order"</em>, which governs steps 6, 7 and 8. The two are not in conflict: step 1 says <b>which</b>
/// effects to gather, and effect-id order says in <b>what order</b> to apply them. <c>PERKS</c>
/// carrying §8's <em>"(in draft order)"</em> is a statement about the perks a build holds, not about
/// arithmetic.
/// </para>
/// <para>
/// ⚠️ <b>The declaration order is load-bearing anyway</b>, for a reason §8 does not anticipate: it is
/// the first tiebreak of <see cref="EffectResolutionOrder"/>, which makes the resolution order
/// <b>total</b> when a build collects two effects sharing one id. See that type for the ruling. So
/// these members must stay in `18` §8 step 1's order, and
/// <c>EffectSourceCatalogueTests.The_ten_sources_are_18_8_step_1_in_its_own_order</c> pins them
/// against the sentence itself.
/// </para>
/// <para>
/// 🔒 Wire values, as <see cref="Content.Effects.EffectOp"/>: explicit, no <c>0</c> member, and
/// the ordinal is the §8 step 1 position. Nine of the ten have no data model yet — see
/// <see cref="EffectSourceCatalogue"/> for the milestone that lands each.
/// </para>
/// </remarks>
internal enum EffectSourceKind
{
    /// <summary>`08` §2's equipped gear. M4-03.</summary>
    GEAR = 1,

    /// <summary>`08` §3's 14 gear affixes. M4-03.</summary>
    AFFIXES = 2,

    /// <summary>`08` §3's 4 SS set-bonus engines. M4-03.</summary>
    SET_BONUSES = 3,

    /// <summary>`09`'s 60-node talent tree. M4-06.</summary>
    TALENTS = 4,

    /// <summary>
    /// `07` §2's pet auras — `18` §7.7's <c>aura</c> block. M4-07.
    /// </summary>
    /// <remarks>
    /// ⚠️ The <c>aura</c> block only. §7.7's sibling <c>active</c> block is an ability on the pet's
    /// own cooldown, not a build effect, and step 1 does not collect it. That distinction is what
    /// scopes <see cref="EffectDefaults"/>'s absent-trigger ruling — see its remarks.
    /// </remarks>
    PET_AURAS = 5,

    /// <summary>`07` §3's equipped mount. M4-08.</summary>
    MOUNT = 6,

    /// <summary>`03` §7's run buffs — shop consumables and run-scoped grants. M3-08.</summary>
    RUN_BUFFS = 7,

    /// <summary>`03` §7a's shrine buffs. M3-11.</summary>
    SHRINE_BUFFS = 8,

    /// <summary>`03` §7a / `19` E's 12-curse catalogue. M3-11.</summary>
    CURSES = 9,

    /// <summary>
    /// `06`'s 98 perks, <em>"(in draft order)"</em>. M3-07.
    /// </summary>
    /// <remarks>
    /// 🔒 Last in §8 step 1's sentence, and therefore last here. Under
    /// <see cref="EffectResolutionOrder"/> that means a perk loses the same-id tiebreak to every
    /// other source — which is only ever consulted when two sources contribute one authored id, and
    /// is stated so that the answer is a rule rather than an accident of enumeration.
    /// </remarks>
    PERKS = 10,
}
