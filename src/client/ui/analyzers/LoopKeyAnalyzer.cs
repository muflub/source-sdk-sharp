using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Linq;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>
/// SSUI004: a widget key built inside a loop that does not use the loop
/// variable. Every row then gets the same id and shares hover, focus, scroll
/// and state (ui-lint's ids rule sees it only for the samples that happen to
/// have two rows). A key is an argument to a parameter named <c>key</c> (Key,
/// Box, Button, Sub, Overlay, Scroll, State, Animate...).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoopKeyAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        "SSUI004", "A widget key in a loop that does not use the loop variable",
        "The key {0} is the same on every pass of the loop over {1}; build it from {1}, as in .Key({1})",
        UiSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Check, OperationKind.Argument);
    }

    private static void Check(OperationAnalysisContext ctx)
    {
        var a = (IArgumentOperation)ctx.Operation;
        if (a.Parameter is not { Name: "key" } || a.ArgumentKind != ArgumentKind.Explicit)
            return;
        // A widget key: a toolkit method's (not a dictionary's).
        if (a.Parameter.ContainingSymbol?.ContainingNamespace?.ToDisplayString() != "SourceSharp.Client.Ui.Toolkit")
            return;

        // The innermost loop around the key, up to the method or lambda boundary.
        for (var p = a.Parent; p is not null && p is not IAnonymousFunctionOperation && p is not ILocalFunctionOperation; p = p.Parent)
        {
            if (p is not ILoopOperation loop)
                continue;

            var vars = LoopVariables(loop);
            if (vars.IsEmpty)
                return;   // a while loop with no variable of its own: nothing to check against

            // The loop variable, and every local in the loop whose value comes from it
            // (var choice = source.Choice(i); ... .Key(choice.Number)).
            var dependent = new HashSet<ILocalSymbol>(vars, SymbolEqualityComparer.Default);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var d in loop.Body.DescendantsAndSelf())
                {
                    if (d is IVariableDeclaratorOperation decl && !dependent.Contains(decl.Symbol)
                        && decl.Initializer is { } init && References(init, dependent))
                    {
                        dependent.Add(decl.Symbol);
                        grew = true;
                    }
                }
            }
            if (References(a.Value, dependent))
                return;

            ctx.ReportDiagnostic(Diagnostic.Create(Rule, a.Value.Syntax.GetLocation(), a.Value.Syntax.ToString(), vars[0].Name));
            return;
        }
    }

    private static bool References(IOperation op, HashSet<ILocalSymbol> locals)
    {
        foreach (var d in op.DescendantsAndSelf())
            if (d is ILocalReferenceOperation r && locals.Contains(r.Local))
                return true;
        return false;
    }

    private static ImmutableArray<ILocalSymbol> LoopVariables(ILoopOperation loop) => loop switch
    {
        IForEachLoopOperation f => f.Locals.Concat(f.LoopControlVariable is IVariableDeclaratorOperation v ? new[] { v.Symbol } : System.Array.Empty<ILocalSymbol>())
                                          .Distinct<ILocalSymbol>(SymbolEqualityComparer.Default).ToImmutableArray(),
        IForLoopOperation f => f.Locals,
        IForToLoopOperation f => f.Locals,
        _ => loop.Locals,
    };
}
