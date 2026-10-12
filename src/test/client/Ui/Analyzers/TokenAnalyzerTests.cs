using SourceSharp.Client.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui.Analyzers;

/// <summary>SSUI003: user-facing literals that are not #Tokens.</summary>
public class TokenAnalyzerTests
{
    [Fact]
    public void Literal_copy_into_a_token_parameter_is_reported()
    {
        var found = AnalyzerHarness.Run(new TokenAnalyzer(), """
            public sealed class Bad : UiPanel
            {
                public override void Describe(ref UiFrame ui)
                {
                    ui.Text("Choose your side", Text.Title);
                    ui.Label("Health");
                    ui.Button("join", "Join", primary: true);
                    ui.Tabs(["#SS_UI_Players", "Map"], 0);
                }
            }
            """);
        Assert.All(found, d => Assert.Equal("SSUI003", d.Id));
        Assert.Equal(["\"Choose your side\"", "\"Health\"", "\"Join\"", "\"Map\""], AnalyzerHarness.Spans(found));
    }

    [Fact]
    public void Tokens_game_data_and_keys_stay_silent()
    {
        var found = AnalyzerHarness.Run(new TokenAnalyzer(), """
            public sealed class Good(string playerName) : UiPanel
            {
                public override void Describe(ref UiFrame ui)
                {
                    ui.Text("#SS_UI_TeamMenu_Title", Text.Title);
                    ui.Data(playerName);
                    ui.Data("dm_lockdown", Text.Label);
                    ui.Button("join", "#SS_UI_TeamMenu_Join");   // "join" is a key, not copy
                    ui.KeyHintData("E", "Use the charger");     // map text is data
                }
            }
            """);
        Assert.Empty(found);
    }
}
