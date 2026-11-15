namespace Sharp.Validator.Tests.Pipeline;

// A collection chain reuses one child context across its elements instead of allocating a context and a result per
// element. What used to come from a fresh object now comes from resetting one, so each element must still see its own
// model and none of the previous element's rule state, errors, or chain bookkeeping.
public sealed class IncludeChildContextReuseTests
{
    private static Basket WithLines(int count) => new()
    {
        Lines = [.. Enumerable.Range(0, count).Select(index => new Line { Sku = "s" + index, Quantity = index })]
    };

    [Fact]
    public void Element_SeesItsOwnModel()
    {
        var seen = new List<string?>();
        var child = new Validator<Line>(x => x.Property(line => line.Quantity).Must((_, context) =>
        {
            seen.Add(context.Model.Sku);
            return true;
        }));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        validator.Validate(WithLines(3));

        Assert.Equal(["s0", "s1", "s2"], seen);
    }

    [Fact]
    public void Errors_AcrossElements_ReportOnlyFailingElements()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Quantity).GreaterThan(0));
        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));

        var result = validator.Validate(new Basket
        {
            Lines = [new Line { Quantity = 0 }, new Line { Quantity = 1 }, new Line { Quantity = -1 }]
        });

        Assert.Equal(["Lines[0].Quantity", "Lines[2].Quantity"], result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public void RuleMessage_DoesNotReachTheNextElement()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Quantity)
            .Must((value, context) =>
            {
                context.ErrorMessage = "element " + value;
                context.MessageParameters.Add("Mark", "m" + value);
                return value == 1;
            }));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var result = validator.Validate(new Basket
        {
            Lines = [new Line { Quantity = 0 }, new Line { Quantity = 1 }, new Line { Quantity = 2 }]
        });

        Assert.Equal(["element 0", "element 2"], result.Errors.Select(error => error.Message));
    }

    [Fact]
    public void Parameters_IncludedPerElement_AreSharedWithTheParent()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Quantity).Must((_, context) =>
        {
            context.Parameters["runs"] = (context.Parameters.TryGetValue("runs", out var runs) ? (int)runs! : 0) + 1;
            return true;
        }));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var context = new ValidationContext<Basket>(WithLines(3));
        validator.Validate(context);

        Assert.Equal(3, context.Parameters["runs"]);
    }

    [Fact]
    public void NestedRunOfTheSameChild_StaysIndependent()
    {
        var inner = new Validator<Line>(x => x.Property(line => line.Quantity).GreaterThan(0));

        var child = new Validator<Line>(x => x.Property(line => line.Sku).Must((_, context) =>
            inner.Validate(new Line { Quantity = 1 }).IsValid && context.Model.Quantity > 0));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var result = validator.Validate(new Basket
        {
            Lines = [new Line { Sku = "a", Quantity = 1 }, new Line { Sku = "b", Quantity = 0 }]
        });

        Assert.Equal(["Lines[1].Sku"], result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public void ThrowingElement_LeavesTheParentContextReusable()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Quantity).Must(value =>
            value >= 0 ? value > 0 : throw new InvalidOperationException("boom")));

        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var failing = new ValidationContext<Basket>(new Basket
        {
            Lines = [new Line { Quantity = 0 }, new Line { Quantity = -1 }]
        });

        Assert.Throws<InvalidOperationException>(() => validator.Validate(failing));

        Assert.Single(validator.Validate(new ValidationContext<Basket>(new Basket
        {
            Lines = [new Line { Quantity = 0 }]
        })).Errors);
    }

    [Fact]
    public void Derived_IncludesOfDifferentTypesShareAChain()
    {
        var special = new Validator<Special>(x => x.Property(item => item.Sku).NotEmpty(options => options.ErrorMessage = "special"));
        var other = new Validator<Other>(x => x.Property(item => item.Sku).NotEmpty(options => options.ErrorMessage = "other"));
        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Mixed).Include(special).Include(other));
        var result = validator.Validate(new Basket { Mixed = [new Special(), new Other(), new Special()] });
        Assert.Equal(["special", "other", "special"], result.Errors.Select(error => error.Message));
    }

    [Fact]
    public void AllValidCost_DoesNotGrowWithTheElementCount()
    {
        var child = new Validator<Line>(x => x.Property(line => line.Sku).NotEmpty());
        var validator = new Validator<Basket>(x => x.Elements(basket => basket.Lines).Include(child));
        var few = WithLines(2);
        var many = WithLines(200);
        Assert.Equal(AllocatedBytes(few), AllocatedBytes(many));

        return;

        long AllocatedBytes(Basket basket)
        {
            validator.Validate(basket);
            var before = GC.GetAllocatedBytesForCurrentThread();
            validator.Validate(basket);

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private sealed class Line
    {
        public string? Sku { get; init; }

        public int Quantity { get; init; }
    }

    private sealed class Special : Base;

    private sealed class Other : Base;

    private class Base
    {
        public string? Sku { get; init; }
    }

    private sealed class Basket
    {
        public List<Line>? Lines { get; init; }

        public List<Base>? Mixed { get; init; }
    }
}