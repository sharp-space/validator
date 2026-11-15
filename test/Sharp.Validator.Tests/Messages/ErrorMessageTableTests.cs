using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Tests.Registration;

namespace Sharp.Validator.Tests.Messages;

[Collection(GlobalOptions.Name)]
// A default message could only be replaced rule by rule, so an app that wanted its own wording repeated it at every
// call site. ErrorMessages maps an error code to the default message of every rule that reports it. It can be set on
// Global, an AddValidators scan, or one validator, and each validator reads it when it freezes.
public sealed class ErrorMessageTableTests
{
    private static T WithGlobal<T>(Action<ValidatorBuilderOptions> configure, Func<T> run)
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions();
            configure(ValidatorBuilderOptions.Global);

            return run();
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    private static string?[] Messages(Validator<Order> validator, Order model) =>
        validator.Validate(model).Errors.Select(error => error.Message).ToArray();

    [Fact]
    public void GlobalMessage_ReplacesTheDefaultOfEveryRuleWithItsCode()
    {
        var messages = WithGlobal(global => global.ErrorMessages["NotEmpty"] = "{PropertyName} is required.", () =>
            Messages(new Validator<Order>(x =>
            {
                x.Property(order => order.Name).NotEmpty();
                x.Property(order => order.Quantity).NotEmpty();
            }), new Order()));

        Assert.Equal(new[] { "Name is required.", "Quantity is required." }, messages);
    }

    [Fact]
    public void GlobalMessage_FillsTheRulePlaceholders()
    {
        var messages = WithGlobal(
            global => global.ErrorMessages["GreaterThan"] = "{PropertyName} needs more than {TargetValue}, not {PropertyValue}.",
            () => Messages(new Validator<Order>(x => x.Property(order => order.Quantity).GreaterThan(5)), new Order { Quantity = 2 }));

        Assert.Equal(new[] { "Quantity needs more than 5, not 2." }, messages);
    }

    [Fact]
    public void MessageSet_OnTheRuleOrItsChain_Wins()
    {
        var messages = WithGlobal(global => global.ErrorMessages["NotEmpty"] = "global", () =>
            Messages(new Validator<Order>(x =>
            {
                x.Property(order => order.Name).NotEmpty(options => options.ErrorMessage = "rule");
                x.Property(order => order.Quantity, options => options.ErrorMessage = "chain").NotEmpty();
            }), new Order()));

        Assert.Equal(new[] { "rule", "chain" }, messages);
    }

    [Fact]
    public void CodeSet_OnTheRulePicksTheMessageForThatCode()
    {
        var messages = WithGlobal(global => global.ErrorMessages["NameMissing"] = "Tell us your name.", () =>
            Messages(new Validator<Order>(x => x.Property(order => order.Name).NotEmpty(options => options.ErrorCode = "NameMissing")),
                new Order()));

        Assert.Equal(new[] { "Tell us your name." }, messages);
    }

    [Fact]
    // A message set on Global after a validator had read it was ignored by that validator and taken by the next one, so
    // what a validator printed depended on which froze first. Global is read-only once a validator reads it; new
    // options assigned to Global serve the validators that freeze afterward, and the earlier ones keep what they read.
    public void Global_IsReadOnlyOnceAValidatorFroze_AndNewOptionsServeLaterValidators()
    {
        var (early, late) = WithGlobal(global => global.ErrorMessages["NotEmpty"] = "first", () =>
        {
            var frozen = new Validator<Order>(x => x.Property(order => order.Name).NotEmpty());
            frozen.Validate(new Order());

            Assert.Throws<InvalidOperationException>(() => ValidatorBuilderOptions.Global.ErrorMessages["NotEmpty"] = "second");

            var next = new ValidatorBuilderOptions { ErrorMessages = { ["NotEmpty"] = "second" } };
            ValidatorBuilderOptions.Global = next;
            var fresh = new Validator<Order>(x => x.Property(order => order.Name).NotEmpty());
            return (Messages(frozen, new Order()), Messages(fresh, new Order()));
        });

        Assert.Equal(new[] { "first" }, early);
        Assert.Equal(new[] { "second" }, late);
    }

    [Fact]
    public void Validator_OverridesGlobalCodeByCode()
    {
        var messages = WithGlobal(global =>
        {
            global.ErrorMessages["NotEmpty"] = "global empty";
            global.ErrorMessages["GreaterThan"] = "global greater";
        }, () =>
        {
            var validator = new Validator<Order>(x =>
            {
                x.Property(order => order.Name).NotEmpty();
                x.Property(order => order.Quantity).GreaterThan(5);
            });

            validator.Configure(options => options.ErrorMessages["NotEmpty"] = "local empty");
            return Messages(validator, new Order());
        });

        Assert.Equal(new[] { "local empty", "global greater" }, messages);
    }

    [Fact]
    public void Scan_OverridesGlobalForTheValidatorsItRegisters()
    {
        var message = WithGlobal(global => global.ErrorMessages["NotEmpty"] = "global", () =>
        {
            var services = new ServiceCollection();
            services.AddValidators(Assembly.GetExecutingAssembly(), options => options.ErrorMessages["NotEmpty"] = "scan");
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<LineValidator>().Validate(new Line()).Errors[0].Message;
        });

        Assert.Equal("scan", message);
    }

    private sealed class Order
    {
        public string? Name { get; init; }

        public int Quantity { get; init; }
    }
}