namespace Sharp.Validator.Tests.Pipeline;

// Covers the non-IList fallback in the Elements evaluators. The IList<T> fast path (added when Enumerable.Index() was
// removed) only applies to List<T>/arrays; sequences that are not IList<T> (LINQ iterators, yield methods) must still
// validate with correct, contiguous [index] names and be enumerated exactly once across all rules.
public sealed class ElementsNonListEnumerableTests
{
    private static IEnumerable<string?> Seq(params string?[] values)
    {
        foreach (var value in values)
            yield return value;
    }

    [Fact]
    public void NonIListEnumerable_ReportsErrorsAtCorrectIndices()
    {
        var validator = new Validator<SequenceHolder>(x => x.Elements(sequenceHolder => sequenceHolder.Items).NotEmpty());

        // Indices 1 and 3 are empty.
        var result = validator.Validate(new SequenceHolder { Items = Seq("a", "", "b", "") });
        Assert.False(result.IsValid);

        Assert.Collection(result.Errors,
            error => Assert.Equal("Items[1]", error.PropertyName),
            error => Assert.Equal("Items[3]", error.PropertyName));
    }

    [Fact]
    public void NonIListEnumerable_PassesWhenAllElementsValid()
    {
        var validator = new Validator<SequenceHolder>(x => x.Elements(sequenceHolder => sequenceHolder.Items).NotEmpty());
        Assert.True(validator.Validate(new SequenceHolder { Items = Seq("a", "b", "c") }).IsValid);
    }

    [Fact]
    public async Task NonIListEnumerable_ReportsErrorsAtCorrectIndicesAsync()
    {
        // Async validator only, so the errors come from the async elements evaluator's non-IList foreach path.
        var validator = new Validator<SequenceHolder>(builder => builder.Elements(x => x.Items)
            .MustAsync(x => Task.FromResult(!string.IsNullOrEmpty(x))));

        var result = await validator.ValidateAsync(new SequenceHolder { Items = Seq("a", "", "b", "") });
        Assert.False(result.IsValid);

        Assert.Collection(result.Errors,
            error => Assert.Equal("Items[1]", error.PropertyName),
            error => Assert.Equal("Items[3]", error.PropertyName));
    }

    [Fact]
    public void NonIListEnumerable_IsEnumeratedOnceForMultipleValidators()
    {
        var model = new CountingSequenceHolder();
        var validator = new Validator<CountingSequenceHolder>(builder => builder.Elements(x => x.Items)
            .NotEmpty()
            .NotEmpty()
            .NotEmpty());

        validator.Validate(model);

        Assert.Equal(1, model.Enumerations);
    }

    private sealed class SequenceHolder
    {
        // Typed as IEnumerable<string?> and backed by a yield iterator, so it is not an IList<string?>.
        public IEnumerable<string?> Items { get; init; } = [];
    }

    private sealed class CountingSequenceHolder
    {
        public int Enumerations { get; private set; }

        // A fresh lazy iterator each access; the evaluator must enumerate the returned sequence only once, even with
        // multiple validators (element-outer iteration), or this counter would exceed 1.
        public IEnumerable<string?> Items => Enumerate();

        private IEnumerable<string?> Enumerate()
        {
            Enumerations++;
            yield return "ok";
            yield return "";
        }
    }
}