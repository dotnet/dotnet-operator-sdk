// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Webhooks.Admission.Mutation;

/// <summary>
/// Defines an MVC controller as "mutation webhook". The route is automatically set to
/// <c>/mutate/[lower-case-name-of-the-type]</c>.
/// This must be used in conjunction with the <see cref="MutationWebhook{TEntity}"/> class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class MutationWebhookAttribute(Type entityType) : RouteAttribute($"/mutate/{entityType.Name.ToLowerInvariant()}");
