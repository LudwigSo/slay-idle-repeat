using Shouldly;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// What the client-side wire records say about themselves when something formats one.
/// </summary>
/// <remarks>
/// 🔒 A record's compiler-generated <c>ToString</c> prints every member, so a device secret or a
/// refresh token reaches any log line, exception message or crash report that ever formats one of
/// these — while the records' own doc comments promise the opposite. These are the first types in
/// the repository that actually hold those values.
/// </remarks>
public sealed class WireClientResultsTests
{
    private const string DeviceId = "DEVICE_printable_9f2a";
    private const string DeviceSecret = "SECRETVALUE_must_never_be_printed";
    private const string AccessToken = "ACCESSVALUE_must_never_be_printed";
    private const string RefreshToken = "REFRESHVALUE_must_never_be_printed";
    private const string DisplayName = "Printable Hero";

    /// <summary>
    /// The leading run of each secret, so a rendering that printed a TRUNCATED secret is caught too.
    /// </summary>
    /// <remarks>
    /// Asserting on the whole value would pass against <c>SECRETVALU…</c>, which is still enough of
    /// a credential to be worth nothing to an attacker and everything to a compliance review.
    /// </remarks>
    private const int LeadingRunLength = 6;

    private static readonly PlayerId Player = new("PLAYER_printable_31c8");

    [Fact]
    public void The_credential_records_do_not_print_their_secrets()
    {
        var credentials = new WireCredentials(DeviceId, DeviceSecret).ToString();
        var registration =
            new WireDeviceRegistration(DeviceId, DeviceSecret, Player, DisplayName).ToString();
        var session = new WireSession(Player, AccessToken, 3600, 2700, RefreshToken, 2_592_000).ToString();

        credentials.ShouldNotContain(
            Leading(DeviceSecret),
            Case.Sensitive,
            "the device secret is the anonymous account's root credential and this record's own doc " +
            "comment says it is never logged. A record that inherits the compiler's ToString prints " +
            "it into every log line, exception message and debugger watch that formats one — and the " +
            "leading run is asserted rather than the whole value so a truncated secret is caught too.");
        registration.ShouldNotContain(
            Leading(DeviceSecret),
            Case.Sensitive,
            "the registration answer is where the secret is issued, so it is the record most likely " +
            "to be logged while somebody is debugging a sign-in — which is exactly when it must not be.");
        session.ShouldNotContain(
            Leading(RefreshToken),
            Case.Sensitive,
            "the refresh token rotates the whole family, so leaking one is leaking the account for as " +
            "long as the family lives.");
        session.ShouldNotContain(
            Leading(AccessToken),
            Case.Sensitive,
            "and the access token is the bearer every command carries. Both are asserted, because a " +
            "redaction that covered one member and forgot the other would satisfy either claim alone.");
    }

    /// <summary>
    /// 🔒 The negative control: a redaction that printed nothing at all would satisfy every claim
    /// above and leave a support engineer unable to say which device or account a line was about.
    /// </summary>
    [Fact]
    public void The_credential_records_still_print_the_identifiers_a_log_needs()
    {
        new WireCredentials(DeviceId, DeviceSecret).ToString().ShouldContain(
            DeviceId,
            Case.Sensitive,
            "the device id is an identifier, not a credential — it proves nothing on its own and it " +
            "is the only thing that makes a redacted line traceable.");
        new WireSession(Player, AccessToken, 3600, 2700, RefreshToken, 2_592_000).ToString().ShouldContain(
            Player.Value,
            Case.Sensitive,
            "likewise the account a session belongs to. A ToString reduced to the type name would " +
            "pass every secrecy assertion here and be worth less than no override at all.");
    }

    private static string Leading(string secret) => secret[..LeadingRunLength];
}
