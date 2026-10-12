using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

using SourceSharp.Client.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui.Analyzers;

/// <summary>SSUI002: raw values where tokens belong, and its nearest-token fix.</summary>
public class RawValueAnalyzerTests
{
    private const string Planted = """
        public sealed class Bad : UiPanel
        {
            public override void Describe(ref UiFrame ui)
            {
                ui.Rect(Tone.Accent, Size.Du(26), Size.Du(40)).Color(Raw.Color(1, 2, 3));
                var magenta = new UiColor(255, 0, 255, 255);
                ui.Shape(UiShape.Cross, Tone.Accent, 14);
                ui.Rect(Tone.Accent, Size.Du(90), Size.Du(40));
            }
        }
        """;

    [Fact]
    public void A_literal_colour_and_off_grid_du_literals_are_reported()
    {
        var found = AnalyzerHarness.Run(new RawValueAnalyzer(), Planted);
        Assert.All(found, d => Assert.Equal("SSUI002", d.Id));
        var messages = found.Select(d => d.GetMessage()).ToList();
        Assert.Contains("26 du is off the 4 du grid; use Space.XL (24 du)", messages);
        Assert.Contains("14 du is off the 4 du grid; use Space.L (16 du)", messages);
        Assert.Contains(messages, m => m.StartsWith("A literal UiColor", StringComparison.Ordinal));
        Assert.Contains("90 du is off the 4 du grid; use 88 or 92", messages);   // a size: the grid, not a spacing token
        Assert.Equal(4, found.Count);   // 40 is on the grid; Raw.Color is the declared escape
    }

    [Fact]
    public void Tokens_grid_sizes_hairlines_and_raw_escapes_stay_silent()
    {
        var found = AnalyzerHarness.Run(new RawValueAnalyzer(), """
            public sealed class Good : UiPanel
            {
                public override void Describe(ref UiFrame ui)
                {
                    ui.Rect(Tone.Accent, Size.Du(24), Size.Du(1)).Border(Tone.Line, 1);
                    ui.Shape(UiShape.Cross, Tone.Accent, 16);
                    using (ui.Column(Space.M).Pad(Raw.Du(13))) { }
                    ui.Data("x").Color(UiPaint.FromGame(default));
                }
            }
            """);
        Assert.Empty(found);
    }

    [Fact]
    public async Task The_fix_puts_the_nearest_token_in()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Planted", LanguageNames.CSharp)
            .WithMetadataReferences(AnalyzerHarness.Compile(Planted).References)
            .WithCompilationOptions(new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var document = project.AddDocument("Planted", SourceText.From(AnalyzerHarness.Usings + "\n" + Planted));

        var diagnostics = AnalyzerHarness.Run(new RawValueAnalyzer(), Planted)
            .Where(d => d.Properties.ContainsKey("token")).ToList();
        Assert.Equal(2, diagnostics.Count);   // the 90 du size has no token to offer

        var tree = (await document.GetSyntaxTreeAsync())!;
        var fix = new NearestTokenFix();
        var actions = new List<CodeAction>();
        var located = Diagnostic.Create(diagnostics[0].Descriptor, Location.Create(tree, diagnostics[0].Location.SourceSpan),
                                        diagnostics[0].Properties, diagnostics[0].GetMessage());
        await fix.RegisterCodeFixesAsync(new CodeFixContext(document, located, (a, _) => actions.Add(a), default));
        var action = Assert.Single(actions);
        Assert.Equal("Use Space.XL", action.Title);

        var op = (await action.GetOperationsAsync(default)).OfType<ApplyChangesOperation>().Single();
        string fixedText = (await op.ChangedSolution.GetDocument(document.Id)!.GetTextAsync()).ToString();
        Assert.Contains("Size.Du((float)Space.XL)", fixedText);
        Assert.DoesNotContain("Size.Du(26)", fixedText);
    }
}
