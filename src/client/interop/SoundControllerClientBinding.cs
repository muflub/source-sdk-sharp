using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's sound envelope controller as its shim asks it (sound cluster
/// 17, lane c2-sims): the shared controller's members, by patch handle.
/// </summary>
public interface IClientSoundController
{
    void Create(int patch);
    void Destroy(int patch);
    void Play(int patch, float volume, float pitch, float startTime);
    float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count);
    void ChangePitch(int patch, float target, float deltaTime);
    void ChangeVolume(int patch, float target, float deltaTime);
    float GetPitch(int patch);
    float GetVolume(int patch);
    void FadeOut(int patch, float deltaTime, bool destroyOnFadeout);
    void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value);
    void CommandClear(int patch);
    void SystemReset();
    void Shutdown(int patch);
}

/// <summary>
/// <see cref="IManagedSoundControllerClient"/>, managed-built: each body counts
/// its own crossing. Every failure answer is 0, what the generated thunk's
/// catch answers, as the server's binding.
/// </summary>
public sealed unsafe class SoundControllerClientBinding(Crossings crossings, IClientSoundController sounds) : IManagedSoundControllerClient
{
    public void ClientSoundControllerCreate(int a0) { crossings.Count(CrossingOp.ClientSoundControllerCreate); sounds.Create(a0); }

    public void ClientSoundControllerDestroy(int a0) { crossings.Count(CrossingOp.ClientSoundControllerDestroy); sounds.Destroy(a0); }

    public void ClientSoundControllerPlay(int a0, float a1, float a2, float a3)
    {
        crossings.Count(CrossingOp.ClientSoundControllerPlay);
        sounds.Play(a0, a1, a2, a3);
    }

    public float ClientSoundControllerPlayEnvelope(int a0, int a1, void* a2, int a3)
    {
        crossings.Count(CrossingOp.ClientSoundControllerPlayEnvelope);
        if (a2 == null || a3 <= 0) return 0f;
        return sounds.PlayEnvelope(a0, a1, new ReadOnlySpan<float>(a2, a3 * 4), a3);
    }

    public void ClientSoundControllerChangePitch(int a0, float a1, float a2)
    {
        crossings.Count(CrossingOp.ClientSoundControllerChangePitch);
        sounds.ChangePitch(a0, a1, a2);
    }

    public void ClientSoundControllerChangeVolume(int a0, float a1, float a2)
    {
        crossings.Count(CrossingOp.ClientSoundControllerChangeVolume);
        sounds.ChangeVolume(a0, a1, a2);
    }

    public float ClientSoundControllerGetPitch(int a0) { crossings.Count(CrossingOp.ClientSoundControllerGetPitch); return sounds.GetPitch(a0); }

    public float ClientSoundControllerGetVolume(int a0) { crossings.Count(CrossingOp.ClientSoundControllerGetVolume); return sounds.GetVolume(a0); }

    public void ClientSoundControllerFadeOut(int a0, float a1, int a2)
    {
        crossings.Count(CrossingOp.ClientSoundControllerFadeOut);
        sounds.FadeOut(a0, a1, a2 != 0);
    }

    public void ClientSoundControllerCommandAdd(int a0, float a1, int a2, float a3, float a4)
    {
        crossings.Count(CrossingOp.ClientSoundControllerCommandAdd);
        sounds.CommandAdd(a0, a1, a2, a3, a4);
    }

    public void ClientSoundControllerCommandClear(int a0) { crossings.Count(CrossingOp.ClientSoundControllerCommandClear); sounds.CommandClear(a0); }

    public void ClientSoundControllerReset() { crossings.Count(CrossingOp.ClientSoundControllerReset); sounds.SystemReset(); }

    public void ClientSoundControllerShutdown(int a0) { crossings.Count(CrossingOp.ClientSoundControllerShutdown); sounds.Shutdown(a0); }
}
