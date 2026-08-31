using SlayIdleRepeat.Adapters.Persistence.Postgres;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The auth area: binds the deployment's auth settings, builds the token issuer, the auth rows and
/// the principal seam, and maps the four routes.
/// </summary>
/// <remarks>
/// Composition-root code on the same regime as the command and remote-config areas: everything is
/// built on the FIRST auth request, never at map time, so a host with neither a signing key nor a
/// database still boots and answers <c>/health</c>. A failed build is retried rather than cached.
/// </remarks>
public static class AuthComposition
{
    private static readonly object InitializationGate = new();
    private static AuthArea? _area;

    /// <summary>Maps the four auth routes.</summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            "/auth/device",
            async (HttpContext http, CancellationToken ct) =>
            {
                var area = Area(app);
                var backbone = GameBackbone.Shared(app.Configuration, app.Environment);

                await WriteAsync(
                    http,
                    await AuthRequestHandler.HandleDeviceRegistrationAsync(
                        area.Store,
                        new HeroNameDisplayNamePolicy(backbone.Content),
                        backbone.Clock.UtcNow,
                        await HttpReplies.ReadBodyAsync(http, ct),
                        ct),
                    ct);
            });

        app.MapPost(
            "/auth/session",
            async (HttpContext http, CancellationToken ct) =>
            {
                var area = Area(app);

                await WriteAsync(
                    http,
                    await AuthRequestHandler.HandleSessionAsync(
                        area.Store,
                        area.Issuer,
                        area.Options,
                        GameBackbone.Shared(app.Configuration, app.Environment).Clock.UtcNow,
                        await HttpReplies.ReadBodyAsync(http, ct),
                        ct),
                    ct);
            });

        app.MapPost(
            "/auth/refresh",
            async (HttpContext http, CancellationToken ct) =>
            {
                var area = Area(app);

                await WriteAsync(
                    http,
                    await AuthRequestHandler.HandleRefreshAsync(
                        area.Store,
                        area.Issuer,
                        area.Options,
                        GameBackbone.Shared(app.Configuration, app.Environment).Clock.UtcNow,
                        await HttpReplies.ReadBodyAsync(http, ct),
                        ct),
                    ct);
            });

        app.MapDelete(
            "/account",
            async (HttpContext http, CancellationToken ct) =>
            {
                var area = Area(app);

                await WriteAsync(
                    http,
                    await AuthRequestHandler.HandleAccountDeletionAsync(
                        area.Store,
                        area.Principals,
                        http.Request.Headers.Authorization,
                        GameBackbone.Shared(app.Configuration, app.Environment).Clock.UtcNow,
                        await HttpReplies.ReadBodyAsync(http, ct),
                        ct),
                    ct);
            });

        return app;
    }

    /// <summary>
    /// The real principal seam, for the areas that sit behind it. Deferred so mapping a route does
    /// not force the signing key and the database into existence at boot.
    /// </summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static IPrincipalResolver Principals(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new DeferredPrincipalResolver(() => Area(app).Principals);
    }

    private static AuthArea Area(WebApplication app)
    {
        if (Volatile.Read(ref _area) is { } built)
        {
            return built;
        }

        lock (InitializationGate)
        {
            var backbone = GameBackbone.Shared(app.Configuration, app.Environment);

            return _area ??= new AuthArea(app.Configuration, backbone.Clock);
        }
    }

    // 🔒 Every reply on this surface carries a device secret, an access token or a refresh token.
    // POSTs are not cached by default, but "by default" is a property of every proxy on the path
    // agreeing — and the query surface pins the same header on far less than this. The plumbing is
    // HttpReplies' (one place a handler's answer becomes a response); the header is this area's.
    private static Task WriteAsync(HttpContext http, AuthReply reply, CancellationToken ct) =>
        HttpReplies.WriteAsync(http, reply.StatusCode, reply.Body, ct, cacheControl: "no-store");
}

/// <summary>Everything the auth routes run on, built once per process.</summary>
internal sealed class AuthArea
{
    internal AuthArea(IConfiguration configuration, IClockPort clock)
    {
        Options = AuthOptions.Bind(configuration);
        Issuer = new AccessTokenIssuer(Options);

        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__Postgres carries no value, and the auth rows are the one thing " +
                "this server has no volatile stand-in for: a device credential that does not " +
                "survive a restart is an account the player loses on the next deploy.");
        }

        var rows = PostgresAuthStore.Create(connectionString);
        var accounts = new CachedAccountStatus(rows, clock);

        Store = new PostgresBackedAuthStore(rows, accounts);
        Principals = new JwtPrincipalResolver(Issuer, accounts, clock);
    }

    internal AuthOptions Options { get; }

    internal AccessTokenIssuer Issuer { get; }

    internal IAuthStore Store { get; }

    internal IPrincipalResolver Principals { get; }
}

/// <summary>The auth rows over the Postgres adapter's concrete store.</summary>
/// <remarks>
/// The seam is declared in this project and implemented here, next to the wiring that owns it; the
/// adapter stays a plain vendor-speaking type with no interface of ours on it. Translating the rows
/// at this boundary is what keeps the adapter free of this project's vocabulary.
/// </remarks>
internal sealed class PostgresBackedAuthStore : IAuthStore
{
    private readonly PostgresAuthStore _rows;
    private readonly CachedAccountStatus _accounts;

    internal PostgresBackedAuthStore(PostgresAuthStore rows, CachedAccountStatus accounts)
    {
        _rows = rows;
        _accounts = accounts;
    }

    public Task CreateAnonymousAccountAsync(AuthDevice device, string displayName, CancellationToken ct) =>
        // The display name belongs to the player row, which the profile store owns; what auth keeps
        // is the credential. The account's first profile is written by its first command.
        _rows.InsertDeviceAsync(
            new AuthDeviceRow(
                device.DeviceId,
                device.Player.Value,
                device.SecretDigest,
                device.CreatedAtUtc,
                device.LastSeenAtUtc),
            ct);

    public async Task<AuthDevice?> FindDeviceAsync(string deviceId, CancellationToken ct) =>
        Device(await _rows.FindDeviceAsync(deviceId, ct).ConfigureAwait(false));

    public async Task<AuthDevice?> FindDeviceForPlayerAsync(PlayerId player, CancellationToken ct) =>
        Device(await _rows.FindDeviceForPlayerAsync(player.Value, ct).ConfigureAwait(false));

    public Task<bool> IsAccountDeletedAsync(PlayerId player, CancellationToken ct) =>
        _rows.IsAccountDeletedAsync(player.Value, ct);

    public Task OpenTokenFamilyAsync(
        AuthTokenFamily family, AuthRefreshToken token, DateTimeOffset lastSeenAtUtc, CancellationToken ct) =>
        _rows.OpenTokenFamilyAsync(
            new AuthTokenFamilyRow(
                family.FamilyId,
                family.DeviceId,
                family.Player.Value,
                family.CreatedAtUtc,
                family.RevokedAtUtc,
                family.RevokedReason?.ToString()),
            TokenRow(token),
            lastSeenAtUtc,
            ct);

    public async Task<AuthTokenState> LoadTokenStateForDigestAsync(byte[] tokenDigest, CancellationToken ct)
    {
        var state = await _rows.LoadTokenStateForDigestAsync(tokenDigest, ct).ConfigureAwait(false);

        return new AuthTokenState(
            state.Families
                .Select(family => new AuthTokenFamily(
                    family.FamilyId,
                    family.DeviceId,
                    new PlayerId(family.PlayerId),
                    family.CreatedAtUtc,
                    family.RevokedAtUtc,
                    Revocation(family.RevokedReason)))
                .ToArray(),
            state.Tokens
                .Select(token => new AuthRefreshToken(
                    token.TokenHash,
                    token.FamilyId,
                    token.DeviceId,
                    new PlayerId(token.PlayerId),
                    token.IssuedAtUtc,
                    token.ExpiresAtUtc,
                    token.RotatedAtUtc))
                .ToArray());
    }

    public Task ApplyRotationAsync(RefreshRotationDecision decision, DateTimeOffset nowUtc, CancellationToken ct) =>
        _rows.ApplyRotationAsync(
            new AuthRotationWrite(
                decision.RotatedTokenDigest,
                decision.IssuedToken is { } issued ? TokenRow(issued) : null,
                decision.RevokedTokenDigests,
                decision.FamilyId,
                decision.FamilyRevocation?.ToString()),
            nowUtc,
            ct);

    public async Task SoftDeleteAccountAsync(
        PlayerId player, DateTimeOffset requestedAtUtc, DateTimeOffset hardDeleteDueAtUtc, CancellationToken ct)
    {
        await _rows.SoftDeleteAccountAsync(
                player.Value,
                requestedAtUtc,
                hardDeleteDueAtUtc,
                TokenFamilyRevocation.ACCOUNT_DELETED.ToString(),
                ct)
            .ConfigureAwait(false);

        // The status view learns of the deletion here rather than on its next sweep: "logins are
        // refused from this instant" is the promise the endpoint just made the player.
        _accounts.MarkLocked(player);
    }

    private static AuthDevice? Device(AuthDeviceRow? row) =>
        row is null
            ? null
            : new AuthDevice(
                row.DeviceId,
                new PlayerId(row.PlayerId),
                row.SecretHash,
                row.CreatedAtUtc,
                row.LastSeenAtUtc);

    private static AuthRefreshTokenRow TokenRow(AuthRefreshToken token) =>
        new(token.TokenDigest,
            token.FamilyId,
            token.DeviceId,
            token.Player.Value,
            token.IssuedAtUtc,
            token.ExpiresAtUtc,
            token.RotatedAtUtc);

    private static TokenFamilyRevocation? Revocation(string? reason) =>
        Enum.TryParse<TokenFamilyRevocation>(reason, out var parsed) ? parsed : null;
}

/// <summary>Whether an account may hold a session, answered from a cached view.</summary>
/// <remarks>
/// ⚠️ This sits on the hot path of EVERY authenticated request, so a read here is a set lookup and
/// never a query: a database round-trip per request would make the lock check the most expensive
/// thing the gateway does. The view is refreshed on an interval and marked immediately when this
/// process itself deletes an account, so the only staleness window is a deletion performed by
/// another instance — bounded by the interval, and it costs a locked account a few more seconds of
/// access, not an unlocked one its account.
/// </remarks>
internal sealed class CachedAccountStatus : IAccountStatusReader, IDisposable
{
    /// <summary>How long a deletion by another instance may go unseen here.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly object _writeGate = new();
    private readonly PostgresAuthStore _rows;
    private readonly IClockPort _clock;
    private readonly Timer _refresh;

    private HashSet<string> _locked = new(StringComparer.Ordinal);

    internal CachedAccountStatus(PostgresAuthStore rows, IClockPort clock)
    {
        _rows = rows;
        _clock = clock;
        _refresh = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.Zero, RefreshInterval);
    }

    /// <inheritdoc/>
    public bool IsLocked(PlayerId player) => Volatile.Read(ref _locked).Contains(player.Value);

    /// <summary>Locks one account in the view now, without waiting for the next sweep.</summary>
    /// <param name="player">The account just soft-deleted.</param>
    internal void MarkLocked(PlayerId player)
    {
        lock (_writeGate)
        {
            // Replaced rather than mutated: readers hold no lock, so the set they see is immutable
            // for as long as they see it.
            Volatile.Write(ref _locked, new HashSet<string>(Volatile.Read(ref _locked), StringComparer.Ordinal)
            {
                player.Value,
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _refresh.Dispose();

    private async Task RefreshAsync()
    {
        try
        {
            var deleted = await _rows.DeletedPlayersAsync(CancellationToken.None).ConfigureAwait(false);

            Volatile.Write(ref _locked, new HashSet<string>(deleted, StringComparer.Ordinal));
        }
        catch (Exception failure)
        {
            // A failed sweep keeps the last good view rather than emptying it: unlocking every
            // account because the database blinked is the worse of the two failure modes. The
            // marker greps in the container's log stream, as the remote-config source's does.
            Console.Error.WriteLine(
                "[auth] account-status refresh failed at " + _clock.UtcNow.ToString("O") +
                "; the previous view stands. " + failure.Message);
        }
    }
}

/// <summary>The principal seam before the area behind it exists.</summary>
/// <remarks>
/// Mapping a route must not build the token issuer or open a connection pool, or a deployment with
/// no auth configuration could not boot far enough to answer <c>/health</c> and say so.
/// </remarks>
internal sealed class DeferredPrincipalResolver : IPrincipalResolver
{
    private readonly Func<IPrincipalResolver> _resolver;

    internal DeferredPrincipalResolver(Func<IPrincipalResolver> resolver) => _resolver = resolver;

    /// <inheritdoc/>
    public PrincipalResolution Resolve(string? authorizationHeader) =>
        _resolver().Resolve(authorizationHeader);
}
