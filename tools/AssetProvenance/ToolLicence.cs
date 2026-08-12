namespace SlayIdleRepeat.AssetProvenance;

/// <summary>
/// One generation tool and whether its commercial terms have been confirmed in writing —
/// `15` §G <em>"Confirm the current commercial terms in writing before the first batch"</em> and
/// `20` §6 <em>"Commercial licence for each tool confirmed in writing"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <see cref="ConfirmedInWriting"/> is <c>bool?</c> and it is <b>unset</b> for every tool in the
/// shipped register. Absent means <em>nobody has confirmed anything</em>, and this assembly
/// contains no path that turns an absent value into <c>false</c>-that-reads-as-checked or into
/// <c>true</c>: <see cref="RequireConfirmedInWriting"/> throws, and
/// <see cref="ProvenanceGate"/> raises <see cref="ViolationCode.UnconfirmedLicence"/>. Steering
/// S6 — a hole stays a hole, greppable, and is never coerced at read time.
/// </para>
/// <para>
/// <b>M8-01b owns filling this in and no agent may.</b> The M8 kickoff (2026-08-12) recorded it as
/// ⛔ product-owner-owned: confirming a licence is a legal act, not an engineering task. What
/// engineering can do — and this is all of it — is make the unconfirmed state fail the build the
/// moment an asset generated with that tool is delivered.
/// </para>
/// </remarks>
/// <param name="Tool">The tool's key, matched ordinally against <see cref="ProvenanceRecord.ToolsNamed"/>.</param>
/// <param name="AppliesTo">What it generates: <c>art</c> or <c>audio</c>.</param>
/// <param name="ConfirmedInWriting">🔒 Null where nobody has confirmed. Never defaulted.</param>
/// <param name="ConfirmationRef">
/// Where the written confirmation is filed. Required the moment
/// <paramref name="ConfirmedInWriting"/> is true — a bare <c>true</c> with nothing behind it is
/// the claim `15` §G calls a formality.
/// </param>
/// <param name="Note">What is known today. Something a later reader can falsify.</param>
public sealed record ToolLicence(
    string Tool,
    string AppliesTo,
    bool? ConfirmedInWriting,
    string? ConfirmationRef,
    string Note)
{
    /// <summary>True only where the confirmation exists AND names where it is filed.</summary>
    public bool IsConfirmed => ConfirmedInWriting == true && !string.IsNullOrWhiteSpace(ConfirmationRef);

    /// <summary>
    /// The confirmation, or a loud failure. 🔒 There is no overload that takes a default: an unset
    /// licence coerced to "probably fine" is exactly the outcome `15` §G calls a legal risk.
    /// </summary>
    public string RequireConfirmedInWriting() =>
        IsConfirmed
            ? ConfirmationRef!
            : throw new InvalidOperationException(
                $"The commercial licence for '{Tool}' is not confirmed in writing " +
                $"(confirmedInWriting={Describe(ConfirmedInWriting)}, confirmationRef=" +
                $"{ConfirmationRef ?? "<absent>"}). 15 §G and 20 §6 make that confirmation a " +
                "prerequisite of shipping anything the tool generated. It is M8-01b, and it is " +
                "owned by the product owner — do not set it from an agent, and do not read an " +
                "absent value as consent.");

    private static string Describe(bool? value) =>
        value is null ? "<unset>" : value.Value ? "true" : "false";
}

/// <summary>The licence register: every generation tool the project may name in a record.</summary>
public sealed class ToolLicenceRegister
{
    private readonly Dictionary<string, ToolLicence> _byTool;

    /// <summary>Builds the register over its entries.</summary>
    /// <exception cref="ProvenanceFormatException">Two entries name the same tool.</exception>
    public ToolLicenceRegister(IReadOnlyList<ToolLicence> licences)
    {
        ArgumentNullException.ThrowIfNull(licences);

        _byTool = new Dictionary<string, ToolLicence>(StringComparer.Ordinal);
        foreach (var licence in licences)
        {
            if (!_byTool.TryAdd(licence.Tool, licence))
            {
                throw new ProvenanceFormatException(
                    ProvenanceStore.LicenceFileName,
                    $"declares the tool '{licence.Tool}' twice. Two rows for one tool means two " +
                    "answers to 'is this licence confirmed', and a lookup would silently take one.");
            }
        }

        Licences = licences;
    }

    /// <summary>Every entry, in file order.</summary>
    public IReadOnlyList<ToolLicence> Licences { get; }

    /// <summary>The entry for a tool, or null where the register does not know it.</summary>
    public ToolLicence? Find(string tool) => _byTool.GetValueOrDefault(tool);

    /// <summary>Every tool whose commercial terms are not confirmed in writing.</summary>
    public IEnumerable<ToolLicence> Unconfirmed => Licences.Where(l => !l.IsConfirmed);
}
