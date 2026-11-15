namespace Sharp.Validator.Tests.Pipeline;

// An async element walk settles a rule whose task has already completed without leaving the walk, and takes the
// awaiting path from the first rule still pending; a chain with an Include keeps the awaiting path for every rule.
// Whichever path an element takes, the walk must report what the awaiting walk reported: the same errors under the same
// names, the same stop on a blocking failure, and the rule's own exception.
public sealed class AsyncElementWalkTests
{
    private static List<Item> Named(params string[] names) =>
        [.. names.Select(name => new Item { Name = name, Child = new Child { Code = name } })];

    private static async Task<bool> Later(bool result)
    {
        await Task.Yield();
        return result;
    }

    // Two rules on an elements chain over the tags or on an element-property chain over the items or the set. A walk
    // over a list hands itself to an awaiting walk at its first pending rule, which the two chains do differently: the
    // elements chain from that rule, the element-property chain from the rest of that element's rules. A set is walked
    // awaiting from the start.
    private static Validator<Model> TwoRules(
        bool elements,
        bool list,
        Func<string?, Task<bool>> first,
        Func<string?, Task<bool>> second
    )
    {
        if (elements) return new(x => x.Elements(model => model.Tags).MustAsync(first).MustAsync(second));

        return list
            ? new(x => x.Elements(model => model.Items).Property(item => item.Name).MustAsync(first).MustAsync(second))
            : new(x => x.Elements(model => model.Set).Property(item => item.Name).MustAsync(first).MustAsync(second));
    }

    private static Model TwoElements(bool list) => list
        ? new() { Tags = new List<string> { "ok", "ok" }, Items = Named("ok", "ok") }
        : new() { Tags = new HashSet<string> { "ok", "ok2" }, Set = [.. Named("ok", "ok2")] };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ElementsWalk_AwaitsAPendingRuleAndNamesEachFailure(bool list)
    {
        var validator = new Validator<Model>(x => x.Elements(model => model.Tags).MustAsync(tag => Later(!tag.StartsWith("bad"))));
        string[] tags = ["ok", "bad1", "ok2", "bad3"];

        var result = await validator.ValidateAsync(
            new Model { Tags = list ? new List<string>(tags) : new HashSet<string>(tags) }, TestContext.Current.CancellationToken);

        Assert.Equal(["Tags[1]", "Tags[3]"], result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public async Task ElementPropertyWalk_AfterAPendingOne_RunsTheRules()
    {
        var validator = new Validator<Model>(x => x.Elements(model => model.Items).Property(item => item.Name)
            .MustAsync(_ => Later(true))
            .MustAsync(name => Later(name != "bad"))
            .MustAsync(name => Task.FromResult(name != "worse")));

        var result = await validator.ValidateAsync(
            new Model { Items = Named("ok", "bad", "worse", "ok") }, TestContext.Current.CancellationToken);

        Assert.Equal(["Items[1].Name", "Items[2].Name"], result.Errors.Select(error => error.PropertyName));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task BlockingFailure_AfterAPendingRule_StopsTheWalk(int failingRule)
    {
        var visited = 0;
        var validator = new Validator<Model>(x => x.Elements(model => model.Items)
            .Property(item => item.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .MustAsync(name =>
            {
                visited++;
                return Later(failingRule != 0 || name != "bad");
            })
            .MustAsync(name => Later(failingRule != 1 || name != "bad")));

        var result = await validator.ValidateAsync(
            new Model { Items = Named("ok", "bad", "bad", "ok") }, TestContext.Current.CancellationToken);

        Assert.Equal(["Items[1].Name"], result.Errors.Select(error => error.PropertyName));
        Assert.Equal(2, visited);
    }

    [Fact]
    public async Task ElementPropertyWalk_AfterAPendingOne_SkipsANullElement()
    {
        var validator = new Validator<Model>(x => x.Elements(model => model.Items).Property(item => item.Name)
            .MustAsync(name => Later(name != "bad")));

        var result = await validator.ValidateAsync(
            new Model { Items = [.. Named("ok"), null!, .. Named("bad")] }, TestContext.Current.CancellationToken);

        Assert.Equal(["Items[2].Name"], result.Errors.Select(error => error.PropertyName));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public async Task RuleThatThrowsBeforeReturningItsTask_SurfacesTheException(
        bool elements,
        bool afterAPendingRule,
        bool list
    )
    {
        var validator = TwoRules(
            elements,
            list,
            _ => afterAPendingRule ? Later(true) : Task.FromResult(true),
            _ => throw new InvalidOperationException("thrown"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await validator.ValidateAsync(TwoElements(list), TestContext.Current.CancellationToken));

        Assert.Equal("thrown", exception.Message);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task RuleWhoseTaskFaults_AfterAPendingOne_SurfacesTheException(int faultingRule, bool elements)
    {
        var validator = TwoRules(
            elements,
            true,
            _ => faultingRule == 0 ? Faulted() : Later(true),
            _ => faultingRule == 1 ? Faulted() : Later(true));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await validator.ValidateAsync(TwoElements(true), TestContext.Current.CancellationToken));

        Assert.Equal("faulted", exception.Message);

        return;

        static async Task<bool> Faulted()
        {
            await Task.Yield();
            throw new InvalidOperationException("faulted");
        }
    }

    [Theory]
    [InlineData(ValidationCascadeMode.Continue, ValidationErrorSeverity.Error, 3, false)]
    [InlineData(ValidationCascadeMode.Continue, ValidationErrorSeverity.Warning, 3, false)]
    [InlineData(ValidationCascadeMode.Stop, ValidationErrorSeverity.Error, 2, false)]
    [InlineData(ValidationCascadeMode.Stop, ValidationErrorSeverity.Warning, 3, false)]
    [InlineData(ValidationCascadeMode.Continue, ValidationErrorSeverity.Error, 3, true)]
    [InlineData(ValidationCascadeMode.Continue, ValidationErrorSeverity.Warning, 3, true)]
    [InlineData(ValidationCascadeMode.Stop, ValidationErrorSeverity.Error, 2, true)]
    [InlineData(ValidationCascadeMode.Stop, ValidationErrorSeverity.Warning, 3, true)]
    // A list hands its walk over at the first pending include and walks the rest awaiting, a set awaits throughout.
    public async Task ElementPropertyWalk_WithAnInclude_ReportsItsOtherRules(
        ValidationCascadeMode cascade,
        ValidationErrorSeverity severity,
        int checkedElements,
        bool list
    )
    {
        var child = new Validator<Child>(x => x.Property(item => item.Code).MustAsync(_ => Later(true)));
        var checkedCount = 0;
        var validator = new Validator<Model>(x =>
        {
            var elements = list ? x.Elements(model => model.Items) : x.Elements(model => model.Set);
            elements
                .Property(item => item.Child, options => options.CascadeMode = cascade)
                .Include(child)
                .MustAsync(item =>
                {
                    checkedCount++;
                    return Task.FromResult(item!.Code != "bad");
                }, options => options.ErrorSeverity = severity);
        });

        var items = Named("ok", "bad", "ok");
        var result = await validator.ValidateAsync(
            new Model { Items = items, Set = [.. items] }, TestContext.Current.CancellationToken);

        Assert.Equal([list ? "Items[1].Child" : "Set[1].Child"], result.Errors.Select(error => error.PropertyName));
        Assert.Equal(severity, Assert.Single(result.Errors).Severity);
        Assert.Equal(checkedElements, checkedCount);
    }

    private sealed class Child
    {
        public string? Code { get; init; }
    }

    private sealed class Item
    {
        public string? Name { get; init; }

        public Child? Child { get; init; }
    }

    private sealed class Model
    {
        public List<Item>? Items { get; init; }

        public HashSet<Item>? Set { get; init; }

        public IEnumerable<string>? Tags { get; init; }
    }
}