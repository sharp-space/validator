namespace Sharp.Validator.Tests.Validators;

public sealed class BuiltInRuleTests
{
    [Fact]
    public void ExtremeUniqueCharacterRequirements_UseDistinctCharactersNotLength()
    {
        var rule = new PasswordUniqueCharacterCountValidator<Model>(129);
        var context = new ValidationContext<Model>(new());
        Assert.True(new PasswordUniqueCharacterCountValidator<Model>(0).Validate("x", context));
        Assert.False(rule.Validate(new string('a', 130), context));
        Assert.True(rule.Validate("aa" + new string(Enumerable.Range(1, 129).Select(x => (char)x).ToArray()), context));
        Assert.True(rule.Validate(null, context));
    }

    [Fact]
    public void NullableDateRules_CheckBothSidesOfTheBoundary()
    {
        var future = DateTimeOffset.UtcNow.AddYears(20);
        var past = future.AddYears(-40);
        var validator = new Validator<Model>(builder =>
        {
            builder.Property(x => x.Date).InFuture();
            builder.Property(x => x.Date).IsToday();
            builder.Property(x => x.OptionalDate).InPast();
            builder.Property(x => x.OptionalDate).InFuture();
            builder.Property(x => x.OptionalDate).IsToday();
            builder.Property(x => x.Offset).InPast();
            builder.Property(x => x.Offset).InFuture();
            builder.Property(x => x.Offset).IsToday();
            builder.Property(x => x.OptionalOffset).InPast();
            builder.Property(x => x.OptionalOffset).InFuture();
            builder.Property(x => x.OptionalOffset).IsToday();
            builder.Property(x => x.Day).InPast();
            builder.Property(x => x.Day).InFuture();
            builder.Property(x => x.Day).IsToday();
            builder.Property(x => x.OptionalDay).InPast();
            builder.Property(x => x.OptionalDay).InFuture();
            builder.Property(x => x.OptionalDay).IsToday();
        });

        foreach (var instant in new[] { past, future, DateTimeOffset.Now })
        {
            var model = new Model
            {
                Date = instant.LocalDateTime, OptionalDate = instant.LocalDateTime,
                Offset = instant, OptionalOffset = instant,
                Day = DateOnly.FromDateTime(instant.LocalDateTime), OptionalDay = DateOnly.FromDateTime(instant.LocalDateTime)
            };

            Assert.False(validator.Validate(model).IsValid);
        }
    }

    [Fact]
    public void FixedPrecisionMessages_DoNotNeedRuntimeParameters()
    {
        var validator = new Validator<Model>(b =>
        {
            b.Property(x => x.Amount).PrecisionScale(2, 1, o => o.ErrorMessage = "invalid");
            b.Property(x => x.OptionalAmount).PrecisionScale(2, 1, o => o.ErrorMessage = "invalid");
        });

        Assert.All(validator.Validate(new Model { Amount = 123m, OptionalAmount = 123m }).Errors,
            error => Assert.Equal("invalid", error.Message));
    }

    [Fact]
    public void Uri_WithAnEmptySchemeWhitelist_AcceptsAnyAbsoluteScheme()
    {
        var validator = new Validator<Model>(b => b.Property(x => x.Text).Uri(UriKind.Absolute, []));
        Assert.True(validator.Validate(new Model { Text = "custom://host/path" }).IsValid);
    }

    [Fact]
    public void RuleConstructors_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new GreaterThanValidator<Model, string>((string)null!));
        Assert.Throws<ArgumentNullException>(() => new GreaterThanValidator<Model, string>(null!, "Target"));
        Assert.Throws<ArgumentNullException>(() => new EqualValidator<Model, string>(null!, "Target"));
        Assert.Throws<ArgumentNullException>(() => new NotEqualValidator<Model, string>(null!, "Target"));
        Assert.Throws<ArgumentNullException>(() => new NotEqualValidator<Model, string>((Func<Model, string>)null!));
        Assert.Throws<ArgumentNullException>(() => new LengthValidator<Model>(_ => 0, null!));
        Assert.Throws<ArgumentNullException>(() => new MinimumLengthValidator<Model>(null!));
        Assert.Throws<ArgumentNullException>(() => new MaximumLengthValidator<Model>(null!));
        Assert.Throws<ArgumentNullException>(() => new InclusiveBetweenValidator<Model, string>(null!, "z"));
        Assert.Throws<ArgumentNullException>(() => new InclusiveBetweenValidator<Model, string>("a", null!));
        Assert.Throws<ArgumentNullException>(() => new InclusiveBetweenValidator<Model, string>(null!, "a", _ => (true, "z"), "z"));
        Assert.Throws<ArgumentNullException>(() => new InclusiveBetweenValidator<Model, string>(_ => (true, "a"), "a", null!, "z"));
    }

    [Fact]
    public void ComparisonAndRangeRules_NullValueOrUnresolvedTarget_Passes()
    {
        var context = new ValidationContext<Model>(new());
        Assert.True(new GreaterThanValidator<Model, string>("a").Validate(null!, context));

        Assert.True(new GreaterThanValidator<Model, string>(_ =>
            (false, "a"), "Target").Validate("z", context));

        Assert.True(new NotEqualValidator<Model, string>(_ =>
            (false, "a"), "Target").Validate("a", context));

        Assert.True(new InclusiveBetweenValidator<Model, string>("a", "z").Validate(null!, context));

        Assert.True(new InclusiveBetweenValidator<Model, string>(_ =>
            (true, null!), null, _ => (true, "z"), null).Validate("m", context));

        Assert.True(new InclusiveBetweenValidator<Model, string>(_ =>
            (true, "a"), null, _ => (true, null!), null).Validate("m", context));

        Assert.True(new NotEqualValidator<Model, string>(_ =>
            "other").Validate("value", context));
    }

    private sealed class Model
    {
        public string? Text { get; init; }

        public decimal Amount { get; init; }

        public decimal? OptionalAmount { get; init; }

        public DateTime Date { get; init; }

        public DateTime? OptionalDate { get; init; }

        public DateTimeOffset Offset { get; init; }

        public DateTimeOffset? OptionalOffset { get; init; }

        public DateOnly Day { get; init; }

        public DateOnly? OptionalDay { get; init; }
    }
}