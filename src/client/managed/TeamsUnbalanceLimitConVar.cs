using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// Client half of <c>mp_teams_unbalance_limit</c>, the server's join limit
/// for the modes that play with teams (team deathmatch's join rule, TF2's
/// round rules). Replicated, as stock declares it on both sides
/// (teamplayroundbased_gamerules.cpp:219), so the client keeps its copy of
/// the name, default, bounds and flags; a mod's <c>ConVarDefault</c> row
/// (TF2's 1) changes both halves alike.
/// </summary>
[ConVar("mp_teams_unbalance_limit", "0", Flags = ConVarFlags.Replicated, Min = 0f, Max = 30f,
        Help = "Teams are unbalanced when one team has this many more players than the other team. (0 disables check)")]
public sealed class ClTeamsUnbalanceLimitConVar : IntConVar { }
