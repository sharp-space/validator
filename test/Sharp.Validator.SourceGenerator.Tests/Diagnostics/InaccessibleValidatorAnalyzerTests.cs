using Microsoft.CodeAnalysis;
using Sharp.Validator.SourceGenerator.Analyzers;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Diagnostics;

public sealed class InaccessibleValidatorAnalyzerTests
{
    [Fact]
    public async Task InaccessibleValidatorAnalyzer_ReportsSVL001OnAFileLocalValidator()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(
            new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            file sealed class Model { }
            file sealed class ModelValidator : Validator<Model> { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SVL001", diagnostic.Id);
        Assert.Contains("file-local", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_ReportsSVL001OnAPrivateNestedValidatorAtItsDeclaration()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Model { public string? Name { get; set; } }

            public static class Host
            {
                private sealed class HiddenValidator : Validator<Model>
                {
                    public HiddenValidator() : base(x => x.Property(m => m.Name).NotEmpty()) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SVL001", diagnostic.Id);

        // The whole point of the analyzer migration: the warning lands on the type declaration
        // (navigable, #pragma-suppressible), not Location.None.
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("HiddenValidator", diagnostic.GetMessage());
    }

    [Fact]
    // A private validator its surrounding code creates, as in Include(new AddressValidator()), was never meant for DI,
    // so SVL001 was noise there.
    public async Task InaccessibleValidatorAnalyzer_PrivateValidatorCreatedByItsContainingType_ReportsNothing()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Address { public string? City { get; set; } }

            public sealed class Order { public Address? Shipping { get; set; } }

            public sealed class OrderValidator : Validator<Order>
            {
                public OrderValidator() => Builder.Property(x => x.Shipping).Include(new AddressValidator());

                private sealed class AddressValidator : Validator<Address>
                {
                    public AddressValidator() => Builder.Property(x => x.City).NotEmpty();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_PrivateValidatorCreatedWithTargetTypedNew_ReportsNothing()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Address { public string? City { get; set; } }

            public sealed class Order { public Address? Billing { get; set; } }

            public sealed partial class OrderValidator : Validator<Order>
            {
                private readonly AddressValidator _billing = new();
            }

            public sealed partial class OrderValidator
            {
                public OrderValidator() => Builder.Property(x => x.Billing).Include(_billing);

                private sealed class AddressValidator : Validator<Address>
                {
                    public AddressValidator() => Builder.Property(x => x.City).NotEmpty();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_FileLocalValidatorCreatedByItsFile_ReportsNothing()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Address { public string? City { get; set; } }

            public sealed class Order { public Address? Shipping { get; set; } }

            public sealed class OrderValidator : Validator<Order>
            {
                public OrderValidator() => Builder.Property(x => x.Shipping).Include(new AddressValidator());
            }

            file sealed class AddressValidator : Validator<Address>
            {
                public AddressValidator() => Builder.Property(x => x.City).NotEmpty();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_OnlyANamesakeCreated_StillReportsThePrivateValidator()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App
            {
                public sealed class Address { public string? City { get; set; } }

                public sealed class Order { public Address? Shipping { get; set; } }

                public sealed class OrderValidator : Validator<Order>
                {
                    public OrderValidator() => Builder.Property(x => x.Shipping).Include(new Shared.AddressValidator());

                    private sealed class AddressValidator : Validator<Address>
                    {
                        public AddressValidator() => Builder.Property(x => x.City).NotEmpty();
                    }
                }
            }

            namespace App.Shared
            {
                public sealed class AddressValidator : Validator<App.Address>
                {
                    public AddressValidator() => Builder.Property(x => x.City).NotEmpty();
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("OrderValidator.AddressValidator", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_ProtectedInternalNestedValidators_ReportNothing()
    {
        // protected internal is accessible assembly-wide — the old DeclaredAccessibility check false-positived here.
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Model { public string? Name { get; set; } }

            public class Host
            {
                protected internal sealed class NestedValidator : Validator<Model>
                {
                    public NestedValidator() : base(x => x.Property(m => m.Name).NotEmpty()) { }
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_WhenTheModelTypeArgumentIsInaccessible_ReportsSVL001()
    {
        // typeof(IValidator<PrivateModel>) would not compile in the registrar even though the validator class
        // itself is public.
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public static class Host
            {
                private sealed class PrivateModel { public string? Name { get; set; } }

                public sealed class ExposedValidator : Validator<PrivateModel>
                {
                    public ExposedValidator() : base(x => x.Property(m => m.Name).NotEmpty()) { }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SVL001", diagnostic.Id);
    }

    [Fact]
    public async Task InaccessibleValidatorAnalyzer_OrdinaryPublicValidators_ReportNothing()
    {
        var diagnostics = await GeneratorTestHost.RunAnalyzerAsync(new InaccessibleValidatorAnalyzer(),
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Model { public string? Name { get; set; } }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).NotEmpty()) { }
            }
            """);

        Assert.Empty(diagnostics);
    }
}