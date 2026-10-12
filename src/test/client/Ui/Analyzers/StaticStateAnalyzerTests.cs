using SourceSharp.Client.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui.Analyzers;

/// <summary>SSUI005: mutable static state in a UI assembly.</summary>
public class StaticStateAnalyzerTests
{
    [Fact]
    public void Mutable_statics_are_reported()
    {
        var found = AnalyzerHarness.Run(new StaticStateAnalyzer(), """
            public static class Bad
            {
                public static int Counter;
                public static readonly int[] Buffer = new int[4];
                public static readonly List<string> Seen = new();
                public static string Current { get; set; } = "";
                public static Dictionary<int, int> Cache { get; } = new();
            }
            """);
        Assert.All(found, d => Assert.Equal("SSUI005", d.Id));
        Assert.Equal(["Bad.Counter", "Bad.Buffer", "Bad.Seen", "Bad.Current", "Bad.Cache"],
                     found.Select(d => d.GetMessage().Split(' ')[0]).ToList());
    }

    [Fact]
    public void Constants_and_immutable_statics_stay_silent()
    {
        var found = AnalyzerHarness.Run(new StaticStateAnalyzer(), """
            public static class Good
            {
                public const int Max = 5;
                public static readonly string Name = "hud";
                public static readonly Tone Default = Tone.Accent;
                public static readonly UiColor Black = default;
                public static IReadOnlyList<string> Names { get; } = ["a", "b"];
                public static readonly Func<int, int> Twice = static x => x * 2;
                public static ReadOnlySpan<byte> Points => [1, 2, 3];
                public static int Doubled(int x) => x * 2;
            }
            """);
        Assert.Empty(found);
    }
}
