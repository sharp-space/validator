using Microsoft.Extensions.DependencyInjection;
using Sharp.Validator.Bindings;

namespace Sharp.Validator.Tests.Validators;

[Collection(GlobalOptions.Name)]
// Each date rule took its own TimeProvider argument, before its configure, so a validator with several date rules
// repeated the clock and an app had no way to set one for all of them. The clock is a validator option now, read when
// the validator freezes from its Configure, its AddValidators scan, or Global, the most specific first; an included
// validator reads its own.
public sealed class DateClockTests
{
    private static readonly DateTimeOffset Year2000 = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Year2001 = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Booked for 2001: in the future by a clock in 2000, in the past by the real one.
    private static readonly Booking In2001 = new() { At = Year2001, Next = new Booking { At = Year2001 } };

    [Fact]
    public void ValidatorClock_ServesEveryDateRule()
    {
        var validator = new Validator<Booking>(x =>
        {
            x.Property(booking => booking.At).InFuture();
            x.Property(booking => booking.At).IsToday();
        });

        validator.Configure(options => options.TimeProvider = new Fixed(Year2001));
        var error = Assert.Single(validator.Validate(In2001).Errors);
        Assert.Equal(ErrorCodes.InFuture, error.Code);
    }

    [Fact]
    public void ScanClock_ServesTheValidatorsItCreates()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ValidatorRegistry.CreateDescriptor(typeof(IValidator<Booking>), typeof(ScannedValidator),
            ServiceLifetime.Singleton, new ValidatorBuilderOptions { TimeProvider = new Fixed(Year2000) }));

        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IValidator<Booking>>().Validate(In2001).IsValid);
    }

    [Fact]
    public void GlobalClock_GivesWayToTheValidatorsOwn()
    {
        var snapshot = ValidatorBuilderOptions.Global;

        try
        {
            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions { TimeProvider = new Fixed(Year2000) };
            var byGlobal = new Validator<Booking>(x => x.Property(booking => booking.At).InFuture());
            var byOwn = new Validator<Booking>(x => x.Property(booking => booking.At).InFuture());
            byOwn.Configure(options => options.TimeProvider = TimeProvider.System);

            Assert.True(byGlobal.Validate(In2001).IsValid);
            Assert.False(byOwn.Validate(In2001).IsValid);
        }
        finally
        {
            ValidatorBuilderOptions.Global = snapshot;
        }
    }

    [Fact]
    public void IncludedValidator_ReadsItsOwnClock()
    {
        var child = new Validator<Booking>(x => x.Property(booking => booking.At).InFuture());
        var parent = new Validator<Booking>(x => x.Property(booking => booking.Next).Include(child));
        parent.Configure(options => options.TimeProvider = new Fixed(Year2000));
        var error = Assert.Single(parent.Validate(In2001).Errors);
        Assert.Equal("Next.At", error.PropertyName);
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Booking
    {
        public DateTimeOffset At { get; init; }

        public Booking? Next { get; init; }
    }

    private sealed class ScannedValidator : Validator<Booking>
    {
        public ScannedValidator() => Builder.Property(x => x.At).InFuture();
    }
}