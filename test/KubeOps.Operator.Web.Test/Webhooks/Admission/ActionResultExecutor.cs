// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission;

internal static class ActionResultExecutor
{
    /// <summary>
    /// Executes the result against an in-memory response and returns its status code and body.
    /// </summary>
    public static async Task<(int StatusCode, string Body)> ExecuteAsync(IActionResult result)
    {
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await result.ExecuteResultAsync(new ActionContext { HttpContext = httpContext });

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, await reader.ReadToEndAsync());
    }
}
