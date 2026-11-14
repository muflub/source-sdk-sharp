using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>Client half of <c>mp_teammates_collide</c> (the server's <c>MpTeammatesCollideConVar</c>).</summary>
[ConVar("mp_teammates_collide", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "In a team mode whose teammates walk through each other, 1 makes teammates block each other again")]
public sealed class MpTeammatesCollideConVar : BoolConVar { }

/// <summary>
/// Client half of <c>mp_teammates_pass</c>: the server's answer to "do
/// teammates walk through each other now", which the prediction's collide rule
/// reads (<c>ClientTeammatesPassRule</c>).
/// </summary>
[ConVar("mp_teammates_pass", "0", Flags = ConVarFlags.Replicated,
        Help = "Set by the server from the active mode and mp_teammates_collide: whether teammates walk through each other")]
public sealed class MpTeammatesPassConVar : BoolConVar { }

/// <summary>Client half of <c>mp_teammates_pushaway</c>.</summary>
[ConVar("mp_teammates_pushaway", "1", Flags = ConVarFlags.Replicated,
        Help = "Whether teammates who walk through each other push each other apart when they overlap")]
public sealed class MpTeammatesPushawayConVar : BoolConVar { }
