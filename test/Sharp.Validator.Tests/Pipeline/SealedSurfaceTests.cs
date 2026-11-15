using System.ComponentModel;
using System.Reflection;

namespace Sharp.Validator.Tests.Pipeline;

// None of these types was meant as an extension point, and a subclass could break what the library promises about one,
// such as a result that never changes once built. They are sealed, and the bases that only the library's own chains and
// rules derive from offer no constructor outside it, so new PropertyValidatorBuilder<T>() and new ValidatorBuilder<T>()
// no longer compile. Validator<TModel>, PropertyValidator<TModel, TProperty>, and
// AsyncPropertyValidator<TModel, TProperty> stay open for custom validators and rules.
public sealed class SealedSurfaceTests
{
    [Theory]
    [InlineData(typeof(ValidationResult))]
    [InlineData(typeof(ValidationContext<>))]
    [InlineData(typeof(ValidatorBuilder<>))]
    [InlineData(typeof(ElementValidatorBuilder<,>))]
    [InlineData(typeof(ElementPropertyValidatorBuilder<,,>))]
    [InlineData(typeof(ValidatorBuilderOptions))]
    [InlineData(typeof(PropertyValidatorBuilderOptions))]
    [InlineData(typeof(PropertyValidatorOptions))]
    [InlineData(typeof(IncludeValidatorOptions))]
    [InlineData(typeof(PasswordValidatorOptions))]
    public void LibraryType_IsSealed(Type type) => Assert.True(type.IsSealed);

    [Theory]
    [InlineData(typeof(ValidatorBuilder<>))]
    [InlineData(typeof(PropertyValidatorBuilder<>))]
    [InlineData(typeof(PropertyValidator))]
    public void LibraryBase_OffersNoConstructorOutsideTheLibrary(Type type) =>
        Assert.DoesNotContain(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly);

    [Theory]
    [InlineData(typeof(IPropertyValidator<,>))]
    [InlineData(typeof(IAsyncPropertyValidator<,>))]
    // The rule interfaces are public only because Add's signature names them, for their contravariance. Every member is
    // internal, so no type outside the library implements them and a caller finds nothing to call, and completion
    // doesn't list them: a rule derives from PropertyValidator<TModel, TProperty> or its async form.
    public void RuleInterface_HasOnlyInternalMembers(Type type) =>
        Assert.All(type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), member =>
            Assert.True(member is MethodInfo { IsAssembly: true } or PropertyInfo { GetMethod.IsAssembly: true }, member.Name));

    [Theory]
    [InlineData(typeof(IPropertyValidator<,>))]
    [InlineData(typeof(IAsyncPropertyValidator<,>))]
    public void RuleInterface_IsHiddenFromCompletion(Type type) =>
        Assert.Equal(EditorBrowsableState.Never, type.GetCustomAttribute<EditorBrowsableAttribute>()?.State);

    [Fact]
    public void ChainBase_IsAbstract() => Assert.True(typeof(PropertyValidatorBuilder<>).IsAbstract);

    [Fact]
    public void ConfigurePropertyValidator_IsHiddenFromSubclassesOutsideTheLibrary()
    {
        var method = typeof(PropertyValidatorBuilder<,>).GetMethod("ConfigurePropertyValidator",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.True(method.IsFamilyAndAssembly);
    }
}