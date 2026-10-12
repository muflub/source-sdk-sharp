using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The sound-hint list CSoundEnt keeps, managed-built (plan_engineapi_vtables
/// E7): shell_soundent.cpp's four calls, the Entry.cs thunks' bodies moved.
/// All four are voids, so the generated catch's answer is right for each.
/// </summary>
public sealed unsafe class SoundEntBinding(Crossings crossings, IServerModule game) : IManagedSoundEnt
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    public void ServerSoundEntInit(int a0, float a1) { Enter(CrossingOp.ServerSoundEntInit); game.SoundEnt.Initialize(a0, a1); }

    public void ServerSoundEntShutdown() { Enter(CrossingOp.ServerSoundEntShutdown); game.SoundEnt.Shutdown(); }

    public void ServerSoundEntInsert(int a0, void* a1, int a2, float a3, uint a4, int a5, uint a6)
    {
        Enter(CrossingOp.ServerSoundEntInsert);
        if (a1 == null) return;

        float* xyz = (float*)a1;
        game.SoundEnt.Insert(a0, new Vector(xyz[0], xyz[1], xyz[2]), a2, a3,
                             new EntityHandle(a4), a5, new EntityHandle(a6));
    }

    public void ServerSoundEntResetClientSound(int a0) { Enter(CrossingOp.ServerSoundEntResetClientSound); game.SoundEnt.ResetClientSound(a0); }
}
