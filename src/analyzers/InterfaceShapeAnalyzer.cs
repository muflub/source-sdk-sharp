using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceSharp.Analyzers;

/// <summary>
/// SS0030: AN INTERFACE ASSEMBLY HOLDS ONLY INTERFACES, ENUMS AND PURE DATA
/// (the user's rule, 2026-10-04). <c>SourceSharp.Server.Interface</c>,
/// <c>SourceSharp.Client.Interface</c> and <c>Debug.Api.Shared</c> are the
/// never-reloading contract between the core, the mods, the dev API and the
/// UI; behaviour there could not be reloaded and would leak types across load
/// contexts. So nothing in them has a body: no method, constructor, operator,
/// accessor or default interface member with code in it. Fields, auto-properties,
/// constants, enum members, primary constructors and what the compiler writes
/// for a record are data. Behaviour goes in its own assembly. Applied to the
/// projects under the role "interface".
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterfaceShapeAnalyzer : DiagnosticAnalyzer
{
    public const string BodyId = "SS0030";

    public static readonly DiagnosticDescriptor Body = new(
        BodyId, "An interface assembly holds behaviour",
        "'{0}' has a body: an interface assembly holds only interfaces, enums and pure data; move the behaviour to its own assembly",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Body);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.Interface))
                return;

            start.RegisterSyntaxNodeAction(ctx =>
            {
                SyntaxNode? body = ctx.Node switch
                {
                    BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                    AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
                    PropertyDeclarationSyntax p => p.ExpressionBody,
                    IndexerDeclarationSyntax i => i.ExpressionBody,
                    LocalFunctionStatementSyntax => null, // inside a body already reported
                    _ => null,
                };

                if (body is null)
                    return;

                ISymbol? symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ctx.CancellationToken);
                string name = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat) ?? ctx.Node.Kind().ToString();
                ctx.ReportDiagnostic(Diagnostic.Create(Body, NameLocation(ctx.Node), name));
            },
            SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration, SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.GetAccessorDeclaration, SyntaxKind.SetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration,
            SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration);
        });
    }

    private static Location NameLocation(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Identifier.GetLocation(),
        ConstructorDeclarationSyntax c => c.Identifier.GetLocation(),
        PropertyDeclarationSyntax p => p.Identifier.GetLocation(),
        AccessorDeclarationSyntax a => a.Keyword.GetLocation(),
        _ => node.GetLocation(),
    };
}
