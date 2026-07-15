// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;
using KubeOps.Transpiler.Exceptions;
using KubeOps.Transpiler.Kubernetes;

namespace KubeOps.Transpiler;

/// <summary>
/// CRD transpiler for Kubernetes entities.
/// </summary>
public static class Crds
{
    private const string Integer = "integer";
    private const string Number = "number";
    private const string String = "string";
    private const string Boolean = "boolean";
    private const string Object = "object";
    private const string Array = "array";

    private const string Int32 = "int32";
    private const string Int64 = "int64";
    private const string Float = "float";
    private const string Double = "double";
    private const string Decimal = "decimal";
    private const string DateTime = "date-time";
    private const string Uuid = "uuid";

    private static readonly string[] IgnoredToplevelProperties = ["metadata", "apiversion", "kind"];

    private static readonly IReadOnlySet<Type> EmptyAncestors = new HashSet<Type>();

    /// <summary>
    /// Transpile a single type to a CRD.
    /// </summary>
    /// <param name="context">The <see cref="MetadataLoadContext"/>.</param>
    /// <param name="type">The type to convert.</param>
    /// <param name="inheritedAttributeResolver">
    /// Resolver for inherited attribute property values produced inside a constructor. Defaults to
    /// <see cref="ReflectionInheritedAttributeResolver"/>; the CLI supplies a Roslyn-based
    /// resolver so that no user code is executed during build-time transpilation.
    /// </param>
    /// <returns>The converted custom resource definition.</returns>
    public static V1CustomResourceDefinition Transpile(
        this MetadataLoadContext context,
        Type type,
        IInheritedAttributeResolver? inheritedAttributeResolver = null)
    {
        type = context.GetContextType(type);
        try
        {
            var (meta, scope) = context.ToEntityMetadata(type);
            var crd = new V1CustomResourceDefinition { Spec = new() }.Initialize();

            crd.Metadata.Name = $"{meta.PluralName}.{meta.Group}";
            crd.Spec.Group = meta.Group;

            crd.Spec.Names =
                new()
                {
                    Kind = meta.Kind,
                    ListKind = meta.ListKind,
                    Singular = meta.SingularName,
                    Plural = meta.PluralName,
                };
            crd.Spec.Scope = scope;
            if (type.GetCustomAttributeData<KubernetesEntityShortNamesAttribute>()?.ConstructorArguments[0].Value is
                ReadOnlyCollection<CustomAttributeTypedArgument> shortNames)
            {
                crd.Spec.Names.ShortNames = shortNames.Select(a => a.Value?.ToString()).ToList();
            }

            var version = new V1CustomResourceDefinitionVersion { Name = meta.Version, Served = true, Storage = true };
            var hasStatus = type.GetProperty("status", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null;
            var scaleAttr = type.GetCustomAttributeData<ScaleSubresourceAttribute>();

            if (hasStatus || scaleAttr != null)
            {
                version.Subresources = new()
                {
                    Status = hasStatus ? new() : null,
                    Scale = scaleAttr != null
                        ? new V1CustomResourceSubresourceScale
                        {
                            SpecReplicasPath = scaleAttr.GetCustomAttributeCtorArg<string>(context, 0)!,
                            StatusReplicasPath = scaleAttr.GetCustomAttributeCtorArg<string>(context, 1)!,
                            LabelSelectorPath = scaleAttr.GetCustomAttributeCtorArg<string>(context, 2),
                        }
                        : null,
                };
            }

            var validationRules = type.GetInheritedCustomAttributesData<ValidationRuleAttribute>().ToList();
            CrdSchemaAttributeValidator.ValidateValidationRuleAttributes(type, validationRules, context);

            version.Schema = new()
            {
                OpenAPIV3Schema = new()
                {
                    Type = Object,
                    Description =
                        type.GetCustomAttributeData<DescriptionAttribute>()?.GetCustomAttributeCtorArg<string>(context, 0),
                    Title =
                        type.GetCustomAttributeData<TitleAttribute>()?.GetCustomAttributeCtorArg<string>(context, 0),
                    Properties = type.GetProperties()
                        .Where(p => !IgnoredToplevelProperties.Contains(p.Name.ToLowerInvariant())
                                    && p.GetCustomAttributeData<IgnoreAttribute>() == null)
                        .Select(p => (Name: p.GetPropertyName(context), Schema: context.Map(p, EmptyAncestors)))
                        .OrderBy(t => t.Name, StringComparer.Ordinal)
                        .ToDictionary(t => t.Name, t => t.Schema),
                    Required = type.GetProperties()
                        .Where(p => !IgnoredToplevelProperties.Contains(p.Name.ToLowerInvariant())
                                    && p.GetCustomAttributeData<IgnoreAttribute>() == null
                                    && IsRequiredSpecProperty(p))
                        .Select(p => p.GetPropertyName(context))
                        .ToList() switch
                    {
                        { Count: > 0 } list => list,
                        _ => null,
                    },
                    XKubernetesValidations = context.MapValidationRules(validationRules),
                },
            };

            version.AdditionalPrinterColumns = context.MapPrinterColumns(type, inheritedAttributeResolver).ToList() switch
            {
                { Count: > 0 } l => l,
                _ => null,
            };
            crd.Spec.Versions = new List<V1CustomResourceDefinitionVersion> { version };

            return crd;
        }
        catch (Exception ex) when (ex is CircularTypeReferenceException or InvalidTypeException)
        {
            throw new TranspilationFailedException(
                $"Failed to transpile the CRD for entity '{type.FullName ?? type.Name}'. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Transpile a list of entities to CRDs and group them by version.
    /// </summary>
    /// <param name="context">The <see cref="MetadataLoadContext"/>.</param>
    /// <param name="types">The types to convert.</param>
    /// <param name="inheritedAttributeResolver">
    /// Resolver for inherited attribute property values produced inside a constructor. Defaults to
    /// <see cref="ReflectionInheritedAttributeResolver"/>; the CLI supplies a Roslyn-based
    /// resolver so that no user code is executed during build-time transpilation.
    /// </param>
    /// <returns>The converted custom resource definitions.</returns>
    public static IEnumerable<V1CustomResourceDefinition> Transpile(
        this MetadataLoadContext context,
        IEnumerable<Type> types,
        IInheritedAttributeResolver? inheritedAttributeResolver = null)
        => types
            .Select(context.GetContextType)
            .Where(type => type.Assembly != context.GetContextType<KubernetesEntityAttribute>().Assembly
                           && type.GetCustomAttributesData<KubernetesEntityAttribute>().Any()
                           && !type.GetCustomAttributesData<IgnoreAttribute>().Any())
            .Select(type => (Props: context.Transpile(type, inheritedAttributeResolver),
                IsStorage: type.GetCustomAttributesData<StorageVersionAttribute>().Any()))
            .GroupBy(grp => grp.Props.Metadata.Name)
            .Select(group =>
            {
                if (group.Count(def => def.IsStorage) > 1)
                {
                    throw new ArgumentException("There are multiple stored versions on an entity.");
                }

                var crd = group.First().Props;
                crd.Spec.Versions = group
                    .SelectMany(c => c.Props.Spec.Versions.Select(v =>
                    {
                        v.Served = true;
                        v.Storage = c.IsStorage;
                        return v;
                    }))
                    .OrderByDescending(v => v.Name, new KubernetesVersionComparer())
                    .ToList();

                // when only one version exists, or when no StorageVersion attributes are found,
                // the first version in the list is the stored one.
                if (crd.Spec.Versions.Count == 1 || !group.Any(def => def.IsStorage))
                {
                    crd.Spec.Versions[0].Storage = true;
                }

                return crd;
            });

    private static string GetPropertyName(this PropertyInfo prop, MetadataLoadContext context)
    {
        var name = prop.GetCustomAttributeData<JsonPropertyNameAttribute>() switch
        {
            null => prop.Name,
            { } attr => attr.GetCustomAttributeCtorArg<string>(context, 0) ?? prop.Name,
        };

        return JsonNamingPolicy.CamelCase.ConvertName(name);
    }

    private static IEnumerable<V1CustomResourceColumnDefinition> MapPrinterColumns(
        this MetadataLoadContext context,
        Type type,
        IInheritedAttributeResolver? inheritedAttributeResolver)
    {
        inheritedAttributeResolver ??= ReflectionInheritedAttributeResolver.Default;

        var props = type.GetProperties()
            .Select(p => (Prop: p, Path: string.Empty, Ancestors: (IReadOnlySet<Type>)new HashSet<Type> { type }))
            .ToList();
        while (props.Count > 0)
        {
            (PropertyInfo prop, string path, IReadOnlySet<Type> ancestors) = props[0];
            props.RemoveAt(0);

            // Path-scoped cycle guard: only skip a type already seen on the current path, so the
            // same non-circular type reused under different properties still contributes its columns.
            if (prop.PropertyType.IsClass && !ancestors.Contains(prop.PropertyType))
            {
                IReadOnlySet<Type> childAncestors = new HashSet<Type>(ancestors) { prop.PropertyType };
                props.AddRange(prop.PropertyType.GetProperties()
                    .Select(p => (Prop: p, Path: $"{path}.{prop.GetPropertyName(context)}", Ancestors: childAncestors)));
            }

            if (prop.GetCustomAttributeData<AdditionalPrinterColumnAttribute>() is not { } attr)
            {
                continue;
            }

            var mapped = context.Map(prop, EmptyAncestors);
            var column = new V1CustomResourceColumnDefinition
            {
                Name = attr.GetCustomAttributeCtorArg<string>(context, 1) ?? prop.GetPropertyName(context),
                JsonPath = $"{path}.{prop.GetPropertyName(context)}",
                Type = mapped.Type,
                Description = mapped.Description,
                Format = mapped.Format,
                Priority = attr.GetCustomAttributeCtorArg<PrinterColumnPriority>(context, 0) switch
                {
                    PrinterColumnPriority.StandardView => 0,
                    _ => 1,
                },
            };
            CrdSchemaAttributeValidator.ValidatePrinterColumn(
                prop,
                column,
                nameof(AdditionalPrinterColumnAttribute));
            yield return column;
        }

        foreach (var attr in type.GetInheritedCustomAttributesData<GenericAdditionalPrinterColumnAttribute>())
        {
            string? jsonPath, colName, colType;
            string? description, format;
            PrinterColumnPriority priority;

            if (attr.ConstructorArguments.Count >= 3)
            {
                jsonPath = attr.GetCustomAttributeCtorArg<string>(context, 0);
                colName = attr.GetCustomAttributeCtorArg<string>(context, 1);
                colType = attr.GetCustomAttributeCtorArg<string>(context, 2);
                description = attr.GetCustomAttributeNamedArg<string>(context, "Description");
                format = attr.GetCustomAttributeNamedArg<string>(context, "Format");
                priority = attr.GetCustomAttributeNamedArg<PrinterColumnPriority>(context, "Priority");
            }
            else if (inheritedAttributeResolver.TryResolve(attr.AttributeType, out var values))
            {
                jsonPath = values.GetValueOrDefault("JsonPath") as string;
                colName = values.GetValueOrDefault("Name") as string;
                colType = values.GetValueOrDefault("Type") as string;
                description = values.GetValueOrDefault("Description") as string;
                format = values.GetValueOrDefault("Format") as string;
                priority = values.GetValueOrDefault("Priority") as PrinterColumnPriority? ?? default;
                if (jsonPath is null || colName is null || colType is null)
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            var column = new V1CustomResourceColumnDefinition
            {
                Name = colName,
                JsonPath = jsonPath,
                Type = colType,
                Description = description,
                Format = format,
                Priority = priority switch
                {
                    PrinterColumnPriority.StandardView => 0,
                    _ => 1,
                },
            };
            CrdSchemaAttributeValidator.ValidatePrinterColumn(
                type,
                column,
                nameof(GenericAdditionalPrinterColumnAttribute));
            yield return column;
        }
    }

    private static V1JSONSchemaProps Map(
        this MetadataLoadContext context,
        PropertyInfo prop,
        IReadOnlySet<Type> ancestors)
    {
        var preservesUnknownFields = prop.GetCustomAttributeData<PreserveUnknownFieldsAttribute>() is not null;
        var isEmbeddedResource = prop.GetCustomAttributeData<EmbeddedResourceAttribute>() is not null;

        V1JSONSchemaProps props;
        if (isEmbeddedResource)
        {
            // Embedded resources are always opaque; the type graph is never traversed.
            props = new() { Type = Object, XKubernetesPreserveUnknownFields = true };
        }
        else if (preservesUnknownFields)
        {
            // Best-effort: keep the structural schema of known fields when the type is fully
            // transpilable; fall back to an opaque object when it contains a cycle or an otherwise
            // non-representable member (PreserveUnknownFields opts that subtree out of validation).
            try
            {
                props = context.Map(prop.PropertyType, ancestors);
            }
            catch (Exception ex) when (ex is CircularTypeReferenceException or InvalidTypeException)
            {
                props = new() { Type = Object };
            }
        }
        else
        {
            props = context.Map(prop.PropertyType, ancestors);
        }

        props.Description ??= prop.GetCustomAttributeData<DescriptionAttribute>()
            ?.GetCustomAttributeCtorArg<string>(context, 0);

        props.Title ??= prop.GetCustomAttributeData<TitleAttribute>()
            ?.GetCustomAttributeCtorArg<string>(context, 0);

        if (prop.GetCustomAttributeData<DefaultValueAttribute>() is { } defaultValue)
        {
            props.DefaultProperty = MapSchemaValue(prop, defaultValue, context);
        }

        if (prop.GetCustomAttributeData<ExampleAttribute>() is { } example)
        {
            props.Example = MapSchemaValue(prop, example, context);
        }

        if (prop.GetCustomAttributeData<FormatAttribute>() is { } format)
        {
            props.Format = format.GetCustomAttributeCtorArg<string>(context, 0);
        }

        if (prop.GetCustomAttributeData<EnumValuesAttribute>() is { } enumValues)
        {
            props.EnumProperty = enumValues.ConstructorArguments[0].Value is
                ReadOnlyCollection<CustomAttributeTypedArgument> values
                    ? values.Select(value => value.Value!).ToList()
                    : null;
        }

        if (prop.IsNullable())
        {
            // Default to Nullable to null to avoid generating `nullable:false`
            props.Nullable = true;
        }

        if (prop.GetCustomAttributeData<ExternalDocsAttribute>() is { } extDocs)
        {
            props.ExternalDocs = new()
            {
                Url = extDocs.GetCustomAttributeCtorArg<string>(context, 0),
                Description = extDocs.GetCustomAttributeCtorArg<string>(context, 1),
            };
        }

        if (prop.GetCustomAttributeData<ItemsAttribute>() is { } items)
        {
            var minItems = items.GetCustomAttributeCtorArg<long>(context, 0);
            props.MinItems = minItems == -1 ? null : minItems;

            var maxItems = items.GetCustomAttributeCtorArg<long>(context, 1);
            props.MaxItems = maxItems == -1 ? null : maxItems;
        }

        if (prop.GetCustomAttributeData<LengthAttribute>() is { } length)
        {
            var minLength = length.GetCustomAttributeCtorArg<long>(context, 0);
            props.MinLength = minLength == -1 ? null : minLength;

            var maxLength = length.GetCustomAttributeCtorArg<long>(context, 1);
            props.MaxLength = maxLength == -1 ? null : maxLength;
        }

        if (prop.GetCustomAttributeData<PropertyLimitsAttribute>() is { } properties)
        {
            var minProperties = properties.GetCustomAttributeCtorArg<long>(context, 0);
            props.MinProperties = minProperties == -1 ? null : minProperties;

            var maxProperties = properties.GetCustomAttributeCtorArg<long>(context, 1);
            props.MaxProperties = maxProperties == -1 ? null : maxProperties;
        }

        if (prop.GetCustomAttributeData<MultipleOfAttribute>() is { } multi)
        {
            props.MultipleOf = multi.GetCustomAttributeCtorArg<double>(context, 0);
        }

        if (prop.GetCustomAttributeData<PatternAttribute>() is { } pattern)
        {
            props.Pattern = pattern.GetCustomAttributeCtorArg<string>(context, 0);
        }

        if (prop.GetCustomAttributeData<RangeMaximumAttribute>() is { } rangeMax)
        {
            props.Maximum = rangeMax.GetCustomAttributeCtorArg<double>(context, 0);
            props.ExclusiveMaximum =
                rangeMax.GetCustomAttributeCtorArg<bool>(context, 1);
        }

        if (prop.GetCustomAttributeData<RangeMinimumAttribute>() is { } rangeMin)
        {
            props.Minimum = rangeMin.GetCustomAttributeCtorArg<double>(context, 0);
            props.ExclusiveMinimum =
                rangeMin.GetCustomAttributeCtorArg<bool>(context, 1);
        }

        if (prop.GetCustomAttributeData<UniqueItemsAttribute>() is not null)
        {
            props.UniqueItems = true;
        }

        if (prop.GetCustomAttributeData<XListTypeAttribute>() is { } listType)
        {
            props.XKubernetesListType = listType.GetCustomAttributeCtorArg<XListType>(context, 0)
                .ToString().ToLowerInvariant();
        }

        if (prop.GetCustomAttributeData<XMapTypeAttribute>() is { } mapType)
        {
            props.XKubernetesMapType = mapType.GetCustomAttributeCtorArg<XMapType>(context, 0)
                .ToString().ToLowerInvariant();
        }

        if (prop.GetCustomAttributeData<XListMapKeysAttribute>() is { } listMapKeysAttr)
        {
            props.XKubernetesListMapKeys = listMapKeysAttr.GetCustomAttributeCtorArrayArg<string>(0);
        }

        if (preservesUnknownFields)
        {
            props.XKubernetesPreserveUnknownFields = true;
        }

        if (isEmbeddedResource)
        {
            props.XKubernetesEmbeddedResource = true;
            props.XKubernetesPreserveUnknownFields = true;
            props.Type = Object;
            props.Properties = null;
        }

        var propValidations = context.MapValidationRules(prop.GetCustomAttributesData<ValidationRuleAttribute>());
        if (propValidations != null)
        {
            props.XKubernetesValidations = props.XKubernetesValidations is { } existing
                ? [.. existing, .. propValidations]
                : propValidations;
        }

        CrdSchemaAttributeValidator.Validate(prop, props, context);

        return props;
    }

    private static V1JSONSchemaProps Map(this MetadataLoadContext context, Type type, IReadOnlySet<Type> ancestors)
    {
        if (type.FullName == "System.String")
        {
            return new() { Type = String };
        }

        if (type.FullName == "System.Object")
        {
            return new() { Type = Object, XKubernetesPreserveUnknownFields = true };
        }

        if (type.Name == typeof(Nullable<>).Name && type.GenericTypeArguments.Length == 1)
        {
            var props = context.Map(type.GenericTypeArguments[0], ancestors);
            props.Nullable = true;
            return props;
        }

        var interfaces = (type.IsInterface
            ? type.GetInterfaces().Append(type)
            : type.GetInterfaces()).ToList();

        var interfaceNames = interfaces.Select(i =>
            i.IsGenericType
                ? i.GetGenericTypeDefinition().FullName
                : i.FullName).ToList();

        if (interfaceNames.Contains(typeof(IDictionary<,>).FullName))
        {
            var dictionaryImpl = interfaces
                .First(i => i.IsGenericType
                            && i.GetGenericTypeDefinition().FullName == typeof(IDictionary<,>).FullName);

            var additionalProperties = context.Map(dictionaryImpl.GenericTypeArguments[1], ancestors);
            return new() { Type = Object, AdditionalProperties = additionalProperties };
        }

        if (interfaceNames.Contains(typeof(IDictionary).FullName))
        {
            return new() { Type = Object, XKubernetesPreserveUnknownFields = true };
        }

        if (interfaceNames.Contains(typeof(IEnumerable<>).FullName))
        {
            return context.MapEnumerationType(type, interfaces, ancestors);
        }

        if (type.BaseType?.Name == nameof(CustomKubernetesEntity) ||
            type.BaseType?.Name == typeof(CustomKubernetesEntity<>).Name)
        {
            return context.MapObjectType(type, ancestors);
        }

        static Type GetRootBaseType(Type type)
        {
            var current = type;
            while (current.BaseType != null)
            {
                var baseName = current.BaseType.FullName;

                if (baseName == "System.Object" ||
                    baseName == "System.ValueType" ||
                    baseName == "System.Enum")
                {
                    return current.BaseType; // This is the root base we're after
                }

                current = current.BaseType;
            }

            return current; // In case it's already System.Object
        }

        var rootBase = GetRootBaseType(type);

        return rootBase.FullName switch
        {
            "System.Object" => context.MapObjectType(type, ancestors),
            "System.ValueType" => context.MapValueType(type),
            "System.Enum" => new() { Type = String, EnumProperty = context.GetEnumNames(type) },
            _ => throw InvalidType(type),
        };
    }

    private static object? MapSchemaValue(
        MemberInfo member,
        CustomAttributeData attribute,
        MetadataLoadContext context)
    {
        var value = attribute.ConstructorArguments[0].Value;
        if (!attribute.GetCustomAttributeNamedArg<bool>(context, "Json"))
        {
            return value;
        }

        if (value is not string json)
        {
            throw new InvalidTypeException(
                $"The attribute '{attribute.AttributeType.Name}' on " +
                $"'{GetSchemaMemberName(member)}' can only enable JSON parsing for string values.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return MapJsonElement(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidTypeException(
                $"The attribute '{attribute.AttributeType.Name}' on " +
                $"'{GetSchemaMemberName(member)}' contains invalid JSON.",
                exception);
        }
    }

    private static string GetSchemaMemberName(MemberInfo member) => member switch
    {
        Type type => type.FullName ?? type.Name,
        _ => $"{member.DeclaringType?.FullName}.{member.Name}",
    };

    private static object? MapJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(property => property.Name, property => MapJsonElement(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(MapJsonElement).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new JsonException($"Unsupported JSON value kind '{element.ValueKind}'."),
    };

    private static List<V1ValidationRule>? MapValidationRules(
        this MetadataLoadContext context,
        IEnumerable<CustomAttributeData> attributesData)
    {
        var rules = attributesData
            .Select(v => new V1ValidationRule
            {
                Rule = v.GetCustomAttributeCtorArg<string>(context, 0),
                FieldPath = v.GetCustomAttributeCtorArg<string?>(context, 1),
                Message = v.GetCustomAttributeCtorArg<string?>(context, 2),
                MessageExpression = v.GetCustomAttributeCtorArg<string?>(context, 3),
                Reason = v.GetCustomAttributeCtorArg<string?>(context, 4),
            })
            .ToList();

        return rules.Count > 0 ? rules : null;
    }

    private static IList<object> GetEnumNames(this MetadataLoadContext context, Type type)
    {
#if NET9_0_OR_GREATER
        var attributeNameByFieldName = new Dictionary<string, string>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetCustomAttributeData<JsonStringEnumMemberNameAttribute>() is { } jsonMemberNameAttribute &&
                jsonMemberNameAttribute.GetCustomAttributeCtorArg<string>(context, 0) is { } jsonMemberNameAtributeName)
            {
                attributeNameByFieldName.Add(field.Name, jsonMemberNameAtributeName);
            }
        }

        return Enum
            .GetNames(type)
            .Select(value => attributeNameByFieldName.GetValueOrDefault(value, value))
            .Cast<object>()
            .ToList();
#else
        return Enum.GetNames(type);
#endif
    }

    private static V1JSONSchemaProps MapObjectType(
        this MetadataLoadContext context,
        Type type,
        IReadOnlySet<Type> ancestors)
    {
        switch (type.FullName)
        {
            case "k8s.Models.ResourceQuantity":
                // Quantities are serialized as strings in CRDs (e.g., "500m", "2Gi")
                return new() { Type = String };
            case "k8s.Models.V1ObjectMeta":
                return new() { Type = Object };
            case "k8s.Models.IntOrString":
                return new() { XKubernetesIntOrString = true };
            default:
                if (context.GetContextType<IKubernetesObject>().IsAssignableFrom(type) &&
                    type is { IsAbstract: false, IsInterface: false } &&
                    type.Assembly == context.GetContextType<IKubernetesObject>().Assembly)
                {
                    return new()
                    {
                        Type = Object,
                        Properties = null,
                        XKubernetesPreserveUnknownFields = true,
                        XKubernetesEmbeddedResource = true,
                    };
                }

                if (ancestors.Contains(type))
                {
                    throw CircularTypeReference(type);
                }

                var nextAncestors = new HashSet<Type>(ancestors) { type };
                var preservesUnknownFields = type.GetCustomAttributeData<PreserveUnknownFieldsAttribute>() != null;
                var validationRules = type.GetInheritedCustomAttributesData<ValidationRuleAttribute>().ToList();
                CrdSchemaAttributeValidator.ValidateValidationRuleAttributes(type, validationRules, context);

                V1JSONSchemaProps props;
                try
                {
                    props = new()
                    {
                        Type = Object,
                        Description =
                            type.GetCustomAttributeData<DescriptionAttribute>()
                                ?.GetCustomAttributeCtorArg<string>(context, 0),
                        Title =
                            type.GetCustomAttributeData<TitleAttribute>()
                                ?.GetCustomAttributeCtorArg<string>(context, 0),
                        Properties = type
                            .GetProperties()
                            .Where(p => p.GetCustomAttributeData<IgnoreAttribute>() == null)
                            .Select(p => (Name: p.GetPropertyName(context), Schema: context.Map(p, nextAncestors)))
                            .OrderBy(t => t.Name, StringComparer.Ordinal)
                            .ToDictionary(t => t.Name, t => t.Schema),
                        Required = type.GetProperties()
                                .Where(p => p.GetCustomAttributeData<RequiredAttribute>() != null
                                            && p.GetCustomAttributeData<IgnoreAttribute>() == null)
                                .Select(p => p.GetPropertyName(context))
                                .OrderBy(name => name, StringComparer.Ordinal)
                                .ToList() switch
                        {
                            { Count: > 0 } p => p,
                            _ => null,
                        },
                        XKubernetesPreserveUnknownFields = preservesUnknownFields ? true : null,
                        XKubernetesValidations = context.MapValidationRules(validationRules),
                    };
                }
                catch (Exception ex) when (preservesUnknownFields
                                           && ex is CircularTypeReferenceException or InvalidTypeException)
                {
                    // Class-level [PreserveUnknownFields] opts the whole type out: if a member cannot
                    // be represented (cycle or non-transpilable type), degrade to an opaque object.
                    props = new() { Type = Object, XKubernetesPreserveUnknownFields = true };
                }

                if (type.GetCustomAttributeData<DefaultValueAttribute>() is { } defaultValue)
                {
                    props.DefaultProperty = MapSchemaValue(type, defaultValue, context);
                }

                return props;
        }
    }

    private static V1JSONSchemaProps MapEnumerationType(
        this MetadataLoadContext context,
        Type type,
        IEnumerable<Type> interfaces,
        IReadOnlySet<Type> ancestors)
    {
        Type? enumerableType = interfaces
            .FirstOrDefault(i => i.IsGenericType
                                 && i.GetGenericTypeDefinition().FullName == typeof(IEnumerable<>).FullName
                                 && i.GenericTypeArguments.Length == 1);

        if (enumerableType == null)
        {
            throw InvalidType(type);
        }

        Type listType = enumerableType.GenericTypeArguments[0];
        if (listType.IsGenericType && listType.GetGenericTypeDefinition().FullName == typeof(KeyValuePair<,>).FullName)
        {
            var additionalProperties = context.Map(listType.GenericTypeArguments[1], ancestors);
            return new() { Type = Object, AdditionalProperties = additionalProperties };
        }

        var items = context.Map(listType, ancestors);
        return new() { Type = Array, Items = items };
    }

    private static V1JSONSchemaProps MapValueType(this MetadataLoadContext _, Type type) =>
        type.FullName switch
        {
            "System.Int32" => new() { Type = Integer, Format = Int32 },
            "System.Int64" => new() { Type = Integer, Format = Int64 },
            "System.Single" => new() { Type = Number, Format = Float },
            "System.Double" => new() { Type = Number, Format = Double },
            "System.Decimal" => new() { Type = Number, Format = Decimal },
            "System.Boolean" => new() { Type = Boolean },
            "System.DateTime" or "System.DateTimeOffset" => new() { Type = String, Format = DateTime },
            "System.Guid" => new() { Type = String, Format = Uuid },
            _ => throw InvalidType(type),
        };

    private static bool IsRequiredSpecProperty(PropertyInfo prop) =>
        prop.Name.Equals("spec", StringComparison.OrdinalIgnoreCase)
        && (prop.PropertyType.GetCustomAttributeData<RequiredAttribute>() != null
            || prop.PropertyType.GetProperties()
                .Any(sp => sp.GetCustomAttributeData<RequiredAttribute>() != null
                           && sp.GetCustomAttributeData<IgnoreAttribute>() == null));

    private static InvalidTypeException InvalidType(Type type) =>
        new($"The given type '{type.FullName ?? type.Name}' is not a valid Kubernetes entity.");

    private static CircularTypeReferenceException CircularTypeReference(Type type) =>
        new($"A circular type reference was detected while transpiling the CRD schema for '{type.FullName ?? type.Name}'. " +
            "Break the cycle, or annotate the property with [PreserveUnknownFields] or [Ignore].");
}
