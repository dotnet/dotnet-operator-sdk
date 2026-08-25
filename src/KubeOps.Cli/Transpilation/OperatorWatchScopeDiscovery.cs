// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace KubeOps.Cli.Transpilation;

internal static class OperatorWatchScopeDiscovery
{
    private const string AddOperatorMethodName = "AddKubernetesOperator";
    private const string OperatorExtensionsTypeName = "KubeOps.Operator.ServiceCollectionExtensions";
    private const string SettingsExtensionsTypeName =
        "KubeOps.Abstractions.Builder.OperatorSettingsBuilderExtensions";

    public static OperatorWatchScope Discover(IEnumerable<Compilation> compilations)
    {
        var registrations = compilations.SelectMany(DiscoverRegistrations).ToList();
        if (registrations.Count == 0)
        {
            return OperatorWatchScope.ClusterWide;
        }

        var diagnostics = registrations
            .Where(registration => registration.Kind == OperatorWatchScopeKind.Unknown)
            .SelectMany(registration => registration.Diagnostics ?? [])
            .ToList();
        if (diagnostics.Count > 0)
        {
            return OperatorWatchScope.Unknown(diagnostics.ToArray());
        }

        var distinctScopes = registrations
            .Select(registration => (registration.Kind, registration.Namespace))
            .Distinct()
            .ToList();
        if (distinctScopes.Count == 1)
        {
            var scope = distinctScopes[0];
            return scope.Kind == OperatorWatchScopeKind.Namespaced
                ? OperatorWatchScope.Namespaced(scope.Namespace!)
                : OperatorWatchScope.ClusterWide;
        }

        return OperatorWatchScope.Unknown(new OperatorWatchScopeDiagnostic(
            "Multiple AddKubernetesOperator registrations configure different watch namespaces."));
    }

    private static IEnumerable<OperatorWatchScope> DiscoverRegistrations(Compilation compilation)
    {
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            foreach (var invocationSyntax in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetOperation(invocationSyntax) is not IInvocationOperation invocation
                    || !IsAddOperatorInvocation(invocation.TargetMethod))
                {
                    continue;
                }

                yield return DiscoverRegistration(invocation, invocationSyntax);
            }
        }
    }

    private static OperatorWatchScope DiscoverRegistration(
        IInvocationOperation invocation,
        InvocationExpressionSyntax invocationSyntax)
    {
        var configureArgument = invocation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name == "configure");
        if (configureArgument is null || configureArgument.Value.ConstantValue is { HasValue: true, Value: null })
        {
            return OperatorWatchScope.ClusterWide;
        }

        var configureOperation = Unwrap(configureArgument.Value);
        if (configureOperation is not IAnonymousFunctionOperation configure
            || configure.Symbol.Parameters is not [{ } settingsParameter])
        {
            return Unknown(
                invocationSyntax,
                "The AddKubernetesOperator configuration delegate is not an inline lambda and cannot be evaluated statically.");
        }

        var walker = new NamespaceConfigurationWalker(settingsParameter);
        walker.Visit(configure.Body);
        if (walker.UnknownReason is not null)
        {
            return Unknown(walker.UnknownSyntax ?? invocationSyntax, walker.UnknownReason);
        }

        if (walker.Values.Count == 0)
        {
            return OperatorWatchScope.ClusterWide;
        }

        var distinctValues = walker.Values.Distinct(StringComparer.Ordinal).ToList();
        if (distinctValues.Count != 1)
        {
            return Unknown(
                invocationSyntax,
                "The AddKubernetesOperator configuration assigns different watch namespaces.");
        }

        return distinctValues[0] is { } @namespace
            ? OperatorWatchScope.Namespaced(@namespace)
            : OperatorWatchScope.ClusterWide;
    }

    private static bool IsAddOperatorInvocation(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.Name == AddOperatorMethodName
               && definition.ContainingType.ToDisplayString() == OperatorExtensionsTypeName;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation is IDelegateCreationOperation delegateCreation
            ? delegateCreation.Target
            : operation;
    }

    private static OperatorWatchScope Unknown(SyntaxNode syntax, string message)
    {
        var lineSpan = syntax.GetLocation().GetLineSpan();
        var position = lineSpan.StartLinePosition;
        return OperatorWatchScope.Unknown(new OperatorWatchScopeDiagnostic(
            message,
            lineSpan.Path,
            position.Line + 1,
            position.Character + 1));
    }

    private sealed class NamespaceConfigurationWalker(IParameterSymbol settingsParameter) : OperationWalker
    {
        private readonly List<string?> _values = [];

        public IReadOnlyList<string?> Values => _values;

        public string? UnknownReason { get; private set; }

        public SyntaxNode? UnknownSyntax { get; private set; }

        public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
        {
            if (operation.Target is IPropertyReferenceOperation property
                && property.Property.Name == "Namespace"
                && IsSettingsBuilder(property.Instance))
            {
                AddValue(operation.Value, operation.Syntax);
            }

            base.VisitSimpleAssignment(operation);
        }

        public override void VisitInvocation(IInvocationOperation operation)
        {
            if (IsSettingsExtension(operation.TargetMethod, "WithNamespace")
                && IsSettingsBuilder(operation.Instance ?? operation.Arguments.FirstOrDefault()?.Value))
            {
                var namespaceArgument = operation.Arguments.FirstOrDefault(argument =>
                    argument.Parameter?.Name == "ns");
                if (namespaceArgument is null)
                {
                    SetUnknown(operation.Syntax, "The WithNamespace argument could not be resolved.");
                }
                else
                {
                    AddValue(namespaceArgument.Value, operation.Syntax);
                }
            }
            else if (UsesSettingsBuilderDirectly(operation) && !IsKnownSettingsExtension(operation.TargetMethod))
            {
                SetUnknown(
                    operation.Syntax,
                    "The operator settings builder is passed to a method that cannot be evaluated statically.");
            }

            base.VisitInvocation(operation);
        }

        public override void VisitParameterReference(IParameterReferenceOperation operation)
        {
            if (SymbolEqualityComparer.Default.Equals(operation.Parameter, settingsParameter)
                && !IsSafeSettingsReference(operation))
            {
                SetUnknown(
                    operation.Syntax,
                    "The operator settings builder is used in a way that cannot be evaluated statically.");
            }

            base.VisitParameterReference(operation);
        }

        private static bool IsKnownSettingsExtension(IMethodSymbol method) =>
            (method.ReducedFrom ?? method).ContainingType.ToDisplayString() == SettingsExtensionsTypeName;

        private static bool IsSettingsExtension(IMethodSymbol method, string methodName) =>
            method.Name == methodName && IsKnownSettingsExtension(method);

        private static bool IsConditional(SyntaxNode syntax) => syntax.Ancestors().Any(ancestor => ancestor is
            IfStatementSyntax or ElseClauseSyntax or SwitchStatementSyntax or SwitchExpressionSyntax
            or ConditionalExpressionSyntax or ForStatementSyntax or ForEachStatementSyntax
            or WhileStatementSyntax or DoStatementSyntax or TryStatementSyntax);

        private static bool IsSafeSettingsReference(IParameterReferenceOperation operation)
        {
            IOperation current = operation;
            while (current.Parent is IConversionOperation conversion)
            {
                current = conversion;
            }

            if (current.Parent is IPropertyReferenceOperation property && property.Instance == current)
            {
                return true;
            }

            if (current.Parent is IArgumentOperation argument
                && argument.Parent is IInvocationOperation argumentInvocation
                && IsKnownSettingsExtension(argumentInvocation.TargetMethod))
            {
                return true;
            }

            return current.Parent is IInvocationOperation instanceInvocation
                   && instanceInvocation.Instance == current
                   && IsKnownSettingsExtension(instanceInvocation.TargetMethod);
        }

        private void AddValue(IOperation operation, SyntaxNode syntax)
        {
            if (IsConditional(syntax))
            {
                SetUnknown(syntax, "The watch namespace is assigned conditionally and cannot be evaluated statically.");
                return;
            }

            var value = Unwrap(operation).ConstantValue;
            if (!value.HasValue || value.Value is not null and not string)
            {
                SetUnknown(syntax, "The watch namespace is not a compile-time constant.");
                return;
            }

            if (value.Value is string { Length: 0 })
            {
                SetUnknown(syntax, "The watch namespace must not be empty.");
                return;
            }

            _values.Add((string?)value.Value);
        }

        private bool UsesSettingsBuilderDirectly(IInvocationOperation operation) =>
            IsSettingsBuilder(operation.Instance)
            || operation.Arguments.Any(argument => IsSettingsBuilder(argument.Value));

        private bool IsSettingsBuilder(IOperation? operation)
        {
            if (operation is null)
            {
                return false;
            }

            operation = Unwrap(operation);
            return operation switch
            {
                IParameterReferenceOperation parameterReference =>
                    SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, settingsParameter),
                IInvocationOperation invocation when IsKnownSettingsExtension(invocation.TargetMethod) =>
                    IsSettingsBuilder(invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value),
                _ => false,
            };
        }

        private void SetUnknown(SyntaxNode syntax, string reason)
        {
            UnknownReason ??= reason;
            UnknownSyntax ??= syntax;
        }
    }
}
