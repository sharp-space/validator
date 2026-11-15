// Linked into both benchmark projects under the consuming project's namespace.

#if COMPARISON_BENCHMARKS
namespace Sharp.Validator.ComparisonBenchmarks;
#else
namespace Sharp.Validator.Benchmarks;
#endif

/// <summary>Whether the benchmarked input satisfies every rule or fails them.</summary>
public enum Case
{
    /// <summary>Every rule passes — the common success path (no error allocation).</summary>
    Valid,

    /// <summary>Every rule fails — exercises each library's error-collection path.</summary>
    Invalid
}

/// <summary>Builds the sample models. The data is identical across libraries; only the rule declaration differs.</summary>
// ReSharper disable once PartialTypeWithSinglePart — the comparison suite adds its own part in AnnotatedData.cs.
internal static partial class Data
{
    private const int TagCount = 20;

    public static Customer Customer(Case @case) => @case == Case.Valid
        ? new() { Email = "alice@example.com", Name = "Alice", Age = 30, PostalCode = "12345" }
        : new() { Email = "not-an-email", Name = "A", Age = 5, PostalCode = "abc" };

    // Valid: every tag is non-empty and within length. Invalid: half empty (fail NotEmpty), half
    // overlong (fail MaximumLength).
    // ReSharper disable once UnusedMember.Global — only the comparison suite, which compiles this file too, uses it.
    public static TagBag TagBag(Case @case) => new()
    {
        Tags = @case == Case.Valid
            ? [.. Enumerable.Range(0, TagCount).Select(x => $"tag{x}")]
            : [.. Enumerable.Range(0, TagCount).Select(x => x % 2 == 0 ? "" : new string('x', 20))]
    };

    // ReSharper disable once UnusedMember.Global — only the comparison suite, which compiles this file too, uses it.
    public static Order Order(Case @case) => @case == Case.Valid
        ? new()
        {
            Id = "ORD-1",
            Customer = Customer(Case.Valid),
            ShippingAddress = new() { Street = "1 Main St", City = "Springfield", PostalCode = "12345" }
        }
        : new()
        {
            Id = "",
            Customer = Customer(Case.Invalid),
            ShippingAddress = new() { Street = "", City = "", PostalCode = "abc" }
        };

    // ReSharper disable once UnusedMember.Global — only the comparison suite, which compiles this file too, uses it.
    public static Account Account(Case @case) => @case == Case.Valid
        ? new() { Username = "alice", Email = "alice@example.com" }
        : new() { Username = "ab", Email = "bad" };

    // Valid: Level==5, defined Status, valid flags combo. Invalid: all three out of range.
    // ReSharper disable once UnusedMember.Global — only this suite uses it; the comparison suite compiles the file too.
    public static Settings Settings(Case @case) => @case == Case.Valid
        ? new() { Level = 5, Status = Status.Active, Access = Access.Read | Access.Write }
        : new() { Level = 3, Status = (Status)99, Access = (Access)64 };
}