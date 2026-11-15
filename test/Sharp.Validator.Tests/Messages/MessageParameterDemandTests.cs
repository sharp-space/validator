using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Messages;

// Failing rules now build message parameters only when something can read them. These are the readers that aren't the
// rule's own frozen template: a wrapper, a message set at runtime, and a template that names a parameter explicitly. A
// fixed comparison target is baked into the template, which must not let a brace in the target act as a placeholder.
public sealed class MessageParameterDemandTests
{
    [Fact]
    public void Wrapper_CanReadRuleParameters()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Length(2, 5));
        validator.Configure(options => options.ErrorMessagePrefix = "[{Length}] ");
        Assert.StartsWith("[1] ", validator.Validate(new Person { Name = "x" }).Errors[0].Message);
    }

    [Fact]
    public void MessageSet_AtRuntime_CanReadRuleParameters()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Add(new RuntimeMessage(3)));
        Assert.Equal("1 of 3", validator.Validate(new Person { Name = "x" }).Errors[0].Message);
    }

    [Fact]
    public void MessageSet_AfterTheRuleFailed_CanReadRuleParameters()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).Add(new MessageAfterFailure(5)));
        Assert.Equal("'Age' must exceed 5.", validator.Validate(new Person { Age = 1 }).Errors[0].Message);
    }

    [Fact]
    public void CustomTemplate_CanReadRuleParameters()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name).Length(2, 5, options => options.ErrorMessage = "{MinLength}-{MaxLength}: {Length}");
            x.Property(person => person.Email).Regex("^a$", RegexOptions.None, options => options.ErrorMessage = "{RegularExpression}");
            x.Property(person => person.Age).GreaterThan(5, options => options.ErrorMessage = "{TargetValue}");
            x.Property(person => person.Password).Equal("p", options => options.ErrorMessage = "{TargetValue}");
        });

        var errors = validator.Validate(new Person { Name = "x", Email = "b", Age = 1, Password = "q" }).Errors;
        Assert.Equal(["2-5: 1", "^a$", "5", "p"], errors.Select(error => error.Message));
    }

    [Fact]
    public void FixedTarget_WithABrace_StaysVerbatim()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).GreaterThan("{PropertyName}"));
        Assert.Equal(
            "'Name' must be greater than '{PropertyName}'.",
            validator.Validate(new Person { Name = "a" }).Errors[0].Message);
    }

    [Fact]
    public void FixedTarget_RendersLikeARuntimeOne()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Age).GreaterThan(5));
        Assert.Equal("'Age' must be greater than '5'.", validator.Validate(new Person { Age = 1 }).Errors[0].Message);
    }

    // Built-in rules are sealed, so a custom rule reuses one by calling it.
    private sealed class RuntimeMessage(int minimum) : PropertyValidator<Person, string?>
    {
        private readonly MinimumLengthValidator<Person> _rule = new(minimum);

        public override bool Validate(string? value, ValidationContext<Person> context)
        {
            context.ErrorMessage = "{Length} of {MinLength}";
            return _rule.Validate(value, context);
        }
    }

    // Sets the message only once the built-in rule has failed, so the rule can't see it when it decides whether to
    // build its parameters.
    private sealed class MessageAfterFailure(int target) : PropertyValidator<Person, int>
    {
        private readonly GreaterThanValidator<Person, int> _rule = new(target);

        public override bool Validate(int value, ValidationContext<Person> context)
        {
            if (_rule.Validate(value, context)) return true;

            context.ErrorMessage = "'{PropertyName}' must exceed {TargetValue}.";

            return false;
        }
    }
}