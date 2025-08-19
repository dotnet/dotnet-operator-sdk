// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Reflection;

namespace KubeOps.Transpiler;

/// <summary>
/// Utilities for loading attributes and information.
/// </summary>
public static class Utilities
{
    /// <summary>
    /// Load a custom attribute from a read-only-reflected type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <typeparam name="TAttribute">The type of the attribute to load.</typeparam>
    /// <returns>The custom attribute data if an attribute is found.</returns>
    public static CustomAttributeData? GetCustomAttributeData<TAttribute>(this Type type)
        where TAttribute : Attribute
        => CustomAttributeData
            .GetCustomAttributes(type)
            .FirstOrDefault(a => a.AttributeType.Name == typeof(TAttribute).Name);

    /// <summary>
    /// Load a custom attribute from a read-only-reflected field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <typeparam name="TAttribute">The type of the attribute to load.</typeparam>
    /// <returns>The custom attribute data if an attribute is found.</returns>
    public static CustomAttributeData? GetCustomAttributeData<TAttribute>(this FieldInfo field)
        where TAttribute : Attribute
        => CustomAttributeData
            .GetCustomAttributes(field)
            .FirstOrDefault(a => a.AttributeType.Name == typeof(TAttribute).Name);

    /// <summary>
    /// Load a custom attribute from a read-only-reflected property.
    /// </summary>
    /// <param name="prop">The property.</param>
    /// <typeparam name="TAttribute">The type of the attribute to load.</typeparam>
    /// <returns>The custom attribute data if an attribute is found.</returns>
    public static CustomAttributeData? GetCustomAttributeData<TAttribute>(this PropertyInfo prop)
        where TAttribute : Attribute
        => CustomAttributeData
            .GetCustomAttributes(prop)
            .FirstOrDefault(a => a.AttributeType.Name == typeof(TAttribute).Name);

    /// <summary>
    /// Load an enumerable of custom attributes from a read-only-reflected type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <typeparam name="TAttribute">The type of the attribute to load.</typeparam>
    /// <returns>The custom attribute data list if any were found.</returns>
    public static IEnumerable<CustomAttributeData> GetCustomAttributesData<TAttribute>(this Type type)
        where TAttribute : Attribute
        => CustomAttributeData
            .GetCustomAttributes(type)
            .Where(a => a.AttributeType.Name == typeof(TAttribute).Name);

    /// <summary>
    /// Load an enumerable of custom attributes from a read-only-reflected property.
    /// </summary>
    /// <param name="prop">The property.</param>
    /// <typeparam name="TAttribute">The type of the attribute to load.</typeparam>
    /// <returns>The custom attribute data list if any were found.</returns>
    public static IEnumerable<CustomAttributeData> GetCustomAttributesData<TAttribute>(this PropertyInfo prop)
        where TAttribute : Attribute
        => CustomAttributeData
            .GetCustomAttributes(prop)
            .Where(a => a.AttributeType.Name == typeof(TAttribute).Name);

    /// <summary>
    /// Load a specific named argument from a custom attribute.
    /// Named arguments are in the property-notation:
    /// <c>[KubernetesEntity(Kind = "foobar")]</c>.
    /// </summary>
    /// <param name="attr">The attribute in question.</param>
    /// <param name="ctx">The metadata load context that loaded everything.</param>
    /// <param name="name">The name of the argument.</param>
    /// <typeparam name="T">What target type the argument has.</typeparam>
    /// <returns>The argument value if found.</returns>
    /// <exception cref="InvalidCastException">Thrown if the data did not match the target type.</exception>
    public static T?
        GetCustomAttributeNamedArg<T>(this CustomAttributeData attr, MetadataLoadContext ctx, string name) =>
        attr.NamedArguments.FirstOrDefault(a => a.MemberName == name).TypedValue.ArgumentType == ctx.GetContextType<T>()
            ? (T)attr.NamedArguments.FirstOrDefault(a => a.MemberName == name).TypedValue.Value!
            : default;

    /// <summary>
    /// Load a specific named argument from a custom attribute.
    /// Without the metadata load context, this method is only usable for types in the same loaded assembly.
    /// Named arguments are in the property-notation:
    /// <c>[KubernetesEntity(Kind = "foobar")]</c>.
    /// </summary>
    /// <param name="attr">The attribute in question.</param>
    /// <param name="name">The name of the argument.</param>
    /// <typeparam name="T">What target type the argument has.</typeparam>
    /// <returns>The argument value if found.</returns>
    /// <exception cref="InvalidCastException">Thrown if the data did not match the target type.</exception>
    public static T?
        GetCustomAttributeNamedArg<T>(this CustomAttributeData attr, string name) =>
        attr.NamedArguments.FirstOrDefault(a => a.MemberName == name).TypedValue.ArgumentType == typeof(T)
            ? (T)attr.NamedArguments.FirstOrDefault(a => a.MemberName == name).TypedValue.Value!
            : default;

    /// <summary>
    /// Load a specific named argument array from a custom attribute.
    /// Named arguments are in the property-notation:
    /// <c>[Test(Foo = new[]{"bar", "baz"})]</c>.
    /// </summary>
    /// <param name="attr">The attribute in question.</param>
    /// <param name="name">The name of the argument.</param>
    /// <typeparam name="T">What target type the arguments have.</typeparam>
    /// <returns>The list of arguments if found.</returns>
    /// <exception cref="InvalidCastException">Thrown if the data did not match the target type.</exception>
    public static IList<T> GetCustomAttributeNamedArrayArg<T>(this CustomAttributeData attr, string name) =>
        attr.NamedArguments.FirstOrDefault(a => a.MemberName == name).TypedValue.Value is
            ReadOnlyCollection<CustomAttributeTypedArgument> value
            ? value.Select(v => (T)v.Value!).ToList()
            : [];

    /// <summary>
    /// Load a specific constructor argument from a custom attribute.
    /// Constructor arguments are in the "new" format:
    /// <c>[KubernetesEntity("foobar")]</c>.
    /// </summary>
    /// <param name="attr">The attribute in question.</param>
    /// <param name="ctx">The metadata load context that loaded everything.</param>
    /// <param name="index">Index of the value in the constructor notation.</param>
    /// <typeparam name="T">What target type the argument has.</typeparam>
    /// <returns>The argument value if found.</returns>
    /// <exception cref="InvalidCastException">Thrown if the data did not match the target type.</exception>
    public static T? GetCustomAttributeCtorArg<T>(this CustomAttributeData attr, MetadataLoadContext ctx, int index) =>
        attr.ConstructorArguments.Count >= index + 1 &&
        attr.ConstructorArguments[index].ArgumentType == ctx.GetContextType<T>()
            ? (T)attr.ConstructorArguments[index].Value!
            : default;

    /// <summary>
    /// Load a specific constructor argument from a custom attribute.
    /// Without the metadata load context, this method is only usable for types in the same loaded assembly.
    /// Constructor arguments are in the "new" format:
    /// <c>[KubernetesEntity("foobar")]</c>.
    /// </summary>
    /// <param name="attr">The attribute in question.</param>
    /// <param name="index">Index of the value in the constructor notation.</param>
    /// <typeparam name="T">What target type the argument has.</typeparam>
    /// <returns>The argument value if found.</returns>
    /// <exception cref="InvalidCastException">Thrown if the data did not match the target type.</exception>
    public static T? GetCustomAttributeCtorArg<T>(this CustomAttributeData attr, int index) =>
        attr.ConstructorArguments.Count >= index + 1 &&
        attr.ConstructorArguments[index].ArgumentType == typeof(T)
            ? (T)attr.ConstructorArguments[index].Value!
            : default;

    /// <summary>
    /// Load a specific constructor argument array from a custom attribute.
    /// Constructor arguments are in the "new" format:
    /// <c>[KubernetesEntity(new[]{"foobar", "barbaz"})]</c>.
    /// </summary>
    /// <param name="attr">The attribute in question.</param>
    /// <param name="index">Index of the value in the constructor notation.</param>
    /// <typeparam name="T">What target type the arguments have.</typeparam>
    /// <returns>The list of arguments if found.</returns>
    /// <exception cref="InvalidCastException">Thrown if the data did not match the target type.</exception>
    public static IList<T> GetCustomAttributeCtorArrayArg<T>(
        this CustomAttributeData attr,
        int index) =>
        attr.ConstructorArguments.Count >= index + 1 &&
        attr.ConstructorArguments[index].Value is
            ReadOnlyCollection<CustomAttributeTypedArgument> value
            ? value.Select(v => (T)v.Value!).ToList()
            : [];

    /// <summary>
    /// Load a type from a metadata load context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The loaded reflected type.</returns>
    public static Type GetContextType<T>(this MetadataLoadContext context)
        => context.GetContextType(typeof(T));

    /// <summary>
    /// Load a type from a metadata load context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="type">The type.</param>
    /// <returns>The loaded reflected type.</returns>
    public static Type GetContextType(this MetadataLoadContext context, Type type)
    {
        foreach (var assembly in context.GetAssemblies())
        {
            if (assembly.GetType(type.FullName!) is { } t)
            {
                return t;
            }
        }

        var newAssembly = context.LoadFromAssemblyPath(type.Assembly.Location);
        return newAssembly.GetType(type.FullName!)!;
    }

    /// <summary>
    /// Check if a type is nullable.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>True if the type is nullable (i.e. contains "nullable" in its name).</returns>
    public static bool IsNullable(this Type type)
        => type.FullName?.Contains("Nullable") == true;

    /// <summary>
    /// Check if a property is nullable.
    /// </summary>
    /// <param name="prop">The property.</param>
    /// <returns>True if the type is nullable (i.e. contains "nullable" in its name).</returns>
    public static bool IsNullable(this PropertyInfo prop)
        => new NullabilityInfoContext().Create(prop).ReadState == NullabilityState.Nullable ||
           prop.PropertyType.FullName?.Contains("Nullable") == true;
}
