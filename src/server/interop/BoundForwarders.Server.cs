// The engine interfaces a composition registers (EngineServices.AddTo): each
// member is one delegation to the side's bound slot, and the compiler holds
// every interface member implemented.

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IFwdEngineSoundServer"/> over the side's bound IEngineSound (server): each call asks
/// <see cref="EngineBound.SoundServer"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundSoundServer(EngineSide side) : IFwdEngineSoundServer
{
    public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity)
        => EngineBound.SoundServer(side).EmitSound(filter, entIndex, channel, sample, volume, soundLevel, flags, pitch, specialDsp, origin, soundTime, speakerEntity);

    public void StopSound(int entIndex, int channel, string sample)
        => EngineBound.SoundServer(side).StopSound(entIndex, channel, sample);

    public float SoundDuration(string sample)
        => EngineBound.SoundServer(side).SoundDuration(sample);

    public bool PrecacheSound(string sample, bool preload)
        => EngineBound.SoundServer(side).PrecacheSound(sample, preload);

    public bool IsSoundPrecached(string sample)
        => EngineBound.SoundServer(side).IsSoundPrecached(sample);

    public void PrefetchSound(string sample)
        => EngineBound.SoundServer(side).PrefetchSound(sample);
}

/// <summary>
/// <see cref="IFwdFileSystem"/> over the side's bound IFileSystem: each call asks
/// <see cref="EngineBound.FileSystem"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundFileSystem(EngineSide side) : IFwdFileSystem
{
    public void CreateDirHierarchy(string path, string pathId)
        => EngineBound.FileSystem(side).CreateDirHierarchy(path, pathId);
}

/// <summary>
/// The area portal records' three IVEngineServer calls, over the same bound
/// slot as the rest of <see cref="BoundEngineServer"/> (EngineServer answers
/// them; the shared half is in BoundForwarders.cs).
/// </summary>
internal sealed partial class BoundEngineServer : SourceSharp.Server.Interop.IAreaPortalEngine
{
    private SourceSharp.Server.Interop.IAreaPortalEngine Portals
        => (SourceSharp.Server.Interop.IAreaPortalEngine)EngineBound.Server(side);

    public void SetAreaPortalState(int portalNumber, bool open)
        => Portals.SetAreaPortalState(portalNumber, open);

    public void SetAreaPortalStates(ReadOnlySpan<int> portalNumbers, ReadOnlySpan<int> isOpen)
        => Portals.SetAreaPortalStates(portalNumbers, isOpen);

    public bool GetAreaPortalPlane(Vector viewOrigin, int portalKey, out Vector normal, out float distance)
        => Portals.GetAreaPortalPlane(viewOrigin, portalKey, out normal, out distance);
}
