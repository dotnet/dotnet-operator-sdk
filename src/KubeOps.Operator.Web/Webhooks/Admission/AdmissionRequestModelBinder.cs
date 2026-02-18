// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

using KubeOps.Operator.Serialization;

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace KubeOps.Operator.Web.Webhooks.Admission;

/// <summary>
/// A custom model binder responsible for binding admission request data
/// to the target model in webhook scenarios using the Kubernetes JSON serializer.
/// </summary>
/// <remarks>
/// The <see cref="AdmissionRequestModelBinder"/> handles binding request payloads
/// by extracting data from the HTTP request body or model value providers. It uses
/// Kubernetes' JSON serialization utilities to deserialize the request into the
/// expected model type.
/// </remarks>
public sealed class AdmissionRequestModelBinder : IModelBinder
{
    /// <summary>
    /// Asynchronously binds the model for an AdmissionRequest by extracting
    /// and deserializing data from the HTTP request, using Kubernetes JSON serialization utilities.
    /// </summary>
    /// <param name="bindingContext">
    /// The context for model binding, which provides access to the model name, value providers,
    /// and the HTTP request data.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous binding operation. Upon completion,
    /// the method sets the binding result in the provided <paramref name="bindingContext"/>.
    /// </returns>
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        string? value;
        var modelValue = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

        if (modelValue != ValueProviderResult.None)
        {
            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, modelValue);
            value = modelValue.FirstValue;
        }
        else
        {
            var httpContext = bindingContext.HttpContext;
            if (httpContext.Request.Body.CanSeek)
            {
                httpContext.Request.Body.Position = 0;
            }

            using var reader = new StreamReader(httpContext.Request.Body);
            value = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var jsonElement = KubernetesJsonSerializer.Deserialize<JsonElement>(value);
        var result = jsonElement.Deserialize(bindingContext.ModelType, KubernetesJsonSerializer.SerializerOptions);

        if (result is null)
        {
            return;
        }

        bindingContext.Result = ModelBindingResult.Success(result);
    }
}
