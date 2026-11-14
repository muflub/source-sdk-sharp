using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Modes;

/// <summary>
/// TF2's modes as the menus see them (plan_bots_teams_tf2 D.2): stock's game
/// type names, the limits every TF2 mode has, and the entity a map needs to
/// play each, the same classes <see cref="TFGameModeSelector"/> picks by.
/// </summary>
public static class TFModeSettings
{
    /// <summary>The limits: friendly fire, the time limit, the round and win limits.</summary>
    public static IReadOnlyList<SettingSpec> Rows() =>
    [
        CoreModeSpecs.FriendlyFire(),
        CoreModeSpecs.TimeLimit(),
        CoreModeSpecs.Choice("mp_maxrounds", "0", ("0", "#SS_UI_Vote_NoLimit"), ("3", "3"), ("5", "5"), ("10", "10")),
        CoreModeSpecs.Choice("mp_winlimit", "0", ("0", "#SS_UI_Vote_NoLimit"), ("2", "2"), ("3", "3"), ("5", "5")),
    ];

    /// <summary>The mode <paramref name="id"/>'s title token and the class its map must hold.</summary>
    public static IModeSettings For(string id) => id switch
    {
        CtfMode.ModeId => new ModeSettings("#TF2_Gametype_Ctf", Rows(), [TFGameModeSelector.FlagClass]),
        PayloadMode.ModeId => new ModeSettings("#TF2_Gametype_Pl", Rows(), [TFGameModeSelector.TrainWatcherClass]),
        KothMode.ModeId => new ModeSettings("#TF2_Gametype_Koth", Rows(), [TFGameModeSelector.KothLogicClass]),
        CPMode.ModeId => new ModeSettings("#TF2_Gametype_Cp", Rows(), [TFGameModeSelector.ControlPointMasterClass]),
        _ => new ModeSettings(id, Rows(), []),
    };
}
