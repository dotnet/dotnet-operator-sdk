// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities;

/// <summary>
/// Specifies the priority of a column in an additional printer view.
/// </summary>
public enum PrinterColumnPriority
{
    /// <summary>
    /// The column is displayed in the standard view.
    /// </summary>
    StandardView,

    /// <summary>
    /// The column is displayed in the wide view.
    /// </summary>
    WideView,
}
