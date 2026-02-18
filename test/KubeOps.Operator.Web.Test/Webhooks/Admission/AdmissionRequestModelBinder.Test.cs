// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

using FluentAssertions;

using KubeOps.Operator.Web.Test.TestApp;
using KubeOps.Operator.Web.Webhooks.Admission;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace KubeOps.Operator.Web.Test.Webhooks.Admission;

public sealed class AdmissionRequestModelBinderTest
{
    private const string ModelName = "request";
    private readonly AdmissionRequestModelBinder _binder = new();

    [Theory(DisplayName = "BindModelAsync binds AdmissionRequest correctly")]
    [Trait("Category", "AdmissionRequestModelBinder")]
    [InlineData(typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>), "PT5M30S", 0, 5, 30)]
    [InlineData(typeof(AdmissionRequest<TestEntityWithTimeSpanConverter>), "05:30:00", 5, 30, 0)]
    public async Task BindModelAsync_WithValidAdmissionRequest_BindsExpectedRequest(
        Type modelType,
        string timeout,
        int hours,
        int minutes,
        int seconds)
    {
        const string expectedUid = "test-uid";
        const string expectedOperation = "CREATE";
        const string expectedValue = "some_value";

        var json = $$"""
                            {
                              "apiVersion": "admission.k8s.io/v1",
                              "kind": "AdmissionReview",
                              "request": {
                                "uid": "{{expectedUid}}",
                                "operation": "{{expectedOperation}}",
                                "object": {
                                  "apiVersion": "test.kubeops.dev/v1",
                                  "kind": "TestEntity",
                                  "metadata": {
                                    "name": "test-entity",
                                    "namespace": "default"
                                  },
                                  "spec": {
                                    "value": "{{expectedValue}}",
                                    "timeout": "{{timeout}}"
                                  }
                                },
                                "dryRun": true
                              }
                            }
                            """;

        var bindingContext = CreateBindingContext(json, modelType);

        await _binder.BindModelAsync(bindingContext);

        bindingContext.Result.IsModelSet.Should().BeTrue("a valid admission review should result in a bound model");
        bindingContext.Result.Model.Should().NotBeNull().And.BeOfType(modelType);
        var model = bindingContext.Result.Model!;

        if (modelType == typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>))
        {
            var result = (AdmissionRequest<TestEntityWithISODurationTimeSpan>)model;
            result.Request.Uid.Should().Be(expectedUid);
            result.Request.Operation.Should().Be(expectedOperation);
            result.Request.DryRun.Should().BeTrue();
            result.Request.Object!.Metadata.Name.Should().Be("test-entity");
            result.Request.Object.Spec.Value.Should().Be(expectedValue);
            result.Request.Object.Spec.Timeout.Should().Be(new(hours, minutes, seconds));
        }
        else if (modelType == typeof(AdmissionRequest<TestEntityWithTimeSpanConverter>))
        {
            var result = (AdmissionRequest<TestEntityWithTimeSpanConverter>)model;
            result.Request.Uid.Should().Be(expectedUid);
            result.Request.Operation.Should().Be(expectedOperation);
            result.Request.DryRun.Should().BeTrue();
            result.Request.Object!.Metadata.Name.Should().Be("test-entity");
            result.Request.Object.Spec.Value.Should().Be(expectedValue);
            result.Request.Object.Spec.Timeout.Should().Be(new(hours, minutes, seconds));
        }
    }

    [Fact(DisplayName = "BindModelAsync does not set model for empty value")]
    [Trait("Category", "AdmissionRequestModelBinder")]
    public async Task BindModelAsync_WithEmptyValue_DoesNotSetModel()
    {
        var bindingContext = CreateBindingContext(
            string.Empty,
            typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        await _binder.BindModelAsync(bindingContext);

        bindingContext.Result.IsModelSet.Should().BeFalse("an empty request body cannot be bound to an admission request");
        bindingContext.Result.Model.Should().BeNull();
    }

    [Fact(DisplayName = "BindModelAsync does not set model for missing value")]
    [Trait("Category", "AdmissionRequestModelBinder")]
    public async Task BindModelAsync_WithMissingValue_DoesNotSetModel()
    {
        var bindingContext = CreateBindingContext(
            null,
            typeof(AdmissionRequest<TestEntityWithISODurationTimeSpan>));

        await _binder.BindModelAsync(bindingContext);

        bindingContext.Result.IsModelSet.Should().BeFalse("a missing request body cannot be bound to an admission request");
        bindingContext.Result.Model.Should().BeNull();
    }

    private static DefaultModelBindingContext CreateBindingContext(string? json, Type modelType)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new(),
            new());

        return new()
        {
            ActionContext = actionContext,
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = ModelName,
            ModelState = actionContext.ModelState,
            ValueProvider = new TestValueProvider(json),
        };
    }

    private sealed class TestValueProvider : IValueProvider
    {
        private readonly string? _value;

        public TestValueProvider(string? value)
        {
            _value = value;
        }

        public bool ContainsPrefix(string prefix) => prefix == ModelName;

        public ValueProviderResult GetValue(string key)
        {
            if (key == ModelName && !string.IsNullOrEmpty(_value))
            {
                return new(new(_value), CultureInfo.InvariantCulture);
            }

            return ValueProviderResult.None;
        }
    }
}
