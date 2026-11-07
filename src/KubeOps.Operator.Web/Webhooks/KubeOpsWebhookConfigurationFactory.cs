// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Webhooks;

namespace KubeOps.Operator.Web.Webhooks;

/// <summary>
/// Default implementation of <see cref="IWebhookConfigurationFactory"/> that constructs
/// mutating and validating webhook configuration resources.
/// </summary>
public class KubeOpsWebhookConfigurationFactory : IWebhookConfigurationFactory
{
    /// <inheritdoc />
    public virtual V1MutatingWebhookConfiguration CreateMutatingConfiguration(
        IEnumerable<MutatingWebhookRegistration> registrations)
    {
        var webhooks = registrations.Select(CreateMutatingWebhook).ToList();
        EnsureUniqueNames(webhooks.Select(w => w.Name));

        return new V1MutatingWebhookConfiguration()
        {
            Metadata = new() { Name = "dev-mutators" },
            Webhooks = webhooks,
        }.Initialize();
    }

    /// <inheritdoc />
    public virtual V1ValidatingWebhookConfiguration CreateValidatingConfiguration(
        IEnumerable<ValidatingWebhookRegistration> registrations)
    {
        var webhooks = registrations.Select(CreateValidatingWebhook).ToList();
        EnsureUniqueNames(webhooks.Select(w => w.Name));

        return new V1ValidatingWebhookConfiguration()
        {
            Metadata = new() { Name = "dev-validators" },
            Webhooks = webhooks,
        }.Initialize();
    }

    /// <summary>
    /// Creates a single <see cref="V1MutatingWebhook"/> from the given registration.
    /// Override this method to customize individual mutating webhook entries.
    /// </summary>
    /// <param name="reg">The mutating webhook registration data.</param>
    /// <returns>A configured <see cref="V1MutatingWebhook"/> instance.</returns>
    protected virtual V1MutatingWebhook CreateMutatingWebhook(MutatingWebhookRegistration reg) => new()
    {
        Name = $"mutate.{reg.Metadata.SingularName}.{Defaulted(reg.Metadata.Group, "core")}.{reg.Metadata.Version}",
        MatchPolicy = "Exact",
        AdmissionReviewVersions = ["v1"],
        SideEffects = "None",
        Rules =
        [
            new V1RuleWithOperations
            {
                Operations = ["*"],
                Resources = [reg.Metadata.PluralName],
                ApiGroups = [reg.Metadata.Group],
                ApiVersions = [reg.Metadata.Version],
            },
        ],
        ClientConfig = new()
        {
            Url = reg.Uri.ToString(),
            CaBundle = reg.CaBundle,
        },
    };

    /// <summary>
    /// Creates a single <see cref="V1ValidatingWebhook"/> from the given registration.
    /// Override this method to customize individual validating webhook entries.
    /// </summary>
    /// <param name="reg">The validating webhook registration data.</param>
    /// <returns>A configured <see cref="V1ValidatingWebhook"/> instance.</returns>
    protected virtual V1ValidatingWebhook CreateValidatingWebhook(ValidatingWebhookRegistration reg) => new()
    {
        Name = $"validate.{reg.Metadata.SingularName}.{Defaulted(reg.Metadata.Group, "core")}.{reg.Metadata.Version}",
        MatchPolicy = "Exact",
        AdmissionReviewVersions = ["v1"],
        SideEffects = "None",
        Rules =
        [
            new V1RuleWithOperations
            {
                Operations = ["*"],
                Resources = [reg.Metadata.PluralName],
                ApiGroups = [reg.Metadata.Group],
                ApiVersions = [reg.Metadata.Version],
            },
        ],
        ClientConfig = new()
        {
            Url = reg.Uri.ToString(),
            CaBundle = reg.CaBundle,
        },
    };

    /// <summary>
    /// The API server rejects a webhook configuration with duplicate webhook names. Fail early with the name instead.
    /// The names are derived from the entity, so a duplicate means two webhooks of the same type handle entities with
    /// the same kind, group and version.
    /// </summary>
    private static void EnsureUniqueNames(IEnumerable<string> names)
    {
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate webhook names: {string.Join(", ", duplicates)}. Each entity kind, group and version " +
                "may have only one validation and one mutation webhook.");
        }
    }

    private static string Defaulted(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}
