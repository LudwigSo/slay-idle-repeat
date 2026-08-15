using System.Globalization;
using System.Text;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core;

/// <summary>Everything ambient a rule might need, as data rather than a call.</summary>
/// <remarks>
/// <para>
/// Time, randomness, content, entitlement and flags are all values resolved once by the
/// composition root, not calls a rule can make mid-execution — the thing that keeps rules pure
/// and replayable. There is deliberately no <c>GameContext.Now()</c> or similar.
/// </para>
/// <para>
/// The guards live in the <c>init</c> accessors rather than property initializers, so they still
/// run on the <c>with</c> path (a synthesized copy constructor calls the <c>init</c> setters, not
/// the initializers, so <c>context with { Content = null! }</c> would otherwise slip through).
/// </para>
/// <para>
/// Equality here is shallow: <see cref="NowUtc"/> and <see cref="CommandSeed"/> compare by value,
/// but <see cref="Content"/>, <see cref="Core.Entitlements"/> and <see cref="Flags"/> compare by
/// reference. Two contexts describing the same ambience but built from separate instances are not
/// equal; nothing in the domain compares two contexts.
/// </para>
/// </remarks>
/// <param name="NowUtc">The instant this command is being applied at. Must carry a zero UTC offset.</param>
/// <param name="CommandSeed">
/// The server-issued per-command seed, present only for meta commands (<c>null</c> on every run
/// command). In-run draws never touch this — they derive from the run's own seed and persisted
/// per-stream counters. Meta draws (wheel spins, container opens, etc.) derive from this seed with
/// a counter that starts at 0 each command and is never persisted, since the command is atomic and
/// idempotency replays its stored outcome rather than re-rolling on resubmission.
/// </param>
/// <param name="Content">The loaded, validated, version-stamped content this command reads.</param>
/// <param name="Entitlements">
/// The subscription entitlement the server resolved. Read-only to the domain, and readable by
/// exactly one rule.
/// </param>
/// <param name="Flags">The kill switches, resolved at the composition root.</param>
public sealed record GameContext(
    DateTimeOffset NowUtc,
    ulong? CommandSeed,
    ContentSnapshot Content,
    Entitlements Entitlements,
    FeatureFlags Flags)
{
    private readonly DateTimeOffset _nowUtc = RequireUtc(NowUtc);
    private readonly ContentSnapshot _content = Require(Content, nameof(Content));
    private readonly Entitlements _entitlements = Require(Entitlements, nameof(Entitlements));
    private readonly FeatureFlags _flags = Require(Flags, nameof(Flags));

    /// <inheritdoc cref="GameContext(DateTimeOffset, ulong?, ContentSnapshot, Entitlements, FeatureFlags)"
    ///     path="/param[@name='NowUtc']"/>
    public DateTimeOffset NowUtc
    {
        get => _nowUtc;
        init => _nowUtc = RequireUtc(value);
    }

    /// <inheritdoc cref="GameContext(DateTimeOffset, ulong?, ContentSnapshot, Entitlements, FeatureFlags)"
    ///     path="/param[@name='Content']"/>
    public ContentSnapshot Content
    {
        get => _content;
        init => _content = Require(value, nameof(Content));
    }

    /// <inheritdoc cref="GameContext(DateTimeOffset, ulong?, ContentSnapshot, Entitlements, FeatureFlags)"
    ///     path="/param[@name='Entitlements']"/>
    public Entitlements Entitlements
    {
        get => _entitlements;
        init => _entitlements = Require(value, nameof(Entitlements));
    }

    /// <inheritdoc cref="GameContext(DateTimeOffset, ulong?, ContentSnapshot, Entitlements, FeatureFlags)"
    ///     path="/param[@name='Flags']"/>
    public FeatureFlags Flags
    {
        get => _flags;
        init => _flags = Require(value, nameof(Flags));
    }

    /// <summary>
    /// Renders the context with <see cref="CultureInfo.InvariantCulture"/> and the round-trip
    /// <c>"O"</c> format, so it reads the same regardless of the host's locale. Entitlements are
    /// deliberately not rendered; content is shown as its version stamp only.
    /// </summary>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Append(CultureInfo.InvariantCulture, $"NowUtc = {NowUtc:O}");
        builder.Append(CultureInfo.InvariantCulture, $", CommandSeed = {Describe(CommandSeed)}");
        builder.Append(CultureInfo.InvariantCulture, $", Content = {Content.Version.Short}");
        builder.Append(CultureInfo.InvariantCulture, $", Flags = {Flags.PvpEnabled}/{Flags.PlusOfferEnabled}");

        return true;
    }

    private static string Describe(ulong? commandSeed) =>
        commandSeed?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static T Require<T>(T value, string parameterName)
        where T : class =>
        value ?? throw new ArgumentNullException(parameterName);

    /// <summary>
    /// Refuses an instant not stated in UTC. Wall-clock rules read components off this value
    /// directly, so a non-zero offset would silently roll the game day early rather than fail loudly.
    /// </summary>
    private static DateTimeOffset RequireUtc(DateTimeOffset nowUtc) =>
        nowUtc.Offset == TimeSpan.Zero
            ? nowUtc
            // Matches Player.RequireZeroOffset, Run.RequireZeroOffset and VirtualClock.RequireStart
            // in throw type, so a host catching the family sees all four consistently.
            : throw new ArgumentOutOfRangeException(
                nameof(NowUtc),
                nowUtc,
                "NowUtc carries a non-zero UTC offset. The 05:00 UTC daily resets, event windows " +
                "and season boundaries read wall-clock components off this value, so a local-offset " +
                "instant rolls the game day early even though it names the same moment. The " +
                "composition root passes IClockPort's UTC answer through unchanged (30 §3).");
}
