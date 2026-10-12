using SourceSharp.Abi;

namespace SourceSharp.Client.ViewCamera;

/// <summary>
/// cs_cl_overview: the second camera's mode (lane camproto). 0 off, 1 the local
/// player from behind and above, 2 the ground around them from straight above.
/// A DEBUG camera (user ruling 2026-10-11), not gameplay: a cheat, and
/// ViewCameraService treats it as 0 without sv_cheats 1.
/// </summary>
[ConVar("cs_cl_overview", "0", Min = 0, Max = 2, Flags = ConVarFlags.Cheat,
        Help = "Second camera in a HUD panel: 0 off, 1 third person, 2 top-down overview")]
public sealed class ViewCameraModeConVar : IntConVar { }

/// <summary>
/// cs_cl_overview_every: draw the second camera on one frame in this many, the
/// first knob on its cost. 2 by default: every frame cost 26-27 client
/// crossings a frame against the live gate's render ceiling of 26, one frame
/// in two 22-23 (plan_client_novalve C7.6).
/// </summary>
[ConVar("cs_cl_overview_every", "2", Min = 1, Max = 60, Flags = ConVarFlags.Cheat,
        Help = "Draw the second camera every Nth frame")]
public sealed class ViewCameraEveryConVar : IntConVar { }
