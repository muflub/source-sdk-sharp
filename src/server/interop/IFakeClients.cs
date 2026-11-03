namespace SourceSharp.Server.Interop;

/// <summary>
/// What a bot needs of IVEngineServer to be a client (plan_hl2mp_ai §3.1): the
/// engine's own client-slot allocator, and the USERINFO convars a fake client
/// has no console to set. <c>EngineServer</c> answers both through the
/// forwarders <c>CreateFakeClientEx</c>, <c>IndexOfEdict</c>,
/// <c>PEntityOfEntIndex</c> and <c>SetFakeClientConVarValue</c>.
/// </summary>
public interface IFakeClients
{
    /// <summary>
    /// <c>engine-&gt;CreateFakeClientEx( name, true )</c>, then
    /// <c>IndexOfEdict</c> of the edict it answers: the new client's entity
    /// index, or 0 when the engine made none (the server is full). The engine
    /// calls <c>ClientPutInServer</c> for the new client before this returns.
    /// </summary>
    int CreateFakeClient(string name);

    /// <summary>
    /// <c>engine-&gt;SetFakeClientConVarValue( edict, name, value )</c> on the
    /// client at <paramref name="entIndex"/>; false when the slot holds no
    /// edict.
    /// </summary>
    bool SetFakeClientConVar(int entIndex, string name, string value);
}
