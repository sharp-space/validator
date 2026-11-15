using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Tests.Pipeline;

// A null selector, predicate, condition, child validator, or rule was accepted where it was declared and failed later:
// a null predicate threw NullReferenceException on the first validation, which the pipeline could even wrap in guidance
// about nullable members. Every public member that takes one now throws ArgumentNullException where it is called,
// naming the parameter the caller passed.
// The table picks each overload by casting its null to the parameter type, which a type argument would hide:
// AddValidator<Person>(null!) reads as registering a validator type. A target selector on a string? chain is picked by
// its parameter's name instead: a typed Expression argument infers the chain's type without its annotation (CS8620).
// ReSharper disable CanReplaceCastWithShorterTypeArgument
public sealed class NullArgumentTests
{
    private static readonly Validator<Address> AddressValidator = new(x => x.Property(address => address.City).NotEmpty());

    public static TheoryData<string, string, Action> Declarations => new()
    {
        {
            "Validator(configure)", "configure",
            () => _ = new Validator<Person>(null!)
        },
        {
            "Property", "expression",
            () => Declare(x => x.Property((Expression<Func<Person, string?>>)null!))
        },
        {
            "Elements", "expression",
            () => Declare(x => x.Elements((Expression<Func<Person, IEnumerable<string?>?>>)null!))
        },
        {
            "Elements Property", "expression",
            () => Declare(x => x.Elements(person => person.Tags).Property((Expression<Func<string?, int>>)null!))
        },
        {
            "block Where condition", "condition",
            () => Declare(x => x.Where((Func<Person, bool>)null!, () => { }))
        },
        {
            "block Where configure", "configure",
            () => Declare(x => x.Where(_ => true, null!))
        },
        {
            "async block Where", "condition",
            () => Declare(x => x.Where((Func<Person, CancellationToken, Task<bool>>)null!, () => { }))
        },
        {
            "chain Where", "condition",
            () => Declare(x => x.Property(person => person.Name).Where((Func<Person, bool>)null!))
        },
        {
            "async chain Where", "condition",
            () => Declare(x => x.Property(person => person.Name).Where((Func<Person, Task<bool>>)null!))
        },
        {
            "Elements Where", "condition",
            () => Declare(x => x.Elements(person => person.Tags).Where((Func<Person, bool>)null!))
        },
        {
            "Must", "predicate",
            () => Declare(x => x.Property(person => person.Name).Must((Func<string?, bool>)null!))
        },
        {
            "Must with context", "predicate",
            () => Declare(x => x.Property(person => person.Name).Must((Func<string?, ValidationContext<Person>, bool>)null!))
        },
        { "MustAsync", "predicate", () => Declare(x => x.Property(person => person.Name).MustAsync((Func<string?, Task<bool>>)null!)) },
        {
            "MustAsync with token", "predicate",
            () => Declare(x => x.Property(person => person.Name).MustAsync((Func<string?, CancellationToken, Task<bool>>)null!))
        },
        {
            "MustAsync with context", "predicate",
            () => Declare(x =>
                x.Property(person => person.Name).MustAsync((Func<string?, ValidationContext<Person>, CancellationToken, Task<bool>>)null!))
        },
        {
            "Equal target", "expression",
            () => Declare(x => x.Property(person => person.Name).Equal(expression: null!))
        },
        {
            "NotEqual target", "expression",
            () => Declare(x => x.Property(person => person.Name).NotEqual(expression: null!))
        },
        {
            "GreaterThan target", "expression",
            () => Declare(x => x.Property(person => person.Age).GreaterThan(null!))
        },
        {
            "InclusiveBetween from", "from",
            () => Declare(x => x.Property(person => person.Age).InclusiveBetween(null!, person => person.Age))
        },
        {
            "ExclusiveBetween to", "to",
            () => Declare(x => x.Property(person => person.Age).ExclusiveBetween(person => person.Age, null!))
        },
        {
            "Include", "validator",
            () => Declare(x => x.Property(person => person.Address).Include((Validator<Address>)null!))
        },
        {
            "Length min", "min",
            () => Declare(x => x.Property(person => person.Name).Length(null!, _ => 1))
        },
        {
            "Length max", "max",
            () => Declare(x => x.Property(person => person.Name).Length(_ => 1, null!))
        },
        {
            "exact Length", "func",
            () => Declare(x => x.Property(person => person.Name).Length(null!))
        },
        {
            "MaximumLength", "func",
            () => Declare(x => x.Property(person => person.Name).MaximumLength(null!))
        },
        {
            "MinimumLength", "func",
            () => Declare(x => x.Property(person => person.Name).MinimumLength(null!))
        },
        {
            "Regex", "regex",
            () => Declare(x => x.Property(person => person.Name).Regex((Regex)null!))
        },
        {
            "Password", "requirements",
            () => Declare(x => x.Property(person => person.Password).Password(null!))
        },
        {
            "Regex from the model", "func",
            () => Declare(x => x.Property(person => person.Name).Regex((Func<Person, Regex?>)null!))
        },
        {
            "Regex pattern from the model", "func",
            () => Declare(x => x.Property(person => person.Name).Regex((Func<Person, string?>)null!))
        },
        {
            "InEnum type", "enumType",
            () => Declare(x => x.Property(person => person.Name).InEnum(null!))
        },
        {
            "Add", "validator",
            () => Declare(x => x.Property(person => person.Name).Add((IPropertyValidator<Person, string?>)null!))
        },
        {
            "async Add", "validator",
            () => Declare(x => x.Property(person => person.Name).Add((IAsyncPropertyValidator<Person, string?>)null!))
        },
        {
            "Elements Property Add", "validator",
            () => Declare(x => x.Elements(person => person.Tags).Property(tag => tag!.Length)
                .Add((IPropertyValidator<Person, int>)null!))
        },
        {
            "Elements Property async Add", "validator",
            () => Declare(x => x.Elements(person => person.Tags).Property(tag => tag!.Length)
                .Add((IAsyncPropertyValidator<Person, int>)null!))
        },
        {
            "string rule Add", "validator",
            () => Declare(x => AddOverGenericChain(x.Property(person => person.Name), (IPropertyValidator<Person, string?>)null!))
        },
        {
            "async string rule Add", "validator",
            () => Declare(x => AddOverGenericChain(x.Property(person => person.Name), (IAsyncPropertyValidator<Person, string?>)null!))
        },
        {
            "IncludeProperty", "expression",
            () => new ValidationContext<Person>(new Person()).IncludeProperty((Expression<Func<Person, string?>>)null!)
        },
        {
            "Validate(model, configure)", "configure",
            () => new Validator<Person>(_ => { }).Validate(new Person(), null!)
        },
        {
            "ValidateAsync(model, configure)", "configure",
            () => _ = new Validator<Person>(_ => { }).ValidateAsync(new Person(), null!)
        },
        {
            "PredicateValidator", "predicate",
            () => _ = new PredicateValidator<Person, string?>(null!)
        },
        {
            "RegexValidator", "regex",
            () => _ = new RegexValidator<Person>((Regex)null!)
        },
        {
            "EqualValidator", "target",
            () => _ = new EqualValidator<Person, string?>((Func<Person, string?>)null!)
        },
        {
            "NotEqualValidator", "target",
            () => _ = new NotEqualValidator<Person, string?>((Func<Person, string?>)null!)
        },
        {
            "GreaterThanValidator", "target",
            () => _ = new GreaterThanValidator<Person, int>(null!)
        },
        {
            "GreaterThanOrEqualValidator", "target",
            () => _ = new GreaterThanOrEqualValidator<Person, int>(null!)
        },
        {
            "LessThanValidator", "target",
            () => _ = new LessThanValidator<Person, int>(null!)
        },
        {
            "LessThanOrEqualValidator", "target",
            () => _ = new LessThanOrEqualValidator<Person, int>(null!)
        },
        {
            "InclusiveBetweenValidator from", "from",
            () => _ = new InclusiveBetweenValidator<Person, int>(null!, _ => 1)
        },
        {
            "InclusiveBetweenValidator to", "to",
            () => _ = new InclusiveBetweenValidator<Person, int>(_ => 1, null!)
        },
        {
            "ExclusiveBetweenValidator from", "from",
            () => _ = new ExclusiveBetweenValidator<Person, int>(null!, _ => 1)
        },
        {
            "ExclusiveBetweenValidator to", "to",
            () => _ = new ExclusiveBetweenValidator<Person, int>(_ => 1, null!)
        },
        {
            "ExactLengthValidator", "func",
            () => _ = new ExactLengthValidator<Person>(null!)
        },
        {
            "LengthValidator", "minimum",
            () => _ = new LengthValidator<Person>(null!, _ => 1)
        },
        {
            "AddValidator(type)", "type",
            () => new ServiceCollection().AddValidator(null!, ServiceLifetime.Singleton)
        },
        {
            "AddValidator(instance)", "validator",
            () => new ServiceCollection().AddValidator((IValidator<Person>)null!)
        },
        {
            "AddValidator(instance) on no services", "services",
            () => ((IServiceCollection)null!).AddValidator(new Validator<Person>(_ => { }))
        },
        {
            "AddValidators(assembly)", "assembly",
            () => new ServiceCollection().AddValidators((Assembly)null!)
        },
        {
            "AddValidators(assemblies)", "assemblies",
            () => new ServiceCollection().AddValidators((IEnumerable<Assembly>)null!)
        }
    };

    [Theory]
    [MemberData(nameof(Declarations))]
    public void NullArgument_ThrowsWhereItIsPassed(string member, string parameter, Action declare)
    {
        var exception = Assert.Throws<ArgumentNullException>(declare);
        Assert.True(parameter == exception.ParamName, $"{member}: expected '{parameter}', got '{exception.ParamName}'.");
        Assert.NotNull(AddressValidator);
    }

    private static void Declare(Action<ValidatorBuilder<Person>> configure) => _ = new Validator<Person>(configure);

    // The string rules' Add binds only where the chain's type is generic, as in an app's string rule extension.
    private static void AddOverGenericChain<TString>(PropertyValidatorBuilder<Person, TString> chain, IPropertyValidator<Person, string?> rule)
        where TString : class?, IEquatable<string>? =>
        chain.Add(rule);

    private static void AddOverGenericChain<TString>(PropertyValidatorBuilder<Person, TString> chain, IAsyncPropertyValidator<Person, string?> rule)
        where TString : class?, IEquatable<string>? =>
        chain.Add(rule);
}