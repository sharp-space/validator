namespace Sharp.Validator.Tests.Pipeline;

// Async chains run without a state machine per rule while each rule completes synchronously, and switch to awaiting at
// the first one that doesn't. Both halves record errors, so a chain crossing between them must keep rule order, Stop,
// and rule-scoped message state exactly as a fully awaited chain does.
public sealed class AsyncCompletionPathTests
{
    private static Func<string?, Task<bool>> Completed(bool outcome) => _ => Task.FromResult(outcome);

    private static Func<string?, Task<bool>> Suspended(bool outcome) => async _ =>
    {
        await Task.Yield();
        return outcome;
    };

    private static string[] Messages(ValidationResult result) => [.. result.Errors.Select(error => error.Message!)];

    [Fact]
    public async Task Rules_KeepTheirOrderAcrossTheSwitchToAwaiting()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MustAsync(Completed(false), options => options.ErrorMessage = "1")
            .MustAsync(Suspended(false), options => options.ErrorMessage = "2")
            .MustAsync(Completed(false), options => options.ErrorMessage = "3")
            .MustAsync(Suspended(false), options => options.ErrorMessage = "4"));

        Assert.Equal(["1", "2", "3", "4"], Messages(await validator.ValidateAsync(new Person { Name = "x" })));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stop_EndsTheChainOnEitherSideOfTheSwitch(bool suspended)
    {
        var ran = false;
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .MustAsync(suspended ? Suspended(false) : Completed(false))
            .MustAsync(_ =>
            {
                ran = true;
                return Task.FromResult(true);
            }));

        Assert.Single((await validator.ValidateAsync(new Person { Name = "x" })).Errors);
        Assert.False(ran);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Warning_DoesNotStopTheChain(bool suspended)
    {
        var validator = new Validator<Person>(x => x
            .Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .MustAsync(suspended ? Suspended(false) : Completed(false), options =>
            {
                options.ErrorMessage = "warning";
                options.ErrorSeverity = ValidationErrorSeverity.Warning;
            })
            .MustAsync(Completed(false), options => options.ErrorMessage = "error"));

        Assert.Equal(["warning", "error"], Messages(await validator.ValidateAsync(new Person { Name = "x" })));
    }

    [Fact]
    public async Task MessageStateSet_ByAPassingRule_DoesNotReachTheNextRule()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MustAsync((_, context, _) =>
            {
                context.ErrorMessage = "leaked";
                return Task.FromResult(true);
            })
            .MustAsync(Completed(false), options => options.ErrorMessage = "own"));

        Assert.Equal(["own"], Messages(await validator.ValidateAsync(new Person { Name = "x" })));
    }

    [Fact]
    public async Task PredicateThatThrowsBeforeItsTask_SurfacesThroughTheAwait()
    {
        static Task<bool> Throwing(string? _) => throw new InvalidOperationException("boom");
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MustAsync(Throwing));
        var pending = validator.ValidateAsync(new Person { Name = "x" });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
    }

    [Fact]
    public async Task CanceledPredicate_CancelsTheValidation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .MustAsync((_, token) => Task.FromCanceled<bool>(token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Person { Name = "x" }, cancellation.Token));
    }

    [Fact]
    public async Task NullValue_ReachesThePredicate()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MustAsync(Completed(false)));
        Assert.False((await validator.ValidateAsync(new Person { Name = null })).IsValid);
    }
}