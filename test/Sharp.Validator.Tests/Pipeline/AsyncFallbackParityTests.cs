namespace Sharp.Validator.Tests.Pipeline;

public sealed class AsyncFallbackParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // A chain's async rules run on a synchronous path while their tasks complete at once, and move to an awaiting path
    // from the first task that waits. Each chain here runs once with every task completing at once and again with the
    // first, the second, or both tasks waiting (Task.Yield), and must report the same errors in the same order: for a
    // property and a nested one with a null on the way, over successful, warning-only and blocking outcomes, Continue
    // and Stop. The comparison goes through ValidateAsync, so it checks the paths a caller takes.
    public async Task AwaitingPropertyEvaluation_MatchesTheSynchronousPath(bool nested)
    {
        foreach (var stop in new[] { false, true })
        foreach (var warning in new[] { false, true })
        foreach (var text in new[] { "ok", "bad", "warning" })
        {
            var model = new Model { Text = text, Child = text == "warning" ? null : new() { Text = text } };
            var expected = await Property(nested, stop, warning, Waits.None).ValidateAsync(model, TestContext.Current.CancellationToken);
            foreach (var waits in new[] { Waits.First, Waits.Second, Waits.Both })
                Assert.Equal(expected.Errors,
                    (await Property(nested, stop, warning, waits).ValidateAsync(model, TestContext.Current.CancellationToken)).Errors);
        }
    }

    [Fact]
    // The same over the elements of a list, whose walk settles completed rules in place, and of a sequence that isn't a
    // list, which the awaiting walk takes from the start. Both sources hold the same elements and must agree with each
    // other as well.
    public async Task AwaitingElementsEvaluation_MatchesTheSynchronousPath()
    {
        foreach (var stop in new[] { false, true })
        foreach (var warning in new[] { false, true })
        foreach (var texts in new List<string?>?[] { null, ["ok", "bad", "warning", null] })
        {
            var expected = await Elements(stop, warning, Waits.None)
                .ValidateAsync(new Model { Texts = texts }, TestContext.Current.CancellationToken);

            foreach (var waits in new[] { Waits.None, Waits.First, Waits.Second, Waits.Both })
            foreach (var sequence in new[] { false, true })
            {
                var model = new Model { Texts = sequence && texts is not null ? Enumerate(texts) : texts };
                Assert.Equal(expected.Errors,
                    (await Elements(stop, warning, waits).ValidateAsync(model, TestContext.Current.CancellationToken)).Errors);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    // An Include makes even a list take the awaiting walk. Over a list and over a sequence, with the child's rule
    // completing at once or waiting, every run reports the same errors: each failing element under Continue, and only
    // the first under Stop, which ends the walk.
    public async Task AwaitingIncludeWalk_MatchesAcrossSourcesAndWaits(bool stop)
    {
        Child[] values = [new(), new(), new() { Text = "ok" }];
        IReadOnlyList<ValidationError>? expected = null;
        foreach (var waits in new[] { false, true })
        foreach (var sequence in new[] { false, true })
        {
            var child = new Validator<Child>(b => b.Property(x => x.Text).MustAsync(Rule(value => value is not null, waits)));
            var validator = new Validator<Model>(b => b.Elements(x => x.Children).Include(child));
            validator.Configure(o => o.CascadeMode = stop ? ValidationCascadeMode.Stop : ValidationCascadeMode.Continue);
            var result = await validator.ValidateAsync(
                new Model { Children = sequence ? Enumerate(values) : values.ToList() }, TestContext.Current.CancellationToken);

            expected ??= result.Errors;
            Assert.Equal(expected, result.Errors);
        }

        Assert.Equal(stop ? 1 : 2, expected!.Count);
    }

    private static Validator<Model> Property(bool nested, bool stop, bool warning, Waits waits)
    {
        var validator = new Validator<Model>(b =>
        {
            var chain = nested ? b.Property(x => x.Child!.Text) : b.Property(x => x.Text);
            chain.MustAsync(Rule(value => value == "ok", waits is Waits.First or Waits.Both),
                    o => o.ErrorSeverity = warning ? ValidationErrorSeverity.Warning : ValidationErrorSeverity.Error)
                .MustAsync(Rule(value => value != "bad", waits is Waits.Second or Waits.Both));
        });

        validator.Configure(o => o.CascadeMode = stop ? ValidationCascadeMode.Stop : ValidationCascadeMode.Continue);
        return validator;
    }

    private static Validator<Model> Elements(bool stop, bool warning, Waits waits)
    {
        var validator = new Validator<Model>(b =>
        {
            b.Elements(x => x.Texts)
                .MustAsync(Rule(value => value == "ok", waits is Waits.First or Waits.Both),
                    o => o.ErrorSeverity = warning ? ValidationErrorSeverity.Warning : ValidationErrorSeverity.Error)
                .MustAsync(Rule(value => value != "bad", waits is Waits.Second or Waits.Both));
        });

        validator.Configure(o => o.CascadeMode = stop ? ValidationCascadeMode.Stop : ValidationCascadeMode.Continue);
        return validator;
    }

    private static Func<string?, Task<bool>> Rule(Func<string?, bool> check, bool waits) => waits
        ? async value =>
        {
            await Task.Yield();
            return check(value);
        }
        : value => Task.FromResult(check(value));

    private static IEnumerable<string?> Enumerate(IEnumerable<string?> values)
    {
        foreach (var value in values) yield return value;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AsyncCollectionIncludes_CollectionOrIterator_ReportTheSameErrors(int count)
    {
        foreach (var iterator in new[] { false, true })
        {
            var child = new Validator<Child>(b => b.Property(x => x.Text).MustAsync(value => Task.FromResult(value is not null)));
            var validator = new Validator<Model>(b => b.Elements(x => x.Children).Include(child));
            var values = Enumerable.Range(0, count).Select(_ => new Child()).ToArray();
            var source = iterator ? Enumerate(values) : new HashSet<Child>(values);
            Assert.Equal(count,
                (await validator.ValidateAsync(new Model { Children = source }, TestContext.Current.CancellationToken)).Errors.Count);
        }
    }

    private static IEnumerable<Child> Enumerate(IEnumerable<Child> values)
    {
        foreach (var value in values) yield return value;
    }

    private sealed class Model
    {
        public Child? Child { get; init; }

        public string? Text { get; init; }

        public IEnumerable<Child>? Children { get; init; }

        public IEnumerable<string?>? Texts { get; init; }
    }

    private sealed class Child
    {
        public string? Text { get; init; }
    }

    private enum Waits
    {
        None,
        First,
        Second,
        Both
    }
}