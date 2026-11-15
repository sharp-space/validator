namespace Sharp.Validator.Tests.Accessors;

// A non-nested selector whose getter dereferences null reaches GetProperty/GetElements, which rethrow the
// NullReferenceException with guidance (and the original inside) instead of a raw NRE, sync and async.
public sealed class AccessorRethrowTests
{
    [Fact]
    // The evaluators share one reader, which keeps the selector's own exception inside, so the stack of the getter that
    // failed survives the rewrap.
    public void RewrappedException_KeepsTheOriginalInside()
    {
        var validator = new Validator<ThrowingAccessors>(x => x.Property(throwingAccessors => throwingAccessors.Boom).NotEmpty());
        var exception = Assert.Throws<NullReferenceException>(() => validator.Validate(new ThrowingAccessors()));
        Assert.IsType<NullReferenceException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    // A list is walked with the walk's own read and any other source with the reader's; each rewraps the same way.
    public async Task ElementMemberAccessor_RethrowsWithGuidance(bool async, bool list)
    {
        var validator = new Validator<Holder>(x =>
        {
            var member = x.Elements(holder => holder.Items!).Property(item => item.Boom);
            if (async) member.MustAsync(_ => Task.FromResult(true));
            else member.NotEmpty();
        });

        IEnumerable<ThrowingAccessors> items =
            list ? new List<ThrowingAccessors> { new() } : new HashSet<ThrowingAccessors> { new() };

        var exception = await Assert.ThrowsAsync<NullReferenceException>(async () =>
            await validator.ValidateAsync(new Holder { Items = items }));

        Assert.Contains("Where condition", exception.Message);
        Assert.IsType<NullReferenceException>(exception.InnerException);
    }

    [Fact]
    public void SyncPropertyAccessor_RethrowsNullReferenceExceptionWithGuidance()
    {
        var validator = new Validator<ThrowingAccessors>(x => x.Property(throwingAccessors => throwingAccessors.Boom).NotEmpty());
        var exception = Assert.Throws<NullReferenceException>(() => validator.Validate(new ThrowingAccessors()));
        Assert.Contains("Where condition", exception.Message);
    }

    [Fact]
    public void SyncElementsAccessor_RethrowsNullReferenceExceptionWithGuidance()
    {
        var validator = new Validator<ThrowingAccessors>(x => x.Elements(throwingAccessors => throwingAccessors.BoomItems).NotEmpty());
        var exception = Assert.Throws<NullReferenceException>(() => validator.Validate(new ThrowingAccessors()));
        Assert.Contains("Where condition", exception.Message);
    }

    [Fact]
    public async Task AsyncPropertyAccessor_RethrowsNullReferenceExceptionWithGuidance()
    {
        var validator =
            new Validator<ThrowingAccessors>(x => x.Property(throwingAccessors => throwingAccessors.Boom).MustAsync(_ => Task.FromResult(true)));

        var exception = await Assert.ThrowsAsync<NullReferenceException>(async () =>
            await validator.ValidateAsync(new ThrowingAccessors()));

        Assert.Contains("Where condition", exception.Message);
    }

    [Fact]
    public async Task AsyncElementsAccessor_RethrowsNullReferenceExceptionWithGuidance()
    {
        var validator = new Validator<ThrowingAccessors>(x =>
            x.Elements(throwingAccessors => throwingAccessors.BoomItems).MustAsync(_ => Task.FromResult(true)));

        var exception = await Assert.ThrowsAsync<NullReferenceException>(async () =>
            await validator.ValidateAsync(new ThrowingAccessors()));

        Assert.Contains("Where condition", exception.Message);
    }

    private sealed class ThrowingAccessors
    {
        private static string? Hidden => null;

        // Single-member (non-nested) selectors: the evaluator calls GetProperty/GetElements, and the getter throws
        // NullReferenceException at invocation time.
        public string Boom => Hidden!.ToUpperInvariant();

        public IEnumerable<string?> BoomItems => Hidden!.Split(',');
    }

    private sealed class Holder
    {
        public IEnumerable<ThrowingAccessors>? Items { get; init; }
    }
}