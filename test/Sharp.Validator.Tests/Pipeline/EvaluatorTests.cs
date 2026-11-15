using System.Collections.ObjectModel;

namespace Sharp.Validator.Tests.Pipeline;

public sealed class EvaluatorTests
{
    [Fact]
    public void Rule_WithoutOptions_ReportsTheDefaultMessage()
    {
        var validator = new Validator<Item>(b => b.Property(x => x.Name).Add(new BareRule()));
        var error = Assert.Single(validator.Validate(new Item()).Errors);
        Assert.Equal("'Name' is not valid.", error.Message);
    }

    [Fact]
    // An element chain builds a failure's error without the per-chain cache, so it has its own check for a template
    // that was cleared, which a rule of the app's no longer reaches now that it gets a default message.
    public void ElementRule_WithAClearedTemplate_ReportsAnEmptyMessage()
    {
        var rule = new NotNullValidator<Model, Item>();
        rule.Options!.ErrorMessage = null;

        var validator = new Validator<Model>(b => b.Elements(x => x.Items).Add(rule));
        var error = Assert.Single(validator.Validate(new Model { Items = [null!] }).Errors);
        Assert.Equal((ErrorCodes.NotNull, string.Empty), (error.Code, error.Message));
    }

    [Fact]
    public void Rule_WithoutOptions_ReportsTheDefaultSeverity()
    {
        var validator = new Validator<Item>(b => b.Property(x => x.Name).Add(new BareRule()));
        var error = Assert.Single(validator.Validate(new Item()).Errors);
        Assert.Equal(ValidationErrorSeverity.Error, error.Severity);
    }

    [Fact]
    public void OrdinaryRulesOnAnIncludeElementChain_ReportTheirOwnFailure()
    {
        var validator = new Validator<Model>(b => b.Elements(x => x.Items)
            .Include(new Validator<Item>(child => child.Property(x => x.Name).NotEmpty()))
            .Must(_ => false));

        Assert.Single(validator.Validate(new Model { Items = [new() { Name = "valid" }] }).Errors);
        Assert.True(validator.Validate(new Model { Items = [] }).IsValid);
    }

    [Fact]
    // An include element chain walks its ordinary rules in the include-aware step as well; a passing one adds no error
    // and the walk goes on to the Include.
    public void OrdinaryRuleOnAnIncludeElementChain_ThatPasses_LetsTheIncludeRun()
    {
        var validator = new Validator<Model>(b => b.Elements(x => x.Items)
            .Must(item => item.Name != null)
            .Include(new Validator<Item>(child => child.Property(x => x.Name).NotEmpty())));

        var result = validator.Validate(new Model { Items = [new() { Name = "valid" }, new() { Name = "" }] });
        Assert.Equal("Items[1].Name", Assert.Single(result.Errors).PropertyName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncInclude_FailedElementChain_StopsBeforeAsyncRules(bool elementProperty)
    {
        foreach (var indexed in new[] { false, true })
        {
            var calls = 0;
            var child = new Validator<Item>(b => b.Property(x => x.Name).NotEmpty());

            var validator = new Validator<Model>(b =>
            {
                if (elementProperty)
                {
                    b.Elements(x => x.Items).Property(x => x.Name!, o => o.CascadeMode = ValidationCascadeMode.Stop)
                        .Include(new Validator<string>(rules => rules.Property(x => x.Length).GreaterThan(0)))
                        .MustAsync(_ =>
                        {
                            calls++;
                            return Task.FromResult(true);
                        });
                }
                else
                {
                    b.Elements(x => x.Items, o => o.CascadeMode = ValidationCascadeMode.Stop)
                        .Include(child).MustAsync(_ =>
                        {
                            calls++;
                            return Task.FromResult(true);
                        });
                }
            });

            IEnumerable<Item> items = indexed
                ? new Collection<Item> { new() { Name = "" }, new() { Name = "" } }
                : new HashSet<Item> { new() { Name = "" }, new() { Name = "" } };

            var result = await validator.ValidateAsync(new Model { Items = items }, TestContext.Current.CancellationToken);
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Equal(0, calls);
        }
    }

    [Fact]
    public async Task ValidateAndThrowAsync_ReturnsNormallyForAValidModel()
    {
        var validator = new Validator<Item>(b => b.Property(x => x.Name).MustAsync(_ => Task.FromResult(true)));
        await validator.ValidateAndThrowAsync(new Item(), TestContext.Current.CancellationToken);
    }

    private sealed class Item
    {
        public string? Name { get; init; }
    }

    private sealed class Model
    {
        public IEnumerable<Item>? Items { get; init; }
    }

    private sealed class BareRule : PropertyValidator<Item, string?>
    {
        public override bool Validate(string? value, ValidationContext<Item> context) => false;
    }
}