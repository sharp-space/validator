using System.Collections;

namespace Sharp.Validator.Result.Tests.Results;

// Errors became a read-only view, and a foreach over it, typed as IReadOnlyList, went through
// ReadOnlyCollection.GetEnumerator, which boxed the list's enumerator on the heap: 40 B on every failed validation that
// walked its errors, where the JIT had kept the box on the stack while Errors was the list itself. Errors is now a
// ValidationErrorCollection, a ReadOnlyCollection with a struct enumerator, so foreach allocates nothing.
public sealed class ErrorCollectionTests
{
    private static readonly ValidationError Error = new("Name", "'Name' must not be empty.");

    [Fact]
    public void EnumeratingErrors_WithForeach_AllocatesNothing()
    {
        var result = new ValidationResult([Error, Error]);
        _ = Count(result);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = Count(result);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(2, count);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Errors_StillReadsAsAReadOnlyList()
    {
        var result = new ValidationResult([Error]);
        IReadOnlyList<ValidationError> list = result.Errors;
        Assert.Equal(Error, list[0]);
        Assert.Equal(["Name"], list.Select(error => error.PropertyName));
        Assert.Throws<NotSupportedException>(() => ((ICollection<ValidationError>)result.Errors).Add(Error));
        Assert.Empty(ValidationResult.Empty.Errors);
    }

    [Fact]
    // The collection's own Count reads its list directly instead of through ReadOnlyCollection's IList<T> call, so it
    // must answer as the interfaces do.
    public void Count_AnswersAsTheInterfacesDo()
    {
        var errors = new ValidationResult([Error, Error]).Errors;
        IReadOnlyCollection<ValidationError> collection = errors;
        var emptyCount = ValidationResult.Empty.Errors.Count;

        Assert.Equal(2, errors.Count);
        Assert.Equal(collection.Count, errors.Count);
        Assert.Equal(0, emptyCount);
    }

    [Fact]
    // Reflection-based tools such as serializers and object mappers look members up by name. Count hides the inherited
    // one with the same signature, so the lookup still finds one. A new indexer couldn't: the inherited one returns T,
    // and GetProperty("Item") would find both and throw.
    public void Reflection_ErrorsView_HasOneCountOneIndexer()
    {
        var type = typeof(ValidationErrorCollection);
        Assert.Equal(type, type.GetProperty(nameof(ValidationErrorCollection.Count))?.DeclaringType);
        Assert.NotNull(type.GetProperty("Item"));
    }

    private static int Count(ValidationResult result)
    {
        var count = 0;
        foreach (var error in result.Errors)
            if (error.PropertyName.Length > 0)
                count++;

        return count;
    }

    [Fact]
    public void BoxedStructEnumerator_Reset_ReplaysErrors()
    {
        var error = new ValidationError("Name");

        // ReSharper disable once GenericEnumeratorNotDisposed — the boxed enumerator under test holds no resources.
        IEnumerator enumerator = new ValidationResult([error]).Errors.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Same(error, enumerator.Current);
        Assert.False(enumerator.MoveNext());

        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Same(error, enumerator.Current);
    }
}