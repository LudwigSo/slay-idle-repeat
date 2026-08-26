using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// Records every exception that escapes the pipeline on the telemetry port, then rethrows it
/// unchanged — observation only, never a handler. A plain class so it is testable with a
/// <c>DefaultHttpContext</c> and no host.
/// </summary>
public sealed class TelemetryExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITelemetryPort _telemetry;

    /// <summary>Builds the middleware over the rest of the pipeline and the port it records on.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="telemetry">Where an escaping exception is recorded.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public TelemetryExceptionMiddleware(RequestDelegate next, ITelemetryPort telemetry)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(telemetry);

        _next = next;
        _telemetry = telemetry;
    }

    /// <summary>Runs the pipeline; records and rethrows whatever escapes it.</summary>
    /// <param name="context">The request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _telemetry.RecordException(error);

            throw;
        }
    }
}
