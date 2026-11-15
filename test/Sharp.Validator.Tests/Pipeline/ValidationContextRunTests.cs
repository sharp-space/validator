namespace Sharp.Validator.Tests.Pipeline;

// A run started on a context whose run hadn't ended took that run's errors, element sources and selection, and reset
// them, so the outer validation silently reported what the inner one left. README forbade it, but nothing stopped it. A
// context now runs one validation at a time and refuses a second, whichever entry point starts it.
public sealed class ValidationContextRunTests
{
    private static Validator<Order> NameRequired() => new(b => b.Property(x => x.Name).NotEmpty());

    [Fact]
    public void Validate_StartedByARuleOnTheContextItReceived_Throws()
    {
        var inner = NameRequired();
        var outer = new Validator<Order>(b => b.Property(x => x.Name).Must((_, context) => inner.Validate(context).IsValid));
        var context = new ValidationContext<Order>(new Order { Name = "x" });
        var exception = Assert.Throws<InvalidOperationException>(() => outer.Validate(context));
        Assert.Contains("one validation on a context at a time", exception.Message, StringComparison.Ordinal);

        // The refusal ended the outer run through its catch, so the context runs again.
        Assert.True(inner.Validate(context).IsValid);
    }

    [Fact]
    public void Validate_RefusedInsideARule_LeavesTheRunningValidationsErrors()
    {
        var inner = NameRequired();
        var refused = false;
        var outer = new Validator<Order>(b =>
        {
            b.Property(x => x.Quantity).GreaterThan(0);
            b.Property(x => x.Name).Must((_, context) =>
            {
                try
                {
                    inner.Validate(context);
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }

                return true;
            });
        });

        var result = outer.Validate(new Order { Name = "x", Quantity = 0 });

        Assert.True(refused);
        Assert.Equal("Quantity", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void ValidateAsync_OnTheContextOfARunningSyncValidation_ReturnsTheRefusalInItsTask()
    {
        var inner = NameRequired();
        ValueTask<ValidationResult> nested = default;
        var outer = new Validator<Order>(b => b.Property(x => x.Name).Must((_, context) =>
        {
            nested = inner.ValidateAsync(context);
            return true;
        }));

        Assert.True(outer.Validate(new Order { Name = "x" }).IsValid);
        Assert.True(nested.IsFaulted);
        Assert.IsType<InvalidOperationException>(nested.AsTask().Exception?.InnerException);
    }

    [Fact]
    public async Task ValidateAsync_StartedWhileAnotherIsPending_FaultsAndLeavesTheFirst()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validator = new Validator<Order>(b => b.Property(x => x.Name).MustAsync(_ => gate.Task));
        var context = new ValidationContext<Order>(new Order { Name = "x" });
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = validator.ValidateAsync(context, cancellationToken);
        var second = validator.ValidateAsync(context, cancellationToken);
        var secondFaultedAtOnce = second.IsFaulted;
        gate.SetResult(false);

        Assert.True(secondFaultedAtOnce);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
        Assert.Single((await first).Errors);
        Assert.Single((await validator.ValidateAsync(context, cancellationToken)).Errors);
    }

    [Fact]
    public void Validate_StartedOnAnIncludedChildsContext_Throws()
    {
        var probe = new Validator<Line>(b => b.Property(x => x.Sku).NotEmpty());
        var child = new Validator<Line>(b => b.Property(x => x.Sku).Must((_, context) => probe.Validate(context).IsValid));
        var parent = new Validator<Order>(b => b.Property(x => x.Line).Include(child));
        Assert.Throws<InvalidOperationException>(() => parent.Validate(new Order { Line = new Line { Sku = "x" } }));
    }

    private sealed class Order
    {
        public string? Name { get; init; }

        public int Quantity { get; init; }

        public Line? Line { get; init; }
    }

    private sealed class Line
    {
        public string? Sku { get; init; }
    }
}