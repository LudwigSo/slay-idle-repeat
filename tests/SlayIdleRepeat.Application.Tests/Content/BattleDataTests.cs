using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Battle Replay screen's strings as content: the document that names them, the schema that
/// governs it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/battle/battle.json</c> is what names them, and these
/// cases are what stop it being quietly deleted or emptied.
/// </remarks>
public sealed class BattleDataTests
{
    private const string BattleDocument = "content/battle/battle.json";

    private const string BattleSchema = "schema/battle.schema.json";

    private const string BattleDirectory = "content/battle/";

    private const string SkipActionKey = "loc.battle.skip.action";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_battle_document_pairs_with_the_battle_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(BattleDocument).ShouldBe(
            BattleSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed battle document fails the build rather than the screen.");
        shipped.ShouldContain(
            BattleDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            BattleSchema,
            "likewise the schema half: deleting schema/battle.schema.json leaves SchemaFor answering " +
            "exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case the
    /// pairing table's own remarks name: it answers correctly while there is exactly one file, and
    /// the gap only becomes visible as a wrongly-resolved path the day a second arrives.
    /// </summary>
    [Fact]
    public void The_battle_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(BattleDirectory, BattleSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the battle screen needs a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_battle_directory_still_pairs_with_the_one_battle_schema()
    {
        ContentLayout.SchemaFor("content/battle/battle_actors.json").ShouldBe(
            BattleSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/battle_actors.schema.json and " +
            "fail as MissingSchema — a build break landing on whoever adds the file, for a pairing " +
            "decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_battle_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the battle strings were added to the " +
            "locales without the document that names them — which is a client that cannot load its " +
            "own content.");
    }

    [Theory]
    [InlineData("loc.battle.title.name")]
    [InlineData("loc.battle.hero.label")]
    [InlineData("loc.battle.enemy.label")]
    [InlineData("loc.battle.speed.label")]
    [InlineData("loc.battle.speed_single.action")]
    [InlineData("loc.battle.speed_double.action")]
    [InlineData("loc.battle.speed_triple.action")]
    [InlineData(SkipActionKey)]
    [InlineData("loc.battle.phase_one.name")]
    [InlineData("loc.battle.phase_two.name")]
    [InlineData("loc.battle.phase_three.name")]
    [InlineData("loc.battle.loading.status")]
    [InlineData("loc.battle.no_run.status")]
    [InlineData("loc.battle.phase_not_battle.status")]
    [InlineData("loc.battle.seed_unavailable.status")]
    [InlineData("loc.battle.simulator_failed.status")]
    [InlineData("loc.battle.log_empty.status")]
    [InlineData("loc.battle.read_unavailable.status")]
    [InlineData("loc.battle.victory.status")]
    [InlineData("loc.battle.defeat.status")]
    [InlineData("loc.battle.refused.status")]
    public void Every_battle_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Battle Replay screen renders, and X-04 requires every " +
            "user-facing string to be a key in EN and DE from day one. A missing one renders as its " +
            "own key on the screen every fight in the game is watched on.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// The eleven greenwood rows of the art manifest — the eight pool archetypes, the two elites and
    /// the boss — each with the name key the replay's caption resolves for it.
    /// </summary>
    public static TheoryData<string> GreenwoodEnemyNameKeys =>
    [
        "loc.enemy.grunt.name",
        "loc.enemy.swarm.name",
        "loc.enemy.brute.name",
        "loc.enemy.skirmisher.name",
        "loc.enemy.warden.name",
        "loc.enemy.caster.name",
        "loc.enemy.leech.name",
        "loc.enemy.reaver.name",
        "loc.enemy.el_thorn_sentinel.name",
        "loc.enemy.el_mossback_alpha.name",
        "loc.enemy.boss_thornmaw.name",
    ];

    [Theory]
    [MemberData(nameof(GreenwoodEnemyNameKeys))]
    public void Every_greenwood_enemy_the_replay_can_name_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.IsAuthorised(EnglishStrings + key).ShouldBeTrue(
            $"'{key}' is what the Battle Replay banner resolves for an enemy the roster names, and " +
            "the catalogue falls back to the key itself — so a missing row is this dotted identifier " +
            "over the enemy's head in every fight it appears in.");
        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            "and a blank row is a banner with a hole in it rather than a name.");
        snapshot.IsAuthorised(GermanStrings + key).ShouldBeTrue(
            "X-04 requires every user-facing string to be a key in EN and DE from day one, and an " +
            "enemy's name is the one string a German player reads on every fight.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 🔒 The German names are authored, not copied: stated on the boss, as 'different from EN and
    /// not the untranslated placeholder' rather than as any particular wording.
    /// </summary>
    [Fact]
    public void The_boss_is_named_in_German_rather_than_carrying_its_English_name_across()
    {
        const string bossNameKey = "loc.enemy.boss_thornmaw.name";
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.IsAuthorised(GermanStrings + bossNameKey).ShouldBeTrue(
            "the boss's name has to exist in German before it can differ from the English one.");

        var german = snapshot.ReadText(GermanStrings + bossNameKey);

        german.ShouldNotBe(
            snapshot.ReadText(EnglishStrings + bossNameKey),
            "the chapter boss is the one enemy every player of chapter 1 meets by name, and its " +
            "German name is authored rather than the English one copied across.");
        german.ShouldNotContain(
            "##TODO_DE##",
            Case.Sensitive,
            "and it is a translation, not the untranslated placeholder the other DE rows still carry.");
    }

    /// <summary>
    /// 🔒 The four stall sentences differ from one another <b>as authored</b> — the claim the client
    /// suite makes about the screen, restated here over the content the loader actually validates.
    /// </summary>
    /// <remarks>
    /// 🔒 There were five: <c>heroStatsUnavailable</c> was retired on 2026-08-19 with the state it
    /// named, because <c>M7-06b</c> had made the hero's stat block buildable and the sentence went on
    /// telling players it was not.
    /// </remarks>
    [Fact]
    public void The_four_reasons_a_replay_has_nothing_to_animate_read_as_four_different_sentences()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        string[] sentences =
        [
            snapshot.ReadText(EnglishStrings + "loc.battle.phase_not_battle.status"),
            snapshot.ReadText(EnglishStrings + "loc.battle.seed_unavailable.status"),
            snapshot.ReadText(EnglishStrings + "loc.battle.simulator_failed.status"),
            snapshot.ReadText(EnglishStrings + "loc.battle.log_empty.status"),
        ];

        sentences.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            sentences.Length,
            "on screen all five of these are the same still frame, so the sentence is the only thing " +
            "telling them apart. Two that read the same send a player — and whoever reads their bug " +
            "report — after the wrong one of five problems with five different owners: " +
            $"[{string.Join(" | ", sentences)}]");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_battle_string_the_battle_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            BattleDocument,
            $"\"skip\": \"{SkipActionKey}\"",
            "\"skip\": \"loc.battle.title.name\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + SkipActionKey,
            "with nothing naming it, the skip caption becomes a translation somebody pays for twice " +
            "— and, because every issue is fatal, a client that will not load. The skip is also the " +
            "one control on this screen an accessibility clause requires, so its caption going " +
            "unnamed is the failure this document most exists to prevent.");
    }
}
