namespace Sharp.Validator.Tests.Fixtures;

public sealed class Person
{
    public string? Name { get; init; }

    public int Age { get; init; }

    public int? NullableAge { get; init; }

    public string? Email { get; init; }

    public string? Password { get; init; }

    public string? Phone { get; init; }

    public string? CardNumber { get; init; }

    public decimal Amount { get; init; }

    public decimal? NullableAmount { get; init; }

    public List<string?>? Tags { get; init; }

    public Address? Address { get; init; }

    public DayOfWeek Day { get; init; }

    public DayOfWeek? NullableDay { get; init; }

    public string? Url { get; init; }

    public DateTime BirthDate { get; init; }

    public DateTime? OptionalDate { get; init; }
}

public sealed class Address
{
    public string? Street => null;

    public string? City { get; init; }
}

[Flags]
public enum Permissions
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    // ReSharper disable once UnusedMember.Global — a usual flags enum names its combination.
    All = Read | Write | Execute
}