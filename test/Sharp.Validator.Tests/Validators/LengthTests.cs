namespace Sharp.Validator.Tests.Validators;

public sealed class LengthTests
{
    [Fact]
    public void Length_RangeInt()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Length(2, 4));
        Assert.True(validator.Validate(new Person { Name = null }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "a" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "ab" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abcd" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abcde" }).IsValid);
    }

    [Fact]
    public void Length_ExactInt()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Length(3));
        Assert.False(validator.Validate(new Person { Name = "ab" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abcd" }).IsValid);
    }

    [Fact]
    public void MaximumLength_Int()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MaximumLength(3));
        Assert.True(validator.Validate(new Person { Name = "ab" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abcd" }).IsValid);
    }

    [Fact]
    public void MinimumLength_Int()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MinimumLength(3));
        Assert.False(validator.Validate(new Person { Name = "ab" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abc" }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abcd" }).IsValid);
    }

    [Fact]
    public void Length_WithFuncs()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Length(x => x.Age, x => x.Age + 2));

        Assert.True(validator.Validate(new Person { Name = "ab", Age = 2 }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abc", Age = 2 }).IsValid);
        Assert.True(validator.Validate(new Person { Name = "abcd", Age = 2 }).IsValid);
        Assert.False(validator.Validate(new Person { Name = "abcde", Age = 2 }).IsValid);
    }

    [Fact]
    public void Length_InvalidConstructorArgs() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Validator<Person>(x => x.Property(person => person.Name).Length(10, 5)));

    [Fact]
    public void Length_FuncOverload_MessageFillsBothBoundPlaceholders()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .Length(x => x.Age, x => x.Age + 2));

        var result = validator.Validate(new Person { Name = "abcdef", Age = 2 });
        Assert.False(result.IsValid);

        // Default message is "'{PropertyName}' must be between {MinLength} and {MaxLength} characters."
        Assert.Contains("between 2 and 4", result.Errors[0].Message ?? "");
    }

    [Fact]
    // MinimumLength(Func) → the StringLengthValidator(Func, int) base ctor; on failure only the dynamic {MinLength}
    // surfaces and the constant -1 maximum never leaks.
    public void MinimumLengthFuncOverload_ReportsOnlyMinPlaceholder()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MinimumLength(person => person.Age));
        Assert.True(validator.Validate(new Person { Name = "abcdef", Age = 5 }).IsValid);

        var result = validator.Validate(new Person { Name = "ab", Age = 5 });
        Assert.False(result.IsValid);

        var error = result.Errors[0];
        Assert.Equal("MinimumLength", error.Code);
        Assert.Contains("at least 5", error.Message!);
        Assert.DoesNotContain("{", error.Message!); // every placeholder substituted
        Assert.DoesNotContain("-1", error.Message!); // sentinel maximum does not leak
    }

    [Fact]
    // MaximumLength(Func) → the StringLengthValidator(int, Func) base ctor; on failure only {MaxLength} surfaces.
    public void MaximumLengthFuncOverload_ReportsOnlyMaxPlaceholder()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).MaximumLength(person => person.Age));
        Assert.True(validator.Validate(new Person { Name = "ab", Age = 3 }).IsValid);

        var result = validator.Validate(new Person { Name = "abcdef", Age = 3 });
        Assert.False(result.IsValid);

        var error = result.Errors[0];
        Assert.Equal("MaximumLength", error.Code);
        Assert.Contains("3 characters or fewer", error.Message!);
        Assert.DoesNotContain("{", error.Message!);
        Assert.DoesNotContain("-1", error.Message!);
    }

    [Fact]
    // Length(Func) exact → ExactLengthValidator(Func) ctor → base (Func, Func); {MaxLength} surfaces.
    public void ExactLengthFuncOverload_ReportsExpectedLength()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Length(person => person.Age));
        Assert.True(validator.Validate(new Person { Name = "abcd", Age = 4 }).IsValid);

        var result = validator.Validate(new Person { Name = "abc", Age = 4 });
        Assert.False(result.IsValid);

        var error = result.Errors[0];
        Assert.Equal("ExactLength", error.Code);
        Assert.Contains("exactly 4", error.Message!);
        Assert.DoesNotContain("{", error.Message!);
    }
}