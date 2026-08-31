namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// A content-distribution exchange that did not produce bytes the caller can use.
/// </summary>
/// <remarks>
/// One kind rather than several: the sync presenter above turns every non-cancellation fault into
/// the same named <c>Unreachable</c> failure, so a second exception type here would be a
/// distinction nothing downstream can act on.
/// </remarks>
public sealed class ContentDistributionException : Exception
{
    /// <summary>States what went wrong, in terms of the exchange rather than of the transport.</summary>
    /// <param name="message">What went wrong.</param>
    public ContentDistributionException(string message)
        : base(message)
    {
    }

    /// <summary>…and carries the transport failure underneath it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The transport failure.</param>
    public ContentDistributionException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
