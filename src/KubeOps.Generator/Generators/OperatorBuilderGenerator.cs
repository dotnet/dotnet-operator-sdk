// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;

using KubeOps.Generator.Discovery;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace KubeOps.Generator.Generators;

/// <summary>
/// Generates the <c>RegisterComponents</c> extension method that composes all component
/// registrations of the compilation and its referenced assemblies. If the compilation contains
/// controllers or finalizers itself, an assembly-level
/// <c>KubeOpsGeneratedRegistrationsAttribute</c> marker is emitted so referencing compilations
/// can compose the registrations of this assembly. Referenced registrations are always invoked
/// through their marker (which only ever describes the components of its own assembly), never by
/// chaining aggregate methods; every component is therefore registered exactly once.
/// The aggregate class itself is internal: it is only meaningful for the compilation it is
/// generated into, and keeping it invisible across assembly boundaries rules out ambiguous
/// extension method calls when multiple assemblies use the generator.
/// </summary>
[Generator]
internal sealed class OperatorBuilderGenerator : IIncrementalGenerator
{
    private const string BuilderIdentifier = "builder";
    private const string ControllerRegistrationsClassName = "ControllerRegistrations";
    private const string FinalizerRegistrationsClassName = "FinalizerRegistrations";

    private const string RegistrationsAttributeFullName =
        "KubeOps.Abstractions.Builder.KubeOpsGeneratedRegistrations";

    // Referenced registration classes must have unique fully qualified names to be invoked from the
    // generated RegisterComponents. A clash (typically two assemblies using the global namespace
    // default) would emit ambiguous code, so the conflicting registration is skipped and reported.
    private static readonly DiagnosticDescriptor ConflictingRegistrations = new(
        id: "KOG002",
        title: "Conflicting generated registration class names",
        messageFormat: "The generated registration class '{0}' of the referenced assembly '{1}' " +
            "conflicts with another registration class of the same fully qualified name; its " +
            "components are not registered by 'RegisterComponents'. Set the " +
            "'KubeOpsGeneratorNamespace' MSBuild property in the conflicting projects to generate " +
            "into distinct namespaces.",
        category: "KubeOps.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(
            EntityDiscovery.GetControllers(context)
                .Combine(EntityDiscovery.GetFinalizers(context))
                .Combine(EntityDiscovery.GetEntities(context))
                .Combine(RegistrationDiscovery.GetReferencedRegistrations(context))
                .Combine(GeneratorOptions.GetGeneratorNamespace(context)),
            (spc, source) => Execute(
                spc,
                source.Left.Left.Left.Left,
                source.Left.Left.Left.Right,
                source.Left.Left.Right,
                source.Left.Right,
                source.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        EquatableArray<ControllerRegistration> controllers,
        EquatableArray<FinalizerRegistration> finalizers,
        EquatableArray<AttributedEntity> entities,
        EquatableArray<ReferencedRegistrations> referencedRegistrations,
        string? generatorNamespace)
    {
        var ownControllers = controllers.Where(c => entities.Any(e =>
            e.ClassDeclaration.FullyQualifiedName == c.FullyQualifiedEntityName)).ToList();
        var ownFinalizers = finalizers.Where(f => entities.Any(e =>
            e.ClassDeclaration.FullyQualifiedName == f.FullyQualifiedEntityName)).ToList();
        var hasOwnControllers = ownControllers.Count > 0;
        var hasOwnFinalizers = ownFinalizers.Count > 0;

        // Class names that are already taken in this compilation, each with the source location a
        // conflict is anchored to (the declaration of the first own component; referenced
        // registrations carry no source location). Referenced registration classes with an already
        // taken fully qualified name cannot be invoked unambiguously and would lead to double
        // registrations - they are skipped and reported via KOG002 instead.
        var takenClassNames = new Dictionary<string, LocationInfo?>(StringComparer.Ordinal);
        if (hasOwnControllers)
        {
            takenClassNames.Add(
                ControllerRegistrationsClassName.QualifyWith(generatorNamespace),
                ownControllers[0].Location);
        }

        if (hasOwnFinalizers)
        {
            takenClassNames[FinalizerRegistrationsClassName.QualifyWith(generatorNamespace)] =
                ownFinalizers[0].Location;
        }

        var statements = new List<StatementSyntax>();

        // Register the components of the own compilation via the sibling generated classes.
        if (hasOwnControllers)
        {
            statements.Add(ParseStatement($"{BuilderIdentifier}.RegisterControllers();"));
        }

        if (hasOwnFinalizers)
        {
            statements.Add(ParseStatement($"{BuilderIdentifier}.RegisterFinalizers();"));
        }

        // Register the components of every marked referenced assembly exactly once. The markers
        // always point to registration classes that only cover their own assembly, so this flat
        // list can never register a component twice - regardless of how the assemblies reference
        // each other.
        foreach (var referenced in referencedRegistrations)
        {
            if (referenced.ControllerRegistrations is not null)
            {
                if (takenClassNames.TryGetValue(referenced.ControllerRegistrations, out var conflictAnchor))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ConflictingRegistrations,
                        conflictAnchor?.ToLocation() ?? Location.None,
                        referenced.ControllerRegistrations,
                        referenced.AssemblyName));
                }
                else
                {
                    takenClassNames.Add(referenced.ControllerRegistrations, null);
                    statements.Add(ParseStatement(
                        $"global::{referenced.ControllerRegistrations}.RegisterControllers({BuilderIdentifier});"));
                }
            }

            if (referenced.FinalizerRegistrations is not null)
            {
                if (takenClassNames.TryGetValue(referenced.FinalizerRegistrations, out var conflictAnchor))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ConflictingRegistrations,
                        conflictAnchor?.ToLocation() ?? Location.None,
                        referenced.FinalizerRegistrations,
                        referenced.AssemblyName));
                }
                else
                {
                    takenClassNames.Add(referenced.FinalizerRegistrations, null);
                    statements.Add(ParseStatement(
                        $"global::{referenced.FinalizerRegistrations}.RegisterFinalizers({BuilderIdentifier});"));
                }
            }
        }

        statements.Add(ReturnStatement(IdentifierName(BuilderIdentifier)));

        var declaration = CompilationUnit()
            .WithUsings(
                List(
                    new List<UsingDirectiveSyntax> { UsingDirective(IdentifierName("KubeOps.Abstractions.Builder")), }))
            .WithLeadingTrivia(AutoGeneratedSyntaxTrivia.Instance);

        if (hasOwnControllers || hasOwnFinalizers)
        {
            declaration = declaration.AddAttributeLists(CreateRegistrationsMarker(
                hasOwnControllers ? ControllerRegistrationsClassName.QualifyWith(generatorNamespace) : null,
                hasOwnFinalizers ? FinalizerRegistrationsClassName.QualifyWith(generatorNamespace) : null));
        }

        declaration = declaration
            .AddNamespaceScope(generatorNamespace)
            .AddMembers(ClassDeclaration("OperatorBuilderExtensions")
                .WithModifiers(TokenList(
                    Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.StaticKeyword)))
                .AddMembers(MethodDeclaration(IdentifierName("IOperatorBuilder"), "RegisterComponents")
                    .WithModifiers(
                        TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                    .WithParameterList(ParameterList(
                        SingletonSeparatedList(
                            Parameter(
                                    Identifier(BuilderIdentifier))
                                .WithModifiers(
                                    TokenList(
                                        Token(SyntaxKind.ThisKeyword)))
                                .WithType(
                                    IdentifierName("IOperatorBuilder")))))
                    .WithBody(Block(statements))))
            .NormalizeWhitespace();

        context.AddSource(
            "OperatorBuilder.g.cs",
            SourceText.From(declaration.ToFullString(), Encoding.UTF8, SourceHashAlgorithm.Sha256));
    }

    private static AttributeListSyntax CreateRegistrationsMarker(
        string? controllerRegistrations,
        string? finalizerRegistrations)
        => AttributeList(
            AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)),
            SingletonSeparatedList(
                Attribute(ParseName($"global::{RegistrationsAttributeFullName}"))
                    .WithArgumentList(AttributeArgumentList(SeparatedList(new[]
                    {
                        AttributeArgument(ToLiteral(controllerRegistrations)),
                        AttributeArgument(ToLiteral(finalizerRegistrations)),
                    })))));

    private static ExpressionSyntax ToLiteral(string? value)
        => value is null
            ? LiteralExpression(SyntaxKind.NullLiteralExpression)
            : LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
}
