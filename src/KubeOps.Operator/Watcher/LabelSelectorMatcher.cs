// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace KubeOps.Operator.Watcher;

/// <summary>
/// Parses and evaluates Kubernetes label selector strings client-side. Supports the full standard
/// syntax: equality (<c>key=value</c>, <c>key==value</c>, <c>key!=value</c>), set-based
/// (<c>key in (a, b)</c>, <c>key notin (a, b)</c>), existence (<c>key</c>, <c>!key</c>), and
/// comma-separated conjunction. Used by the shared watch mode to dispatch events to the controller
/// pipelines whose selectors match an entity.
/// </summary>
internal static class LabelSelectorMatcher
{
    /// <summary>
    /// The operator of a single label selector requirement. Equality-based expressions are normalized
    /// to their set-based equivalent (<c>key=value</c> → <see cref="In"/>, <c>key!=value</c> →
    /// <see cref="NotIn"/>), matching Kubernetes semantics.
    /// </summary>
    internal enum RequirementOperator
    {
        /// <summary>The label key must be present (bare <c>key</c> expression).</summary>
        Exists,

        /// <summary>The label key must be absent (<c>!key</c> expression).</summary>
        NotExists,

        /// <summary>The label value must be one of the requirement's values.</summary>
        In,

        /// <summary>The label key must be absent or its value must not be one of the requirement's values.</summary>
        NotIn,
    }

    /// <summary>
    /// Parses a label selector expression into its requirements.
    /// </summary>
    /// <param name="selector">The selector expression; <see langword="null"/> or empty matches everything.</param>
    /// <returns>The parsed requirements (empty when the selector matches everything).</returns>
    /// <exception cref="FormatException">The expression is not a valid label selector.</exception>
    public static IReadOnlyList<Requirement> Parse(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return [];
        }

        var requirements = new List<Requirement>();
        foreach (var part in SplitTopLevel(selector))
        {
            var requirement = ParseRequirement(part.Trim());
            requirements.Add(requirement);
        }

        return requirements;
    }

    /// <summary>
    /// Evaluates parsed requirements against an entity's labels.
    /// </summary>
    /// <param name="requirements">The parsed requirements (conjunction).</param>
    /// <param name="labels">The entity's labels; <see langword="null"/> is treated as no labels.</param>
    /// <returns><see langword="true"/> when every requirement matches.</returns>
    public static bool Matches(IReadOnlyList<Requirement> requirements, IDictionary<string, string>? labels)
    {
        foreach (var requirement in requirements)
        {
            string? labelValue = null;
            var hasValue = labels is not null && labels.TryGetValue(requirement.Key, out labelValue);

            var matches = requirement.Operator switch
            {
                RequirementOperator.Exists => hasValue,
                RequirementOperator.NotExists => !hasValue,
                RequirementOperator.In => hasValue && requirement.Values.Contains(labelValue!),

                // Kubernetes semantics: "!=" and "notin" also match objects that do not carry the key at all.
                RequirementOperator.NotIn => !hasValue || !requirement.Values.Contains(labelValue!),
                _ => throw new InvalidOperationException($"Unknown requirement operator {requirement.Operator}."),
            };

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    // Splits on commas that are not inside an "in (...)"/"notin (...)" value list.
    private static IEnumerable<string> SplitTopLevel(string selector)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < selector.Length; i++)
        {
            switch (selector[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        throw new FormatException($"Invalid label selector \"{selector}\": unbalanced parentheses.");
                    }

                    break;
                case ',' when depth == 0:
                    yield return selector[start..i];
                    start = i + 1;
                    break;
            }
        }

        if (depth != 0)
        {
            throw new FormatException($"Invalid label selector \"{selector}\": unbalanced parentheses.");
        }

        yield return selector[start..];
    }

    private static Requirement ParseRequirement(string expression)
    {
        if (expression.Length == 0)
        {
            throw new FormatException("Invalid label selector: empty requirement.");
        }

        if (expression[0] == '!')
        {
            return new Requirement(expression[1..].Trim(), RequirementOperator.NotExists, new HashSet<string>(StringComparer.Ordinal));
        }

        var notEqualsIndex = expression.IndexOf("!=", StringComparison.Ordinal);
        if (notEqualsIndex >= 0)
        {
            return new Requirement(
                expression[..notEqualsIndex].Trim(),
                RequirementOperator.NotIn,
                new HashSet<string>(StringComparer.Ordinal) { expression[(notEqualsIndex + 2)..].Trim() });
        }

        var equalsIndex = expression.IndexOf('=');
        if (equalsIndex >= 0)
        {
            var valueStart = equalsIndex + 1 < expression.Length && expression[equalsIndex + 1] == '='
                ? equalsIndex + 2
                : equalsIndex + 1;
            return new Requirement(
                expression[..equalsIndex].Trim(),
                RequirementOperator.In,
                new HashSet<string>(StringComparer.Ordinal) { expression[valueStart..].Trim() });
        }

        var (setOperator, operatorIndex, operatorLength) = FindSetOperator(expression);
        if (setOperator is not null)
        {
            var key = expression[..operatorIndex].Trim();
            var valueList = expression[(operatorIndex + operatorLength)..].Trim();
            if (valueList.Length < 2 || valueList[0] != '(' || valueList[^1] != ')')
            {
                throw new FormatException(
                    $"Invalid label selector requirement \"{expression}\": expected a parenthesized value list.");
            }

            var values = valueList[1..^1]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);

            if (key.Length == 0 || values.Count == 0)
            {
                throw new FormatException(
                    $"Invalid label selector requirement \"{expression}\": missing key or values.");
            }

            return new Requirement(key, setOperator.Value, values);
        }

        // Bare key: existence requirement.
        return new Requirement(expression.Trim(), RequirementOperator.Exists, new HashSet<string>(StringComparer.Ordinal));
    }

    // Finds a whitespace-delimited "in"/"notin" operator (e.g. "key in (a,b)", "key notin (a)").
    private static (RequirementOperator? Operator, int Index, int Length) FindSetOperator(string expression)
    {
        var tokens = expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return (null, 0, 0);
        }

        foreach (var (keyword, op) in new[] { ("notin", RequirementOperator.NotIn), ("in", RequirementOperator.In) })
        {
            var index = FindWholeWord(expression, keyword);
            if (index > 0)
            {
                return (op, index, keyword.Length);
            }
        }

        return (null, 0, 0);
    }

    private static int FindWholeWord(string expression, string keyword)
    {
        var index = expression.IndexOf($" {keyword} ", StringComparison.Ordinal);
        if (index >= 0)
        {
            return index + 1;
        }

        index = expression.IndexOf($" {keyword}(", StringComparison.Ordinal);
        return index >= 0 ? index + 1 : -1;
    }

    /// <summary>
    /// A single parsed label selector requirement.
    /// </summary>
    /// <param name="Key">The label key.</param>
    /// <param name="Operator">The requirement operator.</param>
    /// <param name="Values">The value set for <see cref="RequirementOperator.In"/>/<see cref="RequirementOperator.NotIn"/>.</param>
    internal sealed record Requirement(string Key, RequirementOperator Operator, IReadOnlySet<string> Values);
}
