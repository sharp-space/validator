namespace Sharp.Validator.Tests.Pipeline;

// Pins the element-outer / validator-inner evaluation order for Elements() rules: each element is run against every
// validator before moving to the next element. The IList<T> fast path (which replaced Enumerable.Index() to drop the
// per-call iterator allocation) must preserve this exact ordering and the CascadeMode.Stop short-circuit. The
// pre-existing suite only asserted error *counts* for multi-validator element rules, so a transposed nesting would
// have slipped through.
public sealed class ElementsMultiValidatorOrderTests
{
    [Fact]
    public void ElementsContinueMode_ReportsErrorsElementOuter()
    {
        var validator = new Validator<Person>(builder => builder.Elements(x => x.Tags!)
            .NotEmpty()
            .MinimumLength(3));

        // "xx" passes NotEmpty but fails MinimumLength(3); "" fails both.
        var result = validator.Validate(new Person { Tags = ["xx", ""] });

        // Element-outer order: Tags[0] (MinimumLength), then Tags[1] (NotEmpty, MinimumLength).
        // A validator-outer layout would instead lead with Tags[1] (NotEmpty scanned across all elements first).
        Assert.Collection(result.Errors,
            error => Assert.Equal("Tags[0]", error.PropertyName),
            error => Assert.Equal("Tags[1]", error.PropertyName),
            error => Assert.Equal("Tags[1]", error.PropertyName));
    }

    [Fact]
    public void ElementsCascadeStop_ReturnsFirstElementsFirstFailingRule()
    {
        var validator = new Validator<Person>(builder => builder
            .Elements(x => x.Tags!, x => x.CascadeMode = ValidationCascadeMode.Stop)
            .NotEmpty()
            .MinimumLength(3));

        var result = validator.Validate(new Person { Tags = ["xx", ""] });

        // Stop fires on the first failure encountered while walking element-outer: Tags[0]/MinimumLength.
        var item = Assert.Single(result.Errors);
        Assert.Equal("Tags[0]", item.PropertyName);
    }

    [Fact]
    public async Task ElementsAsyncContinueMode_ReportsErrorsElementOuter()
    {
        // Two async rules (no sync rules) so the errors come from the async elements evaluator, not EvaluateSync.
        var validator = new Validator<Person>(builder => builder.Elements(x => x.Tags!)
            .MustAsync(x => Task.FromResult(!string.IsNullOrEmpty(x))) // mirrors NotEmpty
            .MustAsync(x => Task.FromResult((x?.Length ?? 0) >= 3))); // mirrors MinimumLength(3)

        // "xx" passes the first rule but fails the second; "" fails both.
        var result = await validator.ValidateAsync(new Person { Tags = ["xx", ""] });
        Assert.Collection(result.Errors,
            error => Assert.Equal("Tags[0]", error.PropertyName),
            error => Assert.Equal("Tags[1]", error.PropertyName),
            error => Assert.Equal("Tags[1]", error.PropertyName));
    }
}