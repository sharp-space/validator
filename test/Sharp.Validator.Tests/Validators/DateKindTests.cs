using System.Globalization;
using static Sharp.Validator.Tests.Fixtures.ClockedValidators;

namespace Sharp.Validator.Tests.Validators;

// Date rules must compare a value against "now" in the value's own frame of reference:
// a UTC value against UTC now, a Local/Unspecified value against local wall-clock time.
// Previously every value was compared against local now by raw ticks, so UTC values were misjudged by the
// server's UTC offset.
public sealed class DateKindTests
{
    private static readonly TimeZoneInfo UtcPlusNine =
        TimeZoneInfo.CreateCustomTimeZone("UTC+9-test", TimeSpan.FromHours(9), "UTC+9", "UTC+9");

    // UTC now = 12:00Z → local (UTC+9) now = 21:00.
    private static readonly ZonedTimeProvider Clock =
        new(DateTimeOffset.Parse("2026-05-15T12:00:00Z", CultureInfo.InvariantCulture), UtcPlusNine);

    [Fact]
    public void UtcValueThreeHoursAhead_IsInFuture()
    {
        var validator = Clocked<Person>(Clock, x => x.Property(person => person.BirthDate).InFuture());

        // 15:00Z is 3h in the future; against the local wall-clock (21:00) it would wrongly read as past.
        var utcFuture = new DateTime(2026, 5, 15, 15, 0, 0, DateTimeKind.Utc);
        Assert.True(validator.Validate(new Person { BirthDate = utcFuture }).IsValid);
    }

    [Fact]
    public void UtcValueThreeHoursAhead_IsNotInPast()
    {
        var validator = Clocked<Person>(Clock, x => x.Property(person => person.BirthDate).InPast());
        var utcFuture = new DateTime(2026, 5, 15, 15, 0, 0, DateTimeKind.Utc);
        Assert.False(validator.Validate(new Person { BirthDate = utcFuture }).IsValid);
    }

    [Fact]
    public void UnspecifiedValue_StillComparesAgainstLocalNow()
    {
        var inPast = Clocked<Person>(Clock, x => x.Property(person => person.BirthDate).InPast());

        // 15:00 unspecified is assumed local; local now is 21:00, so it is in the past.
        var localAfternoon = new DateTime(2026, 5, 15, 15, 0, 0);
        Assert.True(inPast.Validate(new Person { BirthDate = localAfternoon }).IsValid);
    }

    [Fact]
    public void IsToday_UsesTheDateOfTheMatchingFrame()
    {
        // UTC now = 20:00Z on the 15th → local (UTC+9) date is already the 16th.
        var clock = new ZonedTimeProvider(
            DateTimeOffset.Parse("2026-05-15T20:00:00Z", CultureInfo.InvariantCulture), UtcPlusNine);

        var validator = Clocked<Person>(clock, x => x.Property(person => person.BirthDate).IsToday());
        Assert.True(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 15, 23, 0, 0, DateTimeKind.Utc) }).IsValid);
        Assert.True(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 16, 1, 0, 0) }).IsValid);
        Assert.False(validator.Validate(new Person { BirthDate = new DateTime(2026, 5, 16, 1, 0, 0, DateTimeKind.Utc) }).IsValid);
    }

    [Fact]
    public void NullableVariants_ApplyTheSameKindHandling()
    {
        var validator = Clocked<Person>(Clock, x => x.Property(person => person.OptionalDate).InFuture());
        var utcFuture = new DateTime(2026, 5, 15, 15, 0, 0, DateTimeKind.Utc);
        Assert.True(validator.Validate(new Person { OptionalDate = utcFuture }).IsValid);
        Assert.True(validator.Validate(new Person { OptionalDate = null }).IsValid);
    }
}