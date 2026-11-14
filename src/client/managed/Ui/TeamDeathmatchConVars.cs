using SourceSharp.Abi;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// Client half of <c>mp_tdm_teams</c> (user 2026-11-08, "also support 2/3/4
/// teams"): how many teams team deathmatch plays, 2 to 4, replicated so the
/// client knows every team that exists, the empty ones too. The server's
/// declaration is the rules'; a replicated ConVar is a contract between the two
/// DLLs, so the client keeps its copy of the name, default, bounds and flags.
/// </summary>
[ConVar("mp_tdm_teams", "2", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Team deathmatch: how many teams play (2-4)",
        Min = 2f, Max = 4f)]
public sealed class ClTdmTeamsConVar : IntConVar { }
