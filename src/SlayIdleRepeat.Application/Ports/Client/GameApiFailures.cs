namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>The exchange did not happen: the transport could not answer, or the server could not answer yet.</summary>
/// <remarks>
/// <para>
/// This is the one the connection-state machinery reads as "the connection is lost". It covers a
/// transport that never reached anyone, a request that timed out, a 429, and every 5xx — all the
/// states in which the same request, sent again later, could still succeed.
/// </para>
/// <para>
/// It is deliberately NOT what a refused command produces: a rejection rides HTTP 200 and is a
/// return value. Folding the two together would put "you cannot afford this" and "the network is
/// down" behind one catch block, and the retry that is right for one is wrong for the other.
/// </para>
/// </remarks>
public sealed class GameApiUnavailableException : Exception
{
    /// <summary>How long the server asked to be left alone for, when it said so.</summary>
    /// <param name="message">What could not be reached.</param>
    /// <param name="retryAfter">The server's own <c>Retry-After</c>, or <c>null</c> when it named none.</param>
    /// <param name="inner">The transport failure underneath, when there was one.</param>
    public GameApiUnavailableException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner) =>
        RetryAfter = retryAfter;

    /// <summary>
    /// How long to wait before trying again, or <c>null</c> when the server named no interval and
    /// the caller's own backoff decides.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>The server answered, understood, and refused the request itself.</summary>
/// <remarks>
/// A body it could not read, a session it will not open, a run it will not show. Retrying the same
/// request unchanged cannot help, which is the whole distinction from
/// <see cref="GameApiUnavailableException"/> — and it is why this one carries the status code: the
/// repair depends on which refusal it was, and a caller that cannot tell them apart will retry all
/// of them or none.
/// </remarks>
public sealed class GameApiRefusedException : Exception
{
    /// <summary>Builds the refusal.</summary>
    /// <param name="statusCode">The status the server answered with.</param>
    /// <param name="message">What it refused.</param>
    public GameApiRefusedException(int statusCode, string message)
        : base(message) =>
        StatusCode = statusCode;

    /// <summary>The status the server answered with.</summary>
    public int StatusCode { get; }
}
