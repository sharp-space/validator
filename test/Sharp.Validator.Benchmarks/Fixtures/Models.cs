// Linked into both benchmark projects under the consuming project's namespace, so the namespace is not the folder's.

#if COMPARISON_BENCHMARKS
namespace Sharp.Validator.ComparisonBenchmarks;
#else
namespace Sharp.Validator.Benchmarks;
#endif

// Plain POCOs shared by the Sharp.Validator and FluentValidation rule sets. The data is identical across libraries for
// every scenario; only the rule declaration differs. DataAnnotations needs its attributes on the type itself, so it
// keeps a separate annotated copy in the comparison project's Fixtures/.

/// <summary>Flat model — a handful of independent property rules.</summary>
public sealed class Customer
{
    public string Email { get; init; } = "";

    public string Name { get; init; } = "";

    public int Age { get; init; }

    public string PostalCode { get; init; } = "";
}

/// <summary>Collection model — the same rule applied to every element of a list.</summary>
public sealed class TagBag
{
    // Element type is nullable so the per-element string rules (defined over string?) bind without a variance warning.
    public List<string?> Tags { get; init; } = [];
}

/// <summary>Child node of the nested object graph.</summary>
public sealed class Address
{
    public string Street { get; init; } = "";

    public string City { get; init; } = "";

    public string PostalCode { get; init; } = "";
}

/// <summary>Nested model — composes two child validators over an object graph.</summary>
public sealed class Order
{
    public string Id { get; init; } = "";

    public Customer Customer { get; init; } = new();

    public Address ShippingAddress { get; init; } = new();
}

/// <summary>Async model — one rule resolves through an awaited predicate.</summary>
public sealed class Account
{
    public string Username { get; init; } = "";

    public string Email { get; init; } = "";
}

public enum Status
{
    Active,
    // ReSharper disable once UnusedMember.Global — a member InEnum checks values against.
    Inactive,
    // ReSharper disable once UnusedMember.Global — a member InEnum checks values against.
    Pending
}

[Flags]
public enum Access
{
    // ReSharper disable once UnusedMember.Global — a member InEnum checks values against.
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    // ReSharper disable once UnusedMember.Global — a member InEnum checks values against.
    All = Read | Write | Execute
}

/// <summary>Value-rule model — exercises Equal (value-type boxing) and InEnum (normal + flags).</summary>
public sealed class Settings
{
    public int Level { get; init; }

    public Status Status { get; init; }

    public Access Access { get; init; }
}