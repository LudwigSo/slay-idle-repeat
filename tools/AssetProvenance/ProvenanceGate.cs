using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetProvenance;

/// <summary>What a gate failure is. Pinning the code is how a test asserts WHICH rule fired.</summary>
public enum ViolationCode
{
    /// <summary>The register is smaller than its floor, or a canary id has vanished from it.</summary>
    ManifestFloor,

    /// <summary>An asset is delivered and no provenance record names it.</summary>
    MissingProvenance,

    /// <summary>A record names an asset id the register does not contain.</summary>
    UnknownAsset,

    /// <summary>A file is delivered whose stem is no register id at all.</summary>
    UnregisteredDelivery,

    /// <summary>A record names an asset a ruling has cut.</summary>
    CutAsset,

    /// <summary>A delivered file's format disagrees with its register's medium.</summary>
    MediumMismatch,

    /// <summary>A record's own fields are wrong — see <see cref="RecordValidator"/>.</summary>
    MalformedRecord,

    /// <summary>A record names a tool the licence register does not know.</summary>
    UnknownTool,

    /// <summary>A record names a tool whose commercial licence is not confirmed in writing.</summary>
    UnconfirmedLicence,

    /// <summary>A record names a tool licensed for a different medium than the asset's.</summary>
    LicenceScopeMismatch,

    /// <summary>The delivery declaration and the delivery set disagree.</summary>
    StaleDeliveryDeclaration,
}

/// <summary>One gate failure.</summary>
/// <param name="Code">Which rule fired.</param>
/// <param name="Subject">The asset id, file or tool it fired on.</param>
/// <param name="Detail">What is wrong and what to do about it.</param>
public sealed record ProvenanceViolation(ViolationCode Code, string Subject, string Detail)
{
    /// <inheritdoc/>
    public override string ToString() => $"[{Code}] {Subject}: {Detail}";
}

/// <summary>How much of the register has actually been delivered.</summary>
/// <remarks>Three states, not a boolean, so "nothing is delivered" can never read the same as "everything is delivered and verified".</remarks>
public enum DeliveryCoverage
{
    /// <summary>Zero assets delivered. Declared by <see cref="DeliveryDeclaration"/>.</summary>
    AwaitingFirstDelivery,

    /// <summary>Some, but not all, of the uncut register is delivered.</summary>
    Partial,

    /// <summary>Every uncut slot in the register is delivered.</summary>
    Complete,
}

/// <summary>The gate's result: the counts it quantified over, and every violation it found.</summary>
public sealed record ProvenanceGateReport(
    int RegisteredAssets,
    int ActiveAssets,
    int CutAssets,
    int DeliveredAssets,
    int CoveredSlots,
    int Records,
    int VerifiedPairings,
    DeliveryCoverage Coverage,
    bool DeclaredAwaitingFirstDelivery,
    IReadOnlyList<ProvenanceViolation> Violations)
{
    /// <summary>True when the gate found nothing wrong.</summary>
    public bool Passed => Violations.Count == 0;

    /// <summary>
    /// The one-line verdict, written so that the zero-delivery state cannot be skim-read as a pass
    /// over a populated one.
    /// </summary>
    /// <remarks>
    /// <see cref="DeliveredAssets"/> counts FILES; <see cref="CoveredSlots"/> counts distinct
    /// uncut register slots those files fill. Only the second may be compared with
    /// <see cref="ActiveAssets"/> — a thousand copies of one asset must never let this say COMPLETE.
    /// </remarks>
    public string Headline() => Coverage switch
    {
        DeliveryCoverage.AwaitingFirstDelivery =>
            $"AWAITING FIRST DELIVERY — 0 of {ActiveAssets} uncut asset slots are delivered, so " +
            $"0 asset-to-record pairings were verified. What DID run: the register floor " +
            $"({RegisteredAssets} rows), and the reverse direction over {Records} record(s). " +
            (DeclaredAwaitingFirstDelivery
                ? $"Declared by DeliveryDeclaration (turns on {DeliveryDeclaration.TurnsOn})."
                : "⚠️ NOT declared: DeliveryDeclaration.AwaitingFirstDelivery is false while nothing " +
                  "is delivered, which is itself a violation above."),
        DeliveryCoverage.Partial =>
            $"PARTIAL DELIVERY — {CoveredSlots} of {ActiveAssets} uncut asset slots are delivered " +
            $"across {DeliveredAssets} file(s); {VerifiedPairings} asset-to-record pairing(s) verified.",
        _ =>
            $"COMPLETE — all {ActiveAssets} uncut asset slots are delivered across " +
            $"{DeliveredAssets} file(s); {VerifiedPairings} asset-to-record pairing(s) verified.",
    };
}

/// <summary>The gate: no delivered asset without a provenance record, and no provenance record for an unknown asset id.</summary>
/// <remarks>
/// <para>
/// Both directions, deliberately: a forward-only gate is silent about a record for an asset that
/// was renamed, cut or never existed, and a record whose asset id resolves to nothing is a licence
/// claim about nothing.
/// </para>
/// <para>
/// Cut assets are handled explicitly rather than lumped in with "unknown". An unknown id is a typo
/// or a rename; a cut id is provenance for work that was called off — they mean different things.
/// </para>
/// </remarks>
public static class ProvenanceGate
{
    /// <summary>The floor on the subject set, sitting below the register's real size so an ordinary correction does not trip it, and well above zero so a register that failed to load cannot pass every rule below.</summary>
    public const int MinimumRegisteredAssets = 1000;

    /// <summary>Named members the register must still contain — a count alone is satisfied by a thousand rows of anything; these are ids the design docs name literally.</summary>
    public static IReadOnlyList<string> CanaryAssetIds { get; } =
    [
        "chr_hero_body_idle",
        "ui_panel_main_9slice",
        "tile_icon_treasure",
        "mus_home",
        "sfx_crit",
    ];

    /// <summary>A cut asset that must still be present-and-cut, so <see cref="ViolationCode.CutAsset"/> keeps having a subject.</summary>
    public const string CanaryCutAssetId = "vfx_bleed_loop_sheet";

    /// <summary>Where the register lives, for a failure message a reader can act on.</summary>
    /// <remarks>
    /// Qualified with <c>game-data/</c>: <see cref="AssetManifestReader.AssetsDirectory"/> and
    /// <see cref="DeliveredAssets.AssetsDirectory"/> are both the bare string <c>assets</c> but mean
    /// different directories.
    /// </remarks>
    public const string RegisterLocation = "game-data/" + AssetManifestReader.AssetsDirectory;

    /// <summary>Runs the gate.</summary>
    /// <param name="manifest">The register — the single source of the asset-id vocabulary.</param>
    /// <param name="store">The provenance store.</param>
    /// <param name="delivered">The delivery set, from <see cref="DeliveredAssets.Scan"/>.</param>
    /// <param name="awaitingFirstDelivery">
    /// The declared delivery state. Defaults to <see cref="DeliveryDeclaration.AwaitingFirstDelivery"/>;
    /// taken as a parameter only so tests can drive the declaration in both directions.
    /// </param>
    public static ProvenanceGateReport Run(
        AssetManifestSet manifest,
        ProvenanceRecordSet store,
        IReadOnlyList<DeliveredAsset> delivered,
        bool awaitingFirstDelivery = DeliveryDeclaration.AwaitingFirstDelivery)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(delivered);

        var violations = new List<ProvenanceViolation>();

        var cutIds = manifest.CutArt.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var registeredIds = manifest.AllIds.ToHashSet(StringComparer.Ordinal);
        var activeCount = registeredIds.Count - cutIds.Count;

        CheckRegisterFloor(violations, manifest, registeredIds, cutIds);
        CheckRecords(violations, manifest, store, cutIds);
        var (covered, paired) = CheckDeliveries(violations, manifest, store, delivered, cutIds);
        var coverage = CheckDeclaration(
            violations, awaitingFirstDelivery, delivered.Count, covered.Count, activeCount);

        return new ProvenanceGateReport(
            registeredIds.Count,
            activeCount,
            cutIds.Count,
            delivered.Count,
            covered.Count,
            store.Records.Count,
            paired,
            coverage,
            awaitingFirstDelivery,
            violations);
    }

    private static void CheckRegisterFloor(
        List<ProvenanceViolation> violations,
        AssetManifestSet manifest,
        HashSet<string> registeredIds,
        HashSet<string> cutIds)
    {
        if (registeredIds.Count < MinimumRegisteredAssets)
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.ManifestFloor,
                RegisterLocation,
                $"the register holds {registeredIds.Count} asset ids, below the floor of " +
                $"{MinimumRegisteredAssets}. Every other rule in this gate quantifies over it, so " +
                "a register that shrank to nothing would take the whole check green with it " +
                "instead of red."));
        }

        foreach (var canary in CanaryAssetIds.Where(id => !registeredIds.Contains(id)))
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.ManifestFloor,
                canary,
                "is named by 15 §D1 / 20 §3 and is no longer in the register. Either the id " +
                "changed — in which case fix this list and every provenance record keyed to it — " +
                "or the register is no longer the one this gate believes it is reading."));
        }

        if (!cutIds.Contains(CanaryCutAssetId))
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.ManifestFloor,
                CanaryCutAssetId,
                $"is not marked cut in the register. The O8 ruling (M8 kickoff, 2026-08-12) cut all " +
                $"32 of 15 §E19's sprite sheets; while none is cut, the {ViolationCode.CutAsset} " +
                "rule below has no subject and can never fire. Manifest reports " +
                $"{manifest.CutArt.Count()} cut art rows."));
        }
    }

    /// <summary>The reverse direction, plus the record's own contents and its tool's licence.</summary>
    private static void CheckRecords(
        List<ProvenanceViolation> violations,
        AssetManifestSet manifest,
        ProvenanceRecordSet store,
        HashSet<string> cutIds)
    {
        foreach (var record in store.Records)
        {
            var medium = MediumOf(manifest, record.AssetId);

            if (medium is null)
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.UnknownAsset,
                    record.AssetId,
                    "has a provenance record, and M8-09's register has no such asset id. A record " +
                    "keyed to nothing is a licence claim about nothing: it satisfies no QA item in " +
                    "20 §6 and it hides a rename. Fix the id, or delete the record with the asset."));
                continue;
            }

            if (cutIds.Contains(record.AssetId))
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.CutAsset,
                    record.AssetId,
                    "has a provenance record and a ruling has cut it — " +
                    $"'{manifest.RequireArt(record.AssetId).Cut}'. A cut asset is never generated " +
                    "and never delivered, so it needs no record; one here is either work that " +
                    "should not have happened or a record that outlived its ruling."));
            }

            foreach (var problem in RecordValidator.Validate(record, medium.Value))
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.MalformedRecord, record.AssetId, problem));
            }

            CheckLicences(violations, store, record, medium.Value);
        }
    }

    /// <summary>Commercial licence for each named tool, confirmed in writing.</summary>
    private static void CheckLicences(
        List<ProvenanceViolation> violations,
        ProvenanceRecordSet store,
        ProvenanceRecord record,
        AssetMedium medium)
    {
        foreach (var tool in record.ToolsNamed)
        {
            var licence = store.Licences.Find(tool);

            if (licence is null)
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.UnknownTool,
                    record.AssetId,
                    $"names the tool '{tool}', which {ProvenanceStore.LicenceFileName} does not " +
                    "declare. 20 §6 asks for the commercial licence of EACH tool in writing; a " +
                    "tool the register has never heard of has no licence status at all."));
                continue;
            }

            // A licence is scoped by medium: an art confirmation is not evidence about audio, and
            // an audio record naming a tool licensed only for art must not ride in on that confirmation.
            var expected = medium == AssetMedium.Art ? "art" : "audio";
            if (!string.Equals(licence.AppliesTo, expected, StringComparison.Ordinal))
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.LicenceScopeMismatch,
                    record.AssetId,
                    $"is {expected} and names the tool '{tool}', whose licence entry applies to " +
                    $"'{licence.AppliesTo}'. A commercial confirmation covers what it covers; one " +
                    "obtained for art is not evidence about audio, and 20 §6 asks for each tool's " +
                    "terms rather than for a general permission."));
                continue;
            }

            if (!licence.IsConfirmed)
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.UnconfirmedLicence,
                    record.AssetId,
                    $"was produced with '{tool}', whose commercial terms are not confirmed in " +
                    $"writing (confirmedInWriting is {(licence.ConfirmedInWriting?.ToString() ?? "unset")}, " +
                    $"confirmationRef is {licence.ConfirmationRef ?? "unset"}). 15 §G: \"Confirm " +
                    "the current commercial terms in writing before the first batch … Legal " +
                    "prerequisite, not a formality.\" That confirmation is M8-01b and the product " +
                    "owner owns it; it is not something to set from code to clear this message."));
            }
        }
    }

    /// <summary>
    /// The forward direction: every delivered asset carries a record, and every delivered file is
    /// something the register knows about.
    /// </summary>
    /// <returns>
    /// The distinct uncut register slots the delivery set fills, and how many of those slots have a
    /// provenance record — both counted over distinct asset ids rather than over files, so two
    /// copies of one asset are one slot and a misnamed file fills none.
    /// </returns>
    private static (HashSet<string> Covered, int Paired) CheckDeliveries(
        List<ProvenanceViolation> violations,
        AssetManifestSet manifest,
        ProvenanceRecordSet store,
        IReadOnlyList<DeliveredAsset> delivered,
        HashSet<string> cutIds)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var paired = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in delivered)
        {
            var medium = MediumOf(manifest, asset.Id);

            if (medium is null)
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.UnregisteredDelivery,
                    asset.RelativePath,
                    $"is delivered and its stem '{asset.Id}' is in neither register. 15 §D1 makes " +
                    "the file name the asset id; a delivered file that answers to no slot is " +
                    "either misnamed or an asset nobody planned, and in both cases the manifest " +
                    "totals it is measured against are now wrong."));
                continue;
            }

            // A DELIVERED cut asset is reported here rather than as a missing record — demanding
            // provenance for it would contradict the reverse direction, which refuses a record for
            // a cut asset. The problem is the delivery, not the paperwork.
            if (cutIds.Contains(asset.Id))
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.CutAsset,
                    asset.RelativePath,
                    $"is delivered and a ruling has cut '{asset.Id}' — " +
                    $"'{manifest.RequireArt(asset.Id).Cut}'. It needs no provenance record because " +
                    "it should not exist; remove the file, or reverse the ruling in the register " +
                    "first."));
                continue;
            }

            CheckDeliveredFormat(violations, manifest, asset, medium.Value);
            covered.Add(asset.Id);

            if (store.Find(asset.Id) is null)
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.MissingProvenance,
                    asset.RelativePath,
                    $"is delivered and no provenance record exists for '{asset.Id}'. 15 §G and 20 " +
                    $"§2.1 require one for EVERY generated asset; write " +
                    $"{ProvenanceStore.StoreDirectory}/{ProvenanceStore.RecordsDirectoryName}/" +
                    $"{asset.Id}{ProvenanceStore.RecordExtension} — " +
                    "`dotnet run --project tools/AssetProvenance -- template " +
                    $"{asset.Id} <kind>` prints the shape."));
                continue;
            }

            paired.Add(asset.Id);
        }

        return (covered, paired.Count);
    }

    /// <summary>The delivered file's format against what the register says the slot is.</summary>
    /// <remarks>
    /// The music rule reads <see cref="AudioAsset.IsMusic"/> rather than re-deriving it from the id
    /// prefix — the register is the single vocabulary for that distinction.
    /// </remarks>
    private static void CheckDeliveredFormat(
        List<ProvenanceViolation> violations,
        AssetManifestSet manifest,
        DeliveredAsset asset,
        AssetMedium medium)
    {
        if (medium == AssetMedium.Art)
        {
            if (asset.Extension != ".png")
            {
                violations.Add(new ProvenanceViolation(
                    ViolationCode.MediumMismatch,
                    asset.RelativePath,
                    $"is delivered as '{asset.Extension}' and '{asset.Id}' is an ART slot; 15 §D1 " +
                    "delivers art as .png."));
            }

            return;
        }

        if (asset.Extension == ".png")
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.MediumMismatch,
                asset.RelativePath,
                $"is delivered as '{asset.Extension}' and '{asset.Id}' is an AUDIO slot; 20 §5 " +
                "delivers OGG (music, shipped SFX) and WAV (SFX source)."));
            return;
        }

        if (manifest.RequireAudio(asset.Id).IsMusic && asset.Extension != ".ogg")
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.MediumMismatch,
                asset.RelativePath,
                $"is delivered as '{asset.Extension}' and '{asset.Id}' is a MUSIC track; 20 §5 " +
                "gives music OGG Vorbis only. WAV is the SFX source format, not a music one."));
        }
    }

    private static DeliveryCoverage CheckDeclaration(
        List<ProvenanceViolation> violations,
        bool awaitingFirstDelivery,
        int deliveredCount,
        int coveredCount,
        int activeCount)
    {
        if (awaitingFirstDelivery && deliveredCount > 0)
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.StaleDeliveryDeclaration,
                nameof(DeliveryDeclaration),
                $"declares AwaitingFirstDelivery = true, and {deliveredCount} asset(s) are " +
                $"delivered under {DeliveredAssets.AssetsDirectory}/. The declaration is what " +
                "stops an empty delivery set reading as a clean pass, and it has now stopped " +
                "being true. Set AwaitingFirstDelivery to false in the same commit as the first " +
                "delivered asset, and update the cases that pin today's coverage state."));
        }

        if (!awaitingFirstDelivery && deliveredCount == 0)
        {
            violations.Add(new ProvenanceViolation(
                ViolationCode.StaleDeliveryDeclaration,
                nameof(DeliveryDeclaration),
                "declares AwaitingFirstDelivery = false, and nothing is delivered. A declaration " +
                "that can be pre-armed is not a declaration: flipped early it would silence the " +
                "other direction for as long as the delivery set stayed empty. Set it back to " +
                "true, or deliver the asset the commit that flipped it was for."));
        }

        // COMPLETE is decided by covered SLOTS, never by the file count; only "nothing at all was
        // delivered" may be read off the file count, since zero files is zero slots either way.
        return deliveredCount switch
        {
            0 => DeliveryCoverage.AwaitingFirstDelivery,
            _ when coveredCount >= activeCount => DeliveryCoverage.Complete,
            _ => DeliveryCoverage.Partial,
        };
    }

    /// <summary>Which register an id belongs to, or null where neither does.</summary>
    /// <remarks>Resolved through <see cref="AssetManifestSet"/> rather than by looking at the id's prefix, since the register is the single vocabulary.</remarks>
    private static AssetMedium? MediumOf(AssetManifestSet manifest, string assetId) =>
        manifest.FindArt(assetId) is not null ? AssetMedium.Art
        : manifest.FindAudio(assetId) is not null ? AssetMedium.Audio
        : null;
}
