namespace Sharp.Validator.Tests.Pipeline;

// Regression: a block Where was copied onto every chain it wraps, so it ran once per chain. Three chains ran an async
// block condition three times, an element chain and its child ran it three times, and a chain with sync and async rules
// ran its own sync condition in both phases. An I/O condition was called that often per validation and could answer
// differently between calls. Each condition now runs at most once per validation.
public sealed class WhereMemoizationTests
{
    [Fact]
    public void SyncBlockCondition_RunsOnceForAllItsChains()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Sync(true), () =>
        {
            x.Property(order => order.Name).NotEmpty();
            x.Property(order => order.Code).NotEmpty();
            x.Property(order => order.Quantity).GreaterThan(0);
        }));

        Assert.Equal(3, validator.Validate(new Order()).Errors.Count);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task AsyncBlockCondition_RunsOnceForAllItsChains()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Async(true), () =>
        {
            x.Property(order => order.Name).NotEmpty();
            x.Property(order => order.Code).MustAsync(_ => Task.FromResult(false));
            x.Property(order => order.Quantity).GreaterThan(0);
        }));

        Assert.Equal(3, (await validator.ValidateAsync(new Order { Code = "c" })).Errors.Count);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task BlockCondition_ElementChainWithChild_RunsOnce()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Async(true), () =>
        {
            var lines = x.Elements(order => order.Lines!);
            lines.NotNull();
            lines.Property(line => line.Sku).NotEmpty();
        }));

        var result = await validator.ValidateAsync(new Order { Lines = [new Line()] });
        Assert.Equal("Lines[0].Sku", Assert.Single(result.Errors).PropertyName);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void ElementChainCondition_ChainWithChild_RunsOnce()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x =>
        {
            var lines = x.Elements(order => order.Lines!).Where(_ => counter.Sync(true));
            lines.NotNull();
            lines.Property(line => line.Sku).NotEmpty();
        });

        Assert.Single(validator.Validate(new Order { Lines = [new Line()] }).Errors);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Chain_WithSyncAndAsyncRules_RunsItsSyncConditionOnce()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Property(order => order.Name)
            .Where(_ => counter.Sync(true))
            .NotEmpty()
            .MustAsync(_ => Task.FromResult(false)));

        Assert.Single((await validator.ValidateAsync(new Order { Name = "x" })).Errors);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task SyncBlockCondition_RunsOnceAcrossTheSyncAndAsyncPhases()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Sync(true), () =>
        {
            x.Property(order => order.Name).NotEmpty();
            x.Property(order => order.Code).MustAsync(_ => Task.FromResult(false));
            x.Property(order => order.Quantity).Where(_ => Task.FromResult(true)).GreaterThan(0);
        }));

        Assert.Equal(3, (await validator.ValidateAsync(new Order { Code = "c" })).Errors.Count);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task FalseBlockCondition_SkipsEveryChainAfterOneRun()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Sync(false), () =>
        {
            x.Property(order => order.Name).NotEmpty();
            x.Property(order => order.Code).MustAsync(_ => Task.FromResult(false));
            x.Property(order => order.Quantity).Where(_ => Task.FromResult(true)).GreaterThan(0);
        }));

        Assert.True((await validator.ValidateAsync(new Order { Code = "c" })).IsValid);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void EachValidation_RunsTheConditionAgain()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Sync(true), () =>
        {
            x.Property(order => order.Name).NotEmpty();
            x.Property(order => order.Code).NotEmpty();
        }));

        var context = new ValidationContext<Order>(new Order());

        validator.Validate(new Order());
        validator.Validate(context);
        validator.Validate(context);

        Assert.Equal(3, counter.Count);
    }

    [Fact]
    public void SharedCondition_RunsForASelectedChainOnlyOnce()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x => x.Where(_ => counter.Sync(true), () =>
        {
            x.Property(order => order.Name).NotEmpty();
            x.Property(order => order.Code).NotEmpty();
            x.Property(order => order.Quantity).GreaterThan(0);
        }));

        var result = validator.Validate(new Order(), context =>
        {
            context.IncludeProperty(order => order.Code);
            context.IncludeProperty(order => order.Quantity);
        });

        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void SharedCondition_WhenEveryChainItGatesIsExcluded_DoesNotRun()
    {
        var counter = new Counter();
        var validator = new Validator<Order>(x =>
        {
            x.Where(_ => counter.Sync(true), () => x.Property(order => order.Name).NotEmpty());
            x.Property(order => order.Code).NotEmpty();
        });

        validator.Validate(new Order(), context => context.IncludeProperty(order => order.Code));

        Assert.Equal(0, counter.Count);
    }

    [Fact]
    // More shared conditions than one word of results holds, some true and some false.
    public void SharedConditionOfALargeValidator_RunsOnce()
    {
        var counters = Enumerable.Range(0, 70).Select(_ => new Counter()).ToArray();
        var validator = new Validator<Order>(x =>
        {
            for (var index = 0; index < counters.Length; index++)
            {
                var counter = counters[index];
                var holds = index % 3 != 0;
                x.Where(_ => counter.Sync(holds), () =>
                {
                    x.Property(order => order.Name).NotEmpty();
                    x.Property(order => order.Code).NotEmpty();
                });
            }
        });

        Assert.Equal(2 * counters.Where((_, index) => index % 3 != 0).Count(), validator.Validate(new Order()).Errors.Count);
        Assert.All(counters, counter => Assert.Equal(1, counter.Count));
    }

    private sealed class Order
    {
        public string? Name { get; init; }

        public string? Code { get; init; }

        public int Quantity { get; init; }

        public List<Line>? Lines { get; init; }
    }

    private sealed class Line
    {
        public string? Sku { get; init; }
    }

    private sealed class Counter
    {
        public int Count;

        public bool Sync(bool result)
        {
            Interlocked.Increment(ref Count);
            return result;
        }

        public async Task<bool> Async(bool result)
        {
            await Task.Yield();
            Interlocked.Increment(ref Count);

            return result;
        }
    }
}