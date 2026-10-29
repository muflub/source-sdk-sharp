#if DEBUG
using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// <c>cs_ui_texture &lt;material&gt;</c> — the HUD art library's report: it
/// loads the named material the way a screen would and says what came back.
/// Debug builds only, like every diagnostic command.
///
/// <para>
/// The library is the UI's, which a UI-only reload replaces with its whole
/// context, so the command asks for the current one each time: holding the
/// first one kept the old UI context alive (live gate hot-reload, LEAK ARM).
/// </para>
/// </summary>
[ConCommand("cs_ui_texture",
            Help = "SourceSharp: load a HUD material through the UI texture library and report it (cs_ui_texture <material>).")]
public sealed class UiTextureCommand(IUiTextureSource source, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string name = args.Trim();
        if (name.Length == 0)
        {
            log.Msg("[SourceSharp/Ui] usage: cs_ui_texture <material>, e.g. hud/objectives_flagpanel_ico_flag_home\n");
            return;
        }
        if (source.Textures is not { } textures)
        {
            log.Msg($"[SourceSharp/Ui] {name}: no UI is loaded, so there is no texture library\n");
            return;
        }

        int handle = textures.Find(name);
        var info = textures.About(name);
        if (info is not { } loaded)
        {
            log.Msg($"[SourceSharp/Ui] {name}: no texture (0); the library's warning above says why\n");
            return;
        }
        log.Msg($"[SourceSharp/Ui] {name}: handle {loaded.Handle}, {loaded.Wide}x{loaded.Tall}, {loaded.Format}\n");
    }
}
#endif
