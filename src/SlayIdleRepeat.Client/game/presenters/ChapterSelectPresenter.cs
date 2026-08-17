using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>One chapter the content set authors, ready to draw.</summary>
/// <param name="ChapterId">The chapter number the chapter document declares.</param>
/// <param name="DisplayName">The chapter's name, already resolved out of the locale.</param>
public sealed record ChapterListing(int ChapterId, string DisplayName);

/// <summary>
/// One requirement a (chapter, tier) pair has not met.
/// </summary>
/// <remarks>
/// 🔒 A closed hierarchy rather than a reason enum, because the identity of a refusal is its
/// <em>payload</em>: "you must clear chapter 1 on Normal" and "you must clear chapter 2 on Normal"
/// are different instructions to the player, and three enum names carrying no chapter and no tier
/// could be swapped by a wrong-branch bug without any test noticing.
/// </remarks>
public abstract record ChapterTierRequirement;

/// <summary>A clear that has not happened yet.</summary>
/// <param name="ChapterId">The chapter that must be cleared.</param>
/// <param name="Tier">The tier it must be cleared on.</param>
public sealed record ClearRequirement(int ChapterId, DifficultyTier Tier) : ChapterTierRequirement;

/// <summary>A Legend Level that has not been reached.</summary>
/// <param name="Required">The level the authored ladder demands.</param>
/// <param name="Actual">The level the player is at.</param>
public sealed record LegendLevelRequirement(int Required, int Actual) : ChapterTierRequirement;

/// <summary>What a lookup for one (chapter, tier) pair found.</summary>
public enum ChapterTierLookup
{
    /// <summary>Every authored requirement is met; confirming it starts a run.</summary>
    Selectable = 1,

    /// <summary>The chapter exists and at least one requirement is unmet.</summary>
    Blocked = 2,

    /// <summary>
    /// The content set authors no such chapter. Distinct from <see cref="Blocked"/> on purpose: a
    /// chapter nobody has written yet is not a chapter the player can work towards.
    /// </summary>
    NotAuthored = 3,
}

/// <summary>What the screen may do with one (chapter, tier) pair, and why not when it may not.</summary>
/// <param name="Lookup">Selectable, blocked, or not authored at all.</param>
/// <param name="Unmet">
/// Every unmet requirement, not the first one found. Mythic can be blocked by a missing clear
/// <em>and</em> by Legend Level at the same time, and a screen that showed only one of them would
/// send the player to do half the work and come back to the same locked button.
/// </param>
public sealed record ChapterTierAvailability(
    ChapterTierLookup Lookup,
    IReadOnlyList<ChapterTierRequirement> Unmet);

/// <summary>What a confirmed selection did.</summary>
public enum ChapterSelectSubmission
{
    /// <summary><c>START_RUN</c> went to the host.</summary>
    Submitted = 1,

    /// <summary>Nothing was submitted, because the pair is not selectable.</summary>
    RefusedNotSelectable = 2,

    /// <summary>Nothing was submitted, because there is no player state to start a run for.</summary>
    RefusedProfileUnavailable = 3,
}

/// <summary>How far the Chapter Select screen has got with the read its gating depends on.</summary>
public enum ChapterSelectStage
{
    /// <summary>The read has not happened yet. The state a freshly built presenter reports.</summary>
    NotYetRead = 1,

    /// <summary>The player's Legend Level and clear history are known.</summary>
    Ready = 2,

    /// <summary>Nothing is stored for this player.</summary>
    ProfileMissing = 3,

    /// <summary>The read itself did not answer. A state, never an escape.</summary>
    ReadUnavailable = 4,
}

/// <summary>
/// Drives the Chapter Select screen: which chapters exist, which (chapter, tier) pairs the player
/// may play, and the <c>START_RUN</c> that a confirmed choice submits.
/// </summary>
/// <remarks>
/// <para>
/// Plain C# taking its collaborators as constructor arguments, so the whole screen runs under a test
/// runner with no engine anywhere near it.
/// </para>
/// <para>
/// 🔒 <b>The ladder is authored data, never a constant here.</b> Which clear each tier demands and
/// which Legend Level Mythic demands are read from <c>tuning/progression.json#/chapterGating</c> on
/// every lookup. A number transcribed into this file would be a balance decision that a tuning edit
/// could no longer move.
/// </para>
/// <para>
/// ⚠️ <b>This gate is presentation only.</b> The rules layer refuses a <c>START_RUN</c> only for a
/// chapter id below one or an undefined tier; nothing server-side enforces the clear ladder or the
/// Legend Level, and no task currently owns making it do so. A client that skipped this screen could
/// start any chapter on any tier.
/// </para>
/// <para>
/// 🔒 There is deliberately no par-power, expected-power or power-warning member. The power model
/// belongs to the row that owns it, and a comparison invented here would be a second answer to a
/// question that already has one owner.
/// </para>
/// </remarks>
public sealed class ChapterSelectPresenter
{
    /// <summary>Builds the screen over the host, the strings, the content set and the profile.</summary>
    /// <param name="gameHost">The seam the player's state is read through and the run is started through.</param>
    /// <param name="strings">Key to display string, over the loaded content set.</param>
    /// <param name="content">The loaded content set the chapters and the gating ladder are read from.</param>
    /// <param name="player">The profile this screen is about.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public ChapterSelectPresenter(
        IGameHost gameHost,
        LocaleStringCatalogue strings,
        ContentSnapshot content,
        PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(gameHost);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(content);

        _ = player;
    }

    /// <summary>Every chapter the content set authors, in ascending id order.</summary>
    public IReadOnlyList<ChapterListing> Chapters => throw NotBuilt();

    /// <summary>How far the read the gating depends on has got.</summary>
    public ChapterSelectStage Stage => throw NotBuilt();

    /// <summary>The screen's heading, resolved.</summary>
    public string Title => throw NotBuilt();

    /// <summary>The confirm control's caption, resolved.</summary>
    public string ConfirmText => throw NotBuilt();

    /// <summary>The caption shown against an unmet <see cref="ClearRequirement"/>, resolved.</summary>
    public string RequiresClearCaption => throw NotBuilt();

    /// <summary>The caption shown against an unmet <see cref="LegendLevelRequirement"/>, resolved.</summary>
    public string RequiresLegendLevelCaption => throw NotBuilt();

    /// <summary>A difficulty tier's name, resolved.</summary>
    /// <param name="tier">The tier to name.</param>
    public string TierName(DifficultyTier tier)
    {
        _ = tier;

        throw NotBuilt();
    }

    /// <summary>What the player may do with one (chapter, tier) pair.</summary>
    /// <param name="chapterId">The chapter asked about.</param>
    /// <param name="tier">The tier asked about.</param>
    public ChapterTierAvailability Availability(int chapterId, DifficultyTier tier)
    {
        _ = chapterId;
        _ = tier;

        throw NotBuilt();
    }

    /// <summary>Reads the player's own state, which is what the gating is decided against.</summary>
    /// <param name="ct">Cancellation.</param>
    public Task StartAsync(CancellationToken ct)
    {
        _ = ct;

        throw NotBuilt();
    }

    /// <summary>Submits <c>START_RUN</c> for a selectable pair, and nothing at all for any other.</summary>
    /// <param name="chapterId">The chosen chapter.</param>
    /// <param name="tier">The chosen tier.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<ChapterSelectSubmission> ConfirmAsync(int chapterId, DifficultyTier tier, CancellationToken ct)
    {
        _ = chapterId;
        _ = tier;
        _ = ct;

        throw NotBuilt();
    }

    private static NotImplementedException NotBuilt() =>
        new("ChapterSelectPresenter is a declaration-only stub: the tests that describe it are written, the behaviour is not.");
}
