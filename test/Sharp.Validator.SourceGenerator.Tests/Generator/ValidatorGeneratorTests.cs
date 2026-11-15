using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sharp.Validator.SourceGenerator.Tests.Fixtures;

namespace Sharp.Validator.SourceGenerator.Tests.Generator;

public sealed class ValidatorGeneratorTests
{
    [Fact]
    public void Registrations_WithUnrelatedRecords_AreSortedByImplementation()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;
            public record Plain;
            public record Derived : Plain;
            public interface IMarker { }
            public struct Value : IMarker { }
            public sealed class Model { }
            public sealed class ZValidator : Validator<Model> { }
            public sealed class AValidator : Validator<Model> { }
            """);

        GeneratorTestHost.AssertCompiles(output);

        Assert.True(generated.IndexOf("typeof(global::AValidator)", StringComparison.Ordinal) <
            generated.IndexOf("typeof(global::ZValidator)", StringComparison.Ordinal));

        Assert.DoesNotContain("typeof(global::Derived)", generated);
    }

    [Fact]
    public void ValidatorGenerator_PublicValidator_EmitsCompilingRegistrar()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Model { public string? Name { get; set; } }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.Name).NotEmpty()) { }
            }
            """);

        Assert.Contains("GeneratedValidatorRegistrar", generated);
        Assert.Contains("typeof(global::Sharp.Validator.IValidator<global::App.Model>)", generated);
        Assert.Contains("typeof(global::App.ModelValidator)", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // A validator is registered once, as itself, with every interface it implements forwarded to that registration. A
    // partial validator is discovered once per declaration and still registered once.
    public void ValidatorGenerator_RegistersEachValidatorOnceWithEveryInterface()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Sharp.Validator;
            using Sharp.Validator.Result;

            namespace App;

            public sealed class Invoice { public string? Number { get; set; } }

            public sealed class Receipt { public string? Number { get; set; } }

            public sealed partial class PaperValidator : Validator<Invoice>
            {
                public PaperValidator() : base(x => x.Property(m => m.Number).NotEmpty()) { }
            }

            public sealed partial class PaperValidator : IValidator<Receipt>
            {
                ValidationResult IValidator<Receipt>.Validate(Receipt model, Action<ValidationContext<Receipt>>? configure) =>
                    throw new NotSupportedException();

                ValidationResult IValidator<Receipt>.Validate(ValidationContext<Receipt> context) => throw new NotSupportedException();

                ValueTask<ValidationResult> IValidator<Receipt>.ValidateAsync(Receipt model, CancellationToken cancellationToken) =>
                    throw new NotSupportedException();

                ValueTask<ValidationResult> IValidator<Receipt>.ValidateAsync(
                    Receipt model, Action<ValidationContext<Receipt>>? configure, CancellationToken cancellationToken) =>
                    throw new NotSupportedException();

                ValueTask<ValidationResult> IValidator<Receipt>.ValidateAsync(
                    ValidationContext<Receipt> context, CancellationToken cancellationToken) =>
                    throw new NotSupportedException();
            }
            """);

        var registration = Assert.Single(generated.Split('\n'),
            static line => line.Contains("ValidatorRegistry.Register(", StringComparison.Ordinal));

        Assert.Contains(
            "Register(services, typeof(global::App.PaperValidator), lifetime(typeof(global::App.PaperValidator)), defaults, " +
            "typeof(global::Sharp.Validator.IValidator<global::App.Invoice>), typeof(global::Sharp.Validator.IValidator<global::App.Receipt>));",
            registration);

        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    public void ValidatorGenerator_EscapesKeywordIdentifiersInGeneratedAccessors()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Model { public string? @event { get; set; } }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(x => x.Property(m => m.@event).NotEmpty()) { }
            }
            """);

        // Escaped at the member-access site, raw in the registry key (must match the runtime's member.Name path).
        Assert.Contains("m.@event", generated);
        Assert.Contains("\"event\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    public void ValidatorGenerator_SkipsStructValidatorsLikeTheReflectionScan()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Sharp.Validator;
            using Sharp.Validator.Result;

            namespace App;

            public sealed class Model { public string? Name { get; set; } }

            public struct StructValidator : IValidator<Model>
            {
                public ValidationResult Validate(object model) => throw new NotSupportedException();
                public ValidationResult Validate(Model model, Action<ValidationContext<Model>>? configure = null) => throw new NotSupportedException();
                public ValidationResult Validate(ValidationContext<Model> context) => throw new NotSupportedException();
                public ValueTask<ValidationResult> ValidateAsync(object model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
                public ValueTask<ValidationResult> ValidateAsync(Model model, CancellationToken cancellationToken = default) => throw new NotSupportedException();
                public ValueTask<ValidationResult> ValidateAsync(Model model, Action<ValidationContext<Model>>? configure = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
                public ValueTask<ValidationResult> ValidateAsync(ValidationContext<Model> context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            }
            """);

        Assert.DoesNotContain("StructValidator", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    public void ValidatorGenerator_ElementPropertySelectors_EmitMatrixSkippingCasts()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using System.Collections.Generic;
            using Sharp.Validator;

            namespace App;

            public sealed class Model
            {
                public Container? Container { get; set; }
                public List<Item?> Items { get; set; } = [];
                public List<int?> Numbers { get; set; } = [];
            }

            public sealed class Container { public List<Item?> Items { get; set; } = []; }
            public sealed class Item
            {
                public string? Label { get; set; }
                public object? CastOnly { get; set; }
                public Child? Child { get; set; }
                public string? @event { get; set; }
            }
            public sealed class Child { public string? Label { get; set; } }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator() : base(builder =>
                {
                    builder.Elements(x => x.Container!.Items).Property(x => x!.Label).NotEmpty();
                    builder.Elements(x => x.Items).Property(x => x!.Child!.Label).NotEmpty();
                    builder.Elements(x => x.Items).Property(x => x!.@event).NotEmpty();
                    builder.Elements(x => x.Numbers).Property(x => x!.Value).GreaterThan(0);
                    builder.Elements(x => x.Items).Property(x => (string?)x!.CastOnly).NotEmpty();
                }) { }
            }
            """);

        Assert.Contains("RegisterElements<global::App.Model, global::App.Item>(\"Container.Items\"", generated);
        Assert.Contains("RegisterProperty<global::App.Item, string>(\"Label\"", generated);
        Assert.Contains("RegisterNullSafe<global::App.Item, string>(\"Child.Label\"", generated);
        Assert.Contains("m.@event", generated);
        Assert.Contains("(\"\", static (", generated);
        Assert.Contains("m.Value", generated);
        Assert.DoesNotContain("CastOnly", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // The registry key leaves out a Nullable<T>.Value hop, so a chain that converts a nullable leaf, or the Value it
    // ends at, shared its key with a plain read of the same member, and whichever came first served both: a null age
    // read through the Value's accessor skipped a NotNull on the plain read. Such a conversion isn't registered now,
    // which the runtime ignores anyway, while a plain read and a Value read as its own type still are.
    public void ConversionOfANullableLeafOrItsValue_IsNotRegistered()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Patient { public int? Age { get; set; } }
            public sealed class Visit
            {
                public Patient Patient { get; set; } = new();
                public int? Retirement { get; set; }
            }

            public sealed class VisitValidator : Validator<Visit>
            {
                public VisitValidator() : base(builder =>
                {
                    builder.Property(x => x.Retirement).GreaterThan(x => x.Patient.Age!.Value);
                    builder.Property<object?>(x => x.Patient.Age).NotNull();
                    builder.Property(x => x.Patient.Age).NotNull();
                    builder.Property(x => x.Patient.Age!.Value).GreaterThan(0);
                }) { }
            }
            """);

        Assert.Single(generated.Split("RegisterNullSafe<global::App.Visit, int?>(\"Patient.Age\"").Skip(1));
        Assert.Contains("return (true, t0.Age);", generated);
        Assert.Contains("RegisterNullSafe<global::App.Visit, int>(\"Patient.Age\"", generated);
        Assert.DoesNotContain("global::App.Visit, object", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // The comparison and range rules take a comparer in an overload of its own now; their target lambdas keep the first
    // positions, so the generator precompiles them in those forms too.
    public void TargetsOfTheComparerOverloads_AreRegistered()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using System.Collections.Generic;
            using Sharp.Validator;

            namespace App;

            public sealed class Order
            {
                public int Quantity { get; set; }
                public int Other { get; set; }
                public int Low { get; set; }
                public int High { get; set; }
            }

            public sealed class OrderValidator : Validator<Order>
            {
                public OrderValidator() : base(builder =>
                {
                    builder.Property(x => x.Quantity).GreaterThan(x => x.Other, Comparer<int>.Default);
                    builder.Property(x => x.Quantity)
                        .InclusiveBetween(x => x.Low, x => x.High, Comparer<int>.Default, o => o.ErrorCode = "Range");
                }) { }
            }
            """);

        Assert.Contains("global::App.Order, int>(\"Other\"", generated);
        Assert.Contains("global::App.Order, int>(\"Low\"", generated);
        Assert.Contains("global::App.Order, int>(\"High\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // The runtime serves a target that C# reads through a widening into a nullable type, Convert(Convert(x, long),
    // long?), from the registration for its path and type. That holds because the registration converts the member as
    // C# does implicitly and a target with a cast of its own isn't registered: a narrowing, or a type in between, can
    // change the value in a way the key doesn't show.
    public void WidenedTarget_IsRegisteredAsTheImplicitConversion_AndACastTargetIsNot()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Limits { public int Floor { get; set; } }
            public sealed class Account
            {
                public long? Balance { get; set; }
                public Limits? Limits { get; set; }
            }

            public sealed class Narrowed
            {
                public short? Small { get; set; }
                public long? Balance { get; set; }
                public Limits? Limits { get; set; }
            }

            public sealed class AccountValidator : Validator<Account>
            {
                public AccountValidator() : base(builder =>
                    builder.Property(x => x.Balance).GreaterThanOrEqual(x => x.Limits!.Floor)) { }
            }

            public sealed class NarrowedValidator : Validator<Narrowed>
            {
                public NarrowedValidator() : base(builder =>
                {
                    builder.Property(x => x.Small).GreaterThanOrEqual(x => (short?)x.Limits!.Floor);
                    builder.Property(x => x.Balance).GreaterThanOrEqual(x => (long?)(short)x.Limits!.Floor);
                }) { }
            }
            """);

        Assert.Contains("RegisterNullSafe<global::App.Account, long?>(\"Limits.Floor\"", generated);
        Assert.Contains("return (true, t0.Floor);", generated);
        Assert.DoesNotContain("\"Limits.Floor\", static (global::App.Narrowed", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // Equal and NotEqual took a plain delegate, which needed no accessor; their expression targets now read through the
    // same null-safe accessors as the comparison rules, so Native AOT gets them precompiled too.
    // Regression: the generator precompiled only a lambda written x => ..., so (x) => x.Name, (Model x) => x.Name, and
    // a lambda in parentheses fell back to Expression.Compile, which Native AOT interprets. SVL011 reads all of them as
    // member chains, so the generator precompiles all of them.
    public void PrecompilesAccessors_ForEverySingleParameterLambdaSpelling()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Model
            {
                public string? Name { get; set; }
                public int Quantity { get; set; }
                public int Minimum { get; set; }
                public int Maximum { get; set; }
            }

            public sealed class ModelValidator : Validator<Model>
            {
                public ModelValidator()
                {
                    Builder.Property((m) => m.Name).NotEmpty();
                    Builder.Property((Model m) => m.Quantity).GreaterThan((m) => m.Minimum).LessThan((m => m.Maximum));
                }
            }
            """);

        Assert.Contains("RegisterProperty<global::App.Model, string>(\"Name\"", generated);
        Assert.Contains("RegisterProperty<global::App.Model, int>(\"Quantity\"", generated);
        Assert.Contains("RegisterNullSafe<global::App.Model, int>(\"Minimum\"", generated);
        Assert.Contains("RegisterNullSafe<global::App.Model, int>(\"Maximum\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // A range read from the model has two targets, and both are precompiled.
    public void ValidatorGenerator_EmitsNullSafeAccessorsForBothRangeBounds()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Limits { public int Max { get; set; } }

            public sealed class Order
            {
                public int Quantity { get; set; }
                public int Minimum { get; set; }
                public Limits? Limits { get; set; }
            }

            public sealed class OrderValidator : Validator<Order>
            {
                public OrderValidator()
                {
                    Builder.Property(x => x.Quantity).InclusiveBetween(x => x.Minimum, x => x.Limits!.Max);
                    Builder.Property(x => x.Quantity).ExclusiveBetween(x => x.Minimum, x => x.Minimum * 10);
                }
            }
            """);

        Assert.Contains("RegisterNullSafe<global::App.Order, int>(\"Minimum\"", generated);
        Assert.Contains("RegisterNullSafe<global::App.Order, int>(\"Limits.Max\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    public void ValidatorGenerator_EmitsNullSafeAccessorsForEqualityTargets()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public sealed class Account
            {
                public string? Password { get; set; }
                public string? Confirm { get; set; }
                public Account? Previous { get; set; }
            }

            public sealed class AccountValidator : Validator<Account>
            {
                public AccountValidator() : base(builder =>
                {
                    builder.Property(x => x.Confirm).Equal(x => x.Password);
                    builder.Property(x => x.Password).NotEqual(x => x.Previous!.Password);
                }) { }
            }
            """);

        Assert.Contains("RegisterNullSafe<global::App.Account, string>(\"Password\"", generated);
        Assert.Contains("RegisterNullSafe<global::App.Account, string>(\"Previous.Password\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // A non-nullable string property starts a string chain rather than a string? one, and its comparison target
    // may be nullable. Nullable annotations don't reach the runtime types, so the accessors keep the keys the
    // runtime looks up.
    public void StringChain_WithANullableTarget_RegistersTheSameKeys()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            #nullable enable
            using System.Collections.Generic;
            using Sharp.Validator;

            namespace App;

            public sealed class Account
            {
                public string Name { get; set; } = "";
                public string? Nick { get; set; }
                public List<string> Tags { get; set; } = [];
            }

            public sealed class AccountValidator : Validator<Account>
            {
                public AccountValidator() : base(builder =>
                {
                    builder.Property(x => x.Name).MaximumLength(9).Equal(x => x.Nick);
                    builder.Elements(x => x.Tags).MinimumLength(1);
                }) { }
            }
            """);

        Assert.Contains("RegisterProperty<global::App.Account, string>(\"Name\"", generated);
        Assert.Contains("RegisterNullSafe<global::App.Account, string>(\"Nick\"", generated);
        Assert.Contains("RegisterElements<global::App.Account, string>(\"Tags\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Theory]
    [InlineData("public sealed class Model { public string? Name { get; set; } }")]
    [InlineData(
        """
        public sealed class Model { public string? Name { get; set; } }
        public class Outer { private sealed class Hidden : Validator<Model> { } }
        """)]
    // Regression: an assembly with no validator generated code could name emitted no registrar, so under Native AOT
    // AddValidators(assembly) took it for one built without the generator and threw. The registrar is now emitted for
    // every assembly the generator runs on, empty when there is nothing to register.
    public void Assembly_WithoutAValidatorToRegister_GetsAnEmptyRegistrar(string source)
    {
        var (output, generated) = GeneratorTestHost.RunGenerator("using Sharp.Validator;\n\nnamespace App;\n\n" + source);
        Assert.Contains("GeneratedValidatorRegistrar", generated);
        Assert.Contains("ValidatorRegistry.AddRegistrar(", generated);
        Assert.DoesNotContain("ValidatorRegistry.Register(", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // Regression: discovery checked the property's accessibility, not its getter's. A validator nested in the model can
    // read a private or protected getter, the generated registrar can't, and the emitted accessor broke the consumer's
    // build with CS0271. A selector through such a getter must stay on the compiled fallback.
    public void Selector_ThroughAGetterGeneratedCodeCannotCall_IsSkipped()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            namespace App;

            public class Address
            {
                public string? City { get; set; }
            }

            public class Model
            {
                public string? Name { private get; set; }

                public string? Code { protected get; set; }

                public Address? Home { private get; set; }

                public string? Tag { internal get; set; }

                public class ModelValidator : Validator<Model>
                {
                    public ModelValidator()
                    {
                        Builder.Property(x => x.Name).NotEmpty();
                        Builder.Property(x => x.Code).NotEmpty();
                        Builder.Property(x => x.Home!.City).NotEmpty();
                        Builder.Property(x => x.Tag).NotEmpty();
                    }
                }
            }
            """);

        Assert.DoesNotContain("(\"Name\"", generated);
        Assert.DoesNotContain("(\"Code\"", generated);
        Assert.DoesNotContain("(\"Home.City\"", generated);

        // The generated registrar is in the same assembly, so an internal getter is still read directly.
        Assert.Contains("RegisterProperty<global::App.Model, string>(\"Tag\"", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    public void FileLocalTypes_RegistrarOrAccessorGeneration_AreSkipped()
    {
        var (output, generated) = GeneratorTestHost.RunGenerator(
            """
            using Sharp.Validator;

            file sealed class HiddenModel { public string? Name { get; set; } }
            file sealed class HiddenValidator : Validator<HiddenModel>
            {
                public HiddenValidator() : base(x => x.Property(m => m.Name).NotEmpty()) { }
            }
            """);

        Assert.DoesNotContain("HiddenValidator", generated);
        Assert.DoesNotContain("HiddenModel", generated);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    // Regression: Outer<T>.Inner has no type arguments of its own, so it passed the open-generic check and the emitted
    // accessor named a T that doesn't exist in generated code, breaking the consumer's build.
    public void NestedModel_InAnOpenGenericType_IsSkipped()
    {
        const string source =
            """
            using Sharp.Validator;

            namespace Demo;

            public class Outer<T>
            {
                public class Inner
                {
                    public string? Name { get; set; }
                }
            }

            public class InnerValidator<T> : Validator<Outer<T>.Inner>
            {
                public InnerValidator() => Builder.Property(x => x.Name).NotEmpty();
            }

            public class Factory<T>
            {
                public class Item
                {
                    public string? Name { get; set; }
                }

                public Validator<Item> Make() => new(builder => builder.Property(x => x.Name).NotEmpty());
            }
            """;

        var (output, _) = GeneratorTestHost.RunGenerator(source);
        GeneratorTestHost.AssertCompiles(output);
    }

    [Fact]
    public void NestedModel_InAClosedGenericType_IsGenerated()
    {
        const string source =
            """
            using Sharp.Validator;

            namespace Demo;

            public class Outer<T>
            {
                public class Inner
                {
                    public string? Name { get; set; }
                }
            }

            public class InnerValidator : Validator<Outer<int>.Inner>
            {
                public InnerValidator() => Builder.Property(x => x.Name).NotEmpty();
            }
            """;

        var (output, generated) = GeneratorTestHost.RunGenerator(source);
        GeneratorTestHost.AssertCompiles(output);
        Assert.Contains("RegisterProperty<global::Demo.Outer<int>.Inner, string>", generated);
    }

    [Fact]
    // Regression: both assemblies generated an internal Sharp.Validator.Generated.GeneratedValidatorRegistrar, so a
    // project that sees the other's internals got CS0436 from generated code, which fails -warnaserror builds. That is
    // the normal app + test project setup.
    public void GeneratedRegistrars_DoNotConflictAcrossInternalsVisibleTo()
    {
        const string library =
            """
            using Sharp.Validator;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]

            namespace Library;

            public class LibraryModel
            {
                public string? Name { get; set; }
            }

            public class LibraryValidator : Validator<LibraryModel>
            {
                public LibraryValidator() => Builder.Property(x => x.Name).NotEmpty();
            }
            """;

        const string consumer =
            """
            using Sharp.Validator;

            namespace Consumer;

            public class ConsumerModel
            {
                public string? Name { get; set; }
            }

            public class ConsumerValidator : Validator<ConsumerModel>
            {
                public ConsumerValidator() => Builder.Property(x => x.Name).NotEmpty();
            }
            """;

        var (libraryOutput, _) = GeneratorTestHost.RunGenerator(library);
        using var image = new MemoryStream();
        Assert.True(libraryOutput.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);

        var compilation = GeneratorTestHost.Compile(consumer)
            .WithAssemblyName("Consumer")
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));

        CSharpGeneratorDriver.Create(new ValidatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(output.GetDiagnostics(TestContext.Current.CancellationToken), x => x.Id == "CS0436");
    }
}