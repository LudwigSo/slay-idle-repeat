namespace SlayIdleRepeat.Adapters.Analytics.PostHog;

/// <summary>
/// The adapter's configuration, bound from the <c>PostHog</c> section — key names here ARE the
/// deployment contract (<c>PostHog__Enabled</c> etc. in the compose file and infra/README.md).
/// </summary>
public sealed class PostHogOptions
{
    /// <summary>Whether events are sent at all. Off by default: a deployment opts in, never out.</summary>
    public bool Enabled { get; set; }

    /// <summary>The instance's base address, e.g. <c>https://eu.posthog.com</c>.</summary>
    public string Host { get; set; } = "";

    /// <summary>The project's write-only API key.</summary>
    public string ProjectKey { get; set; } = "";
}
