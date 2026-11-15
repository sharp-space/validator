using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// Sharp-only micro paths (not a cross-library comparison): the flags-enum membership check, the dynamic-pattern regex cache hit, and the func-bound
/// exact-length rule. Used to decide the perf-backlog items by ns/op rather than by allocation counts.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class RuleHotPathBenchmarks
{
    [Flags]
    public enum Permissions
    {
        // ReSharper disable once UnusedMember.Global — a member InEnum checks values against.
        None = 0,
        Read = 1,
        Write = 2,
        Delete = 4,
        // ReSharper disable once UnusedMember.Global — a member InEnum checks values against.
        All = Read | Write | Delete
    }

    private readonly Validator<Document> _dynamicRegex =
        new(x => x.Property(document => document.Code).Regex(document => document.Pattern!));

    private readonly Validator<Document> _elementProperty =
        new(x => x.Elements(document => document.Lines).Property(line => line.Label).NotEmpty());

    private readonly Validator<Document> _exactLength =
        new(x => x.Property(document => document.Code).Length(_ => 6));

    private readonly Validator<Document> _flags =
        new(x => x.Property(document => document.Access).InEnum());

    private readonly Document _model = new()
    {
        Access = Permissions.Read | Permissions.Write,
        Pattern = "^[a-z]+$",
        Code = "abcdef",
        Lines = [.. Enumerable.Range(0, 10_000).Select(_ => new Line { Label = "ok" })]
    };

    [Benchmark]
    public bool FlagsEnum() => _flags.Validate(_model).IsValid;

    [Benchmark]
    public bool DynamicRegex_CacheHit() => _dynamicRegex.Validate(_model).IsValid;

    [Benchmark]
    public bool ExactLength_FuncBound() => _exactLength.Validate(_model).IsValid;

    [Benchmark]
    public bool ElementProperty_10k_AllValid() => _elementProperty.Validate(_model).IsValid;

    public sealed class Document
    {
        public Permissions Access { get; init; }

        public string? Pattern { get; init; }

        public string? Code { get; init; }

        public List<Line> Lines { get; init; } = [];
    }

    public sealed class Line
    {
        public string? Label { get; init; }
    }
}