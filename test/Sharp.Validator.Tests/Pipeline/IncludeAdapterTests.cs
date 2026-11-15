namespace Sharp.Validator.Tests.Pipeline;

public sealed class IncludeAdapterTests
{
    [Fact]
    public async Task DerivedAdapters_WithoutParentName_SupportDirectUseWithErrorMerging()
    {
        var child = new Validator<Model>(b => b.Property(x => x.Name).NotEmpty());
        var sync = new IncludeDerivedValidator<object, object, Model>(child, new());
        var async = new AsyncIncludeDerivedValidator<object, object, Model>(child, new());
        var context = new ValidationContext<object>(new());
        Assert.True(sync.Validate(new Model { Name = "ok" }, context));
        Assert.False(sync.Validate(new Model(), context));
        Assert.True(await async.ValidateAsync(new Model { Name = "ok" }, context, TestContext.Current.CancellationToken));
        Assert.False(await async.ValidateAsync(new Model(), context, TestContext.Current.CancellationToken));
        Assert.Equal(2, context.ToResult().Errors.Count);
    }

    [Fact]
    // A null value, or one of another type, isn't the child's to validate, so both adapters pass it.
    public async Task DerivedAdapters_NullOrAnotherType_Pass()
    {
        var child = new Validator<Model>(b => b.Property(x => x.Name).NotEmpty());
        var sync = new IncludeDerivedValidator<object, object?, Model>(child, new());
        var async = new AsyncIncludeDerivedValidator<object, object?, Model>(child, new());
        var context = new ValidationContext<object>(new());
        Assert.True(sync.Validate(null, context));
        Assert.True(sync.Validate("not a model", context));
        Assert.True(await async.ValidateAsync(null, context, TestContext.Current.CancellationToken));
        Assert.True(await async.ValidateAsync("not a model", context, TestContext.Current.CancellationToken));
        Assert.Empty(context.ToResult().Errors);
    }

    [Fact]
    public void SyncInclude_BeforeFreezingIt_RejectsAnAsyncChild()
    {
        var child = new Validator<Model>(b => b.Property(x => x.Name).MustAsync(_ => Task.FromResult(true)));
        var adapter = new IncludeDerivedValidator<object, Model, Model>(child, new());
        Assert.Throws<InvalidOperationException>(() => ((IIncludeValidator)adapter).PrepareForComposition(new ValidatorBuilder<object>()));
        Assert.False(child.Builder.IsFrozen);
    }

    [Fact]
    public void IncludedError_WithoutAMessage_KeepsAnEmptyMessage()
    {
        var context = new ValidationContext<object>(new());
        var child = new Validator<Model>(b => b.Property(x => x.Name).Must((_, ctx) =>
        {
            ctx.Errors.Add(new ValidationError("Name"));
            return true;
        }));

        var adapter = new IncludeDerivedValidator<object, Model, Model>(child, new());
        Assert.False(adapter.Validate(new Model(), context));
        var error = Assert.Single(context.ToResult().Errors);
        Assert.Equal("Name", error.PropertyName);
        Assert.Equal(string.Empty, error.Message);
    }

    private sealed class Model
    {
        public string? Name { get; init; }
    }
}