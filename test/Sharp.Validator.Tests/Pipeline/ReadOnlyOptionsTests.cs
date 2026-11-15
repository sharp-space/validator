using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Pipeline;

// Options were read at different times, and a change made after that went unseen without a word: the options a callback
// received when it returned, and a validator's own, its scan's, and Global's when it froze. Each options object is
// read-only once it has been read, so such a change throws instead.
public sealed class ReadOnlyOptionsTests
{
    private static readonly Validator<Address> AddressValidator = new(x => x.Property(address => address.City).NotEmpty());

    public static TheoryData<string, Action> CallbackOptionChanges => new()
    {
        {
            "Property CascadeMode",
            () => Chain().CascadeMode = ValidationCascadeMode.Stop
        },
        {
            "Property PropertyName",
            () => Chain().PropertyName = "late"
        },
        {
            "Property DisplayName",
            () => Chain().DisplayName = "late"
        },
        {
            "Property ErrorSeverity",
            () => Chain().ErrorSeverity = ValidationErrorSeverity.Warning
        },
        {
            "Property ErrorCode",
            () => Chain().ErrorCode = "late"
        },
        {
            "Property ErrorMessage",
            () => Chain().ErrorMessage = "late"
        },
        {
            "Elements ErrorCode",
            () => Captured<PropertyValidatorBuilderOptions>((b, keep) => b.Elements(x => x.Tags, keep)).ErrorCode = "late"
        },
        {
            "Item Property ErrorCode",
            () => Captured<PropertyValidatorBuilderOptions>((b, keep) => b.Elements(x => x.Tags).Property(tag => tag!.Length, keep)).ErrorCode =
                "late"
        },
        {
            "Rule ErrorSeverity",
            () => Rule().ErrorSeverity = ValidationErrorSeverity.Warning
        },
        {
            "Rule ErrorCode",
            () => Rule().ErrorCode = "late"
        },
        {
            "Rule ErrorMessage",
            () => Rule().ErrorMessage = "late"
        },
        {
            "Include IncludeParentPropertyName",
            () => Captured<IncludeValidatorOptions>((b, keep) => b.Property(x => x.Address).Include(AddressValidator, keep))
                .IncludeParentPropertyName = false
        },
        {
            "Password RequiredLength",
            () => Password().RequiredLength = 1
        },
        {
            "Password RequiredDigits",
            () => Password().RequiredDigits = 1
        },
        {
            "Password RequiredNonAlphanumerics",
            () => Password().RequiredNonAlphanumerics = 1
        },
        {
            "Password RequiredUppercaseLetters",
            () => Password().RequiredUppercaseLetters = 1
        },
        {
            "Password RequiredLowercaseLetters",
            () => Password().RequiredLowercaseLetters = 1
        },
        {
            "Password RequiredUniqueCharacters",
            () => Password().RequiredUniqueCharacters = 1
        }
    };

    public static TheoryData<string, Action<ValidatorBuilderOptions>> ValidatorOptionChanges => new()
    {
        { "CascadeMode", options => options.CascadeMode = ValidationCascadeMode.Stop },
        { "PropertyNamePolicy", options => options.PropertyNamePolicy = PropertyNamePolicy.CamelCase },
        { "PropertyNamePrefix", options => options.PropertyNamePrefix = "late" },
        { "PropertyNameSuffix", options => options.PropertyNameSuffix = "late" },
        { "ErrorMessagePrefix", options => options.ErrorMessagePrefix = "late" },
        { "ErrorMessageSuffix", options => options.ErrorMessageSuffix = "late" },
        { "DisplayNameResolver", options => options.DisplayNameResolver = _ => "late" },
        { "TimeProvider", options => options.TimeProvider = TimeProvider.System },
        { "ErrorMessages indexer", options => options.ErrorMessages["Code"] = "late" },
        { "ErrorMessages Add", options => options.ErrorMessages.Add("Other", "late") },
        { "ErrorMessages Add pair", options => options.ErrorMessages.Add(new KeyValuePair<string, string>("Other", "late")) },
        { "ErrorMessages Remove", options => options.ErrorMessages.Remove("Code") },
        { "ErrorMessages Remove pair", options => options.ErrorMessages.Remove(new KeyValuePair<string, string>("Code", "early")) },
        { "ErrorMessages Clear", options => options.ErrorMessages.Clear() }
    };

    [Theory]
    [MemberData(nameof(CallbackOptionChanges))]
    public void CallbackOptions_ThrowOnAChangeOnceTheCallbackReturned(string member, Action change)
    {
        var exception = Assert.Throws<InvalidOperationException>(change);
        Assert.True(exception.Message.Contains("inside the callback", StringComparison.Ordinal), member);
    }

    [Theory]
    [MemberData(nameof(ValidatorOptionChanges))]
    // Configure may run more than once before the first validation, so a validator's own options stay writable until it
    // freezes, and each change before that counts.
    public void ValidatorOptions_ThrowOnAChangeOnceTheValidatorFroze(string member, Action<ValidatorBuilderOptions> change)
    {
        ValidatorBuilderOptions? kept = null;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.Configure(options =>
        {
            kept = options;
            options.ErrorMessages["Code"] = "early";
        });

        change(kept!);
        validator.Validate(new Person());

        var exception = Assert.Throws<InvalidOperationException>(() => change(kept!));
        Assert.True(exception.Message.Contains("ValidatorBuilderOptions.Global", StringComparison.Ordinal), member);
    }

    [Fact]
    public void ScanDefaults_ThrowOnAChangeOnceAValidatorReadThem()
    {
        var scan = new ValidatorBuilderOptions();
        IServiceCollection services = new ServiceCollection();
        services.Add(ValidatorRegistry.CreateDescriptor(
            typeof(IValidator<Person>), typeof(ScannedValidator), ServiceLifetime.Singleton, scan));

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<IValidator<Person>>();
        scan.ErrorMessagePrefix = "scan: ";

        var error = Assert.Single(validator.Validate(new Person()).Errors);
        Assert.Equal("scan: 'Name' must not be empty.", error.Message);
        Assert.Throws<InvalidOperationException>(() => scan.ErrorMessagePrefix = "late: ");
    }

    [Fact]
    public void ErrorMessages_StayReadableOnceReadOnly()
    {
        ValidatorBuilderOptions? kept = null;
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.Configure(options =>
        {
            kept = options;
            options.ErrorMessages["NotEmpty"] = "required";
        });

        var table = kept!.ErrorMessages;
        var writableBefore = !table.IsReadOnly;
        validator.Validate(new Person());

        var copy = new KeyValuePair<string, string>[1];
        table.CopyTo(copy, 0);

        var contains = table.Contains(new KeyValuePair<string, string>("NotEmpty", "required"));
        var containsKey = table.ContainsKey("NotEmpty");
        var found = table.TryGetValue("NotEmpty", out var message);
        var count = table.Count;

        List<KeyValuePair<string, string>> typed = [.. table];
        List<object> untyped = [.. (IEnumerable)table];

        Assert.True(writableBefore);
        Assert.True(table.IsReadOnly);
        Assert.Equal("required", table["NotEmpty"]);
        Assert.Equal(["NotEmpty"], table.Keys);
        Assert.Equal(["required"], table.Values);
        Assert.Equal(1, count);
        Assert.True(contains && containsKey && found);
        Assert.Equal("required", message);
        Assert.Equal(copy, typed);
        Assert.Equal(copy.Cast<object>(), untyped);
    }

    private static PropertyValidatorBuilderOptions Chain() =>
        Captured<PropertyValidatorBuilderOptions>((b, keep) => b.Property(x => x.Name, keep));

    private static PropertyValidatorOptions Rule() =>
        Captured<PropertyValidatorOptions>((b, keep) => b.Property(x => x.Name).NotEmpty(keep));

    private static PasswordValidatorOptions Password() =>
        Captured<PasswordValidatorOptions>((b, keep) => b.Property(x => x.Password).Password(keep));

    private static TOptions Captured<TOptions>(Action<ValidatorBuilder<Person>, Action<TOptions>> declare)
    {
        TOptions? captured = default;
        _ = new Validator<Person>(builder => declare(builder, options => captured = options));
        return captured!;
    }

    private sealed class ScannedValidator : Validator<Person>
    {
        public ScannedValidator() => Builder.Property(x => x.Name).NotEmpty();
    }
}