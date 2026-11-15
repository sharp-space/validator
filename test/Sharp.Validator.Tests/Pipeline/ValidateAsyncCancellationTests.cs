namespace Sharp.Validator.Tests.Pipeline;

// ValidateAsync observed a canceled token only where a path happened to check it. A validator without async rules took
// the sync fast path and ran every rule, a chain of completed async rules ran too, and only an element chain stopped.
// It also threw a sync rule's exception from the call itself, while the async path returned it in the task. Now a
// canceled token stops the validation before any rule runs and before each later chain, and a rule's exception or the
// cancellation comes back in the returned ValueTask on every path. Only a null argument still throws from the call.
// An async condition and an included validator stop at the same boundaries as the chains around them.
public sealed class ValidateAsyncCancellationTests
{
    private static CancellationToken Canceled => new(true);

    [Fact]
    public async Task CanceledToken_BeforeItsRules_StopsAValidatorWithoutAsyncRules()
    {
        var calls = 0;
        var validator = new Validator<Order>(x => x.Property(order => order.Name).Must(_ => ++calls > 0));

        var pending = validator.ValidateAsync(new Order { Name = "a" }, Canceled);
        Assert.True(pending.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CanceledToken_BeforeTheyRun_StopsCompletedAsyncRules()
    {
        var calls = 0;
        var validator = new Validator<Order>(x => x.Property(order => order.Name)
            .MustAsync((_, _) => Task.FromResult(++calls > 0)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Order { Name = "a" }, Canceled));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CanceledToken_BeforeItsRules_StopsAnElementChain()
    {
        var calls = 0;
        var validator = new Validator<Order>(x => x.Elements(order => order.Lines!)
            .MustAsync((_, _) => Task.FromResult(++calls > 0)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Order { Lines = ["a"] }, Canceled));

        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    // A walk over a list settles a rule whose task has completed in place, so a token that one element's rule cancels
    // stops the walk from the walk's own call before the next element, not from its task. The validator stays usable.
    public async Task Cancellation_DuringAListWalk_StopsBeforeTheNextElement(bool elements)
    {
        using var source = new CancellationTokenSource();
        var calls = 0;

        var validator = new Validator<Order>(x =>
        {
            if (elements) x.Elements(order => order.Lines!).MustAsync(CancelOnFirst);
            else x.Elements(order => order.Items!).Property(item => item.Sku).MustAsync(CancelOnFirst);
        });

        var order = new Order { Lines = ["a", "b"], Items = [new Item { Sku = "a" }, new Item { Sku = "b" }] };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(order, source.Token));

        Assert.Equal(1, calls);
        Assert.True((await validator.ValidateAsync(order, TestContext.Current.CancellationToken)).IsValid);
        Assert.Equal(3, calls);

        return;

        Task<bool> CancelOnFirst(string? value)
        {
            if (++calls == 1) source.Cancel();

            return Task.FromResult(value != null);
        }
    }

    [Fact]
    public async Task Cancellation_DuringOneChain_StopsTheNextChain()
    {
        using var source = new CancellationTokenSource();
        var laterCalls = 0;
        var validator = new Validator<Order>(x =>
        {
            x.Property(order => order.Name).MustAsync((_, _) =>
            {
                source.Cancel();
                return Task.FromResult(true);
            });

            x.Property(order => order.Code).MustAsync((_, _) => Task.FromResult(++laterCalls > 0));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Order { Name = "a", Code = "b" }, source.Token));

        Assert.Equal(0, laterCalls);
    }

    [Fact]
    public async Task CanceledToken_BeforeTheyRun_StopsAsyncConditions()
    {
        var calls = 0;
        var validator = new Validator<Order>(x =>
        {
            x.Where((_, _) => Task.FromResult(++calls > 0), () => x.Property(order => order.Name).NotEmpty());
            x.Property(order => order.Code).Where((_, _) => Task.FromResult(++calls > 0)).NotEmpty();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Order(), Canceled));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Cancellation_DuringAnAsyncCondition_StopsTheNextChain()
    {
        using var source = new CancellationTokenSource();
        var laterCalls = 0;
        var validator = new Validator<Order>(x =>
        {
            x.Property(order => order.Name).Where((_, _) =>
            {
                source.Cancel();
                return Task.FromResult(true);
            }).NotEmpty();

            x.Property(order => order.Code).MustAsync((_, _) => Task.FromResult(++laterCalls > 0));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Order { Name = "a", Code = "b" }, source.Token));

        Assert.Equal(0, laterCalls);
    }

    [Fact]
    public async Task CanceledToken_BeforeItsRules_StopsAnIncludedValidator()
    {
        var calls = 0;
        var child = new Validator<Item>(x => x.Property(item => item.Sku)
            .MustAsync((_, _) => Task.FromResult(++calls > 0)));

        var validator = new Validator<Order>(x => x.Property(order => order.Item).Include(child));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new Order { Item = new Item { Sku = "a" } }, Canceled));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Cancellation_InsideIncludedValidator_SkipsRemainingChains()
    {
        using var source = new CancellationTokenSource();
        var (childLater, parentLater) = (0, 0);
        var child = new Validator<Item>(x =>
        {
            x.Property(item => item.Sku).MustAsync((_, _) =>
            {
                source.Cancel();
                return Task.FromResult(true);
            });

            x.Property(item => item.Note).MustAsync((_, _) => Task.FromResult(++childLater > 0));
        });

        var validator = new Validator<Order>(x =>
        {
            x.Property(order => order.Item).Include(child);
            x.Property(order => order.Code).MustAsync((_, _) => Task.FromResult(++parentLater > 0));
        });

        var order = new Order { Item = new Item { Sku = "a", Note = "b" }, Code = "c" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(order, source.Token));

        Assert.Equal((0, 0), (childLater, parentLater));
    }

    [Fact]
    public async Task SyncRuleException_ComesBackInTheValueTask()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Name).Must(_ => throw new FormatException("boom")));
        var pending = validator.ValidateAsync(new Order { Name = "a" });
        Assert.True(pending.IsFaulted);
        await Assert.ThrowsAsync<FormatException>(async () => await pending);
    }

    [Fact]
    public void NullContext_StillThrowsFromTheCall()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Name).NotEmpty());
        Assert.Throws<ArgumentNullException>(() => validator.ValidateAsync((ValidationContext<Order>)null!, Canceled));
    }

    [Fact]
    public async Task Context_AfterACanceledRun_ValidatesAgain()
    {
        using var source = new CancellationTokenSource();
        var validator = new Validator<Order>(x =>
        {
            x.Property(order => order.Name).MustAsync((_, _) =>
            {
                source.Cancel();
                return Task.FromResult(false);
            });

            x.Property(order => order.Code).MustAsync((code, _) => Task.FromResult(!string.IsNullOrEmpty(code)));
        });

        var context = new ValidationContext<Order>(new Order { Name = "a", Code = "" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await validator.ValidateAsync(context, source.Token));

        var result = await validator.ValidateAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal(["Code", "Name"], result.Errors.Select(error => error.PropertyName).Order());
    }

    private sealed class Order
    {
        public string? Name { get; init; }

        public string? Code { get; init; }

        public List<string?>? Lines { get; init; }

        public Item? Item { get; init; }

        public List<Item>? Items { get; init; }
    }

    private sealed class Item
    {
        public string? Sku { get; init; }

        public string? Note { get; init; }
    }
}