// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace KubeOps.Transpiler;

/// <summary>
/// Reads constructor-body argument values from attribute types that inherit from another attribute.
/// When <c>[ReadyPrinterColumn]</c> is applied (where <c>ReadyPrinterColumnAttribute</c> calls
/// <c>base(".status...", "Ready", "string")</c>), the values exist only in the constructor IL body —
/// not in the assembly metadata blob. This reader uses <see cref="System.Reflection.Metadata"/>
/// to parse those <c>ldstr</c> operands from the constructor method body.
/// </summary>
internal static class InheritedAttributeCtorReader
{
    /// <summary>
    /// Attempts to extract the three string arguments passed to the
    /// <c>GenericAdditionalPrinterColumnAttribute(string jsonPath, string name, string type)</c>
    /// base constructor from the IL body of <paramref name="attributeType"/>'s parameterless constructor.
    /// </summary>
    /// <param name="attributeType">The attribute type whose constructor IL to analyse.</param>
    /// <param name="jsonPath">The first base-ctor argument (json path).</param>
    /// <param name="name">The second base-ctor argument (column name).</param>
    /// <param name="type">The third base-ctor argument (column type string).</param>
    /// <returns>
    /// <see langword="true"/> when all three values were successfully extracted;
    /// <see langword="false"/> when the assembly is not accessible from disk, the constructor body
    /// does not match the expected <c>ldstr, ldstr, ldstr, call</c> pattern, or any other error occurs.
    /// </returns>
    internal static bool TryReadBaseCtorArgs(
        Type attributeType,
        out string? jsonPath,
        out string? name,
        out string? type)
    {
        jsonPath = null;
        name = null;
        type = null;

        var location = attributeType.Assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return false;

        try
        {
            using var peStream = File.OpenRead(location);
            using var peReader = new PEReader(peStream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var fullName = GetClrFullName(typeDefHandle, metadataReader);
                if (fullName != attributeType.FullName)
                    continue;

                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = metadataReader.GetMethodDefinition(methodHandle);
                    if (metadataReader.GetString(method.Name) != ".ctor")
                        continue;

                    var sigReader = metadataReader.GetBlobReader(method.Signature);
                    sigReader.ReadSignatureHeader();
                    var paramCount = sigReader.ReadCompressedInteger();
                    if (paramCount != 0)
                        continue;

                    if (method.RelativeVirtualAddress == 0)
                        return false;

                    var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                    var il = body.GetILContent().ToArray();
                    return TryExtractLdstrArgs(il, metadataReader, out jsonPath, out name, out type);
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Builds the CLR-style full type name (using <c>+</c> as the nesting separator) for a type
    /// definition, matching the format returned by <see cref="Type.FullName"/> in
    /// <see cref="System.Reflection.MetadataLoadContext"/>.
    /// </summary>
    private static string GetClrFullName(TypeDefinitionHandle handle, MetadataReader metadataReader)
    {
        var typeDef = metadataReader.GetTypeDefinition(handle);
        var name = metadataReader.GetString(typeDef.Name);

        var declaringHandle = typeDef.GetDeclaringType();
        if (!declaringHandle.IsNil)
            return $"{GetClrFullName(declaringHandle, metadataReader)}+{name}";

        var ns = metadataReader.GetString(typeDef.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    /// <summary>
    /// Scans raw IL for <c>ldstr</c> (0x72) opcodes and collects the resolved strings.
    /// The first three found before a <c>call</c> are returned as the base-constructor arguments.
    /// Handles common IL prefixes: <c>nop</c>, <c>ldarg.0</c>, <c>call</c>, <c>ret</c>.
    /// Returns false when fewer than three strings are found before a call or an unrecognised opcode
    /// is encountered.
    /// </summary>
    private static bool TryExtractLdstrArgs(
        byte[] il,
        MetadataReader metadataReader,
        out string? arg0,
        out string? arg1,
        out string? arg2)
    {
        arg0 = null;
        arg1 = null;
        arg2 = null;

        var strings = new List<string>(3);
        var i = 0;

        while (i < il.Length)
        {
            var opcode = il[i++];

            switch (opcode)
            {
                case 0x00: // nop
                case 0x02: // ldarg.0
                case 0x2A: // ret
                    break;

                case 0x72: // ldstr <4-byte metadata token>
                    {
                        if (i + 4 > il.Length)
                            return false;

                        var rawToken = il[i] | (il[i + 1] << 8) | (il[i + 2] << 16) | (il[i + 3] << 24);
                        i += 4;

                        var handle = MetadataTokens.Handle(rawToken);
                        if (handle.Kind == HandleKind.UserString)
                            strings.Add(metadataReader.GetUserString((UserStringHandle)handle));

                        break;
                    }

                case 0x28: // call <4-byte method token>
                case 0x6F: // callvirt <4-byte method token>
                    i += 4;
                    if (strings.Count >= 3)
                    {
                        arg0 = strings[0];
                        arg1 = strings[1];
                        arg2 = strings[2];
                        return true;
                    }

                    break;

                default:
                    return false;
            }
        }

        return false;
    }
}
