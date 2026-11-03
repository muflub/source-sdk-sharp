using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// What a round cleanup asks of IVEngineServer about the edict table (tf2
/// ruling N2): whether a map entity's old slot is free again, and the engine's
/// leave to reuse freed slots at once. <c>EngineServer</c> answers both through
/// its forwarders.
/// </summary>
public interface IEdictSlots
{
    /// <summary>
    /// <c>engine-&gt;PEntityOfEntIndex( index ) != NULL</c>: whether edict
    /// <paramref name="index"/> holds an entity. False for a negative index.
    /// </summary>
    bool InUse(int index);

    /// <summary>
    /// <c>engine-&gt;AllowImmediateEdictReuse()</c>: every freed edict past the
    /// clients may be handed out again in this frame, rather than after the
    /// engine's one-second hold.
    /// </summary>
    void AllowImmediateReuse();
}
