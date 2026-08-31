using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// A hand-written <see cref="IAuthStore"/>: scriptable rows in, a record of every write out.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked, following this repository's own rule for port fakes — a mock
/// would drift from what the real adapter does and would let a test assert on a call graph rather
/// than on a stored row.
/// </remarks>
internal sealed class FakeAuthStore : IAuthStore
{
    private readonly Dictionary<string, AuthDevice> _byDeviceId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AuthDevice> _byPlayer = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deleted = new(StringComparer.Ordinal);
    private readonly List<(byte[] Digest, AuthTokenState State)> _states = new();

    /// <summary>Every account this store was asked to create.</summary>
    internal List<(AuthDevice Device, string DisplayName)> Created { get; } = new();

    /// <summary>Every family opened, with the token it opened on.</summary>
    internal List<(AuthTokenFamily Family, AuthRefreshToken Token, DateTimeOffset LastSeenAtUtc)> Opened { get; } = new();

    /// <summary>Every rotation decision applied.</summary>
    internal List<RefreshRotationDecision> Rotations { get; } = new();

    /// <summary>Every soft delete recorded.</summary>
    internal List<(PlayerId Player, DateTimeOffset RequestedAtUtc, DateTimeOffset DueAtUtc)> SoftDeletes { get; } = new();

    /// <summary>Puts a device row in the store.</summary>
    internal FakeAuthStore WithDevice(AuthDevice device)
    {
        _byDeviceId[device.DeviceId] = device;
        _byPlayer[device.Player.Value] = device;

        return this;
    }

    /// <summary>Marks an account soft-deleted.</summary>
    internal FakeAuthStore WithDeletedAccount(PlayerId player)
    {
        _deleted.Add(player.Value);

        return this;
    }

    /// <summary>Answers a digest lookup with the given rows.</summary>
    internal FakeAuthStore WithTokenState(byte[] digest, AuthTokenState state)
    {
        _states.Add((digest, state));

        return this;
    }

    /// <summary>Whether the account is still live here — what a refused deletion must not change.</summary>
    internal bool IsLive(PlayerId player) => !_deleted.Contains(player.Value);

    /// <inheritdoc/>
    public Task CreateAnonymousAccountAsync(AuthDevice device, string displayName, CancellationToken ct)
    {
        Created.Add((device, displayName));
        WithDevice(device);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<AuthDevice?> FindDeviceAsync(string deviceId, CancellationToken ct) =>
        Task.FromResult(_byDeviceId.TryGetValue(deviceId, out var device) ? device : null);

    /// <inheritdoc/>
    public Task<AuthDevice?> FindDeviceForPlayerAsync(PlayerId player, CancellationToken ct) =>
        Task.FromResult(_byPlayer.TryGetValue(player.Value, out var device) ? device : null);

    /// <inheritdoc/>
    public Task<bool> IsAccountDeletedAsync(PlayerId player, CancellationToken ct) =>
        Task.FromResult(_deleted.Contains(player.Value));

    /// <inheritdoc/>
    public Task OpenTokenFamilyAsync(
        AuthTokenFamily family,
        AuthRefreshToken token,
        DateTimeOffset lastSeenAtUtc,
        CancellationToken ct)
    {
        Opened.Add((family, token, lastSeenAtUtc));

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<AuthTokenState> LoadTokenStateForDigestAsync(byte[] tokenDigest, CancellationToken ct) =>
        Task.FromResult(
            _states.FirstOrDefault(entry => entry.Digest.SequenceEqual(tokenDigest)).State
            ?? AuthTokenState.Empty);

    /// <inheritdoc/>
    public Task ApplyRotationAsync(RefreshRotationDecision decision, DateTimeOffset nowUtc, CancellationToken ct)
    {
        Rotations.Add(decision);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SoftDeleteAccountAsync(
        PlayerId player,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset hardDeleteDueAtUtc,
        CancellationToken ct)
    {
        SoftDeletes.Add((player, requestedAtUtc, hardDeleteDueAtUtc));
        _deleted.Add(player.Value);

        return Task.CompletedTask;
    }
}

/// <summary>A display-name filter pinned to one answer, recording what it was asked.</summary>
internal sealed class RecordingDisplayNamePolicy : IDisplayNamePolicy
{
    private readonly DisplayNameDecision _decision;

    internal RecordingDisplayNamePolicy(DisplayNameDecision decision) => _decision = decision;

    /// <summary>Every candidate the handler passed through, in order — <c>null</c> included.</summary>
    internal List<string?> Candidates { get; } = new();

    /// <inheritdoc/>
    public DisplayNameDecision Decide(string? candidate)
    {
        Candidates.Add(candidate);

        return _decision;
    }
}

/// <summary>A principal seam pinned to one answer, so each HTTP arm is reachable directly.</summary>
internal sealed class FixedPrincipalResolver(PrincipalResolution resolution) : IPrincipalResolver
{
    /// <summary>Every header the caller asked about, in order — <c>null</c> included.</summary>
    internal List<string?> Headers { get; } = new();

    /// <inheritdoc/>
    public PrincipalResolution Resolve(string? authorizationHeader)
    {
        Headers.Add(authorizationHeader);

        return resolution;
    }
}
