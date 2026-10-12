using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>
/// SSUI002: raw values in describe code instead of tokens (plan_client_ui
/// §5.8.1). A literal <c>new UiColor(...)</c> (use a Tone, or
/// <c>UiPaint.FromGame</c> for a colour the game supplies), and a numeric
/// literal into a design-unit parameter (one named <c>du</c> or <c>...Du</c>)
/// that is off the 4 du grid. Hairlines (2 du and under) and Raw.* (the
/// declared escape, listed by ui-lint) are not reported. The code fix puts the
/// nearest Space token in.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawValueAnalyzer : DiagnosticAnalyzer
{
    public const string ColourId = "SSUI002";

    public static readonly DiagnosticDescriptor Rule = new(
        ColourId, "A raw value where a token belongs",
        "{0}; {1}",
        UiSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Screens use tokens: Tone for colours, Space for spacing (4, 8, 12, 16, 24, 32, 48 du). A size off the 4 du grid is almost always a near miss.");

    /// <summary>The Space tokens, as (du, name).</summary>
    public static readonly ImmutableArray<(int Du, string Name)> Tokens =
        ImmutableArray.Create((4, "XS"), (8, "S"), (12, "M"), (16, "L"), (24, "XL"), (32, "XXL"), (48, "Huge"));

    /// <summary>The token nearest <paramref name="du"/> (a tie goes to the larger).</summary>
    public static (int Du, string Name) Nearest(double du)
    {
        var best = Tokens[0];
        foreach (var t in Tokens)
            if (System.Math.Abs(t.Du - du) <= System.Math.Abs(best.Du - du))
                best = t;
        return best;
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockStartAction(start =>
        {
            if (!UiSymbols.IsDescribe(start.OwningSymbol))
                return;
            start.RegisterOperationAction(Colour, OperationKind.ObjectCreation);
            start.RegisterOperationAction(Argument, OperationKind.Argument);
        });
    }

    private static void Colour(OperationAnalysisContext ctx)
    {
        var o = (IObjectCreationOperation)ctx.Operation;
        if (UiSymbols.Name(o.Type) == UiSymbols.Color && !UiSymbols.IsExempt(o))
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, o.Syntax.GetLocation(),
                "A literal UiColor", "use a Tone (or UiPaint.FromGame for a colour the game supplies)"));
    }

    private static void Argument(OperationAnalysisContext ctx)
    {
        var a = (IArgumentOperation)ctx.Operation;
        var p = a.Parameter;
        if (p is null || a.ArgumentKind != ArgumentKind.Explicit || !(p.Name == "du" || p.Name.EndsWith("Du", System.StringComparison.Ordinal)))
            return;
        if (p.ContainingType is { Name: "Raw" })
            return;
        var value = a.Value is IConversionOperation c ? c.Operand : a.Value;
        if (value is not ILiteralOperation { ConstantValue: { HasValue: true, Value: var v } } || v is null)
            return;
        double du = System.Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
        if (du <= 2 || du % 4 == 0)
            return;
        if (du > 56)
        {
            // A size, not a spacing: the grid, not a token.
            double down = System.Math.Floor(du / 4) * 4, up = down + 4;
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation(),
                $"{du} du is off the 4 du grid", $"use {down} or {up}"));
            return;
        }
        var t = Nearest(du);
        var props = ImmutableDictionary<string, string?>.Empty.Add("token", t.Name);
        ctx.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation(), props,
            $"{du} du is off the 4 du grid", $"use Space.{t.Name} ({t.Du} du)"));
    }
}
