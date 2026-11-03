//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

namespace TF2.Client.Ui;

/// <summary>
/// The voice menus (stock's client/voice_menu.cpp over the labels
/// multiplay_gamerules.cpp reads from scripts/voicecommands.txt), as radial popups:
/// the same key again closes, only a player spawned in a game gets one, it closes
/// should they stop being, and a choice is the <c>voicemenu &lt;menu&gt; &lt;item&gt;</c> the server answers.
/// </summary>
public sealed class TFVoiceMenus(RadialMenus radial, IEngineFiles files, IFwdEngineClient engine, ITFLocalPlayer player, IObserverSource observer)
{
    public const string File = "scripts/voicecommands.txt";

    private string[][]? _menus;

    /// <summary>Each menu's labels, in the file's order.</summary>
    public static string[][] Parse(string text)
    {
        if (KeyValuesText.Parse(text).Find("voicemenus") is not { } root)
            return [];
        return [.. root.Children.Select(menu => menu.Children.Select(item => item.GetString("menu_label", "") ?? "").ToArray())];
    }

    private bool Spawned
        => CanSpeak(engine.IsInGame(), player.Team, player.ClassName, player.Health, observer.Mode);

    /// <summary>Only a player spawned in a game: a level up, on a playing team, a class chosen, alive and not observing.</summary>
    public static bool CanSpeak(bool inGame, int team, string className, int health, SpectatorMode observing)
        => inGame && team is 2 or 3 && className.Length > 0 && health > 0 && observing == SpectatorMode.None;

    public void Open(int menu)
    {
        if (!Spawned)
            return;
        var menus = _menus ??= files.ReadGameFile(File) is { } text ? Parse(text) : [];
        if (menu < 0 || menu >= menus.Length)
            return;
        radial.Toggle(menu, () => Page(menu, menus[menu]), () => Spawned, $"+voice_menu_{menu + 1}");
    }

    /// <summary>Letting go of the key that holds the menu up speaks what is under the pointer.</summary>
    public void Release(int menu) => radial.Release(menu);

    private RadialPage Page(int menu, string[] labels)
        => new($"#TF_Voice_Menu_{(char)('A' + menu)}",
               [.. labels.Select((label, item) => new RadialEntry(label, () => engine.SubmitConsoleLine($"voicemenu {menu} {item}")))]);
}

[ConCommand("+voice_menu_1", Help = "Shows voice menu 1 while held: a click, a number or letting go on a line speaks it")]
public sealed class TFVoiceMenu1HoldCommand(TFVoiceMenus menus) : IConCommand
{
    public void Execute(string args) => menus.Open(0);
}

[ConCommand("-voice_menu_1", Help = "Speaks the line under the pointer and closes voice menu 1")]
public sealed class TFVoiceMenu1LetGoCommand(TFVoiceMenus menus) : IConCommand
{
    public void Execute(string args) => menus.Release(0);
}

[ConCommand("+voice_menu_2", Help = "Shows voice menu 2 while held: a click, a number or letting go on a line speaks it")]
public sealed class TFVoiceMenu2HoldCommand(TFVoiceMenus menus) : IConCommand
{
    public void Execute(string args) => menus.Open(1);
}

[ConCommand("-voice_menu_2", Help = "Speaks the line under the pointer and closes voice menu 2")]
public sealed class TFVoiceMenu2LetGoCommand(TFVoiceMenus menus) : IConCommand
{
    public void Execute(string args) => menus.Release(1);
}

[ConCommand("+voice_menu_3", Help = "Shows voice menu 3 while held: a click, a number or letting go on a line speaks it")]
public sealed class TFVoiceMenu3HoldCommand(TFVoiceMenus menus) : IConCommand
{
    public void Execute(string args) => menus.Open(2);
}

[ConCommand("-voice_menu_3", Help = "Speaks the line under the pointer and closes voice menu 3")]
public sealed class TFVoiceMenu3LetGoCommand(TFVoiceMenus menus) : IConCommand
{
    public void Execute(string args) => menus.Release(2);
}

/// <summary>
/// Stock's command, for a config that binds it: it has no let-go, so it opens the menu to
/// click or number, and moves its key to the held command for next time.
/// </summary>
public abstract class TFVoiceMenuBoundCommand(TFVoiceMenus menus, IEngineHud hud, IFwdEngineClient engine, int menu) : IConCommand
{
    public void Execute(string args)
    {
        string command = $"voice_menu_{menu + 1}";
        if (hud.LookupBinding(command) is { Length: > 0 } key)
            engine.SubmitConsoleLine($"bind \"{key}\" \"+{command}\"");
        menus.Open(menu);
    }
}

[ConCommand("voice_menu_1", Help = "Opens voice menu 1")]
public sealed class TFVoiceMenu1Command(TFVoiceMenus menus, IEngineHud hud, IFwdEngineClient engine) : TFVoiceMenuBoundCommand(menus, hud, engine, 0);

[ConCommand("voice_menu_2", Help = "Opens voice menu 2")]
public sealed class TFVoiceMenu2Command(TFVoiceMenus menus, IEngineHud hud, IFwdEngineClient engine) : TFVoiceMenuBoundCommand(menus, hud, engine, 1);

[ConCommand("voice_menu_3", Help = "Opens voice menu 3")]
public sealed class TFVoiceMenu3Command(TFVoiceMenus menus, IEngineHud hud, IFwdEngineClient engine) : TFVoiceMenuBoundCommand(menus, hud, engine, 2);
