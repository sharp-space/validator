using System.Reflection;

namespace Sharp.Validator.Tests.Validators;

// Rules took optional arguments before their configure, such as a comparer, a clock, or regex options, so changing a
// rule's message needed a named argument, and Password's first lambda could have been either callback. Every rule now
// takes configure last, as its only optional argument, and each optional setting is an overload of its own.
public sealed class RuleOverloadShapeTests
{
    [Fact]
    public void RuleMethods_TakeConfigureAsTheirOnlyOptionalArgument()
    {
        var rules = typeof(ValidationExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType.Name.StartsWith("PropertyValidatorBuilder", StringComparison.Ordinal))
            .ToList();

        Assert.True(rules.Count > 50, $"found only {rules.Count} rule methods");

        Assert.All(rules, method =>
        {
            var parameters = method.GetParameters();
            var optional = parameters.Where(parameter => parameter.IsOptional).ToArray();
            Assert.True(optional.Length == 0 || (optional is [{ Name: "configure" } only] && only.Position == parameters.Length - 1),
                method.ToString());
        });
    }

    [Fact]
    public void ComparerOverloads_UseTheDefaultComparerForNull()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Name).Equal("a", comparer: null);
            x.Property(person => person.Age).GreaterThan(1, null, options => options.ErrorCode = "Age");
            x.Property(person => person.Age).LessThan(1, comparer: null);
            x.Property(person => person.Age).LessThanOrEqual(1, comparer: null);
            x.Property(person => person.Age).ExclusiveBetween(1, 3, comparer: null);
            x.Property(person => person.Age).InclusiveBetween(person => person.Age, person => person.Age, comparer: null);
            x.Property(person => person.NullableAmount).PrecisionScale(2, 1, true);
        });

        var errors = validator.Validate(new Person { Name = "b", Age = 1, NullableAmount = 1.50m }).Errors;
        Assert.Equal(["Equal", "Age", "LessThan", "ExclusiveBetween"], errors.Select(error => error.Code));
    }

    [Fact]
    public void Overloads_TakeTheirSettingAndConfigure()
    {
        var validator = new Validator<Person>(x =>
        {
            x.Property(person => person.Url).Uri(options => options.ErrorCode = "A");
            x.Property(person => person.Url).Uri(UriKind.Absolute, options => options.ErrorCode = "B");
            x.Property(person => person.Name).InEnum(typeof(DayOfWeek), options => options.ErrorCode = "C");
            x.Property(person => person.Name).InEnum(typeof(DayOfWeek), StringComparison.OrdinalIgnoreCase, options => options.ErrorCode = "D");
            x.Property(person => person.Amount).PrecisionScale(2, 1, options => options.ErrorCode = "E");
            x.Property(person => person.Amount).PrecisionScale(2, 1, true, options => options.ErrorCode = "F");
            x.Property(person => person.Name).Regex(_ => "^b$", options => options.ErrorCode = "G");
            x.Property(person => person.Password).Password(requirements => requirements.RequiredDigits = 0, options => options.ErrorCode = "H");
        });

        var codes = validator.Validate(new Person { Url = "relative", Name = "monday", Amount = 12.34m, Password = "abc" }).Errors
            .Select(error => error.Code);

        Assert.Equal(["A", "B", "C", "E", "F", "G", "H"], codes);
    }

    [Fact]
    public void Password_WithoutRequirements_UsesTheDefaults()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Password).Password());
        Assert.False(validator.Validate(new Person { Password = "short" }).IsValid);
    }
}