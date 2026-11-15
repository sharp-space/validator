using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// One <c>Where</c> block gating eight chains, with a sync and with an async condition, against the same chains ungated. A block's condition runs at
/// most once per validation: the first chain records its verdict and the other seven read it, so the gap to the ungated chains is the condition plus
/// that memo. The async condition returns a completed task, and it moves the gated chains' sync rules into the async phase, so that method also times
/// the async pass over the eight chains.
/// </summary>
[MemoryDiagnoser]
[AccessorJobs]
public class SharedConditionBenchmarks
{
    private static readonly Task<bool> CompletedTrue = Task.FromResult(true);
    private static readonly Task<bool> CompletedFalse = Task.FromResult(false);

    private readonly Validator<Order> _asyncCondition =
        new(x => x.Where(order => order.Active ? CompletedTrue : CompletedFalse, () => Chains(x)));

    private readonly Order _model = new()
    {
        Active = true,
        Text01 = "a",
        Text02 = "b",
        Text03 = "c",
        Text04 = "d",
        Text05 = "e",
        Text06 = "f",
        Text07 = "g",
        Text08 = "h"
    };

    private readonly Validator<Order> _syncCondition = new(x => x.Where(order => order.Active, () => Chains(x)));

    private readonly Validator<Order> _ungated = new(Chains);

    private static void Chains(ValidatorBuilder<Order> x)
    {
        x.Property(order => order.Text01).NotEmpty();
        x.Property(order => order.Text02).NotEmpty();
        x.Property(order => order.Text03).NotEmpty();
        x.Property(order => order.Text04).NotEmpty();
        x.Property(order => order.Text05).NotEmpty();
        x.Property(order => order.Text06).NotEmpty();
        x.Property(order => order.Text07).NotEmpty();
        x.Property(order => order.Text08).NotEmpty();
    }

    [GlobalSetup]
    public void Setup() => AccessorPath.VerifyJob();

    [Benchmark]
    public bool Ungated() => _ungated.Validate(_model).IsValid;

    [Benchmark]
    public bool SyncCondition() => _syncCondition.Validate(_model).IsValid;

    [Benchmark]
    public bool AsyncCondition() => _asyncCondition.ValidateAsync(_model).GetAwaiter().GetResult().IsValid;

    public sealed class Order
    {
        public bool Active { get; init; }

        public string? Text01 { get; init; }

        public string? Text02 { get; init; }

        public string? Text03 { get; init; }

        public string? Text04 { get; init; }

        public string? Text05 { get; init; }

        public string? Text06 { get; init; }

        public string? Text07 { get; init; }

        public string? Text08 { get; init; }
    }
}