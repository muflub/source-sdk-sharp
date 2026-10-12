using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// CSoundControllerImp's calls into the managed sound controller, managed-built
/// (plan_engineapi_vtables E6): the Entry.cs thunks' bodies, moved. Every
/// failure answer is 0 (a void, or a pitch or volume of 0), which is what the
/// generated thunk's catch answers, so no slot keeps a catch of its own.
/// </summary>
public sealed unsafe class SoundControllerBinding(Crossings crossings, IServerModule game) : IManagedSoundController
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    public void ServerSoundControllerCreate(int a0) { Enter(CrossingOp.ServerSoundControllerCreate); game.SoundController.Create(a0); }

    public void ServerSoundControllerDestroy(int a0) { Enter(CrossingOp.ServerSoundControllerDestroy); game.SoundController.Destroy(a0); }

    public void ServerSoundControllerPlay(int a0, float a1, float a2, float a3)
    {
        Enter(CrossingOp.ServerSoundControllerPlay);
        game.SoundController.Play(a0, a1, a2, a3);
    }

    public float ServerSoundControllerPlayEnvelope(int a0, int a1, void* a2, int a3)
    {
        Enter(CrossingOp.ServerSoundControllerPlayEnvelope);
        if (a2 == null || a3 <= 0) return 0f;
        return game.SoundController.PlayEnvelope(a0, a1, new ReadOnlySpan<float>(a2, a3 * 4), a3);
    }

    public void ServerSoundControllerChangePitch(int a0, float a1, float a2)
    {
        Enter(CrossingOp.ServerSoundControllerChangePitch);
        game.SoundController.ChangePitch(a0, a1, a2);
    }

    public void ServerSoundControllerChangeVolume(int a0, float a1, float a2)
    {
        Enter(CrossingOp.ServerSoundControllerChangeVolume);
        game.SoundController.ChangeVolume(a0, a1, a2);
    }

    public float ServerSoundControllerGetPitch(int a0) { Enter(CrossingOp.ServerSoundControllerGetPitch); return game.SoundController.GetPitch(a0); }

    public float ServerSoundControllerGetVolume(int a0) { Enter(CrossingOp.ServerSoundControllerGetVolume); return game.SoundController.GetVolume(a0); }

    public void ServerSoundControllerFadeOut(int a0, float a1, int a2)
    {
        Enter(CrossingOp.ServerSoundControllerFadeOut);
        game.SoundController.FadeOut(a0, a1, a2 != 0);
    }

    public void ServerSoundControllerCommandAdd(int a0, float a1, int a2, float a3, float a4)
    {
        Enter(CrossingOp.ServerSoundControllerCommandAdd);
        game.SoundController.CommandAdd(a0, a1, a2, a3, a4);
    }

    public void ServerSoundControllerCommandClear(int a0) { Enter(CrossingOp.ServerSoundControllerCommandClear); game.SoundController.CommandClear(a0); }

    public void ServerSoundControllerReset() { Enter(CrossingOp.ServerSoundControllerReset); game.SoundController.SystemReset(); }

    public void ServerSoundControllerShutdown(int a0) { Enter(CrossingOp.ServerSoundControllerShutdown); game.SoundController.Shutdown(a0); }
}
