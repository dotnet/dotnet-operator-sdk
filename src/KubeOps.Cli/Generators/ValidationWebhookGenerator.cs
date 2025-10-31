// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Cli.Output;
using KubeOps.Cli.Transpilation;

namespace KubeOps.Cli.Generators;

internal sealed class ValidationWebhookGenerator
    (List<ValidationWebhook> webhooks, byte[] caBundle, OutputFormat format) : IConfigGenerator
{
    public void Generate(ResultOutput output)
    {
        if (webhooks.Count == 0)
        {
            return;
        }

        var validatorConfig = new V1ValidatingWebhookConfiguration
        {
            Metadata = new() { Name = "validators" },
            Webhooks = new List<V1ValidatingWebhook>(),
        }.Initialize();

        foreach (var hook in webhooks)
        {
            validatorConfig.Webhooks.Add(new()
            {
                Name = $"validate.{hook.Metadata.SingularName}.{hook.Metadata.Group}.{hook.Metadata.Version}",
                MatchPolicy = "Exact",
                AdmissionReviewVersions = new[] { "v1" },
                SideEffects = "None",
                Rules = new[]
                {
                    new V1RuleWithOperations
                    {
                        Operations = hook.GetOperations(),
                        Resources = new[] { hook.Metadata.PluralName },
                        ApiGroups = new[] { hook.Metadata.Group },
                        ApiVersions = new[] { hook.Metadata.Version },
                    },
                },
                ClientConfig = new()
                {
                    CaBundle = caBundle,
                    Service = new()
                    {
                        Name = "operator",
                        Path = hook.WebhookPath,
                    },
                },
            });
        }

        output.Add(
            $"validators.{format.GetFileExtension()}", validatorConfig);
    }
}
