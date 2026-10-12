using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Analyzers;

/// <summary>
/// THE ONE SERIAL COLLECTION'S MEMBERSHIP IS A LIST (src/test/common/ProcessWide.cs).
/// A test class joins with <c>[Collection(ProcessWide.Name)]</c> AND a row in
/// its project's <c>ProcessWide.Rows()</c> naming what it shares:
/// <list type="bullet">
/// <item>SS0060: a class in the collection with no row;</item>
/// <item>SS0061: a row for a class not in the collection;</item>
/// <item>SS0062: a second <c>[CollectionDefinition]</c>: the serial collection is defined in one place.</item>
/// </list>
/// Checked when the whole project has been seen (a compilation-end rule), over
/// every test project (role "test").
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProcessWideAnalyzer : DiagnosticAnalyzer
{
    public const string UnlistedId = "SS0060";
    public const string StrayRowId = "SS0061";
    public const string SecondDefinitionId = "SS0062";

    public const string DefinitionTypeName = "ProcessWide";
    public const string RowsMethodName = "Rows";

    private const string CollectionAttribute = "Xunit.CollectionAttribute";
    private const string CollectionDefinitionAttribute = "Xunit.CollectionDefinitionAttribute";

    public static readonly DiagnosticDescriptor Unlisted = new(
        UnlistedId, "A class in the serial collection has no row",
        "'{0}' is in [Collection(ProcessWide.Name)] with no row in ProcessWide.Rows(); add one naming what it shares, or inject what it shares and leave the collection",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor StrayRow = new(
        StrayRowId, "A serial-collection row names a class not in the collection",
        "ProcessWide.Rows() lists '{0}', which is not in [Collection(ProcessWide.Name)]; delete the row",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true, customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor SecondDefinition = new(
        SecondDefinitionId, "A second collection definition",
        "'{0}' defines a collection; the serial collection is defined in one place (ProcessWide) and everything else runs in parallel",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Unlisted, StrayRow, SecondDefinition);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.Test))
                return;

            // The collection's name is ProcessWide.Name's value: the definition's own argument.
            var definitions = new ConcurrentBag<INamedTypeSymbol>();
            var members = new ConcurrentBag<(INamedTypeSymbol Type, string? Name, Location Where)>();
            var rows = new ConcurrentBag<(INamedTypeSymbol Type, Location Where)>();

            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;

                foreach (var attribute in type.GetAttributes())
                {
                    string name = Symbols.ClrFullName(attribute.AttributeClass);
                    string? argument = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
                    Location where = attribute.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation() ?? Symbols.LocationOf(type);

                    if (name == CollectionDefinitionAttribute)
                    {
                        if (type.Name == DefinitionTypeName)
                            definitions.Add(type);
                        else
                            ctx.ReportDiagnostic(Diagnostic.Create(SecondDefinition, where, Symbols.ClrFullName(type)));
                    }
                    else if (name == CollectionAttribute)
                    {
                        members.Add((type, argument, where));
                    }
                }
            }, SymbolKind.NamedType);

            start.RegisterOperationAction(ctx =>
            {
                if (ctx.ContainingSymbol is IMethodSymbol { Name: RowsMethodName } method
                    && method.ContainingType.Name == DefinitionTypeName
                    && ((ITypeOfOperation)ctx.Operation).TypeOperand is INamedTypeSymbol listed)
                    rows.Add((listed.OriginalDefinition, ctx.Operation.Syntax.GetLocation()));
            }, OperationKind.TypeOf);

            start.RegisterCompilationEndAction(end =>
            {
                var definition = definitions.FirstOrDefault();
                string? collection = definition?.GetAttributes()
                    .Where(a => Symbols.ClrFullName(a.AttributeClass) == CollectionDefinitionAttribute)
                    .Select(a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null)
                    .FirstOrDefault() ?? DefinitionTypeName;

                var inCollection = members.Where(m => m.Name == collection).ToList();
                var listed = rows.Select(r => r.Type).ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                var joined = inCollection.Select(m => m.Type).ToImmutableHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

                foreach (var (type, _, where) in inCollection.OrderBy(m => Symbols.ClrFullName(m.Type), System.StringComparer.Ordinal))
                {
                    if (!listed.Contains(type))
                        end.ReportDiagnostic(Diagnostic.Create(Unlisted, where, Symbols.ClrFullName(type)));
                }

                foreach (var (type, where) in rows.OrderBy(r => Symbols.ClrFullName(r.Type), System.StringComparer.Ordinal))
                {
                    if (!joined.Contains(type))
                        end.ReportDiagnostic(Diagnostic.Create(StrayRow, where, Symbols.ClrFullName(type)));
                }
            });
        });
    }
}
