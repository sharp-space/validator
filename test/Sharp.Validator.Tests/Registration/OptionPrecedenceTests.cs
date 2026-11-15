using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Registration;

[Collection(GlobalOptions.Name)]
// Regression: scan defaults were merged into options that already held Global's values, so the ??= merge kept Global
// and the more specific AddValidators setting was silently ignored. The precedence is Configure, then scan, then
// Global, resolved per field when the validator freezes.
public sealed class OptionPrecedenceTests
{
    private static string ReportedName<TValidator>(ValidatorBuilderOptions scan)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ValidatorRegistry.CreateDescriptor(
            typeof(IValidator<PrecedenceModel>), typeof(TValidator), ServiceLifetime.Singleton, scan));

        using var provider = services.BuildServiceProvider();
        var result = provider.GetRequiredService<IValidator<PrecedenceModel>>().Validate(new PrecedenceModel());
        return Assert.Single(result.Errors).PropertyName;
    }

    private static void WithGlobal(ValidatorBuilderOptions global, Action test)
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = global;
            test();
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    public void Descriptor_Defaults_DoNotRequireTheImplementationToBeAConfigurableValidator()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ValidatorRegistry.CreateDescriptor(typeof(object), typeof(object), ServiceLifetime.Singleton,
            new ValidatorBuilderOptions()));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<object>(provider.GetRequiredService<object>());
    }

    [Fact]
    public void ScanDefault_WinsOverGlobal() =>
        WithGlobal(new ValidatorBuilderOptions { PropertyNamePolicy = PropertyNamePolicy.SnakeCase }, () =>
            Assert.Equal("firstName", ReportedName<PlainValidator>(
                new ValidatorBuilderOptions { PropertyNamePolicy = PropertyNamePolicy.CamelCase })));

    [Fact]
    public void Configure_WinsOverScanDefaultAndGlobal() =>
        WithGlobal(new ValidatorBuilderOptions { PropertyNamePolicy = PropertyNamePolicy.SnakeCase }, () =>
            Assert.Equal("first-name", ReportedName<ConfiguredValidator>(
                new ValidatorBuilderOptions { PropertyNamePolicy = PropertyNamePolicy.CamelCase })));

    [Fact]
    public void Precedence_ResolvesPerField() =>
        WithGlobal(new ValidatorBuilderOptions { PropertyNamePrefix = "G_" }, () =>
            Assert.Equal("G_firstName", ReportedName<PlainValidator>(
                new ValidatorBuilderOptions { PropertyNamePolicy = PropertyNamePolicy.CamelCase })));

    [Fact]
    public void GlobalSetAfterConfigure_StillAppliesAtFirstUse()
    {
        var validator = new Validator<PrecedenceModel>(x => x.Property(m => m.FirstName).NotEmpty());
        validator.Configure(x => x.PropertyNamePrefix = "LOCAL_");

        WithGlobal(new ValidatorBuilderOptions { PropertyNameSuffix = "_GLOBAL" }, () =>
            Assert.Equal("LOCAL_FirstName_GLOBAL", validator.Validate(new PrecedenceModel()).Errors[0].PropertyName));
    }

    [Fact]
    // ValidatorServiceCollectionExtensions.CreateDefaults via a configure that sets all six option fields, reached
    // through the registry-hit generated-registration path.
    public void AddValidatorsConfigure_AppliesScanLocalDefaultsWithoutMutatingGlobalOptions()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions(); // every field unset

            var assembly = typeof(ValidationError).Assembly; // Sharp.Validator.Result: no generator, never scanned
            ValidatorRegistry.AddRegistrar(assembly, (services, lifetime, defaults) =>
                services.Add(ValidatorRegistry.CreateDescriptor(
                    typeof(IValidator<RegModel>), typeof(RegValidator), lifetime(typeof(RegValidator)), defaults)));

            var services = new ServiceCollection();
            services.AddValidators(assembly, options =>
            {
                options.CascadeMode = ValidationCascadeMode.Stop;
                options.PropertyNamePolicy = PropertyNamePolicy.SnakeCase;
                options.PropertyNamePrefix = "P_";
                options.PropertyNameSuffix = "_S";
                options.ErrorMessagePrefix = "E_";
                options.ErrorMessageSuffix = "_E";
            });

            var global = ValidatorBuilderOptions.Global;
            Assert.Null(global.CascadeMode);
            Assert.Null(global.PropertyNamePolicy);
            Assert.Null(global.PropertyNamePrefix);
            Assert.Null(global.PropertyNameSuffix);
            Assert.Null(global.ErrorMessagePrefix);
            Assert.Null(global.ErrorMessageSuffix);

            using var provider = services.BuildServiceProvider();
            var validator = provider.GetRequiredService<IValidator<RegModel>>();
            var result = validator.Validate(new RegModel());
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
            Assert.Equal("P_value_S", result.Errors[0].PropertyName);
            Assert.Equal("E_'P_value_S' must not be empty._E", result.Errors[0].Message);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    // The Global fallback for ErrorMessagePrefix, ErrorMessageSuffix, and PropertyNameSuffix.
    public void Builder_InheritsGlobalMessageAffixesAndNameSuffix()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions
            {
                ErrorMessagePrefix = "[",
                ErrorMessageSuffix = "]",
                PropertyNameSuffix = "_S"
            };

            var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
                .NotEmpty(x => x.ErrorMessage = "required"));

            var result = validator.Validate(new Person { Name = null });
            Assert.False(result.IsValid);
            Assert.Equal("Name_S", result.Errors[0].PropertyName);
            Assert.Equal("[required]", result.Errors[0].Message);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    public sealed class PrecedenceModel
    {
        public string? FirstName { get; init; }
    }

    public sealed class PlainValidator : Validator<PrecedenceModel>
    {
        public PlainValidator() => Builder.Property(x => x.FirstName).NotEmpty();
    }

    public sealed class ConfiguredValidator : Validator<PrecedenceModel>
    {
        public ConfiguredValidator()
        {
            Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.KebabCase);
            Builder.Property(x => x.FirstName).NotEmpty();
        }
    }
}