using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Benchmarks.Scenarios;

/// <summary>
/// What a validator costs per DI scope. Each method creates a scope, resolves a ten-rule validator and validates one valid model. A Singleton is
/// built once; a Scoped validator is built in every scope, where its constructor declares the rules and its first validation freezes them. README ›
/// Choose a lifetime quotes this cost. Default job rather than ShortRun: building a validator touches a few hundred methods, and tiering needs the
/// longer warmup to settle.
/// </summary>
[MemoryDiagnoser]
public class ValidatorLifetimeBenchmarks
{
    private readonly Signup _model = new()
    {
        Email = "alice@example.com", Name = "Alice", Username = "alice", Age = 30, PostalCode = "12345",
        Phone = "+1 555 0100"
    };
    private ServiceProvider _scoped = null!;

    private ServiceProvider _singleton = null!;

    [GlobalSetup]
    public void Setup()
    {
        _singleton = new ServiceCollection().AddValidator<SignupValidator>().BuildServiceProvider();
        _scoped = new ServiceCollection().AddValidator<SignupValidator>(ServiceLifetime.Scoped).BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _singleton.Dispose();
        _scoped.Dispose();
    }

    [Benchmark(Baseline = true)]
    public bool Singleton() => ValidateInScope(_singleton);

    [Benchmark]
    public bool Scoped() => ValidateInScope(_scoped);

    private bool ValidateInScope(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IValidator<Signup>>().Validate(_model).IsValid;
    }

    public sealed class Signup
    {
        public string Email { get; init; } = "";

        public string Name { get; init; } = "";

        public string Username { get; init; } = "";

        public int Age { get; init; }

        public string PostalCode { get; init; } = "";

        public string Phone { get; init; } = "";
    }

    public sealed class SignupValidator : Validator<Signup>
    {
        public SignupValidator()
        {
            Builder.Property(x => x.Email).NotEmpty().Email();
            Builder.Property(x => x.Name).NotEmpty().Length(2, 50);
            Builder.Property(x => x.Username).NotEmpty().Length(3, 20);
            Builder.Property(x => x.Age).InclusiveBetween(18, 120);
            Builder.Property(x => x.PostalCode).NotEmpty().Regex(@"^\d{5}$");
            Builder.Property(x => x.Phone).PhoneNumber();
        }
    }
}