using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The movement's two sounds (IMoveHelper::StartSound: a fall, a swim stroke),
/// which native's move helper hands the managed SoundEmitter: the player's
/// index, where it is, and the sound. Who hears it is decided there.
/// </summary>
public interface IMoveSoundSink
{
    /// <summary>StartSound( origin, channel, sample, volume, soundlevel, flags, pitch ).</summary>
    void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel, int flags,
                   int pitch);

    /// <summary>StartSound( origin, soundname ).</summary>
    void MoveSoundScript(int entIndex, Vector origin, string soundName);
}

/// <summary>
/// IManagedSoundEmitter, managed-built (plan_server_native_v2 T4a): the
/// standalone player's move helper calls it where it called GameSound_Emit.
/// </summary>
public sealed unsafe class SoundEmitterBinding(Crossings crossings, IMoveSoundSink sink) : IManagedSoundEmitter
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    public void ServerMoveSound(int a0, void* a1, int a2, byte* a3, float a4, int a5, int a6, int a7)
    {
        Enter(CrossingOp.ServerMoveSound);
        if (a1 == null || a3 == null)
            return;

        float* xyz = (float*)a1;
        sink.MoveSound(a0, new Vector(xyz[0], xyz[1], xyz[2]), a2, Vtable.TextOf(a3, 512) ?? string.Empty, a4, a5, a6, a7);
    }

    public void ServerMoveSoundScript(int a0, void* a1, byte* a2)
    {
        Enter(CrossingOp.ServerMoveSoundScript);
        if (a1 == null || a2 == null)
            return;

        float* xyz = (float*)a1;
        sink.MoveSoundScript(a0, new Vector(xyz[0], xyz[1], xyz[2]), Vtable.TextOf(a2, 512) ?? string.Empty);
    }
}
