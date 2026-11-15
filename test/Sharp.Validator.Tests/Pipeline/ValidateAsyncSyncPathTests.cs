namespace Sharp.Validator.Tests.Pipeline;

// ValidateAsync has a synchronous fast-path (when no async rule is registered it returns the completed result inline
// instead of running the async state machine). These pin that it is behavior-preserving: a sync-only validator through
// ValidateAsync must match Validate, complete synchronously, and an async rule must still route through the async core
// (covered by the async Include/MustAsync tests elsewhere).
public sealed class ValidateAsyncSyncPathTests
{
    [Fact]
    public async Task SyncOnlyValidator_ViaValidateAsync_MatchesValidate()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Age).GreaterThan(0);
        });

        var model = new Person { Name = null, Age = 0 };
        var sync = validator.Validate(model);
        var async = await validator.ValidateAsync(model);
        Assert.False(async.IsValid);
        Assert.Equal(2, async.Errors.Count);
        Assert.Equal(sync.Errors.Count, async.Errors.Count);

        Assert.Equal(
            sync.Errors.Select(x => x.PropertyName),
            async.Errors.Select(x => x.PropertyName));
    }

    [Fact]
    public async Task SyncOnlyValidator_ValidateAsyncCompletesSynchronously()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var task = validator.ValidateAsync(new Person { Name = "ok" });

        // The fast-path returns new ValueTask<>(result) without ever suspending.
        Assert.True(task.IsCompletedSuccessfully);

        var result = await task;
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task AsyncRule_StillRunsThroughTheAsyncCoreViaValidateAsync()
    {
        var validator = new Validator<Person>(builder => builder.Property(x => x.Name)
            .MustAsync(async (value, _) =>
            {
                await Task.Yield();
                return !string.IsNullOrEmpty(value);
            }));

        Assert.False((await validator.ValidateAsync(new Person { Name = "" })).IsValid);
        Assert.True((await validator.ValidateAsync(new Person { Name = "ok" })).IsValid);
    }
}