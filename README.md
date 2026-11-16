# Sharp.Validator

A validator for .NET built for speed and Native AOT, with first-class fluent rules, async validation, and DI
integration.

```csharp
public class UserValidator : Validator<User>
{
    public UserValidator()
    {
        Builder.Property(x => x.Email).NotEmpty().Email();
        Builder.Property(x => x.Age).InclusiveBetween(13, 120);
        Builder.Elements(x => x.Tags).NotEmpty().MaximumLength(32);
        Builder.Elements(x => x.Addresses).Property(x => x.City).NotEmpty();
        Builder.Property(x => x.Password).NotEmpty().Password(options =>
        {
            options.RequiredLength = 12;
            options.RequiredUppercaseLetters = 1;
        });
    }
}
```

## Features

- **Rules in plain C#.** A validator declares its rules in its constructor as chains on its properties, with built-in
  rules for null and empty values, equality, comparisons and ranges, lengths, formats, dates, and decimals.
- **Async rules and conditions.** `MustAsync` and async `Where` conditions run in `ValidateAsync`, and every condition
  runs at most once per validation, so a condition that queries a database costs one call.
- **Collections and child validators.** `Elements` validates each item, its `Property` a property of each item, and
  `Include` runs another validator, with errors reported under paths such as `Addresses[0].City`.
- **Messages under your control.** Placeholders, default messages by error code for app-wide wording or another
  language, display names, and naming policies that match `System.Text.Json`.
- **Results that fit ASP.NET Core.** `ToDictionary()` returns the shape `Results.ValidationProblem` takes, and warnings
  and information entries are reported without failing `IsValid`.
- **Native AOT- and trim-safe.** A bundled source generator registers validators for dependency injection and
  precompiles property accessors at build time, and analyzers flag code that Native AOT would run interpreted.
- **Allocation-aware.** A valid model allocates no error list and nothing per rule or per element, and a literal regex
  pattern scales across cores.
- **Works with [Sharp.Mediator](https://github.com/sharp-space/mediator).** `Sharp.Mediator.Validation` runs a message's
  validators before its handler, and can return the failures as a response instead of throwing.

## Packages

| Package | Description |
| --- | --- |
| [`Sharp.Validator`](https://www.nuget.org/packages/Sharp.Validator) | The validation engine, built-in rules, and DI integration, with the source generator and analyzers bundled in |
| [`Sharp.Validator.Result`](https://www.nuget.org/packages/Sharp.Validator.Result) | Dependency-free result types: `ValidationResult`, `ValidationError`, and `ValidationException` |

A project that defines validators references `Sharp.Validator`, which brings `Sharp.Validator.Result` with it.
Reference `Sharp.Validator.Result` alone from layers that only consume results.

## Install

```bash
dotnet add package Sharp.Validator
```

Requires .NET 10 or later.
Package versions follow Semantic Versioning independently of .NET releases, starting at 1.0.0. Patch releases fix
bugs; minor releases add backward-compatible features; major releases introduce breaking changes or raise minimum
SDK, compiler, or runtime requirements. All packages in this repository share a version; Validator, Mediator, and
Messenger release independently. The minimum supported runtime remains .NET 10. Changes are listed in the
[release notes](https://github.com/sharp-space/validator/releases).

## Quick start

Define a validator for a model, then validate an instance.

```csharp
using Sharp.Validator;

var validator = new OrderValidator();
var result = validator.Validate(new Order("A-1001", 0m, "not-an-email"));

if (!result.IsValid)
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.PropertyName}: {error.Message}");

public record Order(string Id, decimal Amount, string Email);

public class OrderValidator : Validator<Order>
{
    public OrderValidator()
    {
        Builder.Property(x => x.Id).NotEmpty();
        Builder.Property(x => x.Amount).GreaterThan(0);
        Builder.Property(x => x.Email).NotEmpty().Email();
    }
}
```

The program prints:

```text
Amount: 'Amount' must be greater than '0'.
Email: 'Email' is not a valid email address.
```

A validator keeps no per-call state, so create it once and share it, across threads too. [Results](#results)
describes what `Validate` returns, and [Dependency injection](#dependency-injection) how to register validators.

Validators, rules, and options are in the `Sharp.Validator` namespace. The result types, `ValidationResult`,
`ValidationError`, `ValidationErrorSeverity`, and `ValidationException`, are in `Sharp.Validator.Result`, and rule
classes such as `EqualValidator<TModel, TProperty>` in `Sharp.Validator.Validators`.

## Inline validators

A validator can also be built from a lambda instead of a class of its own.

### Define a validator without a class

```csharp
var validator = new Validator<Order>(x =>
{
    x.Property(order => order.Id).NotEmpty();
    x.Property(order => order.Amount).GreaterThan(0);
});
```

An inline validator takes the same rules as a class, which suits tests and checks used in one place. `AddValidators`
registers only validator classes and builds them with constructor injection; register an inline validator as an
instance with `AddValidator(validator)`.

## Built-in validators

Rules are extension methods on a chain, so they read left to right and stack. Most rules skip `null`, and the format
rules (`Email`, `CreditCard`, `PhoneNumber`, `Regex`, `Url`, `Uri`, and `InEnum` on a string) skip an empty string as
well, so combine them with `NotNull()` or `NotEmpty()` to require a value.

A chain has its property's declared type: a `string Name` gives a `string` chain and a `string? Nickname` a `string?`
chain, so `Must(name => name.Length > 2)` compiles without a nullable warning on the first and warns on the second,
as it should. The string rules apply to both.

The declared type is what the compiler checks, not what the value holds at run time. A non-nullable `string` that is
`null` anyway, such as one a deserializer left unset, reaches `Must` as `null`, so `name.Length` throws
`NullReferenceException`; the built-in rules handle the `null` as usual. Where that can happen, put `NotNull()` first
and set the chain's `CascadeMode` to `Stop`, so the predicate doesn't run on it (see
[Set defaults for one property's rules](#set-defaults-for-one-propertys-rules)).

> **Note:** `Equal` and `NotEqual` follow equality instead. `null` equals `null`, so `Equal` passes and `NotEqual`
> fails, and `null` against a value fails `Equal`, so a confirmation field left empty can't pass. A comparer you pass
> decides for `null` too, and `NotEqual` always fails where `Equal` with the same target and comparer passes. `Must` and
> `MustAsync` hand the predicate every value, `null` included, so the predicate decides what `null` means.

### Common rules

- `Null()`, `NotNull()`
- `Empty()`, `NotEmpty()`
- `Equal(value)` and `Equal(x => x.Other)`, `NotEqual(value)` and `NotEqual(x => x.Other)`, each also with an
  `IEqualityComparer<TProperty>`: `Equal(value, comparer)`
- `InEnum()` for an enum or nullable enum property, where any other property type doesn't compile, and
  `InEnum(typeof(TEnum))` or `InEnum(typeof(TEnum), StringComparison)` for a `string` property
- `Must(value => ...)` and `Must((value, context) => ...)`, `MustAsync(value => ...)`,
  `MustAsync((value, cancellationToken) => ...)`, and `MustAsync((value, context, cancellationToken) => ...)`, described
  in [Custom rules](#custom-rules)

### Comparisons and ranges

```csharp
Builder.Property(x => x.Quantity).GreaterThan(0);
Builder.Property(x => x.End).GreaterThanOrEqual(x => x.Start);
Builder.Property(x => x.Discount).InclusiveBetween(0m, 0.5m);
Builder.Property(x => x.Price).InclusiveBetween(x => x.MinimumPrice, x => x.MaximumPrice);
```

- `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, and `LessThanOrEqual` take a value or an expression that reads the
  target from the model. A fixed value or bound can't be `null`: the rule throws when it is declared, and the compiler
  warns where a value that may be `null` is passed.
- `InclusiveBetween(from, to)` and `ExclusiveBetween(from, to)` take two values or two expressions. Values with `to`
  below `from` throw when the rule is declared, and so do equal values for `ExclusiveBetween`, because no value fits.
  Expression bounds can't be checked up front, so with them such a range admits no value.
- Each also takes an `IComparer<TProperty>` before its `configure`, as in `GreaterThan(0, comparer)`. Strings compare
  ordinally by default, so the result doesn't depend on the culture.

A target expression reads another property (`x => x.Password`), a nested one (`x => x.Policy.Min`), or a computed value
(`x => x.Minimum * 2`). A `null` member on the way to a target or a bound skips the check, and so does a `null`
target or bound, so combine the rule with `NotNull()` to require the value. For one fixed and one model bound, pair
`GreaterThanOrEqual(0)` with `LessThanOrEqual(x => x.Maximum)`.

Write a target or bound as a lambda. It may be nullable where the chain's type is not, such as a `string?` property
compared with a `string` one, and `Equal` and `NotEqual` take a nullable value too, so neither needs a `!`. `Equal` and
`NotEqual` judge a `null` target by equality, as the note under [Built-in validators](#built-in-validators) says. A
chain of a value type takes no nullable target: an `int` chain doesn't take an `int?` property, so start the chain at
the nullable one instead.

> **Note:** An `Expression<Func<TModel, T>>` variable passed instead of a lambda can raise CS8620 on a chain of a
> reference type, even where its annotation matches the chain's: `Expression<T>` has no variance, and type inference
> drops the `?`. On a `string` chain, declare the variable `Expression<Func<TModel, string?>>`; on a `string?` chain,
> write `chain!.Equal(target)`.

A target that is a member chain of the model is named in messages as `{TargetPropertyName}` and is precompiled for
Native AOT, including one that C# reads through a widening into a nullable type, such as an `int` member compared with
a `long?` or `decimal?` property. A target that starts at a captured or static value, such as `x => settings.Max`, is
read null-safely too, but it isn't precompiled: like a computed target, it is compiled for each validator instance. It
isn't another property, so messages print its value, and a computed target has no name either.

> **Note:** Under Native AOT a computed target runs in the expression interpreter, measured at about four times the
> time and some 200 more bytes per validation, and one that applies an operator the runtime looks up by reflection,
> such as `*` on `decimal`, can't be built at all once trimming has removed the operator. In an AOT app, keep the target
> a member chain, or check a computed value with `Must((value, context) => ...)` or
> `Add(new EqualValidator<Order, decimal>(x => x.Price * x.Quantity))`, which the comparison and range rules accept as
> well, such as `Add(new GreaterThanValidator<Order, decimal>(x => x.Minimum * 2))`. Trimmed and AOT projects get
> [SVL011](https://github.com/sharp-space/validator/blob/main/doc/diagnostics/SVL011.md) on a computed target.

### Strings

- `Length(min, max)`, `Length(exact)`, `MinimumLength(min)`, `MaximumLength(max)`, each also with a
  `Func<TModel, int>` read from the model at validation time, as in `MaximumLength(x => x.MaxNameLength)`
- `Regex(pattern)`, `Regex(pattern, options)`, `Regex(regex)`, and `Regex(x => pattern)` for a pattern the model
  supplies. A pattern given as a string gets a 2-second match timeout unless you pass another, and a timeout counts as
  a validation failure, not an exception, so hostile input that would backtrack forever is reported as invalid. Such a
  pattern ignores case in the invariant culture, like a `[GeneratedRegex]`, so `IgnoreCase` and `(?i)` judge the same
  on every server; pass a `Regex` built in another culture to match in that one. An invalid pattern from the model,
  or a `null` pattern or `Regex`, fails validation.
- `Email()` checks the WHATWG HTML syntax, the same check browsers apply to `input[type=email]`. It is ASCII-only, so
  an internationalized domain must be written in punycode.
- `CreditCard()` runs the Luhn check over 12 to 19 ASCII digits, and allows spaces and dashes.
- `PhoneNumber()` is a lenient format hint (digits, separators, an optional extension), not a guarantee that the
  number exists. Use libphonenumber for that.
- `Url()` accepts absolute `http` and `https` URLs. `Uri()` and `Uri(UriKind)` accept any scheme, and
  `Uri(UriKind, schemes)` only the ones listed. Surrounding whitespace and control characters fail, and an absolute URI
  must write its scheme, so `/etc/passwd` and `C:\temp` fail on every OS.
- `Password()` bundles one rule per requirement, and `Password(requirements => ...)` sets them. Set a requirement to `0`
  to turn it off. The first callback is always the requirements, so `Password(_ => { }, x => x.ErrorMessage = "...")`
  changes the message alone. That message goes to every requirement's rule; to word each one, set `ErrorMessages` for
  its code, such as `ErrorCodes.PasswordDigits`.

| Option | Default | Description |
| --- | --- | --- |
| `RequiredLength` | 8 | Minimum length |
| `RequiredDigits` | 0 | Minimum number of digits |
| `RequiredNonAlphanumerics` | 0 | Minimum number of characters that are neither letters nor digits |
| `RequiredLowercaseLetters` | 0 | Minimum number of lowercase letters |
| `RequiredUppercaseLetters` | 0 | Minimum number of uppercase letters |
| `RequiredUniqueCharacters` | 0 | Minimum number of distinct characters |

By default only the length counts. NIST SP 800-63B advises judging a password by its length rather than by the kinds of
characters it mixes, because composition rules push people toward predictable patterns. Turn the character counts on
to match an existing policy, and raise the length for a password that is the only factor of a sign-in. Checking a
password against lists of breached ones, which the same guidance also asks for, is outside this library.

### Dates and times

`InPast()`, `InFuture()`, and `IsToday()` check `DateTime`, `DateTimeOffset`, `DateOnly`, and their nullable forms.
They read "now" from `ValidatorBuilderOptions.TimeProvider`, `TimeProvider.System` unless `Configure`, an
`AddValidators` scan, or `Global` sets one, so a test or an app fixes the clock once for every date rule of a
validator:

```csharp
public class BookingValidator : Validator<Booking>
{
    public BookingValidator(TimeProvider clock)
    {
        Configure(x => x.TimeProvider = clock);
        Builder.Property(x => x.Start).InFuture();
    }
}
```

A validator reads its clock when it [freezes](#when-a-validator-freezes), and an included validator reads its own, not
the parent's, so set the clock on the child too, or on `Global`.

- A `DateTime` compares with now in its own kind: a UTC value with UTC now, a Local or Unspecified one with local time.
- A `DateTimeOffset` compares as an instant, and `IsToday` reads its date in the value's own offset.
- A `DateOnly` compares with today's local date by calendar date, so today is neither in the past nor in the future.

> **Note:** An unset value (`default`, 0001-01-01) is in the past. Add `NotEmpty()` to reject it.

### Numbers

- `PrecisionScale(precision, scale)` and `PrecisionScale(precision, scale, ignoreTrailingZeros)` for `decimal` and
  `decimal?`

## Collections and child validators

`Elements` runs rules on every item of a collection, `Property` on an `Elements` chain runs them on a property of every
item, and `Include` runs another validator on a property's value.

### Validate every item of a collection

```csharp
var members = Builder.Elements(x => x.Members);
members.NotNull();                               // Members[i]
members.Property(x => x.Home.City).NotEmpty();   // Members[i].Home.City
```

`Elements(selector)` applies its rules to each item and reports `Items[i]`. `Property(...)` on an `Elements` chain
selects from the item, not the model; start a chain for another property of the model from `Builder`. A `Where` on the
`Elements` chain gates its `Property` chains too. Severity, code, and cascade defaults set on the collection apply only
to the rules on the items themselves, and options passed to an item's `Property` apply only to that chain.

The collection is read once per validation for the chain and its `Property` chains, so a lazy sequence is enumerated
once. When more than one chain walks a collection that isn't a list, that read copies it into an array, which each
validation allocates.

> **Note:** A rule must not add or remove items of the collection it validates. A `List<T>` that changes size while
> its items are validated throws `InvalidOperationException`, as the list's own enumerator does. Unlike the
> enumerator, the check compares sizes only, so a change that keeps the count, such as replacing an item, goes
> unnoticed.

### Validate a property with another validator

```csharp
Builder.Property(x => x.ShippingAddress).Include(new AddressValidator());
Builder.Elements(x => x.Addresses).Include(new AddressValidator());                      // Addresses[i].City
Builder.Elements(x => x.Members).Property(x => x.Home).Include(new AddressValidator());  // Members[i].Home.City
```

- `Include(validator)` runs a child validator on the property's value and reports its errors under the parent path,
  such as `ShippingAddress.City`. `Include(validator, x => x.IncludeParentPropertyName = false)` reports the child's
  names alone.
- A validator for a type derived from the property's runs only on values of that type and skips any other, so
  `Property(x => x.Pet).Include(new DogValidator())` checks dogs alone.

Including a child freezes it, so its rules and options can't change afterward. The parent and the child share
`ValidationContext.Parameters`; their errors, message state, and cascade state stay apart. A partial-validation
selection narrows into the child (see [Partial validation](#partial-validation)).

A child validator keeps its own naming policy, and the parent adds only its path and its own literal prefix and
suffix. Wrappers that every validator inherits from `ValidatorBuilderOptions.Global` or an `AddValidators` scan apply
once, at the root of the validation, while wrappers set through a validator's own `Configure` stay on its errors.

### Recursive models

`Include(this)` throws, because recursive includes aren't supported: a cycle in the object graph would end in a stack
overflow, which .NET can't catch. To validate a tree, compose a new validator per level down to a fixed depth:

```csharp
public class NodeValidator : Validator<Node>
{
    public NodeValidator(int depth = 8)
    {
        Builder.Property(x => x.Name).NotEmpty();
        if (depth > 0) Builder.Elements(x => x.Children).Include(new NodeValidator(depth - 1));
    }
}
```

## Async validation

A validator with an async rule, an async `Where` condition, or an async child validator runs through `ValidateAsync`.

### Check a value against a service

```csharp
public class UserValidator : Validator<User>
{
    public UserValidator(IUserRepository users)
    {
        Builder.Property(x => x.Email).NotEmpty().Email().MustAsync(
            async (email, cancellationToken) => !await users.ExistsAsync(email, cancellationToken),
            x => x.ErrorMessage = "Email already exists.");
    }
}

var result = await validator.ValidateAsync(user, cancellationToken);
```

The sync rules run first, then the async ones. `ValidateAsync` returns a `ValueTask`, which completes synchronously
when the validator has no async rule.

> **Note:** Calling `Validate` on a validator with an async rule throws `InvalidOperationException`. The sync
> `Validate` takes no `CancellationToken`; pass one to `ValidateAsync` when you need cancellation.

`ValidateAsync` passes its token to every async rule and condition. A canceled token stops the validation before any
rule runs, before each chain with async rules, and between the elements of a collection; a running rule stops only if
it observes the token itself. The cancellation, or an exception a rule throws, comes back in the returned `ValueTask`.
Only a `null` argument, or a configuration error found when the validator freezes, throws from the call itself.

### Reuse a context

`Validate(context)` and `ValidateAsync(context, cancellationToken)` validate the model a `ValidationContext<TModel>`
holds, so conditions and rules always see the same instance.
A context also holds the state of the validation running on it, its errors and parameters among them, so it runs one
validation at a time. A validation started on a context whose validation hasn't finished, such as one a rule starts on
the context it received, throws `InvalidOperationException`, which `ValidateAsync` returns in its `ValueTask`. The check
isn't a lock, so give each concurrent validation a context of its own.

## Custom rules

`Must` and `MustAsync` take an inline predicate. For a rule you reuse, write a rule type.

### Write an inline rule

```csharp
Builder.Property(x => x.DiscountCode).Must((code, context) =>
{
    if (context.Model.IsVip) return true;                              // context.Model is the whole instance
    context.ErrorMessage = $"'{code}' is reserved for VIP customers."; // applies to this failure only
    return false;
});
```

The overload with a `ValidationContext` gives the predicate the whole model and a message for this failure only.
`MustAsync((value, context, cancellationToken) => ...)` has the same form. Inside the predicate,
`context.MessageParameters.Add("Limit", "10")` feeds a `{Limit}` placeholder with a string, and `context.Parameters`, an
`IDictionary<string, object?>`, is free-form state for the call. Included child validators see the same `Parameters`
dictionary and may add values for later parent rules; all other context state stays apart.

A context belongs to the call that received it, a rule's, a condition's, or the configurator passed to `Validate` or
`ValidateAsync`. Use it during that call, and don't keep it, or the `Parameters` and `MessageParameters` it hands out,
once the call returns: Sharp.Validator may reuse a context it created from then on, as an `Include` over a collection
already reuses one child context for all its elements. To read a context after the validation, such as parameters a
rule set, create it yourself and pass it to `Validate(context)`.

### Write a reusable rule

```csharp
public class SlugValidator<TModel> : PropertyValidator<TModel, string?>
{
    public SlugValidator() => Configure(options =>
    {
        options.ErrorCode = "InvalidSlug";
        options.ErrorMessage = "'{PropertyName}' must contain only lowercase letters, digits, and hyphens.";
    });

    public override bool Validate(string? value, ValidationContext<TModel> context) =>
        value is null || value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}

Builder.Property(x => x.Slug).Add(new SlugValidator<Post>());
```

Derive from `PropertyValidator<TModel, TProperty>`, or `AsyncPropertyValidator<TModel, TProperty>` for an async rule,
and add it with `Add`. Set its default code and message with `Configure` in the constructor, as the built-in rules do,
so an `ErrorMessages` entry for that code replaces the message. A rule that sets no code reports the name its type is
declared with, such as `SlugValidator` for `SlugValidator<TModel>`, and one that sets no message reports
`'{PropertyName}' is not valid.`. The public rule classes in `Sharp.Validator.Validators` are sealed; to build on one,
call its `Validate` from your rule. The date, password, and string `InEnum` rules are internal and are reached only
through their extension methods.

> **Note:** A rule instance carries its own options, so give each chain its own instance. Adding one instance to a
> second chain throws `InvalidOperationException`.

`Add` takes a rule for the chain's type or a wider one, so a `string?` rule such as `SlugValidator` fits a `string`
chain too, and an `object?` rule fits any chain of a reference type. A chain of a value type, such as `int`, takes only
rules for that exact type: variance doesn't reach value types, so an `object?` rule doesn't compile there.

To give a string rule a method that reads like the built-in ones, write the extension over both string chains with
the constraint the built-in string rules use. `Add` takes the `string?` rule there too:

```csharp
public static class SlugRules
{
    extension<TModel, TString>(PropertyValidatorBuilder<TModel, TString> builder)
        where TString : class?, IEquatable<string>?
    {
        public PropertyValidatorBuilder<TModel, TString> Slug() => builder.Add(new SlugValidator<TModel>());
    }
}

Builder.Property(x => x.Slug).Slug();
```

Keep the constraint: without it, `Add` doesn't take a `string?` rule, and the method would show on every chain. An
extension over `PropertyValidatorBuilder<TModel, string?>` alone warns (CS8620) on a `string` chain. The constraint
also admits a type of your own that implements `IEquatable<string>`, which no string rule can check, so a string rule
added to such a chain throws `InvalidOperationException` when it is declared.

### Check across properties

A rule reports its errors under a property, so a selector has to name one. `Property(x => x)` and `Elements(x => x)`
throw when the rule is declared: rules on the model as a whole and a collection as the root model aren't supported. Put
a check across properties on the property its error belongs to, and read the others from `context.Model`:

```csharp
Builder.Property(x => x.End).Must((end, context) => end > context.Model.Start,
    x => x.ErrorMessage = "'{PropertyName}' must be after the start.");
```

To validate a collection, validate a model that holds it: `Elements(x => x.Items)` reports `Items[i]`.

A selector is a chain of properties or fields that starts at its parameter, such as `x => x.Address.City`, so its path
names the value it reads. `Nullable<T>.Value` counts as a member, the conversions C# writes for an implicit widening
count as none, and one operator may sit on top: `!x.Active`, `-x.Amount`, `x.Item as string`, a cast, or an array's
`Length`. Any other selector throws when it is declared, and so does `IncludeProperty` with one, because its path would
stop at its last member and report its errors under another property's name:

| Selector | Write instead |
| --- | --- |
| `x => x.Children[0].Name` | `Elements(x => x.Children).Property(c => c.Name)` |
| `x => x.GetChild().Name`, `x => x.Items.Where(...)` | A `Must` on the property the call reads |
| `x => ((Child) x.Item).Name` | `Property(x => x.Item).Include(new ChildValidator())`, which runs only for a `Child` |
| `x => other.Name`, `x => Settings.Name` | A `Must` on the property the value belongs with |

A conversion of the model to a type it already is names the model too. A validator over a generic model, such as
`Validator<TModel> where TModel : INamed`, can declare `x => x.Name`, which C# writes as `x => ((INamed)x).Name`, and
reports it as `Name`. Such a chain reads the member its conversion's type declares, so a `DisplayNameResolver`
receives `INamed.Name`, and the attributes on the model's own `Name` don't name it. A conversion to a type the model
isn't, such as a derived type, still throws.

A comparison target is read, not reported under, so it may start at a captured or static value or compute one.

## Conditional validation

`Where` runs a chain's rules only when a condition holds. A condition can be sync or async, and can read the context.

### Run rules only when a condition holds

```csharp
public class OrderValidator : Validator<Order>
{
    public OrderValidator()
    {
        Builder.Property(x => x.ShippingAddress)
            .Where(x => x.Type == OrderType.Physical)
            .NotEmpty();

        Builder.Property(x => x.DownloadUrl)
            .Where(x => x.Type == OrderType.Digital)
            .NotEmpty();
    }
}
```

Chained conditions must all hold:

```csharp
Builder.Property(x => x.Coupon)
    .Where(x => x.IsCheckout)
    .Where(x => x.Country == "KR")
    .NotEmpty();
```

### Apply one condition to several chains

```csharp
public class UserValidator : Validator<User>
{
    public UserValidator()
    {
        Builder.Property(x => x.Email).NotEmpty();

        Builder.Where(x => x.IsAdmin, () =>
        {
            Builder.Property(x => x.AdminCode).NotEmpty();
            Builder.Property(x => x.AdminLevel).GreaterThan(0);
        });
    }
}
```

The block form lives on the builder, so an inline validator calls `x.Where(...)` the same way.

Conditions run from the outermost block inward, then in chain order, and the first `false` ends the evaluation, so an
outer `Where` can guard what an inner condition dereferences. The order is the same when sync and async conditions are
mixed. Each condition runs at most once per validation, and its verdict holds for the rest of it: a block's condition
gates all of its chains with one call, an `Elements` chain's condition gates the chain and its `Property` chains
with one call, and a chain with sync and async rules doesn't run its conditions again in the async phase. A condition
that calls a database or a service therefore costs one call per validation.

## Partial validation

Pass a context configurator to `Validate` or `ValidateAsync` to run only the rules for selected properties.

### Validate selected properties

```csharp
var result = validator.Validate(user, x => x.IncludeProperty(model => model.Email));
// Only Email's rules run.

await validator.ValidateAsync(user, x => x.IncludeProperty(model => model.Email), cancellationToken);
```

Call `IncludeProperty` more than once to include several properties. Selection works on paths, and a chain the
selection excludes doesn't run its `Where` conditions either:

| Selection | Runs |
| --- | --- |
| `x => x.Billing` | Rules on `Billing` and everything beneath it, including an included child validator |
| `x => x.Billing.City` | Rules on `Billing` and `Billing.City`, and only `City` inside an included child validator |
| `x => x.Items` | The collection's `Elements` chain and its `Property` chains |

The same configurator can seed `context.Parameters` for custom rules. The configurator is done with the context when it
returns; to read the context after the validation, create it and pass it to `Validate(context)` instead.

## Cascade modes

By default every rule runs, and every failure is reported. `ValidationCascadeMode.Stop` ends validation at the first
failure.

### Stop at the first failure

```csharp
public class UserValidator : Validator<User>
{
    public UserValidator()
    {
        Builder.Property(x => x.Email).NotEmpty().Email();
        Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);
    }
}
```

`Stop` reacts to the first `Error`-severity entry. `Warning` and `Information` entries don't block: they are recorded
and validation continues, so they can't hide a later error and report an invalid model as valid.

## Property options

Options passed to `Property` set defaults for every rule on that chain: the reported name, the name in messages,
severity, code, message, and cascade mode.

### Set defaults for one property's rules

```csharp
Builder.Property(x => x.Name, options =>
{
    options.PropertyName = "name";                            // reported as written
    options.ErrorSeverity = ValidationErrorSeverity.Warning;  // default severity for these rules
    options.ErrorCode = "NAME_INVALID";                       // default error code
    options.CascadeMode = ValidationCascadeMode.Stop;         // stop at this property's first failure
}).NotEmpty().MinimumLength(3);
```

| Option | Default | Description |
| --- | --- | --- |
| `PropertyName` | The selector's path, under the naming policy | The reported name, used as written: the naming policy doesn't apply to it |
| `DisplayName` | Not set | The name used in messages (see [Name properties in messages](#name-properties-in-messages)) |
| `ErrorSeverity` | `Error` | Default severity of the property's rules |
| `ErrorCode` | Each rule's own | Default error code of the property's rules |
| `ErrorMessage` | Each rule's own | Default message of the property's rules |
| `CascadeMode` | The validator's | `Stop` ends the chain at its first failure |

A rule's own options merge with these field by field, so a rule that sets only `ErrorMessage` keeps the property's
severity and code. An `Include` on the property keeps the child's severity, code, and message, because a child may mix
severities; `PropertyName` and `CascadeMode` still apply.

## Error messages

Every built-in rule has a default message and an error code. Replace a message on one rule, replace the default for a
code across the app, and name properties in messages.

### Set a rule's message

```csharp
Builder.Property(x => x.Email)
    .NotEmpty(x => x.ErrorMessage = "Email is required.")
    .Email(x => x.ErrorMessage = "'{PropertyValue}' is not a valid email address.");
```

Every message can use `{PropertyName}` and `{PropertyValue}`. Each error code adds a fixed set of placeholders, and
every form of its rule fills them, so a message set for a code works whichever form reported it:

| Error code | Placeholders |
| --- | --- |
| `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual` | `{TargetValue}`, and `{TargetPropertyName}`, empty unless the target is another property |
| `InclusiveBetween`, `ExclusiveBetween` | `{From}`, `{To}`, and `{FromPropertyName}`, `{ToPropertyName}`, each empty unless that bound is another property |
| `Length`, `ExactLength` | `{MinLength}`, `{MaxLength}`, `{Length}` |
| `MaximumLength` | `{MaxLength}`, `{Length}` |
| `MinimumLength`, `PasswordLength` | `{MinLength}`, `{Length}` |
| `Regex` | `{RegularExpression}` |
| `PrecisionScale` | `{IntegerDigits}`, `{Scale}`: the value's digits before the decimal point, without leading zeros, and after it, without the trailing zeros the rule ignores; `{ExpectedPrecision}`, `{ExpectedScale}`: the limits |
| `PasswordDigits`, `PasswordNonAlphanumerics`, `PasswordUppercaseLetters`, `PasswordLowercaseLetters`, `PasswordUniqueCharacters` | `{RequiredCount}` |
| Every other code | None |

`Equal` and `NotEqual` against another property name it in their default message (`'ConfirmPassword' must be equal to
'Password'.`) rather than print its value, which may be a secret; a computed target still shows its value.

A rule's predicate adds its own placeholders with `context.MessageParameters.Add("Foo", "bar")`, and the bare key is
wrapped as `{Foo}`. `MessageParameters` and `context.ErrorMessage` belong to the rule being evaluated and are cleared
when it finishes, so per-call data goes in `context.Parameters`.

Messages are formatted in one pass: an unknown placeholder stays as written, and a substituted value is never scanned
again, so user input that looks like a placeholder is printed literally. Values are formatted in the invariant
culture, and `{PropertyValue}` prints `null` for a null value.

### Replace default messages by error code

Every built-in rule reports an error code named after it, such as `NotEmpty` or `InclusiveBetween`, and an async rule
reports the code of its sync form, so `MustAsync` reports `Must`. A rule of your own reports the code it sets, or its
type's name. `ErrorCodes` holds the built-in codes as constants, for clients that branch on `ValidationError.Code` and
for `ErrorMessages`, which replaces the default message of every rule that reports a code, for app-wide wording or
another language:

```csharp
ValidatorBuilderOptions.Global.ErrorMessages[ErrorCodes.NotEmpty] = "{PropertyName} is required.";
ValidatorBuilderOptions.Global.ErrorMessages[ErrorCodes.InclusiveBetween] = "{PropertyName} must be from {From} to {To}.";
```

The entry for a rule's error code, with the same placeholders, takes the place of its built-in message. A message set
on the rule (`NotEmpty(x => x.ErrorMessage = ...)`) or on its chain still wins, and a code set on the rule
(`x.ErrorCode = "NameMissing"`) picks the entry for that code. `ErrorMessages` can also be set per validator in
`Configure` or per `AddValidators` scan, and those entries win over Global's, code by code. A validator reads the table
when it freezes, and the table is read-only from then on, so set it at startup. Choosing a language per request isn't
built in.

### Name properties in messages

A display name names a property in messages, while the error's `PropertyName`, the key a client maps the error to,
keeps the path:

```csharp
ValidatorBuilderOptions.Global.DisplayNameResolver = member => member.GetCustomAttribute<DisplayAttribute>()?.Name;

Builder.Property(x => x.Email, x => x.DisplayName = "E-mail").NotEmpty();   // wins over the resolver
// Message: "'E-mail' must not be empty."   PropertyName: "Email"
```

The resolver receives the member a selector ends at (`Email` for `x => x.Customer.Email`) and returns `null` to keep
the reported name. It runs when a validator freezes, once for each chain and for each cross-property target that is
another property, so `{TargetPropertyName}`, `{FromPropertyName}`, and `{ToPropertyName}` read display names too. A
display name is used as written: the naming policy and the name wrappers apply to `PropertyName` only. In an element
chain's message it replaces the whole name, and the index stays in `PropertyName` (`Tags[1]`).

## Property names

Errors report the selector's path as the property name. A naming policy and wrappers change how it is written.

### Change the naming policy

```csharp
Configure(x => x.PropertyNamePolicy = PropertyNamePolicy.SnakeCase);
// "FirstName" → "first_name"
```

The policies are `None`, the default, which reports names as declared, `CamelCase`, `PascalCase`, `SnakeCase`, and
`KebabCase`. `CamelCase`, `SnakeCase`, and `KebabCase` use the `System.Text.Json` naming policies, so error keys equal
the member names clients receive: `ID` reports as `id` and `IPAddress` as `ipAddress` under `CamelCase`.

### Report nested paths

```csharp
Builder.Property(x => x.Address.City).NotEmpty();
// PropertyName: "Address.City", or "address.city" under CamelCase
```

A nested selector reports a dotted path, with the policy applied to each segment.

### Wrap names and messages

```csharp
Configure(x =>
{
    x.PropertyNamePrefix = "order.";
    x.ErrorMessageSuffix = " See the order form.";
});
```

| Option | Default | Description |
| --- | --- | --- |
| `PropertyNamePrefix`, `PropertyNameSuffix` | Not set | Literal text around every reported name |
| `ErrorMessagePrefix`, `ErrorMessageSuffix` | Not set | Literal text around every message |

Set these and the naming policy in `Configure`, per `AddValidators` scan, or app-wide in
`ValidatorBuilderOptions.Global`. Validators read Global when they freeze, and the most specific setting wins: the
validator's own `Configure`, then the scan, then Global. To change the app-wide defaults after a validator has frozen,
assign new options to `ValidatorBuilderOptions.Global`: validators that freeze afterward read them, and those that froze
earlier keep what they read.

Policies use invariant casing and apply only to path segments. Wrappers and an explicit `PropertyName` are never
transformed, and a collection index follows its collection segment: `CamelCase` with `PRE_` and `_SUF` reports
`PRE_items[0].label_SUF`. `{TargetPropertyName}`, `{FromPropertyName}`, and `{ToPropertyName}` get the same policy,
without wrappers, unless a display name replaces them.

## Results

`Validate` and `ValidateAsync` return a `ValidationResult`. `ValidateAndThrow` throws instead.

### Read the result

`IsValid` is `false` only when an `Error`-severity entry exists. A result never changes once it's returned: `Errors` is
a read-only `ValidationErrorCollection` of every entry, which a `foreach` walks without allocating, and a result you
build yourself copies the collection you give it. Each
`ValidationError` carries `PropertyName`, `Message`, `Severity`, and an optional `Code`. Two errors are equal when all
four are, compared ordinally, so `Distinct()` and `Contains` compare content.

`result.ToDictionary()` groups the messages by property name, warnings and information included, in the shape ASP.NET
Core's `ValidationProblemDetails` takes:

```csharp
app.MapPost("/orders", (Order order, IValidator<Order> validator) =>
{
    var result = validator.Validate(order);
    return result.IsValid ? Results.Ok() : Results.ValidationProblem(result.ToDictionary());
});
```

### Throw on failure

```csharp
validator.ValidateAndThrow(model);                              // throws ValidationException when invalid
await validator.ValidateAndThrowAsync(model, cancellationToken);
```

`ValidationException.Errors` carries every recorded entry, but only `Error`-severity entries trigger the throw. To throw
from a result of your own, build the exception yourself: `new ValidationException(result.Errors.ToArray())`.

### Report warnings and information

Every error carries a `Severity`: `Error` (the default), `Warning`, or `Information`. Warning and information entries
appear in `Errors` but don't fail `IsValid`; check `HasWarnings` and `HasInformation`, or filter `Errors` by severity:

```csharp
Builder.Property(x => x.LegacyField)
    .NotEmpty(x => x.ErrorSeverity = ValidationErrorSeverity.Warning);

var result = validator.Validate(model);
// result.IsValid is true even when LegacyField is empty, and result.HasWarnings is true.
```

### Serialize errors

System.Text.Json writes `Severity` by member name, so a serialized error reads `"severity":"Error"` rather than a
number. An enum converter in your `JsonSerializerOptions.Converters` takes precedence: add
`new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` to get `"error"`. Numbers are still accepted when reading,
and a `ValidationError` reads back with System.Text.Json.

## Dependency injection

`AddValidator` registers one validator, and `AddValidators` registers every validator in an assembly through the
registrations the bundled source generator writes at build time.

### Register validators

```csharp
services.AddValidator<UserValidator>();                              // one validator
services.AddValidators(typeof(Program).Assembly, options =>          // every validator in an assembly
{
    options.PropertyNamePolicy = PropertyNamePolicy.CamelCase;
    options.CascadeMode = ValidationCascadeMode.Continue;
});

// Inject anywhere:
public class UserService(IValidator<User> validator) { /* ... */ }
```

The options passed to `AddValidators` fill unset fields of the validators that scan registers. Register each
validator once. A scan's lifetime selector sets a scanned validator's lifetime, and registering it again after the
scan adds a second registration, so `IEnumerable<IValidator<T>>` returns it twice.

### Choose a lifetime

```csharp
// Scoped only where a validator has scoped dependencies:
services.AddValidators(typeof(Program).Assembly, configure: null,
    lifetime: x => x == typeof(CreateOrderValidator) ? ServiceLifetime.Scoped : ServiceLifetime.Singleton);
```

Validators keep no per-call state, since each `Validate` builds a fresh context, so one instance is safe to reuse and
share across threads. The default lifetime is therefore Singleton. Pass a `ServiceLifetime`, or a per-type selector as
above, only when a validator depends on a scoped or transient service.

A Scoped or Transient validator is built again for every scope or resolve: its constructor declares the rules, and its
first validation freezes them. For a validator with ten rules that measured about 6 µs and 9 KB per scope, against
0.3 µs and 232 B for a Singleton resolved in the same scope, and the cost grows with the rules. To keep a validator
Singleton when a rule needs a scoped service, depend on a singleton that creates one per call, such as EF Core's
`IDbContextFactory<TContext>`, and use it inside `MustAsync`.

### Inject one validator into another

`AddValidator<T>()` and `AddValidators` also register the validator type itself, and every `IValidator<T>` it
implements resolves through that registration. A Singleton or Scoped validator is therefore one instance for both, and
a Transient one is new on every resolve. The container disposes a validator once for each of those registrations it
was resolved through, so a validator that implements `IDisposable` or `IAsyncDisposable` must tolerate a second
`Dispose` or `DisposeAsync` call, as .NET's dispose pattern requires; better, leave disposable resources to the
services it receives. A validator can take another by its type, which `Include` needs:

```csharp
public class OrderValidator : Validator<Order>
{
    public OrderValidator(AddressValidator address) => Builder.Property(x => x.ShippingAddress).Include(address);
}
```

`AddValidator(validator)` registers an instance as `IValidator<TModel>` only. Every resolve returns that instance, and
the container never disposes it, because the caller that created it owns it. An open generic type registered with
`AddValidator(typeof(V<>), lifetime)` resolves to separate instances for `V<T>` and `IValidator<T>`, because DI closes
an open generic only through a type mapping.

### Implement IValidator yourself

A type that isn't a `Validator<T>`, such as a decorator or a test double, implements `IValidator<T>` by writing the two
members that take a `ValidationContext<T>`:

```csharp
public class LoggingValidator<T>(IValidator<T> inner, ILogger logger) : IValidator<T>
{
    public ValidationResult Validate(ValidationContext<T> context) => Log(inner.Validate(context));

    public async ValueTask<ValidationResult> ValidateAsync(ValidationContext<T> context, CancellationToken cancellationToken = default) =>
        Log(await inner.ValidateAsync(context, cancellationToken));

    private ValidationResult Log(ValidationResult result)
    {
        if (!result.IsValid) logger.LogInformation("{Count} validation errors", result.Errors.Count);
        return result;
    }
}
```

The members that take a model, and the `object` ones of `IValidator`, have default implementations that create a context
and call these two. Default implementations belong to the interface, so call them through `IValidator<T>`: on a variable
of the class's own type, `Validate(model)` doesn't compile unless the class declares it. A class that implements
`IValidator<T>` for two model types gets two default implementations of `IValidator.Validate(object)`, which C# rejects
as CS8705, so such a class implements the two `IValidator` members itself.

### Validate messages with Sharp.Mediator

The `Sharp.Mediator.Validation` package of [Sharp.Mediator](https://github.com/sharp-space/mediator) runs every
validator registered for a request before its handler:

```csharp
services.AddMediator(x => x
    .AddValidationMiddleware()
    .AddHandlers(typeof(Program).Assembly));

services.AddValidators(typeof(Program).Assembly);
```

An invalid request throws `ValidationException` and never reaches its handler. A handler that returns a `Response`
envelope can get the failures back as an `Invalid` response instead, which `Sharp.Mediator.HttpResults` turns into a
422; the [Sharp.Mediator README](https://github.com/sharp-space/mediator#sharpmediatorvalidation) lists the options.

### When a validator freezes

The first `Validate` or `ValidateAsync`, or the first time `Include` consumes a validator, freezes
its definition. After that, `Configure`, adding rules, and adding `Where` conditions throw
`InvalidOperationException`.

An options object is read-only once it has been read, so a change that would go unseen throws
`InvalidOperationException` instead:

- The options a `configure` callback receives, for a chain, a rule, an `Include`, or `Password`, are read when the
  callback returns. A custom rule's own `Configure` options are read when a chain takes the rule.
- `ValidatorBuilderOptions`, from `Configure`, an `AddValidators` scan, or `Global`, are read when a validator that uses
  them freezes, `ErrorMessages` included. Assigning new options to `ValidatorBuilderOptions.Global` is always allowed.

`Include` freezes the validator it includes, so creating a validator whose constructor includes another reads `Global`
already. Set `Global` before the app creates or runs its first validator.

## Coming from FluentValidation

| FluentValidation | Sharp.Validator | Notes |
| --- | --- | --- |
| `RuleFor(x => x.Name)` | `Builder.Property(x => x.Name)` | |
| `RuleForEach(x => x.Tags)` | `Builder.Elements(x => x.Tags)` | Errors report as `Tags[i]` |
| `RuleForEach(...).ChildRules(...)` | `Builder.Elements(...).Property(...)` | Child errors report as `Items[i].Child` |
| `When(...)`, `WhenAsync(...)` | `.Where(...)` on a chain, or `Builder.Where(condition, () => { ... })` for a block | Chained conditions must all hold |
| `Must`, `MustAsync` | `Must`, `MustAsync` | The predicate receives `null` too, as in FluentValidation |
| `SetValidator(child)` | `Include(child)` | FluentValidation's own `Include` merges rules of the same type; this `Include` runs a child validator |
| `SetValidator(this)` | Not supported | See [Recursive models](#recursive-models) |
| `RuleFor(x => x)` | Not supported | Put the rule on one property and read the model from `context.Model`; see [Check across properties](#check-across-properties) |
| `RuleForEach(x => x)` | Not supported | Validate a model that holds the collection |
| `PrecisionScale(p, s)` | `PrecisionScale(p, s)` | The message reports the value's own digits, where FluentValidation reports them padded to the allowed decimals |
| `WithMessage(...)`, `WithSeverity(...)`, `WithErrorCode(...)` | A callback on the rule: `.NotEmpty(x => x.ErrorMessage = ...)` | `ErrorSeverity` and `ErrorCode` set the others |
| `WithName(...)` | `DisplayName` in the options of `Property(selector, options)` | See [Name properties in messages](#name-properties-in-messages) |
| `OverridePropertyName(...)` | `PropertyName` in the options of `Property(selector, options)` | |
| `Unless(...)` | `.Where(...)` with the condition negated | |
| `RuleSet` | Not supported | To validate some properties only, see [Partial validation](#partial-validation) |
| `Cascade(CascadeMode.Stop)` | `CascadeMode = ValidationCascadeMode.Stop` | Per validator through `Configure`, per property through `Property(selector, options)`; only `Error` severity stops, because a warning doesn't fail `IsValid` here |
| `ValidateAndThrow`, `ValidateAndThrowAsync` | `ValidateAndThrow`, `ValidateAndThrowAsync` | |

## Native AOT and trimming

Building validators and calling `Validate` or `ValidateAsync` works under Native AOT and trimming out of the box. For
registration, the package bundles a source generator that finds your `IValidator<T>` implementations at compile time
and writes explicit, closed-generic registrations, so the same `AddValidators(assembly)` call works under the JIT and
Native AOT without configuration:

```csharp
// Uses the generated registrations under both the JIT and Native AOT. Only an assembly built without the generator
// falls back to a reflection scan, and only under the JIT.
services.AddValidators(typeof(Program).Assembly);
```

Under Native AOT, `AddValidators` throws `InvalidOperationException` for an assembly built without the generator instead
of scanning it.

- `AddValidator<T>()` is trim-annotated and safe under Native AOT.
- A validator that generated code can't name, such as a `private` nested or `file`-local type, is left out by
  `AddValidators`, through the generated registrations and the reflection fallback alike, and reported as
  [SVL001](https://github.com/sharp-space/validator/blob/main/doc/diagnostics/SVL001.md) on its declaration. Make it
  `public` or `internal`, or register it with `AddValidator<T>()`. A validator that the code around it creates with
  `new`, as `Include(new AddressValidator())` does, isn't meant for DI and isn't reported.
- The generator also precompiles the property accessors that selectors such as `x => x.Address.City` need, so they
  don't run in the expression interpreter under Native AOT.
- In a project with `PublishAot` set, a literal `Regex("...")` rule is reported as
  [SVL010](https://github.com/sharp-space/validator/blob/main/doc/diagnostics/SVL010.md), because it runs interpreted
  under Native AOT. Its code fix converts the pattern to a `[GeneratedRegex]` method and keeps the 2-second match
  timeout; both ignore case in the invariant culture, so the conversion keeps the rule's verdicts. Under the JIT the
  literal pattern scales better under parallel load (see [Performance](#performance)), so SVL010 stays silent there.
- In a trimmed or AOT project, a cross-property target that isn't a member chain of the model, such as
  `x => x.Minimum * 2` or `x => settings.Max`, is
  reported as [SVL011](https://github.com/sharp-space/validator/blob/main/doc/diagnostics/SVL011.md): it is compiled
  when the validator is built, so it runs interpreted under Native AOT, and trimming can remove an operator it needs.
- The libraries build with `<IsAotCompatible>true</IsAotCompatible>`, and CI publishes and runs a Native AOT test app.

> **Note:** When you publish with `PublishTrimmed` under the JIT, build the scanned assemblies with the bundled
> generator, which is the default: the reflection fallback can't see types the trimmer removed.

## Performance

Validating the same models with the same rules, Sharp.Validator is 2.1 to 25 times faster than
[FluentValidation](https://github.com/FluentValidation/FluentValidation) 12.2.0 and 7.7 to 13 times faster than
DataAnnotations in every scenario measured:

| Scenario | Model | Sharp.Validator | FluentValidation 12.2.0 | DataAnnotations |
| --- | --- | ---: | ---: | ---: |
| Flat model with seven rules | Valid | 181 ns, 80 B | 386 ns, 696 B | 1,390 ns, 2,288 B |
| | Invalid | 189 ns, 216 B | 4,700 ns, 9,144 B | 2,430 ns, 3,472 B |
| Two included validators | Valid | 436 ns, 240 B | 1,540 ns, 2,416 B | |
| | Invalid | 811 ns, 1,408 B | 8,920 ns, 15,384 B | |
| Collection of 20 strings | Valid | 207 ns, 80 B | 3,170 ns, 5,856 B | |
| | Invalid | 1,920 ns, 4,376 B | 21,900 ns, 42,600 B | |
| Async rule | Valid | 110 ns, 80 B | 388 ns, 736 B | |
| | Invalid | 202 ns, 392 B | 1,740 ns, 3,048 B | |

Each cell is the time of one validation, the median of five processes on Linux x64, and the memory the validation
allocates. Each library runs warm, with each validator created once as a Singleton is, and an invalid model breaks the
same number of rules in each: four of the flat model's, eight across an order and its customer and address, one for each
of the 20 strings, and two in the async model. The flat model checks an email address, a length, a range, and a regular
expression; each string must be neither empty nor longer than 16 characters; and the async rule completes at once.
DataAnnotations has no counterpart for an included validator, a rule on each element, or an async rule, so it validates
the flat model only. Sharp.Validator reads properties here through compiled expressions, the fallback for a build
without the source generator, so the table leaves out what the generated accessors save.

Sharp.Validator is allocation-aware by design. The all-valid path has constant-size result overhead and allocates no
error list and nothing per rule or per element, except in two rules and one walk: `Url()` and `Uri()` parse each
value into a `System.Uri`, `NotEmpty()` and `Empty()` can allocate the enumerator they read a collection through when
it doesn't implement the non-generic `ICollection`, as a `HashSet<T>` doesn't, and a collection that isn't a list,
walked by more than one chain, is copied into an array (see
[Validate every item of a collection](#validate-every-item-of-a-collection)). The source generator precompiles property
accessors, which are otherwise expression-compiled once and cached, and the hot paths avoid LINQ, boxing, and
closures. `Include` keeps these properties: an included child creates no result object, and a collection chain reuses
one child context across its elements, so a context belongs to the rule call that received it. A rule with a fixed
message reports one immutable `ValidationError` from every validation, which keeps the failure path to a list entry.

.NET's `Regex` keeps one idle runner per instance, so a shared instance stops scaling once threads overlap on it. A
`Regex("pattern")` rule spreads its pattern over one instance per core, up to 64; past that, cores share instances. Each
instance costs about 14 KiB, and with `RegexOptions.Compiled` a compile of its own. The instances belong to the pattern:
rules built over the same pattern, options, and timeout share them, so a Scoped validator doesn't parse its patterns
again in every scope, and the process keeps them, for up to 256 patterns. A `Regex` you pass in stays one shared
instance, and that includes a `[GeneratedRegex]`: it is the fastest option on one thread and tops out near a single
core's throughput under sustained parallel load, such as a bulk `Parallel.ForEach`. Ordinary request rates stay far
below that limit.

### Run the benchmarks

The repository separates Sharp-only benchmarks from comparisons with FluentValidation and DataAnnotations. In a clone of
the repository:

```bash
# Sharp-only scenarios
dotnet run -c Release --project test/Sharp.Validator.Benchmarks
# Comparisons with FluentValidation and DataAnnotations
dotnet run -c Release --project test/Sharp.Validator.ComparisonBenchmarks -- --filter '*'
# Smoke checks and cross-library error-count parity, without timing
dotnet run -c Release --project test/Sharp.Validator.Benchmarks -- --check
dotnet run -c Release --project test/Sharp.Validator.ComparisonBenchmarks -- --check
# Throughput as threads are added
dotnet run -c Release --project test/Sharp.Validator.Benchmarks -- --filter '*ParallelThroughput*'
# Element walks of 2, 10, and 10,000 elements, through generated and fallback accessors
dotnet run -c Release --project test/Sharp.Validator.Benchmarks -- --filter '*ElementWalk*'
# What a Scoped validator costs per scope, against a Singleton
dotnet run -c Release --project test/Sharp.Validator.Benchmarks -- --filter '*ValidatorLifetime*'
# The Sharp-only scenarios compiled with Native AOT, against the JIT, both through generated accessors
dotnet run -c Release --project test/Sharp.Validator.Benchmarks -- --native-aot --filter '*RuleHotPath*'
```

The comparison above comes from `FlatModelBenchmarks`, `NestedBenchmarks`, `CollectionBenchmarks`, and
`AsyncBenchmarks` in `test/Sharp.Validator.ComparisonBenchmarks`, which give each library the same models, rules, and
inputs; its `--check` confirms that every library reports the same number of errors. The table's runs add
`--launchCount 5 --warmupCount 5 --iterationCount 20` to the comparison command. The comparisons read properties through
compiled expressions, while the Sharp-only element walk, chain count, shared condition, and element include scenarios
time the generated and fallback accessors side by side.

## Diagnostics

The bundled analyzers report `SVL`-prefixed diagnostics. Each one has a page with its cause, its fix, and how to
suppress it: [diagnostics](https://github.com/sharp-space/validator/blob/main/doc/diagnostics/README.md).

## Contributing

Bug reports and pull requests are welcome — see
[CONTRIBUTING.md](https://github.com/sharp-space/validator/blob/main/CONTRIBUTING.md), which also has the code of
conduct. Report security problems as described in the
[security policy](https://github.com/sharp-space/validator/security/policy).

## License

Sharp.Validator is licensed under the [MIT License](https://github.com/sharp-space/validator/blob/main/LICENSE).
