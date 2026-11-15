using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Element walks that run a child validator for every element, over 2 and 200 elements, with a sync child and with an async one, through
/// <c>Elements(...).Include</c> and through a member of each element. Both kinds of walk reuse one child context across their elements. The async walks
/// keep it in a slot made once per walk of two or more elements, which took a 200-element walk from 20,928 to 256 bytes and costs short walks a few
/// percent, because every async include call carries the slot.
/// </summary>
[MemoryDiagnoser]
[AccessorJobs]
public class ElementIncludeBenchmarks
{
    private static readonly Task<bool> CompletedTrue = Task.FromResult(true);

    private readonly Validator<Basket> _elementProperty =
        new(x => x.Elements(basket => basket.Lines).Property(line => line.Item).Include(Child()));

    private readonly Validator<Basket> _elementPropertyAsync =
        new(x => x.Elements(basket => basket.Lines).Property(line => line.Item).Include(AsyncChild()));

    private readonly Validator<Basket> _elements =
        new(x => x.Elements(basket => basket.Items).Include(Child()));

    private readonly Validator<Basket> _elementsAsync =
        new(x => x.Elements(basket => basket.Items).Include(AsyncChild()));

    private Basket _model = null!;

    [Params(2, 200)]
    public int Count { get; set; }

    private static Validator<Item> Child() => new(x =>
    {
        x.Property(item => item.Sku).NotEmpty();
        x.Property(item => item.Quantity).GreaterThan(0);
    });

    private static Validator<Item> AsyncChild() => new(x =>
    {
        x.Property(item => item.Sku).NotEmpty().MustAsync(_ => CompletedTrue);
        x.Property(item => item.Quantity).GreaterThan(0);
    });

    [GlobalSetup]
    public void Setup()
    {
        AccessorPath.VerifyJob();
        _model = new Basket
        {
            Items = [.. Enumerable.Range(0, Count).Select(index => new Item { Sku = "sku", Quantity = index + 1 })],
            Lines = [.. Enumerable.Range(0, Count).Select(index => new Line { Item = new() { Sku = "sku", Quantity = index + 1 } })]
        };
    }

    [Benchmark]
    public bool Elements() => _elements.Validate(_model).IsValid;

    [Benchmark]
    public bool ElementProperty() => _elementProperty.Validate(_model).IsValid;

    [Benchmark]
    public bool Elements_Async() => _elementsAsync.ValidateAsync(_model).GetAwaiter().GetResult().IsValid;

    [Benchmark]
    public bool ElementProperty_Async() =>
        _elementPropertyAsync.ValidateAsync(_model).GetAwaiter().GetResult().IsValid;

    public sealed class Basket
    {
        public List<Item> Items { get; init; } = [];

        public List<Line> Lines { get; init; } = [];
    }

    public sealed class Line
    {
        public Item Item { get; init; } = new();
    }

    public sealed class Item
    {
        public string? Sku { get; init; }

        public int Quantity { get; init; }
    }
}