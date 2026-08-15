namespace SlayIdleRepeat.Core.Primitives;

/// <summary>A value, or a non-empty description of why there is none — the return of every validating factory in the domain.</summary>
/// <typeparam name="T">The value a success carries.</typeparam>
/// <remarks>
/// One validated entry point for every persisted state in the game: a corrupt row fails loudly at
/// the seam rather than silently three rules later.
/// <para>
/// This is not the command-rejection channel — that is <c>CommandResult</c> +
/// <see cref="RejectionReason"/>. The two look similar and mean opposite things: a
/// <see cref="RejectionReason"/> is the game correctly saying no to a legal request, and a failed
/// <see cref="Result{T}"/> is state that should never have existed. Conflating them would let a
/// corrupt database row reach a player as a polite "not enough energy".
/// </para>
/// <para>
/// Deliberately minimal: no error hierarchy, no captured exception, no <c>Map</c>/<c>Bind</c>
/// combinator library.
/// </para>
/// <para>
/// A class, not a struct: <c>default(Result&lt;T&gt;)</c> would be a result that is neither a
/// success nor a failure, reachable from an uninitialised field.
/// </para>
/// </remarks>
public sealed class Result<T>
{
    private readonly T _value;
    private readonly string _error;

    private Result(bool isSuccess, T value, string error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>Whether this result carries a value.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether this result carries a failure description.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The value. Throws when this result is a failure, quoting the failure description.</summary>
    /// <exception cref="InvalidOperationException">This result is a failure.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(
            $"This Result has no value: {_error}. Reading the value without checking IsSuccess " +
            "first moves the failure three rules deeper, where the stack trace names the reader " +
            "instead of the row.");

    /// <summary>The failure description, never null or blank. Throws when this result is a success.</summary>
    /// <exception cref="InvalidOperationException">This result is a success.</exception>
    public string Error => IsSuccess
        ? throw new InvalidOperationException(
            "This Result succeeded, so it has no error to read. Answering with an empty string would " +
            "be a value a caller can branch on, and the branch would be wrong.")
        : _error;

    /// <summary>A result carrying <paramref name="value"/>.</summary>
    /// <param name="value">The value. Never null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Result<T> Success(T value)
    {
        // A genuine null check, never a `default(T)` comparison: for a value type the default is an
        // ordinary value — Result<int>.Success(0) is a zero balance, not an absence.
        ArgumentNullException.ThrowIfNull(value);

        return new Result<T>(isSuccess: true, value, string.Empty);
    }

    /// <summary>A result carrying no value and the reason there is none.</summary>
    /// <param name="error">Why there is no value. Never null, empty or whitespace.</param>
    /// <exception cref="ArgumentException"><paramref name="error"/> is null, empty or whitespace.</exception>
    public static Result<T> Failure(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "A failed Result must say why it failed. A blank description is the silent " +
                "corruption 30 §11.3 is written against: the caller learns that something is wrong " +
                "and nothing about what.",
                nameof(error));
        }

        return new Result<T>(isSuccess: false, default!, error);
    }
}
