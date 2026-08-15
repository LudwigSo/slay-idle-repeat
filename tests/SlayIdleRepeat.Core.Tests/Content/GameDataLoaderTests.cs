using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>The harness's <c>game-data</c> → <see cref="ContentSnapshot"/> loader, over the real tree.</summary>
/// <remarks>
/// Ships in the harness because that tool is pinned to <c>Core</c> with no packages and cannot reach
/// the <c>Application</c> pipeline or any adapter. Every negative case below carries a negative
/// control that must stay green, since a loader that threw on everything would satisfy the refusals too.
/// </remarks>
public sealed class GameDataLoaderTests
{
    /// <summary>The document every replacement below stands in for. Any real path would do.</summary>
    private const string Replaceable = "content/bosses/bosses.json";

    /// <summary>
    /// RAGE decays over an unstated curve, so the leaf is an authored <c>null</c> — the shipped hole
    /// this loader's <c>null</c> mapping is probed against.
    /// </summary>
    private const string AuthoredNull = "content/statuses.json#/statuses/8/decayCurve";

    /// <summary>The second shipped hole: <c>CURSED</c> names no curse.</summary>
    private const string SecondAuthoredNull = "content/enemies/enemies.json#/elites/modifiers/6/curseId";

    /// <summary>S3 — the tree really is the shipped one, and the loader really read it.</summary>
    [Fact]
    public void The_shipped_tree_loads_into_a_snapshot_that_holds_its_documents()
    {
        var snapshot = GameDataLoader.Load();

        snapshot.DocumentPaths.Count.ShouldBeGreaterThanOrEqualTo(
            40, "S3 — game-data holds four content documents, sixteen tuning files, the schemas and " +
                "the two loc files; an empty or shrunken tree would make every case here vacuous");

        snapshot.DocumentPaths.ShouldContain("content/bosses/bosses.json");
        snapshot.DocumentPaths.ShouldContain("content/enemies/enemies.json");
        snapshot.DocumentPaths.ShouldContain("tuning/par_power.json");

        // The keys are dataRoot-relative with forward slashes on every host — a backslash here would
        // make every pointer in every catalogue miss on Windows and hit on Linux.
        foreach (var path in snapshot.DocumentPaths)
        {
            path.Contains('\\').ShouldBeFalse(path);
            Path.IsPathRooted(path).ShouldBeFalse(path);
        }

        snapshot.ReadDouble("content/bosses/bosses.json#/secondaryStats/critDamage").ShouldBe(0.5);
    }

    /// <summary>
    /// A JSON <c>null</c> means "the design docs do not authorise a value here", and loads as
    /// <see cref="ContentValueKind.Unauthorised"/>, never as a zero.
    /// </summary>
    /// <remarks>
    /// Two shipped holes and two negative controls: <c>IsAuthorised</c> returning <c>false</c> is
    /// equally consistent with a loader that resolved nothing, so each hole is paired with an
    /// authored value at a sibling pointer that must read back authorised with its value.
    /// </remarks>
    [Theory]
    [InlineData(AuthoredNull, "content/statuses.json#/statuses/8/stat", "ATK")]
    [InlineData(SecondAuthoredNull,
        "content/enemies/enemies.json#/elites/modifiers/6/id", "CURSED")]
    public void An_authored_null_loads_as_an_unauthorised_hole_and_never_as_a_zero(
        string hole, string authored, string authoredValue)
    {
        var snapshot = GameDataLoader.Load();

        snapshot.IsAuthorised(hole).ShouldBeFalse(
            $"{hole} is null in the shipped data, which is the design docs authorising no value");
        snapshot.Read(hole).Kind.ShouldBe(ContentValueKind.Unauthorised);

        // Not a zero: mapping null to 0 would produce plausible numbers the simulator would grade,
        // hiding an unauthored curve as flat.
        Should.Throw<UnauthorisedTunableException>(() => snapshot.ReadDouble(hole))
            .Reference.ShouldBe(hole);

        // The discriminator: point the same two assertions at a value that IS authored.
        snapshot.IsAuthorised(authored).ShouldBeTrue(
            "the negative control — the loader resolves this document's authored leaves too, so " +
            "'unauthorised' above is a statement about the null and not about the loader");
        snapshot.ReadText(authored).ShouldBe(authoredValue);
    }

    /// <summary>
    /// A duplicate key <b>throws</b>. <c>JsonDocument</c> keeps one of the two and discards the
    /// other in silence, which would otherwise be a failure with nothing to see.
    /// </summary>
    /// <remarks>
    /// Two shapes: a duplicate at the document root and one nested inside an array element are
    /// different code paths — the second is the one a rule that only checked the top level would
    /// wave through, and it is the shape real authored data would produce.
    /// </remarks>
    [Theory]
    [InlineData("{ \"crit\": 0.05, \"crit\": 0.99 }", "crit", "at the document root")]
    [InlineData("{ \"scripts\": [ { \"id\": \"A\", \"id\": \"B\" } ] }", "id", "inside an array element")]
    public void A_duplicate_key_is_refused_rather_than_silently_resolved(
        string json, string key, string where)
    {
        var thrown = Should.Throw<InvalidOperationException>(
            () => GameDataLoader.LoadWith(GameDataLoader.DataRoot, Replacing(json)));

        thrown.Message.ShouldContain(key, Case.Sensitive, where);
        thrown.Message.ShouldContain(Replaceable, Case.Sensitive, "which document");
    }

    /// <summary>
    /// The negative control for the case above: the same two shapes with the duplicate removed
    /// load, and their values are readable. Without it, a loader that threw on every replacement
    /// would pass the refusals.
    /// </summary>
    [Theory]
    [InlineData("{ \"crit\": 0.05, \"critDamage\": 0.99 }", "#/critDamage", 0.99)]
    [InlineData("{ \"scripts\": [ { \"id\": \"A\", \"weight\": 4 } ] }", "#/scripts/0/weight", 4.0)]
    public void The_same_documents_without_the_duplicate_load(string json, string pointer, double expected)
    {
        var snapshot = GameDataLoader.LoadWith(GameDataLoader.DataRoot, Replacing(json));

        snapshot.ReadDouble(Replaceable + pointer).ShouldBe(expected);
    }

    /// <summary>An override is in memory and never an edit to <c>game-data/</c>, and it really does replace the document rather than being ignored.</summary>
    [Fact]
    public void An_override_replaces_the_document_in_memory_and_leaves_the_tree_alone()
    {
        var shipped = GameDataLoader.Load();
        var experiment = GameDataLoader.LoadWith(
            GameDataLoader.DataRoot, Replacing("{ \"secondaryStats\": { \"crit\": 0.99 } }"));

        experiment.ReadDouble(Replaceable + "#/secondaryStats/crit").ShouldBe(0.99);
        shipped.ReadDouble(Replaceable + "#/secondaryStats/crit").ShouldBe(
            0.05, "17 §1.2's authored baseline — the shipped snapshot is untouched by the experiment");

        // And the file on disk is untouched, which is the point.
        GameDataLoader.Load()
            .ReadDouble(Replaceable + "#/secondaryStats/crit")
            .ShouldBe(0.05, "an override that wrote to game-data/ would leave this at 0.99 forever");
    }

    /// <summary>An override naming a document the tree does not hold fails rather than doing nothing.</summary>
    /// <remarks>
    /// A silently ignored override is the worst outcome available: the run reports a result for an
    /// experiment it never performed.
    /// </remarks>
    [Theory]
    [InlineData("content/bosses/boses.json", "a typo in a real path")]
    [InlineData("tuning/adds_power.json", "a document nobody authored")]
    public void An_override_naming_no_document_fails_rather_than_running_the_unmodified_tree(
        string path, string why)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal) { [path] = "{}" };

        var thrown = Should.Throw<ArgumentException>(
            () => GameDataLoader.LoadWith(GameDataLoader.DataRoot, replacements));

        thrown.Message.ShouldContain(path, Case.Sensitive, why);
    }

    /// <summary>
    /// The stamp is a real hash of the loaded documents — <see cref="ContentVersion"/> exists so
    /// that a replayed command reproduces its outcome across a balance patch, which a constant
    /// cannot do.
    /// </summary>
    /// <remarks>
    /// Both directions: "two trees differ" alone passes on a random stamp, and "the same tree
    /// agrees" alone passes on a constant of zeros. Together they say it is a function of the
    /// content and nothing else.
    /// </remarks>
    [Fact]
    public void The_version_stamp_is_a_real_hash_of_the_documents_loaded()
    {
        var first = GameDataLoader.Load().Version;
        var again = GameDataLoader.Load().Version;
        var experiment = GameDataLoader
            .LoadWith(GameDataLoader.DataRoot, Replacing("{ \"secondaryStats\": { \"crit\": 0.99 } }"))
            .Version;

        first.Value.Length.ShouldBe(ContentVersion.HexLength);
        first.Value.ShouldBe(first.Value.ToLowerInvariant(), "a stamp is 64 LOWERCASE hex characters");
        first.Value.ShouldNotBe(new string('0', ContentVersion.HexLength), "never a placeholder");

        again.ShouldBe(first, "the same bytes stamp the same way on every run, or replay is worthless");
        experiment.ShouldNotBe(
            first, "two different data trees must not produce one version — the whole point of the " +
                   "stamp is that a balance patch is visible to a replay");
    }

    /// <summary>The checkout is found by walking up, and it is the one holding <c>game-data</c>.</summary>
    [Fact]
    public void The_repository_root_is_the_directory_holding_the_solution_file()
    {
        var root = GameDataLoader.FindRepositoryRoot();

        File.Exists(Path.Combine(root, "SlayIdleRepeat.sln")).ShouldBeTrue(
            $"searched upward from {AppContext.BaseDirectory}");
        Directory.Exists(GameDataLoader.DataRoot).ShouldBeTrue(GameDataLoader.DataRoot);
    }

    /// <summary>A data root that is not there fails legibly rather than loading an empty snapshot.</summary>
    [Fact]
    public void A_missing_data_root_fails_rather_than_loading_nothing()
    {
        var absent = Path.Combine(GameDataLoader.FindRepositoryRoot(), "game-data-that-is-not-there");

        Should.Throw<DirectoryNotFoundException>(() => GameDataLoader.Load(absent))
            .Message.ShouldContain("game-data-that-is-not-there", Case.Sensitive);
    }

    private static Dictionary<string, string> Replacing(string json) =>
        new(StringComparer.Ordinal) { [Replaceable] = json };
}
