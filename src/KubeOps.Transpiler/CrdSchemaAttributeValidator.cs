// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

using k8s.Models;

using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.Transpiler.Exceptions;

namespace KubeOps.Transpiler;

internal static class CrdSchemaAttributeValidator
{
    private const string ObjectSchemaType = "object";
    private const string ArraySchemaType = "array";

    private static readonly ISet<string> AllowedValidationReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "FieldValueRequired",
        "FieldValueForbidden",
        "FieldValueInvalid",
        "FieldValueDuplicate",
    };

    private static readonly ISet<string> AllowedPrinterColumnTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "integer",
        "number",
        "string",
        "boolean",
        "date",
    };

    private static readonly ISet<string> AllowedPrinterColumnFormats = new HashSet<string>(StringComparer.Ordinal)
    {
        "int32",
        "int64",
        "float",
        "double",
        "byte",
        "date",
        "date-time",
        "password",
    };

    public static void Validate(PropertyInfo prop, V1JSONSchemaProps props, MetadataLoadContext context)
    {
        ValidateUniqueItems(prop);
        ValidateListTopologyAttributes(prop, props, context);
        ValidateMapTopologyAttribute(prop, props);
        ValidateValidationRuleAttributes(prop, prop.GetCustomAttributesData<ValidationRuleAttribute>(), context);
    }

    public static void ValidatePrinterColumn(
        MemberInfo source,
        V1CustomResourceColumnDefinition column,
        string attributeName)
    {
        // Rule: additionalPrinterColumns name is required. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        if (string.IsNullOrEmpty(column.Name))
        {
            throw InvalidSchemaAttribute(source, attributeName, "Additional printer column name is required.");
        }

        // Rule: additionalPrinterColumns type is required and must be one of the Kubernetes column types. See:
        // https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#type
        if (column.Type is null || !AllowedPrinterColumnTypes.Contains(column.Type))
        {
            throw InvalidSchemaAttribute(
                source,
                attributeName,
                $"Additional printer column '{column.Name}' requires type integer, number, string, boolean, or date, " +
                $"but generated type is '{column.Type ?? "<none>"}'.");
        }

        // Rule: additionalPrinterColumns format is optional, but must be one of the Kubernetes column formats.
        // Kubernetes treats an empty string as unset, so only a non-empty unknown format is rejected. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        if (!string.IsNullOrEmpty(column.Format) && !AllowedPrinterColumnFormats.Contains(column.Format))
        {
            throw InvalidSchemaAttribute(
                source,
                attributeName,
                $"Additional printer column '{column.Name}' has unsupported format '{column.Format}'.");
        }

        // Rule: additionalPrinterColumns JSONPath is required. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        if (string.IsNullOrEmpty(column.JsonPath))
        {
            throw InvalidSchemaAttribute(source, attributeName, "Additional printer column JSONPath is required.");
        }
    }

    public static void ValidateValidationRuleAttributes(
        MemberInfo source,
        IEnumerable<CustomAttributeData> validations,
        MetadataLoadContext context)
    {
        foreach (var validation in validations)
        {
            ValidateValidationRuleAttribute(source, validation, context);
        }
    }

    private static void ValidateUniqueItems(PropertyInfo prop)
    {
        if (prop.GetCustomAttributeData<UniqueItemsAttribute>() is null)
        {
            return;
        }

        // Rule: CRD schemas must not set uniqueItems=true. See:
        // https://k8s.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#validation
        throw InvalidSchemaAttribute(
            prop,
            nameof(UniqueItemsAttribute),
            "Kubernetes CRD schemas do not allow uniqueItems to be set to true. " +
            "Use [XListType(XListType.Set)] for Kubernetes set semantics.");
    }

    private static void ValidateListTopologyAttributes(
        PropertyInfo prop,
        V1JSONSchemaProps props,
        MetadataLoadContext context)
    {
        var listTypeAttribute = prop.GetCustomAttributeData<XListTypeAttribute>();
        var listMapKeysAttribute = prop.GetCustomAttributeData<XListMapKeysAttribute>();
        if (listTypeAttribute is null && listMapKeysAttribute is null)
        {
            return;
        }

        // Rule: x-kubernetes-list-type requires type=array. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        EnsureSchemaType(prop, nameof(XListTypeAttribute), props, ArraySchemaType);

        var listType = listTypeAttribute?.GetCustomAttributeCtorArg<XListType>(context, 0);
        var listMapKeys = listMapKeysAttribute?.GetCustomAttributeCtorArrayArg<string>(0) ?? [];
        if (listMapKeys.Count > 0 && listType != XListType.Map)
        {
            // Rule: list-map-keys requires x-kubernetes-list-type=map. See:
            // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
            throw InvalidSchemaAttribute(
                prop,
                nameof(XListMapKeysAttribute),
                $"{nameof(XListMapKeysAttribute)} requires [XListType(XListType.Map)].");
        }

        if (listType is null)
        {
            return;
        }

        var itemSchema = props.Items as V1JSONSchemaProps;
        if (itemSchema is null)
        {
            // Rule: map lists require a single item schema; set/map list item checks need that schema. See:
            // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
            throw InvalidSchemaAttribute(
                prop,
                nameof(XListTypeAttribute),
                "Kubernetes list topology requires an array item schema.");
        }

        if (listType is XListType.Set or XListType.Map && itemSchema.Nullable == true)
        {
            // Rule: set/map list items must not be nullable. See:
            // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
            throw InvalidSchemaAttribute(
                prop,
                nameof(XListTypeAttribute),
                $"Items of Kubernetes '{props.XKubernetesListType}' lists cannot be nullable.");
        }

        if (listType == XListType.Set)
        {
            ValidateSetListItemSchema(prop, itemSchema);
        }

        if (listType == XListType.Map)
        {
            ValidateMapListSchema(prop, itemSchema, listMapKeys);
        }
    }

    private static void ValidateSetListItemSchema(PropertyInfo prop, V1JSONSchemaProps itemSchema)
    {
        switch (itemSchema.Type)
        {
            // Rule: nested-array set items are valid as long as their list topology is atomic; an array
            // without an explicit x-kubernetes-list-type is implicitly atomic and accepted by Kubernetes. See:
            // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
            case ArraySchemaType when itemSchema.XKubernetesListType is null or "atomic":
            case ObjectSchemaType when itemSchema.XKubernetesMapType == "atomic":
                return;

            case ArraySchemaType:
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListTypeAttribute),
                    "Items of Kubernetes set lists must have atomic list topology, but the nested list item " +
                    $"declares x-kubernetes-list-type '{itemSchema.XKubernetesListType}'.");

            case ObjectSchemaType:
                // Rule: complex object set-list items must be atomic. KubeOps cannot currently express
                // x-kubernetes-map-type: atomic at item level, so such items always produce an invalid schema.
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListTypeAttribute),
                    "Set lists with object items require atomic item topology, which KubeOps cannot " +
                    "currently express at item level. Use scalar items or XListType.Atomic.");

            default:
                return;
        }
    }

    private static void ValidateMapListSchema(
        PropertyInfo prop,
        V1JSONSchemaProps itemSchema,
        IList<string> listMapKeys)
    {
        if (listMapKeys.Count == 0)
        {
            // Rule: map lists require non-empty list-map-keys. See:
            // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
            throw InvalidSchemaAttribute(
                prop,
                nameof(XListMapKeysAttribute),
                "Kubernetes map lists require at least one list-map key.");
        }

        if (itemSchema.Type != ObjectSchemaType)
        {
            // Rule: map-list items must be object schemas. See:
            // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
            throw InvalidSchemaAttribute(
                prop,
                nameof(XListTypeAttribute),
                $"Kubernetes map-list items must be '{ObjectSchemaType}', but generated item type is " +
                $"'{itemSchema.Type ?? "<none>"}'.");
        }

        var properties = itemSchema.Properties ?? new Dictionary<string, V1JSONSchemaProps>();
        var required = itemSchema.Required?.ToHashSet(StringComparer.Ordinal) ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in listMapKeys)
        {
            if (!seen.Add(key))
            {
                // Rule: map-list keys must be unique. See:
                // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListMapKeysAttribute),
                    $"Kubernetes map-list key '{key}' is duplicated.");
            }

            if (!properties.TryGetValue(key, out var keySchema))
            {
                // Rule: map-list keys must name item properties. See:
                // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListMapKeysAttribute),
                    $"Kubernetes map-list key '{key}' must be an item property.");
            }

            if (keySchema.Type is ObjectSchemaType or ArraySchemaType)
            {
                // Rule: map-list keys must be scalar. See:
                // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListMapKeysAttribute),
                    $"Kubernetes map-list key '{key}' must be scalar, but generated type is '{keySchema.Type}'.");
            }

            if (keySchema.Nullable.GetValueOrDefault())
            {
                // Rule: map-list keys must not be nullable. See:
                // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListMapKeysAttribute),
                    $"Kubernetes map-list key '{key}' cannot be nullable.");
            }

            if (!required.Contains(key))
            {
                // Rule: map-list keys must be required or defaulted. See:
                // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
                throw InvalidSchemaAttribute(
                    prop,
                    nameof(XListMapKeysAttribute),
                    $"Kubernetes map-list key '{key}' must be required. Add [Required] to the item property.");
            }
        }
    }

    private static void ValidateMapTopologyAttribute(PropertyInfo prop, V1JSONSchemaProps props)
    {
        if (prop.GetCustomAttributeData<XMapTypeAttribute>() is null)
        {
            return;
        }

        // Rule: x-kubernetes-map-type requires type=object. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        EnsureSchemaType(prop, nameof(XMapTypeAttribute), props, ObjectSchemaType);
    }

    private static void ValidateValidationRuleAttribute(
        MemberInfo source,
        CustomAttributeData validation,
        MetadataLoadContext context)
    {
        var rule = validation.GetCustomAttributeCtorArg<string>(context, 0);
        var fieldPath = validation.GetCustomAttributeCtorArg<string?>(context, 1);
        var message = validation.GetCustomAttributeCtorArg<string?>(context, 2);
        var reason = validation.GetCustomAttributeCtorArg<string?>(context, 4);

        // Rule: validation rule must not be empty. See:
        // https://k8s.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#validation-rules
        if (string.IsNullOrWhiteSpace(rule))
        {
            throw InvalidSchemaAttribute(
                source,
                nameof(ValidationRuleAttribute),
                "Validation rule must not be empty.");
        }

        // Note: Kubernetes treats an empty message, messageExpression or fieldPath as unset, so those empty
        // values are not rejected here; only malformed non-empty values are.

        // Rule: validation rule message must not contain line breaks. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        if (message?.Contains('\n', StringComparison.Ordinal) == true)
        {
            throw InvalidSchemaAttribute(
                source,
                nameof(ValidationRuleAttribute),
                "Validation rule message must not contain line breaks.");
        }

        // Rule: multiline validation rule needs an explicit message. See:
        // https://k8s.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#validation-rules
        if (rule.Contains('\n', StringComparison.Ordinal) && string.IsNullOrWhiteSpace(message))
        {
            throw InvalidSchemaAttribute(
                source,
                nameof(ValidationRuleAttribute),
                "Validation rule message must be specified if the rule contains line breaks.");
        }

        // Rule: validation rule reason must be one of the Kubernetes field-value reasons. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        if (reason is not null && !AllowedValidationReasons.Contains(reason))
        {
            throw InvalidSchemaAttribute(
                source,
                nameof(ValidationRuleAttribute),
                $"Validation rule reason '{reason}' is not supported.");
        }

        // Rule: validation rule fieldPath must not contain line breaks. See:
        // https://github.com/kubernetes/apiextensions-apiserver/blob/master/pkg/apis/apiextensions/validation/validation.go
        if (fieldPath?.Contains('\n', StringComparison.Ordinal) == true)
        {
            throw InvalidSchemaAttribute(
                source,
                nameof(ValidationRuleAttribute),
                "Validation rule fieldPath must not contain line breaks.");
        }
    }

    private static void EnsureSchemaType(
        PropertyInfo prop,
        string attributeName,
        V1JSONSchemaProps props,
        string expectedType)
    {
        if (props.Type != expectedType)
        {
            throw InvalidSchemaAttribute(
                prop,
                attributeName,
                $"{attributeName} requires schema type '{expectedType}', but generated type is " +
                $"'{props.Type ?? "<none>"}'.");
        }
    }

    private static InvalidTypeException InvalidSchemaAttribute(MemberInfo source, string attributeName, string message)
    {
        var sourceName = source is Type type
            ? type.FullName ?? type.Name
            : $"{source.DeclaringType?.FullName}.{source.Name}";

        return new($"The attribute '{attributeName}' on '{sourceName}' generates an invalid Kubernetes CRD schema. " +
                   message);
    }
}
