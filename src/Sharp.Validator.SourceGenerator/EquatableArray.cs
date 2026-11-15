using System;
using System.Collections.Immutable;

namespace Sharp.Validator.SourceGenerator;

/// <summary>
/// Value-equatable wrapper over <see cref="ImmutableArray{T}" />. The incremental generator compares each pipeline step's output to decide what to
/// re-run; a bare <see cref="ImmutableArray{T}" /> compares by reference, so an unchanged-but-rebuilt array would invalidate the cache and re-run
/// emission on every keystroke. Comparing by content lets unchanged results stay cached. (Hand-written equality/hashing keeps this netstandard2.0-clean
/// — no System.HashCode.)
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    public ImmutableArray<T> AsImmutableArray() => array.IsDefault ? ImmutableArray<T>.Empty : array;

    public bool Equals(EquatableArray<T> other)
    {
        var left = AsImmutableArray();
        var right = other.AsImmutableArray();

        if (left.Length != right.Length) return false;

        for (var index = 0; index < left.Length; index++)
            if (!left[index].Equals(right[index]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var immutableArray = AsImmutableArray();

        unchecked
        {
            var hash = 17;
            for (var index = 0; index < immutableArray.Length; index++)
                hash = (hash * 31) + immutableArray[index].GetHashCode();

            return hash;
        }
    }
}