// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// Marks an assembly as containing KubeOps source-generated registration classes.
/// </summary>
/// <remarks>
/// <para>
/// The KubeOps source generator applies this attribute automatically to every assembly that
/// contains controllers or finalizers; it is not meant to be applied manually.
/// </para>
/// <para>
/// The referenced registration classes register only the components that are defined in the
/// annotated assembly itself (they never chain into further assemblies). A consuming compilation
/// discovers these attributes on all (transitively) referenced assemblies and invokes every
/// registration class exactly once from its own generated <c>RegisterComponents</c> method.
/// This flat composition guarantees that no component is registered twice, regardless of how the
/// assemblies reference each other.
/// </para>
/// </remarks>
/// <param name="controllerRegistrations">
/// The fully qualified name (without <c>global::</c>) of the generated class whose
/// <c>RegisterControllers</c> method registers the controllers of the annotated assembly,
/// or <c>null</c> if the assembly contains no controllers.
/// </param>
/// <param name="finalizerRegistrations">
/// The fully qualified name (without <c>global::</c>) of the generated class whose
/// <c>RegisterFinalizers</c> method registers the finalizers of the annotated assembly,
/// or <c>null</c> if the assembly contains no finalizers.
/// </param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class KubeOpsGeneratedRegistrationsAttribute(
    string? controllerRegistrations,
    string? finalizerRegistrations) : Attribute
{
    /// <summary>
    /// Gets the fully qualified name (without <c>global::</c>) of the generated class whose
    /// <c>RegisterControllers</c> method registers the controllers of the annotated assembly,
    /// or <c>null</c> if the assembly contains no controllers.
    /// </summary>
    public string? ControllerRegistrations { get; } = controllerRegistrations;

    /// <summary>
    /// Gets the fully qualified name (without <c>global::</c>) of the generated class whose
    /// <c>RegisterFinalizers</c> method registers the finalizers of the annotated assembly,
    /// or <c>null</c> if the assembly contains no finalizers.
    /// </summary>
    public string? FinalizerRegistrations { get; } = finalizerRegistrations;
}
