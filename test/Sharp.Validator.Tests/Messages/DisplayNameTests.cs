using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Sharp.Validator.Tests.Messages;

[Collection(GlobalOptions.Name)]
// A message named a property only by its reported name, which is also the key a client maps the error to, so a
// friendlier name in the message meant changing the key too. A display name, from a chain's DisplayName or from a
// resolver over the selected member, now names the property in messages, and the error's PropertyName keeps the path.
public sealed class DisplayNameTests
{
    private static readonly Func<MemberInfo, string?> FromDisplayAttribute =
        member => member.GetCustomAttribute<DisplayAttribute>()?.Name;

    private static Validator<Account> Displayed(Action<ValidatorBuilder<Account>> rules)
    {
        var validator = new Validator<Account>(rules);
        validator.Configure(options => options.DisplayNameResolver = FromDisplayAttribute);
        return validator;
    }

    [Fact]
    public void DisplayNameResolver_Message_UsesDisplayNameWhileKeyKeepsPath()
    {
        var error = Assert.Single(Displayed(x => x.Property(account => account.Email).NotEmpty()).Validate(new Account()).Errors);
        Assert.Equal("'Email address' must not be empty.", error.Message);
        Assert.Equal("Email", error.PropertyName);
    }

    [Fact]
    public void ChainDisplayName_WinsOverTheResolver()
    {
        var validator = Displayed(x => x.Property(account => account.Email, options => options.DisplayName = "E-mail").NotEmpty());
        Assert.Equal("'E-mail' must not be empty.", Assert.Single(validator.Validate(new Account()).Errors).Message);
    }

    [Fact]
    public void MemberWithoutResolverName_KeepsItsReportedName()
    {
        var validator = Displayed(x => x.Property(account => account.Nickname).NotEmpty());
        Assert.Equal("'Nickname' must not be empty.", Assert.Single(validator.Validate(new Account()).Errors).Message);
    }

    [Fact]
    public void DisplayName_IsUsedAsWritten()
    {
        var validator = new Validator<Account>(x => x.Property(account => account.Email).NotEmpty());
        validator.Configure(options =>
        {
            options.DisplayNameResolver = FromDisplayAttribute;
            options.PropertyNamePolicy = PropertyNamePolicy.SnakeCase;
            options.PropertyNamePrefix = "p_";
        });

        var error = Assert.Single(validator.Validate(new Account()).Errors);
        Assert.Equal("'Email address' must not be empty.", error.Message);
        Assert.Equal("p_email", error.PropertyName);
    }

    [Fact]
    public void CrossPropertyTarget_IsDisplayedToo()
    {
        var validator = Displayed(x => x.Property(account => account.Confirm).Equal(account => account.Password));
        var error = Assert.Single(validator.Validate(new Account { Password = "a", Confirm = "b" }).Errors);
        Assert.Equal("'Password again' must be equal to 'Your password'.", error.Message);
    }

    [Fact]
    public void Range_BoundsAreDisplayedToo()
    {
        var validator = Displayed(x => x.Property(account => account.Quantity)
            .InclusiveBetween(account => account.Minimum, account => account.Maximum,
                options => options.ErrorMessage = "{PropertyName}: {FromPropertyName} to {ToPropertyName}"));

        var error = Assert.Single(validator.Validate(new Account { Quantity = 9, Minimum = 1, Maximum = 5 }).Errors);
        Assert.Equal("Quantity: Minimum to Upper limit", error.Message);
    }

    [Fact]
    public void ElementMessage_DisplayName_KeepsIndexInKey()
    {
        var validator = Displayed(x => x.Elements(account => account.Tags!).NotEmpty());
        var error = Assert.Single(validator.Validate(new Account { Tags = ["a", ""] }).Errors);
        Assert.Equal("'Tag' must not be empty.", error.Message);
        Assert.Equal("Tags[1]", error.PropertyName);
    }

    [Fact]
    public void ElementPropertyMessage_TakesItsMemberDisplayName()
    {
        var validator = Displayed(x => x.Elements(account => account.Lines!).Property(line => line.Sku).NotEmpty());
        var error = Assert.Single(validator.Validate(new Account { Lines = [new Line { Sku = "a" }, new Line()] }).Errors);
        Assert.Equal("'SKU' must not be empty.", error.Message);
        Assert.Equal("Lines[1].Sku", error.PropertyName);
    }

    [Fact]
    public async Task AsyncRuleMessage_TakesTheDisplayName()
    {
        var validator = Displayed(x => x.Property(account => account.Email).MustAsync(_ => Task.FromResult(false)));
        var error = Assert.Single((await validator.ValidateAsync(new Account { Email = "a" })).Errors);
        Assert.Equal("'Email address' does not satisfy the required condition.", error.Message);
    }

    [Fact]
    public void MessageSetAtRuntimeByARule_TakesTheDisplayName()
    {
        var validator = Displayed(x => x.Property(account => account.Email).Must((_, context) =>
        {
            context.ErrorMessage = "{PropertyName} was rejected.";
            return false;
        }));

        Assert.Equal("Email address was rejected.", Assert.Single(validator.Validate(new Account { Email = "a" }).Errors).Message);
    }

    [Fact]
    public void GlobalResolver_ReachesEveryValidator()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions { DisplayNameResolver = FromDisplayAttribute };
            var validator = new Validator<Account>(x => x.Property(account => account.Email).NotEmpty());
            Assert.Equal("'Email address' must not be empty.", Assert.Single(validator.Validate(new Account()).Errors).Message);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    private sealed class Account
    {
        [Display(Name = "Email address")]
        public string? Email { get; init; }

        [Display(Name = "Your password")]
        public string? Password { get; init; }

        [Display(Name = "Password again")]
        public string? Confirm { get; init; }

        public int Minimum { get; init; }

        [Display(Name = "Upper limit")]
        public int Maximum { get; init; }

        public int Quantity { get; init; }

        [Display(Name = "Tag")]
        public List<string?>? Tags { get; init; }

        public string? Nickname { get; init; }

        public List<Line>? Lines { get; init; }
    }

    private sealed class Line
    {
        [Display(Name = "SKU")]
        public string? Sku { get; init; }
    }
}