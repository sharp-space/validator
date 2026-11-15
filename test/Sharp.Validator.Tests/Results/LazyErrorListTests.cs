namespace Sharp.Validator.Tests.Results;

// The error list is allocated lazily (no list on the all-valid path) and the result is built only after all rules run.
// These pin the footguns that a naive lazy implementation would hit: async errors added after the sync phase must still
// surface (not be dropped by a result that captured a null list early), warnings must be captured while leaving IsValid
// true, the public Errors stays non-null when valid, and async cascade-Stop still short-circuits on a sync error.
public sealed class LazyErrorListTests
{
    [Fact]
    public async Task AsyncFailure_AfterPassingSyncRules_IsReported()
    {
        // Name (sync) passes => no list is allocated during the sync phase; Email (async) then fails => the result must
        // still surface that error. A result built before the async phase would drop it.
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty();
            builder.Property(x => x.Email).MustAsync(async (_, _) =>
            {
                await Task.Yield();
                return false;
            });
        });

        var result = await validator.ValidateAsync(new Person { Name = "ok", Email = "x" });
        Assert.False(result.IsValid);
        var item = Assert.Single(result.Errors);
        Assert.Equal("Email", item.PropertyName);
    }

    [Fact]
    public void WarningOnlyResult_IsValidButStillCaptured()
    {
        var validator = new Validator<Person>(builder =>
            builder.Property(x => x.Name).NotEmpty(x => x.ErrorSeverity = ValidationErrorSeverity.Warning));

        var result = validator.Validate(new Person { Name = null });
        Assert.True(result.IsValid); // a Warning entry does not flip IsValid
        Assert.True(result.HasWarnings); // ...but it was captured (lazy list materialized on the Add)
        Assert.Single(result.Errors);
    }

    [Fact]
    public void ValidResultErrors_IsNonNullEmptyList()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
        var result = validator.Validate(new Person { Name = "ok" });
        Assert.True(result.IsValid);
        Assert.NotNull(result.Errors); // non-null contract preserved even with no list allocated internally
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task AsyncCascadeStop_ShortCircuitsOnASyncError()
    {
        var validator = new Validator<Person>(builder =>
        {
            builder.Property(x => x.Name).NotEmpty(); // sync, fails
            builder.Property(x => x.Email).MustAsync(async (_, _) =>
            {
                await Task.Yield();
                return false; // would add a second error if reached
            });
        });

        validator.Configure(x => x.CascadeMode = ValidationCascadeMode.Stop);
        var result = await validator.ValidateAsync(new Person { Name = null, Email = "x" });

        // Stop + a sync Error-severity entry => the async phase is skipped; only the sync error is reported.
        var item = Assert.Single(result.Errors);
        Assert.Equal("Name", item.PropertyName);
    }
}