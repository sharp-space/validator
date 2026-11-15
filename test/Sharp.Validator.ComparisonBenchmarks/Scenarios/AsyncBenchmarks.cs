using BenchmarkDotNet.Attributes;
using FvTarget = Sharp.Validator.ComparisonBenchmarks.FluentValidation;

namespace Sharp.Validator.ComparisonBenchmarks.Scenarios;

/// <summary>
/// Drives the asynchronous pipeline: one rule resolves through an awaited (synchronously completing) predicate. Sharp returns
/// <see cref="ValueTask{T}" />, FluentValidation returns <see cref="System.Threading.Tasks.Task{T}" />, so this captures the async-dispatch overhead and
/// the allocation difference between the two return types. DataAnnotations is omitted: it has no asynchronous validation API.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class AsyncBenchmarks
{
    private readonly FvTarget.AccountValidator _fluent = new();

    private readonly AccountValidator _sharp = new();

    private Account _account = null!;

    [Params(Case.Valid, Case.Invalid)]
    public Case Input { get; set; }

    [GlobalSetup]
    public void Setup() => _account = Data.Account(Input);

    [Benchmark(Baseline = true)]
    public async ValueTask<int> Sharp() => (await _sharp.ValidateAsync(_account)).Errors.Count;

    [Benchmark]
    public async Task<int> FluentValidation() => (await _fluent.ValidateAsync(_account)).Errors.Count;
}