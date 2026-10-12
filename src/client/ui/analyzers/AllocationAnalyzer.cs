using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>
/// SSUI001: no allocation in a describe method (plan_client_ui §5.2: describe,
/// layout and emit allocate nothing once warm). Flags capturing lambdas, LINQ,
/// boxing, <c>new</c> of a class or an array, delegate creation, <c>string +</c>,
/// a string-typed interpolation and <c>ToString()</c>. An interpolated string
/// handed to the toolkit's <c>UiTextHandler</c> (<c>ui.Text($"{n}")</c>)
/// formats into the frame and is not flagged; nor is code under
/// <c>if (ui.Naming)</c> or in a <c>throw</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AllocationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        "SSUI001", "Allocation in a describe method",
        "{0} allocates in a describe method, every frame; {1}",
        UiSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Describe, layout and emit allocate nothing per frame. Keep buffers in fields, format numbers with ui.Number or ui.Text($\"...\"), and build costly names only under if (ui.Naming).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockStartAction(start =>
        {
            if (!UiSymbols.IsDescribe(start.OwningSymbol))
                return;
            start.RegisterOperationAction(Check,
                OperationKind.AnonymousFunction, OperationKind.Invocation, OperationKind.Conversion,
                OperationKind.ObjectCreation, OperationKind.ArrayCreation, OperationKind.AnonymousObjectCreation,
                OperationKind.DelegateCreation, OperationKind.Binary, OperationKind.InterpolatedString);
        });
    }

    private static void Check(OperationAnalysisContext ctx)
    {
        var op = ctx.Operation;
        if (UiSymbols.IsExempt(op))
            return;

        (string What, string Fix)? hit = op switch
        {
            IAnonymousFunctionOperation f when Captures(f, ctx) =>
                ("A capturing lambda", "make it static, or move the state into a field"),
            IDelegateCreationOperation d when d.Target is IMethodReferenceOperation =>
                ("A delegate from a method group", "call the method directly"),
            IInvocationOperation i when UiSymbols.Name(i.TargetMethod.ContainingType) == "System.Linq.Enumerable" =>
                ($"LINQ ({i.TargetMethod.Name})", "write the loop"),
            IInvocationOperation i when i.TargetMethod.Name == "ToString" && i.TargetMethod.Parameters.Length == 0 =>
                ("ToString()", "use ui.Number(n) or ui.Text($\"{n}\")"),
            IConversionOperation c when c.Operand.Type is { IsValueType: true, IsRefLikeType: false } && c.Type is { IsReferenceType: true } && !InHandler(c) =>
                ($"Boxing {c.Operand.Type?.Name}", "pass the value as its own type"),
            IObjectCreationOperation o when o.Type is { IsReferenceType: true } =>
                ($"new {o.Type.Name}", "allocate it once, in the constructor or a field"),
            IArrayCreationOperation =>
                ("An array", "keep the array in a field, or use a span literal"),
            IAnonymousObjectCreationOperation =>
                ("An anonymous object", "use a struct"),
            IBinaryOperation b when b.OperatorKind == BinaryOperatorKind.Add && b.Type?.SpecialType == SpecialType.System_String =>
                ("String concatenation", "use ui.Text($\"...\"), which formats into the frame"),
            IInterpolatedStringOperation s when s.Type?.SpecialType == SpecialType.System_String && s.Parent is not IInterpolatedStringHandlerCreationOperation =>
                ("A string interpolation", "pass it straight to ui.Text($\"...\") (the frame's handler), not through a string"),
            _ => ((string, string)?)null,
        };

        if (hit is { } h)
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, op.Syntax.GetLocation(), h.What, h.Fix));
    }

    private static bool InHandler(IOperation op)
    {
        for (var p = op.Parent; p is not null; p = p.Parent)
            if (p is IInterpolatedStringHandlerCreationOperation)
                return true;
        return false;
    }

    private static bool Captures(IAnonymousFunctionOperation f, OperationAnalysisContext ctx)
    {
        if (f.Symbol.IsStatic || f.SemanticModel is null)
            return false;
        // The lambda as the region: what it captures is CapturedInside; a read of
        // `this` captures the instance.
        var flow = f.SemanticModel.AnalyzeDataFlow(f.Syntax);
        if (flow is null || !flow.Succeeded)
            return true;
        if (!flow.CapturedInside.IsEmpty)
            return true;
        foreach (var s in flow.ReadInside)
            if (s is IParameterSymbol { IsThis: true })
                return true;
        return false;
    }
}
