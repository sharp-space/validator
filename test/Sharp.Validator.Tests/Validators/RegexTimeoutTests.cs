using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

// Regression: callers had no way to override the regex match timeout for string/Func<string> patterns; the 2-second
// default was hard-coded. The new overloads accept a TimeSpan.
// A timeout is a validation failure, not an exception: the guard exists to stop catastrophic backtracking on hostile
// input, and letting it escape would turn the ReDoS defense into an exception-based DoS (an unhandled 500 per request).
public sealed class RegexTimeoutTests
{
    [Fact]
    public void Regex_WithCustomTimeout_FailsValidationOnCatastrophicBacktracking()
    {
        // Classic exponential-blowup pattern with a short timeout: the validator must neither hang nor throw — the
        // input the engine refused to finish is reported as invalid.
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Regex("^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(20)));

        // 30+ 'a' chars followed by '!' makes the engine backtrack until timeout.
        var input = new string('a', 35) + "!";
        var result = validator.Validate(new Person { Name = input });
        Assert.False(result.IsValid);
        Assert.Equal("Name", result.Errors[0].PropertyName);
    }

    [Fact]
    public void RegexDefaultTimeout_StillAppliesWhenNoCustomTimeoutSupplied()
    {
        // Sanity: the no-timeout overload is unchanged — fast patterns still validate fine.
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Regex("^[A-Z]+$"));

        Assert.True(validator.Validate(new Person { Name = "HELLO" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "hello" }).IsValid);
    }
}