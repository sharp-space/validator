namespace Sharp.Validator.Tests.Pipeline;

// Regression: a List<T> is walked over a span of its backing array, sized when the walk began. A rule that removed an
// element left the walk reading a cleared slot, and one that added an element left it unchecked, where the index loop
// it replaced saw the list as it was. A List<T> that changes size while its elements are validated now throws, as the
// list's own enumerator does.
//
// The async evaluators walk the live list by index and checked nothing, so a rule that removed the element it was given
// shifted the next one into its slot, the walk stepped past it, and an invalid element passed unchecked. They throw the
// same exception now, wherever the walk ends: after the last element, or early on a Stop cascade.
public sealed class ElementsListMutationTests
{
    [Fact]
    public void RemovingAnElement_DuringValidation_Throws()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Items!).Must((item, context) =>
        {
            if (item == "a") context.Model.Items!.Remove("c");

            return true;
        }));

        Assert.Throws<InvalidOperationException>(() => validator.Validate(new Holder { Items = ["a", "b", "c"] }));
    }

    [Fact]
    public void AddingAnElement_DuringValidation_Throws()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Items!).Must((item, context) =>
        {
            if (item == "a") context.Model.Items!.Add("");

            return true;
        }));

        Assert.Throws<InvalidOperationException>(() => validator.Validate(new Holder { Items = ["a", "b", "c"] }));
    }

    [Fact]
    public void ElementPropertyChain_DetectsAChangeToo()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Children!).Property(child => child.Name)
            .Must((name, context) =>
            {
                if (name == "a") context.Model.Children!.RemoveAt(2);

                return true;
            }));

        Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(new Holder { Children = [new Child { Name = "a" }, new Child { Name = "b" }, new Child { Name = "c" }] }));
    }

    [Fact]
    public async Task RemovingTheCurrentElement_DuringAsyncValidation_Throws()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Items!).MustAsync((item, context, _) =>
        {
            if (item == "a") context.Model.Items!.RemoveAt(0);

            return Task.FromResult(item != "invalid");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.ValidateAsync(new Holder { Items = ["a", "invalid"] }));
    }

    [Fact]
    public async Task AddingAnElement_DuringSuspendedAsyncValidation_Throws()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Items!).MustAsync(async (item, context, _) =>
        {
            await Task.Yield();
            if (item == "a") context.Model.Items!.Add("");

            return true;
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.ValidateAsync(new Holder { Items = ["a", "b"] }));
    }

    [Fact]
    public async Task ChangeMade_OnTheLastElementAfterTheWalk_IsDetected()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Items!).MustAsync((item, context, _) =>
        {
            if (item == "b") context.Model.Items!.RemoveAt(0);

            return Task.FromResult(true);
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.ValidateAsync(new Holder { Items = ["a", "b"] }));
    }

    [Fact]
    public async Task Change_BeforeAStopCascade_EndsTheWalkAndIsDetected()
    {
        var validator = new Validator<Holder>(x => x
            .Elements(holder => holder.Items!, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .MustAsync((_, context, _) =>
            {
                context.Model.Items!.Add("");
                return Task.FromResult(false);
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.ValidateAsync(new Holder { Items = ["a", "b"] }));
    }

    [Fact]
    public async Task ElementPropertyChain_DuringAsyncValidation_DetectsAChange()
    {
        var validator = new Validator<Holder>(x => x.Elements(holder => holder.Children!).Property(child => child.Name)
            .MustAsync((name, context, _) =>
            {
                if (name == "a") context.Model.Children!.RemoveAt(0);

                return Task.FromResult(name != "invalid");
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await validator.ValidateAsync(new Holder { Children = [new Child { Name = "a" }, new Child { Name = "invalid" }] }));
    }

    private sealed class Holder
    {
        public List<string?>? Items { get; init; }

        public List<Child>? Children { get; init; }
    }

    private sealed class Child
    {
        public string? Name { get; init; }
    }
}