namespace Sharp.Validator.Tests.Validators;

// The variants that exist only for how a rule is implemented, such as the nullable enum rule, one rule per password
// threshold, or one date rule per date type, and the constructors that take the accessor tuple the library uses inside
// were public, so none of them could change after release. They are internal: the extension methods build the variants,
// and a rule over a computed target takes a function of the model.
public sealed class PublicRuleTypesTests
{
    [Fact]
    public void ExportedRules_AreTheMainRuleTypes()
    {
        string[] expected =
        [
            "AsyncPredicateValidator", "ComparisonValidator", "CreditCardValidator", "EmailValidator", "EmptyValidator", "EnumValidator",
            "EqualValidator", "ExactLengthValidator", "ExclusiveBetweenValidator", "GreaterThanOrEqualValidator", "GreaterThanValidator",
            "InclusiveBetweenValidator", "LengthValidator", "LessThanOrEqualValidator", "LessThanValidator", "MaximumLengthValidator",
            "MinimumLengthValidator", "NotEmptyValidator", "NotEqualValidator", "NotNullValidator", "NullValidator", "PhoneNumberValidator",
            "PrecisionScaleValidator", "PredicateValidator", "RangeValidator", "RegexValidator", "StringLengthValidator", "UriValidator"
        ];

        var exported = typeof(PropertyValidator).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(RegexValidator<>).Namespace && typeof(PropertyValidator).IsAssignableFrom(type))
            .Select(type => type.Name[..type.Name.IndexOf('`')])
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), exported);
    }

    [Theory]
    [InlineData(typeof(EqualValidator<,>))]
    [InlineData(typeof(NotEqualValidator<,>))]
    [InlineData(typeof(GreaterThanValidator<,>))]
    [InlineData(typeof(GreaterThanOrEqualValidator<,>))]
    [InlineData(typeof(LessThanValidator<,>))]
    [InlineData(typeof(LessThanOrEqualValidator<,>))]
    [InlineData(typeof(InclusiveBetweenValidator<,>))]
    [InlineData(typeof(ExclusiveBetweenValidator<,>))]
    public void TargetRules_TakeNoAccessorTupleOutsideTheLibrary(Type rule) =>
        Assert.DoesNotContain(rule.GetConstructors(), constructor => constructor.GetParameters().Any(parameter =>
            parameter.ParameterType is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(Func<,>) &&
            type.GetGenericArguments()[1] is { IsGenericType: true } result &&
            result.GetGenericTypeDefinition() == typeof(ValueTuple<,>)));
}