// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Abstractions.Entities.Attributes;

/// <summary>
/// ValidationRule describes a validation according to the custom
/// resource defintion validation rules
/// at: https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#validation-rules.
/// </summary>
/// <remarks>
/// This attribute is used in Kubernetes Custom Resource Definitions (CRDs) to annotate
/// properties with specific validation constraints. These constraints can define the rules that
/// must be adhered to when setting values for the annotated property.
/// </remarks>
/// <param name="rule">
/// rule represents the expression which will be evaluated by CEL.
/// ref: https://github.com/google/cel-spec.
/// </param>
/// <param name="fieldPath">
/// fieldPath represents the field path returned when the validation fails.
/// It must be a relative JSON path (i.e. with array notation) scoped to the location of this x-kubernetes-validations
/// extension in the schema and refer to an existing field.
/// </param>
/// <param name="message">
/// message represents the message displayed when validation fails.
/// The message is required if the Rule contains line breaks.
/// The message must not contain line breaks.
/// If unset, the message is "failed rule: {Rule}".
/// </param>
/// <param name="messageExpression">
/// messageExpression declares a CEL expression that evaluates to the validation failure message that is returned when this rule fails.
/// Since messageExpression is used as a failure message, it must evaluate to a string. If both message and messageExpression are present
/// on a rule, then messageExpression will be used if validation fails.
/// </param>
/// <param name="reason">
/// reason provides a machine-readable validation failure reason that is returned to the caller when a request fails this validation rule.
/// </param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class ValidationRuleAttribute(
    string rule,
    string? fieldPath = null,
    string? message = null,
    string? messageExpression = null,
    string? reason = null) : Attribute
{
    public string Rule => rule;

    public string? Message => message;

    public string? MessageExpression => messageExpression;

    public string? Reason => reason;

    public string? FieldPath => fieldPath;
}
