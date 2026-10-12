using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Linq;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>
/// SSUI005: mutable static state in a UI assembly. The UI reloads in its own
/// AssemblyLoadContext (plan_client_ui U5); a static that holds state either
/// survives nothing or keeps the old context alive. State lives in instances
/// from DI. A const, or a static readonly of an immutable type (a primitive,
/// an enum, a string, a struct, a delegate, a Type, a read-only or immutable
/// collection interface) is a constant and is allowed; so are static methods.
/// StaticStateCensusTests checks the same over the built assemblies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticStateAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        "SSUI005", "Mutable static state in a UI assembly",
        "{0} is mutable static state; the UI reloads in its own context, so keep state in an instance from DI (or make it a const or a static readonly of an immutable type)",
        UiSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Field, SymbolKind.Field);
        context.RegisterSymbolAction(Property, SymbolKind.Property);
    }

    private static void Field(SymbolAnalysisContext ctx)
    {
        var f = (IFieldSymbol)ctx.Symbol;
        if (!f.IsStatic || f.IsConst || f.IsImplicitlyDeclared)
            return;
        if (f.IsReadOnly && IsImmutable(f.Type))
            return;
        ctx.ReportDiagnostic(Diagnostic.Create(Rule, f.Locations[0], $"{f.ContainingType.Name}.{f.Name}"));
    }

    private static void Property(SymbolAnalysisContext ctx)
    {
        var p = (IPropertySymbol)ctx.Symbol;
        if (!p.IsStatic || p.IsAbstract)
            return;
        // An auto-property: settable, or get-only of a mutable type. A computed one holds nothing.
        bool auto = p.ContainingType.GetMembers().OfType<IFieldSymbol>()
                     .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, p));
        if (!auto)
            return;
        if (p.SetMethod is null && IsImmutable(p.Type))
            return;
        ctx.ReportDiagnostic(Diagnostic.Create(Rule, p.Locations[0], $"{p.ContainingType.Name}.{p.Name}"));
    }

    internal static bool IsImmutable(ITypeSymbol t)
    {
        if (t.TypeKind is TypeKind.Enum or TypeKind.Delegate or TypeKind.Pointer or TypeKind.FunctionPointer)
            return true;
        if (t.SpecialType == SpecialType.System_String)
            return true;
        if (t.IsValueType)
            return true;
        string name = UiSymbols.Name(t is INamedTypeSymbol n && n.IsGenericType ? n.ConstructedFrom : t);
        return name == "System.Type"
            || name.StartsWith("System.Collections.Generic.IReadOnly", System.StringComparison.Ordinal)
            || name.StartsWith("System.Collections.Immutable.", System.StringComparison.Ordinal)
            || name.StartsWith("System.Collections.Frozen.", System.StringComparison.Ordinal);
    }
}
