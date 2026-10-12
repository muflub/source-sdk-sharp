using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Analyzers;

/// <summary>
/// EVERY ABI THUNK COUNTS ITS OWN CROSSING, FIRST. An <c>[UnmanagedCallersOnly]</c>
/// method in a side's own entry (<c>ServerSideEntry</c>, <c>ClientSideEntry</c>)
/// opens with <c>Crossings.Count(CrossingOp.&lt;its own name&gt;)</c>, or, as a
/// side entry has no counters before its state exists, returns
/// <c>SideLoad.X([side,] CrossingOp.&lt;its own name&gt;, ...)</c>, which counts
/// it on the side's state before anything else:
/// <list type="bullet">
/// <item>SS0070: a thunk that does not open that way;</item>
/// <item>SS0071: a thunk that names another thunk's op.</item>
/// </list>
/// <c>CrossingsReport</c> reads the counters and is not counted. Applied to the
/// runtime assemblies (role "static-state"), where the entries live.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossingsThunkAnalyzer : DiagnosticAnalyzer
{
    public const string NotCountedFirstId = "SS0070";
    public const string CountsAnotherId = "SS0071";

    private const string CrossingOpType = "SourceSharp.Abi.CrossingOp";
    private const string CrossingsType = "SourceSharp.Abi.Crossings";
    private const string SideLoadType = "SourceSharp.Shared.Interop.SideLoad";
    private const string SideType = "SourceSharp.Abi.Side";

    /// <summary>The thunk that reads the counters: counting it would measure the measurement.</summary>
    public const string Exempt = "CrossingsReport";

    private static readonly ImmutableHashSet<string> EntryTypes = ImmutableHashSet.Create("ServerSideEntry", "ClientSideEntry");

    public static readonly DiagnosticDescriptor NotCountedFirst = new(
        NotCountedFirstId, "An ABI thunk does not count itself first",
        "the thunk '{0}' must open with Crossings.Count(CrossingOp.{0}) or hand CrossingOp.{0} to SideLoad first",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CountsAnother = new(
        CountsAnotherId, "An ABI thunk counts another thunk's op",
        "the thunk '{0}' names CrossingOp.{1}: a thunk counts only its own op",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(NotCountedFirst, CountsAnother);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            if (!AnalyzerRoles.Has(start.Options, AnalyzerRoles.StaticState))
                return;

            start.RegisterOperationBlockAction(Analyze);
        });
    }

    private static bool IsThunk(ISymbol symbol) =>
        symbol is IMethodSymbol method
        && EntryTypes.Contains(method.ContainingType.Name)
        && Symbols.HasAttribute(method, "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute");

    private static void Analyze(OperationBlockAnalysisContext ctx)
    {
        if (!IsThunk(ctx.OwningSymbol) || ctx.OwningSymbol.Name == Exempt)
            return;

        string name = ctx.OwningSymbol.Name;
        var body = ctx.OperationBlocks.OfType<IMethodBodyOperation>().Select(b => (IOperation?)b.BlockBody ?? b.ExpressionBody).FirstOrDefault()
                   ?? ctx.OperationBlocks.FirstOrDefault();

        if (body is null)
            return;

        foreach (var op in body.Descendants().OfType<IFieldReferenceOperation>())
        {
            if (Symbols.ClrFullName(op.Field.ContainingType) == CrossingOpType && op.Field.Name != name)
                ctx.ReportDiagnostic(Diagnostic.Create(CountsAnother, op.Syntax.GetLocation(), name, op.Field.Name));
        }

        IOperation? first = body is IBlockOperation block ? block.Operations.FirstOrDefault() : body;

        if (!OpensWithItsCount(first, name))
            ctx.ReportDiagnostic(Diagnostic.Create(NotCountedFirst, first?.Syntax.GetLocation() ?? Symbols.LocationOf(ctx.OwningSymbol), name));
    }

    /// <summary><c>crossings.Count(CrossingOp.N);</c> or <c>return SideLoad.X([Side.S,] CrossingOp.N, ...);</c>.</summary>
    private static bool OpensWithItsCount(IOperation? first, string name)
    {
        // An expression body is a return of its value; a block's statement may be either.
        IOperation? value = first switch
        {
            IExpressionStatementOperation s => s.Operation,
            IReturnOperation r => r.ReturnedValue,
            _ => first,
        };

        if (value is not IInvocationOperation call)
            return false;

        string declaring = Symbols.ClrFullName(call.TargetMethod.ContainingType);
        var args = call.Arguments.OrderBy(a => a.Parameter?.Ordinal ?? 0).Select(a => a.Value).ToList();

        if (declaring == CrossingsType && call.TargetMethod.Name == "Count" && first is IExpressionStatementOperation)
            return args.Count == 1 && IsOp(args[0], name);

        if (declaring == SideLoadType && first is IReturnOperation)
        {
            int at = args.Count > 0 && Symbols.ClrFullName(args[0].Type as INamedTypeSymbol) == SideType ? 1 : 0;
            return args.Count > at && IsOp(args[at], name);
        }

        return false;
    }

    private static bool IsOp(IOperation arg, string name) =>
        arg is IFieldReferenceOperation f
        && Symbols.ClrFullName(f.Field.ContainingType) == CrossingOpType
        && f.Field.Name == name;
}
