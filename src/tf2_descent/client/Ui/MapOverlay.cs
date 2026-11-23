using SourceSharp.Abi;
using SourceSharp.Client.Input;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// THE MAP OVERLAY'S TOGGLE. Tab shows the automap over the view, as Diablo's
// Tab map does: translucent, centred on the hero, the HUD still live. It takes
// Tab from TF2's +showscores: Descent shows no scoreboard (the party window, P,
// is its place). M keeps the full automap window.
//=============================================================================

/// <summary>Whether the map overlay is up: the client's own toggle, kept across levels as Diablo keeps it.</summary>
[SideService(Side.Client, As = [typeof(IDescentMapOverlay)])]
public sealed class MapOverlayState : IDescentMapOverlay
{
    public bool Shown { get; private set; }

    /// <summary>Shows the overlay when it is down and puts it down when it is up.</summary>
    public void Toggle() => Shown = !Shown;
}

/// <summary><c>tf2_descent_map_overlay</c>: shows the map overlay, or puts it down when it is up (the Tab key).</summary>
[ConCommand("tf2_descent_map_overlay", Help = "tf2_descent_map_overlay: show the TF2 Descent map over the view, or put it down when it is up")]
public sealed class MapOverlayCommand(MapOverlayState overlay)
{
    public void Execute() => overlay.Toggle();
}

// Tab, Diablo's map key, taken from TF2's +showscores (TFKeyActions' Order 620).
// After TF2's whole list and the other Descent keys, so on a fresh config this
// bind is the one Tab ends with.
[KeyAction("tf2_descent_map_overlay", "Map overlay", Section = "Descent", Order = 1160, Keys = ["TAB"])]
public sealed class MapOverlayKey;
