// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Transpiler;

/// <summary>
/// Recovers the effective property values of an attribute type whose values are produced inside
/// its constructor rather than supplied at the application site. The classic case is a reusable,
/// named attribute that derives from a configurable base attribute and forwards constants to the
/// base constructor (e.g. <c>ReadyPrinterColumnAttribute : GenericAdditionalPrinterColumnAttribute</c>).
/// Such values are not present in the attribute-application metadata blob, so they cannot be read
/// through <see cref="System.Reflection.CustomAttributeData"/>.
/// </summary>
/// <remarks>
/// The seam is deliberately attribute-agnostic: it returns every resolved property keyed by name,
/// so any current or future CRD-shaping attribute can recover inherited values through the same
/// mechanism. Each host supplies the strongest tool it has — real reflection at runtime (the
/// attribute is instantiated) or the Roslyn semantic model at build time (the base-constructor
/// constants are read from source).
/// </remarks>
public interface IInheritedAttributeResolver
{
    /// <summary>
    /// Attempts to materialize the effective property values of <paramref name="attributeType"/>.
    /// </summary>
    /// <param name="attributeType">The (read-only reflected) attribute type to inspect.</param>
    /// <param name="propertyValues">
    /// The resolved property values keyed by property name (ordinal). Empty when resolution fails.
    /// </param>
    /// <returns><see langword="true"/> when at least one value was resolved; otherwise <see langword="false"/>.</returns>
    bool TryResolve(Type attributeType, out IReadOnlyDictionary<string, object?> propertyValues);
}
