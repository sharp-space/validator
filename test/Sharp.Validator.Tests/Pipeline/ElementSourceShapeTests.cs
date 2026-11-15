using System.Collections.ObjectModel;

namespace Sharp.Validator.Tests.Pipeline;

// List<T> and arrays run over a span while other sources keep the indexer or the enumerator, so one chain now has three
// ways to walk its elements. They must not differ in order, in indexes, or in where Stop ends the walk.
public sealed class ElementSourceShapeTests
{
    private static readonly Line?[] SampleLines = [new() { Sku = "a" }, null, new() { Sku = "" }, new() { Sku = "" }];

    private static IEnumerable<Line?> Shaped(string shape) => shape switch
    {
        "list" => SampleLines.ToList(),
        "array" => SampleLines.ToArray(),
        "indexed" => new Collection<Line?>(SampleLines.ToList()),
        _ => SampleLines.Select(line => line)
    };

    private static string[] Names(Validator<Order> validator, string shape) =>
        [.. validator.Validate(new Order { Lines = Shaped(shape) }).Errors.Select(error => error.PropertyName)];

    [Theory]
    [InlineData("list")]
    [InlineData("array")]
    [InlineData("indexed")]
    [InlineData("lazy")]
    public void Elements_ReportTheSameIndexesForEverySource(string shape)
    {
        var validator = new Validator<Order>(x => x.Elements(order => order.Lines).NotNull());
        Assert.Equal(["Lines[1]"], Names(validator, shape));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("array")]
    [InlineData("indexed")]
    [InlineData("lazy")]
    public void ElementProperties_SkipNullElementsForEverySource(string shape)
    {
        var validator = new Validator<Order>(x => x.Elements(order => order.Lines).Property(line => line!.Sku).NotEmpty());
        Assert.Equal(["Lines[2].Sku", "Lines[3].Sku"], Names(validator, shape));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("array")]
    [InlineData("indexed")]
    [InlineData("lazy")]
    public void Stop_EndsTheWalkAtTheFirstFailureForEverySource(string shape)
    {
        var validator = new Validator<Order>(x => x
            .Elements(order => order.Lines, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .Property(line => line!.Sku, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty());

        Assert.Equal(["Lines[2].Sku"], Names(validator, shape));
    }

    private sealed class Line
    {
        public string? Sku { get; init; }
    }

    private sealed class Order
    {
        public IEnumerable<Line?>? Lines { get; init; }
    }
}