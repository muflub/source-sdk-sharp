using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The sound-hint list CSoundEnt keeps, managed-built (plan_engineapi_vtables
/// E7): shell_soundent.cpp's calls, the Entry.cs thunks' bodies moved. All
/// three are voids, so the generated catch's answer is right for each. The
/// fourth, ServerSoundEntInsert, had only test callers once the inserts were
/// managed (BlastSweep and the sound emitters call SoundEnt.Insert) and left in
/// the serialised ABI pass (2026-10-20).
/// </summary>
public sealed unsafe class SoundEntBinding(Crossings crossings, IServerModule game) : IManagedSoundEnt
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    public void ServerSoundEntInit(int a0, float a1) { Enter(CrossingOp.ServerSoundEntInit); game.SoundEnt.Initialize(a0, a1); }

    public void ServerSoundEntShutdown() { Enter(CrossingOp.ServerSoundEntShutdown); game.SoundEnt.Shutdown(); }

    public void ServerSoundEntResetClientSound(int a0) { Enter(CrossingOp.ServerSoundEntResetClientSound); game.SoundEnt.ResetClientSound(a0); }
}
