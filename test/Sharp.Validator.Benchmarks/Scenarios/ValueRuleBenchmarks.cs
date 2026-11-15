using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Isolates the value-type validators that the other scenarios never exercise: Equal (which boxes both operands when no comparer is supplied) and InEnum
/// over a normal enum (Enum.IsDefined boxing) and a flags enum (per-call underlying-values array + element boxing). Sharp-only — this is a before/after
/// harness for the EqualValidator/EnumValidator allocation work, not a cross-library comparison.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class ValueRuleBenchmarks
{
    private readonly SettingsValidator _sharp = new();

    private Settings _settings = null!;

    [Params(Case.Valid, Case.Invalid)]
    public Case Input { get; set; }

    [GlobalSetup]
    public void Setup() => _settings = Data.Settings(Input);

    [Benchmark]
    public int Sharp() => _sharp.Validate(_settings).Errors.Count;
}