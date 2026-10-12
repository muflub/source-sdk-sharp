using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceSharp.Analyzers;

/// <summary>
/// A TEST THAT IS WRITTEN IS A TEST THAT RUNS. Two shapes compile and are then
/// never run by xUnit:
/// <list type="bullet">
/// <item>SS0050: the old hand-rolled runner's <c>Run(Action&lt;string, bool&gt;)</c>
/// shape, a method whose one parameter is a check callback;</item>
/// <item>SS0051: a public method on a test class (one that declares a
/// [Fact] or [Theory], or an attribute derived from them) that is neither: it
/// reads as a test and is not one. A helper is private or internal. Exempt:
/// overrides, interface implementations (Dispose, IAsyncLifetime) and the
/// members a [MemberData] names.</item>
/// </list>
/// Applied to the test projects (role "test").
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestShapeAnalyzer : DiagnosticAnalyzer
{
    public const string RunnerShapeId = "SS0050";
    public const string UnmarkedPublicId = "SS0051";

    private const string Fact = "Xunit.FactAttribute";
    private const string MemberData = "Xunit.MemberDataAttribute";

    public static readonly DiagnosticDescriptor RunnerShape = new(
        RunnerShapeId, "A Run(Action<string, bool>)-shaped test",
        "'{0}' takes a check callback, the old runner's shape: xUnit never runs it; write each check as a [Fact]",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmarkedPublic = new(
        UnmarkedPublicId, "A public method on a test class is not a test",
        "'{0}' is public on a test class with no [Fact] or [Theory]: xUnit never runs it; mark it, or make the helper private",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(RunnerShape, UnmarkedPublic);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.Test))
                return;

            start.RegisterSymbolAction(ctx =>
            {
                var method = (IMethodSymbol)ctx.Symbol;

                if (method.MethodKind == MethodKind.Ordinary && method.Parameters.Length == 1 && IsCheckCallback(method.Parameters[0].Type))
                    ctx.ReportDiagnostic(Diagnostic.Create(RunnerShape, Symbols.LocationOf(method), Name(method)));
            }, SymbolKind.Method);

            start.RegisterSymbolAction(AnalyzeTestClass, SymbolKind.NamedType);
        });
    }

    private static string Name(IMethodSymbol method) =>
        method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    /// <summary><c>System.Action&lt;string, bool&gt;</c>.</summary>
    private static bool IsCheckCallback(ITypeSymbol type) =>
        type is INamedTypeSymbol { Arity: 2 } named
        && Symbols.ClrFullName(named.OriginalDefinition) == "System.Action`2"
        && named.TypeArguments[0].SpecialType == SpecialType.System_String
        && named.TypeArguments[1].SpecialType == SpecialType.System_Boolean;

    private static bool IsTest(IMethodSymbol method) =>
        method.GetAttributes().Any(a => DerivesFrom(a.AttributeClass, Fact));

    private static bool DerivesFrom(INamedTypeSymbol? type, string fullName)
    {
        for (INamedTypeSymbol? at = type; at is not null; at = at.BaseType)
        {
            if (Symbols.ClrFullName(at) == fullName)
                return true;
        }

        return false;
    }

    private static void AnalyzeTestClass(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        if (type.TypeKind != TypeKind.Class)
            return;

        var methods = type.GetMembers().OfType<IMethodSymbol>().ToList();

        if (!methods.Any(IsTest))
            return;

        // [MemberData(nameof(X))] on any of the class's facts names X: a data source, public by xUnit's rule.
        var dataSources = new HashSet<string>(methods
            .SelectMany(m => m.GetAttributes())
            .Where(a => DerivesFrom(a.AttributeClass, MemberData) && a.ConstructorArguments.Length > 0)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .Where(n => n is not null)!);

        foreach (var method in methods)
        {
            if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public
                || method.IsImplicitlyDeclared || method.IsOverride || IsTest(method) || dataSources.Contains(method.Name)
                || ImplementsAnInterface(method))
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(UnmarkedPublic, Symbols.LocationOf(method), Name(method)));
        }
    }

    private static bool ImplementsAnInterface(IMethodSymbol method)
    {
        if (method.ExplicitInterfaceImplementations.Length > 0)
            return true;

        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method))
                    return true;
            }
        }

        return false;
    }
}
