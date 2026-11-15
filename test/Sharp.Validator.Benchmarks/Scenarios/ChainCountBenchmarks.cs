using BenchmarkDotNet.Attributes;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// What one more rule chain costs: the same model validated by one chain and by 24, each with a single cheap rule, so the gap divided by 23 is the cost
/// of a chain. The flat, nested and collection scenarios run a regex or an email check on most chains, which buries a few instructions added to every
/// chain: a generic lookup that added eight per chain on arm64 moved them by -1.1 to +3.4%, inside their launch-to-launch spread. The 24 chains mix
/// reference-type properties, which run shared generic code, with nullable value types, which don't.
/// </summary>
[MemoryDiagnoser]
[AccessorJobs]
public class ChainCountBenchmarks
{
    private readonly Validator<Profile> _manyChains = new(x =>
    {
        x.Property(profile => profile.Text01).NotEmpty();
        x.Property(profile => profile.Text02).NotEmpty();
        x.Property(profile => profile.Text03).NotEmpty();
        x.Property(profile => profile.Text04).NotEmpty();
        x.Property(profile => profile.Text05).NotEmpty();
        x.Property(profile => profile.Text06).NotEmpty();
        x.Property(profile => profile.Text07).NotEmpty();
        x.Property(profile => profile.Text08).NotEmpty();
        x.Property(profile => profile.Text09).NotEmpty();
        x.Property(profile => profile.Text10).NotEmpty();
        x.Property(profile => profile.Text11).NotEmpty();
        x.Property(profile => profile.Text12).NotEmpty();
        x.Property(profile => profile.Text13).NotEmpty();
        x.Property(profile => profile.Text14).NotEmpty();
        x.Property(profile => profile.Text15).NotEmpty();
        x.Property(profile => profile.Text16).NotEmpty();
        x.Property(profile => profile.Number01).NotNull();
        x.Property(profile => profile.Number02).NotNull();
        x.Property(profile => profile.Number03).NotNull();
        x.Property(profile => profile.Number04).NotNull();
        x.Property(profile => profile.Number05).NotNull();
        x.Property(profile => profile.Number06).NotNull();
        x.Property(profile => profile.Number07).NotNull();
        x.Property(profile => profile.Number08).NotNull();
    });

    private readonly Profile _model = new()
    {
        Text01 = "a", Text02 = "b", Text03 = "c", Text04 = "d", Text05 = "e", Text06 = "f", Text07 = "g", Text08 = "h",
        Text09 = "i", Text10 = "j", Text11 = "k", Text12 = "l", Text13 = "m", Text14 = "n", Text15 = "o", Text16 = "p",
        Number01 = 1, Number02 = 2, Number03 = 3, Number04 = 4, Number05 = 5, Number06 = 6, Number07 = 7, Number08 = 8
    };

    private readonly Validator<Profile> _oneChain = new(x => x.Property(profile => profile.Text01).NotEmpty());

    [GlobalSetup]
    public void Setup() => AccessorPath.VerifyJob();

    [Benchmark]
    public bool OneChain() => _oneChain.Validate(_model).IsValid;

    [Benchmark]
    public bool TwentyFourChains() => _manyChains.Validate(_model).IsValid;

    public sealed class Profile
    {
        public string? Text01 { get; init; }

        public string? Text02 { get; init; }

        public string? Text03 { get; init; }

        public string? Text04 { get; init; }

        public string? Text05 { get; init; }

        public string? Text06 { get; init; }

        public string? Text07 { get; init; }

        public string? Text08 { get; init; }

        public string? Text09 { get; init; }

        public string? Text10 { get; init; }

        public string? Text11 { get; init; }

        public string? Text12 { get; init; }

        public string? Text13 { get; init; }

        public string? Text14 { get; init; }

        public string? Text15 { get; init; }

        public string? Text16 { get; init; }

        public int? Number01 { get; init; }

        public int? Number02 { get; init; }

        public int? Number03 { get; init; }

        public int? Number04 { get; init; }

        public int? Number05 { get; init; }

        public int? Number06 { get; init; }

        public int? Number07 { get; init; }

        public int? Number08 { get; init; }
    }
}