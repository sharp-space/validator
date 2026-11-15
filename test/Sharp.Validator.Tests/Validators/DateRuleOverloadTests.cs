using System.Globalization;
using static Sharp.Validator.Tests.Fixtures.ClockedValidators;

namespace Sharp.Validator.Tests.Validators;

// InPast, InFuture, and IsToday took DateTime only. They now take DateTimeOffset, which compares as an instant and
// reads "today" in the value's own offset, and DateOnly, which compares by calendar date with today's local date, as an
// Unspecified DateTime is read as local. Each has a nullable form that skips null.
public sealed class DateRuleOverloadTests
{
    private static readonly TimeZoneInfo UtcPlusNine =
        TimeZoneInfo.CreateCustomTimeZone("UTC+9-date-overloads", TimeSpan.FromHours(9), "UTC+9", "UTC+9");

    // 20:00Z is 05:00 the next morning in UTC+9, so the UTC date is May 15 and the local date May 16.
    private static readonly ZonedTimeProvider Clock = new(new DateTimeOffset(2026, 5, 15, 20, 0, 0, TimeSpan.Zero), UtcPlusNine);

    [Theory]
    [InlineData("2026-05-15T19:59:59+00:00", true, false)]
    [InlineData("2026-05-16T04:59:59+09:00", true, false)]
    [InlineData("2026-05-15T20:00:01+00:00", false, true)]
    [InlineData("2026-05-15T20:00:00+00:00", false, false)]
    public void DateTimeOffset_ComparesAsAnInstant(string at, bool past, bool future)
    {
        var model = new Event { At = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) };
        Assert.Equal(past, IsValid(x => x.Property(e => e.At).InPast(), model));
        Assert.Equal(future, IsValid(x => x.Property(e => e.At).InFuture(), model));
    }

    [Theory]
    [InlineData("2026-05-15T00:00:00+00:00", true)]
    [InlineData("2026-05-16T00:30:00+09:00", true)]
    [InlineData("2026-05-15T23:00:00+09:00", false)]
    [InlineData("2026-05-16T00:30:00+00:00", false)]
    public void DateTimeOffset_IsTodayByTheCalendarOfItsOwnOffset(string at, bool today)
    {
        var model = new Event { At = DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) };
        Assert.Equal(today, IsValid(x => x.Property(e => e.At).IsToday(), model));
    }

    [Theory]
    [InlineData("2026-05-15", true, false, false)]
    [InlineData("2026-05-16", false, false, true)]
    [InlineData("2026-05-17", false, true, false)]
    public void DateOnly_ComparesWithTheLocalDate(string on, bool past, bool future, bool today)
    {
        var model = new Event { On = DateOnly.Parse(on, CultureInfo.InvariantCulture) };
        Assert.Equal(past, IsValid(x => x.Property(e => e.On).InPast(), model));
        Assert.Equal(future, IsValid(x => x.Property(e => e.On).InFuture(), model));
        Assert.Equal(today, IsValid(x => x.Property(e => e.On).IsToday(), model));
    }

    [Fact]
    public void NullableForms_SkipNull() =>
        Assert.True(IsValid(x =>
        {
            x.Property(e => e.MaybeAt).InPast().InFuture().IsToday();
            x.Property(e => e.MaybeOn).InPast().InFuture().IsToday();
        }, new Event()));

    [Fact]
    public void NullableForms_CheckAValue()
    {
        var validator = Clocked<Event>(Clock, x =>
        {
            x.Property(e => e.MaybeAt).InPast();
            x.Property(e => e.MaybeOn).InPast();
        });

        var result = validator.Validate(new Event { MaybeAt = Clock.GetUtcNow().AddDays(1), MaybeOn = new DateOnly(2026, 5, 17) });
        Assert.Equal(["MaybeAt", "MaybeOn"], result.Errors.Select(error => error.PropertyName));
    }

    private ValidationResult FailingDateResult()
    {
        var validator = Clocked<Event>(Clock, x =>
        {
            x.Property(e => e.At).InFuture();
            x.Property(e => e.On).IsToday();
            x.Property(e => e.MaybeOn).InPast();
        });

        return validator.Validate(new Event
        {
            At = Clock.GetUtcNow().AddDays(-1), On = new DateOnly(2026, 5, 1), MaybeOn = new DateOnly(2026, 6, 1)
        });
    }

    [Fact]
    public void DateRules_Failures_ReportTheDocumentedMessages() =>
        Assert.Equal(
            ["'At' must be in the future.", "'On' must be today's date.", "'MaybeOn' must be in the past."],
            FailingDateResult().Errors.Select(error => error.Message));

    [Fact]
    public void DateRules_Failures_ReportTheDocumentedCodes() =>
        Assert.Equal(["InFuture", "IsToday", "InPast"], FailingDateResult().Errors.Select(error => error.Code));

    [Fact]
    public void UnsetDate_InPast_Passes() => Assert.True(IsValid(x => x.Property(e => e.On).InPast(), new Event()));

    [Fact]
    public void UnsetDate_NotEmptyThenInPast_Fails()
    {
        Assert.False(IsValid(x => x.Property(e => e.On).NotEmpty().InPast(), new Event()));
        Assert.False(IsValid(x => x.Property(e => e.At).NotEmpty().InPast(), new Event()));
    }

    private static bool IsValid(Action<ValidatorBuilder<Event>> rules, Event model) => Clocked(Clock, rules).Validate(model).IsValid;

    private sealed class Event
    {
        public DateTimeOffset At { get; init; }

        public DateTimeOffset? MaybeAt { get; init; }

        public DateOnly On { get; init; }

        public DateOnly? MaybeOn { get; init; }
    }
}