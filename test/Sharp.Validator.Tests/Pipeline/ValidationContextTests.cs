using System.Collections;

namespace Sharp.Validator.Tests.Pipeline;

public sealed class ValidationContextTests
{
    [Fact]
    public void MessageParameters_IndexerOrEnumerator_NormalizesKeys()
    {
        var parameters = new MessageParameters { ["Name"] = "one", ["{Other}"] = "two", ["{x"] = "three" };
        Assert.Equal("one", parameters["{Name}"]);
        Assert.Equal("two", parameters["Other"]);
        Assert.Equal("three", parameters["{x"]);
        Assert.Equal(3, parameters.ToArray().Length);

        // ReSharper disable once GenericEnumeratorNotDisposed — the non-generic path is the case under test.
        var enumerator = ((IEnumerable)parameters).GetEnumerator();
        var count = 0;
        while (enumerator.MoveNext()) count++;

        Assert.Equal(3, count);
    }

    [Fact]
    public void NullSharedElementSource_IsEvaluatedOncePerRun()
    {
        var context = new ValidationContext<object>(new());
        var calls = 0;

        Assert.Null(context.GetElements(0, 1, Read));
        Assert.Null(context.GetElements(0, 1, Read));
        Assert.Equal(1, calls);

        context.AbortValidation();
        Assert.Null(context.GetElements(0, 1, Read));
        Assert.Equal(2, calls);

        return;

        IEnumerable<int>? Read(object _)
        {
            calls++;
            return null;
        }
    }

    [Fact]
    public void ChildCreated_OutsideASelectedChain_DoesNotNarrowItsRules()
    {
        var parent = new ValidationContext<Person>(new());
        parent.IncludeProperty(x => x.Name);
        var child = parent.CreateChild(new Person());
        var validator = new Validator<Person>(b => b.Property(x => x.Email).NotEmpty());
        Assert.False(validator.Validate(child).IsValid);
    }

    [Fact]
    public void UnfrozenBuilder_HasNoConditionsOrName()
    {
        PropertyValidatorBuilder<Person, string?>? chain = null;
        _ = new Validator<Person>(b => chain = b.Property(x => x.Name).NotEmpty());

        Assert.NotNull(chain);
        Assert.Empty(chain.Conditions);
        Assert.Equal(string.Empty, chain.PropertyName);
        Assert.False(chain.PrimaryBuilder.IsFrozen);
    }

    [Fact]
    public void UnfrozenBuilder_FreezesOnTheFirstValidation()
    {
        PropertyValidatorBuilder<Person, string?>? chain = null;
        var validator = new Validator<Person>(b => chain = b.Property(x => x.Name).NotEmpty());
        Assert.False(validator.Validate(new Person()).IsValid);
        Assert.True(chain!.PrimaryBuilder.IsFrozen);
        Assert.Equal("Name", chain.PropertyName);
    }

    [Fact]
    // A rule may not keep a context the library hands it, so these read the parameters through a context they created.
    public void ValidationContext_ParametersAreLazilyInitialised()
    {
        var context = new ValidationContext<Person>(new Person { Name = "x" });
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name).Must((_, ruleContext) =>
        {
            ruleContext.Parameters["seen"] = true;
            return true;
        }));

        Assert.True(validator.Validate(context).IsValid);
        Assert.True((bool)context.Parameters["seen"]!);
    }

    [Fact]
    // Parameters returned the Dictionary that held them, which tied every caller to that class, and its type
    // didn't admit the null values the dictionary stores. It is an IDictionary<string, object?>, so how it is
    // held can change later.
    public void Parameters_AreADictionaryInterfaceThatHoldsNull()
    {
        var context = new ValidationContext<Person>(new Person { Name = "x" });
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name).Must((_, ruleContext) =>
        {
            ruleContext.Parameters["absent"] = null;
            return true;
        }));

        validator.Validate(context);

        Assert.True(context.Parameters.TryGetValue("absent", out var value));
        Assert.Null(value);

        Assert.Equal(typeof(IDictionary<string, object>),
            typeof(ValidationContext<Person>).GetProperty(nameof(ValidationContext<>.Parameters))!.PropertyType);
    }
}