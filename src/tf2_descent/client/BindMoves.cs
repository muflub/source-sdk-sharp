using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Client.Ui;

namespace TF2.Descent.Client;

//=============================================================================
// THE DEFAULT BINDS A CONFIG PREDATES. A key action's keys reach only a fresh
// config (KeyActions.BindDefaults): a player whose config.cfg was written
// before Descent took Tab for the map overlay and E for use keeps TF2's
// +showscores and dropitem there. Once per config, at the first level, these
// keys move to Descent's binds, and the config records that they have
// (tf2_descent_binds, archived): a key the player rebinds afterwards stays his.
//=============================================================================

/// <summary>The version of Descent's default binds the config holds; 0 is a config from before any moved.</summary>
[ConVar("tf2_descent_binds", "0", Flags = ConVarFlags.Archive, Help = "The version of TF2 Descent's default binds this config holds (the moves run once, below the current one)")]
public sealed class DescentBindsConVar : IntConVar { }

/// <summary>At the first level a config plays, binds the keys Descent took from TF2 and records the version.</summary>
[SideService(Side.Client)]
public sealed class DescentBindMoves(DescentBindsConVar binds, IDescentCommandLine console) : IClientLevelHook, IOrderedHook
{
    /// <summary>The binds' version: 1 moved Tab to the map overlay and E to use.</summary>
    public const int Current = 1;

    /// <summary>A unique order among the mod's level hooks.</summary>
    public int HookOrder => 1601;

    // The keys version 1 moved, each with the bind it ends with (MapOverlayKey, UseKey).
    private readonly (string Key, string Command)[] _moves =
    [
        ("TAB", "tf2_descent_map_overlay"),
        ("e", "+use"),
    ];

    public void OnLevelInit(string mapName)
    {
        if (binds.Value >= Current)
            return;
        foreach (var (key, command) in _moves)
            console.Send($"bind \"{key}\" \"{command}\"");
        console.Send($"tf2_descent_binds {Current}");
    }
}
