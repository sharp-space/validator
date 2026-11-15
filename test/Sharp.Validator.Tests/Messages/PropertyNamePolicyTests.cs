using System.Text.Json;

namespace Sharp.Validator.Tests.Messages;

[Collection(GlobalOptions.Name)]
public sealed class PropertyNamePolicyTests
{
    public static TheoryData<PropertyNamePolicy, JsonNamingPolicy> SerializerPolicies => new()
    {
        { PropertyNamePolicy.CamelCase, JsonNamingPolicy.CamelCase },
        { PropertyNamePolicy.SnakeCase, JsonNamingPolicy.SnakeCaseLower },
        { PropertyNamePolicy.KebabCase, JsonNamingPolicy.KebabCaseLower }
    };

    [Theory]
    [InlineData(PropertyNamePolicy.None, "Name")]
    [InlineData(PropertyNamePolicy.CamelCase, "name")]
    [InlineData(PropertyNamePolicy.PascalCase, "Name")]
    [InlineData(PropertyNamePolicy.SnakeCase, "name")]
    [InlineData(PropertyNamePolicy.KebabCase, "name")]
    public void NamePolicy_IsApplied(PropertyNamePolicy policy, string expected)
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.Configure(x => x.PropertyNamePolicy = policy);
        var result = validator.Validate(new Person { Name = null });
        Assert.Equal(expected, result.Errors[0].PropertyName);
    }

    [Fact]
    public void PrefixAndSuffix_AreApplied()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.Configure(options =>
        {
            options.PropertyNamePrefix = "PRE_";
            options.PropertyNameSuffix = "_SUF";
        });

        var result = validator.Validate(new Person { Name = null });
        Assert.Equal("PRE_Name_SUF", result.Errors[0].PropertyName);
    }

    [Fact]
    public void PrefixSuffixCombine_WithSnakeCase()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        validator.Configure(options =>
        {
            options.PropertyNamePolicy = PropertyNamePolicy.SnakeCase;
            options.PropertyNamePrefix = "Hello";
        });

        var result = validator.Validate(new Person { Name = null });

        // Prefixes are literal external wrappers; only the selected path segment is transformed.
        Assert.Equal("Helloname", result.Errors[0].PropertyName);
    }

    private static List<string> ReportedNames(PropertyNamePolicy policy)
    {
        var validator = new Validator<Members>(x =>
        {
            x.Property(m => m.ID).NotEmpty();
            x.Property(m => m.URL).NotEmpty();
            x.Property(m => m.IPAddress).NotEmpty();
            x.Property(m => m.HTMLBody).NotEmpty();
            x.Property(m => m.UserID).NotEmpty();
            x.Property(m => m.OAuthToken).NotEmpty();
            x.Property(m => m.Address2Line).NotEmpty();
            x.Property(m => m.snake_member).NotEmpty();
        });

        validator.Configure(x => x.PropertyNamePolicy = policy);
        return validator.Validate(new Members()).Errors.Select(x => x.PropertyName).ToList();
    }

    [Fact]
    // Regression: CamelCase lowered only the first letter, so ID became iD and IPAddress became iPAddress while
    // System.Text.Json and Newtonsoft both report id and ipAddress. Error keys exist to match those names.
    public void CamelCase_LowersALeadingAcronym() =>
        Assert.Equal(
            ["id", "url", "ipAddress", "htmlBody", "userID", "oAuthToken", "address2Line", "snake_member"],
            ReportedNames(PropertyNamePolicy.CamelCase));

    [Theory]
    [MemberData(nameof(SerializerPolicies))]
    public void Policy_MatchesTheSystemTextJsonPolicy(PropertyNamePolicy policy, JsonNamingPolicy serializer)
    {
        string[] members =
        [
            nameof(Members.ID), nameof(Members.URL), nameof(Members.IPAddress), nameof(Members.HTMLBody),
            nameof(Members.UserID), nameof(Members.OAuthToken), nameof(Members.Address2Line), nameof(Members.snake_member)
        ];

        Assert.Equal(members.Select(serializer.ConvertName), ReportedNames(policy));
    }

    [Fact]
    public void UnknownNamePolicy_FallsThroughToIdentity()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());

        // An out-of-range policy exercises the default switch arm (name returned unchanged).
        validator.Configure(x => x.PropertyNamePolicy = (PropertyNamePolicy)999);

        var result = validator.Validate(new Person { Name = null });
        Assert.False(result.IsValid);
        Assert.Equal("Name", result.Errors[0].PropertyName);
    }

    // The member names are the input the policies convert, so they break the naming rules on purpose.
    // ReSharper disable InconsistentNaming
    private sealed class Members
    {
        public string? ID { get; init; }

        public string? URL { get; init; }

        public string? IPAddress { get; init; }

        public string? HTMLBody { get; init; }

        public string? UserID { get; init; }

        public string? OAuthToken { get; init; }

        public string? Address2Line { get; init; }

        public string? snake_member { get; init; }
    }
    // ReSharper restore InconsistentNaming
}