using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceSharp.Client.Ui.Analyzers;

/// <summary>SSUI002's fix: replace an off-grid du literal with the nearest Space token, <c>(float)Space.XL</c>.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NearestTokenFix)), Shared]
public sealed class NearestTokenFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(RawValueAnalyzer.ColourId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;
        foreach (var d in context.Diagnostics)
        {
            if (!d.Properties.TryGetValue("token", out var token) || token is null)
                continue;
            var node = root.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
            var replacement = SyntaxFactory.ParseExpression($"(float)Space.{token}").WithTriviaFrom(node);
            context.RegisterCodeFix(
                CodeAction.Create($"Use Space.{token}",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(node, replacement))),
                    equivalenceKey: "SSUI002." + token),
                d);
        }
    }
}
