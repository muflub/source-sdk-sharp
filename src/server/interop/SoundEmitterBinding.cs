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
/// The sound emitter system's two native edges (plan_server_native_v2 T4a,
/// the precache path): the SourceSharpManaged row's Init, which runs the
/// system's first, and the script precache the world's own precache and the
/// registered EffectsPrecache still ask for.
/// </summary>
public interface ISoundSystemSink
{
    /// <summary><c>CSoundEmitterSystem::Init</c>: false stops the server coming up.</summary>
    bool SystemInit();

    /// <summary><c>GameSound_PrecacheScript( name )</c>.</summary>
    void PrecacheScriptSound(string soundName);
}

/// <summary>
/// IManagedSoundEmitter, managed-built (plan_server_native_v2 T4a): the
/// standalone player's move helper calls it where it called GameSound_Emit,
/// and native's last sound-system callers where they called the native
/// CSoundEmitterSystem.
/// </summary>
public sealed unsafe class SoundEmitterBinding(Crossings crossings, IMoveSoundSink sink, ISoundSystemSink system)
    : IManagedSoundEmitter
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

    public byte ServerSoundSystemInit()
    {
        Enter(CrossingOp.ServerSoundSystemInit);
        return system.SystemInit() ? (byte)1 : (byte)0;
    }

    public void ServerPrecacheScriptSound(byte* a0)
    {
        Enter(CrossingOp.ServerPrecacheScriptSound);
        if (a0 == null)
            return;

        system.PrecacheScriptSound(Vtable.TextOf(a0, 512) ?? string.Empty);
    }
}
