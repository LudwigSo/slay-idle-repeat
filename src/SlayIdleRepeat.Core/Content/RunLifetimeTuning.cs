using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// How long a run stays live without an accepted run command of its own, read out of
/// <c>tuning/progression.json#/runLifetime</c>.
/// </summary>
/// <remarks>
/// The window slides from the run's own last accepted command, never the player's — a shop visit
/// mid-run must not keep a run alive. The server's configured run-row lifetime mirrors this number,
/// so a row cannot outlive or predecease the rule that ends it.
/// </remarks>
internal sealed class RunLifetimeTuning
{
    /// <summary>The document the window lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    /// <summary>The authored window, in whole hours. 48 as shipped.</summary>
    internal const string ExpiryHoursReference = DocumentPath + "#/runLifetime/expiryHours";

    private RunLifetimeTuning(TimeSpan window) => Window = window;

    /// <summary>How long a run may go untouched before it is over.</summary>
    internal TimeSpan Window { get; }

    /// <summary>Reads the window. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or the pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">The pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">The leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">The value is authorised but unusable.</exception>
    internal static RunLifetimeTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var hours = content.ReadInt64(ExpiryHoursReference);

        // A window of zero or less would expire every run at the instant it opened, which reads on
        // the wire as a game where no command is ever legal.
        if (hours <= 0 || hours > MaximumHours)
        {
            throw new InvalidTunableException(
                ExpiryHoursReference,
                "A run's window is a positive span a player could plausibly come back inside. " +
                "14 §16.3 authors 48 hours; this document authors " + Text(hours) + ".");
        }

        return new RunLifetimeTuning(TimeSpan.FromHours(hours));
    }

    /// <summary>A year, past which the window is a value nobody meant rather than a generous one.</summary>
    private const long MaximumHours = 24L * 365L;

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
