using Microsoft.Extensions.DependencyInjection;

namespace Sharp.Validator.Tests.Registration;

// AddValidator(instance) took a lifetime, and a Scoped or Transient one registered a factory that returned the
// instance. The container then treated the object as its own and disposed it at the end of every scope, while other
// scopes kept using it. An existing instance is one object the caller made, which the container can neither scope nor
// create again, so it is registered as itself: every resolve returns it and the container never disposes it.
public sealed class InstanceRegistrationTests
{
    [Fact]
    public void ExistingInstance_Registered_IsReturnedEverywhereWithoutDisposal()
    {
        var instance = new DisposableRegValidator();
        var services = new ServiceCollection();
        services.AddValidator(instance);

        using (var provider = services.BuildServiceProvider())
        {
            Assert.Same(instance, provider.GetRequiredService<IValidator<RegModel>>());

            for (var index = 0; index < 2; index++)
            {
                using var scope = provider.CreateScope();
                Assert.Same(instance, scope.ServiceProvider.GetRequiredService<IValidator<RegModel>>());
            }
        }

        Assert.Equal(0, instance.DisposeCalls);
    }

    private sealed class DisposableRegValidator() : Validator<RegModel>(x => x.Property(model => model.Value).NotEmpty()), IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }
}