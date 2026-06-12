// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Immutable;

namespace KubeOps.Generator.Discovery;

/// <summary>
/// An immutable array wrapper that implements structural (value-based) equality.
/// </summary>
/// <remarks>
/// <para>
/// Incremental generator pipeline models must compare by value so that equality can drive result
/// caching. <see cref="ImmutableArray{T}"/> compares by the underlying array reference, which would
/// defeat caching; this wrapper compares element by element instead. Roslyn ships an equivalent type
/// internally but does not expose it publicly, so it has to be re-declared here. This is a known,
/// repeatedly requested gap, see
/// <see href="https://github.com/dotnet/runtime/issues/77183">dotnet/runtime#77183</see> (make
/// <see cref="ImmutableArray{T}"/> itself value-equatable),
/// <see href="https://github.com/dotnet/runtime/issues/89318">dotnet/runtime#89318</see> (ship an
/// equatable collection type for incremental generators) and
/// <see href="https://github.com/dotnet/roslyn-analyzers/issues/6352">dotnet/roslyn-analyzers#6352</see>
/// (which notes that nearly all generator authors have to hand-roll this type).
/// </para>
/// <para>
/// Unlike Roslyn's internal version this type deliberately omits the <c>where T : IEquatable&lt;T&gt;</c>
/// constraint and compares elements via <see cref="EqualityComparer{T}.Default"/>. The constraint
/// would reject enum element types (an enum does not implement <see cref="IEquatable{T}"/>), and we
/// need <c>EquatableArray&lt;SyntaxKind&gt;</c> for the cached entity modifiers.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the elements stored in the array.</typeparam>
internal readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _array = array;

    public int Count => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array.IsDefault)
        {
            return other._array.IsDefault;
        }

        if (other._array.IsDefault || _array.Length != other._array.Length)
        {
            return false;
        }

        for (var i = 0; i < _array.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(_array[i], other._array[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefault)
        {
            return 0;
        }

        var hash = 17;
        foreach (var item in _array)
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator()
        => ((IEnumerable<T>)(_array.IsDefault ? ImmutableArray<T>.Empty : _array)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
