using System.Reflection;
using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

// Every RegexValidator built over a constant pattern parsed it into instances of its own, which a Scoped validator did
// in every scope. The instances now belong to the pattern, so every rule over the same pattern, options and timeout
// shares them. These pin that rules share them only when all three agree, that each rule's first thread still gets an
// instance no other thread is using, that a pattern which doesn't parse still throws where its rule is declared, and
// that past its limit the cache builds without keeping.
public sealed class RegexPatternSharingTests
{
    [Fact]
    public void ConstantPattern_BuiltByTwoRules_SharesItsInstances()
    {
        var pattern = Unique();
        Assert.Same(
            InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.None)),
            InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.None)));
    }

    [Fact]
    // Every pattern a rule builds ignores case in the invariant culture, so asking for that culture changes nothing.
    public void ConstantPattern_WithAndWithoutCultureInvariant_SharesItsInstances()
    {
        var pattern = Unique();
        Assert.Same(
            InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.IgnoreCase)),
            InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
    }

    [Fact]
    public void ConstantPattern_OtherOptions_KeepsInstancesOfItsOwn()
    {
        var exact = new Validator<Person>(x => x.Property(person => person.Name).Regex("^[a-z]{3}$"));
        var ignoringCase = new Validator<Person>(x => x.Property(person => person.Name).Regex("^[a-z]{3}$", RegexOptions.IgnoreCase));
        var model = new Person { Name = "ABC" };
        Assert.Equal((false, true), (exact.Validate(model).IsValid, ignoringCase.Validate(model).IsValid));
    }

    [Fact]
    public void ConstantPattern_OtherTimeout_KeepsInstancesOfItsOwn()
    {
        var pattern = Unique();
        var longer = InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.None, TimeSpan.FromSeconds(3)));
        var shorter = InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(30)));
        Assert.Equal((TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(30)),
            (longer.ForCurrentThread().MatchTimeout, shorter.ForCurrentThread().MatchTimeout));
    }

    [Fact]
    // Two Singleton validators over one pattern, each first run on a thread of its own: had they shared the original,
    // the two threads would bounce its one idle runner between them, and allocate a runner whenever they collide.
    public void RulesOverOnePattern_TheirFirstThreads_MatchOnDifferentInstances()
    {
        var pattern = Unique();
        var first = InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.None));
        var second = InstancesOf(new RegexValidator<Person>(pattern, RegexOptions.None));
        var (onFirst, onSecond) = OnTwoLiveThreads(first.ForCurrentThread, second.ForCurrentThread);
        Assert.NotSame(onFirst, onSecond);
    }

    [Fact]
    public void ConstantPatternThatDoesNotParse_ThrowsWhereItsRuleIsDeclared()
    {
        Assert.Throws<RegexParseException>(() => new Validator<Person>(x => x.Property(person => person.Name).Regex("[")));
        Assert.Throws<RegexParseException>(() => new Validator<Person>(x => x.Property(person => person.Name).Regex("[")));
    }

    [Fact]
    public void Cache_AtItsLimit_BuildsWithoutKeeping()
    {
        var cache = new ConstantPatterns(1);
        var kept = cache.GetOrAdd("^a$", RegexOptions.None, Regex.InfiniteMatchTimeout);
        var past = cache.GetOrAdd("^b$", RegexOptions.None, Regex.InfiniteMatchTimeout);

        Assert.Same(kept, cache.GetOrAdd("^a$", RegexOptions.None, Regex.InfiniteMatchTimeout));
        Assert.NotSame(past, cache.GetOrAdd("^b$", RegexOptions.None, Regex.InfiniteMatchTimeout));
    }

    // A pattern no other test uses, so no other thread has claimed its original.
    private static string Unique() => "^shared-" + Guid.NewGuid().ToString("N") + "$";

    private static PatternInstances InstancesOf(RegexValidator<Person> rule) =>
        (PatternInstances)typeof(RegexValidator<Person>)
            .GetField("_instances", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rule)!;

    // The first thread stays alive while the second picks: a managed thread id is reused once its thread ends, and the
    // second thread would then pass for the owner of the original.
    private static (Regex First, Regex Second) OnTwoLiveThreads(Func<Regex> first, Func<Regex> second)
    {
        using var firstPicked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Regex? onFirst = null, onSecond = null;

        var holder = new Thread(() =>
        {
            onFirst = first();
            firstPicked.Set();
            release.Wait();
        });

        holder.Start();
        firstPicked.Wait();

        var other = new Thread(() => onSecond = second());
        other.Start();
        other.Join();
        release.Set();
        holder.Join();

        return (onFirst!, onSecond!);
    }
}