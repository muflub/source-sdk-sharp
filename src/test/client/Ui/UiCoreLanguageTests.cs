using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The UI's own copy (#SS_UI_*) is the core's, not a mod folder's: a game
/// mod with no resource/mod_sharp_english.txt (tf2_descent on the k3s host,
/// 2026-10-11) drew raw #SS_UI_ tokens. The core strings ship inside the
/// toolkit; a mod's language file is asked first and may override them.
/// </summary>
public sealed class UiCoreLanguageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-uilang-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void ModFile(string tokens)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "resource"));
        File.WriteAllText(Path.Combine(_dir, ClientUi.LanguageFile),
                          "\"lang\"\n{\n\"Language\" \"English\"\n\"Tokens\"\n{\n" + tokens + "\n}\n}\n", Encoding.Unicode);
    }

    [Fact]
    public void A_mod_with_no_language_file_still_has_the_core_ui_strings()
    {
        var lang = ClientUi.Language(_dir, new RecordingLog());
        Assert.NotNull(lang);
        Assert.Equal("OK", lang!.Find("#SS_UI_Ok"));
        Assert.Equal("Cancel", lang.Find("#SS_UI_Cancel"));
        Assert.Equal("Health", lang.Find("#SS_UI_Health"));
    }

    [Fact]
    public void A_mods_file_is_asked_first_and_its_own_tokens_join_the_core()
    {
        ModFile("\"SS_UI_Ok\" \"Righto\"\n\"Mod_Only\" \"From the mod\"");
        var lang = ClientUi.Language(_dir, new RecordingLog())!;
        Assert.Equal("Righto", lang.Find("#SS_UI_Ok"));          // the mod overrides the core
        Assert.Equal("From the mod", lang.Find("#Mod_Only"));
        Assert.Equal("Cancel", lang.Find("#SS_UI_Cancel"));      // and the core fills the rest
    }

    [Fact]
    public void The_mod_test_file_no_longer_carries_the_core_strings()
    {
        // One home for each string: the core's #SS_UI_ copy left mods/test's
        // file, which keeps the game's own (weapon names, chat formats).
        var mod = Client.Ui.Toolkit.UiLanguageFile.Load(RepoLayout.Repo("game", "mod_test", "resource", "mod_sharp_english.txt"));
        Assert.Null(mod.Find("#SS_UI_Ok"));
        Assert.Equal("OVERWATCH STANDARD ISSUE\n(PULSE-RIFLE)", mod.Find("#HL2_Pulse_Rifle"));
    }
}
