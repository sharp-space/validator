namespace Sharp.Validator.Tests.Pipeline;

[Collection(GlobalOptions.Name)]
// Regression: Builder.Options must not expose the ValidatorBuilderOptions.Global instance directly. A later
// Configure(...) on a single validator must never mutate the shared global, and validators should still inherit
// Global's defaults for unset fields.
public sealed class GlobalOptionsIsolationTests
{
    [Fact]
    public void Configure_DoesNotMutateGlobalCascadeMode()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions
            {
                CascadeMode = ValidationCascadeMode.Continue
            };

            var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
            validator.Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);

            Assert.Equal(ValidationCascadeMode.Continue, ValidatorBuilderOptions.Global.CascadeMode);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    public void Configure_DoesNotMutateGlobalPropertyNamePrefix()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions
            {
                PropertyNamePrefix = "GLOBAL_"
            };

            var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
            validator.Configure(x => x.PropertyNamePrefix = "LOCAL_");

            Assert.Equal("GLOBAL_", ValidatorBuilderOptions.Global.PropertyNamePrefix);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    public void Builder_InheritsGlobalDefaultsWhenNotOverridden()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions
            {
                PropertyNamePolicy = PropertyNamePolicy.SnakeCase
            };

            var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
            var result = validator.Validate(new Person { Name = null });
            Assert.Equal("name", result.Errors[0].PropertyName);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    public void TwoValidators_AfterConfigure_DoNotShareOptions()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions
            {
                PropertyNamePolicy = PropertyNamePolicy.SnakeCase
            };

            var firstValidator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
            var secondValidator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
            firstValidator.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.KebabCase);

            // 'secondValidator' must not see 'firstValidator's change; it still reflects Global.
            Assert.Equal("name", secondValidator.Validate(new Person { Name = null }).Errors[0].PropertyName);
            Assert.Equal("name", firstValidator.Validate(new Person { Name = null }).Errors[0].PropertyName);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }
}