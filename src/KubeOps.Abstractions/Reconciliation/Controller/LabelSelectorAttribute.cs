// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Reconciliation.Controller;

/// <summary>
/// Declares the label selector to use for the watch of the annotated controller. The source generator
/// picks this attribute up and registers the controller via
/// <c>AddControllerWithLabelSelector&lt;TController, TEntity, TLabelSelector&gt;</c>, so the selector
/// is applied without manual registration code.
/// </summary>
/// <param name="selectorType">
/// The selector implementation. Must implement <c>IEntityLabelSelector&lt;TEntity&gt;</c> for the
/// entity type the controller reconciles.
/// </param>
/// <example>
/// <code>
/// [LabelSelector(typeof(ManagedEntityLabelSelector))]
/// public class ManagedEntityController : IEntityController&lt;V1ManagedEntity&gt;
/// {
///     // ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LabelSelectorAttribute(Type selectorType) : Attribute
{
    /// <summary>
    /// Gets the label selector implementation type.
    /// </summary>
    public Type SelectorType => selectorType;
}
