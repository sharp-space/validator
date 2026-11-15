using System.Reflection;

namespace Sharp.Validator.Tests.Validators;

// A subclass of a built-in rule could set a message after the rule had decided whether to build its parameters, and
// placeholders such as {TargetValue} then went unsubstituted. Built-in rules are sealed, and the abstract ones the
// library derives from offer no constructor outside it, so a custom rule starts from
// PropertyValidator<TModel, TProperty> or calls a built-in rule.
public sealed class BuiltInRuleSealingTests
{
    [Fact]
    public void BuiltInRules_CannotBeDerivedFromOutsideTheLibrary()
    {
        var rules = typeof(PropertyValidator).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(RegexValidator<>).Namespace && typeof(PropertyValidator).IsAssignableFrom(type))
            .ToList();

        var open = rules
            .Where(type => type.IsAbstract
                ? type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly)
                : !type.IsSealed)
            .Select(type => type.Name);

        Assert.True(rules.Count > 20, $"found only {rules.Count} built-in rules");
        Assert.Empty(open);
    }
}