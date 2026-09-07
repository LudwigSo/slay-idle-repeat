using Shouldly;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The one <c>BEGIN_SESSION</c> a launch sends: that it is sent at all, what it carries, and that
/// neither a refusal nor a fault stops the game.
/// </summary>
/// <remarks>
/// 🔴 <b>This whole file exists because the command had no production sender.</b>
/// <c>Player.CreateStarting</c> opens both Energy banks at zero and the daily free refill is
/// <c>BEGIN_SESSION</c>'s grant — so with <c>START_RUN</c> now charging the authored run cost, a
/// freshly installed profile could not start a run until regeneration had filled the price. That is
/// steering S25's shape exactly: a documented seam whose only caller was deferred.
/// </remarks>
public sealed class GameDayOpenerTests
{
    private static readonly PlayerId Profile = new("PLAYER_game_day_5b3a");

    private const string ClientVersion = "0.1.0-fixture";

    /// <summary>A stamp no constant anywhere in the client could match by accident.</summary>
    private static readonly ContentVersion Stamp =
        ContentVersion.FromHex(new string('d', ContentVersion.HexLength));

    [Fact]
    public async Task Opening_the_day_submits_BEGIN_SESSION_for_the_profile_that_was_opened()
    {
        var host = RecordingGameHost.Finding(PlayerState.Player(Profile));

        await Opener(host).OpenAsync(Profile, CancellationToken.None);

        host.SubmitCallCount.ShouldBe(
            1,
            "one command per launch. Nothing else in the build sends it, so a launch that skipped " +
            "it leaves a new profile with no Energy and no way to earn any but waiting.");
        host.SubmitCommand.ShouldBeOfType<BeginSessionCommand>();
        host.SubmitPlayer.ShouldBe(Profile, "the profile the boot opened, and no other.");
        host.SubmitRun.ShouldBeNull(
            "BEGIN_SESSION is a meta command about the player. Addressing it to a run would ask the " +
            "domain to apply it to a slice it is not about.");
    }

    /// <summary>
    /// 🔒 The hash it carries is the version of the content THIS client loaded.
    /// </summary>
    /// <remarks>
    /// <c>ContentVersionCheck</c> says this is the only command carrying a content hash and refuses
    /// one that is not exactly the version the server serves. A client sending a constant — the
    /// version it was built against, say — would be refused after its first content update, on
    /// every launch, and lose the daily grant for good.
    /// </remarks>
    [Fact]
    public async Task The_command_carries_the_version_of_the_content_this_client_loaded()
    {
        var host = RecordingGameHost.Finding(PlayerState.Player(Profile));

        await Opener(host).OpenAsync(Profile, CancellationToken.None);

        var session = host.SubmitCommand.ShouldBeOfType<BeginSessionCommand>();

        session.ContentHash.ShouldBe(
            Stamp.Value,
            "the stamp of the loaded snapshot, spelt exactly as the wire form is — the check is " +
            "ordinal, so an upper-cased or prefixed spelling is a different claim.");
        session.ClientVersion.ShouldBe(
            ClientVersion, "and the build the player is running, as the platform reported it.");
    }

    [Fact]
    public async Task An_opened_day_is_reported_as_opened()
    {
        var opener = Opener(RecordingGameHost.Finding(PlayerState.Player(Profile)));

        var opened = await opener.OpenAsync(Profile, CancellationToken.None);

        opened.ShouldBeTrue();
        opener.Opened.ShouldBeTrue();
        opener.Refusal.ShouldBeNull("nothing refused it.");
        opener.Failure.ShouldBeNull("and nothing faulted.");
    }

    /// <summary>
    /// 🔒 A refusal is recorded, never thrown — and it does not stop a launch.
    /// </summary>
    /// <remarks>
    /// The handler is idempotent per game day, so a launch whose call was refused loses that day's
    /// refill and nothing else. A boot that stopped here would trade a missing grant for an app
    /// that will not start, which is the worse of the two by a distance.
    /// </remarks>
    [Fact]
    public async Task A_refused_day_is_recorded_with_the_reason_and_does_not_throw()
    {
        var host = RecordingGameHost
            .Finding(PlayerState.Player(Profile))
            .RefusingCommands(RejectionReason.CONTENT_VERSION_MISMATCH);
        var opener = Opener(host);

        var opened = await opener.OpenAsync(Profile, CancellationToken.None);

        opened.ShouldBeFalse();
        opener.Opened.ShouldBeFalse();
        opener.Refusal.ShouldBe(
            RejectionReason.CONTENT_VERSION_MISMATCH,
            "which reason it was is the whole diagnostic. 'It did not work' is the same line for a " +
            "stale content set and for a day already opened.");
        opener.Failure.ShouldBeNull(
            "a refusal is the domain ANSWERING. Reporting it as a fault as well would have a launch " +
            "look like it could not reach the host it just heard from.");
    }

    /// <summary>
    /// 🔒 …and so is a host that does not answer at all, which is a different failure.
    /// </summary>
    /// <remarks>
    /// Told apart from a refusal on purpose: a refusal is an answer a caller reads, a fault is an
    /// exception a caller catches, and the two are fixed by different people. Awaited inside the
    /// guard rather than merely called inside it — a real host's submit is an async method, so its
    /// failure arrives as a faulted task and a try around the call alone would never see it.
    /// </remarks>
    [Fact]
    public async Task A_host_that_does_not_answer_is_recorded_as_a_fault_and_does_not_throw()
    {
        var host = RecordingGameHost
            .Finding(PlayerState.Player(Profile))
            .FaultingItsCommands(new InvalidOperationException("the local store is not there"));
        var opener = Opener(host);

        var opened = await opener.OpenAsync(Profile, CancellationToken.None);

        opened.ShouldBeFalse();
        opener.Failure.ShouldNotBeNull().ShouldContain(
            "the local store is not there",
            Case.Sensitive,
            "the line has to name the failure that happened rather than merely be non-blank. A " +
            "fixed string satisfies every 'is it set?' check and says nothing.");
        opener.Refusal.ShouldBeNull("nothing refused it — nothing answered at all.");
    }

    /// <summary>A second open reports afresh rather than remembering the first attempt's verdict.</summary>
    /// <remarks>
    /// The negative control for the two cases above: an opener that latched its failure would print
    /// a stale refusal under the next launch's real answer, and neither of those cases would notice.
    /// </remarks>
    [Fact]
    public async Task A_later_open_reports_its_own_answer_rather_than_the_last_one()
    {
        var host = RecordingGameHost
            .Finding(PlayerState.Player(Profile))
            .FaultingItsCommands(new InvalidOperationException("one bad call"), times: 1);
        var opener = Opener(host);

        await opener.OpenAsync(Profile, CancellationToken.None);

        opener.Failure.ShouldNotBeNull("the fixture must actually fault the first call.");

        await opener.OpenAsync(Profile, CancellationToken.None);

        opener.Opened.ShouldBeTrue("the second call was answered.");
        opener.Failure.ShouldBeNull("and the first call's fault is not still being reported.");
    }

    [Fact]
    public void Constructor_rejects_a_null_host() =>
        Should.Throw<ArgumentNullException>(
                  () => new GameDayOpener(host: null!, ClientVersion, Stamp))
              .ParamName.ShouldBe("host");

    private static GameDayOpener Opener(RecordingGameHost host) =>
        new(host, ClientVersion, Stamp);
}
