using SourceSharp.Client.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui.Analyzers;

/// <summary>SSUI001: allocation in a describe method.</summary>
public class AllocationAnalyzerTests
{
    [Fact]
    public void Each_allocating_shape_in_a_describe_method_is_reported()
    {
        var found = AnalyzerHarness.Run(new AllocationAnalyzer(), """
            public sealed class Bad(int[] values) : UiPanel
            {
                private int Helper() => 1;
                public override void Describe(ref UiFrame ui)
                {
                    int n = values.Length;
                    Func<int> captured = () => n;
                    var sum = values.Sum();
                    object boxed = n;
                    var list = new List<int>();
                    var arr = new int[4];
                    string joined = "a" + n;
                    string interp = $"{n} rounds";
                    string text = n.ToString();
                    Func<int> group = Helper;
                    ui.Data(text);
                }
            }
            """);
        var spans = AnalyzerHarness.Spans(found);
        Assert.All(found, d => Assert.Equal("SSUI001", d.Id));
        Assert.Contains("() => n", spans);
        Assert.Contains("values.Sum()", spans);
        Assert.Contains("n", spans);                          // the boxing conversion
        Assert.Contains("new List<int>()", spans);
        Assert.Contains("new int[4]", spans);
        Assert.Contains("\"a\" + n", spans);
        Assert.Contains("$\"{n} rounds\"", spans);
        Assert.Contains("n.ToString()", spans);
        Assert.Contains("Helper", spans);
    }

    [Fact]
    public void The_frames_text_handler_and_allowed_shapes_stay_silent()
    {
        var found = AnalyzerHarness.Run(new AllocationAnalyzer(), """
            public readonly record struct Point(int X, int Y);
            public sealed class Good(int[] values) : UiPanel
            {
                public override void Describe(ref UiFrame ui)
                {
                    int clip = values[0], reserve = values[1];
                    ui.Text($"{clip} / {reserve}", Text.Number);   // UiTextHandler: formats into the frame
                    ui.Number(clip);
                    Func<int, int> twice = static x => x * 2;
                    var p = new Point(1, 2);
                    if (ui.Naming)
                        ui.Data("row " + clip).Name($"row {clip}");
                    if (clip < 0)
                        throw new InvalidOperationException($"bad clip {clip}");
                    ui.Number(twice(p.X));
                }

                // Not a describe method: no ref UiFrame. Free to allocate.
                public string Describe() => string.Join(",", values.Select(v => v.ToString()));
            }
            """);
        Assert.Empty(found);
    }
}
