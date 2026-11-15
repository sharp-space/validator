using System.Reflection;

namespace Sharp.Validator.Tests.Pipeline;

public sealed class ElementPropertyTests
{
    [Fact]
    public void ElementsProperty_ValidatesChildPropertyWithIndexedPath()
    {
        var validator = new Validator<FieldDefinition>(builder => builder
            .Elements(x => x.SelectValues!)
            .Property(x => x!.Label)
            .NotEmpty());

        var result = validator.Validate(new FieldDefinition
        {
            SelectValues =
            [
                new SelectValue { Label = "ok" },
                new SelectValue { Label = "" }
            ]
        });

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("SelectValues[1].Label", result.Errors[0].PropertyName);
    }

    [Fact]
    public void ElementsProperty_NullOrEmptyCollection_Passes()
    {
        var validator = new Validator<FieldDefinition>(builder => builder
            .Elements(x => x.SelectValues!)
            .Property(x => x!.Label)
            .NotEmpty());

        Assert.True(validator.Validate(new FieldDefinition { SelectValues = null }).IsValid);
        Assert.True(validator.Validate(new FieldDefinition { SelectValues = [] }).IsValid);
    }

    [Fact]
    public void ElementsProperty_NullElementOrNullIntermediate_IsSkipped()
    {
        var validator = new Validator<FieldDefinition>(builder => builder
            .Elements(x => x.SelectValues!)
            .Property(x => x!.Detail!.Label)
            .NotEmpty());

        var result = validator.Validate(new FieldDefinition
        {
            SelectValues =
            [
                null,
                new SelectValue { Detail = null },
                new SelectValue { Detail = new SelectValueDetail { Label = "" } }
            ]
        });

        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("SelectValues[2].Detail.Label", item.PropertyName);
    }

    [Fact]
    public void ElementsPropertyContinueMode_ReportsErrorsElementOuter()
    {
        var validator = new Validator<FieldDefinition>(builder => builder
            .Elements(x => x.SelectValues!)
            .Property(x => x!.Label)
            .NotEmpty()
            .MinimumLength(3));

        var result = validator.Validate(new FieldDefinition
        {
            SelectValues =
            [
                new SelectValue { Label = "xx" },
                new SelectValue { Label = "" }
            ]
        });

        Assert.Collection(result.Errors,
            error => Assert.Equal("SelectValues[0].Label", error.PropertyName),
            error => Assert.Equal("SelectValues[1].Label", error.PropertyName),
            error => Assert.Equal("SelectValues[1].Label", error.PropertyName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // A walk over a list reads a nested selector in a loop of its own, which must stop on a blocking failure as the
    // loop for any other selector does.
    public void ElementsPropertyCascadeStop_ReturnsFirstElementsFirstFailingRule(bool nested)
    {
        var validator = new Validator<FieldDefinition>(builder =>
        {
            var elements = builder.Elements(x => x.SelectValues!);
            var chain = nested
                ? elements.Property(x => x!.Detail!.Label, x => x.CascadeMode = ValidationCascadeMode.Stop)
                : elements.Property(x => x!.Label, x => x.CascadeMode = ValidationCascadeMode.Stop);

            chain.NotEmpty().MinimumLength(3);
        });

        var result = validator.Validate(new FieldDefinition { SelectValues = [Labeled("xx"), Labeled("")] });
        var item = Assert.Single(result.Errors);
        Assert.Equal(nested ? "SelectValues[0].Detail.Label" : "SelectValues[0].Label", item.PropertyName);
    }

    private static SelectValue Labeled(string label) =>
        new() { Label = label, Detail = new SelectValueDetail { Label = label } };

    [Fact]
    public async Task ElementsPropertyAsyncContinueMode_ReportsErrorsElementOuter()
    {
        var validator = new Validator<FieldDefinition>(builder => builder
            .Elements(x => x.SelectValues!)
            .Property(x => x!.Label)
            .MustAsync(x => Task.FromResult(!string.IsNullOrEmpty(x)))
            .MustAsync(x => Task.FromResult((x?.Length ?? 0) >= 3)));

        var result = await validator.ValidateAsync(new FieldDefinition
        {
            SelectValues =
            [
                new SelectValue { Label = "xx" },
                new SelectValue { Label = "" }
            ]
        });

        Assert.Collection(result.Errors,
            error => Assert.Equal("SelectValues[0].Label", error.PropertyName),
            error => Assert.Equal("SelectValues[1].Label", error.PropertyName),
            error => Assert.Equal("SelectValues[1].Label", error.PropertyName));
    }

    [Fact]
    public void ElementsProperty_PolicyPrefixAndSuffix_AreAppliedOnce()
    {
        var validator = new Validator<FieldDefinition>(builder => builder
            .Elements(x => x.SelectValues!)
            .Property(x => x!.Label)
            .NotEmpty());

        validator.Configure(options =>
        {
            options.PropertyNamePolicy = PropertyNamePolicy.CamelCase;
            options.PropertyNamePrefix = "root.";
            options.PropertyNameSuffix = ".value";
        });

        var result = validator.Validate(new FieldDefinition
        {
            SelectValues = [new SelectValue { Label = "" }]
        });

        var item = Assert.Single(result.Errors);
        Assert.Equal("root.selectValues[0].label.value", item.PropertyName);
    }

    [Fact]
    public void ElementsProperty_RespectsParentIncludePropertyFilter()
    {
        var validator = new Validator<FieldDefinition>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Elements(x => x.SelectValues!)
                .Property(x => x!.Label)
                .NotEmpty();
        });

        var result = validator.Validate(
            new FieldDefinition
            {
                Name = "",
                SelectValues = [new SelectValue { Label = "" }]
            },
            context => context.IncludeProperty(x => x.SelectValues));

        var item = Assert.Single(result.Errors);
        Assert.Equal("SelectValues[0].Label", item.PropertyName);
    }

    [Fact]
    // Every rule chain used to inherit Property and Elements for chaining to another model property, so
    // builder.Elements(x => x.Items).Property(x => x.Name) checked the order's Name, not each item's. Property on an
    // Elements chain now always selects from the element, and a rule chain no longer starts another model chain.
    public void Property_OnAnElementsChainWhenTheModelHasTheSameMember_SelectsFromTheElement()
    {
        var validator = new Validator<Order>(builder => builder.Elements(x => x.Items).Property(x => x.Name).NotEmpty());
        var result = validator.Validate(new Order { Name = "", Items = [new OrderItem { Name = "" }] });
        Assert.Equal("Items[0].Name", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(typeof(PropertyValidatorBuilder<Order, string?>), "Property")]
    [InlineData(typeof(PropertyValidatorBuilder<Order, string?>), "Elements")]
    [InlineData(typeof(ElementValidatorBuilder<Order, OrderItem>), "Elements")]
    public void RuleChain_DoesNotStartAChainForAnotherModelProperty(Type chain, string method) =>
        Assert.DoesNotContain(chain.GetMethods(BindingFlags.Public | BindingFlags.Instance),
            candidate => candidate.Name == method);

    private sealed class FieldDefinition
    {
        public string? Name { get; init; }

        public List<SelectValue?>? SelectValues { get; init; }
    }

    private sealed class SelectValue
    {
        public string? Label { get; init; }

        public SelectValueDetail? Detail { get; init; }
    }

    private sealed class SelectValueDetail
    {
        public string? Label { get; init; }
    }

    private sealed class Order
    {
        public string? Name { get; init; }

        public List<OrderItem> Items { get; init; } = [];
    }

    private sealed class OrderItem
    {
        public string? Name { get; init; }
    }
}