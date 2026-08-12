using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 M2-13's authored boss data, run through the engine that consumes it: `17` §1.2's nine
/// coefficient rows, `17` §2-9's mechanics, and the eight authoring rules
/// <see cref="BossEncounterBuilder"/> refuses on.
/// </summary>
/// <remarks>
/// <para>
/// `17` §11: <em>"All 8 bosses expressed purely in the effect DSL — zero bespoke boss code."</em>
/// These cases are the half of that claim the content pipeline cannot make: the pipeline proves the
/// data is <b>valid JSON against the schemas</b>, and this proves the same data <b>builds into an
/// encounter</b> — which is a different failure surface, because A1-A5, T1-T3 and O1 are engine
/// rules that no schema states.
/// </para>
/// <para>
/// 🔒 <b>Every negative case mutates an <em>authored</em> script rather than a fixture.</b> That is
/// what makes it a statement about the shipped data: the mutation is one field, the rest of the
/// script is what is on disk, and the assertion names the marker (steering S2) rather than merely
/// observing that something threw.
/// </para>
/// </remarks>
public sealed class AuthoredBossScriptTests
{
    /// <summary>`17` §1.2 — the nine rows the table has, and the floor for every census below.</summary>
    private const int AuthoredScriptCount = 9;

    private const string Thornmaw = "BOSS_THORNMAW";
    private const string Gulgrot = "BOSS_GULGROT";
    private const string OssuaryKing = "BOSS_OSSUARY_KING";
    private const string Dicelord = "BOSS_DICELORD";
    private const string Ftue = "BOSS_FTUE";

    private static BossEncounter Build(AuthoredBossScripts.Authored authored) =>
        BossEncounterBuilder.Build(BossTestBench.Request(authored.Script, authored.Effects));

    // ─────────────────────────────────────────────────────── the data builds, and there is data

    /// <summary>
    /// 🔒 The headline: every authored script resolves and builds. A boss whose mechanics do not
    /// resolve is a fight with its mechanics silently deleted, which the balance harness reads as
    /// the boss being weak.
    /// </summary>
    /// <remarks>
    /// S3 — the subject set is floored at <see cref="AuthoredScriptCount"/>. Without it, a reader
    /// that returned an empty list would make this and every case below pass over nothing, which is
    /// exactly the vacuous pass a data-driven suite is prone to.
    /// </remarks>
    [Fact]
    public void Every_authored_script_builds_into_an_encounter()
    {
        var authored = AuthoredBossScripts.All;

        authored.Count.ShouldBe(
            AuthoredScriptCount, "17 §1.2's table has nine rows: the eight campaign bosses and BOSS_FTUE");

        foreach (var script in authored)
        {
            var encounter = Build(script);

            encounter.BossId.ShouldBe(script.Script.Id);
        }
    }

    /// <summary>🔒 `17` §1.2, transcribed. Every number here is one that section states.</summary>
    /// <remarks>
    /// S6 — this is the governing rule for the whole task, so the rows are pinned literally rather
    /// than derived from the file they are asserting about.
    /// </remarks>
    [Theory]
    [InlineData(Thornmaw, 1, 2.40, 0.80, 0.80, 0.70)]
    [InlineData(Gulgrot, 2, 2.50, 0.85, 0.70, 0.75)]
    [InlineData(OssuaryKing, 3, 2.10, 0.90, 1.00, 0.80)]
    [InlineData("BOSS_CINDERMAW", 4, 2.40, 1.05, 1.10, 0.75)]
    [InlineData("BOSS_RIMEHOLD", 5, 2.80, 0.95, 1.30, 0.60)]
    [InlineData("BOSS_COGITATOR_PRIME", 6, 2.20, 0.90, 0.85, 1.00)]
    [InlineData("BOSS_SPOREQUEEN_VELL", 7, 2.50, 0.90, 0.75, 0.90)]
    [InlineData(Dicelord, 8, 2.60, 1.00, 0.95, 0.90)]
    [InlineData(Ftue, null, 2.40, 0.80, 0.80, 0.70)]
    public void The_coefficient_rows_are_17_section_1_2s(
        string bossId, int? chapter, double hp, double atk, double def, double aspd)
    {
        var authored = AuthoredBossScripts.Of(bossId);

        authored.Script.Coefficients.Hp.ShouldBe(hp);
        authored.Script.Coefficients.Atk.ShouldBe(atk);
        authored.Script.Coefficients.Def.ShouldBe(def);
        authored.Script.Coefficients.Aspd.ShouldBe(aspd);
        authored.Chapter.ShouldBe(chapter, "17 §1.2's Ch column, which is empty for the FTUE row");
    }

    /// <summary>
    /// 🔒 `17` §1.2 — <em>"Fixed authored inputs: power = 900, Level = 1 📐 — it does not use
    /// EnemyPower(i)"</em>, and it is the only row that carries them.
    /// </summary>
    [Fact]
    public void Only_the_FTUE_row_carries_17_section_1_2s_fixed_authored_inputs()
    {
        var ftue = AuthoredBossScripts.Of(Ftue);

        ftue.FixedPower.ShouldBe(900.0);
        ftue.FixedLevel.ShouldBe(1);

        var campaign = AuthoredBossScripts.All
            .Where(a => !string.Equals(a.Script.Id, Ftue, StringComparison.Ordinal))
            .ToArray();

        campaign.Length.ShouldBe(AuthoredScriptCount - 1, "the eight campaign bosses of 17 §2-9");

        foreach (var boss in campaign)
        {
            boss.FixedPower.ShouldBeNull($"{boss.Script.Id} takes its power from its board node");
            boss.FixedLevel.ShouldBeNull($"{boss.Script.Id} takes the level its chapter and tier derive");
        }
    }

    /// <summary>🔒 `17` §1.2 — <em>"every boss uses the baseline"</em>, authored once.</summary>
    [Fact]
    public void The_secondary_stats_are_17_section_1_2s_baseline()
    {
        var (crit, critDamage, dodge, lifesteal) = AuthoredBossScripts.SecondaryStats;

        crit.ShouldBe(0.05);
        critDamage.ShouldBe(0.50);
        dodge.ShouldBe(0.0);
        lifesteal.ShouldBe(
            0.0, "17 §1.2: Gulgrot's 30% is a phase mechanic in the fight script, never a base stat");
    }

    /// <summary>
    /// 🔒 `17` §1.2's <em>"never a base stat"</em>, from the other side: the two secondaries the
    /// section names as phase mechanics are authored as effects on the scripts that own them.
    /// </summary>
    [Fact]
    public void The_two_secondaries_17_section_1_2_calls_phase_mechanics_are_authored_as_effects()
    {
        var lifesteal = AuthoredBossScripts.Of(Gulgrot).Effects["BOSS_GULGROT_P3_GORGE_LIFESTEAL"];

        lifesteal.Op.ShouldBe(EffectOp.STAT_ADD_PCT);
        lifesteal.Stat!.Value.Stat.ShouldBe(StatId.LIFESTEAL);
        lifesteal.Value.ShouldBe(0.30, "17 §3's phase 3: 'boss gains 30% Lifesteal'");
        lifesteal.Duration!.Scope.ShouldBe(DurationScope.PHASE, "R3 — every boss AURA is PHASE-scoped");

        AuthoredBossScripts.Of(Dicelord).Effects["BOSS_DICELORD_P3_LOADED_CRIT"]
            .Op.ShouldBe(EffectOp.FORCE_CRIT_NEXT);
    }

    // ─────────────────────────────────────────────────────── 17 §2-9, transcribed

    /// <summary>
    /// 🔒 `17` §2-9's authored magnitudes and periods, one row per mechanic. <b>This is the task's
    /// governing assertion</b> (steering S6): every number in the boss data has to trace to a `17`
    /// line, and until these rows existed a single-token typo in any of them shipped green — the
    /// other censuses here check an op, a cap, a scope or a sibling reference, and never the number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="value"/> is the effect's authored <c>value</c>; <paramref name="intervalSeconds"/>
    /// its <c>PERIODIC</c> period, or <c>null</c> where the mechanic is not periodic (a phase-entry
    /// aura, an on-hit rider, a reactive burst). Both are read off the shipped file, and the expected
    /// numbers are quoted from `17` in the third column.
    /// </para>
    /// <para>
    /// 🔒 The row count is floored against the data below, so a mechanic added to `bosses.json`
    /// without a row here fails rather than quietly escaping the transcription check.
    /// </para>
    /// </remarks>
    [Theory]
    // 17 §2 — Thornmaw
    [InlineData("BOSS_THORNMAW_P2_ROOT", -0.40, 8.0, "§2: 'PERIODIC 8s — Root: hero ASPD -40% for 3 s'")]
    [InlineData("BOSS_THORNMAW_P3_BLOOM", 2.0, null, "§2: 'summons 2 SWARM adds'")]
    [InlineData("BOSS_THORNMAW_P3_REGROWTH", 2.0, 12.0, "§2: 'PERIODIC 12s — summons 2 more'")]
    [InlineData("BOSS_THORNMAW_P3_RAGE", 0.30, null, "§2: 'boss gains RAGE +30% ATK'")]
    // 17 §3 — Gulgrot
    [InlineData("BOSS_GULGROT_P1_CROAK_POISON", 0.02, null, "§3: 'POISON (1 stack, 2% Max HP/s)'")]
    [InlineData("BOSS_GULGROT_P2_BOG_AIR", -0.35, null, "§3: 'Bog Air: hero HEAL% -35%'")]
    [InlineData("BOSS_GULGROT_P2_BELCH_FIRST", 0.02, 10.0, "§3: 'PERIODIC 10s — Belch: 2 POISON stacks'")]
    [InlineData("BOSS_GULGROT_P2_BELCH_SECOND", 0.02, 10.0, "§3: the second of Belch's two stacks")]
    [InlineData("BOSS_GULGROT_P3_GORGE_LIFESTEAL", 0.30, null, "§3: 'boss gains 30% Lifesteal'")]
    [InlineData("BOSS_GULGROT_P3_SPIT", 1.20, null, "§3: 'Spit: 120% ATK burst'")]
    [InlineData("BOSS_GULGROT_P3_SPIT_POISON", 0.02, null, "§3: 'and 1 POISON stack'")]
    // 17 §4 — Ossuary King
    [InlineData("BOSS_OSSUARY_KING_P1_COURT", 2.0, null, "§4: 'summons 2 GRUNT skeletons'")]
    [InlineData("BOSS_OSSUARY_KING_P1_RECALL", 2.0, 15.0, "§4: 'PERIODIC 15s — resummons any dead ones'")]
    [InlineData("BOSS_OSSUARY_KING_P2_OSSIFY_WARD", 0.20, 14.0, "§4: 'PERIODIC 14s — a WARD equal to 20% of its Max HP'")]
    [InlineData("BOSS_OSSUARY_KING_P2_OSSIFY_DR", 0.30, 14.0, "§4: 'and DR% +30% until the ward breaks or 6 s pass'")]
    [InlineData("BOSS_OSSUARY_KING_P3_RISE_AGAIN", 0.25, null, "§4: 'boss returns to 25% HP'")]
    [InlineData("BOSS_OSSUARY_KING_P3_RISEN_ATK", 0.40, null, "§4: 'ATK +40%'")]
    [InlineData("BOSS_OSSUARY_KING_P3_RISEN_ASPD", 0.25, null, "§4: 'ASPD +25%'")]
    // 17 §5 — Cindermaw
    [InlineData("BOSS_CINDERMAW_SMOULDER_BURN", 0.08, null, "§5: 'BURN (8% boss ATK/s, 3 s, stacks to 5)'")]
    [InlineData("BOSS_CINDERMAW_P2_MAGMA_VENT", 1.80, 9.0, "§5: 'PERIODIC 9s — Magma Vent: 180% ATK'")]
    [InlineData("BOSS_CINDERMAW_P2_ERUPTION_DR", 0.20, null, "§5: 'boss DR% +20%'")]
    [InlineData("BOSS_CINDERMAW_P3_MAGMA_VENT", 1.80, 7.0, "§5: 'PERIODIC 7s — Magma Vent continues'")]
    [InlineData("BOSS_CINDERMAW_P3_OVERHEAT_ATK", 0.60, null, "§5: 'Overheat: boss ATK +60%'")]
    [InlineData("BOSS_CINDERMAW_P3_OVERHEAT_DEF", -0.40, null, "§5: 'DEF -40%'")]
    // 17 §6 — Rimehold
    [InlineData("BOSS_RIMEHOLD_P1_CHILL", -0.25, 12.0, "§6: 'PERIODIC 12s — Chill: hero ASPD -25% for 4 s'")]
    [InlineData("BOSS_RIMEHOLD_P2_GLACIAL_ARMOUR", 0.60, null, "§6: 'boss DEF +60%'")]
    [InlineData("BOSS_RIMEHOLD_P2_SHATTERBACK", 0.25, null, "§6: 'Shatterback: reflects 25% of the hit'")]
    [InlineData("BOSS_RIMEHOLD_P2_CORE_EXPOSED", 1.60, null, "§6: 'hits deal x1.6 damage'")]
    [InlineData("BOSS_RIMEHOLD_P3_COLLAPSE", 2.20, 10.0, "§6: 'PERIODIC 10s — Collapse: 220% ATK'")]
    [InlineData("BOSS_RIMEHOLD_P3_ICE_SHARDS", 2.0, 10.0, "§6: 'and 2 SWARM ice shards spawn'")]
    [InlineData("BOSS_RIMEHOLD_P3_AVALANCHE_ASPD", 0.40, null, "§6: 'boss ASPD +40%'")]
    // 17 §7 — Cogitator Prime
    [InlineData("BOSS_COGITATOR_PRIME_P1_ESCALATION_ATK", 1.02, 5.0, "§7: '+2% ATK ... every 5 s' (R1: the value IS the multiplier)")]
    [InlineData("BOSS_COGITATOR_PRIME_P1_ESCALATION_ASPD", 1.02, 5.0, "§7: 'and +2% ASPD every 5 s'")]
    [InlineData("BOSS_COGITATOR_PRIME_P2_ESCALATION_ATK", 1.02, 5.0, "§7: Escalation 'never resets for the whole fight'")]
    [InlineData("BOSS_COGITATOR_PRIME_P2_ESCALATION_ASPD", 1.02, 5.0, "§7: the same, for ASPD")]
    [InlineData("BOSS_COGITATOR_PRIME_P2_COUNTERMEASURES", 2.0, null, "§7: 'summons 2 WARDEN drones'")]
    [InlineData("BOSS_COGITATOR_PRIME_P2_RECALIBRATE", 0.50, 16.0, "§7: 'PERIODIC 16s — ... gains half of it for 10 s'")]
    [InlineData("BOSS_COGITATOR_PRIME_P3_OVERCLOCK_ATK", 1.04, 5.0, "§7: 'Escalation rate doubles to +4% per 5 s'")]
    [InlineData("BOSS_COGITATOR_PRIME_P3_OVERCLOCK_ASPD", 1.04, 5.0, "§7: the same, for ASPD")]
    [InlineData("BOSS_COGITATOR_PRIME_P3_PISTON_SLAM", 2.00, 8.0, "§7: 'PERIODIC 8s — Piston Slam: 200% ATK'")]
    // 17 §8 — Sporequeen Vell
    [InlineData("BOSS_SPOREQUEEN_VELL_P1_POLLINATION", -0.12, 6.0, "§8: 'PERIODIC 6s — 1 SPORE stack (hero HEAL% -12% each)'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P2_BLOOM_COURT", 2.0, null, "§8: 'summons 2 CASTER sporelings'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P2_BURST_CAP", 1.50, 12.0, "§8: 'PERIODIC 12s — Burst Cap: 150% ATK AoE'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P2_BURST_CAP_POISON_FIRST", 0.02, 12.0, "§8: 'applies POISON x2'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P2_BURST_CAP_POISON_SECOND", 0.02, 12.0, "§8: the second of the two")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P3_ROT", 0.015, 1.0, "§8: 'hero takes 1.5% Max HP true damage per second'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P3_REGROW", 1.0, 10.0, "§8: 'PERIODIC 10s — resummons 1 sporeling'")]
    // 17 §9 — the Dicelord
    [InlineData("BOSS_DICELORD_P1_FATE_BOSS_ATK", 0.25, null, "§9: '1-2: boss gains ATK +25% for 8 s'")]
    [InlineData("BOSS_DICELORD_P1_FATE_HERO_ATK", 0.25, null, "§9: '3-4: hero gains ATK +25% for 8 s'")]
    [InlineData("BOSS_DICELORD_P1_FATE_BOTH_ASPD", 0.30, null, "§9: '5-6: both gain ASPD +30% for 8 s'")]
    [InlineData("BOSS_DICELORD_P2_FATE_BOSS_ATK", 0.25, null, "§9: the phase-2 table's boss buff")]
    [InlineData("BOSS_DICELORD_P2_FATE_BOTH_ASPD", 0.30, null, "§9: the phase-2 table's both-buff")]
    [InlineData("BOSS_DICELORD_P3_HOUSE_WARD", 0.25, null, "§9: 'a WARD equal to 25% Max HP'")]
    [InlineData("BOSS_DICELORD_P3_HOUSE_THORNS", 0.30, null, "§9: 'and Thorns 30%'")]
    [InlineData("BOSS_DICELORD_P3_ALL_IN", 3.00, 8.0, "§9: 'PERIODIC 8s — All In: 300% ATK single hit'")]
    [InlineData("BOSS_DICELORD_P3_LOADED_CRIT_MULT", 2.00, null, "R21: 05 §4's crit step already pays x1.5")]
    public void The_authored_magnitudes_and_periods_are_17_section_2_to_9s(
        string effectId, double value, double? intervalSeconds, string quotation)
    {
        var effect = AuthoredBossScripts.All
            .Select(a => a.Effects.TryGetValue(effectId, out var found) ? found : null)
            .FirstOrDefault(e => e is not null)
            ?? throw new InvalidOperationException(
                $"no authored script declares '{effectId}'. A row here naming an effect the data does " +
                "not carry is a transcription check over nothing.");

        effect.Value.ShouldBe(value, $"17 {quotation}");

        if (intervalSeconds is { } period)
        {
            effect.Trigger!.Kind.ShouldBe(TriggerKind.PERIODIC, $"17 {quotation}");
            effect.Trigger.Interval.ShouldBe(period, $"17 {quotation}");
        }
        else
        {
            (effect.Trigger?.Kind).ShouldNotBe(
                TriggerKind.PERIODIC,
                $"{effectId} is authored with a period and 17 {quotation} states none");
        }
    }

    /// <summary>
    /// 🔒 S3 — the transcription theory above covers <b>every</b> authored effect that carries a
    /// value, so a mechanic added to <c>bosses.json</c> cannot escape it by simply not having a row.
    /// </summary>
    /// <remarks>
    /// Without this, the theory is a list somebody has to remember to extend — and the failure mode
    /// of a forgotten row is silence, which is the one this suite is written against.
    /// </remarks>
    [Fact]
    public void Every_authored_effect_carrying_a_value_has_a_transcription_row()
    {
        var carryingAValue = AuthoredBossScripts.All
            .SelectMany(a => a.Effects.Values)
            .Where(e => e.Value is not null)
            .Select(e => e.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var covered = typeof(AuthoredBossScriptTests)
            .GetMethod(nameof(The_authored_magnitudes_and_periods_are_17_section_2_to_9s))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(d => (string)d.GetData(null!).First()[0]!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        covered.Length.ShouldBeGreaterThanOrEqualTo(
            50, "S3 — the floor under the reflection above; the theory really does carry its rows");

        carryingAValue.ShouldBe(
            covered,
            "every authored magnitude traces to a 17 line, and the transcription theory is where that " +
            "is written down. An effect here and not there is a number nobody checked against 17");
    }

    // ─────────────────────────────────────────────────────── the adds power fraction (the M2-12 gap)

    /// <summary>
    /// 🔒 `17` §1 — the adds fraction is <b>per boss</b>, present on exactly the scripts that summon,
    /// and inside the 25-35% band.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Both directions, because either alone is satisfiable by the wrong data.</b> "In band on
    /// every script that has one" passes on a file where nobody authored one at all; "present on the
    /// summoners" passes on a file that authors 0.60. The pairing with the SUMMON census is what
    /// makes it a statement about this data.
    /// </remarks>
    [Fact]
    public void The_adds_power_fraction_is_per_boss_and_present_exactly_where_a_script_summons()
    {
        var summoning = new List<string>();

        foreach (var authored in AuthoredBossScripts.All)
        {
            var summons = authored.Effects.Values.Any(e => e.Op == EffectOp.SUMMON);
            var fraction = authored.Script.AddsPowerFraction;

            if (summons)
            {
                summoning.Add(authored.Script.Id);

                fraction.ShouldNotBeNull($"{authored.Script.Id} summons, so 17 §1 gives its adds a power");
                fraction.Value.ShouldBeGreaterThanOrEqualTo(BossAdds.MinPowerFraction);
                fraction.Value.ShouldBeLessThanOrEqualTo(BossAdds.MaxPowerFraction);
            }
            else
            {
                fraction.ShouldBeNull(
                    $"{authored.Script.Id} authors no SUMMON, and an adds fraction on a boss with no " +
                    "adds is data nothing reads");
            }
        }

        summoning.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(
            new List<string>
            {
                "BOSS_COGITATOR_PRIME", "BOSS_OSSUARY_KING", "BOSS_RIMEHOLD",
                "BOSS_SPOREQUEEN_VELL", Thornmaw,
            }.OrderBy(id => id, StringComparer.Ordinal),
            "17 §2, §4, §6, §7 and §8 are the five fights that summon; §3, §5 and §9 do not");
    }

    /// <summary>
    /// 🔒 The evidence that decided per-boss over per-mechanic: no boss summons two archetypes, so
    /// no fight ever needs two fractions.
    /// </summary>
    [Fact]
    public void No_script_summons_more_than_one_archetype()
    {
        var summoners = 0;

        foreach (var authored in AuthoredBossScripts.All)
        {
            var archetypes = authored.Effects.Values
                .Where(e => e.Op == EffectOp.SUMMON)
                .Select(e => e.Archetype!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (archetypes.Length == 0)
            {
                continue;
            }

            summoners++;
            archetypes.Length.ShouldBe(
                1,
                $"{authored.Script.Id} summons {string.Join(", ", archetypes)}. 17 gives each " +
                "summoning boss ONE archetype, which is why one fraction per boss is enough");
        }

        summoners.ShouldBe(5, "17 §2, §4, §6, §7 and §8");
    }

    /// <summary>
    /// 🔒 <b>R3</b> — every effect a phase <em>entry</em> grants for a duration is <c>PHASE</c>-scoped,
    /// and nothing anywhere uses the retired 999-second idiom.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>The subject set is a structural proxy for `17` §1.1's <c>AURA</c>, not that list.</b> An
    /// <c>ON_PHASE_ENTER</c> effect carrying a duration is what R3 is decidable over; `17` calls
    /// thirteen things an <c>AURA</c>, and two of them — Cogitator's Escalation and Sporequeen's Rot
    /// — are authored as <c>PERIODIC</c> and fall outside this census <b>by construction</b>. That is
    /// deliberate and the reason is in each script's own <c>_doc</c>: Escalation must survive every
    /// transition (`17` §7: <em>"never resets for the whole fight"</em>), which is a <c>BATTLE</c>
    /// scope, and Rot is a per-second drain rather than a passive.
    /// </para>
    /// <para>
    /// 🔴 The second assertion is the one with teeth on `18` §7.8's erratum: <em>"the
    /// <c>{"seconds": 999}</c> idiom is not to be used anywhere"</em>. It is stated over EVERY
    /// authored duration, not only the auras, because the idiom is reachable from any of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_phase_entry_effect_with_a_duration_is_PHASE_scoped()
    {
        var auras = 0;
        var durations = 0;

        foreach (var authored in AuthoredBossScripts.All)
        {
            foreach (var effect in authored.Effects.Values)
            {
                if (effect.Duration is { } duration)
                {
                    durations++;
                    duration.Seconds.ShouldNotBe(
                        999.0, $"{effect.Id} — 18 §7.8's erratum retires the 999-second idiom");
                }

                if (effect.Trigger is not { Kind: TriggerKind.ON_PHASE_ENTER } ||
                    effect.Duration is null)
                {
                    continue;
                }

                auras++;
                effect.Duration.Scope.ShouldBe(
                    DurationScope.PHASE,
                    $"{effect.Id} — R3. PHASE-scoped statuses were degrading to BATTLE until M2-12, so " +
                    "every boss AURA did nothing at all; it is fixed, and the data authors PHASE");
            }
        }

        auras.ShouldBe(
            10,
            "17 §2's RAGE, §3's Bog Air and Gorge, §5's Eruption DR and Overheat's two halves, " +
            "§6's Glacial Armour and Core, §9's Thorns, and §6's Avalanche");

        durations.ShouldBeGreaterThanOrEqualTo(
            auras, "S3 — the 999-second sweep really did walk more than the auras");
    }

    // ─────────────────────────────────────────────────────── the eight refusals, against this data

    /// <summary>🔒 <b>A1</b> — `17` §1's <em>"exactly 3"</em>, numbered 1, 2, 3 in order.</summary>
    /// <remarks>
    /// 🔴 <b>Two shapes.</b> Dropping a block and re-ordering two are different mistakes, and a rule
    /// that only counted would pass the second. The negative control is the unmutated script, which
    /// the first case above builds.
    /// </remarks>
    [Theory]
    [InlineData(true, "a block is missing")]
    [InlineData(false, "two blocks are out of order")]
    public void A1_refuses_a_script_whose_phase_blocks_are_not_1_2_3_in_order(bool drop, string why)
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);
        var blocks = authored.Script.Phases.ToList();

        var mutated = drop
            ? new List<BossPhaseBlock> { blocks[0], blocks[1] }
            : new List<BossPhaseBlock> { blocks[0], blocks[2], blocks[1] };

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(authored.Script with { Phases = mutated }, authored.Effects)));

        thrown.Message.ShouldContain("A1", Case.Sensitive, why);
        thrown.Message.ShouldContain(Thornmaw, Case.Sensitive, "which boss");
    }

    /// <summary>🔒 <b>A2</b> — a mechanic naming an effect the script does not declare.</summary>
    /// <remarks>
    /// 🔴 <b>Two shapes: a typo, and an id that is real content of ANOTHER boss.</b> The second is
    /// the case a global effect registry would have accepted, and it is the one that proves the
    /// scope is the script rather than the repository.
    /// </remarks>
    [Theory]
    [InlineData("BOSS_THORNMAW_P2_TYPO", "an id nothing declares")]
    [InlineData("BOSS_GULGROT_P2_BOG_AIR", "a real effect id of a DIFFERENT boss's script")]
    public void A2_refuses_a_mechanic_that_is_not_a_sibling(string outsider, string why)
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(WithPhase2(authored, new BossMechanic(outsider)), authored.Effects)));

        thrown.Message.ShouldContain("A2", Case.Sensitive, why);
        thrown.Message.ShouldContain(outsider, Case.Sensitive, "which mechanic");
    }

    /// <summary>
    /// 🔒 <b>A3</b> — no script may author one of the three engine built-ins, and none does.
    /// </summary>
    /// <remarks>
    /// The census is the positive half and the theory is the negative half. Both matter: the census
    /// alone passes on a file with no effects at all, and the refusal alone says nothing about the
    /// shipped data.
    /// </remarks>
    [Theory]
    [InlineData(BossBuiltIns.EnrageId)]
    [InlineData(BossBuiltIns.Phase3StunImmunityId)]
    [InlineData(BossBuiltIns.Phase3FreezeImmunityId)]
    public void A3_refuses_a_script_that_authors_a_built_in(string builtIn)
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(WithPhase2(authored, new BossMechanic(builtIn)), authored.Effects)));

        thrown.Message.ShouldContain("A3", Case.Sensitive, "the built-in marker");
        thrown.Message.ShouldContain(builtIn, Case.Sensitive, "which built-in");
    }

    /// <summary>🔒 <b>A3</b>, the positive half: no authored script names a built-in anywhere.</summary>
    [Fact]
    public void No_authored_script_names_an_engine_built_in()
    {
        var builtIns = BossBuiltIns.All.Select(b => b.Id).ToArray();

        builtIns.Length.ShouldBe(3, "SYS_ENRAGE and the two phase-3 immunities");

        var effects = 0;

        foreach (var authored in AuthoredBossScripts.All)
        {
            foreach (var id in authored.Effects.Keys)
            {
                effects++;
                builtIns.ShouldNotContain(
                    id, $"{authored.Script.Id} authors '{id}', which the encounter builder attaches");
            }
        }

        effects.ShouldBeGreaterThanOrEqualTo(
            50, "S3 — the eight fights of 17 §2-9 author roughly fifty mechanics between them");
    }

    /// <summary>
    /// 🔒 <b>A4</b> — `05` §3.1 sweeps <c>ON_BATTLE_START</c> at pre-tick 0b and enters phase 1 at
    /// 0c, so one in a later block lands while the boss is still in phase 1.
    /// </summary>
    [Fact]
    public void A4_refuses_an_ON_BATTLE_START_outside_phase_1()
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);
        var opener = authored.Effects["BOSS_THORNMAW_P2_ROOT"] with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        };

        var effects = new Dictionary<string, EffectDefinition>(authored.Effects, StringComparer.Ordinal)
        {
            [opener.Id] = opener,
        };

        // A4 is the FIRST of the four per-mechanic checks the builder runs, so no other rule can
        // pre-empt it — which is why the probe needs nothing else to be true of the mechanic.
        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(WithPhase2(authored, new BossMechanic(opener.Id)), effects)));

        thrown.Message.ShouldContain("A4", Case.Sensitive, "the opener marker");
        thrown.Message.ShouldContain(opener.Id, Case.Sensitive, "which mechanic");
    }

    /// <summary>
    /// 🔒 <b>A5</b> — `17` §1 caps a boss's adds at 3 alive, and an <em>absent</em> cap is refused
    /// too, because an absent one means no cap at all.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Two shapes, and the absent one is the load-bearing case.</b> A rule that only compared
    /// numbers would pass a boss that simply omitted the key and then spawned adds without a ceiling.
    /// </remarks>
    [Theory]
    [InlineData(null, "no cap at all")]
    [InlineData(4, "a fourth add, which is a fight nobody tuned")]
    public void A5_refuses_a_summon_whose_maxAlive_is_absent_or_above_three(int? maxAlive, string why)
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);
        var uncapped = authored.Effects["BOSS_THORNMAW_P3_BLOOM"] with { MaxAlive = maxAlive };

        var effects = new Dictionary<string, EffectDefinition>(authored.Effects, StringComparer.Ordinal)
        {
            [uncapped.Id] = uncapped,
        };

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(authored.Script, effects)));

        thrown.Message.ShouldContain("A5", Case.Sensitive, why);

        // S2 — WHICH summon. The script is handed in unmutated apart from this one effect, and
        // Thornmaw authors two summons, so a refusal naming only the rule could be the other one's.
        thrown.Message.ShouldContain("BOSS_THORNMAW_P3_BLOOM", Case.Sensitive, "which mechanic");
    }

    /// <summary>
    /// 🔒 <b>A5</b>, the positive half: every authored summon caps at the standing add count its own
    /// `17` section states, and never above `17` §1's ceiling of three.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Not "3 everywhere".</b> `17` §1's <em>"capped at 3 alive at once"</em> is a ceiling the
    /// builder enforces, and a summon authored at 3 tops the field up to 3 whatever its own fight
    /// says — so `17` §4's <em>"summons 2 skeletons … resummons any dead ones"</em> would quietly grow
    /// a third. `17` §2 writes <em>"(max 3 alive)"</em> in so many words and `17` §6 states no standing
    /// count at all; those two are the only 3s.
    /// </remarks>
    [Theory]
    [InlineData("BOSS_THORNMAW_P3_BLOOM", 3, "§2: '(max 3 alive)', stated")]
    [InlineData("BOSS_THORNMAW_P3_REGROWTH", 3, "§2: the same cap, on the mechanic that tops it up")]
    [InlineData("BOSS_OSSUARY_KING_P1_COURT", 2, "§4: 'summons 2 GRUNT skeletons'")]
    [InlineData("BOSS_OSSUARY_KING_P1_RECALL", 2, "§4: 'resummons any dead ones' — a top-up to two")]
    [InlineData("BOSS_RIMEHOLD_P3_ICE_SHARDS", 3, "§6: '2 SWARM ice shards spawn', no standing count")]
    [InlineData("BOSS_COGITATOR_PRIME_P2_COUNTERMEASURES", 2, "§7: 'summons 2 WARDEN drones'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P2_BLOOM_COURT", 2, "§8: 'summons 2 CASTER sporelings'")]
    [InlineData("BOSS_SPOREQUEEN_VELL_P3_REGROW", 2, "§8: 'resummons 1 sporeling' — back to two")]
    public void Every_authored_summon_caps_at_its_own_sections_standing_add_count(
        string effectId, int maxAlive, string quotation)
    {
        var effect = AuthoredBossScripts.All
            .Select(a => a.Effects.TryGetValue(effectId, out var found) ? found : null)
            .FirstOrDefault(e => e is not null)
            ?? throw new InvalidOperationException($"no authored script declares '{effectId}'");

        effect.Op.ShouldBe(EffectOp.SUMMON);
        effect.MaxAlive.ShouldBe(maxAlive, $"17 {quotation}");
        effect.MaxAlive!.Value.ShouldBeLessThanOrEqualTo(
            BossAdds.MaxAlive, "17 §1's ceiling, which A5 refuses anything above");
    }

    /// <summary>🔒 S3 — the theory above covers every authored summon, not a subset of them.</summary>
    [Fact]
    public void Every_authored_summon_has_a_cap_row()
    {
        AuthoredBossScripts.All
            .SelectMany(a => a.Effects.Values)
            .Count(e => e.Op == EffectOp.SUMMON)
            .ShouldBe(8, "17 §2, §4, §6, §7 and §8 author eight summons between them");
    }

    /// <summary>
    /// 🔒 <b>T1</b> — `17` §1's 1.0-1.5 s band, and `05` §3's fixed tick: a legal wind-up is a whole
    /// number of ticks inside the band.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Two shapes, because the band and the tick grid are different constraints.</b> 0.8 s is
    /// a whole 16 ticks and outside the band; 1.23 s is inside the band and 24.6 ticks, which points
    /// at neither of two ticks.
    /// </remarks>
    [Theory]
    [InlineData(0.8, "a whole number of ticks, but outside 17 §1's band")]
    [InlineData(1.23, "inside the band, but between two ticks")]
    public void T1_refuses_a_wind_up_outside_the_band_or_off_the_tick_grid(double lead, string why)
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(
                WithPhase2(authored, new BossMechanic("BOSS_THORNMAW_P2_ROOT", lead)), authored.Effects)));

        thrown.Message.ShouldContain("T1", Case.Sensitive, why);
    }

    /// <summary>
    /// 🔒 <b>T2</b> — the period must exceed the wind-up, or firing k+1 is announced before firing k
    /// lands and two wind-ups become indistinguishable in a log that IS the replay.
    /// </summary>
    [Fact]
    public void T2_refuses_a_wind_up_at_least_as_long_as_the_period()
    {
        var authored = AuthoredBossScripts.Of(Thornmaw);
        var rapid = authored.Effects["BOSS_THORNMAW_P2_ROOT"] with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
        };

        var effects = new Dictionary<string, EffectDefinition>(authored.Effects, StringComparer.Ordinal)
        {
            [rapid.Id] = rapid,
        };

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(
                WithPhase2(authored, new BossMechanic(rapid.Id, 1.5)), effects)));

        thrown.Message.ShouldContain("T2", Case.Sensitive, "the period-versus-lead marker");
    }

    /// <summary>
    /// 🔒 <b>T3</b> — a periodic damaging mechanic whose period leaves room for a wind-up must carry
    /// one. `17` §1: <em>"they must be able to read what is happening, or the fight feels
    /// arbitrary."</em>
    /// </summary>
    [Fact]
    public void T3_refuses_a_damaging_periodic_that_authors_no_wind_up()
    {
        var authored = AuthoredBossScripts.Of(Dicelord);
        var blocks = authored.Script.Phases.ToList();

        // 17 §9's All In, stripped of the 1.5 s 17 §9 authors for it.
        var stripped = blocks[2].Mechanics
            .Select(m => string.Equals(m.EffectId, "BOSS_DICELORD_P3_ALL_IN", StringComparison.Ordinal)
                ? new BossMechanic(m.EffectId)
                : m)
            .ToList();

        blocks[2] = new BossPhaseBlock { Phase = 3, Mechanics = stripped };

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(authored.Script with { Phases = blocks }, authored.Effects)));

        thrown.Message.ShouldContain("T3", Case.Sensitive, "the missing-wind-up marker");
        thrown.Message.ShouldContain("BOSS_DICELORD_P3_ALL_IN", Case.Sensitive, "which mechanic");
    }

    /// <summary>
    /// 🔒 <b>T1/T3</b>, the positive half: every authored wind-up is inside `17` §1's band and a whole
    /// number of ticks, and `17`'s four stated leads are transcribed.
    /// </summary>
    [Fact]
    public void Every_authored_wind_up_is_in_17_section_1s_band_and_a_whole_number_of_ticks()
    {
        var leads = 0;

        foreach (var authored in AuthoredBossScripts.All)
        {
            foreach (var (phase, effectId, lead) in authored.TelegraphSeconds)
            {
                leads++;

                var where = $"{authored.Script.Id} phase {phase}, {effectId}";

                lead.ShouldBeGreaterThanOrEqualTo(BossTelegraphs.MinLeadSeconds, where);
                lead.ShouldBeLessThanOrEqualTo(BossTelegraphs.MaxLeadSeconds, where);

                var ticks = BossTelegraphs.ExactLeadTicks(lead);
                ticks.ShouldBe(Math.Floor(ticks), $"{where} — 05 §3's simulation is fixed-tick");
            }
        }

        // 🔴 EXACTLY eight, not "at least". A floor of seven passes on data with one wind-up deleted,
        // and "17 §1 requires a wind-up on every damaging mechanic" is exactly what that would break.
        leads.ShouldBe(
            8,
            "17 §2 (Root), §3 (Belch), §5 (Magma Vent, twice — phases 2 and 3), §6 (Collapse), " +
            "§7 (Piston Slam), §8 (Burst Cap) and §9 (All In)");

        // 17's own four stated leads, transcribed rather than inferred.
        Lead(Thornmaw, 2, "BOSS_THORNMAW_P2_ROOT")
            .ShouldBe(1.2, "17 §2: 'vines coil around the hero's feet 1.2 s before'");
        Lead(Gulgrot, 2, "BOSS_GULGROT_P2_BELCH_FIRST")
            .ShouldBe(1.2, "17 §3: 'a green cloud swells around the boss for 1.2 s'");
        Lead("BOSS_CINDERMAW", 2, "BOSS_CINDERMAW_P2_MAGMA_VENT")
            .ShouldBe(1.3, "17 §5: 'the floor under the hero glows orange for 1.3 s'");
        Lead(Dicelord, 3, "BOSS_DICELORD_P3_ALL_IN")
            .ShouldBe(1.5, "17 §9: '300% ATK single hit, telegraphed 1.5 s'");
    }

    /// <summary>🔒 <b>O1</b> — every <c>RANDOM_OUTCOME</c> row names a sibling of its own script.</summary>
    /// <remarks>
    /// 🔴 <b>Two shapes, on <c>BossEncounterBuilderTests</c>' precedent: an id nothing declares, and
    /// a real effect id belonging to another boss</b> — the second being what a registry would have
    /// accepted.
    /// </remarks>
    [Theory]
    [InlineData("BOSS_DICELORD_P1_FATE_TYPO", "an id nothing declares")]
    [InlineData("BOSS_THORNMAW_P3_RAGE", "a real effect id of a DIFFERENT boss's script")]
    public void O1_refuses_a_RANDOM_OUTCOME_row_naming_a_non_sibling(string outsider, string why)
    {
        var authored = AuthoredBossScripts.Of(Dicelord);
        var roll = authored.Effects["BOSS_DICELORD_P1_ROLL_OF_FATE"];

        var rows = roll.Outcomes!.ToList();
        rows[0] = new RandomOutcomeEntry(outsider, rows[0].Weight);

        var effects = new Dictionary<string, EffectDefinition>(authored.Effects, StringComparer.Ordinal)
        {
            [roll.Id] = roll with { Outcomes = rows },
        };

        var thrown = Should.Throw<EffectContextException>(() => BossEncounterBuilder.Build(
            BossTestBench.Request(authored.Script, effects)));

        thrown.Message.ShouldContain("O1", Case.Sensitive, why);
        thrown.Message.ShouldContain(outsider, Case.Sensitive, "which row");
    }

    // ─────────────────────────────────────────────────────── R21, and the Dicelord's tables

    /// <summary>
    /// 🔒 <b>R21</b> — `17` §9's <em>"every 6th attack the boss makes is an automatic critical hit
    /// for ×3"</em>, and why the authored multiplier is 2.0 and not 3.0.
    /// </summary>
    /// <remarks>
    /// `05` §4 step 4 applies <c>dmg *= (1 + CDMG)</c> and `17` §1.2 fixes a boss's CDMG at 0.50, so
    /// a forced crit is already ×1.5. Step 2 applies the attack multiplier to <c>raw</c>. The product
    /// is what `17` §9 asks for, and it is pinned <b>numerically from the authored values</b> rather
    /// than restated as a literal — a <c>const</c> would be folded by the compiler and would pin
    /// nothing about this data.
    /// </remarks>
    [Fact]
    public void R21_the_every_sixth_attack_lands_at_exactly_three_times_damage()
    {
        var dicelord = AuthoredBossScripts.Of(Dicelord);
        var forced = dicelord.Effects["BOSS_DICELORD_P3_LOADED_CRIT"];
        var multiplier = dicelord.Effects["BOSS_DICELORD_P3_LOADED_CRIT_MULT"];

        forced.Op.ShouldBe(EffectOp.FORCE_CRIT_NEXT);
        forced.Charges.ShouldBe(1, "R6's charges key, added by M2-03 for exactly this");
        forced.Value.ShouldBeNull("FORCE_CRIT_NEXT carries no value: the crit is paid by the actor's CDMG");
        forced.Trigger!.Kind.ShouldBe(TriggerKind.ON_ATTACK);
        forced.Trigger.EveryNth.ShouldBe(6, "17 §9: 'every 6th attack'");

        multiplier.Op.ShouldBe(EffectOp.ATTACK_MULT_NEXT);
        multiplier.Charges.ShouldBe(1);
        multiplier.Trigger!.EveryNth.ShouldBe(6, "the two halves fire on the same attack");

        var critDamage = AuthoredBossScripts.SecondaryStats.CritDamage;
        var landed = (1.0 + critDamage) * multiplier.Value!.Value;

        landed.ShouldBe(3.0, "17 §9's ×3: 05 §4 step 4's 1.5 from CDMG times the authored 2.0");
    }

    /// <summary>
    /// 🔒 `17` §9 and the M2 kickoff — one visible d6 per phase, and phase 2's table is the ruled
    /// one: <em>1-4 boss buff, 5-6 both buff</em>, the hero-favourable outcome removed.
    /// </summary>
    [Fact]
    public void The_Roll_of_Fate_tables_are_a_d6_in_both_phases()
    {
        var dicelord = AuthoredBossScripts.Of(Dicelord);

        var phase1 = dicelord.Effects["BOSS_DICELORD_P1_ROLL_OF_FATE"].Outcomes!;
        var phase2 = dicelord.Effects["BOSS_DICELORD_P2_LOADED_DICE"].Outcomes!;

        phase1.Count.ShouldBe(3, "17 §9 phase 1: 1-2, 3-4, 5-6");
        phase1.Select(r => r.Weight).ShouldBe(new List<double> { 2, 2, 2 });
        phase1.Sum(r => r.Weight).ShouldBe(6.0, "one d6");

        phase2.Count.ShouldBe(2, "the kickoff ruling removes the hero-favourable outcome");
        phase2.Select(r => r.Weight).ShouldBe(new List<double> { 4, 2 }, "1-4 boss buff, 5-6 both buff");
        phase2.Sum(r => r.Weight).ShouldBe(6.0, "still one d6");

        // Every row is a sibling — the same claim O1 refuses the negation of, asserted positively.
        foreach (var row in phase1.Concat(phase2))
        {
            dicelord.Effects.Keys.ShouldContain(
                row.EffectId, "18 §10.1 E6 — every outcome row names a sibling");
        }
    }

    // ─────────────────────────────────────────────────────── helpers

    /// <summary>
    /// The script with phase 2's mechanics replaced by one. Phase 2 rather than phase 1 because A4
    /// is stated over the later blocks, so one helper serves every negative case here.
    /// </summary>
    /// <summary>One authored wind-up, named by the mechanic it sits on rather than by its effect.</summary>
    private static double Lead(string bossId, int phase, string effectId) =>
        AuthoredBossScripts.Of(bossId).TelegraphSeconds
            .Single(t => t.Phase == phase && string.Equals(t.EffectId, effectId, StringComparison.Ordinal))
            .Lead;

    private static BossScript WithPhase2(AuthoredBossScripts.Authored authored, BossMechanic mechanic)
    {
        var blocks = authored.Script.Phases.ToList();

        blocks[1] = new BossPhaseBlock
        {
            Phase = 2,
            Mechanics = new List<BossMechanic> { mechanic },
        };

        return authored.Script with { Phases = blocks };
    }
}
