using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// `30` §11.3 — <see cref="Result{T}"/>, the return of <c>Rehydrate</c>: <i>"one validated entry
/// point for every persisted state in the game — a corrupt row fails loudly at the seam rather
/// than silently three rules later."</i>
/// </summary>
/// <remarks>
/// <para>
/// Every test here is about <b>loudness</b>. A <c>Result</c> that answered a failed read with
/// <c>default</c> would carry the corrupt row three rules deeper and produce a
/// <c>NullReferenceException</c> in a calculator, which is the failure mode §11.3 exists to
/// prevent — the seam is where a corrupt row is supposed to stop.
/// </para>
/// <para>
/// 🔒 <see cref="Result{T}"/> is deliberately <b>not</b> the command-rejection channel. That is
/// <c>CommandResult</c> + <see cref="RejectionReason"/> (M1-06). Conflating them would let a
/// corrupt database row masquerade as a legal-move refusal.
/// </para>
/// </remarks>
public sealed class ResultTests
{
    private sealed record Row(string Name);

    [Fact]
    public void A_success_carries_its_value()
    {
        var row = new Row("player-1");
        var result = Result<Row>.Success(row);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBeSameAs(row);
    }

    [Fact]
    public void A_failure_carries_its_description()
    {
        var result = Result<Row>.Failure("SchemaVersion 7 is unknown to this build");

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("SchemaVersion 7 is unknown to this build");
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws_rather_than_returning_default()
    {
        var result = Result<Row>.Failure("SchemaVersion 7 is unknown to this build");

        var thrown = Should.Throw<InvalidOperationException>(() => result.Value);

        thrown.Message.ShouldMatchWildcard(
            "*SchemaVersion 7 is unknown to this build*",
            "the failure description has to reach the exception. A Result whose refusal does not quote " +
            "the reason turns 'a corrupt row fails loudly at the seam' (30 §11.3) into a stack trace " +
            "that names the reader instead of the row.");

        thrown.Message.ShouldContain(
            "Result",
            Case.Sensitive,
            "and the refusal must say it was a Result that refused. The stack trace names the reader's " +
            "own line, so without this word the exception reads as a bug where the value was used " +
            "rather than a corrupt row that was never loaded.");
    }

    [Fact]
    public void Reading_the_error_of_a_success_throws()
    {
        var result = Result<Row>.Success(new Row("player-1"));

        Should.Throw<InvalidOperationException>(() => result.Error)
            .Message.ShouldMatchWildcard(
                "*succeeded*",
                "a successful Result has no error text, and answering with an empty string would be a " +
                "lie a caller can branch on.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_failure_requires_a_non_empty_description(string? blank)
    {
        Should.Throw<ArgumentException>(() => Result<Row>.Failure(blank!))
            .Message.ShouldMatchWildcard(
                "*30 §11.3*",
                "a failure with nothing to say is exactly the silent corruption 30 §11.3 is written " +
                "against — the refusal must cite it.");
    }

    [Fact]
    public void A_success_refuses_a_null_value()
    {
        Should.Throw<ArgumentNullException>(() => Result<Row>.Success(null!));
    }

    /// <summary>
    /// <c>Success(default(T))</c> for a value type is a success, not a null.
    /// </summary>
    /// <remarks>
    /// The null guard on <c>Success</c> has to be a genuine null check
    /// (<see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>, which sees a boxed
    /// <c>0</c> and passes) rather than a <c>default(T)</c> comparison, which would refuse the
    /// perfectly ordinary <c>Result&lt;int&gt;.Success(0)</c> — and refuse it only for the values a
    /// rehydrated row is most likely to hold.
    /// </remarks>
    [Fact]
    public void A_value_type_result_accepts_the_default_of_its_type()
    {
        var result = Result<int>.Success(0);

        result.IsSuccess.ShouldBeTrue(
            "0 is a value, not an absence. A guard written as a default(T) comparison rather than a " +
            "null check turns every zero balance and every unset counter into a load failure.");
        result.Value.ShouldBe(0);
    }
}
