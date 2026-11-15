using System.Reflection;

namespace Sharp.Validator.Tests.Pipeline;

// IValidator<T> asked an implementation for seven members, five of which build a context and call one of the other two,
// so a decorator or a test double wrote all seven, and no member could be added later without breaking it. It asks for
// the two that take a context; the rest are default implementations, which Validator<T> declares for itself.
public sealed class ValidatorInterfaceTests
{
    [Fact]
    public async Task DefaultMembers_BuildAContextAndCallTheRequiredOnes()
    {
        var implementation = new ContextOnlyValidator();
        IValidator<Person> validator = implementation;
        IValidator untyped = implementation;
        var model = new Person { Email = "e" };
        Assert.False(validator.Validate(model).IsValid);
        Assert.False(validator.Validate(model, context => context.Parameters["seen"] = true).IsValid);
        Assert.False((await validator.ValidateAsync(model)).IsValid);
        Assert.False((await validator.ValidateAsync(model, context => context.Parameters["seen"] = true)).IsValid);
        Assert.False(untyped.Validate(model).IsValid);
        Assert.False((await untyped.ValidateAsync(model)).IsValid);
        Assert.Equal(["sync e", "sync e", "async e", "async e", "sync e", "async e"], implementation.Seen);
    }

    [Fact]
    public void DefaultMembers_RunTheConfigureBeforeValidating()
    {
        IValidator<Person> validator = new ContextOnlyValidator();
        var model = new Person();
        var result = validator.Validate(model, context => context.IncludeProperty(person => person.Email));
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task DefaultMembers_RejectANullConfigureOrModel()
    {
        IValidator<Person> validator = new ContextOnlyValidator();
        IValidator untyped = validator;

        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => validator.Validate(new Person(), null!)).ParamName);

        Assert.Equal("configure",
            (await Assert.ThrowsAsync<ArgumentNullException>(async () => await validator.ValidateAsync(new Person(), null!))).ParamName);

        Assert.Equal("model", Assert.Throws<ArgumentNullException>(() => untyped.Validate(null!)).ParamName);

        Assert.Equal("model",
            (await Assert.ThrowsAsync<ArgumentNullException>(async () => await untyped.ValidateAsync(null!))).ParamName);
    }

    [Fact]
    // Validator<T> declares every member the interface defaults, so a call through the interface runs its own.
    public void Validator_DeclaresEveryDefaultedMember()
    {
        var typed = typeof(Validator<Person>).GetInterfaceMap(typeof(IValidator<Person>));
        var untyped = typeof(Validator<Person>).GetInterfaceMap(typeof(IValidator));

        // IValidator<T>'s own bodies for IValidator's members are private methods of the interface; skip them.
        MethodInfo[] targets =
        [
            .. typed.InterfaceMethods.Select((method, index) => (method, index)).Where(pair => pair.method.IsPublic)
                .Select(pair => typed.TargetMethods[pair.index]),
            .. untyped.TargetMethods
        ];

        Assert.Equal(8, targets.Length);
        Assert.All(targets, method => Assert.Equal(typeof(Validator<Person>), method.DeclaringType));
    }

    // Only the two required members, so every other call goes through a default implementation.
    private sealed class ContextOnlyValidator : IValidator<Person>
    {
        private readonly Validator<Person> _inner = new(x => x.Property(person => person.Name).NotEmpty());

        public List<string> Seen { get; } = [];

        public ValidationResult Validate(ValidationContext<Person> context)
        {
            Seen.Add($"sync {context.Model.Email}");
            return _inner.Validate(context);
        }

        public ValueTask<ValidationResult> ValidateAsync(ValidationContext<Person> context, CancellationToken cancellationToken = default)
        {
            Seen.Add($"async {context.Model.Email}");
            return _inner.ValidateAsync(context, cancellationToken);
        }
    }
}