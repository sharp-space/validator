using System.Linq.Expressions;
using System.Reflection;

namespace Sharp.Validator.Tests.Accessors;

// The accessor caches' Build paths that the public API never reaches, so each test calls Build directly.
public sealed class AccessorCacheTests
{
    [Fact]
    // NullSafeAccessorCache.Build members.Count == 0 throw. Unreachable through the public API: a cross-property target
    // that isn't a member chain is compiled as written (CrossPropertyTarget) and never reaches Build.
    // Exercise Build directly.
    public void NullSafeAccessor_RejectsANonMemberChainTarget()
    {
        var buildMethod = typeof(Validator<Person>).Assembly
            .GetType("Sharp.Validator.NullSafeAccessorCache`2")!
            .MakeGenericType(typeof(Person), typeof(int))
            .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;

        Expression<Func<Person, int>> target = person => person.Age + 1;
        var exception = Assert.Throws<TargetInvocationException>(() => buildMethod.Invoke(null, [target]));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }
}