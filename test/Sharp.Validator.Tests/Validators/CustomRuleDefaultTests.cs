namespace Sharp.Validator.Tests.Validators;

// A rule written for an app that set no code or message reported a null code and an empty message, which a client can
// neither branch on nor show. It now reports its type's name, as written in C#, and a message that names the property,
// the way a built-in rule reports its own; a code or message from the rule, its chain, or ErrorMessages still wins.
public sealed class CustomRuleDefaultTests
{
    private static ValidationError SingleError(
        PropertyValidator<Order, string?> rule,
        Action<PropertyValidatorOptions>? configure = null
    ) => Assert.Single(new Validator<Order>(b => b.Property(x => x.Sku).Add(rule, configure)).Validate(new Order()).Errors);

    [Fact]
    public void Rule_SettingNothing_ReportsItsTypeNameAndADefaultMessage()
    {
        var error = SingleError(new BareRule());
        Assert.Equal(("BareRule", "'Sku' is not valid."), (error.Code, error.Message));
    }

    [Fact]
    public void GenericRule_SettingNothing_ReportsItsNameWithoutTheArity()
    {
        var error = SingleError(new GenericBareRule<Order>());
        Assert.Equal(("GenericBareRule", "'Sku' is not valid."), (error.Code, error.Message));
    }

    [Fact]
    // A file-local type's runtime name carries its file and a hash of the file's path, so a code taken from it differed
    // between machines that build from different folders. The code is the name the type is declared with.
    public void FileLocalRule_SettingNothing_ReportsItsDeclaredName()
    {
        var validator = new Validator<Shipment>(b => b.Property(x => x.Code).Add(new FileLocalRule()));
        var error = Assert.Single(validator.Validate(new Shipment()).Errors);
        Assert.Equal(("FileLocalRule", "'Code' is not valid."), (error.Code, error.Message));
    }

    [Fact]
    public void GenericFileLocalRule_SettingNothing_ReportsItsDeclaredName()
    {
        var validator = new Validator<Shipment>(b => b.Property(x => x.Code).Add(new GenericFileLocalRule<Shipment>()));
        var error = Assert.Single(validator.Validate(new Shipment()).Errors);
        Assert.Equal("GenericFileLocalRule", error.Code);
    }

    [Fact]
    public async Task AsyncRule_SettingNothing_ReportsItsTypeNameAndADefaultMessage()
    {
        var validator = new Validator<Order>(b => b.Property(x => x.Sku).Add(new AsyncBareRule()));
        var result = await validator.ValidateAsync(new Order(), TestContext.Current.CancellationToken);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("AsyncBareRule", "'Sku' is not valid."), (error.Code, error.Message));
    }

    [Fact]
    public void Rule_SettingOnlyACode_KeepsItWithTheDefaultMessage()
    {
        var error = SingleError(new CodeOnlyRule());
        Assert.Equal(("SkuTaken", "'Sku' is not valid."), (error.Code, error.Message));
    }

    [Fact]
    public void Rule_SettingOnlyAMessage_KeepsItWithItsTypeName()
    {
        var error = SingleError(new MessageOnlyRule());
        Assert.Equal(("MessageOnlyRule", "'Sku' is taken."), (error.Code, error.Message));
    }

    [Fact]
    public void Rule_ConfiguredWhereAdded_ReportsThatCodeAndMessage()
    {
        var error = SingleError(new BareRule(), options =>
        {
            options.ErrorCode = "SkuInvalid";
            options.ErrorMessage = "{PropertyName} is invalid.";
        });

        Assert.Equal(("SkuInvalid", "Sku is invalid."), (error.Code, error.Message));
    }

    [Fact]
    public void Rule_UnderAChainCode_ReportsTheChainCode()
    {
        var validator = new Validator<Order>(b => b.Property(x => x.Sku, options => options.ErrorCode = "SKU_INVALID")
            .Add(new BareRule()));

        var error = Assert.Single(validator.Validate(new Order()).Errors);
        Assert.Equal(("SKU_INVALID", "'Sku' is not valid."), (error.Code, error.Message));
    }

    [Fact]
    public void ErrorMessages_KeyedByTheTypeName_ReplaceTheDefaultMessage()
    {
        var validator = new Validator<Order>(b => b.Property(x => x.Sku).Add(new BareRule()));
        validator.Configure(options => options.ErrorMessages["BareRule"] = "'{PropertyName}' failed the bare rule.");
        var error = Assert.Single(validator.Validate(new Order()).Errors);
        Assert.Equal(("BareRule", "'Sku' failed the bare rule."), (error.Code, error.Message));
    }

    private sealed class Order
    {
        public string? Sku { get; init; }
    }

    private sealed class BareRule : PropertyValidator<Order, string?>
    {
        public override bool Validate(string? value, ValidationContext<Order> context) => false;
    }

    private sealed class GenericBareRule<TModel> : PropertyValidator<TModel, string?>
    {
        public override bool Validate(string? value, ValidationContext<TModel> context) => false;
    }

    private sealed class AsyncBareRule : AsyncPropertyValidator<Order, string?>
    {
        public override Task<bool> ValidateAsync(
            string? value,
            ValidationContext<Order> context,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);
    }

    private sealed class CodeOnlyRule : PropertyValidator<Order, string?>
    {
        public CodeOnlyRule() => Configure(options => options.ErrorCode = "SkuTaken");

        public override bool Validate(string? value, ValidationContext<Order> context) => false;
    }

    private sealed class MessageOnlyRule : PropertyValidator<Order, string?>
    {
        public MessageOnlyRule() => Configure(options => options.ErrorMessage = "'{PropertyName}' is taken.");

        public override bool Validate(string? value, ValidationContext<Order> context) => false;
    }
}

file sealed class Shipment
{
    public string? Code { get; init; }
}

file sealed class FileLocalRule : PropertyValidator<Shipment, string?>
{
    public override bool Validate(string? value, ValidationContext<Shipment> context) => false;
}

file sealed class GenericFileLocalRule<TModel> : PropertyValidator<TModel, string?>
{
    public override bool Validate(string? value, ValidationContext<TModel> context) => false;
}