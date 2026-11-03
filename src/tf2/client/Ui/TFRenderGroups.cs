using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;

namespace TF2.Client.Ui;

/// <summary>
/// The render groups in game: the loader's current UI, which a UI-only reload
/// may swap between the asks (the TFMenuNavigation way). Stock locks the
/// hud's global group from inside the scoreboard's ShowPanel
/// (tf_clientscoreboard.cpp:441) and frees it on the hide (:469); here the
/// lock is the scoreboard's open state, read at the ask.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFRenderGroups)])]
public sealed class TFRenderGroups(UiLoader ui) : ITFRenderGroups
{
    public bool GlobalLocked => ui.Current?.IsOpen("scoreboard") ?? false;
}
