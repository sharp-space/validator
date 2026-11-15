namespace Sharp.Validator.Tests.Messages;

// ErrorMessage and MessageParameters belong to the rule being evaluated and are cleared when it finishes, so a value
// set by a passing rule can't leak into the next failure. Their XML docs used to promise "the next failing validator".
public sealed class RuleMessageScopeTests
{
    [Fact]
    public void MessageSet_ByAPassingRule_DoesNotReachALaterFailure()
    {
        var validator = new Validator<Person>(x => x
            .Property(m => m.Name)
            .Must((_, context) =>
            {
                context.ErrorMessage = "from the passing rule";
                return true;
            })
            .MinimumLength(5));

        var message = Assert.Single(validator.Validate(new Person { Name = "a" }).Errors).Message;
        Assert.Equal("'Name' must be at least 5 characters.", message);
    }

    [Fact]
    public void ParameterSet_ByAPassingRule_DoesNotReachALaterFailure()
    {
        var validator = new Validator<Person>(x => x
            .Property(m => m.Name)
            .Must((_, context) =>
            {
                context.MessageParameters.Add("Tenant", "acme");
                return true;
            })
            .MinimumLength(5, o => o.ErrorMessage = "{Tenant}"));

        Assert.Equal("{Tenant}", Assert.Single(validator.Validate(new Person { Name = "a" }).Errors).Message);
    }

    [Fact]
    public void MessageSeeded_BeforeValidation_AppliesToTheFirstRuleOnly()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(m => m.Email).NotEmpty();
            x.Property(m => m.Name).NotEmpty();
        });

        var result = validator.Validate(new Person { Email = "a" }, c => c.ErrorMessage = "seeded");
        Assert.Equal("'Name' must not be empty.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void Parameters_CarryPerCallDataAcrossRules()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(m => m.Email).NotEmpty();
            x.Property(m => m.Name).Must((_, context) =>
            {
                context.ErrorMessage = $"tenant {context.Parameters["tenant"]}";
                return false;
            });
        });

        var result = validator.Validate(new Person { Email = "a", Name = "b" }, c => c.Parameters["tenant"] = "acme");
        Assert.Equal("tenant acme", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void RuleMessageState_AfterSuccessAndFailure_IsCleared()
    {
        var success = new Validator<StateModel>(builder =>
        {
            builder.Property(m => m.First).Add(new MessageStateValidator(true));
            builder.Property(m => m.Second).NotEmpty();
        });

        Assert.DoesNotContain("LEAK", Assert.Single(success.Validate(new StateModel()).Errors).Message);

        var failure = new Validator<StateModel>(builder => builder.Property(m => m.First)
            .Add(new MessageStateValidator(false))
            .NotEmpty());

        var errors = failure.Validate(new StateModel()).Errors;
        Assert.Equal("LEAK leaked", errors[0].Message);
        Assert.DoesNotContain("LEAK", errors[1].Message);
    }

    [Fact]
    public void RuleMessageStateAndElementCache_AfterException_AreCleared()
    {
        var model = new StateModel { Throw = true };
        var context = new ValidationContext<StateModel>(model);
        var validator = new Validator<StateModel>(builder =>
        {
            builder.Property(m => m.First).Add(new ThrowingMessageStateValidator());
            builder.Property(m => m.Second).NotEmpty();
        });

        Assert.Throws<InvalidOperationException>(() => validator.Validate(context));

        model.Throw = false;

        var error = Assert.Single(validator.Validate(context).Errors);
        Assert.DoesNotContain("LEAK", error.Message);
    }

    [Fact]
    public void PropertyRuleOptions_MergeFieldByField()
    {
        var validator = new Validator<Person>(x => x.Property(m => m.Name, options =>
            {
                options.ErrorSeverity = ValidationErrorSeverity.Warning;
                options.ErrorCode = "property-code";
            })
            .NotEmpty(options => options.ErrorMessage = "custom"));

        var error = Assert.Single(validator.Validate(new Person()).Errors);
        Assert.Equal("custom", error.Message);
        Assert.Equal("property-code", error.Code);
        Assert.Equal(ValidationErrorSeverity.Warning, error.Severity);
    }

    private sealed class StateModel
    {
        public string? First { get; init; }

        public string? Second { get; init; }

        public bool Throw { get; set; }
    }

    private sealed class MessageStateValidator(bool pass) : PropertyValidator<StateModel, string?>
    {
        public override bool Validate(string? value, ValidationContext<StateModel> context)
        {
            context.ErrorMessage = "LEAK {token}";
            context.MessageParameters.Add("token", "leaked");

            return pass;
        }
    }

    private sealed class ThrowingMessageStateValidator : PropertyValidator<StateModel, string?>
    {
        public override bool Validate(string? value, ValidationContext<StateModel> context)
        {
            context.ErrorMessage = "LEAK";
            context.MessageParameters.Add("token", "leaked");
            return context.Model.Throw ? throw new InvalidOperationException("boom") : true;
        }
    }
}