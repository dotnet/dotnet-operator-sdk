// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// Defines a generic additional printer column.
/// With this, other elements (such as Metadata.Name)
/// can be referenced. In contrast to the <see cref="AdditionalPrinterColumnAttribute"/>,
/// all needed information must be provided.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class GenericAdditionalPrinterColumnAttribute : Attribute
{
    /// <summary>
    /// Create a generic additional printer column.
    /// </summary>
    /// <param name="jsonPath">JsonPath as in <see cref="JsonPath"/>.</param>
    /// <param name="name">Name as in <see cref="Name"/>.</param>
    /// <param name="type">Type as in <see cref="Type"/>.</param>
    public GenericAdditionalPrinterColumnAttribute(string jsonPath, string name, string type)
    {
        JsonPath = jsonPath;
        Name = name;
        Type = type;
    }

    /// <summary>
    /// The json path for the property inside the resource.
    /// <example>.spec.replicas</example>
    /// <example>.metadata.namespace</example>
    /// <example>.metadata.creationTimestamp</example>
    /// </summary>
    public string JsonPath { get; }

    /// <summary>
    /// The name of the column.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Description for the column.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The type of the column.
    /// As documented in
    /// <a href="https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#type">https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#type</a>.
    /// The type field can be any of the following (from OpenAPI v3 data types):
    /// <list type="bullet">
    /// <item>
    /// <description>`integer` - non-floating-point number</description>
    /// </item>
    /// <item>
    /// <description>`number` - floating point number</description>
    /// </item>
    /// <item>
    /// <description>`string` - strings</description>
    /// </item>
    /// <item>
    /// <description>`boolean`- `true` or `false`</description>
    /// </item>
    /// <item>
    /// <description>`date` - rendered differentially as time since this timestamp</description>
    /// </item>
    /// </list>
    /// If the value inside a CustomResource does not match the type specified for the column, the value is omitted.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// The format of the column.
    /// As documented in
    /// <a href="https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#format">https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#format</a>.
    /// The format field can be any of the following:
    /// <list type="bullet">
    /// <item>
    /// <description>`int32`</description>
    /// </item>
    /// <item>
    /// <description>`int64`</description>
    /// </item>
    /// <item>
    /// <description>`float`</description>
    /// </item>
    /// <item>
    /// <description>`double`</description>
    /// </item>
    /// <item>
    /// <description>`byte`</description>
    /// </item>
    /// <item>
    /// <description>`date`</description>
    /// </item>
    /// <item>
    /// <description>`date-time`</description>
    /// </item>
    /// <item>
    /// <description>`password`</description>
    /// </item>
    /// </list>
    /// </summary>
    public string? Format { get; init; }

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
    public PrinterColumnPriority Priority { get; init; }
}
