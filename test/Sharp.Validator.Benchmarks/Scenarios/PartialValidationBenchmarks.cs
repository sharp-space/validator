using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Partial validation: one validator run whole, and with <c>IncludeProperty</c> selecting one property or one nested path. A call that selects builds
/// its selector's expression tree, which the C# compiler does at the call site, and the context's selection, and every chain then checks its path
/// against the selection. The whole run is the baseline, so Ratio reads as what selecting costs or saves over running every chain.
/// </summary>
[MemoryDiagnoser]
public class PartialValidationBenchmarks
{
    private readonly Validator<Account> _validator = new(x =>
    {
        x.Property(account => account.Email).NotEmpty().MaximumLength(100);
        x.Property(account => account.Name).NotEmpty();
        x.Property(account => account.Phone).NotEmpty();
        x.Property(account => account.Address.Street).NotEmpty();
        x.Property(account => account.Address.City).NotEmpty();
        x.Property(account => account.Address.PostalCode).NotEmpty();
    });

    private Account _model = null!;

    [GlobalSetup]
    public void Setup() => _model = new Account
    {
        Email = "alice@example.com",
        Name = "Alice",
        Phone = "555-0100",
        Address = new Address { Street = "1 Main St", City = "Springfield", PostalCode = "12345" }
    };

    [Benchmark(Baseline = true)]
    public bool Whole() => _validator.Validate(_model).IsValid;

    [Benchmark]
    public bool OneProperty() => _validator.Validate(_model, x => x.IncludeProperty(account => account.Email)).IsValid;

    [Benchmark]
    public bool NestedPath() =>
        _validator.Validate(_model, x => x.IncludeProperty(account => account.Address.City)).IsValid;

    public sealed class Account
    {
        public string? Email { get; init; }

        public string? Name { get; init; }

        public string? Phone { get; init; }

        public Address Address { get; init; } = new();
    }

    public sealed class Address
    {
        public string? Street { get; init; }

        public string? City { get; init; }

        public string? PostalCode { get; init; }
    }
}