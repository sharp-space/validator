using DaTarget = Sharp.Validator.ComparisonBenchmarks.DataAnnotations;

namespace Sharp.Validator.ComparisonBenchmarks;

internal static partial class Data
{
    public static DaTarget.Customer AnnotatedCustomer(Case @case) => @case == Case.Valid
        ? new() { Email = "alice@example.com", Name = "Alice", Age = 30, PostalCode = "12345" }
        : new() { Email = "not-an-email", Name = "A", Age = 5, PostalCode = "abc" };
}