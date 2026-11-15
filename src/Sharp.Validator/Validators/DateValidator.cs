namespace Sharp.Validator.Validators;

// The date rules read "now" from the clock of the validator that freezes them: ValidatorBuilderOptions.TimeProvider
// from the most specific level that sets it, or TimeProvider.System.
internal abstract class ClockedValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    private protected TimeProvider Clock { get; private set; } = TimeProvider.System;

    private protected override void FreezeSettings(
        in ValidatorBuilderOptionsSnapshot builderOptions,
        in ValidatorMessageOptions messages
    ) => Clock = messages.TimeProvider;
}

/// <summary>Value must be strictly before "now". UTC values compare against UTC now; Local/Unspecified against local time.</summary>
internal sealed class InPastValidator<TModel> : ClockedValidator<TModel, DateTime>
{
    public InPastValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InPast;
        options.ErrorMessage = "'{PropertyName}' must be in the past.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTime value, ValidationContext<TModel> context) =>
        value < DateNow.Matching(Clock, value.Kind);
}

/// <summary>Value must be strictly after "now". UTC values compare against UTC now; Local/Unspecified against local time.</summary>
internal sealed class InFutureValidator<TModel> : ClockedValidator<TModel, DateTime>
{
    public InFutureValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InFuture;
        options.ErrorMessage = "'{PropertyName}' must be in the future.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTime value, ValidationContext<TModel> context) =>
        value > DateNow.Matching(Clock, value.Kind);
}

/// <summary>Date portion must equal "today". UTC values compare against the UTC date; Local/Unspecified against the local date.</summary>
internal sealed class IsTodayValidator<TModel> : ClockedValidator<TModel, DateTime>
{
    public IsTodayValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.IsToday;
        options.ErrorMessage = "'{PropertyName}' must be today's date.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTime value, ValidationContext<TModel> context) =>
        value.Date == DateNow.Matching(Clock, value.Kind).Date;
}

/// <summary>Nullable variant of <see cref="InPastValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableInPastValidator<TModel> : ClockedValidator<TModel, DateTime?>
{
    public NullableInPastValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InPast;
        options.ErrorMessage = "'{PropertyName}' must be in the past.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTime? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value < DateNow.Matching(Clock, value.Value.Kind);
}

/// <summary>Nullable variant of <see cref="InFutureValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableInFutureValidator<TModel> : ClockedValidator<TModel, DateTime?>
{
    public NullableInFutureValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InFuture;
        options.ErrorMessage = "'{PropertyName}' must be in the future.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTime? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value > DateNow.Matching(Clock, value.Value.Kind);
}

/// <summary>Nullable variant of <see cref="IsTodayValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableIsTodayValidator<TModel> : ClockedValidator<TModel, DateTime?>
{
    public NullableIsTodayValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.IsToday;
        options.ErrorMessage = "'{PropertyName}' must be today's date.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTime? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value.Date == DateNow.Matching(Clock, value.Value.Kind).Date;
}

/// <summary>Value must be strictly before "now", compared as an instant.</summary>
internal sealed class DateTimeOffsetInPastValidator<TModel> : ClockedValidator<TModel, DateTimeOffset>
{
    public DateTimeOffsetInPastValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InPast;
        options.ErrorMessage = "'{PropertyName}' must be in the past.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTimeOffset value, ValidationContext<TModel> context) =>
        value < Clock.GetUtcNow();
}

/// <summary>Value must be strictly after "now", compared as an instant.</summary>
internal sealed class DateTimeOffsetInFutureValidator<TModel> : ClockedValidator<TModel, DateTimeOffset>
{
    public DateTimeOffsetInFutureValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InFuture;
        options.ErrorMessage = "'{PropertyName}' must be in the future.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTimeOffset value, ValidationContext<TModel> context) =>
        value > Clock.GetUtcNow();
}

/// <summary>Date must equal "today" by the calendar of the value's own offset.</summary>
internal sealed class DateTimeOffsetIsTodayValidator<TModel> : ClockedValidator<TModel, DateTimeOffset>
{
    public DateTimeOffsetIsTodayValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.IsToday;
        options.ErrorMessage = "'{PropertyName}' must be today's date.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTimeOffset value, ValidationContext<TModel> context) =>
        value.Date == Clock.GetUtcNow().ToOffset(value.Offset).Date;
}

/// <summary>Nullable variant of <see cref="DateTimeOffsetInPastValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableDateTimeOffsetInPastValidator<TModel> : ClockedValidator<TModel, DateTimeOffset?>
{
    public NullableDateTimeOffsetInPastValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InPast;
        options.ErrorMessage = "'{PropertyName}' must be in the past.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTimeOffset? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value < Clock.GetUtcNow();
}

/// <summary>Nullable variant of <see cref="DateTimeOffsetInFutureValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableDateTimeOffsetInFutureValidator<TModel> : ClockedValidator<TModel, DateTimeOffset?>
{
    public NullableDateTimeOffsetInFutureValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InFuture;
        options.ErrorMessage = "'{PropertyName}' must be in the future.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTimeOffset? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value > Clock.GetUtcNow();
}

/// <summary>Nullable variant of <see cref="DateTimeOffsetIsTodayValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableDateTimeOffsetIsTodayValidator<TModel> : ClockedValidator<TModel, DateTimeOffset?>
{
    public NullableDateTimeOffsetIsTodayValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.IsToday;
        options.ErrorMessage = "'{PropertyName}' must be today's date.";
    });

    /// <inheritdoc />
    public override bool Validate(DateTimeOffset? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value.Date == Clock.GetUtcNow().ToOffset(value.Value.Offset).Date;
}

/// <summary>Date must be before today's local date, which an Unspecified <see cref="DateTime" /> is read against too.</summary>
internal sealed class DateOnlyInPastValidator<TModel> : ClockedValidator<TModel, DateOnly>
{
    public DateOnlyInPastValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InPast;
        options.ErrorMessage = "'{PropertyName}' must be in the past.";
    });

    /// <inheritdoc />
    public override bool Validate(DateOnly value, ValidationContext<TModel> context) =>
        value < DateNow.LocalDate(Clock);
}

/// <summary>Date must be after today's local date, which an Unspecified <see cref="DateTime" /> is read against too.</summary>
internal sealed class DateOnlyInFutureValidator<TModel> : ClockedValidator<TModel, DateOnly>
{
    public DateOnlyInFutureValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InFuture;
        options.ErrorMessage = "'{PropertyName}' must be in the future.";
    });

    /// <inheritdoc />
    public override bool Validate(DateOnly value, ValidationContext<TModel> context) =>
        value > DateNow.LocalDate(Clock);
}

/// <summary>Date must equal today's local date, which an Unspecified <see cref="DateTime" /> is read against too.</summary>
internal sealed class DateOnlyIsTodayValidator<TModel> : ClockedValidator<TModel, DateOnly>
{
    public DateOnlyIsTodayValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.IsToday;
        options.ErrorMessage = "'{PropertyName}' must be today's date.";
    });

    /// <inheritdoc />
    public override bool Validate(DateOnly value, ValidationContext<TModel> context) =>
        value == DateNow.LocalDate(Clock);
}

/// <summary>Nullable variant of <see cref="DateOnlyInPastValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableDateOnlyInPastValidator<TModel> : ClockedValidator<TModel, DateOnly?>
{
    public NullableDateOnlyInPastValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InPast;
        options.ErrorMessage = "'{PropertyName}' must be in the past.";
    });

    /// <inheritdoc />
    public override bool Validate(DateOnly? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value < DateNow.LocalDate(Clock);
}

/// <summary>Nullable variant of <see cref="DateOnlyInFutureValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableDateOnlyInFutureValidator<TModel> : ClockedValidator<TModel, DateOnly?>
{
    public NullableDateOnlyInFutureValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.InFuture;
        options.ErrorMessage = "'{PropertyName}' must be in the future.";
    });

    /// <inheritdoc />
    public override bool Validate(DateOnly? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value > DateNow.LocalDate(Clock);
}

/// <summary>Nullable variant of <see cref="DateOnlyIsTodayValidator{TModel}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableDateOnlyIsTodayValidator<TModel> : ClockedValidator<TModel, DateOnly?>
{
    public NullableDateOnlyIsTodayValidator() => SetDefaults(options =>
    {
        options.ErrorCode = ErrorCodes.IsToday;
        options.ErrorMessage = "'{PropertyName}' must be today's date.";
    });

    /// <inheritdoc />
    public override bool Validate(DateOnly? value, ValidationContext<TModel> context) =>
        !value.HasValue || value.Value == DateNow.LocalDate(Clock);
}

file static class DateNow
{
    /// <summary>
    /// "Now" in the value's frame of reference. A UTC value compared against local wall-clock time (or vice versa) would skew the verdict by the server's
    /// UTC offset — the most common storage convention (UTC in the database, local server timezone) would misjudge values within that window. Unspecified is
    /// assumed local, matching <see cref="DateTime.ToUniversalTime" />'s convention.
    /// </summary>
    internal static DateTime Matching(TimeProvider timeProvider, DateTimeKind kind) =>
        kind == DateTimeKind.Utc ? timeProvider.GetUtcNow().UtcDateTime : timeProvider.GetLocalNow().DateTime;

    /// <summary>
    /// Today's date in the local time zone: a <see cref="DateOnly" /> has no zone, so it reads like an Unspecified <see cref="DateTime" />.
    /// </summary>
    internal static DateOnly LocalDate(TimeProvider timeProvider) => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
}