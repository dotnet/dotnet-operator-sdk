// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a property as an additional printer column.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class AdditionalPrinterColumnAttribute(PrinterColumnPriority priority = default, string? name = null)
    : Attribute
{
    /// <summary>
    /// The name of the column. Defaults to the property-name.
    /// </summary>
    public string? Name => name;

    /// <summary>
    /// The priority of the additional printer column.
    /// As documented in
    /// <a href="https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#priority">https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#priority</a>
    /// the following rules apply to priority:
    /// <list type="bullet">
    /// <item>
    /// <description>Columns with priority `0` are shown in standard view</description>
    /// </item>
    /// <item>
    /// <description>Columns with priority greater than `0` are shown only in wide view</description>
    /// </item>
    /// </list>
    /// </summary>
    public PrinterColumnPriority Priority => priority;
}
