using System.Collections;
using System.Linq.Expressions;

namespace Sharp.Validator.Tests.Pipeline;

public sealed class ElementSourceReuseTests
{
    [Fact]
    public async Task NonListSource_MixedSyncAsyncAndChildChains_IsEnumeratedOnce()
    {
        var source = new OneShot<Item>(
            new Item { Name = "ok", Code = "ok" },
            new Item { Name = "", Code = "" });

        var validator = new Validator<Model>(builder =>
        {
            var elements = builder.Elements(x => x.Items);
            elements.Must(_ => true);
            elements.MustAsync(_ => Task.FromResult(true));
            elements.Property(x => x.Name).NotEmpty();
            elements.Property(x => x.Code).NotEmpty();
        });

        var result = await validator.ValidateAsync(new Model { Items = source });

        Assert.Equal(1, source.Enumerations);
        Assert.Equal(["Items[1].Name", "Items[1].Code"], result.Errors.Select(x => x.PropertyName));
    }

    [Fact]
    public void IListSource_IsReusedByReferenceWithoutEnumeratorMaterialization()
    {
        var source = new IndexOnlyList<Item>([new Item { Name = "", Code = "" }]);
        var validator = new Validator<Model>(builder =>
        {
            var elements = builder.Elements(x => x.Items);
            elements.Property(x => x.Name).NotEmpty();
            elements.Property(x => x.Code).NotEmpty();
        });

        var result = validator.Validate(new Model { Items = source });
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void ReusingContext_ClearsThePerValidationElementCache()
    {
        var model = new FreshSequenceModel();
        var context = new ValidationContext<FreshSequenceModel>(model);
        var validator = new Validator<FreshSequenceModel>(builder =>
        {
            var elements = builder.Elements(x => x.Items);
            elements.Property(x => x.Name).NotEmpty();
            elements.Property(x => x.Code).NotEmpty();
        });

        validator.Validate(context);
        validator.Validate(context);

        Assert.Equal(2, model.Enumerations);
    }

    [Fact]
    // Every validator numbers the sources its chains share from 0, and a context can go to one validator and then to
    // another. The run that ends clears its sources, or the second validator would walk the first one's elements.
    public void ContextPassedOn_ToAValidatorWithAsManySlots_WalksItsOwnSource()
    {
        var context = new ValidationContext<TwoListModel>(new TwoListModel
        {
            Items = [new Item { Name = "ok", Code = "ok" }],
            Others = [new Item { Name = "", Code = "ok" }]
        });

        Assert.True(TwoChainsOver(x => x.Items).Validate(context).IsValid);
        Assert.Equal(["Others[0].Name"], TwoChainsOver(x => x.Others).Validate(context).Errors.Select(x => x.PropertyName));
    }

    [Fact]
    public void ContextPassedOn_ToAValidatorWithMoreSlots_WalksEverySource()
    {
        var context = new ValidationContext<TwoListModel>(new TwoListModel
        {
            Items = [new Item { Name = "", Code = "ok" }],
            Others = [new Item { Name = "ok", Code = "" }]
        });

        var both = new Validator<TwoListModel>(builder =>
        {
            var items = builder.Elements(x => x.Items);
            items.Property(x => x.Name).NotEmpty();
            items.Property(x => x.Code).NotEmpty();
            var others = builder.Elements(x => x.Others);
            others.Property(x => x.Name).NotEmpty();
            others.Property(x => x.Code).NotEmpty();
        });

        TwoChainsOver(x => x.Items).Validate(context);

        Assert.Equal(["Items[0].Name", "Others[0].Code"], both.Validate(context).Errors.Select(x => x.PropertyName));
    }

    [Fact]
    // An Include over elements runs its child once per element, on one child context. Each run walks its own element's
    // collection, not the one the run before it kept.
    public void IncludeOverElements_EachChildRun_WalksItsOwnElementsSource()
    {
        var group = new Validator<Group>(builder =>
        {
            var members = builder.Elements(x => x.Members);
            members.Property(x => x.Name).NotEmpty();
            members.Property(x => x.Code).NotEmpty();
        });

        var validator = new Validator<Team>(builder => builder.Elements(x => x.Groups).Include(group));
        var result = validator.Validate(new Team
        {
            Groups =
            [
                new Group { Members = [new Item { Name = "ok", Code = "ok" }] },
                new Group { Members = [new Item { Name = "", Code = "ok" }] }
            ]
        });

        Assert.Equal(["Groups[1].Members[0].Name"], result.Errors.Select(x => x.PropertyName));
    }

    [Fact]
    // Chains that share a list source used to cost a dictionary per validation, 216 bytes; one slot costs an object[]
    // of length one: a header, a method table pointer, the length, and the slot, a pointer's size each.
    public void SharedListSource_CostsOneSlotPerValidation()
    {
        var model = new TwoListModel { Items = [new Item { Name = "ok", Code = "ok" }, new Item { Name = "ok", Code = "ok" }] };
        var oneChain = new Validator<TwoListModel>(builder => builder.Elements(x => x.Items).Property(x => x.Name).NotEmpty());
        Assert.Equal(4 * IntPtr.Size, AllocatedBytes(TwoChainsOver(x => x.Items), model) - AllocatedBytes(oneChain, model));
    }

    [Fact]
    public async Task AsyncElementLoop_ObservesCancellationBetweenElements()
    {
        using var source = new CancellationTokenSource();
        var visits = 0;
        var validator = new Validator<NumberModel>(builder => builder.Elements(x => x.Values)
            .MustAsync((value, _) =>
            {
                visits++;
                if (value == 1) source.Cancel();

                return Task.FromResult(true);
            }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await validator.ValidateAsync(new NumberModel { Values = [1, 2, 3] }, source.Token));

        Assert.Equal(1, visits);
    }

    [Fact]
    public void AllValidTenThousandElementProperties_HaveConstantAllocation()
    {
        var validator = new Validator<ListModel>(builder => builder.Elements(x => x.Items)
            .Property(x => x.Name)
            .NotEmpty());

        var model = new ListModel
        {
            Items = [.. Enumerable.Range(0, 10_000).Select(_ => new Item { Name = "ok" })]
        };

        validator.Validate(model);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = validator.Validate(model);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result.IsValid);
        Assert.True(allocated < 1_024, $"Expected constant allocation below 1 KiB, observed {allocated:N0} bytes.");
    }

    private static Validator<TwoListModel> TwoChainsOver(Expression<Func<TwoListModel, IEnumerable<Item>?>> source) =>
        new(builder =>
        {
            var elements = builder.Elements(source);
            elements.Property(x => x.Name).NotEmpty();
            elements.Property(x => x.Code).NotEmpty();
        });

    private static long AllocatedBytes(Validator<TwoListModel> validator, TwoListModel model)
    {
        validator.Validate(model);
        var before = GC.GetAllocatedBytesForCurrentThread();
        validator.Validate(model);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class Model
    {
        public IEnumerable<Item> Items { get; init; } = [];
    }

    private sealed class TwoListModel
    {
        public List<Item> Items { get; init; } = [];

        public List<Item> Others { get; init; } = [];
    }

    private sealed class Team
    {
        public List<Group> Groups { get; init; } = [];
    }

    private sealed class Group
    {
        public List<Item> Members { get; init; } = [];
    }

    private sealed class ListModel
    {
        public List<Item> Items { get; init; } = [];
    }

    private sealed class NumberModel
    {
        public List<int> Values { get; init; } = [];
    }

    private sealed class Item
    {
        public string? Name { get; init; }

        public string? Code { get; init; }
    }

    private sealed class FreshSequenceModel
    {
        internal int Enumerations;

        public IEnumerable<Item> Items => Enumerate();

        private IEnumerable<Item> Enumerate()
        {
            Enumerations++;
            yield return new Item { Name = "", Code = "" };
        }
    }

    private sealed class OneShot<T>(params T[] values) : IEnumerable<T>
    {
        internal int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            if (++Enumerations != 1) throw new InvalidOperationException("Sequence was enumerated more than once.");

            return ((IEnumerable<T>)values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class IndexOnlyList<T>(IList<T> values) : IList<T>
    {
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("IList must be indexed, not materialized.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(T item) => values.Add(item);

        public void Clear() => values.Clear();

        public bool Contains(T item) => values.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => values.CopyTo(array, arrayIndex);

        public bool Remove(T item) => values.Remove(item);

        public int Count => values.Count;

        public bool IsReadOnly => values.IsReadOnly;

        public int IndexOf(T item) => values.IndexOf(item);

        public void Insert(int index, T item) => values.Insert(index, item);

        public void RemoveAt(int index) => values.RemoveAt(index);

        public T this[int index]
        {
            get => values[index];
            set => values[index] = value;
        }
    }
}