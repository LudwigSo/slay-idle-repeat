using Shouldly;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// States what <see cref="IGameHost"/> <em>means</em> (<c>23</c> §5 A8) — written once against the
/// interface so the composed in-process host and the scripted fake cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Every case below is about how the host answers a question it cannot say yes to. That is where
/// two implementations of this seam drift: one spells "there is no such run" as an empty view and
/// the other as an exception; one mints a fresh profile on every call and the other remembers.
/// Both look correct in isolation, and a presenter written against either one breaks against the
/// other — which is the whole reason this port stopped being an ungated hosting interface.
/// </para>
/// <para>
/// ⚠️ <b>Nothing here asserts what a command DOES.</b> The fake runs no rules and never will: the
/// domain is a single seam with its own suites, and a shared contract case that demanded a real
/// outcome would be a case only one implementation could ever pass. What is shared is the shape of
/// the answer, which is what a caller writes code against.
/// </para>
/// </remarks>
[ContractSuiteFor(typeof(IGameHost))]
public abstract class IGameHostContractTests
{
    /// <summary>Cancellation none of these cases exercise, named once so the calls read.</summary>
    private static readonly CancellationToken Cancel = CancellationToken.None;

    /// <summary>A run no implementation has ever heard of.</summary>
    private static readonly RunId UnknownRun = new("RUN_no_such_run_anywhere");

    /// <summary>A player no implementation has ever minted.</summary>
    private static readonly PlayerId UnknownPlayer = new("PLAYER_no_such_profile_anywhere");

    /// <summary>A host of the implementation under test, freshly opened for business.</summary>
    protected abstract IGameHost Create();

    /// <summary>
    /// 🔒 The profile is minted once and never again: the second call names the player the first one
    /// did.
    /// </summary>
    /// <remarks>
    /// The load-bearing case of this suite. Every screen in the client opens the profile on the way
    /// in, so a host that minted per call would hand each screen a different player — and the
    /// symptom is not a crash but a game that silently forgets everything between screens, which is
    /// the most expensive kind of defect to trace back to its seam.
    /// </remarks>
    [Fact]
    public async Task Opening_the_profile_twice_names_the_same_player()
    {
        var host = Create();

        var first = await host.OpenProfileAsync(Cancel);
        var second = await host.OpenProfileAsync(Cancel);

        second.ShouldBe(
            first,
            $"the profile was opened twice and answered '{first}' then '{second}'. This installation "
            + "plays one account; a host that mints per call gives every caller its own game.");
    }

    /// <summary>A player this host does not know is a lookup answer, never an exception.</summary>
    /// <remarks>
    /// The absence has to arrive as data because the caller is a presenter, and a presenter that has
    /// to catch to find out whether a profile exists will eventually catch something else too. It is
    /// asserted after the profile has been opened, so the case is about the id being unknown rather
    /// than about the host being empty.
    /// </remarks>
    [Fact]
    public async Task Reading_a_player_this_host_did_not_mint_answers_NoSuchPlayer()
    {
        var host = Create();
        await host.OpenProfileAsync(Cancel);

        var result = await host.ReadOwnStateAsync(UnknownPlayer, run: null, Cancel);

        result.Lookup.ShouldBe(
            OwnStateLookup.NoSuchPlayer,
            $"reading '{UnknownPlayer}' answered {result.Lookup}. Nothing is stored for that player, "
            + "and this port spells that absence as a lookup value rather than as an exception or an "
            + "empty view a caller cannot tell from a real one.");
        result.View.ShouldBeNull("there is no state to view, so there is no view.");
    }

    /// <summary>
    /// 🔒 A run the player is not in answers <see cref="OwnStateLookup.NoSuchRun"/> — the same answer
    /// a stranger's run gets.
    /// </summary>
    /// <remarks>
    /// The two are deliberately indistinguishable, which is the port's own ruling: telling them
    /// apart tells a caller which run ids exist. An implementation that answered "found, but empty"
    /// here would be handing a presenter a run with no fields and no way to know why.
    /// </remarks>
    [Fact]
    public async Task Reading_a_run_the_player_is_not_in_answers_NoSuchRun()
    {
        var host = Create();
        var player = await host.OpenProfileAsync(Cancel);

        var result = await host.ReadOwnStateAsync(player, UnknownRun, Cancel);

        result.Lookup.ShouldBe(
            OwnStateLookup.NoSuchRun,
            $"reading '{UnknownRun}' for a player who is not in it answered {result.Lookup}. The "
            + "player exists, so NoSuchPlayer would be wrong; the run does not, and an answer that "
            + "distinguished 'never existed' from 'belongs to somebody else' would let a caller probe "
            + "for other people's runs.");
        result.View.ShouldBeNull("there is no run to view, so there is no view.");
    }

    /// <summary>
    /// 🔒 A refused command is a returned outcome carrying its reason — never a thrown exception,
    /// and never a silent success.
    /// </summary>
    /// <remarks>
    /// An illegal move is data, not an exception, and this is where that ruling meets a caller. The
    /// command below is addressed to a run the player is not in, which every implementation refuses
    /// before any rule is consulted — so the case is about the SHAPE of a refusal and needs no
    /// agreement about game rules to be shared.
    /// </remarks>
    [Fact]
    public async Task A_refused_command_answers_with_its_reason_rather_than_throwing()
    {
        var host = Create();
        var player = await host.OpenProfileAsync(Cancel);

        // Awaited rather than wrapped: a host that threw here fails this case with its own exception,
        // which is what the clause is about — a refusal must not be one.
        var outcome = await host.SubmitAsync(player, UnknownRun, new SkipDraftCommand(), Cancel);

        outcome.Accepted.ShouldBeFalse("the run does not exist, so nothing could have been applied.");
        outcome.Rejection.ShouldBe(
            RejectionReason.RUN_NOT_FOUND,
            $"the outcome was refused with {outcome.Rejection}. The addressing refusal has one "
            + "reason, and a caller that cannot read which one it was cannot tell a missing run from "
            + "an unaffordable purchase.");
        outcome.State.ShouldNotBeNull(
            "a refusal still carries the untouched state — that is what lets a caller re-render "
            + "without a second read, and it is the difference between a refusal and a failure.");
        outcome.Events.ShouldBeEmpty("a refused command changed nothing, so it has nothing to report.");
    }
}
