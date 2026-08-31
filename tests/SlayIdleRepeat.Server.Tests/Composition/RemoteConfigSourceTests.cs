using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The flags document's whole contract (14 §10, §14): every member optional with the identity as
/// its omission, anything unrecognisable refusing the WHOLE document loudly, and absence being the
/// identity element rather than an error. <c>Document</c> is what <c>GET /config</c> serves, so it
/// is pinned to the operator's exact bytes.
/// </summary>
public sealed class RemoteConfigSourceTests
{
    [Fact]
    public void A_full_document_parses_into_the_matching_flags_and_rides_out_verbatim()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        // Alternating switch values: each neighbouring pair differs, so two members wired
        // crosswise cannot both read back right (the one-switch Theory discriminates the rest).
        const string document = """
            {
              "pvpEnabled": false,
              "plusOfferEnabled": true,
              "mailEnabled": false,
              "disabledAdPlacements": ["ad_killed"],
              "disabledChapters": ["7"]
            }
            """;

        try
        {
            File.WriteAllText(path, document);

            var source = new RemoteConfigSource(path, warnings.Add);

            source.Current.PvpEnabled.ShouldBeFalse();
            source.Current.PlusOfferEnabled.ShouldBeTrue();
            source.Current.MailEnabled.ShouldBeFalse();
            source.Current.IsAdPlacementEnabled("ad_killed").ShouldBeFalse();
            source.Current.IsChapterEnabled("7").ShouldBeFalse();
            source.Document.ShouldBe(document, "GET /config serves the operator's exact bytes, never a re-rendering");
            warnings.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_empty_document_is_the_identity_and_rides_out_verbatim()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            File.WriteAllText(path, "{}");

            var source = new RemoteConfigSource(path, warnings.Add);

            ShouldBeTheIdentity(source.Current);
            source.Document.ShouldBe("{}", "an accepted file rides out as written, however little it says");
            warnings.ShouldBeEmpty("an empty document kills nothing and is not an incident");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Omitted members are the identity, member by member — and a present one moves only itself.</summary>
    [Theory]
    [InlineData("{\"pvpEnabled\": false}", false, true, true)]
    [InlineData("{\"plusOfferEnabled\": false}", true, false, true)]
    [InlineData("{\"mailEnabled\": false}", true, true, false)]
    public void A_document_naming_one_switch_moves_only_that_switch(
        string document, bool pvp, bool plusOffer, bool mail)
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            File.WriteAllText(path, document);

            var source = new RemoteConfigSource(path, warnings.Add);

            source.Current.PvpEnabled.ShouldBe(pvp);
            source.Current.PlusOfferEnabled.ShouldBe(plusOffer);
            source.Current.MailEnabled.ShouldBe(
                mail, "a switch reading back through a neighbour means the members are wired crosswise");
            source.Current.DisabledAdPlacements.ShouldBeEmpty();
            source.Current.DisabledChapters.ShouldBeEmpty();
            warnings.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_document_naming_one_kill_list_kills_there_and_nowhere_else()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            File.WriteAllText(path, "{\"disabledAdPlacements\": [\"ad_killed\"]}");

            var source = new RemoteConfigSource(path, warnings.Add);

            source.Current.IsAdPlacementEnabled("ad_killed").ShouldBeFalse();
            source.Current.DisabledChapters.ShouldBeEmpty(
                "a placement kill landing in the chapter list is the two lists wired crosswise");
            source.Current.PvpEnabled.ShouldBeTrue();
            source.Current.PlusOfferEnabled.ShouldBeTrue();
            source.Current.MailEnabled.ShouldBeTrue();
            warnings.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A typo like <c>disabledChapter</c> must not silently kill nothing — the whole document is refused.</summary>
    [Fact]
    public void An_unknown_top_level_member_refuses_the_whole_document_and_keeps_the_last_good_one()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();
        const string lastGood = "{\"pvpEnabled\": false}";

        try
        {
            File.WriteAllText(path, lastGood);
            var source = new RemoteConfigSource(path, warnings.Add);
            source.Current.PvpEnabled.ShouldBeFalse("the last-good state the refusal below must keep");

            File.WriteAllText(path, "{\"pvpEnabled\": true, \"disabledChapter\": [\"7\"]}");
            source.Reload();

            warnings.ShouldContain(
                w => w.Contains("[remote-config]"),
                "a refusal without the greppable marker is a kill switch an operator thinks is thrown");
            source.Current.PvpEnabled.ShouldBeFalse("nothing of the refused document may apply, not even its known members");
            source.Document.ShouldBe(lastGood);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_blank_kill_list_entry_refuses_the_whole_document()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            File.WriteAllText(path, "{\"disabledAdPlacements\": [\"ad_killed\", \"\"]}");

            var source = new RemoteConfigSource(path, warnings.Add);

            warnings.ShouldContain(w => w.Contains("[remote-config]"));
            source.Current.IsAdPlacementEnabled("ad_killed").ShouldBeTrue(
                "half-applying a refused document would kill the named placement while hiding the refusal");
            source.Document.ShouldNotContain("ad_killed", customMessage: "a refused file must never be served");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>JsonDocument tolerates duplicates last-wins — which of the two did the operator mean?</summary>
    [Fact]
    public void A_duplicate_top_level_member_refuses_the_whole_document()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            // 🔴 The LAST value must differ from the default, or this case cannot fail. Written the
            // other way round it read `false, true` — and `true` is both JsonDocument's last-wins
            // answer AND the identity flags' value, so the assertion below held whether the document
            // was refused or silently accepted.
            File.WriteAllText(path, "{\"pvpEnabled\": true, \"pvpEnabled\": false}");

            var source = new RemoteConfigSource(path, warnings.Add);

            warnings.ShouldContain(
                w => w.Contains("[remote-config]") && w.Contains("Duplicate top-level member")
                    && w.Contains("pvpEnabled"),
                "the warning must name the rule that fired and the member it fired on, or a document " +
                "refused for some other reason reads as this one." + Environment.NewLine +
                string.Join(Environment.NewLine, warnings));

            source.Current.PvpEnabled.ShouldBeTrue(
                "neither of a duplicate member's contradicting values may apply, so the identity " +
                "flags stand — and the last-wins value is now `false`, so this can only be true if " +
                "the whole document was refused");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Malformed_json_refuses_the_whole_document()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            File.WriteAllText(path, "{\"pvpEnabled\": fal");

            var source = new RemoteConfigSource(path, warnings.Add);

            warnings.ShouldContain(w => w.Contains("[remote-config]"));
            ShouldBeTheIdentity(source.Current);
            Should.NotThrow(
                () => JsonDocument.Parse(source.Document),
                "an unparseable file served to every client would break each of them the same way");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Absence is the authored identity — everything enabled — never a quiet failure (S6).</summary>
    [Fact]
    public void An_absent_file_is_the_identity_and_a_loud_greppable_warning()
    {
        var warnings = new List<string>();

        var source = new RemoteConfigSource(ATempConfigPath(), warnings.Add);

        ShouldBeTheIdentity(source.Current);
        ShouldBeTheRenderedIdentityDocument(source.Document);
        warnings.ShouldContain(
            w => w.Contains("[remote-config]"),
            "ops grep for the marker; an unmarked warning is one nobody finds");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unset_path_is_the_identity_and_a_loud_greppable_warning(string? path)
    {
        var warnings = new List<string>();

        var source = new RemoteConfigSource(path, warnings.Add);

        ShouldBeTheIdentity(source.Current);
        ShouldBeTheRenderedIdentityDocument(source.Document);
        warnings.ShouldContain(w => w.Contains("[remote-config]"));
    }

    [Fact]
    public void Reload_swaps_to_the_changed_file_on_disk()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();
        const string changed = "{\"mailEnabled\": false}";

        try
        {
            File.WriteAllText(path, "{\"pvpEnabled\": false}");
            var source = new RemoteConfigSource(path, warnings.Add);

            File.WriteAllText(path, changed);
            source.Reload();

            source.Current.PvpEnabled.ShouldBeTrue("the old document's kill does not survive the document that lifted it");
            source.Current.MailEnabled.ShouldBeFalse();
            source.Document.ShouldBe(changed);
            warnings.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reload_after_the_file_is_deleted_applies_absence_semantics()
    {
        var path = ATempConfigPath();
        var warnings = new List<string>();

        try
        {
            File.WriteAllText(path, "{\"pvpEnabled\": false}");
            var source = new RemoteConfigSource(path, warnings.Add);

            File.Delete(path);
            source.Reload();

            ShouldBeTheIdentity(source.Current);
            ShouldBeTheRenderedIdentityDocument(source.Document);
            warnings.ShouldContain(w => w.Contains("[remote-config]"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Short unique paths under the system temp root — deep worktree paths hit MAX_PATH on this repo's machines.</summary>
    private static string ATempConfigPath() =>
        Path.Combine(Path.GetTempPath(), "sir-rc-" + Guid.NewGuid().ToString("N") + ".json");

    private static void ShouldBeTheIdentity(FeatureFlags flags)
    {
        flags.PvpEnabled.ShouldBeTrue();
        flags.PlusOfferEnabled.ShouldBeTrue();
        flags.MailEnabled.ShouldBeTrue();
        flags.DisabledAdPlacements.ShouldBeEmpty();
        flags.DisabledChapters.ShouldBeEmpty();
    }

    /// <summary>The identity rendered explicitly: all five members present, each at its identity.</summary>
    private static void ShouldBeTheRenderedIdentityDocument(string document)
    {
        using var parsed = JsonDocument.Parse(document);

        parsed.RootElement.GetProperty("pvpEnabled").GetBoolean().ShouldBeTrue();
        parsed.RootElement.GetProperty("plusOfferEnabled").GetBoolean().ShouldBeTrue();
        parsed.RootElement.GetProperty("mailEnabled").GetBoolean().ShouldBeTrue();
        parsed.RootElement.GetProperty("disabledAdPlacements").GetArrayLength().ShouldBe(0);
        parsed.RootElement.GetProperty("disabledChapters").GetArrayLength().ShouldBe(0);
    }
}
