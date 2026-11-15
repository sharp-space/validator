using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// One shared validator called from several threads, the shape of a DI singleton under bulk load. Regex keeps one idle runner per instance, so
/// single-threaded ns/op says nothing about how a regex rule scales: here ns/op is wall time per validation across all threads, and it should fall as
/// threads are added. Sharp-only, and the only scenario that shows the difference between a pattern the validator owns and a shared [GeneratedRegex].
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public partial class ParallelThroughputBenchmarks
{
    private const int Operations = 400_000;

    private readonly Validator<Customer> _email =
        new(builder => builder.Property(x => x.Email).Email());

    private readonly CustomerValidator _flat = new();

    private readonly Customer _model = Data.Customer(Case.Valid);

    private readonly Validator<Customer> _ownedPattern =
        new(builder => builder.Property(x => x.PostalCode).Regex(@"^\d{5}$"));

    private readonly Validator<Customer> _sharedPattern =
        new(builder => builder.Property(x => x.PostalCode).Regex(PostalCode()));

    [Params(1, 2, 4, 8)]
    public int Threads { get; set; }

    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex PostalCode();

    private void Run(Validator<Customer> validator)
    {
        var model = _model;
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            var remaining = Operations / Threads;
            while (remaining-- > 0) validator.Validate(model);
        });
    }

    [Benchmark(OperationsPerInvoke = Operations)]
    public void Flat() => Run(_flat);

    [Benchmark(OperationsPerInvoke = Operations)]
    public void OwnedPattern() => Run(_ownedPattern);

    [Benchmark(OperationsPerInvoke = Operations)]
    public void SharedGeneratedRegex() => Run(_sharedPattern);

    [Benchmark(OperationsPerInvoke = Operations)]
    public void Email() => Run(_email);
}