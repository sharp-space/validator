using System.Globalization;
using static Sharp.Validator.Tests.Fixtures.ClockedValidators;

namespace Sharp.Validator.Tests.Validators;

public sealed class DateTests
{
    private static FixedTimeProvider Now(string iso) =>
        new(DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture));

    [Fact]
    public void InPast_DateTimeStrictlyLessThanNow()
    {
        var fixedNow = Now("2026-05-15T12:00:00Z");
        var validator = Clocked<Person>(fixedNow, x => x.Property(person => person.BirthDate).InPast());
        Assert.True(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 11, 59, 59, DateTimeKind.Utc) }).IsValid);
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc) }).IsValid);
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 12, 0, 1, DateTimeKind.Utc) }).IsValid);
    }

    [Fact]
    public void InFuture_DateTimeStrictlyGreaterThanNow()
    {
        var fixedNow = Now("2026-05-15T12:00:00Z");
        var validator = Clocked<Person>(fixedNow, x => x.Property(person => person.BirthDate).InFuture());
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 11, 59, 59, DateTimeKind.Utc) }).IsValid);
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc) }).IsValid);
        Assert.True(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 12, 0, 1, DateTimeKind.Utc) }).IsValid);
    }

    [Fact]
    public void IsTodayDateTime_ComparesDatesOnly()
    {
        var fixedNow = Now("2026-05-15T08:00:00Z");
        var validator = Clocked<Person>(fixedNow, x => x.Property(person => person.BirthDate).IsToday());
        Assert.True(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 0, 0, 1) }).IsValid);
        Assert.True(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 23, 59, 59) }).IsValid);
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 14, 23, 59, 59) }).IsValid);
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 16, 0, 0, 0) }).IsValid);
    }

    [Fact]
    public void NullableInPast_SkipsNull()
    {
        var fixedNow = Now("2026-05-15T12:00:00Z");
        var validator = Clocked<Person>(fixedNow, x => x.Property(person => person.OptionalDate).InPast());
        Assert.True(validator.Validate(new Person { OptionalDate = null }).IsValid);
        Assert.True(validator.Validate(new Person { OptionalDate = new DateTime(2025, 1, 1) }).IsValid);
        Assert.False(validator.Validate(new Person { OptionalDate = new DateTime(2099, 1, 1) }).IsValid);
    }

    [Fact]
    public void NullableInFuture_SkipsNull()
    {
        var fixedNow = Now("2026-05-15T12:00:00Z");
        var validator = Clocked<Person>(fixedNow, x => x.Property(person => person.OptionalDate).InFuture());
        Assert.True(validator.Validate(new Person { OptionalDate = null }).IsValid);
        Assert.True(validator.Validate(new Person { OptionalDate = new DateTime(2099, 1, 1) }).IsValid);
        Assert.False(validator.Validate(new Person { OptionalDate = new DateTime(2025, 1, 1) }).IsValid);
    }

    [Fact]
    public void NullableIsToday_SkipsNull()
    {
        var fixedNow = Now("2026-05-15T08:00:00Z");
        var validator = Clocked<Person>(fixedNow, x => x.Property(person => person.OptionalDate).IsToday());
        Assert.True(validator.Validate(new Person { OptionalDate = null }).IsValid);
        Assert.True(validator.Validate(new Person { OptionalDate = new DateTime(2026, 5, 15, 23, 0, 0) }).IsValid);
        Assert.False(validator.Validate(new Person { OptionalDate = new DateTime(2026, 5, 14) }).IsValid);
    }

    [Fact]
    public void Date_ValidatorsDefaultToTimeProviderSystem()
    {
        // Regression: passing null TimeProvider must not throw.
        var validator = new Validator<Person>(x => x.Property(person => person.BirthDate).InPast());
        Assert.True(validator.Validate(new Person { BirthDate = DateTime.Now.AddYears(-1) }).IsValid);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}