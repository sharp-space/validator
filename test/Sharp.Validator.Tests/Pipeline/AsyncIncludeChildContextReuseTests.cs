namespace Sharp.Validator.Tests.Pipeline;

// An async Include created a new child context for every element, about 104 B each, while the sync path reused one per
// chain. The async element walks now hand their includes one reusable slot per chain run: the walk awaits an element's
// child before it binds the next, so the context is free again, as in the sync evaluators. Each element must still see
// its own model and none of the previous element's state, whether the child completes at once or suspends.
// A walk over a collection of fewer than two elements has nothing to reuse the context for, so it makes no slot and
// costs what it did before.
public sealed class AsyncIncludeChildContextReuseTests
{
    private static Basket WithLines(int count) => new()
    {
        Lines = [.. Enumerable.Range(0, count).Select(index => new Line { Sku = "s" + index, Quantity = index })],
        Array = [.. Enumerable.Range(0, count).Select(index => new Line { Sku = "a" + index })],
        Holders = [.. Enumerable.Range(0, count).Select(index => new Holder { Line = new Line { Sku = "h" + index } })]
    };

    [Fact]
    public async Task ElementSeesItsOwnModel_WhenTheChildSuspends()
    {
        var seen = new List<string?>();
        var child = new Validator<Line>(x => x.Property(line => line.Quantity).MustAsync(async (_, context, _) =>
        {
            await Task.Yield();
            seen.Add(context.Model.Sku);

            return true;
        }));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        await validator.ValidateAsync(WithLines(3));

        Assert.Equal(["s0", "s1", "s2"], seen);
    }

    [Fact]
    public async Task Errors_AcrossElements_ReportOnlyFailingElements()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Quantity)
            .MustAsync((quantity, _) => Task.FromResult(quantity > 0)));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var result = await validator.ValidateAsync(new Basket
        {
            Lines = [new Line { Quantity = 0 }, new Line { Quantity = 1 }, new Line { Quantity = -1 }]
        });

        Assert.Equal(["Lines[0].Quantity", "Lines[2].Quantity"], result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public async Task ThrowingElement_LeavesTheParentContextReusable()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Quantity).MustAsync((quantity, _) =>
            quantity >= 0 ? Task.FromResult(quantity > 0) : throw new InvalidOperationException("boom")));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var context = new ValidationContext<Basket>(new Basket { Lines = [new Line { Quantity = 0 }, new Line { Quantity = -1 }] });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.ValidateAsync(context));

        var result = await validator.ValidateAsync(new ValidationContext<Basket>(new Basket { Lines = [new Line { Quantity = 0 }] }));
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task Derived_IncludesOfDifferentTypesShareAChain()
    {
        var special = new Validator<Special>(x => x.Property(item => item.Sku)
            .MustAsync((sku, _) => Task.FromResult(sku != null), options => options.ErrorMessage = "special"));

        var other = new Validator<Other>(x => x.Property(item => item.Sku)
            .MustAsync((sku, _) => Task.FromResult(sku != null), options => options.ErrorMessage = "other"));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Mixed).Include(special).Include(other));
        var result = await validator.ValidateAsync(new Basket { Mixed = [new Special(), new Other(), new Special()] });
        Assert.Equal(["special", "other", "special"], result.Errors.Select(error => error.Message));
    }

    [Fact]
    public async Task CanceledWalk_StopsBeforeTheNextElementThenValidatorIsReusable()
    {
        using var source = new CancellationTokenSource();
        var seen = new List<string?>();
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync(async (sku, _) =>
        {
            seen.Add(sku);
            await Task.Yield();
            if (sku == "stop") await source.CancelAsync();

            return !string.IsNullOrEmpty(sku);
        }));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var canceled = new Basket { Lines = [new Line { Sku = "stop" }, new Line { Sku = "next" }] };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(canceled, source.Token));

        Assert.Equal(["stop"], seen);

        var next = new Basket { Lines = [new Line { Sku = "a" }, new Line(), new Line { Sku = "b" }] };
        Assert.Equal("Lines[1].Sku", Assert.Single((await validator.ValidateAsync(next)).Errors).PropertyName);
    }

    [Fact]
    // The slot belongs to one walk, never to the validator, so walks of one validator that suspend at the same time
    // each bind their own child context: every result names its own element and formats its own value.
    public async Task Parallel_WalksOfOneValidatorKeepTheirOwnElements()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync(async (sku, _) =>
        {
            await Task.Yield();
            return sku?.StartsWith("bad", StringComparison.Ordinal) != true;
        }, options => options.ErrorMessage = "{PropertyValue}"));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(run => Task.Run(async () =>
        {
            var lines = Enumerable.Range(0, 20)
                .Select(index => new Line { Sku = (index == run % 20 ? "bad" : "ok") + run });

            return (Run: run, Result: await validator.ValidateAsync(new Basket { Lines = [.. lines] }));
        })));

        foreach (var (run, result) in results)
        {
            var error = Assert.Single(result.Errors);
            Assert.Equal(($"Lines[{run % 20}].Sku", $"bad{run}"), (error.PropertyName, error.Message));
        }
    }

    [Fact]
    public void AllValidCost_DoesNotGrowWithTheElementCount()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync((sku, _) => Task.FromResult(sku != null)));
        var list = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var array = new Validator<Basket>(x => x.Elements(basket => basket.Array).Include(child));

        Assert.Equal(AllocatedBytes(list, WithLines(2)), AllocatedBytes(list, WithLines(200)));
        Assert.Equal(AllocatedBytes(array, WithLines(2)), AllocatedBytes(array, WithLines(200)));
    }

    [Fact]
    public void AllValidCostOfAnElementPropertyInclude_DoesNotGrowWithTheElementCount()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync((sku, _) => Task.FromResult(sku != null)));
        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Holders).Property(holder => holder.Line).Include(child));
        Assert.Equal(AllocatedBytes(validator, WithLines(2)), AllocatedBytes(validator, WithLines(200)));
    }

    [Fact]
    public void EmptyListPaysNothing_ForAnInclude()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync((sku, _) => Task.FromResult(sku != null)));
        var include = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var rule = new Validator<Basket>(x => x.Elements(basket => basket.Lines).MustAsync((_, _) => Task.FromResult(true)));
        Assert.Equal(AllocatedBytes(rule, WithLines(0)), AllocatedBytes(include, WithLines(0)));
    }

    [Fact]
    public void SingleElement_CostsWhatTheSameValueCostsAsAProperty()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync((sku, _) => Task.FromResult(sku != null)));
        var list = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var array = new Validator<Basket>(x => x.Elements(basket => basket.Array).Include(child));
        var property = new Validator<Holder>(x => x.Property(holder => holder.Line).Include(child));
        var line = new Line { Sku = "s" };
        var single = AllocatedBytes(property, new Holder { Line = line });
        Assert.Equal(single, AllocatedBytes(list, new Basket { Lines = [line] }));
        Assert.Equal(single, AllocatedBytes(array, new Basket { Array = [line] }));
    }

    [Fact]
    public void SingleElementProperty_CostsWhatTheSameValueCostsAsAProperty()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).MustAsync((sku, _) => Task.FromResult(sku != null)));
        var elementProperty = new Validator<Basket>(x => x.Elements(basket => basket.Holders).Property(holder => holder.Line).Include(child));
        var property = new Validator<Holder>(x => x.Property(holder => holder.Line).Include(child));
        var holder = new Holder { Line = new Line { Sku = "s" } };
        Assert.Equal(AllocatedBytes(property, holder), AllocatedBytes(elementProperty, new Basket { Holders = [holder] }));
    }

    // Every rule here completes synchronously, so the whole validation runs on this thread and its count is exact.
    private static long AllocatedBytes<TModel>(Validator<TModel> validator, TModel model)
    {
        Assert.True(validator.ValidateAsync(model).IsCompletedSuccessfully);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = validator.ValidateAsync(model).Result;

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class Line
    {
        public string? Sku { get; init; }

        public int Quantity { get; init; }
    }

    private class Base
    {
        public string? Sku { get; init; }
    }

    private sealed class Special : Base;

    private sealed class Other : Base;

    private sealed class Holder
    {
        public Line? Line { get; init; }
    }

    private sealed class Basket
    {
        public List<Line>? Lines { get; init; }

        public Line[]? Array { get; init; }

        public List<Base>? Mixed { get; init; }

        public List<Holder>? Holders { get; init; }
    }
}