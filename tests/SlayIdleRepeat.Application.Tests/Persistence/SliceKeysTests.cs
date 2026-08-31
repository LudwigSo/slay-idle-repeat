using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Persistence;

/// <summary>The key space: two prefixes, and a loud refusal for an id that would leave it.</summary>
public sealed class SliceKeysTests
{
    [Fact]
    public void ForPlayer_names_the_committed_row_by_prefixing_the_player_id()
    {
        var key = SliceKeys.ForPlayer(new PlayerId("PLAYER_00000001"));

        key.ShouldBe(
            "player.PLAYER_00000001",
            "the committed row is addressed by the player's own id under a 'player.' prefix; anything " +
            "that rewrites the id makes two different players' rows collidable.");
    }

    [Fact]
    public void ForRun_names_the_archive_row_by_prefixing_the_run_id()
    {
        var key = SliceKeys.ForRun(new RunId("RUN_PLAYER_00000001_1"));

        key.ShouldBe(
            "run.RUN_PLAYER_00000001_1",
            "an ended run is archived under the id the domain already guarantees distinct, so no later " +
            "run can displace an earlier one's finished row.");
    }

    [Theory]
    [InlineData("has a space")]
    [InlineData("has/a/slash")]
    [InlineData("has\\a\\backslash")]
    [InlineData("has:a:colon")]
    [InlineData("hät_an_accent")]
    public void ForPlayer_refuses_a_player_id_that_would_leave_the_caches_key_space(string id)
    {
        // The identity, not the symptom: every ArgumentException has a message, so ShouldNotBeEmpty
        // pinned nothing while the message it carried claimed the offending id is NAMED.
        Should.Throw<ArgumentException>(() => SliceKeys.ForPlayer(new PlayerId(id)))
            .Message.ShouldContain(
                id,
                Case.Sensitive,
                "an id outside the cache's key space must be refused here and NAMED, not escaped or " +
                "trimmed into something that happens to fit: two ids that mangle to one key read each " +
                "other's state.");
    }

    [Theory]
    [InlineData("has a space")]
    [InlineData("has/a/slash")]
    [InlineData("has\\a\\backslash")]
    [InlineData("has:a:colon")]
    [InlineData("hät_an_accent")]
    public void ForRun_refuses_a_run_id_that_would_leave_the_caches_key_space(string id)
    {
        Should.Throw<ArgumentException>(() => SliceKeys.ForRun(new RunId(id)))
            .Message.ShouldContain(
                id,
                Case.Sensitive,
                "the archive key is subject to the same closed key space as the committed one, and " +
                "its refusal must name the id it refused.");
    }

    [Fact]
    public void ForPlayer_refuses_a_player_id_whose_constructor_never_ran()
    {
        Should.Throw<ArgumentException>(() => SliceKeys.ForPlayer(default))
            .Message.ShouldNotBeEmpty(
                "default(PlayerId) carries no text at all, so it names no row — refusing it here beats " +
                "building the key 'player.' and reading whatever is under it.");
    }

    [Fact]
    public void ForRun_refuses_a_run_id_whose_constructor_never_ran()
    {
        Should.Throw<ArgumentException>(() => SliceKeys.ForRun(default))
            .Message.ShouldNotBeEmpty("default(RunId) names no archived run.");
    }

    /// <summary>
    /// The positive control for the two refusal cases above: a rule that refused everything would
    /// satisfy them and store nothing.
    /// </summary>
    [Theory]
    [InlineData("PLAYER_00000001")]
    [InlineData("a-b.c_d-9")]
    public void ForPlayer_produces_a_key_the_cache_itself_accepts(string id)
    {
        Should.NotThrow(() => new InMemoryLocalCache().Seed(SliceKeys.ForPlayer(new PlayerId(id)), new byte[] { 1 }));
    }

    /// <inheritdoc cref="ForPlayer_produces_a_key_the_cache_itself_accepts"/>
    [Theory]
    [InlineData("RUN_PLAYER_00000001_1")]
    [InlineData("a-b.c_d-9")]
    public void ForRun_produces_a_key_the_cache_itself_accepts(string id)
    {
        Should.NotThrow(() => new InMemoryLocalCache().Seed(SliceKeys.ForRun(new RunId(id)), new byte[] { 1 }));
    }
}
