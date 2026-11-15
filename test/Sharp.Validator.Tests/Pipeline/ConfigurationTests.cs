namespace Sharp.Validator.Tests.Pipeline;

public sealed class ConfigurationTests
{
    [Fact]
    public void Configure_BeforeFreezing_CanMergeMoreThanOnce()
    {
        var validator = new Validator<Model>(b => b.Property(x => x.Text).NotEqual("m"));
        validator.Configure(o => o.PropertyNamePrefix = "prefix");
        validator.Configure(o => o.PropertyNameSuffix = "suffix");
        Assert.Equal("prefixTextsuffix", Assert.Single(validator.Validate(new Model()).Errors).PropertyName);
    }

    [Fact]
    public void Snapshot_NameOfMissingMember_IsNull() => Assert.Null(default(ValidatorBuilderOptionsSnapshot).NameOf(null));

    [Fact]
    public void DisplayName_WithoutAnyResolver_IsNullAndGlobalResolverIsOptional()
    {
        var messages = new ValidatorMessageOptions(null, null, new ValidatorBuilderOptions { DisplayNameResolver = _ => "global" });

        Assert.Null(new ValidatorMessageOptions(null, null, null).DisplayNameOf(typeof(Model).GetProperty(nameof(Model.Text))));
        Assert.Equal("global", messages.DisplayNameOf(typeof(Model).GetProperty(nameof(Model.Text))));
    }

    [Fact]
    public void DisplayNameResolvers_FollowLocalScanGlobalPrecedence()
    {
        ValidatorBuilderOptions?[] choices = [null, new(), new() { DisplayNameResolver = _ => "resolved" }];
        var member = typeof(Model).GetProperty(nameof(Model.Text));
        foreach (var local in choices)
        foreach (var scan in choices)
        foreach (var global in choices)
        {
            var messages = new ValidatorMessageOptions(local, scan, global);
            var hasResolver = local?.DisplayNameResolver is not null || scan?.DisplayNameResolver is not null ||
                global?.DisplayNameResolver is not null;

            Assert.Equal(hasResolver ? "resolved" : null, messages.DisplayNameOf(member));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NamedComparisonTargets_CanUseADisplayNameResolver(int rule)
    {
        var validator = new Validator<Model>(b =>
        {
            switch (rule)
            {
                case 0: b.Property(x => x.Text).GreaterThan(x => x.Target, o => o.ErrorMessage = "{PropertyName} > {TargetPropertyName}"); break;
                case 1: b.Property(x => x.Text).NotEqual(x => x.Target); break;
                case 2:
                    b.Property(x => x.Text).InclusiveBetween(x => x.Target, x => x.Target,
                        o => o.ErrorMessage = "{PropertyName}: {FromPropertyName}..{ToPropertyName}"); break;
            }
        });

        validator.Configure(o => o.DisplayNameResolver = member =>
            member.Name == nameof(Model.Target) ? "target-display" : "text-display");

        var error = Assert.Single(validator.Validate(new Model { Text = rule == 1 ? "z" : "m" }).Errors);

        Assert.Equal(rule switch
        {
            0 => "text-display > target-display",
            1 => "'text-display' must not be equal to 'target-display'.",
            _ => "text-display: target-display..target-display"
        }, error.Message);
    }

    [Fact]
    public void UnindexedCollection_SyncFailure_StopsAtTheFirstElement()
    {
        var sync = new Validator<Model>(b => b.Elements(x => x.Items, o => o.CascadeMode = ValidationCascadeMode.Stop).NotNull());
        Assert.Single(sync.Validate(new Model { Items = new HashSet<string?> { null, "valid" } }).Errors);
    }

    [Fact]
    public async Task UnindexedCollection_AsyncFailure_StopsAtTheFirstElement()
    {
        var async = new Validator<Model>(b =>
            b.Elements(x => x.Items, o => o.CascadeMode = ValidationCascadeMode.Stop).MustAsync(_ => Task.FromResult(false)));

        Assert.Single((await async.ValidateAsync(
            new Model { Items = new HashSet<string?> { "one", "two" } }, TestContext.Current.CancellationToken)).Errors);
    }

    [Fact]
    public void BareRule_AfterAnInclude_CanReportAFailureWithoutOptions()
    {
        var validator = new Validator<Model>(b => b.Elements(x => x.Items)
            .Include(new Validator<string?>(child => child.Property(x => x!.Length).GreaterThan(0)))
            .Add(new BareRule()));

        var error = Assert.Single(validator.Validate(new Model { Items = ["valid"] }).Errors);
        Assert.Equal("'Items[0]' is not valid.", error.Message);
    }

    [Fact]
    public void DerivedIncludeConfiguration_ControlsReportedNames()
    {
        var child = new Validator<Derived>(b => b.Property(x => x.Text).Equal("other"));
        var validator = new Validator<Container>(b => b.Property(x => x.Value).Include(child, o => o.IncludeParentPropertyName = false));
        Assert.False(validator.Validate(new Container()).IsValid);

        // Use the adapter directly because an identity selector is not a public property path.
        var adapter = new IncludeDerivedValidator<Model, Model, Derived>(child, new() { IncludeParentPropertyName = false });
        Assert.False(adapter.Validate(new Derived(), new ValidationContext<Model>(new())));
    }

    [Fact]
    public void ChildRejectedBySyncAdapterDuringFreeze_BecomesAsync()
    {
        PropertyValidatorBuilder<Model, string>? chain = null;

        var child = new Validator<Model>(b => chain = b.Property(x => x.Text).NotEmpty());
        child.Configure(o => o.DisplayNameResolver = _ =>
        {
            chain!.MustAsync(_ => Task.FromResult(true));
            return "display";
        });

        Assert.Throws<InvalidOperationException>(() => IncludeValidatorComposition.Prepare(new ValidatorBuilder<Model>(), child, false));
    }

    [Fact]
    public void ReflectionNameability_DescendsIntoElementTypes()
    {
        Assert.True(ValidatorServiceCollectionExtensions.IsNameable(typeof(string[])));
        Assert.False(ValidatorServiceCollectionExtensions.IsNameable(typeof(Model[])));
    }

    private class Model
    {
        public string Text { get; init; } = "m";

        public string Target { get; } = "z";

        public IEnumerable<string?>? Items { get; init; }
    }

    private sealed class Derived : Model;

    private sealed class Container
    {
        public Model Value { get; } = new Derived();
    }

    private sealed class BareRule : PropertyValidator<Model, string?>
    {
        public override bool Validate(string? value, ValidationContext<Model> context) => false;
    }
}