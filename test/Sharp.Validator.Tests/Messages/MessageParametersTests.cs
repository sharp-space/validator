namespace Sharp.Validator.Tests.Messages;

public sealed class MessageParametersTests
{
    [Fact]
    public void MessageParameters_AddOverwritesDuplicateKey()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, x => x.ErrorMessage = "{X}")
            .Must((_, context) =>
            {
                context.MessageParameters.Add("X", "first");
                context.MessageParameters.Add("X", "second");

                return false;
            }));

        var result = validator.Validate(new Person { Name = "anything" });
        Assert.False(result.IsValid);
        Assert.Equal("second", result.Errors[0].Message);
    }

    [Fact]
    public void MessageParameters_AddReturnsTheParametersSoCallsChain()
    {
        MessageParameters? returned = null;
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, x => x.ErrorMessage = "{A}{B}")
            .Must((_, context) =>
            {
                returned = context.MessageParameters.Add("A", "1").Add("B", "2");
                Assert.Same(context.MessageParameters, returned);

                return false;
            }));

        Assert.Equal("12", validator.Validate(new Person { Name = "anything" }).Errors[0].Message);
        Assert.NotNull(returned);
    }

    [Fact]
    public void MessageParameters_IndexerSetsAValueWhileTheRuleRuns()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name, x => x.ErrorMessage = "{X}")
            .Must((_, context) =>
            {
                context.MessageParameters["X"] = "first";
                context.MessageParameters["{X}"] = "second";

                return false;
            }));

        Assert.Equal("second", validator.Validate(new Person { Name = "anything" }).Errors[0].Message);
    }
}