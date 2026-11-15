namespace Sharp.Validator.Tests.Accessors;

// Non-parameter-rooted selectors (closure fields, static members) used to crash at rule construction:
// the null-safe accessor builder assumed the chain was rooted at the model parameter. Worse, the expression.ToString()
// cache key renders captures as value(DisplayClass), so once the crash was "fixed" naively, two instances with
// different captured values would silently share one compiled accessor.
public sealed class ClosureTargetAccessorTests
{
    [Fact]
    public void ClosureFieldTarget_SeparateInstances_DoNotCrossPollute()
    {
        var strict = new ThresholdValidator(30);
        var lenient = new ThresholdValidator(18);
        var person = new Person { Age = 20 };
        Assert.True(lenient.Validate(person).IsValid);
        Assert.False(strict.Validate(person).IsValid);

        // And again in the other order — whichever compiled first must not have poisoned a shared cache.
        Assert.True(lenient.Validate(person).IsValid);
    }

    [Fact]
    public void StaticRootedTarget_Compiles()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).GreaterThanOrEqual(p => AgePolicy.Minimum));
        Assert.True(validator.Validate(new Person { Age = 21 }).IsValid);
        Assert.False(validator.Validate(new Person { Age = 20 }).IsValid);
    }

    [Fact]
    public void NullIntermediate_InClosureChain_SkipsTheRule()
    {
        var withTarget = new NullableCaptureValidator(new Address { City = "Seoul" });
        Assert.True(withTarget.Validate(new Person { Name = "Seoul" }).IsValid);
        Assert.False(withTarget.Validate(new Person { Name = "Busan" }).IsValid);
    }

    [Fact]
    // A chain's own selector rooted at `this` reads the validator rather than the model and has no path to report its
    // errors under, so it is rejected where it is declared. A comparison target may still start there (above).
    public void CapturedThisPropertySelector_IsRejectedWhereItIsDeclared()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new FallbackValidator("x").Register());
        Assert.Contains("doesn't start at its parameter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    // A target that started at a captured or static value was named after the variable or member, as in 'Age' must be
    // equal to 'limit'. Only a member of the model is another property, so such a target is printed by its value.
    public void CapturedOrStaticTarget_IsPrintedByItsValue()
    {
        // ReSharper disable once ConvertToConstant.Local — the captured local is the case under test.
        var limit = 7;
        var captured = new Validator<Person>(x => x.Property(person => person.Age).Equal(person => limit));
        var fromStatic = new Validator<Person>(x => x.Property(person => person.Age).GreaterThanOrEqual(person => AgePolicy.Minimum));

        Assert.Equal("'Age' must be equal to '7'.", Assert.Single(captured.Validate(new Person { Age = 1 }).Errors).Message);

        Assert.Equal("'Age' must be greater than or equal to '21'.",
            Assert.Single(fromStatic.Validate(new Person { Age = 1 }).Errors).Message);
    }

    [Fact]
    // Only a chain that starts at a captured or static value loses its name. One that reads the model through a cast or
    // an indexer is still another property, named so that a secret it holds isn't printed.
    public void ModelTargetThroughACastOrAnIndexer_KeepsItsName()
    {
        var cast = new Validator<Account>(x => x.Property(account => account.Confirm).Equal(account => ((SecretAccount)account).Secret));
        var indexed = new Validator<Account>(x => x.Property(account => account.Confirm)
            .Equal(account => account.History![0].Confirm, options => options.ErrorMessage = "{TargetPropertyName}"));

        var castError = Assert.Single(cast.Validate(new SecretAccount { Confirm = "a", Secret = "hunter2" }).Errors);
        var indexedError = Assert.Single(indexed.Validate(new Account { Confirm = "a", History = [new Account { Confirm = "b" }] }).Errors);
        Assert.Equal("'Confirm' must be equal to 'Secret'.", castError.Message);
        Assert.Equal("Confirm", indexedError.Message);
    }

    private sealed class ThresholdValidator : Validator<Person>
    {
        // Captured by the comparison target selector below — the chain root is `this`, not the model.
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly int _minimumAge;

        public ThresholdValidator(int minimumAge)
        {
            _minimumAge = minimumAge;
            Builder.Property(x => x.Age).GreaterThanOrEqual(x => _minimumAge);
        }
    }

    private static class AgePolicy
    {
        public static int Minimum => 21;
    }

    private sealed class NullableCaptureValidator : Validator<Person>
    {
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable — read through `this`, as above.
        private readonly Address? _reference;

        public NullableCaptureValidator(Address? reference)
        {
            _reference = reference;

            // Intermediate (_reference) is null-guarded: a null capture skips the comparison entirely.
            Builder.Property(x => x.Name).Equal(x => _reference!.City);
        }
    }

    private sealed class FallbackValidator(string? fallback) : Validator<Person>
    {
        private string? Fallback { get; } = fallback;

        public void Register() => Builder.Property(x => Fallback).NotEmpty();
    }

    private class Account
    {
        public string? Confirm { get; init; }

        public List<Account>? History { get; init; }
    }

    private sealed class SecretAccount : Account
    {
        public string? Secret { get; init; }
    }
}