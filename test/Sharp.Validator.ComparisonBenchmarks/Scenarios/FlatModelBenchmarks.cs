using BenchmarkDotNet.Attributes;
using FvTarget = Sharp.Validator.ComparisonBenchmarks.FluentValidation;
using DaTarget = Sharp.Validator.ComparisonBenchmarks.DataAnnotations;

namespace Sharp.Validator.ComparisonBenchmarks.Scenarios;

/// <summary>
/// Flat model with four independent property rules — the common case. The only scenario all three libraries can express identically, so it is the one
/// that includes DataAnnotations. Validators are built once in setup; only the per-call <c>Validate</c> hot path is measured.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class FlatModelBenchmarks
{
    private readonly FvTarget.CustomerValidator _fluent = new();

    private readonly CustomerValidator _sharp = new();
    private DaTarget.Customer _annotated = null!;

    private Customer _poco = null!;

    [Params(Case.Valid, Case.Invalid)]
    public Case Input { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _poco = Data.Customer(Input);
        _annotated = Data.AnnotatedCustomer(Input);
    }

    [Benchmark(Baseline = true)]
    public int Sharp() => _sharp.Validate(_poco).Errors.Count;

    [Benchmark]
    public int FluentValidation() => _fluent.Validate(_poco).Errors.Count;

    [Benchmark]
    public int DataAnnotations() => DaTarget.DataAnnotationsRunner.Validate(_annotated);
}