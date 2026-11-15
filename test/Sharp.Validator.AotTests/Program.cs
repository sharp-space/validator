using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator;
using Sharp.Validator.Bindings;
using Sharp.Validator.NoGeneratorFixtures;
using Sharp.Validator.NoValidatorFixtures;
using Sharp.Validator.Result;

// Native AOT canary (dependency-free so the strict TrimMode=full + warn-as-error publish stays clean): exercises the
// Expression.Compile accessors (simple, nested null-safe, cross-property, Elements), the enum path, async, dotted-path
// naming, the trim-annotated explicit DI registration, and the source-generated AddValidators path.

var fail = 0;

var validator = new Validator<Order>(builder =>
{
    builder.Property(x => x.Name).NotEmpty(); // simple accessor (Expression.Compile)
    builder.Property(x => x.Policy!.Max).GreaterThanOrEqual(x => x.Policy!.Min); // nested null-safe + ValueTuple accessor
    builder.Elements(x => x.Tags).NotEmpty(); // elements null-safe accessor
    builder.Elements(x => x.SelectValues).Property(x => x!.Label).NotEmpty(); // element child property accessor
    builder.Elements(x => x.SelectValues).Property(x => x!.Detail!.@event).MaximumLength(64); // nested keyword element accessor
    builder.Property(x => x.Day).InEnum(); // enum (GetValuesAsUnderlyingType)
    builder.Property(x => x.@event).MaximumLength(64); // keyword identifier — generated code must preserve the escape
});

Check(validator.Validate(
        new Order
        {
            Name = "a",
            Policy = new() { Max = 10, Min = 5 },
            Tags = ["x"],
            SelectValues = [new() { Label = "x" }],
            Day = DayOfWeek.Monday
        })
    .IsValid, "all-valid");

Check(!validator.Validate(
        new Order
        {
            Name = "a",
            Policy = new() { Max = 5, Min = 10 },
            Tags = ["x"],
            SelectValues = [new() { Label = "x" }],
            Day = DayOfWeek.Monday
        })
    .IsValid, "Max<Min invalid");

Check(validator.Validate(
    new Order
    {
        Name = "a",
        Policy = null,
        Tags = ["x"],
        SelectValues = [new() { Label = "x" }],
        Day = DayOfWeek.Monday
    }).IsValid, "nested null intermediate -> skip");

Check(!validator.Validate(
        new Order
        {
            Name = "a",
            Policy = new() { Max = 10, Min = 5 },
            Tags = ["x", ""],
            SelectValues = [new() { Label = "x" }],
            Day = DayOfWeek.Monday
        })
    .IsValid, "empty Tag element invalid");

Check(!validator.Validate(
        new Order
        {
            Name = "a",
            Policy = new() { Max = 10, Min = 5 },
            Tags = ["x"],
            SelectValues = [new() { Label = "" }],
            Day = DayOfWeek.Monday
        })
    .IsValid, "empty SelectValue.Label invalid");

Check(!validator.Validate(
        new Order
        {
            Name = "a",
            Policy = new() { Max = 10, Min = 5 },
            Tags = ["x"],
            SelectValues = [new() { Label = "x" }],
            Day = (DayOfWeek)99
        })
    .IsValid, "undefined enum invalid");

// dotted-path naming: Configure before the first Validate (PropertyName caches on first use).
var named = new Validator<Order>(x => x
    .Property(order => order.Policy!.Max).GreaterThanOrEqual(order => order.Policy!.Min));

named.Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.CamelCase);

Check(named.Validate(
        new Order
        {
            Policy = new() { Max = 5, Min = 10 }
        }).Errors.Any(x => x.PropertyName == "policy.max"),
    "dotted-path name = policy.max");

// cross-property targets: a member chain reads through a generated null-safe accessor and names itself, while a
// computed one is compiled at registration and runs in the expression interpreter under AOT. Both must still judge
// correctly. The computed one multiplies ints: an expression applying decimal's operator can't be built once
// trimming has removed it. SVL011 warns about the computed target in this AOT project; the canary keeps it on
// purpose to check that path.
var crossProperty = new Validator<Order>(x =>
{
#pragma warning disable SVL011
    x.Property(order => order.Quantity).GreaterThan(order => order.Minimum * 2);
#pragma warning restore SVL011
    x.Property(order => order.Name).Equal(order => order.@event, options => options.ErrorMessage = "{TargetPropertyName}");
});

Check(crossProperty.Validate(
        new Order { Name = "a", @event = "a", Quantity = 11, Minimum = 5 }).IsValid,
    "computed cross-property target valid");

Check(!crossProperty.Validate(
        new Order { Name = "a", @event = "a", Quantity = 10, Minimum = 5 }).IsValid,
    "computed cross-property target invalid");

Check(crossProperty.Validate(
        new Order { Name = "a", @event = "b", Quantity = 11, Minimum = 5 }).Errors
    .Any(x => x.Message == "event"), "Equal names its member target");

Check(AccessorRegistry.HasNullSafe<Order, string>("event"), "generated null-safe accessor registered: Equal target Order.event");

// an int member compared with a long? or decimal? property: C# reads it through two conversions, and the runtime takes
// the generated null-safe accessor for its path rather than interpreting a compiled one.
var widened = new Validator<Ledger>(x =>
{
    x.Property(ledger => ledger.Balance).GreaterThanOrEqual(ledger => ledger.Limits!.Floor);
    x.Property(ledger => ledger.Total).Equal(ledger => ledger.Limits!.Units);
});

Check(widened.Validate(new Ledger { Balance = 1, Total = 1 }).IsValid,
    "widened targets behind a null member skip the checks");

Check(widened.Validate(new Ledger { Balance = 5, Total = 3, Limits = new Limits { Floor = 2, Units = 3 } }).IsValid,
    "widened targets valid");

Check(widened.Validate(new Ledger { Balance = 1, Total = 4, Limits = new Limits { Floor = 2, Units = 3 } }).Errors
        .Select(x => x.Message).SequenceEqual(["'Balance' must be greater than or equal to '2'.", "'Total' must be equal to 'Limits.Units'."]),
    "widened targets invalid, Equal's named in its message");

Check(AccessorRegistry.HasNullSafe<Ledger, long?>("Limits.Floor") && AccessorRegistry.HasNullSafe<Ledger, decimal?>("Limits.Units"),
    "generated null-safe accessors registered: widened targets");

// a range read from the model: both bounds read through generated null-safe accessors, and decimal compares without
// building an expression, so trimming can't break it.
var priceRange = new Validator<Order>(x => x.Property(order => order.Price)
    .InclusiveBetween(order => order.Policy!.Floor, order => order.Policy!.Ceiling));

Check(priceRange.Validate(new Order { Price = 5, Policy = new Policy { Floor = 1, Ceiling = 10 } }).IsValid,
    "range read from the model valid");

Check(priceRange.Validate(new Order { Price = 11, Policy = new Policy { Floor = 1, Ceiling = 10 } }).Errors
    .Any(x => x.Message == "'Price' must be between 1 and 10."), "range read from the model invalid, its bounds in the message");

Check(priceRange.Validate(new Order { Price = 11 }).IsValid, "range bound behind a null member skips the check");

Check(AccessorRegistry.HasNullSafe<Order, decimal>("Policy.Floor") && AccessorRegistry.HasNullSafe<Order, decimal>("Policy.Ceiling"),
    "generated null-safe accessors registered: both range bounds");

// default messages by error code and display names, both settled when a validator freezes
var displayNamed = new Validator<Customer>(x => x
    .Property(customer => customer.Email, options => options.DisplayName = "E-mail").NotEmpty());

displayNamed.Configure(options => options.ErrorMessages["NotEmpty"] = "{PropertyName} is required.");

Check(displayNamed.Validate(new Customer()).Errors.Single() is { Message: "E-mail is required.", PropertyName: "Email" },
    "message by error code with a display name");

var displayResolved = new Validator<Customer>(x => x
    .Property(customer => customer.Email).NotEmpty());

displayResolved.Configure(options => options.DisplayNameResolver = member => member.Name.ToUpperInvariant());

Check(displayResolved.Validate(new Customer()).Errors.Single().Message == "'EMAIL' must not be empty.",
    "display-name resolver reads the member");

// async accessor path (interpreted under AOT)
var asyncValidator = new Validator<Order>(x => x
    .Property(order => order.Name).MustAsync(name => Task.FromResult(!string.IsNullOrEmpty(name))));

Check((await asyncValidator.ValidateAsync(new Order { Name = "ok" })).IsValid, "async valid");
Check(!(await asyncValidator.ValidateAsync(new Order { Name = "" })).IsValid, "async invalid");

// explicit DI registration — the trim/AOT-safe registration surface (DynamicallyAccessedMembers-annotated).
var services = new ServiceCollection();
services.AddValidator<OrderNameValidator>();
await using var provider = services.BuildServiceProvider();

Check(provider.GetService<IValidator<Order>>() is not null, "AddValidator<T> resolves under NativeAOT");
Check(ReferenceEquals(provider.GetService<OrderNameValidator>(), provider.GetService<IValidator<Order>>()),
    "AddValidator<T> registers the validator type, sharing its instance");

// source-generated registration path — AddValidators(assembly) auto-branches to the generated registry under AOT. A
// missing registrar throws under AOT, so reaching these asserts already proves the generated registrar ran.
var scanned = new ServiceCollection();
scanned.AddValidators(Assembly.GetExecutingAssembly());
await using var scannedProvider = scanned.BuildServiceProvider();

Check(scannedProvider.GetService<IValidator<Order>>() is not null,
    "AddValidators -> generated path resolves IValidator<Order>");

Check(scannedProvider.GetService<IValidator<Customer>>() is not null,
    "AddValidators -> generated path discovers >1 validator");

Check(ReferenceEquals(scannedProvider.GetService<CustomerValidator>(), scannedProvider.GetService<IValidator<Customer>>()),
    "AddValidators -> generated path registers the validator type, sharing its instance");

var configuredScan = new ServiceCollection();
configuredScan.AddValidators(Assembly.GetExecutingAssembly(), options => options.PropertyNamePrefix = "AOT_");
await using var configuredProvider = configuredScan.BuildServiceProvider();
var configuredCustomer = configuredProvider.GetRequiredService<IValidator<Customer>>();

Check(configuredCustomer.Validate(new Customer()).Errors.Any(x => x.PropertyName == "AOT_Email"),
    "AddValidators -> generated path applies scan-local defaults");

// The source-generated flat accessors must be registered under the exact path key the runtime looks up.
// A key mismatch would silently fall back to Expression.Compile (losing the win) with no error — assert against it.
Check(AccessorRegistry.HasProperty<Order, string>("Name"), "generated flat accessor registered: Order.Name");
Check(AccessorRegistry.HasProperty<Order, string>("event"), "generated accessor registered: Order.event");
Check(AccessorRegistry.HasProperty<Customer, string>("Email"), "generated flat accessor registered: Customer.Email");
Check(AccessorRegistry.HasNullSafe<Order, decimal>("Policy.Max"), "generated null-safe accessor registered: Order.Policy.Max");
Check(AccessorRegistry.HasNullSafe<Order, decimal>("Policy.Min"), "generated cross-property accessor registered: Order.Policy.Min");
Check(AccessorRegistry.HasElements<Order, string>("Tags"), "generated elements accessor registered: Order.Tags");
Check(AccessorRegistry.HasElements<Order, SelectValue>("SelectValues"), "generated elements accessor registered: Order.SelectValues");
Check(AccessorRegistry.HasProperty<SelectValue, string>("Label"), "generated element property accessor registered: SelectValue.Label");
Check(AccessorRegistry.HasNullSafe<SelectValue, string>("Detail.event"),
    "generated nested keyword element accessor registered: SelectValue.Detail.event");

// A getter only code inside the model can call: the generator skips the selector instead of emitting an accessor that
// breaks this build with CS0271, and the compiled fallback, interpreted under AOT, still judges the value.
var privateGetter = new SecretHolder.SecretHolderValidator();
Check(!privateGetter.Validate(new SecretHolder { Secret = "" }).IsValid,
    "private getter: empty value fails through the fallback");

Check(privateGetter.Validate(new SecretHolder { Secret = "x" }).IsValid,
    "private getter: set value passes through the fallback");

Check(!AccessorRegistry.HasProperty<SecretHolder, string>("Secret"),
    "no generated accessor for a private getter");

Check(privateGetter.Validate(new SecretHolder { Secret = "x", Vault = new Vault { Code = "" } }).Errors
        .Any(x => x.PropertyName == "Vault.Code"),
    "fallback null check treats a member as present whatever its == answers");

Check(!AccessorRegistry.HasNullSafe<SecretHolder, string>("Vault.Code"),
    "no generated accessor through a private getter");

// Negative canary: an assembly built without the generator must fail loudly under AOT — a silent reflection scan over
// trimmed types would under-register. Also pins the guard's actionable guidance message.
if (!RuntimeFeature.IsDynamicCodeSupported)
{
    var noRegistrar = new ServiceCollection();

    try
    {
        noRegistrar.AddValidators(typeof(NoRegistrarValidator).Assembly);
        Check(false, "no-registrar scan must throw under NativeAOT");
    }
    catch (InvalidOperationException exception)
    {
        Check(exception.Message.Contains("No source-generated validator registrations", StringComparison.Ordinal),
            "no-registrar guard throws with actionable guidance");
    }
}
else Console.WriteLine("skip: no-registrar guard (JIT run — the reflection fallback handles this case)");

// An assembly built with the generator but declaring no validator gets an empty registrar, so its scan registers
// nothing. Before the generator emitted one, AOT took such an assembly for one built without the generator and threw
// the guard above, telling the caller to reference the analyzer it already referenced.
var noValidator = new ServiceCollection();
try
{
    noValidator.AddValidators(typeof(NoValidatorModel).Assembly);
    Check(noValidator.Count == 0, "an assembly without validators scans to no registrations");
}
catch (InvalidOperationException exception)
{
    Check(false, $"an assembly without validators must scan without throwing: {exception.Message}");
}

// [GeneratedRegex] + the Regex(Regex) overload — the shape the SVL010 code fix produces — must compile and run under
// Native AOT (proves our generator and the .NET Regex generator coexist, and the analyzer ignores this form).
var regexCanary = new RegexCanary();
Check(regexCanary.Validate(new Order { Name = "abc" }).IsValid, "GeneratedRegex rule valid (SVL010 fix target)");
Check(!regexCanary.Validate(new Order { Name = "abc1" }).IsValid, "GeneratedRegex rule invalid (SVL010 fix target)");

// The generator keys accessors by member path, so a computing selector must not reuse its operand's accessor.
var activeFlag = new ActiveFlagValidator().Validate(new ActiveFlag { Active = true });
Check(activeFlag.Errors is [{ Code: "Negated" }],
    "computing selector ignores the generated accessor of its operand");

// A nullable member and its Value read as the same nullable type share a registry key. The generator registered both,
// so the first served the other: read through the Value's accessor, a null age skipped NotNull. The Value read comes
// first here, as a comparison target, so the plain read would have taken its accessor.
var nullableAge = new Validator<Visit>(x =>
{
    x.Property(visit => visit.Retirement).GreaterThan(visit => visit.Patient.Age!.Value);
    x.Property(visit => visit.Patient.Age).NotNull();
});

Check(nullableAge.Validate(new Visit { Retirement = 70 }).Errors is [{ PropertyName: "Patient.Age" }],
    "NotNull reports a null nullable member beside a comparison read of its Value");

// A rule of the app's that sets no code reports its type's name, which the runtime must still know after trimming.
var bareRule = new Validator<Order>(x => x.Property(order => order.Name).Add(new BareRule<Order>()));
Check(bareRule.Validate(new Order { Name = "a" }).Errors is [{ Code: "BareRule", Message: "'Name' is not valid." }],
    "a rule that sets no code or message reports its type's name and the default message");

var fileLocalRule = new Validator<Order>(x => x.Property(order => order.Name).Add(new FileLocalRule<Order>()));
Check(fileLocalRule.Validate(new Order { Name = "a" }).Errors is [{ Code: "FileLocalRule" }],
    "a file-local rule's code is its declared name, without the file and path hash");

// A chain has its property's declared type, so a non-nullable string chain takes the string rules through the cast in
// the string rules' Add, a string? rule through the variant Add, and an object? rule behind the wider-rule adapter.
var stringChain = new Validator<Ticket>(x =>
{
    x.Property(ticket => ticket.Title).MaximumLength(3).Must(title => title.Length > 0, o => o.ErrorCode = "Empty");
    x.Property(ticket => ticket.Title).Add(new BareRule<Ticket>(), o => o.ErrorCode = "Bare");
    x.Property(ticket => ticket.Title).Add(new FilledRule<Ticket>());
    x.Elements(ticket => ticket.Labels).MinimumLength(2);
});
Check(stringChain.Validate(new Ticket { Title = "abcd", Labels = ["a"] }).Errors.Select(error => error.Code)
        .SequenceEqual([ErrorCodes.MaximumLength, "Bare", ErrorCodes.MinimumLength]),
    "string chain runs string rules, a string? rule, and an object? rule");
Check(stringChain.Validate(new Ticket()).Errors.Select(error => error.Code).SequenceEqual(["Empty", "Bare", "FilledRule"]),
    "object? rule on a string chain reports its own type name");
Check(AccessorRegistry.HasProperty<Ticket, string>("Title") && AccessorRegistry.HasElements<Ticket, string>("Labels"),
    "generated accessors registered for string chains: Ticket.Title, Ticket.Labels");

// ValidationError is serialized as a response body; under trimming the severity converter must survive and still write
// the member name rather than a number.
var errorJson = JsonSerializer.Serialize(new ValidationError("Name", "required", "NotEmpty"), CanaryJsonContext.Default.ValidationError);
Check(errorJson.Contains("\"severity\":\"Error\"", StringComparison.Ordinal),
    "ValidationError severity serializes by name under NativeAOT");

var readError = JsonSerializer.Deserialize(errorJson, CanaryJsonContext.Default.ValidationError);
Check(readError is { PropertyName: "Name", Code: "NotEmpty", Severity: ValidationErrorSeverity.Error },
    "ValidationError round-trips through JSON under NativeAOT");

// Rough timing of the flat Validate hot path. Under Native AOT the generated delegate (this build) should be materially
// faster than the expression interpreter used when the analyzer is absent. Informational, not pass/fail.
var flat = new Validator<Customer>(x => x.Property(customer => customer.Email).NotEmpty());
var sample = new Customer { Email = "a@b.com" };
for (var index = 0; index < 10_000; index++) flat.Validate(sample);

const int iterations = 2_000_000;

var stopwatch = Stopwatch.StartNew();
for (var index = 0; index < iterations; index++) flat.Validate(sample);
stopwatch.Stop();

Console.WriteLine($"flat Validate: {stopwatch.Elapsed.TotalNanoseconds / iterations:F1} ns/op (N={iterations})");

// Nested null-safe accessor hot path (generated block vs. the interpreter when the analyzer is absent).
var nested = new Validator<Order>(x => x.Property(order => order.Policy!.Max).GreaterThanOrEqual(0m));
var nestedSample = new Order { Policy = new() { Max = 10 } };
for (var index = 0; index < 10_000; index++) nested.Validate(nestedSample);

var nestedStopwatch = Stopwatch.StartNew();
for (var index = 0; index < iterations; index++) nested.Validate(nestedSample);
nestedStopwatch.Stop();

Console.WriteLine($"nested Validate: {nestedStopwatch.Elapsed.TotalNanoseconds / iterations:F1} ns/op (N={iterations})");
Console.WriteLine(fail == 0 ? "ALL PASS" : $"{fail} FAILED");

return fail == 0 ? 0 : 1;

void Check(bool ok, string label)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")}: {label}");
    if (!ok) fail++;
}

internal sealed class Patient
{
    public int? Age { get; init; }
}

internal sealed class Visit
{
    public Patient Patient { get; } = new();

    public int? Retirement { get; init; }
}

internal sealed class Policy
{
    public decimal Max { get; init; }

    public decimal Min { get; init; }

    public decimal Floor { get; init; }

    public decimal Ceiling { get; init; }
}

internal sealed class Order
{
    public string? Name { get; init; }

    public Policy? Policy { get; init; }

    public List<string?>? Tags { get; init; }

    public List<SelectValue?>? SelectValues { get; init; }

    public DayOfWeek Day { get; init; }

    public int Quantity { get; init; }

    public int Minimum { get; init; }

    public decimal Price { get; init; }

    // Reserved-keyword identifier: rules over this property prove the generated accessor escapes it.
    // ReSharper disable once InconsistentNaming
    public string? @event { get; init; }
}

internal sealed class Limits
{
    public int Floor { get; init; }

    public int Units { get; init; }
}

internal sealed class Ledger
{
    public long? Balance { get; init; }

    public decimal? Total { get; init; }

    public Limits? Limits { get; init; }
}

internal sealed class SelectValue
{
    public string? Label { get; init; }

    public SelectValueDetail? Detail { get; init; }
}

internal sealed class SelectValueDetail
{
    // ReSharper disable once InconsistentNaming — a reserved-keyword identifier, as on Order.
    public string? @event { get; init; }
}

internal sealed class OrderNameValidator() : Validator<Order>(x => x.Property(order => order.Name).NotEmpty());

internal sealed class SecretHolder
{
    public string? Secret { private get; init; }

    // The private getter sends this chain to the null-safe fallback too, where Vault's own == must not decide whether
    // the member is there.
    public Vault? Vault { private get; init; }

    internal sealed class SecretHolderValidator : Validator<SecretHolder>
    {
        public SecretHolderValidator()
        {
            Builder.Property(x => x.Secret).NotEmpty();
            Builder.Property(x => x.Vault!.Code).NotEmpty();
        }
    }
}

internal sealed class Vault
{
    public string? Code { get; init; }

    public override bool Equals(object? obj) => obj is Vault;

    public override int GetHashCode() => 0;

    // The operators ignore their operands: an accessor that asked them whether Vault is there would get a wrong answer.
    // ReSharper disable UnusedParameter.Global
    public static bool operator ==(Vault? left, Vault? right) => true;

    public static bool operator !=(Vault? left, Vault? right) => false;
    // ReSharper restore UnusedParameter.Global
}

internal sealed class Customer
{
    public string? Email { get; init; }
}

internal sealed class CustomerValidator() : Validator<Customer>(x => x.Property(customer => customer.Email).NotEmpty());

internal sealed class ActiveFlag
{
    public bool Active { get; init; }
}

internal sealed class ActiveFlagValidator : Validator<ActiveFlag>
{
    public ActiveFlagValidator()
    {
        Builder.Property(x => x.Active).Equal(true);
        Builder.Property(x => !x.Active).Equal(true, x => x.ErrorCode = "Negated");
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ValidationError))]
internal sealed partial class CanaryJsonContext : JsonSerializerContext;

internal sealed class BareRule<TModel> : PropertyValidator<TModel, string?>
{
    public override bool Validate(string? value, ValidationContext<TModel> context) => false;
}

internal sealed class FilledRule<TModel> : PropertyValidator<TModel, object?>
{
    public override bool Validate(object? value, ValidationContext<TModel> context) => value is not (null or "");
}

internal sealed class Ticket
{
    public string Title { get; init; } = "";

    public List<string> Labels { get; init; } = [];
}

file sealed class FileLocalRule<TModel> : PropertyValidator<TModel, string?>
{
    public override bool Validate(string? value, ValidationContext<TModel> context) => false;
}

internal sealed partial class RegexCanary : Validator<Order>
{
    public RegexCanary() => Builder.Property(x => x.Name).Regex(NamePattern());

    [GeneratedRegex("^[a-z]+$")]
    private static partial Regex NamePattern();
}