using SourceSharp.Client.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui.Analyzers;

/// <summary>SSUI004: a widget key in a loop that does not use the loop variable.</summary>
public class LoopKeyAnalyzerTests
{
    [Fact]
    public void A_constant_key_in_a_loop_is_reported()
    {
        var found = AnalyzerHarness.Run(new LoopKeyAnalyzer(), """
            public sealed class Bad(string[] names) : UiPanel
            {
                public override void Describe(ref UiFrame ui)
                {
                    for (int i = 0; i < names.Length; i++)
                        ui.Rect(Tone.Accent, Size.Fill, Size.Du(32)).Key("row");
                    foreach (var name in names)
                        ui.Button("pick", "#SS_UI_TeamMenu_Join");
                }
            }
            """);
        Assert.All(found, d => Assert.Equal("SSUI004", d.Id));
        Assert.Equal(["\"row\"", "\"pick\""], AnalyzerHarness.Spans(found));
        Assert.Contains("over i", found[0].GetMessage());
        Assert.Contains("over name", found[1].GetMessage());
    }

    [Fact]
    public void Keys_from_the_loop_variable_or_its_derivatives_stay_silent()
    {
        var found = AnalyzerHarness.Run(new LoopKeyAnalyzer(), """
            public sealed class Good(string[] names, int[] ids) : UiPanel
            {
                public override void Describe(ref UiFrame ui)
                {
                    for (int i = 0; i < names.Length; i++)
                        ui.Rect(Tone.Accent, Size.Fill, Size.Du(32)).Key(i);
                    foreach (var name in names)
                        ui.Button(name, "#SS_UI_TeamMenu_Join");
                    for (int i = 0; i < ids.Length; i++)
                    {
                        int id = ids[i];
                        int shown = id * 2;
                        ui.Rect(Tone.Accent, Size.Fill, Size.Du(32)).Key(shown);
                    }
                    ui.Box("outside").Dispose();                 // not in a loop
                    var lookup = new Dictionary<string, int>();
                    foreach (var name in names)
                        lookup.TryGetValue("constant", out _);   // a dictionary's key, not a widget's
                }
            }
            """);
        Assert.Empty(found);
    }
}
