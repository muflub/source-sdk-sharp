using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The core's two game modes as the menus see them (plan_bots_teams_tf2 D.2):
/// deathmatch (the core's <c>"null"</c>, shown as "Deathmatch", plan Q13) and
/// team deathmatch (<c>tdm</c>), each with the settings that mean something
/// in it. Built the same way on both sides: the server's catalog publishes
/// them, and the client's create-server page reads them before any server
/// exists. Each call builds fresh specs: nothing here is state.
/// </summary>
public static class CoreModeSpecs
{
    public const string DeathmatchId = "null", TeamDeathmatchId = "tdm";

    public const string DeathmatchTitle = "#SS_UI_Mode_Deathmatch", TeamDeathmatchTitle = "#SS_UI_Mode_TeamDeathmatch";

    /// <summary>The title token of a setting: the vote ring's and the create-server page's label.</summary>
    public static string SettingToken(string key) => "#SS_UI_Vote_Setting_" + key;

    public static ModeSpec Deathmatch() => new(DeathmatchId, DeathmatchTitle, false, [TimeLimit(), FragLimit()], []);

    /// <summary>
    /// Team deathmatch's: the deathmatch limits, friendly fire, the team count,
    /// whether teammates collide, and the join limit, which the join rule reads
    /// for the next round (plan D.2: an unbalance limit is <see cref="SettingApplies.NextRound"/>).
    /// </summary>
    public static ModeSpec TeamDeathmatch() => new(TeamDeathmatchId, TeamDeathmatchTitle, true,
    [
        FriendlyFire(),
        TimeLimit(),
        FragLimit(),
        Choice("mp_tdm_teams", "2", ("2", "2"), ("3", "3"), ("4", "4")),
        Bool("mp_teammates_collide", "0"),
        Choice("mp_teams_unbalance_limit", "0", SettingApplies.NextRound, ("0", "#SS_UI_Vote_Off"), ("1", "1"), ("2", "2")),
    ], []);

    /// <summary>Both core modes, deathmatch first.</summary>
    public static IReadOnlyList<ModeSpec> All() => [Deathmatch(), TeamDeathmatch()];

    public static SettingSpec FriendlyFire() => Bool("mp_friendlyfire", "0");

    public static SettingSpec TimeLimit()
        => Choice("mp_timelimit", "0", ("0", "#SS_UI_Vote_NoLimit"), ("10", "#SS_UI_Vote_Min10"), ("20", "#SS_UI_Vote_Min20"),
                  ("30", "#SS_UI_Vote_Min30"), ("60", "#SS_UI_Vote_Min60"));

    public static SettingSpec FragLimit()
        => Choice("mp_fraglimit", "0", ("0", "#SS_UI_Vote_NoLimit"), ("10", "10"), ("20", "20"), ("30", "30"), ("50", "50"));

    /// <summary>An on/off setting: off is 0, on is 1.</summary>
    public static SettingSpec Bool(string key, string defaultValue, SettingApplies applies = SettingApplies.Now)
        => new(key, SettingKind.Bool, [new("0", "#SS_UI_Vote_Off"), new("1", "#SS_UI_Vote_On")], defaultValue, applies, Min: 0f, Max: 1f);

    public static SettingSpec Choice(string key, string defaultValue, params (string Value, string Label)[] choices)
        => Choice(key, defaultValue, SettingApplies.Now, choices);

    public static SettingSpec Choice(string key, string defaultValue, SettingApplies applies, params (string Value, string Label)[] choices)
        => new(key, SettingKind.Choice, [.. choices.Select(c => new SettingChoice(c.Value, c.Label))], defaultValue, applies);
}
