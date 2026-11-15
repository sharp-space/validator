using Sharp.Validator.Result;

namespace Sharp.Validator;

/// <summary>
/// Per-validator options. Set via the <c>configure</c> callback on each rule; they are read when the callback returns, and are read-only from then on.
/// </summary>
public sealed class PropertyValidatorOptions
{
    // Null while the options are writable; once read, the message that says when.
    private string? _readOnlyMessage;

    /// <summary>Severity stamped on the produced <see cref="ValidationError" />.</summary>
    public ValidationErrorSeverity? ErrorSeverity
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Error code stamped on the produced <see cref="ValidationError" />.</summary>
    public string? ErrorCode
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Error message template. Supports <c>{PropertyName}</c>, <c>{PropertyValue}</c>, and validator-specific placeholders.</summary>
    public string? ErrorMessage
    {
        get;
        set => field = Writable(value);
    }

    /// <summary>Set when the rule's <c>configure</c> or its chain set the message, which then wins over a default message by code.</summary>
    internal bool IsMessageOverridden { get; set; }

    internal void MakeReadOnly(string message = ReadOnlyOptions.ReadWhenTheCallbackReturned) => _readOnlyMessage = message;

    private T Writable<T>(T value) =>
        _readOnlyMessage is { } message ? throw new InvalidOperationException(message) : value;

    internal PropertyValidatorOptions Clone() => new()
    {
        ErrorSeverity = ErrorSeverity,
        ErrorCode = ErrorCode,
        ErrorMessage = ErrorMessage,
        IsMessageOverridden = IsMessageOverridden
    };
}