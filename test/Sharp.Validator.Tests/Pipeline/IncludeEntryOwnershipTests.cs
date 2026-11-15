namespace Sharp.Validator.Tests.Pipeline;

// Property(selector, options) sets defaults for the property's own rules. Entries merged from an included child keep
// the child's severity, code, and message, because a child may mix severities and flattening them would lose that.
public sealed class IncludeEntryOwnershipTests
{
    private static Validator<Address> Child() => new(x => x.Property(m => m.City).NotEmpty(o =>
    {
        o.ErrorCode = "CHILD";
        o.ErrorMessage = "child message";
    }));

    [Fact]
    public void ChildEntries_KeepTheirSeverityCodeAndMessage()
    {
        var parent = new Validator<Person>(x => x
            .Property(m => m.Address, o =>
            {
                o.ErrorSeverity = ValidationErrorSeverity.Warning;
                o.ErrorCode = "PARENT";
                o.ErrorMessage = "parent message";
            })
            .Include(Child()));

        var error = Assert.Single(parent.Validate(new Person { Address = new Address() }).Errors);
        Assert.Equal(ValidationErrorSeverity.Error, error.Severity);
        Assert.Equal("CHILD", error.Code);
        Assert.Equal("child message", error.Message);
    }

    [Fact]
    public void PropertyRulesNextToTheInclude_StillTakeTheDefaults()
    {
        var parent = new Validator<Person>(x => x
            .Property(m => m.Address, o => o.ErrorCode = "PARENT")
            .NotNull()
            .Include(Child()));

        Assert.Equal("PARENT", Assert.Single(parent.Validate(new Person()).Errors).Code);
    }

    [Fact]
    public void PropertyNameOverride_StillReachesChildEntries()
    {
        var parent = new Validator<Person>(x => x
            .Property(m => m.Address, o => o.PropertyName = "Home")
            .Include(Child()));

        Assert.Equal("Home.City", Assert.Single(parent.Validate(new Person { Address = new Address() }).Errors).PropertyName);
    }
}