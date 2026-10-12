using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>
/// SSUI003: user-facing text written as a literal instead of a #Token. Any
/// string literal passed to a parameter named <c>token</c>, <c>...Token</c> or
/// <c>tokens</c> (ui.Text, ui.Label, ui.Button, ui.Dialog, ui.EmptyState,
/// ui.Tabs...) must start with '#', so the language file can translate it.
/// Game data goes through ui.Data, which takes no token.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TokenAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        "SSUI003", "User-facing text that is not a #Token",
        "\"{0}\" is user-facing text written as a literal; add a #Token to src/client/ui/resource/sourcesharp_ui_english.txt (or a mod's resource/*_english.txt), or pass game data through ui.Data",
        UiSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Check, OperationKind.Argument);
    }

    private static bool IsTokenParameter(IParameterSymbol p)
        => p.Name == "token" || p.Name == "tokens" || p.Name.EndsWith("Token", System.StringComparison.Ordinal);

    private static void Check(OperationAnalysisContext ctx)
    {
        var a = (IArgumentOperation)ctx.Operation;
        if (a.Parameter is null || a.ArgumentKind != ArgumentKind.Explicit || !IsTokenParameter(a.Parameter))
            return;
        foreach (var d in a.Value.DescendantsAndSelf())
        {
            if (d is ILiteralOperation { ConstantValue: { HasValue: true, Value: string s } } && !s.StartsWith("#", System.StringComparison.Ordinal))
                ctx.ReportDiagnostic(Diagnostic.Create(Rule, d.Syntax.GetLocation(), s));
        }
    }
}
