//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>Stock's cl_showpausedimage (vgui_int.cpp): off, a pause shows no plaque.</summary>
[ConVar("cl_showpausedimage", "1", Help = "Show the 'Paused' image when game is paused.")]
public sealed class ClShowPausedImageConVar : BoolConVar
{
}

/// <summary>
/// THE LOADING AND PAUSED PLAQUES (stock CLoadingDisc, vgui_loadingdiscpanel.cpp,
/// shown by VGui_PreRender's rule, vgui_int.cpp): from the engine's facts as the
/// shell forwards them on change. Loading while the engine draws its loading
/// image and no demo plays; paused while the game is paused, the game UI is
/// not up, cl_showpausedimage is on, no screenshot is being taken and no demo
/// plays. The loading plaque says #GameUI_LoadingGame (LoadingDiscPanel.res's
/// label); the paused one #GameUI_Paused, or #GameUI_PausedTimer with the
/// seconds left on a timed pause. The text is made when it changes, never per frame.
/// </summary>
public sealed class LoadingDisc(IEngineHud hud, ClShowPausedImageConVar? showPaused = null) : ILoadingDiscSource, ILoadingDiscEvents
{
    public bool Loading { get; private set; }

    public bool Paused { get; private set; }

    public string LoadingText { get; private set; } = string.Empty;

    public string PausedText { get; private set; } = string.Empty;

    private int _seconds = -1;

    public void State(PlaqueFacts facts, int pausedSeconds)
    {
        bool demo = (facts & PlaqueFacts.PlayingDemo) != 0;
        Loading = (facts & PlaqueFacts.DrawingLoadingImage) != 0 && !demo;
        if (Loading && LoadingText.Length == 0)
            LoadingText = Find("#GameUI_LoadingGame");

        bool paused = (facts & PlaqueFacts.Paused) != 0
                      && (facts & (PlaqueFacts.GameUiVisible | PlaqueFacts.TakingScreenshot)) == 0
                      && !demo && (showPaused is null || showPaused.Value);
        int seconds = paused ? pausedSeconds : -1;
        if (paused && (!Paused || seconds != _seconds))
            PausedText = seconds >= 0
                ? TextMessages.Construct(Find("#GameUI_PausedTimer"), seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "", "", "")
                : Find("#GameUI_Paused");
        Paused = paused;
        _seconds = seconds;
    }

    private string Find(string token)
    {
        string s = hud.Localise(token);
        return s.Length == 0 ? token : s;
    }
}
