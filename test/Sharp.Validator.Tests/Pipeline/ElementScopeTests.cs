namespace Sharp.Validator.Tests.Pipeline;

// A property rule added on an Elements chain stays scoped to each element: it inherits the chain's condition but not
// its rule options.
public sealed class ElementScopeTests
{
    [Fact]
    public void Property_OnAnElementsChainAfterWhere_KeepsElementScope()
    {
        var validator = new Validator<Model>(builder => builder.Elements(x => x.Items)
            .Where(_ => true)
            .Property(x => x.Label)
            .NotEmpty());

        var result = validator.Validate(new Model { Name = "", Items = [new Item { Label = "" }] });
        Assert.Equal("Items[0].Label", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void ElementsCondition_IsInheritedButRuleOptionsAreNot()
    {
        var gated = new Validator<Model>(builder => builder.Elements(x => x.Items)
            .Where(_ => false)
            .Property(x => x.Label)
            .NotEmpty());

        Assert.True(gated.Validate(new Model { Items = [new Item { Label = "" }] }).IsValid);

        var validator = new Validator<Model>(builder =>
        {
            var elements = builder.Elements(x => x.Items, options =>
            {
                options.ErrorSeverity = ValidationErrorSeverity.Warning;
                options.ErrorCode = "element";
            });

            elements.Must(_ => false);
            elements.Property(x => x.Label, options => options.ErrorCode = "child").NotEmpty();
        });

        var result = validator.Validate(new Model { Items = [new Item { Label = "" }] });
        Assert.Equal(ValidationErrorSeverity.Warning, result.Errors[0].Severity);
        Assert.Equal("element", result.Errors[0].Code);
        Assert.Equal(ValidationErrorSeverity.Error, result.Errors[1].Severity);
        Assert.Equal("child", result.Errors[1].Code);
    }

    private sealed class Model
    {
        public string? Name { get; init; }

        public List<Item> Items { get; init; } = [];
    }

    private sealed class Item
    {
        public string? Label { get; init; }
    }
}