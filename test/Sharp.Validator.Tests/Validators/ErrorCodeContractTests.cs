using System.Reflection;
using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

// A code's placeholders were filled in some forms of its rule only: {TargetPropertyName} when the target was another
// property, the bound names when a bound was, and the limits of PrecisionScale and of each password requirement never,
// since those rules wrote them into their default messages. A message set for a code then printed a placeholder as
// written in the other forms. Every form of a rule now fills the placeholders of its code, and ErrorCodes lists every
// code the rules report, named after its rule.
public sealed class ErrorCodeContractTests
{
    private static readonly string[] Target = ["{TargetValue}", "{TargetPropertyName}"];
    private static readonly string[] Bounds = ["{From}", "{To}", "{FromPropertyName}", "{ToPropertyName}"];
    private static readonly string[] BothLengths = ["{MinLength}", "{MaxLength}", "{Length}"];
    private static readonly string[] MaxLength = ["{MaxLength}", "{Length}"];
    private static readonly string[] MinLength = ["{MinLength}", "{Length}"];
    private static readonly string[] Pattern = ["{RegularExpression}"];
    private static readonly string[] Precision = ["{IntegerDigits}", "{Scale}", "{ExpectedPrecision}", "{ExpectedScale}"];
    private static readonly string[] Required = ["{RequiredCount}"];
    private static readonly string[] None = [];

    private static readonly DateTime Later = DateTime.UtcNow.AddDays(3);
    private static readonly DateTime Earlier = DateTime.UtcNow.AddDays(-3);
    private static readonly Sample LaterDates = Dates(Later);
    private static readonly Sample EarlierDates = Dates(Earlier);

    private static readonly Case[] Cases =
    [
        new(ErrorCodes.NotNull, None,
            b => b.Property(x => x.Text).NotNull(), new Sample()),
        new(ErrorCodes.Null, None,
            b => b.Property(x => x.Text).Null(), new Sample { Text = "a" }),
        new(ErrorCodes.NotEmpty, None,
            b => b.Property(x => x.Text).NotEmpty(), new Sample { Text = "" }),
        new(ErrorCodes.Empty, None,
            b => b.Property(x => x.Text).Empty(), new Sample { Text = "a" }),

        new(ErrorCodes.Equal, Target,
            b => b.Property(x => x.Number).Equal(1), new Sample { Number = 5 }),
        new(ErrorCodes.Equal, Target,
            b => b.Property(x => x.Number).Equal(x => x.Other), new Sample { Number = 5, Other = 1 }),
        new(ErrorCodes.Equal, Target,
            b => b.Property(x => x.Number).Add(new EqualValidator<Sample, int>(x => x.Other)),
            new Sample { Number = 5, Other = 1 }),
        new(ErrorCodes.NotEqual, Target,
            b => b.Property(x => x.Number).NotEqual(5), new Sample { Number = 5 }),
        new(ErrorCodes.NotEqual, Target,
            b => b.Property(x => x.Number).NotEqual(x => x.Other), new Sample { Number = 5, Other = 5 }),
        new(ErrorCodes.NotEqual, Target,
            b => b.Property(x => x.Number).Add(new NotEqualValidator<Sample, int>(x => x.Other)),
            new Sample { Number = 5, Other = 5 }),

        new(ErrorCodes.GreaterThan, Target,
            b => b.Property(x => x.Number).GreaterThan(10), new Sample { Number = 5 }),
        new(ErrorCodes.GreaterThan, Target,
            b => b.Property(x => x.Number).GreaterThan(x => x.Other), new Sample { Number = 5, Other = 10 }),
        new(ErrorCodes.GreaterThan, Target,
            b => b.Property(x => x.Number).Add(new GreaterThanValidator<Sample, int>(x => x.Other)),
            new Sample { Number = 5, Other = 10 }),
        new(ErrorCodes.GreaterThanOrEqual, Target,
            b => b.Property(x => x.Number).GreaterThanOrEqual(10), new Sample { Number = 5 }),
        new(ErrorCodes.GreaterThanOrEqual, Target,
            b => b.Property(x => x.Number).GreaterThanOrEqual(x => x.Other),
            new Sample { Number = 5, Other = 10 }),
        new(ErrorCodes.GreaterThanOrEqual, Target,
            b => b.Property(x => x.Number).Add(new GreaterThanOrEqualValidator<Sample, int>(x => x.Other)),
            new Sample { Number = 5, Other = 10 }),
        new(ErrorCodes.LessThan, Target,
            b => b.Property(x => x.Number).LessThan(1), new Sample { Number = 5 }),
        new(ErrorCodes.LessThan, Target,
            b => b.Property(x => x.Number).LessThan(x => x.Other), new Sample { Number = 5, Other = 1 }),
        new(ErrorCodes.LessThan, Target,
            b => b.Property(x => x.Number).Add(new LessThanValidator<Sample, int>(x => x.Other)),
            new Sample { Number = 5, Other = 1 }),
        new(ErrorCodes.LessThanOrEqual, Target,
            b => b.Property(x => x.Number).LessThanOrEqual(1), new Sample { Number = 5 }),
        new(ErrorCodes.LessThanOrEqual, Target,
            b => b.Property(x => x.Number).LessThanOrEqual(x => x.Other), new Sample { Number = 5, Other = 1 }),
        new(ErrorCodes.LessThanOrEqual, Target,
            b => b.Property(x => x.Number).Add(new LessThanOrEqualValidator<Sample, int>(x => x.Other)),
            new Sample { Number = 5, Other = 1 }),

        new(ErrorCodes.InclusiveBetween, Bounds,
            b => b.Property(x => x.Number).InclusiveBetween(10, 20), new Sample { Number = 5 }),
        new(ErrorCodes.InclusiveBetween, Bounds,
            b => b.Property(x => x.Number).InclusiveBetween(x => x.Low, x => x.High),
            new Sample { Number = 5, Low = 10, High = 20 }),
        new(ErrorCodes.InclusiveBetween, Bounds,
            b => b.Property(x => x.Number).Add(new InclusiveBetweenValidator<Sample, int>(x => x.Low, x => x.High)),
            new Sample { Number = 5, Low = 10, High = 20 }),
        new(ErrorCodes.ExclusiveBetween, Bounds,
            b => b.Property(x => x.Number).ExclusiveBetween(10, 20), new Sample { Number = 5 }),
        new(ErrorCodes.ExclusiveBetween, Bounds,
            b => b.Property(x => x.Number).ExclusiveBetween(x => x.Low, x => x.High),
            new Sample { Number = 5, Low = 10, High = 20 }),
        new(ErrorCodes.ExclusiveBetween, Bounds,
            b => b.Property(x => x.Number).Add(new ExclusiveBetweenValidator<Sample, int>(x => x.Low, x => x.High)),
            new Sample { Number = 5, Low = 10, High = 20 }),

        new(ErrorCodes.InEnum, None,
            b => b.Property(x => x.Day).InEnum(), new Sample { Day = (DayOfWeek)99 }),
        new(ErrorCodes.InEnum, None,
            b => b.Property(x => x.OptionalDay).InEnum(), new Sample { OptionalDay = (DayOfWeek)99 }),
        new(ErrorCodes.InEnum, None,
            b => b.Property(x => x.Text).InEnum(typeof(DayOfWeek)), new Sample { Text = "Someday" }),

        new(ErrorCodes.Length, BothLengths,
            b => b.Property(x => x.Text).Length(2, 3), new Sample { Text = "a" }),
        new(ErrorCodes.Length, BothLengths,
            b => b.Property(x => x.Text).Length(_ => 2, _ => 3), new Sample { Text = "a" }),
        new(ErrorCodes.ExactLength, BothLengths,
            b => b.Property(x => x.Text).Length(3), new Sample { Text = "a" }),
        new(ErrorCodes.ExactLength, BothLengths,
            b => b.Property(x => x.Text).Length(_ => 3), new Sample { Text = "a" }),
        new(ErrorCodes.MaximumLength, MaxLength,
            b => b.Property(x => x.Text).MaximumLength(1), new Sample { Text = "abc" }),
        new(ErrorCodes.MaximumLength, MaxLength,
            b => b.Property(x => x.Text).MaximumLength(_ => 1), new Sample { Text = "abc" }),
        new(ErrorCodes.MinimumLength, MinLength,
            b => b.Property(x => x.Text).MinimumLength(3), new Sample { Text = "a" }),
        new(ErrorCodes.MinimumLength, MinLength,
            b => b.Property(x => x.Text).MinimumLength(_ => 3), new Sample { Text = "a" }),

        new(ErrorCodes.Regex, Pattern,
            b => b.Property(x => x.Text).Regex("^b$"), new Sample { Text = "a" }),
        new(ErrorCodes.Regex, Pattern,
            b => b.Property(x => x.Text).Regex(new Regex("^b$")), new Sample { Text = "a" }),
        new(ErrorCodes.Regex, Pattern,
            b => b.Property(x => x.Text).Regex(_ => "^b$"), new Sample { Text = "a" }),
        new(ErrorCodes.Regex, Pattern,
            b => b.Property(x => x.Text).Regex(_ => new Regex("^b$")), new Sample { Text = "a" }),
        new(ErrorCodes.Regex, Pattern,
            b => b.Property(x => x.Text).Regex(_ => "["), new Sample { Text = "a" }),
        new(ErrorCodes.Regex, Pattern,
            b => b.Property(x => x.Text).Regex(_ => (string?)null), new Sample { Text = "a" }),

        new(ErrorCodes.Email, None,
            b => b.Property(x => x.Text).Email(), new Sample { Text = "nope" }),
        new(ErrorCodes.CreditCard, None,
            b => b.Property(x => x.Text).CreditCard(), new Sample { Text = "1234" }),
        new(ErrorCodes.PhoneNumber, None,
            b => b.Property(x => x.Text).PhoneNumber(), new Sample { Text = "abc" }),
        new(ErrorCodes.Uri, None,
            b => b.Property(x => x.Text).Uri(), new Sample { Text = "not a uri" }),
        new(ErrorCodes.Url, None,
            b => b.Property(x => x.Text).Url(), new Sample { Text = "ftp://example.com" }),

        new(ErrorCodes.Must, None,
            b => b.Property(x => x.Text).Must(_ => false), new Sample()),
        new(ErrorCodes.Must, None,
            b => b.Property(x => x.Text).Must((_, _) => false), new Sample()),
        new(ErrorCodes.Must, None,
            b => b.Property(x => x.Text).MustAsync(_ => Task.FromResult(false)), new Sample()),
        new(ErrorCodes.Must, None,
            b => b.Property(x => x.Text).MustAsync((_, _) => Task.FromResult(false)), new Sample()),
        new(ErrorCodes.Must, None,
            b => b.Property(x => x.Text).MustAsync((_, _, _) => Task.FromResult(false)), new Sample()),

        new(ErrorCodes.PrecisionScale, Precision,
            b => b.Property(x => x.Amount).PrecisionScale(4, 2), new Sample { Amount = 123.456m }),
        new(ErrorCodes.PrecisionScale, Precision,
            b => b.Property(x => x.OptionalAmount).PrecisionScale(4, 2),
            new Sample { OptionalAmount = 123.456m }),

        new(ErrorCodes.InPast, None,
            b => b.Property(x => x.When).InPast(), LaterDates),
        new(ErrorCodes.InPast, None,
            b => b.Property(x => x.OptionalWhen).InPast(), LaterDates),
        new(ErrorCodes.InPast, None,
            b => b.Property(x => x.At).InPast(), LaterDates),
        new(ErrorCodes.InPast, None,
            b => b.Property(x => x.OptionalAt).InPast(), LaterDates),
        new(ErrorCodes.InPast, None,
            b => b.Property(x => x.Date).InPast(), LaterDates),
        new(ErrorCodes.InPast, None,
            b => b.Property(x => x.OptionalDate).InPast(), LaterDates),
        new(ErrorCodes.InFuture, None,
            b => b.Property(x => x.When).InFuture(), EarlierDates),
        new(ErrorCodes.InFuture, None,
            b => b.Property(x => x.OptionalWhen).InFuture(), EarlierDates),
        new(ErrorCodes.InFuture, None,
            b => b.Property(x => x.At).InFuture(), EarlierDates),
        new(ErrorCodes.InFuture, None,
            b => b.Property(x => x.OptionalAt).InFuture(), EarlierDates),
        new(ErrorCodes.InFuture, None,
            b => b.Property(x => x.Date).InFuture(), EarlierDates),
        new(ErrorCodes.InFuture, None,
            b => b.Property(x => x.OptionalDate).InFuture(), EarlierDates),
        new(ErrorCodes.IsToday, None,
            b => b.Property(x => x.When).IsToday(), EarlierDates),
        new(ErrorCodes.IsToday, None,
            b => b.Property(x => x.OptionalWhen).IsToday(), EarlierDates),
        new(ErrorCodes.IsToday, None,
            b => b.Property(x => x.At).IsToday(), EarlierDates),
        new(ErrorCodes.IsToday, None,
            b => b.Property(x => x.OptionalAt).IsToday(), EarlierDates),
        new(ErrorCodes.IsToday, None,
            b => b.Property(x => x.Date).IsToday(), EarlierDates),
        new(ErrorCodes.IsToday, None,
            b => b.Property(x => x.OptionalDate).IsToday(), EarlierDates),

        new(ErrorCodes.PasswordLength, MinLength,
            b => b.Property(x => x.Text).Password(Only(o => o.RequiredLength = 8)),
            new Sample { Text = "abc" }),
        new(ErrorCodes.PasswordDigits, Required,
            b => b.Property(x => x.Text).Password(Only(o => o.RequiredDigits = 1)), new Sample { Text = "abc" }),
        new(ErrorCodes.PasswordNonAlphanumerics, Required,
            b => b.Property(x => x.Text).Password(Only(o => o.RequiredNonAlphanumerics = 1)),
            new Sample { Text = "abc" }),
        new(ErrorCodes.PasswordUppercaseLetters, Required,
            b => b.Property(x => x.Text).Password(Only(o => o.RequiredUppercaseLetters = 1)),
            new Sample { Text = "abc" }),
        new(ErrorCodes.PasswordLowercaseLetters, Required,
            b => b.Property(x => x.Text).Password(Only(o => o.RequiredLowercaseLetters = 1)),
            new Sample { Text = "ABC" }),
        new(ErrorCodes.PasswordUniqueCharacters, Required,
            b => b.Property(x => x.Text).Password(Only(o => o.RequiredUniqueCharacters = 3)),
            new Sample { Text = "aaa" })
    ];

    public static TheoryData<int> CaseNumbers
    {
        get
        {
            var numbers = new TheoryData<int>();
            for (var number = 0; number < Cases.Length; number++) numbers.Add(number);

            return numbers;
        }
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    // A message that uses every placeholder of the code comes back with none left as written.
    public async Task EveryForm_ReportsItsCodeAndFillsItsPlaceholders(int number)
    {
        var form = Cases[number];
        var validator = new Validator<Sample>(form.Declare);
        validator.Configure(options =>
            options.ErrorMessages[form.Code] = string.Join("|", ["{PropertyName}", "{PropertyValue}", .. form.Placeholders]));

        var error = Assert.Single((await validator.ValidateAsync(form.Model)).Errors);
        Assert.Equal(form.Code, error.Code);
        Assert.False(error.Message!.Contains('{'), $"case {number}: {error.Message}");
    }

    [Fact]
    public void ErrorCodes_ListEveryCodeTheRulesReport()
    {
        var published = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(published, Cases.Select(form => form.Code).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    // ErrorCodes_ListEveryCodeTheRulesReport trusts Cases to build every built-in rule. A rule type no case builds
    // could report a code nothing checks, so every concrete rule type of the library, public or internal, is built by a
    // case. An include reports its child's codes, and a wider rule's adapter the code of the rule it runs.
    public void Cases_BuildEveryBuiltInRuleType()
    {
        var built = Cases.SelectMany(form => RulesOf(new Validator<Sample>(form.Declare)))
            .Select(rule => Definition(rule.GetType())).ToHashSet();

        var ruleTypes = typeof(PropertyValidator).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(PropertyValidator).IsAssignableFrom(type) &&
                !typeof(IIncludeValidator).IsAssignableFrom(type) &&
                type != typeof(WiderRuleValidator<,>) && type != typeof(AsyncWiderRuleValidator<,>)).ToList();

        Assert.True(ruleTypes.Count > 40, $"found only {ruleTypes.Count} rule types");
        Assert.Empty(ruleTypes.Where(type => !built.Contains(type)).Select(type => type.Name));
    }

    [Fact]
    public void ErrorCodes_AreNamedAfterTheirRules() =>
        Assert.All(typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static),
            field => Assert.Equal(field.Name, field.GetRawConstantValue()));

    [Fact]
    public void EachCode_HasOnePlaceholderSet() =>
        Assert.All(Cases.GroupBy(form => form.Code),
            forms => Assert.Single(forms.Select(form => string.Join(",", form.Placeholders)).Distinct()));

    private static IEnumerable<PropertyValidator> RulesOf(Validator<Sample> validator) =>
        validator.Builder.PropertyBuilders.SelectMany(chain => new[]
        {
            chain.PropertyEvaluator?.Validators, chain.ElementsEvaluator?.Validators,
            chain.ElementPropertyEvaluator?.Validators, chain.AsyncEvaluator?.Validators
        }.SelectMany(rules => rules ?? []));

    private static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    // Turns off every password requirement but the one the case sets.
    private static Action<PasswordValidatorOptions> Only(Action<PasswordValidatorOptions> require) => options =>
    {
        options.RequiredLength = 0;
        options.RequiredDigits = 0;
        options.RequiredNonAlphanumerics = 0;
        options.RequiredUppercaseLetters = 0;
        options.RequiredLowercaseLetters = 0;
        options.RequiredUniqueCharacters = 0;
        require(options);
    };

    private static Sample Dates(DateTime moment) => new()
    {
        When = moment,
        OptionalWhen = moment,
        At = new DateTimeOffset(moment),
        OptionalAt = new DateTimeOffset(moment),
        Date = DateOnly.FromDateTime(moment),
        OptionalDate = DateOnly.FromDateTime(moment)
    };

    private sealed class Sample
    {
        public string? Text { get; init; }

        public int Number { get; init; }

        public int Other { get; init; }

        public int Low { get; init; }

        public int High { get; init; }

        public decimal Amount { get; init; }

        public decimal? OptionalAmount { get; init; }

        public DayOfWeek Day { get; init; }

        public DayOfWeek? OptionalDay { get; init; }

        public DateTime When { get; init; }

        public DateTime? OptionalWhen { get; init; }

        public DateTimeOffset At { get; init; }

        public DateTimeOffset? OptionalAt { get; init; }

        public DateOnly Date { get; init; }

        public DateOnly? OptionalDate { get; init; }
    }

    private sealed record Case(string Code, string[] Placeholders, Action<ValidatorBuilder<Sample>> Declare, Sample Model);
}