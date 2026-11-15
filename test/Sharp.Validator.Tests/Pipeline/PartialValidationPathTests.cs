namespace Sharp.Validator.Tests.Pipeline;

// Regression: partial validation identified a rule by its leaf PropertyInfo. Selecting Billing.City also ran
// Shipping.City, and selecting Billing skipped Billing.City and came back valid. Selection now works on paths: a
// selected path covers everything beneath it, rules on its ancestors still run, and it reaches into included children.
public sealed class PartialValidationPathTests
{
    private static readonly Model Blank = new() { Billing = new Location(), Shipping = new Location() };

    private static Validator<Location> LocationRules() => new(x =>
    {
        x.Property(m => m.City).NotEmpty();
        x.Property(m => m.Zip).NotEmpty();
    });

    private static Validator<Model> NestedRules() => new(x =>
    {
        x.Property(m => m.Billing!.City).NotEmpty();
        x.Property(m => m.Billing!.Zip).NotEmpty();
        x.Property(m => m.Shipping!.City).NotEmpty();
    });

    private static Validator<Model> IncludedRules()
    {
        var location = LocationRules();
        return new Validator<Model>(x =>
        {
            x.Property(m => m.Billing).NotNull().Include(location);
            x.Property(m => m.Shipping).Include(location);
        });
    }

    private static IEnumerable<string> Names(ValidationResult result) => result.Errors.Select(x => x.PropertyName);

    [Fact]
    public void SelectingANestedPath_LeavesOutSiblingsWithTheSameMember() =>
        Assert.Equal(["Billing.City"], Names(NestedRules().Validate(Blank, c => c.IncludeProperty(m => m.Billing!.City))));

    [Fact]
    public void SelectingAParent_CoversThePathsBeneathIt() =>
        Assert.Equal(
            ["Billing.City", "Billing.Zip"],
            Names(NestedRules().Validate(Blank, c => c.IncludeProperty(m => m.Billing))));

    [Fact]
    public void SelectingAParent_RunsItsWholeIncludedChild() =>
        Assert.Equal(
            ["Billing.City", "Billing.Zip"],
            Names(IncludedRules().Validate(Blank, c => c.IncludeProperty(m => m.Billing))));

    [Fact]
    // A selection of the chain and of a path beneath it runs the child whole, whichever was selected first.
    public void SelectingAParentAndAPathBeneathIt_RunsItsWholeIncludedChild()
    {
        var pathFirst = IncludedRules().Validate(Blank, c =>
        {
            c.IncludeProperty(m => m.Billing!.City);
            c.IncludeProperty(m => m.Billing);
        });

        var parentFirst = IncludedRules().Validate(Blank, c =>
        {
            c.IncludeProperty(m => m.Billing);
            c.IncludeProperty(m => m.Billing!.City);
        });

        Assert.Equal(["Billing.City", "Billing.Zip"], Names(pathFirst));
        Assert.Equal(["Billing.City", "Billing.Zip"], Names(parentFirst));
    }

    [Fact]
    public void Selecting_IntoAChild_NarrowsTheChild() =>
        Assert.Equal(["Billing.City"], Names(IncludedRules().Validate(Blank, c => c.IncludeProperty(m => m.Billing!.City))));

    [Fact]
    public void Selecting_IntoAChild_KeepsRulesOnTheAncestor() =>
        Assert.Equal(["Billing"], Names(IncludedRules().Validate(new Model(), c => c.IncludeProperty(m => m.Billing!.City))));

    [Fact]
    public async Task Selection_NarrowsAnAsyncChild()
    {
        var location = new Validator<Location>(x =>
        {
            x.Property(m => m.City).MustAsync(_ => Task.FromResult(false));
            x.Property(m => m.Zip).MustAsync(_ => Task.FromResult(false));
        });

        var validator = new Validator<Model>(x => x.Property(m => m.Billing).Include(location));
        var model = new Model { Billing = new Location { City = "a", Zip = "b" } };
        var result = await validator.ValidateAsync(model, c => c.IncludeProperty(m => m.Billing!.City));
        Assert.Equal(["Billing.City"], Names(result));
    }

    [Fact]
    public void Selection_NarrowsThroughSeveralIncludeLevels()
    {
        var location = LocationRules();
        var model = new Validator<Model>(x => x.Property(m => m.Billing).Include(location));
        var outer = new Validator<Outer>(x => x.Property(m => m.Inner).Include(model));

        var result = outer.Validate(
            new Outer { Inner = new Model { Billing = new Location() } },
            c => c.IncludeProperty(m => m.Inner!.Billing!.Zip));

        Assert.Equal(["Inner.Billing.Zip"], Names(result));
    }

    [Fact]
    public void SharedChild_IsNotNarrowedForOtherChains() =>
        Assert.Equal(
            ["Billing.City", "Shipping.City", "Shipping.Zip"],
            Names(IncludedRules().Validate(Blank, c =>
            {
                c.IncludeProperty(m => m.Billing!.City);
                c.IncludeProperty(m => m.Shipping);
            })));

    [Fact]
    public void SelectingACollection_RunsItsElementPropertyChains()
    {
        var validator = new Validator<Model>(x =>
        {
            x.Elements(m => m.Items!).Property(i => i.Label).NotEmpty();
            x.Property(m => m.Name).NotEmpty();
        });

        var result = validator.Validate(new Model { Items = [new Item()] }, c => c.IncludeProperty(m => m.Items));
        Assert.Equal(["Items[0].Label"], Names(result));
    }

    [Fact]
    public void MemberNamePrefix_IsNotAPathPrefix()
    {
        var validator = new Validator<PrefixModel>(x =>
        {
            x.Property(m => m.Bill).NotEmpty();
            x.Property(m => m.Billing).NotEmpty();
        });

        var result = validator.Validate(new PrefixModel(), c => c.IncludeProperty(m => m.Bill));
        Assert.Equal(["Bill"], Names(result));
    }

    [Fact]
    public void ReusedContext_KeepsItsSelection()
    {
        var validator = NestedRules();
        var context = new ValidationContext<Model>(Blank);
        context.IncludeProperty(m => m.Billing!.City);
        validator.Validate(context);
        Assert.Equal(["Billing.City"], Names(validator.Validate(context)));
    }

    private sealed class Location
    {
        public string? City { get; init; }

        public string? Zip { get; init; }
    }

    private sealed class Item
    {
        public string? Label { get; init; }
    }

    private sealed class Model
    {
        public Location? Billing { get; init; }

        public Location? Shipping { get; init; }

        public List<Item>? Items { get; init; }

        public string? Name { get; init; }
    }

    private sealed class Outer
    {
        public Model? Inner { get; init; }
    }

    private sealed class PrefixModel
    {
        public string? Bill { get; init; }

        public string? Billing { get; init; }
    }
}