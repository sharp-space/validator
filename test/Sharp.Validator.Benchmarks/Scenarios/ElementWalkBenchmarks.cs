using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Element walks over a list of strings and over a member of each element, synchronous and with an awaited predicate per element. At 2 and 10 elements a
/// chain's fixed cost dominates; at 10,000 only what each element costs shows. A generic lookup once added to every element made the 10,000-element walk
/// 27% slower, and a 10-element walk hid it. The predicate returns a completed task, so the async walks time the async machinery without a real wait.
/// Two more walks have several chains walk one <c>Elements</c> call's source, as README's examples do: two member chains, and a rule on each element
/// beside a member chain. The validation reads that source once for all of them, which is a fixed cost per validation, so it shows at 2 elements.
/// </summary>
[MemoryDiagnoser]
[AccessorJobs]
public class ElementWalkBenchmarks
{
    private static readonly Task<bool> CompletedTrue = Task.FromResult(true);

    private readonly Validator<Document> _elementProperties = new(x =>
    {
        var lines = x.Elements(document => document.Lines);
        lines.Property(line => line.Label).NotEmpty();
        lines.Property(line => line.Code).NotEmpty();
    });

    private readonly Validator<Document> _elementProperty =
        new(x => x.Elements(document => document.Lines).Property(line => line.Label).NotEmpty());

    private readonly Validator<Document> _elementPropertyAsync =
        new(x => x.Elements(document => document.Lines).Property(line => line.Label).NotEmpty().MustAsync(_ => CompletedTrue));

    private readonly Validator<Document> _elementRuleAndProperty = new(x =>
    {
        var lines = x.Elements(document => document.Lines);
        lines.NotNull();
        lines.Property(line => line.Label).NotEmpty();
    });

    private readonly Validator<Document> _elements =
        new(x => x.Elements(document => document.Tags).NotEmpty());

    private readonly Validator<Document> _elementsAsync =
        new(x => x.Elements(document => document.Tags).NotEmpty().MustAsync(_ => CompletedTrue));

    private Document _model = null!;

    [Params(2, 10, 10_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        AccessorPath.VerifyJob();
        _model = new Document
        {
            Tags = [.. Enumerable.Range(0, Count).Select(_ => "ok")],
            Lines = [.. Enumerable.Range(0, Count).Select(_ => new Line { Label = "ok", Code = "ok" })]
        };
    }

    [Benchmark]
    public bool Elements() => _elements.Validate(_model).IsValid;

    [Benchmark]
    public bool ElementProperty() => _elementProperty.Validate(_model).IsValid;

    [Benchmark]
    public bool ElementProperty_TwoChains() => _elementProperties.Validate(_model).IsValid;

    [Benchmark]
    public bool ElementRule_AndProperty() => _elementRuleAndProperty.Validate(_model).IsValid;

    [Benchmark]
    public bool Elements_Async() => _elementsAsync.ValidateAsync(_model).GetAwaiter().GetResult().IsValid;

    [Benchmark]
    public bool ElementProperty_Async() =>
        _elementPropertyAsync.ValidateAsync(_model).GetAwaiter().GetResult().IsValid;

    public sealed class Document
    {
        public List<string?> Tags { get; init; } = [];

        public List<Line> Lines { get; init; } = [];
    }

    public sealed class Line
    {
        public string? Label { get; init; }

        public string? Code { get; init; }
    }
}