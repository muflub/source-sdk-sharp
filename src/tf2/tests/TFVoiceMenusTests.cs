extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using Xunit;

using TFVoiceMenus = client::TF2.Client.Ui.TFVoiceMenus;

namespace TF2.Tests;

/// <summary>The voice menus' labels, from the file's own shape.</summary>
public sealed class TFVoiceMenusTests
{
    private const string File = """
        "voicemenus"
        {
            "menu_0"
            {
                "1" { "concept" "TLK_PLAYER_MEDIC" "menu_label" "#Voice_Menu_Medic" }
                "2" { "concept" "TLK_PLAYER_THANKS" "menu_label" "#Voice_Menu_Thanks" }
            }
            "menu_1"
            {
                "1" { "concept" "TLK_PLAYER_INCOMING" "menu_label" "#Voice_Menu_Incoming" }
            }
        }
        """;

    [Fact]
    public void Each_menu_s_labels_read_in_the_file_s_order()
    {
        var menus = TFVoiceMenus.Parse(File);
        Assert.Equal(2, menus.Length);
        Assert.Equal(["#Voice_Menu_Medic", "#Voice_Menu_Thanks"], menus[0]);
        Assert.Equal(["#Voice_Menu_Incoming"], menus[1]);
    }

    [Theory]
    [InlineData(true, 2, "Scout", 125, true)]
    [InlineData(true, 3, "Spy", 1, true)]
    [InlineData(false, 2, "Scout", 125, false)]   // at the menu
    [InlineData(true, 0, "Scout", 125, false)]    // no team yet
    [InlineData(true, 1, "Scout", 125, false)]    // spectating
    [InlineData(true, 2, "", 125, false)]         // no class yet
    [InlineData(true, 2, "Scout", 0, false)]      // dead
    public void Only_a_player_spawned_in_a_game_can_speak(bool inGame, int team, string className, int health, bool can)
        => Assert.Equal(can, TFVoiceMenus.CanSpeak(inGame, team, className, health, client::SourceSharp.Client.Ui.SpectatorMode.None));

    [Fact]
    public void An_observer_cannot_speak()
        => Assert.False(TFVoiceMenus.CanSpeak(true, 2, "Scout", 125, client::SourceSharp.Client.Ui.SpectatorMode.Roaming));

    [Fact]
    public void A_file_without_the_root_has_no_menus()
        => Assert.Empty(TFVoiceMenus.Parse("\"other\" { }"));
}
