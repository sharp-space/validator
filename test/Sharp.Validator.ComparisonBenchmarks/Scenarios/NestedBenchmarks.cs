using BenchmarkDotNet.Attributes;
using FvTarget = Sharp.Validator.ComparisonBenchmarks.FluentValidation;

namespace Sharp.Validator.ComparisonBenchmarks.Scenarios;

/// <summary>
/// Validates an object graph that composes two child validators (Sharp <c>Include</c> vs FluentValidation <c>SetValidator</c>). DataAnnotations is
/// omitted: <c>Validator.TryValidateObject</c> does not recurse into complex properties, so a nested graph cannot be validated through the same API.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class NestedBenchmarks
{
    private readonly FvTarget.OrderValidator _fluent = new();

    private readonly OrderValidator _sharp = new();

    private Order _order = null!;

    [Params(Case.Valid, Case.Invalid)]
    public Case Input { get; set; }

    [GlobalSetup]
    public void Setup() => _order = Data.Order(Input);

    [Benchmark(Baseline = true)]
    public int Sharp() => _sharp.Validate(_order).Errors.Count;

    [Benchmark]
    public int FluentValidation() => _fluent.Validate(_order).Errors.Count;
}