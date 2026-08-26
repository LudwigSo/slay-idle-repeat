using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Analytics;

/// <summary>
/// The vocabulary, name by name: every emitted analytics event is proven from a real accepted
/// command's batch with discriminating values, and what the vocabulary does not name is proven
/// silent.
/// </summary>
public sealed class AnalyticsTranslatorTests
{
    [Fact]
    public void Translate_maps_an_accepted_start_run_to_run_start()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var batch = AnalyticsWorlds.Sent(
            game, player, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL));

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.RunStart)
            .ShouldHaveSingleItem("one accepted START_RUN is exactly one run_start.");

        emitted.Properties["run_id"].ShouldBe(
            batch.State.Run!.Id.Value,
            "the run the command opened, from the committed state — the only place the id exists.");
        emitted.Properties["chapter"].ShouldBe("1", "the chapter the command named.");
        emitted.Properties["tier"].ShouldBe("NORMAL", "the tier the command named.");
    }

    [Fact]
    public void Translate_maps_an_accepted_end_run_to_run_end()
    {
        var batch = AnalyticsWorlds.VictoryEndRun();

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.RunEnd)
            .ShouldHaveSingleItem("one accepted END_RUN is exactly one run_end.");

        emitted.Properties["run_id"].ShouldBe(batch.State.Run!.Id.Value, "which run ended.");
        emitted.Properties["victory"].ShouldBe(
            "true",
            "the committed state carries BossDefeated, and this run's Boss is dead. Anything richer " +
            "than what Command and State honestly carry would be invented.");
    }

    [Fact]
    public void Translate_maps_a_death_end_run_to_run_end_with_victory_false()
    {
        // The discriminating counterpart to the victory case: a translator that hard-codes
        // victory="true" passes that one and fails here.
        var batch = AnalyticsWorlds.DeathEndRun();

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.RunEnd)
            .ShouldHaveSingleItem("one accepted END_RUN is exactly one run_end, dead or victorious.");

        emitted.Properties["victory"].ShouldBe(
            "false",
            "this run's Boss is alive and the hero is at 0 HP — reporting it as a victory would " +
            "poison every completion-rate read on the dashboard.");
    }

    [Fact]
    public void Translate_maps_an_accepted_begin_session_to_session_start()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var batch = AnalyticsWorlds.Sent(game, player, new BeginSessionCommand("1.9.3", "hash_abc"));

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.SessionStart)
            .ShouldHaveSingleItem("one accepted BEGIN_SESSION is exactly one session_start.");

        emitted.Properties["client_version"].ShouldBe("1.9.3", "the build the command reported.");
        emitted.Properties["content_hash"].ShouldBe("hash_abc", "the content version the command reported.");
    }

    [Fact]
    public void Translate_maps_a_rolled_die_to_die_rolled_with_source_rolled()
    {
        var (game, player) = Worlds.InARun();
        var batch = AnalyticsWorlds.Sent(game, player, new RollDiceCommand());

        var rolled = batch.Events.OfType<DiceRolled>()
            .ShouldHaveSingleItem("fixture floor: an accepted ROLL_DICE announces exactly one face.");

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.DieRolled)
            .ShouldHaveSingleItem("one DiceRolled is exactly one die_rolled.");

        emitted.Properties["face"].ShouldBe(
            rolled.Pips.ToString(CultureInfo.InvariantCulture),
            "the face the domain actually drew, rendered invariantly.");
        emitted.Properties["source"].ShouldBe(
            "rolled", "what tells a random roll from a spent fixed die on the same event name.");
    }

    [Fact]
    public void Translate_maps_a_spent_fixed_die_to_die_rolled_with_source_fixed()
    {
        var batch = AnalyticsWorlds.FixedDieWalk(pips: 3);

        batch.Events.OfType<FixedDieUsed>()
            .ShouldHaveSingleItem("fixture floor: an accepted USE_FIXED_DIE announces the spent die.");

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.DieRolled)
            .ShouldHaveSingleItem("one FixedDieUsed is exactly one die_rolled.");

        emitted.Properties["face"].ShouldBe("3", "the number the die showed.");
        emitted.Properties["source"].ShouldBe(
            "fixed",
            "source=rolled here would collapse Core's two events into one analytics fact and make " +
            "the fixed-die economy invisible.");
    }

    [Fact]
    public void Translate_maps_every_currency_movement_to_its_own_currency_changed()
    {
        var batch = AnalyticsWorlds.VictoryEndRun(bankedSoulShards: 50);

        var moved = batch.Events.OfType<CurrencyChanged>().ToArray();

        moved.Length.ShouldBeGreaterThanOrEqualTo(
            2,
            "fixture floor: a victory pays the banked Soul Shards AND grants the energy its " +
            "legend level-up carries — a fixture with one movement cannot prove per-event fidelity.");

        var payout = moved.Where(movement => movement.Reason == "run_end_payout")
            .ShouldHaveSingleItem(
                "fixture floor: the banked amount is paid under the payout reason exactly once.");

        payout.Delta.ShouldBe(50, "fixture floor: Victory pays the banked amount in full.");

        var emitted = AnalyticsTranslator.Translate(batch)
            .Where(e => e.Name == AnalyticsVocabulary.CurrencyChanged)
            .ToArray();

        emitted.Select(e => (e.Properties["currency"], e.Properties["delta"], e.Properties["reason"]))
            .ShouldBe(
                moved.Select(m => (
                    m.Id.ToString(),
                    m.Delta.ToString(CultureInfo.InvariantCulture),
                    m.Reason)),
                "every CurrencyChanged is exactly one currency_changed, in domain order — a " +
                "translator that keeps only the first movement makes every later wallet invisible.");

        var payoutEvent = emitted.Where(e => e.Properties["reason"] == "run_end_payout")
            .ShouldHaveSingleItem("the payout movement survives translation under its own reason.");

        payoutEvent.Properties["currency"].ShouldBe("SOUL_SHARDS", "which wallet moved.");
        payoutEvent.Properties["delta"].ShouldBe("50", "how much, signed, rendered invariantly.");
    }

    /// <summary>
    /// 🔒 The negative control: a domain event with no authored analytics name is ignored, never
    /// improvised into one. gear_merged and gear_enhanced are different facts than a grant, so
    /// inventing gear_granted here would pre-empt their owners.
    /// </summary>
    [Fact]
    public void Translate_emits_nothing_for_a_gear_grant()
    {
        var batch = AnalyticsWorlds.VictoryEndRun();

        batch.Events.OfType<GearGranted>().ShouldNotBeEmpty(
            "fixture floor: the session floor pays a qualifying run that produced nothing at its " +
            "band, so this victory carries at least one GearGranted for the translator to ignore.");

        var names = AnalyticsTranslator.Translate(batch).Select(e => e.Name).ToArray();

        names.ShouldAllBe(
            name => AnalyticsVocabulary.Emitted.Contains(name),
            "every emitted name comes from the closed vocabulary — this batch produced " +
            string.Join(", ", names) + ".");
        names.ShouldNotContain("gear_granted", "GearGranted maps to no authored event.");
    }

    [Fact]
    public void Translate_emits_nothing_for_a_batch_with_no_authored_mapping()
    {
        // ROLL_DICE is deliberately the command here: die_rolled comes from the DiceRolled EVENT,
        // so a batch carrying only an unmapped event under an unmapped command must yield nothing.
        var (game, player) = Worlds.InARun();
        var batch = new DispatchedEvents(
            player,
            new RollDiceCommand(),
            game.State(player),
            [new PityCounterAdvanced(1, "elite_mercy", 2)]);

        AnalyticsTranslator.Translate(batch).ShouldBeEmpty(
            "PityCounterAdvanced maps to no authored event, and a translator that emits something " +
            "for every batch is inventing facts.");
    }

    [Fact]
    public void Translate_refuses_a_null_batch()
    {
        Should.Throw<ArgumentNullException>(() => AnalyticsTranslator.Translate(null!));
    }
}
