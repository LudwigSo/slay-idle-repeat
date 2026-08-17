using SlayIdleRepeat.Core.Content.Dice;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>One face kind, as the panel lists it.</summary>
/// <param name="Kind">The kind, from the rules layer's own public vocabulary.</param>
/// <param name="Name">Its name, already resolved.</param>
/// <param name="Effect">What it does, already resolved — its identity, never its magnitude.</param>
public sealed record DieFaceListing(DieFaceKind Kind, string Name, string Effect);

/// <summary>
/// Drives the Die Panel: what the six face kinds are and what each does.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so it runs under a test runner with
/// no engine anywhere near it.
/// </para>
/// <para>
/// 🔴 <b>It lists the vocabulary, not the player's die, and the difference is the whole honesty of
/// this screen.</b> The design asks for the current six faces with the source that granted each —
/// "a hidden die is a hostile die". No client can supply that: the type that composes a die from
/// talents, mounts, drafted perks, forge upgrades and curses is internal to the rules layer, is
/// documented there as not wired to a run at all, and neither the player row nor the run row carries
/// a single face. So the panel says what each kind of face does, shows the faces the last roll
/// actually reported, and says in words that the slot-by-slot composition is not available — see
/// <see cref="TheComposedDieIsNotReadableHere"/>. Six generic faces drawn as though they were the
/// player's own would be the hostile die with extra steps.
/// </para>
/// <para>
/// 🔒 <b>No effect line carries a number.</b> How far each face moves, how much a surge heals, how
/// many times a chain may chain and what a fortune multiplies are all tunable values owned by the
/// rules layer, and a copy of any of them in a translated string could not be tuned with the
/// original. The lines state what does not move.
/// </para>
/// <para>
/// ⚠️ Absent because the run does not carry them: each face's tier, which slot each face occupies,
/// which source granted it, and the fan layout the design describes for a long press — the first
/// three because they are unreadable, and the fan because a fan of six faces nobody can identify is
/// a decoration rather than a disclosure.
/// </para>
/// </remarks>
public sealed class DiePanelPresenter
{
    /// <summary>
    /// ⚠️ Deliberately not reconstructed, and named so it can be found. Every route to the player's
    /// actual die is closed to a client, and the one type that would compute it is not connected to
    /// a run in the first place. Transcribing today's answer — that the die is six ordinary pip
    /// faces, because every upgrade source is still unbuilt — would put a true-today constant in
    /// front of the player that goes silently wrong on the commit that wires the first talent.
    /// </summary>
    private const string TheComposedDieIsNotReadableHere =
        "The composer that would apply talents, mounts, perks, forge upgrades and curses to a base " +
        "die is internal to the rules assembly, and its own remarks record that nothing calls it " +
        "with real data yet. Neither PlayerSnapshot nor RunSnapshot carries a face, a tier or a " +
        "slot. So this panel cannot say which face sits in which slot or where it came from, and " +
        "it says so rather than drawing a die the player may not have.";

    private const string TitleKey = "loc.die_panel.title.name";
    private const string LastFaceLabelKey = "loc.die_panel.last_face.label";
    private const string NoRollYetStatusKey = "loc.die_panel.no_roll_yet.status";
    private const string FacesUnavailableStatusKey = "loc.die_panel.faces_unavailable.status";

    /// <summary>
    /// Every face kind the game defines, each with the two keys the panel draws it from.
    /// </summary>
    /// <remarks>
    /// 🔒 Built from the enum's own members rather than from a list written here, so a face kind
    /// added to the game arrives in this panel by itself. What it arrives without is a caption: the
    /// key it would need is absent, the resolver answers with the key text, and the panel shows a
    /// visibly wrong string rather than silently omitting a face the player can roll. That is the
    /// intended failure direction — a missing line is louder than a missing row.
    /// </remarks>
    private static readonly DieFaceKind[] AllKinds = Enum.GetValues<DieFaceKind>();

    private readonly LocaleStringCatalogue _strings;

    /// <summary>Builds the panel over the strings it draws.</summary>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="strings"/> is null.</exception>
    public DiePanelPresenter(LocaleStringCatalogue strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        _strings = strings;
    }

    /// <summary>The panel's heading, resolved.</summary>
    public string Title => _strings.Resolve(TitleKey);

    /// <summary>The caption over the faces the last roll reported, resolved.</summary>
    public string LastFaceLabel => _strings.Resolve(LastFaceLabelKey);

    /// <summary>The line shown before any roll has been observed, resolved.</summary>
    public string NoRollYetStatus => _strings.Resolve(NoRollYetStatusKey);

    /// <summary>The panel's admission that the composed die is not readable, resolved.</summary>
    /// <remarks>
    /// A permanent line rather than one shown on a failure. Nothing about this build makes the die
    /// readable on a good day, so a line that came and went would suggest a state where it does.
    /// </remarks>
    public string FacesUnavailableStatus => _strings.Resolve(FacesUnavailableStatusKey);

    /// <summary>Every face kind, in the order the vocabulary declares them, resolved.</summary>
    public IReadOnlyList<DieFaceListing> Faces =>
        [.. AllKinds.Select(kind => new DieFaceListing(kind, NameOf(kind), EffectOf(kind)))];

    /// <summary>One face kind's name, resolved.</summary>
    /// <param name="kind">The kind to name.</param>
    public string NameOf(DieFaceKind kind) => _strings.Resolve(KeyFor(kind, "name"));

    /// <summary>What one face kind does, resolved.</summary>
    /// <param name="kind">The kind to describe.</param>
    public string EffectOf(DieFaceKind kind) => _strings.Resolve(KeyFor(kind, "effect"));

    /// <remarks>
    /// The kind's own name, lowercased, is the key segment — which is what lets a face kind added to
    /// the enum find its two strings without a second table here to keep in step.
    /// </remarks>
    private static string KeyFor(DieFaceKind kind, string field) =>
        $"loc.die_panel.{kind.ToString().ToLowerInvariant()}.{field}";
}
