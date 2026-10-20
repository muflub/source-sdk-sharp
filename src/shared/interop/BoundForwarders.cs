// The engine interfaces a composition registers (EngineServices.AddTo): each
// member is one delegation to the side's bound slot, and the compiler holds
// every interface member implemented.

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IFwdSoundEmitter"/> over the side's bound ISoundEmitterSystemBase: each call asks
/// <see cref="EngineBound.SoundEmitter"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundSoundEmitter(EngineSide side) : IFwdSoundEmitter
{
    public int SoundIndex(string name)
        => EngineBound.SoundEmitter(side).SoundIndex(name);

    public string? SoundName(int index)
        => EngineBound.SoundEmitter(side).SoundName(index);

    public SoundScriptParams? ScriptParameters(string name)
        => EngineBound.SoundEmitter(side).ScriptParameters(name);

    public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender)
        => EngineBound.SoundEmitter(side).ParametersForSound(name, ref parameters, gender);

    public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters, int gender, bool beingEmitted)
        => EngineBound.SoundEmitter(side).ParametersForSoundEx(name, ref handle, ref parameters, gender, beingEmitted);

    public SoundScriptEntry? Entry(int index)
        => EngineBound.SoundEmitter(side).Entry(index);

    public string? WavFileForSound(string name, int gender)
        => EngineBound.SoundEmitter(side).WavFileForSound(name, gender);

    public int LookupSoundLevel(string name)
        => EngineBound.SoundEmitter(side).LookupSoundLevel(name);

    public int ActorGender(string? model)
        => EngineBound.SoundEmitter(side).ActorGender(model);

    public bool ModInit()
        => EngineBound.SoundEmitter(side).ModInit();

    public void ModShutdown()
        => EngineBound.SoundEmitter(side).ModShutdown();

    public void AddSoundOverrides(string scriptFile, bool preload = false)
        => EngineBound.SoundEmitter(side).AddSoundOverrides(scriptFile, preload);

    public void ClearSoundOverrides()
        => EngineBound.SoundEmitter(side).ClearSoundOverrides();

    public void Flush()
        => EngineBound.SoundEmitter(side).Flush();

    public int First()
        => EngineBound.SoundEmitter(side).First();

    public int Next(int index)
        => EngineBound.SoundEmitter(side).Next(index);

    public int InvalidIndex()
        => EngineBound.SoundEmitter(side).InvalidIndex();

    public bool IsValidIndex(int index)
        => EngineBound.SoundEmitter(side).IsValidIndex(index);

    public bool ShouldPreload(int index)
        => EngineBound.SoundEmitter(side).ShouldPreload(index);
}

/// <summary>
/// <see cref="IFwdPhysicsSurfaceProps"/> over the side's bound IPhysicsSurfaceProps: each call asks
/// <see cref="EngineBound.SurfaceProps"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundSurfaceProps(EngineSide side) : IFwdPhysicsSurfaceProps, IVPhysicsSurfaceSounds
{
    // The same table's sounds (PhysicsSurfaceProps answers both).
    public SurfaceImpactSounds? Sounds(int surfaceProps)
        => ((IVPhysicsSurfaceSounds)EngineBound.SurfaceProps(side)).Sounds(surfaceProps);

    public int ParseSurfaceData(string fileName, string text)
        => EngineBound.SurfaceProps(side).ParseSurfaceData(fileName, text);

    public int SurfacePropCount()
        => EngineBound.SurfaceProps(side).SurfacePropCount();

    public string? PropName(int index)
        => EngineBound.SurfaceProps(side).PropName(index);

    public int SurfaceIndex(string name)
        => EngineBound.SurfaceProps(side).SurfaceIndex(name);

    public int GameMaterial(int surfaceProps)
        => EngineBound.SurfaceProps(side).GameMaterial(surfaceProps);

    public MoveSurface Surface(int surfaceProps)
        => EngineBound.SurfaceProps(side).Surface(surfaceProps);

    public string? ScrapeSound(int surfaceProps, bool smooth)
        => EngineBound.SurfaceProps(side).ScrapeSound(surfaceProps, smooth);
}

/// <summary>
/// <see cref="IFwdBaseFileSystem"/> over the side's bound IBaseFileSystem: each call asks
/// <see cref="EngineBound.Files"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundBaseFileSystem(EngineSide side) : IFwdBaseFileSystem
{
    public byte[]? ReadAll(string fileName, string pathId)
        => EngineBound.Files(side).ReadAll(fileName, pathId);

    public uint FileSize(string fileName, string pathId)
        => EngineBound.Files(side).FileSize(fileName, pathId);

    public long FileTime(string fileName, string pathId)
        => EngineBound.Files(side).FileTime(fileName, pathId);

    public bool FileExists(string fileName, string pathId)
        => EngineBound.Files(side).FileExists(fileName, pathId);

    public nint Open(string fileName, string options, string pathId)
        => EngineBound.Files(side).Open(fileName, options, pathId);

    public int Write(ReadOnlySpan<byte> bytes, nint handle)
        => EngineBound.Files(side).Write(bytes, handle);

    public void Close(nint handle)
        => EngineBound.Files(side).Close(handle);
}

/// <summary>
/// <see cref="IFwdModelInfo"/> over the side's bound IVModelInfo: each call asks
/// <see cref="EngineBound.ModelInfo"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundModelInfo(EngineSide side) : IFwdModelInfo
{
    public nint Model(int modelIndex)
        => EngineBound.ModelInfo(side).Model(modelIndex);

    public int ModelIndex(string name)
        => EngineBound.ModelInfo(side).ModelIndex(name);

    public string? ModelName(nint model)
        => EngineBound.ModelInfo(side).ModelName(model);

    public ModelType ModelTypeOf(nint model)
        => EngineBound.ModelInfo(side).ModelTypeOf(model);

    public int FrameCountAt(int modelIndex)
        => EngineBound.ModelInfo(side).FrameCountAt(modelIndex);

    public ModelType TypeAt(int modelIndex)
        => EngineBound.ModelInfo(side).TypeAt(modelIndex);

    public string? KeyValueText(nint model)
        => EngineBound.ModelInfo(side).KeyValueText(model);

    public string? CollisionKeyValueText(int modelIndex)
        => EngineBound.ModelInfo(side).CollisionKeyValueText(modelIndex);

    public byte[]? StudioImage(int modelIndex, string? includeName)
        => EngineBound.ModelInfo(side).StudioImage(modelIndex, includeName);

    public byte[]? StudioAnimBlock(int modelIndex, string? includeName, int block)
        => EngineBound.ModelInfo(side).StudioAnimBlock(modelIndex, includeName, block);
}

/// <summary>
/// <see cref="IFwdDebugOverlay"/> over the side's bound IVDebugOverlay: each call asks
/// <see cref="EngineBound.DebugOverlay"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundDebugOverlay(EngineSide side) : IFwdDebugOverlay
{
    public void TextAtPosition(Vector origin, int line, float duration, int r, int g, int b, int a, string text)
        => EngineBound.DebugOverlay(side).TextAtPosition(origin, line, duration, r, g, b, a, text);
}

/// <summary>
/// <see cref="IFwdEngineServer"/> over the side's bound IVEngineServer: each call asks
/// <see cref="EngineBound.Server"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed unsafe partial class BoundEngineServer(EngineSide side) : IFwdEngineServer, ISaveMemory
{
    // The save allocator is the same IVEngineServer (EngineServer answers both).
    public void* SaveAlloc(nuint count, nuint size)
        => ((ISaveMemory)EngineBound.Server(side)).SaveAlloc(count, size);

    public void SaveFree(void* block)
        => ((ISaveMemory)EngineBound.Server(side)).SaveFree(block);

    public string? GameDir()
        => EngineBound.Server(side).GameDir();

    public string? MapEntitiesString()
        => EngineBound.Server(side).MapEntitiesString();

    public bool IsModelPrecached(string path)
        => EngineBound.Server(side).IsModelPrecached(path);

    public bool IsDedicatedServer()
        => EngineBound.Server(side).IsDedicatedServer();

    public int EntityCount()
        => EngineBound.Server(side).EntityCount();

    public float Time()
        => EngineBound.Server(side).Time();

    public int PlayerUserId(int entIndex)
        => EngineBound.Server(side).PlayerUserId(entIndex);

    public void ServerCommandNow(string command)
        => EngineBound.Server(side).ServerCommandNow(command);

    public void SetView(int playerIndex, int viewIndex)
        => EngineBound.Server(side).SetView(playerIndex, viewIndex);

    public void LightStyle(int style, string pattern)
        => EngineBound.Server(side).LightStyle(style, pattern);

    public bool BoxInPvs(Vector eye, Vector mins, Vector maxs)
        => EngineBound.Server(side).BoxInPvs(eye, mins, maxs);

    public int GetArea(Vector origin)
        => EngineBound.Server(side).GetArea(origin);

    public ulong ClientSteamId(int entIndex)
        => EngineBound.Server(side).ClientSteamId(entIndex);

    public string? ClientAddress(int entIndex)
        => EngineBound.Server(side).ClientAddress(entIndex);

    public float? ClientAvgLatency(int entIndex)
        => EngineBound.Server(side).ClientAvgLatency(entIndex);

    public string? ClientConVarValue(int entIndex, string name)
        => EngineBound.Server(side).ClientConVarValue(entIndex, name);

    public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel, int flags, int pitch, float soundTime)
        => EngineBound.Server(side).EmitAmbientSound(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime);

    public int SentenceIndexFromName(string name)
        => EngineBound.Server(side).SentenceIndexFromName(name);

    public int PrecacheDecal(string name, bool preload)
        => EngineBound.Server(side).PrecacheDecal(name, preload);

    public bool IsDecalPrecached(string name)
        => EngineBound.Server(side).IsDecalPrecached(name);

    public void StaticDecal(Vector originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowPriority)
        => EngineBound.Server(side).StaticDecal(originInEntitySpace, decalIndex, entityIndex, modelIndex, lowPriority);

    public void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits)
        => EngineBound.Server(side).MulticastRecipients(usePas, origin, playerBits);

    public void PlaybackTempEntity(nint filter, float delay, ReadOnlySpan<byte> sender, nint sendTable, int classId)
        => EngineBound.Server(side).PlaybackTempEntity(filter, delay, sender, sendTable, classId);
}
