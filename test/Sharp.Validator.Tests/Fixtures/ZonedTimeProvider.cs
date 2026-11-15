namespace Sharp.Validator.Tests.Fixtures;

// A clock that reads one fixed instant and reports the given local time zone.
internal sealed class ZonedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo zone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => zone;

    public override DateTimeOffset GetUtcNow() => utcNow;
}