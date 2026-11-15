using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Sharp.Validator.Result;

/// <summary>
/// The entries of a <see cref="ValidationResult" />: a read-only list in the order they were reported. A <c>foreach</c> over it allocates nothing;
/// through an interface it enumerates like any <see cref="ReadOnlyCollection{T}" />.
/// </summary>
public sealed class ValidationErrorCollection : ReadOnlyCollection<ValidationError>
{
    // A result's own list, which nothing outside this assembly reaches, so the collection never changes. List reads
    // Items back with Unsafe.As, so this parameter must stay a List<ValidationError>: widened, it would still compile.
    internal ValidationErrorCollection(List<ValidationError> errors) : base(errors)
    {
    }

    // Items is the list the constructor took, so it needs no type check: on x64 the JIT's check made a Count read
    // slower than the guarded IList<T> call it replaced, where the JIT's profile already named the list's type.
    internal List<ValidationError> List => Unsafe.As<List<ValidationError>>(Items);

    // ReadOnlyCollection reads Count through IList<T>. Native AOT keeps that an interface call, and the JIT makes it
    // direct only when the profile of ReadOnlyCollection's shared code names this list's type. Reading the list itself,
    // as GetEnumerator does, needs neither. The indexer stays inherited: the base one returns T, so a new one would
    // have another signature, and reflection's GetProperty("Item") would find both and throw AmbiguousMatchException.

    /// <summary>The number of entries.</summary>
    public new int Count => List.Count;

    /// <summary>Returns an enumerator that walks the entries without allocating.</summary>
    public new Enumerator GetEnumerator() => new(List);

    /// <summary>Walks the entries of a <see cref="ValidationErrorCollection" /> without allocating.</summary>
    public struct Enumerator : IEnumerator<ValidationError>
    {
        private readonly List<ValidationError> _errors;

        private int _index;

        internal Enumerator(List<ValidationError> errors)
        {
            _errors = errors;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly ValidationError Current => _errors[_index];

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++_index < _errors.Count;

        void IEnumerator.Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}