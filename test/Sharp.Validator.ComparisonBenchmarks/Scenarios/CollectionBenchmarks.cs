using BenchmarkDotNet.Attributes;
using FvTarget = Sharp.Validator.ComparisonBenchmarks.FluentValidation;

namespace Sharp.Validator.ComparisonBenchmarks.Scenarios;

/// <summary>
/// Applies a per-element rule to a 20-item list (Sharp <c>Elements</c> vs FluentValidation <c>RuleForEach</c>). DataAnnotations is omitted:
/// <c>Validator.TryValidateObject</c> validates the collection property itself, not each element, so it has no equivalent to benchmark.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class CollectionBenchmarks
{
    private readonly FvTarget.TagBagValidator _fluent = new();

    private readonly TagBagValidator _sharp = new();

    private TagBag _bag = null!;

    [Params(Case.Valid, Case.Invalid)]
    public Case Input { get; set; }

    [GlobalSetup]
    public void Setup() => _bag = Data.TagBag(Input);

    [Benchmark(Baseline = true)]
    public int Sharp() => _sharp.Validate(_bag).Errors.Count;

    [Benchmark]
    public int FluentValidation() => _fluent.Validate(_bag).Errors.Count;
}