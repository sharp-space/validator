namespace Sharp.Validator.Tests.Pipeline;

public sealed class AsyncFlowTests
{
    [Fact]
    public async Task MustAsync_Cancellation_IsPropagated()
    {
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .MustAsync(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return true;
            }));

        using var source = new CancellationTokenSource();
        var task = validator.ValidateAsync(new Person { Name = "x" }, source.Token);
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task AsyncWhereSkip_DoesNotRunAsyncPredicate()
    {
        var ran = false;
        var validator = new Validator<Person>(builder => builder
            .Property(x => x.Name)
            .Where(x => x.Age > 0)
            .MustAsync(async (_, _) =>
            {
                ran = true;
                await Task.Yield();

                return false;
            }));

        await validator.ValidateAsync(new Person { Name = "x", Age = 0 });

        Assert.False(ran);
    }
}