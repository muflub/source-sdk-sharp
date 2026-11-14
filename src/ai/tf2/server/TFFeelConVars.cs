using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Trees;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// <c>ai_tf_feel_carrier</c>: 0 makes the enemy carrying a bot's flag no one special as a target
/// (<see cref="TFFeelSwitches.Carrier"/>). Each of these six switches one part of the feel layer
/// (plan_bots_teams_tf2_part_g G.2.1) off, to tell in a live game which part changed the bots'
/// play; a bot reads them once, when it is added, so each is a cheat as <c>ai_sync</c> is.
/// </summary>
[ConVar("ai_tf_feel_carrier", "1", Help = "0: the enemy carrying a bot's flag is no special target (no tier, no bonus); for bots added afterwards", Flags = ConVarFlags.Cheat)]
public sealed class TFFeelCarrierConVar : BoolConVar { }

/// <summary><c>ai_tf_feel_search</c>: 0 and a bot does not go to look into a sound it heard (<see cref="TFFeelSwitches.Search"/>).</summary>
[ConVar("ai_tf_feel_search", "1", Help = "0: a bot does not go to look into a sound it heard; for bots added afterwards", Flags = ConVarFlags.Cheat)]
public sealed class TFFeelSearchConVar : BoolConVar { }

/// <summary><c>ai_tf_feel_aim</c>: 0 and a bot's aim error is a new draw at each decide, as it was before it wandered (<see cref="TFFeelSwitches.Aim"/>).</summary>
[ConVar("ai_tf_feel_aim", "1", Help = "0: a bot's aim error is a new draw each decide, where it wanders; for bots added afterwards", Flags = ConVarFlags.Cheat)]
public sealed class TFFeelAimConVar : BoolConVar { }

/// <summary><c>ai_tf_feel_retreat</c>: 0 and every bot goes for health under 40 %, whatever its skills (<see cref="TFFeelSwitches.Retreat"/>).</summary>
[ConVar("ai_tf_feel_retreat", "1", Help = "0: every bot goes for health under 40 %, not at the share its skills say; for bots added afterwards", Flags = ConVarFlags.Cheat)]
public sealed class TFFeelRetreatConVar : BoolConVar { }

/// <summary><c>ai_tf_feel_friendgate</c>: 0 and a bot fires with a teammate in the way (<see cref="TFFeelSwitches.FriendGate"/>).</summary>
[ConVar("ai_tf_feel_friendgate", "1", Help = "0: a bot does not hold its fire for a teammate in the way; for bots added afterwards", Flags = ConVarFlags.Cheat)]
public sealed class TFFeelFriendGateConVar : BoolConVar { }

/// <summary><c>ai_tf_feel_glance</c>: 0 and a bot on its way does not glance off its route (<see cref="TFFeelSwitches.Glance"/>).</summary>
[ConVar("ai_tf_feel_glance", "1", Help = "0: a bot on its way does not glance off its route; for bots added afterwards", Flags = ConVarFlags.Cheat)]
public sealed class TFFeelGlanceConVar : BoolConVar { }

/// <summary>
/// The feel layer's switches as the six convars stand now. <see cref="TFBotGame"/> reads them when
/// it makes a bot's decider, which keeps them for its life.
/// </summary>
[SideService(Side.Server)]
public sealed class TFFeelSwitchConVars(TFFeelCarrierConVar carrier, TFFeelSearchConVar search, TFFeelAimConVar aim, TFFeelRetreatConVar retreat,
                                        TFFeelFriendGateConVar friendGate, TFFeelGlanceConVar glance)
{
    public TFFeelSwitches Read() => new(carrier.Value, search.Value, aim.Value, retreat.Value, friendGate.Value, glance.Value);
}
