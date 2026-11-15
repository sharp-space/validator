using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Isolates the synchronous fast-path of <c>ValidateAsync</c>. A validator with only synchronous rules invoked through <c>ValidateAsync</c> should
/// bypass the async state machine and complete inline; the gap between <c>Validate</c> and <c>ValidateAsync</c> on the same sync-only validator is the
/// async-machinery overhead. Default job (not ShortRun) because the effect is single-digit ns and needs tight error bars.
/// </summary>
[MemoryDiagnoser]
public class AsyncFastPathBenchmarks
{
    private readonly CustomerValidator _sync = new(); // sync-only rules => IsAsync == false

    private Customer _model = null!;

    [GlobalSetup]
    public void Setup() => _model = Data.Customer(Case.Valid);

    [Benchmark(Baseline = true)]
    public int Validate_Sync() => _sync.Validate(_model).Errors.Count;

    [Benchmark]
    // Sync-only validator through ValidateAsync: this is the path the synchronous fast-path optimizes.
    public int ValidateAsync_SyncOnly() => _sync.ValidateAsync(_model).GetAwaiter().GetResult().Errors.Count;
}