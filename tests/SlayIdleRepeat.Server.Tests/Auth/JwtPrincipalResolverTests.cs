using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Auth;
using SlayIdleRepeat.Server.Endpoints;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Auth;

/// <summary>
/// The gateway's principal seam, once it is real: a Bearer JWT resolves its own <c>sub</c>, every
/// way of not having one is a 401, and a locked account is the 403 account-state answer.
/// </summary>
public sealed class JwtPrincipalResolverTests
{
    private static readonly PlayerId Player = new("PLAYER_bearer_subject");

    private const string Device = "DEVICE_bearer_holder";

    /// <summary>A locked-account reader pinned to one answer.</summary>
    private sealed class FixedAccountStatus(bool locked) : IAccountStatusReader
    {
        public bool IsLocked(PlayerId player) => locked;
    }

    private static AdjustableClock ClockAt(DateTimeOffset instant)
    {
        var clock = new AdjustableClock();
        clock.Set(instant);

        return clock;
    }

    private static JwtPrincipalResolver Resolver(
        DateTimeOffset? readingAt = null,
        bool locked = false,
        string signingKey = AuthFixtures.SigningKey) =>
        new(new AccessTokenIssuer(AuthFixtures.Options(signingKey)),
            new FixedAccountStatus(locked),
            ClockAt(readingAt ?? AuthFixtures.Now));

    private static string MintedToken(string signingKey = AuthFixtures.SigningKey) =>
        new AccessTokenIssuer(AuthFixtures.Options(signingKey)).Mint(Player, Device, AuthFixtures.Now);

    [Fact]
    public void Resolve_returns_the_tokens_subject_as_the_player()
    {
        var resolution = Resolver().Resolve("Bearer " + MintedToken());

        resolution.Player.ShouldBe(Player, "sub is the identity every endpoint behind this seam acts as.");
        resolution.RefusalStatus.ShouldBeNull();
    }

    /// <summary>
    /// 🔒 The proof the placeholder is gone. It read <c>Bearer &lt;playerId&gt;</c> as the player it
    /// named — anyone naming a player WAS that player — so the exact input it used to accept must
    /// now be a 401.
    /// </summary>
    [Theory]
    [InlineData("Bearer p_someplayer")]
    [InlineData("Bearer PLAYER_bearer_subject")]
    public void Resolve_is_unauthorized_for_a_bare_player_id(string header)
    {
        var resolution = Resolver().Resolve(header);

        resolution.Player.ShouldBeNull();
        resolution.RefusalStatus.ShouldBe(
            401,
            "a player id is a name, not a credential. Resolving one would authenticate nothing, " +
            "which is the exact behaviour this seam replaced.");
    }

    [Fact]
    public void Resolve_is_unauthorized_when_no_authorization_header_was_sent()
    {
        Resolver().Resolve(null).RefusalStatus.ShouldBe(401);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token abcdef")]
    [InlineData("bearer lowercase-scheme")]
    public void Resolve_is_unauthorized_without_the_bearer_prefix(string header)
    {
        Resolver().Resolve(header).RefusalStatus.ShouldBe(
            401, "the scheme is part of the contract; a token under another scheme is not presented.");
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("Bearer      ")]
    public void Resolve_is_unauthorized_for_an_empty_bearer_token(string header)
    {
        Resolver().Resolve(header).RefusalStatus.ShouldBe(401);
    }

    [Fact]
    public void Resolve_is_unauthorized_for_an_expired_token()
    {
        var resolution = Resolver(readingAt: AuthFixtures.Now.AddMinutes(61))
            .Resolve("Bearer " + MintedToken());

        resolution.RefusalStatus.ShouldBe(
            401,
            "401 is the status the client cures by refreshing silently and retrying the same " +
            "commandId; an expired token is exactly that case and must never be 403.");
    }

    [Fact]
    public void Resolve_is_unauthorized_for_a_token_signed_with_another_key()
    {
        var foreign = MintedToken(AuthFixtures.OtherSigningKey);

        Resolver().Resolve("Bearer " + foreign).RefusalStatus.ShouldBe(401);
    }

    [Fact]
    public void Resolve_is_locked_for_a_soft_deleted_or_locked_account()
    {
        var resolution = Resolver(locked: true).Resolve("Bearer " + MintedToken());

        resolution.Player.ShouldBeNull();
        resolution.RefusalStatus.ShouldBe(
            403,
            "the token is perfectly valid, so refreshing it cures nothing — this is the " +
            "account-state screen, not a token problem.");
    }
}
