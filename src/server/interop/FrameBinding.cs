using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's frame and level edges, managed-built (plan_engineapi_vtables
/// E18): the frame's pre-think, middle and post-think, and the level's init and
/// shutdown. These are the Entry.cs thunks' bodies, moved. All five are voids,
/// so the generated catch's answer is right for each. The module's unload is
/// not a slot: UnloadSide does it, on the line after the struct's Shutdown slot
/// used to. The level shutdown clears the composition's liveness mirror and
/// the server engine's own model caches (plan_abi_hotreload D8), all three
/// handed in by the composition that builds this.
/// </summary>
public sealed unsafe class FrameBinding(Crossings crossings, ILog log, ISideModule module, LivenessMirror liveness,
                                        ModelDescriptors models, BreakModels breaks) : IManagedFrame
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    public void Frame(float a0) { Enter(CrossingOp.ServerFrame); module.Frame(a0); }

    public void FramePreThink(float a0) { Enter(CrossingOp.ServerFramePreThink); module.FramePreThink(a0); }

    public void FramePostThink(float a0) { Enter(CrossingOp.ServerFramePostThink); module.FramePostThink(a0); }

    public void LevelInit(byte* a0)
    {
        Enter(CrossingOp.ServerLevelInit);
        log.Msg("[SourceSharp] > managed ServerLevelInit\n");
        module.LevelInit(a0 == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)a0) ?? string.Empty));
    }

    /// <summary>
    /// The level's end, and the three tables keyed by this level's entities and
    /// models with it, as the thunk cleared them: this side's own.
    /// </summary>
    public void LevelShutdown()
    {
        Enter(CrossingOp.ServerLevelShutdown);
        log.Msg("[SourceSharp] > managed ServerLevelShutdown\n");
        module.LevelShutdown();
        liveness.Clear();
        models.Clear();
        breaks.Clear();
    }
}
