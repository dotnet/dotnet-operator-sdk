// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace KubeOps.Operator.Web.Webhooks;

/// <summary>
/// A webhook found by the <see cref="WebhookLoader"/>.
/// </summary>
/// <param name="Type">The webhook class.</param>
/// <param name="Entity">The entity type the webhook handles.</param>
internal sealed record WebhookType(TypeInfo Type, Type Entity);
