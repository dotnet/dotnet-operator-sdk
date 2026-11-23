// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace KubeOps.Abstractions.Builder;

/// <summary>
/// The rules the Kubernetes API server applies to the <c>fieldManager</c> of a write request.
/// </summary>
public static class FieldManagerName
{
    /// <summary>
    /// The maximum length of a field manager, in bytes of its UTF-8 representation.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Throws if <paramref name="fieldManager"/> is not accepted by the Kubernetes API server:
    /// it must not be empty, must not exceed <see cref="MaxLength"/> bytes and
    /// may only contain printable characters.
    /// </summary>
    /// <param name="fieldManager">The field manager to validate.</param>
    /// <param name="paramName">The name of the validated parameter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fieldManager"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="fieldManager"/> is not a valid field manager.</exception>
    public static void ThrowIfInvalid(
        string fieldManager,
        [CallerArgumentExpression(nameof(fieldManager))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(fieldManager, paramName);

        if (GetError(fieldManager) is { } error)
        {
            throw new ArgumentException(error, paramName);
        }
    }

    /// <summary>
    /// Determines why the API server would reject <paramref name="fieldManager"/>.
    /// </summary>
    /// <param name="fieldManager">The field manager to validate.</param>
    /// <returns>The reason, or <c>null</c> if <paramref name="fieldManager"/> is valid.</returns>
    internal static string? GetError(string? fieldManager)
    {
        if (string.IsNullOrEmpty(fieldManager))
        {
            return "The field manager must not be empty.";
        }

        if (Encoding.UTF8.GetByteCount(fieldManager) > MaxLength)
        {
            return $"The field manager must not be longer than {MaxLength} bytes.";
        }

        return IsPrintable(fieldManager) ? null : "The field manager may only contain printable characters.";
    }

    // Mirrors Go's unicode.IsPrint, which the API server uses: letters, marks, numbers, punctuation,
    // symbols and the ASCII space. Invalid UTF-16 (a lone surrogate) is rejected as well.
    private static bool IsPrintable(ReadOnlySpan<char> value)
    {
        while (!value.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(value, out var rune, out var consumed) != OperationStatus.Done ||
                !IsPrintable(rune))
            {
                return false;
            }

            value = value[consumed..];
        }

        return true;
    }

    private static bool IsPrintable(Rune rune) =>
        rune.Value == ' ' ||
        Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator);
}
