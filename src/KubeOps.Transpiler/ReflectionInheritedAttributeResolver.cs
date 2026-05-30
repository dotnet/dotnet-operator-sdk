// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Reflection;

namespace KubeOps.Transpiler;

/// <summary>
/// Resolves inherited attribute values by instantiating the attribute via real reflection. This is
/// the correct mechanism whenever the attribute's assembly is already loaded into the current
/// process (operator runtime, unit tests): the constructor runs, executes its <c>base(...)</c> call,
/// and the resulting property values are read back. It does <b>not</b> rely on the IL layout of the
/// constructor and works for any attribute type regardless of which properties it exposes.
/// </summary>
public sealed class ReflectionInheritedAttributeResolver : IInheritedAttributeResolver
{
    /// <summary>
    /// A shared, stateless default instance.
    /// </summary>
    public static readonly ReflectionInheritedAttributeResolver Default = new();

    /// <inheritdoc />
    public bool TryResolve(Type attributeType, out IReadOnlyDictionary<string, object?> propertyValues)
    {
        propertyValues = ReadOnlyDictionary<string, object?>.Empty;

        var runtimeType = ResolveLoadedType(attributeType);
        if (runtimeType is null)
        {
            return false;
        }

        if (Activator.CreateInstance(runtimeType, nonPublic: true) is not Attribute instance)
        {
            return false;
        }

        propertyValues = runtimeType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true } && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, p => p.GetValue(instance), StringComparer.Ordinal);
        return propertyValues.Count > 0;
    }

    private static Type? ResolveLoadedType(Type readOnlyReflectedType)
    {
        if (readOnlyReflectedType.AssemblyQualifiedName is { } aqn && Type.GetType(aqn, throwOnError: false) is { } byAqn)
        {
            return byAqn;
        }

        if (readOnlyReflectedType.FullName is not { } fullName)
        {
            return null;
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
    }
}
