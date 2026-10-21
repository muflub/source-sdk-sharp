namespace SourceSharp.Abi;

/// <summary>
/// The engine forwarders a side with no engine behind it answers with, for a
/// fact (TestSides): an empty world, no surface properties, and for every
/// other engine interface one that answers nothing. The forwarder counterpart
/// of the default areas (ShimAreaDefaults.g.cs), under the same ruling:
/// managed logic never asks whether an engine is attached or holds a null
/// interface, so a test without one is given interfaces that answer good
/// defaults.
///
/// <para>
/// Held on the side's bound slots, where they answer while the side has no
/// shim root; a fact that attaches a fake root asks the root from then on.
/// A fact that composes a consumer directly hands it one of these.
/// </para>
/// </summary>
internal static class DefaultForwarders
{
    /// <summary>Holds the defaults on <paramref name="side"/>'s bound slots, for as long as it has no shim root.</summary>
    public static void HoldOn(EngineSide side)
    {
        side.Bound.Random.Hold(new LowestRandom());
        side.Bound.Trace.Hold(new EmptyWorldTrace());
        side.Bound.SurfaceProps.Hold(new NoSurfaceProps());
        side.Bound.SoundEmitter.Hold(new NoSoundEmitter());
        side.Bound.Files.Hold(new NoFiles());
        side.Bound.ModelInfo.Hold(new NoModelInfo());
        side.Bound.DebugOverlay.Hold(new NoOverlay());
#if TEST_SUPPORT_SERVER
        side.Bound.Server.Hold(new NoEngineServer());
        side.Bound.SoundServer.Hold(new NoSoundServer());
        side.Bound.FileSystem.Hold(new NoFileSystem());
#else
        side.Bound.Client.Hold(new NoEngineClient());
        side.Bound.InputSystem.Hold(new NoInputSystem());
        side.Bound.SoundClient.Hold(new NoSoundClient());
#endif
    }

    /// <summary>A world with nothing in it: every point empty, every ray unblocked.</summary>
    internal sealed class EmptyWorldTrace : IFwdEngineTrace
    {
        public int PointContents(Vector point) => 0;

        public EngineGameTrace TraceRay(in EngineRay ray, uint mask, nint filter)
            => new() { StartPos = ray.Start, EndPos = ray.Start + ray.Delta, Fraction = 1f };
    }

    /// <summary>A stream that always draws the low end of the range: deterministic, and in range.</summary>
    internal sealed class LowestRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;

        public int RandomInt(int min, int max) => min;

        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    /// <summary>No surface table: no names, no indices, the default surface's movement answers.</summary>
    internal sealed class NoSurfaceProps : IFwdPhysicsSurfaceProps, IVPhysicsSurfaceSounds
    {
        public SurfaceImpactSounds? Sounds(int surfaceProps) => null;

        public int ParseSurfaceData(string fileName, string text) => 0;

        public int SurfacePropCount() => 0;

        public string? PropName(int index) => null;

        // VPhysics answers its default surface for a name it lacks.
        public int SurfaceIndex(string name) => 0;

        public int GameMaterial(int surfaceProps) => 0;

        public MoveSurface Surface(int surfaceProps) => default;

        public string? ScrapeSound(int surfaceProps, bool smooth) => null;
    }

    /// <summary>No sound scripts answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoSoundEmitter : IFwdSoundEmitter
    {
        public int SoundIndex(string name) => -1;

        public string? SoundName(int index) => null;

        public SoundScriptParams? ScriptParameters(string name) => null;

        public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender) => false;

        public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters, int gender, bool beingEmitted) => false;

        public SoundScriptEntry? Entry(int index) => null;

        public string? WavFileForSound(string name, int gender) => null;

        public int LookupSoundLevel(string name) => default;

        public int ActorGender(string? model) => default;

        public bool ModInit() => false;

        public void ModShutdown() { }

        public void AddSoundOverrides(string scriptFile, bool preload = false) { }

        public void ClearSoundOverrides() { }

        public void Flush() { }

        public int First() => -1;

        public int Next(int index) => -1;

        public int InvalidIndex() => -1;

        public bool IsValidIndex(int index) => false;

        public bool ShouldPreload(int index) => false;
    }

    /// <summary>No files answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoFiles : IFwdBaseFileSystem
    {
        public byte[]? ReadAll(string fileName, string pathId) => null;

        public uint FileSize(string fileName, string pathId) => default;

        public long FileTime(string fileName, string pathId) => default;

        public bool FileExists(string fileName, string pathId) => false;

        public nint Open(string fileName, string options, string pathId) => default;

        public int Write(ReadOnlySpan<byte> bytes, nint handle) => default;

        public void Close(nint handle) { }
    }

    /// <summary>No models answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoModelInfo : IFwdModelInfo
    {
        public nint Model(int modelIndex) => default;

        public int ModelIndex(string name) => -1;

        public string? ModelName(nint model) => null;

        public ModelType ModelTypeOf(nint model) => default;

        public int FrameCountAt(int modelIndex) => default;

        public ModelType TypeAt(int modelIndex) => default;

        public string? KeyValueText(nint model) => null;

        public string? CollisionKeyValueText(int modelIndex) => null;

        public byte[]? StudioImage(int modelIndex, string? includeName) => null;

        public byte[]? StudioAnimBlock(int modelIndex, string? includeName, int block) => null;
    }

    /// <summary>No overlay answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoOverlay : IFwdDebugOverlay
    {
        public void TextAtPosition(Vector origin, int line, float duration, int r, int g, int b, int a, string text) { }
    }

#if TEST_SUPPORT_SERVER
    /// <summary>An engine server with no level answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed unsafe class NoEngineServer : IFwdEngineServer, ISaveMemory, SourceSharp.Server.Interop.IAreaPortalEngine
    {
        // No allocator: a save is refused, said by the save session.
        public void* SaveAlloc(nuint count, nuint size) => null;

        public void SaveFree(void* block) { }

        // No portals: every one left open, as with no plane.
        public void SetAreaPortalState(int portalNumber, bool open) { }

        public void SetAreaPortalStates(ReadOnlySpan<int> portalNumbers, ReadOnlySpan<int> isOpen) { }

        public bool GetAreaPortalPlane(Vector viewOrigin, int portalKey, out Vector normal, out float distance)
        {
            (normal, distance) = (default, 0f);
            return false;
        }

        public string? GameDir() => null;

        public string? MapEntitiesString() => null;

        public bool IsModelPrecached(string path) => false;

        public bool IsDedicatedServer() => false;

        public int EntityCount() => default;

        public float Time() => default;

        public int PlayerUserId(int entIndex) => -1;

        public void ServerCommandNow(string command) { }

        public void SetView(int playerIndex, int viewIndex) { }

        public void LightStyle(int style, string pattern) { }

        public bool BoxInPvs(Vector eye, Vector mins, Vector maxs) => false;

        public int GetArea(Vector origin) => default;

        public ulong ClientSteamId(int entIndex) => default;

        public string? ClientAddress(int entIndex) => null;

        public float? ClientAvgLatency(int entIndex) => null;

        public string? ClientConVarValue(int entIndex, string name) => null;

        public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel, int flags, int pitch, float soundTime) { }

        public int SentenceIndexFromName(string name) => -1;

        public int PrecacheDecal(string name, bool preload) => -1;

        public bool IsDecalPrecached(string name) => false;

        public void StaticDecal(Vector originInEntitySpace, int decalIndex, int entityIndex, int modelIndex, bool lowPriority) { }

        public void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits) { }

        public void PlaybackTempEntity(nint filter, float delay, ReadOnlySpan<byte> sender, nint sendTable, int classId) { }
    }

    /// <summary>No server sound answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoSoundServer : IFwdEngineSoundServer
    {
        public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity) { }

        public void StopSound(int entIndex, int channel, string sample) { }

        public float SoundDuration(string sample) => default;

        public bool PrecacheSound(string sample, bool preload) => false;

        public bool IsSoundPrecached(string sample) => false;

        public void PrefetchSound(string sample) { }
    }

    /// <summary>No file system answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoFileSystem : IFwdFileSystem
    {
        public void CreateDirHierarchy(string path, string pathId) { }
    }
#else
    /// <summary>An engine client in no game answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed unsafe class NoEngineClient : IFwdEngineClient, ISteamLaunchCommands, ISaveMemory, Client.Input.IClientInputEngine
    {
        // No allocator: a save is refused, said by the save session.
        public void* SaveAlloc(nuint count, nuint size) => null;

        public void SaveFree(void* block) { }

        // The input host's engine: level angles, no screen, never paused, no
        // mouse, commands go nowhere. ConsolePrintAt is the IFwd one.
        Client.Input.InputAngles Client.Input.IClientInputEngine.GetViewAngles() => default;

        void Client.Input.IClientInputEngine.SetViewAngles(Client.Input.InputAngles angles) { }

        void Client.Input.IClientInputEngine.GetScreenSize(out int width, out int height) => (width, height) = (0, 0);

        bool Client.Input.IClientInputEngine.IsPaused => false;

        void Client.Input.IClientInputEngine.GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta) => (x, y) = (0, 0);

        void Client.Input.IClientInputEngine.ClientCmdUnrestricted(string command) { }

        public string? LevelName() => null;

        public string? GameDirectory() => null;

        public uint ProtocolVersion() => default;

        public void Reconnect() { }

        public void RequestManagedReload() { }

        public void ConsolePrintAt(int position, string text) { }

        public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text) { }

        public bool TryLightForPoint(in Vector position, bool clamp, out Vector light)
        {
            light = default; return false;
        }

        public bool IsInGame() => false;

        public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light)
        {
            light = default; return false;
        }

        public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info)
        {
            info = default; return false;
        }

        public void JoinTeam(int team) { }

        public void ConnectFromSteamLaunch(string connectLine) { }

        public void SubmitConsoleLine(string line) { }

        public void SpectatorCommand(SpectatorKeyCommand command) { }

        public void SpectatorServerCommand(string line) { }

        public void VoiceServerCommand(string line, bool reliable) { }

        public int SentenceIndexFromName(string name) => -1;

        public int LocalPlayerSlot() => default;
    }

    /// <summary>No input system answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoInputSystem : IFwdInputSystem
    {
        public string? ButtonName(int buttonCode) => null;
    }

    /// <summary>No client sound answering nothing: no names, no indices (-1 where 0 is a real one), no effects.</summary>
    internal sealed class NoSoundClient : IFwdEngineSoundClient
    {
        public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity) { }

        public void EmitSoundAttenuation(nint filter, int entIndex, int channel, string sample, float volume, float attenuation, int flags, int pitch, int specialDsp, Vector? origin, Vector? direction) { }

        public void StopSound(int entIndex, int channel, string sample) { }

        public float SoundDuration(string sample) => default;

        public void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime) { }
    }
#endif
}
