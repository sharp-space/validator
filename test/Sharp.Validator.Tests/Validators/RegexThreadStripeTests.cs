using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Sharp.Validator.Tests.Validators;

[Collection(GlobalOptions.Name)]
// Regex keeps one idle runner per instance, so a pattern this library owns gets further instances for other threads.
// Those are built from the first one, and a copy that lost its options or its timeout would answer differently, or
// hang, only under load. Each test runs the rule from several threads at once so the copies really are used.
public sealed class RegexThreadStripeTests
{
    private static void OnThreads(int count, Action action)
    {
        using var start = new Barrier(count);
        var failures = new Exception?[count];
        var threads = Enumerable.Range(0, count).Select(index => new Thread(() =>
        {
            start.SignalAndWait();

            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures[index] = exception;
            }
        })).ToArray();

        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        Assert.All(failures, Assert.Null);
    }

    [Fact]
    public void Options_HoldOnEveryThread()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex("^abc$", RegexOptions.IgnoreCase));

        OnThreads(16, () =>
        {
            Assert.True(validator.Validate(new Person { Name = "ABC" }).IsValid);
            Assert.False(validator.Validate(new Person { Name = "abd" }).IsValid);
        });
    }

    [Fact]
    public void MatchTimeout_HoldsOnEveryThread()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .Regex("^(a+)+$", RegexOptions.None, TimeSpan.FromMilliseconds(20)));

        var hostile = new Person { Name = new string('a', 35) + "!" };
        OnThreads(4, () => Assert.False(validator.Validate(hostile).IsValid));
    }

    [Fact]
    public void Template_ReadsThePatternOnEveryThread()
    {
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .Regex("^a$", RegexOptions.None, options => options.ErrorMessage = "{RegularExpression}"));

        OnThreads(16, () => Assert.Equal("^a$", validator.Validate(new Person { Name = "b" }).Errors[0].Message));
    }

    [Fact]
    // One copy per core grows with the machine, and a Compiled pattern compiles every copy. Past 64 cores, cores
    // share a copy instead. Run under DOTNET_PROCESSOR_COUNT=96 to see the cap bite: 96 cores round up to 128
    // slots without it.
    public void Pattern_KeepsAtMost64Copies() =>
        Assert.Equal((int)BitOperations.RoundUpToPowerOf2((uint)Math.Min(Environment.ProcessorCount, 64)),
            Fresh().Copies(null).Length);

    [Fact]
    // Which thread owns the original is decided by a compare-and-swap that only a real race can lose, and a stress test
    // cannot promise one. The claim takes the owner it observed, so the three outcomes are checked without any threads.
    public void OnlyTheFirstClaimOnTheOriginalInstance_Succeeds()
    {
        var instances = Fresh();
        Assert.True(instances.ClaimOriginal(1, 0));
        Assert.False(instances.ClaimOriginal(2, 0)); // saw no owner, then lost the race
        Assert.False(instances.ClaimOriginal(3, 1)); // saw the owner
    }

    [Fact]
    // The array the copies live in is published by another compare-and-swap that only a real race can lose, and a
    // stress test that loses it only some of the time leaves the coverage gate to chance. It takes the array the caller
    // observed, so every thread is shown to share the first published array without any threads.
    public void OnlyTheFirstArrayOfCopies_IsPublished()
    {
        var instances = Fresh();
        var published = instances.Copies(null); // saw none, and published one
        Assert.Same(published, instances.Copies(null)); // saw none, then lost to the published one
        Assert.Same(published, instances.Copies(published)); // saw the published one
    }

    // Instances of their own, outside the shared cache, so no other test has claimed their original or their copies.
    private static PatternInstances Fresh()
    {
        var key = ("^a$", RegexOptions.None, Regex.InfiniteMatchTimeout);
        return new(key, Create, Create(key));

        static Regex Create((string Pattern, RegexOptions Options, TimeSpan Timeout) key) =>
            new(key.Pattern, key.Options, key.Timeout);
    }

    [Fact]
    public void PrebuiltRegex_KeepsItsOwnInstance()
    {
        // The caller's Regex may be a [GeneratedRegex] subclass, which can't be rebuilt from its pattern.
        var regex = new Regex("^a$", RegexOptions.None, TimeSpan.FromSeconds(1));
        var validator = new Validator<Person>(x => x.Property(person => person.Name)
            .Regex(regex, options => options.ErrorMessage = "{RegularExpression}"));

        OnThreads(4, () => Assert.Equal("^a$", validator.Validate(new Person { Name = "b" }).Errors[0].Message));
    }

    [Theory]
    [InlineData("^[a-z]+$", RegexOptions.IgnoreCase)]
    [InlineData("(?i)^[a-z]+$", RegexOptions.None)]
    // Case-insensitive matching, from the option or from (?i), takes its casing table from the culture current when a
    // Regex is built, and a copy is built later on whichever thread first needs one. Every copy ignores case in the
    // invariant culture, so no thread's culture, at construction or since, changes a verdict: "I" matches [a-z] and the
    // Turkish "İ" doesn't.
    public void Thread_IgnoresCaseInTheInvariantCulture(string pattern, RegexOptions options)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            // A comment of its own makes this a pattern no other test builds, so its original is built
            // here, under tr-TR.
            var unique = pattern + "(?#" + nameof(Thread_IgnoresCaseInTheInvariantCulture) + ")";
            var validator = new Validator<Person>(x => x.Property(person => person.Name).Regex(unique, options));
            Assert.True(validator.Validate(new Person { Name = "I" }).IsValid);

            OnThreads(16, () =>
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

                Assert.True(validator.Validate(new Person { Name = "I" }).IsValid);
                Assert.False(validator.Validate(new Person { Name = "\u0130" }).IsValid);
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}