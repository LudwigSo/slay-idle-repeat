using SlayIdleRepeat.AssetManifest;
using Shouldly;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// The gate, driven with a synthetic delivery set and store over the REAL shipped register — every
/// rule proven to fire, and proven to fire for its own reason.
/// </summary>
/// <remarks>
/// Zero assets are delivered in this repository today, so the forward direction has no live
/// subject — these cases hand the gate a delivery set to check that it bites, rather than trusting
/// a green run over an empty set.
/// </remarks>
public sealed class ProvenanceGateTests
{
    // ------------------------------------------------------------------ the happy path

    [Fact]
    public void A_delivered_asset_with_a_record_and_a_confirmed_licence_passes()
    {
        var report = Run(
            [ProvenanceFixtures.DeliveredArt()],
            [ProvenanceFixtures.Midjourney()],
            [ProvenanceFixtures.ConfirmedLicence()]);

        report.Violations.ShouldBeEmpty();
        report.Passed.ShouldBeTrue();
        report.VerifiedPairings.ShouldBe(1);
        report.Coverage.ShouldBe(DeliveryCoverage.Partial);
    }

    // ------------------------------------------------------------------ forward direction

    /// <summary>The rule this whole gate exists for: an asset on disk with no provenance record fails the build.</summary>
    [Fact]
    public void A_delivered_asset_with_no_record_fails()
    {
        var report = Run([ProvenanceFixtures.DeliveredArt()], [], [ProvenanceFixtures.ConfirmedLicence()]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.MissingProvenance);
        violation.Subject.ShouldBe("art/chr_hero_body_idle.png");
        violation.Detail.ShouldMatchWildcard("*no provenance record exists for 'chr_hero_body_idle'*15 §G and 20 §2.1*");
        report.VerifiedPairings.ShouldBe(0);
    }

    [Fact]
    public void A_delivered_file_whose_stem_is_in_neither_register_fails_as_an_unregistered_delivery()
    {
        var report = Run(
            [new DeliveredAsset("chr_hero_bod_idle", "art/chr_hero_bod_idle.png", ".png")],
            [],
            [ProvenanceFixtures.ConfirmedLicence()]);

        // A typo'd delivery is NOT reported as a missing record — the two say different things to
        // whoever has to fix it.
        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.UnregisteredDelivery);
        violation.Detail.ShouldMatchWildcard("*is in neither register*15 §D1 makes the file name the asset id*");
    }

    [Fact]
    public void An_art_slot_delivered_as_audio_fails_as_a_medium_mismatch()
    {
        var report = Run(
            [new DeliveredAsset(ProvenanceFixtures.ArtId, "art/chr_hero_body_idle.ogg", ".ogg")],
            [ProvenanceFixtures.Midjourney()],
            [ProvenanceFixtures.ConfirmedLicence()]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.MediumMismatch);

        // Both branches of the rule emit the same code, so pin which one fired.
        violation.Detail.ShouldMatchWildcard("*is an ART slot; 15 §D1 delivers art as .png*");
    }

    /// <inheritdoc cref="An_art_slot_delivered_as_audio_fails_as_a_medium_mismatch"/>
    [Fact]
    public void An_audio_slot_delivered_as_art_fails_as_a_medium_mismatch()
    {
        var report = Run(
            [new DeliveredAsset(ProvenanceFixtures.AudioId, "audio/mus_home.png", ".png")],
            [ProvenanceFixtures.Cc0()],
            [ProvenanceFixtures.ConfirmedLicence("Audacity") with { AppliesTo = "audio" }]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.MediumMismatch);
        violation.Detail.ShouldMatchWildcard("*is an AUDIO slot; 20 §5 delivers OGG*WAV (SFX source)*");
    }

    /// <summary>
    /// Music is OGG Vorbis only; WAV is the SFX <em>source</em> format. Read off
    /// <c>AudioAsset.IsMusic</c> rather than the id's prefix — the register is the vocabulary.
    /// </summary>
    [Fact]
    public void A_music_track_delivered_as_wav_fails_because_20_s5_gives_music_ogg_only()
    {
        var report = Run(
            [new DeliveredAsset(ProvenanceFixtures.AudioId, "audio/mus_home.wav", ".wav")],
            [ProvenanceFixtures.Cc0()],
            [ProvenanceFixtures.ConfirmedLicence("Audacity") with { AppliesTo = "audio" }]);

        report.Violations.ShouldHaveSingleItem()
            .Detail.ShouldMatchWildcard("*is a MUSIC track; 20 §5 gives music OGG Vorbis only*");
    }

    [Fact]
    public void An_sfx_delivered_as_wav_is_accepted_because_20_s5_makes_wav_its_source_format()
    {
        var report = Run(
            [new DeliveredAsset("sfx_crit", "audio/sfx_crit.wav", ".wav")],
            [ProvenanceFixtures.Cc0("sfx_crit")],
            [ProvenanceFixtures.ConfirmedLicence("Audacity") with { AppliesTo = "audio" }]);

        report.Violations.ShouldBeEmpty();
    }

    /// <summary>
    /// A DELIVERED cut asset is reported as the cut asset it is, not as a missing record — the gate
    /// must never tell an author to write a record it would then refuse under ruling O8.
    /// </summary>
    [Fact]
    public void A_delivered_cut_asset_is_reported_as_cut_and_not_as_missing_provenance()
    {
        var report = Run(
            [ProvenanceFixtures.DeliveredArt(ProvenanceFixtures.CutArtId)],
            [],
            [ProvenanceFixtures.ConfirmedLicence()]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.CutAsset);
        violation.Detail.ShouldMatchWildcard("*is delivered and a ruling has cut*needs no provenance record*");
        report.Violations.ShouldNotContain(v => v.Code == ViolationCode.MissingProvenance);
        report.CoveredSlots.ShouldBe(0);
    }

    // ------------------------------------------------------------------ reverse direction

    /// <summary>
    /// The other half: orphan checking runs both ways, and a record for an id the register does not
    /// hold is a licence claim about nothing.
    /// </summary>
    [Fact]
    public void A_record_for_an_unknown_asset_id_fails()
    {
        var report = Run([], [ProvenanceFixtures.Midjourney(ProvenanceFixtures.UnknownId)], ProvenanceFixtures.ShippedLicences);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.UnknownAsset);
        violation.Subject.ShouldBe(ProvenanceFixtures.UnknownId);
        violation.Detail.ShouldMatchWildcard("*register has no such asset id*hides a rename*");
    }

    /// <summary>
    /// The O8 ruling cut all 32 sprite sheets. A cut asset needs no record — and a record for one is
    /// its OWN failure, not an unknown id: an unknown id is a typo, a cut id is provenance for work
    /// that was called off.
    /// </summary>
    [Fact]
    public void A_record_for_a_cut_asset_fails_as_a_cut_asset_and_not_as_an_unknown_one()
    {
        var report = Run([], [ProvenanceFixtures.Midjourney(ProvenanceFixtures.CutArtId)], [ProvenanceFixtures.ConfirmedLicence()]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.CutAsset);
        violation.Subject.ShouldBe(ProvenanceFixtures.CutArtId);
        violation.Detail.ShouldMatchWildcard("*a ruling has cut it*needs no record*");
        report.Violations.ShouldNotContain(v => v.Code == ViolationCode.UnknownAsset);
    }

    [Fact]
    public void A_cut_asset_needing_no_record_is_not_reported_when_it_simply_has_none()
    {
        // The complement of the case above, and the reason it is stated: 32 cut rows sit in the
        // register with no records, and none of them may show up as a finding.
        var report = Run([], [], ProvenanceFixtures.ShippedLicences);

        report.CutAssets.ShouldBe(32);
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void A_malformed_record_is_reported_through_the_gate_as_well_as_the_validator()
    {
        var report = Run([], [ProvenanceFixtures.Midjourney() with { Seed = "" }], [ProvenanceFixtures.ConfirmedLicence()]);

        report.Violations.ShouldHaveSingleItem().Code.ShouldBe(ViolationCode.MalformedRecord);
    }

    // ------------------------------------------------------------------ licences

    /// <summary>
    /// Commercial terms must be confirmed in writing before the first batch. This rule cannot fire
    /// today because nothing is delivered and nothing has a record — and it fires on the first
    /// asset that does.
    /// </summary>
    [Fact]
    public void An_asset_made_with_a_tool_whose_licence_is_unconfirmed_fails()
    {
        var report = Run(
            [ProvenanceFixtures.DeliveredArt()],
            [ProvenanceFixtures.Midjourney()],
            ProvenanceFixtures.ShippedLicences);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.UnconfirmedLicence);
        violation.Detail.ShouldMatchWildcard(
            "*'midjourney', whose commercial terms are not confirmed in writing*confirmedInWriting is unset*M8-01b*");
    }

    /// <summary>
    /// A bare <c>true</c> is not a confirmation. Without a <c>confirmationRef</c> naming where the
    /// written terms are filed, the register is asserting the formality this rule says it is not.
    /// </summary>
    [Fact]
    public void A_licence_marked_confirmed_with_nothing_filed_behind_it_still_fails()
    {
        var report = Run(
            [ProvenanceFixtures.DeliveredArt()],
            [ProvenanceFixtures.Midjourney()],
            [new ToolLicence("midjourney", "art", true, null, "no paperwork")]);

        report.Violations.ShouldHaveSingleItem().Code.ShouldBe(ViolationCode.UnconfirmedLicence);
    }

    /// <summary>
    /// A confirmation obtained for one medium is not evidence about the other — without this, the
    /// day art terms are confirmed for a tool, an audio asset naming the same tool would ride in on it.
    /// </summary>
    [Fact]
    public void A_licence_confirmed_for_art_does_not_cover_an_audio_asset()
    {
        var report = Run(
            [ProvenanceFixtures.DeliveredAudio()],
            [ProvenanceFixtures.Cc0() with { Tooling = new AudioTooling("midjourney", "v6.1") }],
            [ProvenanceFixtures.ConfirmedLicence()]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.LicenceScopeMismatch);
        violation.Detail.ShouldMatchWildcard(
            "*is audio and names the tool 'midjourney', whose licence entry applies to 'art'*");
    }

    [Fact]
    public void A_record_naming_a_tool_the_licence_register_never_declared_fails_as_unknown()
    {
        var report = Run(
            [ProvenanceFixtures.DeliveredAudio()],
            [ProvenanceFixtures.Cc0()],
            [ProvenanceFixtures.ConfirmedLicence()]);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.UnknownTool);
        violation.Detail.ShouldMatchWildcard("*names the tool 'Audacity'*tool-licences.json does not declare*");
    }

    /// <summary>
    /// A procedural ART record names no tool: its generator is this repository, which needs no
    /// commercial licence confirmed. The rule must not fire on it.
    /// </summary>
    [Fact]
    public void A_procedural_art_record_names_no_tool_and_needs_no_licence()
    {
        ProvenanceFixtures.Procedural().ToolsNamed.ShouldBeEmpty();

        Run([ProvenanceFixtures.DeliveredArt(ProvenanceFixtures.OtherArtId)],
                [ProvenanceFixtures.Procedural()],
                ProvenanceFixtures.ShippedLicences)
            .Violations.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ the floor

    /// <summary>
    /// The gate quantifies over the shipped register, and a register that shrank below its floor is
    /// RED rather than "nothing to check". Proven by handing the gate a register with two rows in it.
    /// </summary>
    [Fact]
    public void A_register_below_its_floor_fails_rather_than_passing_over_nothing()
    {
        var report = ProvenanceGate.Run(
            TinyRegister(),
            ProvenanceFixtures.StoreOf([], ProvenanceFixtures.ShippedLicences),
            []);

        // The floor has three branches and they all carry ManifestFloor, so each is pinned by its
        // own subject rather than the shared code.
        report.Violations
            .First(v => v.Code == ViolationCode.ManifestFloor &&
                        v.Subject == ProvenanceGate.RegisterLocation)
            .Detail.ShouldMatchWildcard("*the register holds 2 asset ids, below the floor of 1000*");

        report.Violations
            .First(v => v.Subject == "ui_panel_main_9slice")
            .Detail.ShouldMatchWildcard(
                "*is named by 15 §D1 / 20 §3 and is no longer in the register*");
    }

    /// <summary>
    /// The floor's other half — a count alone is satisfied by a thousand rows of anything, so every
    /// literal canary id must still be in the register.
    /// </summary>
    [Fact]
    public void Every_canary_id_is_still_in_the_shipped_register()
    {
        ProvenanceGate.CanaryAssetIds.Count.ShouldBeGreaterThanOrEqualTo(5);

        foreach (var canary in ProvenanceGate.CanaryAssetIds)
        {
            var found = ProvenanceFixtures.Register.FindArt(canary) is not null ||
                        ProvenanceFixtures.Register.FindAudio(canary) is not null;

            found.ShouldBeTrue(
                $"'{canary}' is a canary the gate's floor is keyed on and the register no longer " +
                "holds it. Fix the list and every record keyed to the old id.");
        }
    }

    [Fact]
    public void A_register_with_nothing_cut_fails_the_floor_because_the_cut_asset_rule_would_have_no_subject()
    {
        var report = ProvenanceGate.Run(
            TinyRegister(),
            ProvenanceFixtures.StoreOf([], ProvenanceFixtures.ShippedLicences),
            []);

        report.Violations
            .ShouldContain(v => v.Code == ViolationCode.ManifestFloor &&
                                v.Subject == ProvenanceGate.CanaryCutAssetId);
    }

    // ------------------------------------------------------------------ the declaration

    /// <summary>
    /// The declared "nothing is delivered yet" state expires by itself. While
    /// <c>DeliveryDeclaration.AwaitingFirstDelivery</c> is true, one delivered asset is enough to
    /// fail the build, so the declaration cannot outlive the situation it describes.
    /// </summary>
    [Fact]
    public void One_delivered_asset_makes_the_awaiting_first_delivery_declaration_stale()
    {
        var report = ProvenanceGate.Run(
            ProvenanceFixtures.Register,
            ProvenanceFixtures.StoreOf([ProvenanceFixtures.Midjourney()], [ProvenanceFixtures.ConfirmedLicence()]),
            [ProvenanceFixtures.DeliveredArt()],
            awaitingFirstDelivery: true);

        var violation = report.Violations.ShouldHaveSingleItem();
        violation.Code.ShouldBe(ViolationCode.StaleDeliveryDeclaration);
        violation.Detail.ShouldMatchWildcard(
            "*declares AwaitingFirstDelivery = true, and 1 asset(s) are delivered*stopped being true*");
        report.Coverage.ShouldBe(DeliveryCoverage.Partial);
    }

    /// <summary>
    /// The declaration's own caveat: it must fail when it has been <em>satisfied</em> too.
    /// Pre-arming the flag while nothing is delivered would silence the first direction for as long
    /// as the delivery set stayed empty.
    /// </summary>
    [Fact]
    public void Flipping_the_declaration_early_is_equally_stale()
    {
        var report = ProvenanceGate.Run(
            ProvenanceFixtures.Register,
            ProvenanceFixtures.StoreOf([], ProvenanceFixtures.ShippedLicences),
            [],
            awaitingFirstDelivery: false);

        report.Violations.ShouldHaveSingleItem()
            .Detail.ShouldMatchWildcard(
                "*declares AwaitingFirstDelivery = false, and nothing is delivered*not a declaration*");
    }

    /// <summary>
    /// The constant that ships today, pinned. It is <c>true</c> because zero assets are delivered;
    /// the commit that delivers the first one flips it and rewrites this.
    /// </summary>
    [Fact]
    public void The_shipped_declaration_says_nothing_has_been_delivered_yet()
    {
        DeliveryDeclaration.AwaitingFirstDelivery.ShouldBeTrue(
            "flip it in the same commit as the first delivered asset, not before.");
        DeliveryDeclaration.TurnsOn.ShouldBe("M8-02");

        // The reason has to name the thing a later reader would falsify — the audio half is
        // blocked for a DIFFERENT cause (no licence) than the art half (no human session), and a
        // reason that mentioned only one would go stale silently when the other cleared.
        DeliveryDeclaration.Reason.ShouldContain("M8-07", Case.Sensitive);
        DeliveryDeclaration.Reason.ShouldContain("Midjourney", Case.Sensitive);
        DeliveryDeclaration.Reason.ShouldContain("M8-10", Case.Sensitive);
    }

    /// <summary>
    /// The empty state and a populated one must never read the same. The headline for zero
    /// deliveries says so in words, names the pairing count it actually verified, and says which
    /// checks DID run.
    /// </summary>
    [Fact]
    public void The_zero_delivery_headline_cannot_be_read_as_a_pass_over_a_populated_set()
    {
        var empty = Run([], [], ProvenanceFixtures.ShippedLicences);
        var populated = Run(
            [ProvenanceFixtures.DeliveredArt()],
            [ProvenanceFixtures.Midjourney()],
            [ProvenanceFixtures.ConfirmedLicence()]);

        empty.Passed.ShouldBeTrue();
        populated.Passed.ShouldBeTrue();

        empty.Coverage.ShouldBe(DeliveryCoverage.AwaitingFirstDelivery);
        empty.Headline().ShouldStartWith("AWAITING FIRST DELIVERY", Case.Sensitive);
        empty.Headline().ShouldContain("0 asset-to-record pairings were verified", Case.Sensitive);
        empty.Headline().ShouldContain("turns on M8-02", Case.Sensitive);

        empty.Headline().ShouldNotBe(populated.Headline());
        empty.VerifiedPairings.ShouldBe(0);
        populated.VerifiedPairings.ShouldBe(1);
    }

    // ------------------------------------------------------------------ coverage arithmetic

    /// <summary>
    /// COMPLETE is decided by distinct uncut SLOTS filled, never by the file count. Two copies of
    /// one asset are one slot; a thousand misnamed files fill none.
    /// </summary>
    [Fact]
    public void Duplicate_and_unregistered_files_never_add_up_to_complete_coverage()
    {
        var flood = Enumerable
            .Range(0, 1200)
            .Select(i => new DeliveredAsset(
                ProvenanceFixtures.ArtId, $"art/copy{i}/chr_hero_body_idle.png", ".png"))
            .ToArray();

        var report = Run(flood, [ProvenanceFixtures.Midjourney()], [ProvenanceFixtures.ConfirmedLicence()]);

        report.DeliveredAssets.ShouldBe(1200);
        report.DeliveredAssets.ShouldBeGreaterThan(report.ActiveAssets);

        report.CoveredSlots.ShouldBe(1);
        report.VerifiedPairings.ShouldBe(1);
        report.Coverage.ShouldBe(DeliveryCoverage.Partial);
        report.Headline().ShouldStartWith("PARTIAL DELIVERY — 1 of 1040", Case.Sensitive);
    }

    /// <summary>
    /// The COMPLETE branch, over a register small enough to fill. Without a case here it is a state
    /// of the report nothing has ever produced.
    /// </summary>
    [Fact]
    public void Filling_every_uncut_slot_reports_complete()
    {
        var report = ProvenanceGate.Run(
            TinyRegister(),
            ProvenanceFixtures.StoreOf(
                [ProvenanceFixtures.Midjourney(), ProvenanceFixtures.Cc0()],
                [ProvenanceFixtures.ConfirmedLicence(), new ToolLicence("Audacity", "audio", true, "filed", "case")]),
            [ProvenanceFixtures.DeliveredArt(), ProvenanceFixtures.DeliveredAudio()],
            awaitingFirstDelivery: false);

        report.ActiveAssets.ShouldBe(2);
        report.CoveredSlots.ShouldBe(2);
        report.Coverage.ShouldBe(DeliveryCoverage.Complete);
        report.Headline().ShouldStartWith("COMPLETE — all 2 uncut asset slots", Case.Sensitive);

        // The floor still fires over a two-row register, which is the point of the floor.
        report.Violations.ShouldAllBe(v => v.Code == ViolationCode.ManifestFloor);
        report.Violations.Count.ShouldBeGreaterThan(0);
    }

    // ------------------------------------------------------------------ counts

    [Fact]
    public void The_report_states_the_register_it_quantified_over()
    {
        var report = Run([], [], ProvenanceFixtures.ShippedLicences);

        // Counted from the shipped register, not quoted.
        report.RegisteredAssets.ShouldBe(
            ProvenanceFixtures.Register.Art.Assets.Count + ProvenanceFixtures.Register.Audio.Assets.Count);
        report.RegisteredAssets.ShouldBe(1072);
        report.CutAssets.ShouldBe(32);
        report.ActiveAssets.ShouldBe(1040);
    }

    /// <summary>
    /// Runs the gate over the real register with a synthetic store and delivery set.
    /// </summary>
    /// <remarks>
    /// The declaration is derived from the delivery set the case supplies, so that a case about
    /// (say) a missing record does not also trip <see cref="ViolationCode.StaleDeliveryDeclaration"/>
    /// and make <c>ShouldHaveSingleItem</c> a lie.
    /// </remarks>
    private static ProvenanceGateReport Run(
        IReadOnlyList<DeliveredAsset> delivered,
        IReadOnlyList<ProvenanceRecord> records,
        IReadOnlyList<ToolLicence> licences) =>
        ProvenanceGate.Run(
            ProvenanceFixtures.Register,
            ProvenanceFixtures.StoreOf(records, licences),
            delivered,
            awaitingFirstDelivery: delivered.Count == 0);

    /// <summary>
    /// A register with two rows and nothing cut — the shape that would make every other rule in the
    /// gate pass over nothing.
    /// </summary>
    private static AssetManifestSet TinyRegister() => AssetManifestReader.LoadFrom(
        """
        {
          "_status": "a two-row register, for the floor case",
          "totals": { "claimedBySummaryTable": 2, "transcribed": 1, "cut": 0, "active": 1, "derived": 0 },
          "biomes": [], "rarities": [], "atlases": [], "sections": [], "discrepancies": [],
          "assets": [
            { "id": "chr_hero_body_idle", "section": "E2", "sourceSection": "15 §E2",
              "derived": false, "idSource": "doc" }
          ]
        }
        """,
        """
        {
          "_status": "a one-row register, for the floor case",
          "totals": { "claimedMusic": 1, "claimedSfx": 0, "claimedCombined": 1,
                      "transcribedMusic": 1, "transcribedSfx": 0, "transcribedCombined": 1,
                      "sfxWithoutDescriptor": 0 },
          "families": [], "discrepancies": [],
          "assets": [
            { "id": "mus_home", "kind": "mus", "family": "music", "sourceSection": "20 §3",
              "format": "ogg" }
          ]
        }
        """);
}
