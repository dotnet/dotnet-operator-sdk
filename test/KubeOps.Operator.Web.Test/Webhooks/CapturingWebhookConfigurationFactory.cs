// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s.Models;

using KubeOps.Abstractions.Webhooks;

namespace KubeOps.Operator.Web.Test.Webhooks;

internal sealed class CapturingWebhookConfigurationFactory : IWebhookConfigurationFactory
{
    public List<MutatingWebhookRegistration> MutatingRegistrations { get; } = [];

    public List<ValidatingWebhookRegistration> ValidatingRegistrations { get; } = [];

    public IEnumerable<byte[]> MutatingCaBundles => MutatingRegistrations.Select(r => r.CaBundle).OfType<byte[]>();

    public IEnumerable<byte[]> ValidatingCaBundles => ValidatingRegistrations.Select(r => r.CaBundle).OfType<byte[]>();

    public V1MutatingWebhookConfiguration CreateMutatingConfiguration(
        IEnumerable<MutatingWebhookRegistration> registrations)
    {
        MutatingRegistrations.AddRange(registrations);

        return new()
        {
            Metadata = new() { Name = "mutators" },
            Webhooks = [new() { Name = "mutate.test.kubeops.dev.v1" }],
        };
    }

    public V1ValidatingWebhookConfiguration CreateValidatingConfiguration(
        IEnumerable<ValidatingWebhookRegistration> registrations)
    {
        ValidatingRegistrations.AddRange(registrations);

        return new()
        {
            Metadata = new() { Name = "validators" },
            Webhooks = [new() { Name = "validate.test.kubeops.dev.v1" }],
        };
    }
}
