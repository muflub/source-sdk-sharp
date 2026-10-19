using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Every engine call the managed SoundEmitterSystem's precache path makes
/// (plan_server_native_v2 T4a, step 2): the script table's lifecycle and
/// walk, the server's engine sound's precache, the filesystem's reads and
/// the precache log's writes, and the precache window native keeps. The
/// system decides; this only carries out, so a test can stand in for the
/// engine with a recorder.
/// </summary>
public interface ISoundPrecacheEngine
{
    /// <summary><c>soundemitterbase->ModInit()</c>.</summary>
    bool ModInit();

    /// <summary><c>soundemitterbase->ModShutdown()</c>.</summary>
    void ModShutdown();

    /// <summary><c>soundemitterbase->AddSoundOverrides( scriptfile )</c>.</summary>
    void AddSoundOverrides(string scriptFile);

    /// <summary><c>soundemitterbase->ClearSoundOverrides()</c>.</summary>
    void ClearSoundOverrides();

    /// <summary><c>soundemitterbase->Flush()</c>.</summary>
    void Flush();

    /// <summary><c>First()</c>, <c>Next( i )</c>, <c>InvalidIndex()</c>: the table's walk.</summary>
    int First();

    /// <inheritdoc cref="First"/>
    int Next(int index);

    /// <inheritdoc cref="First"/>
    int InvalidIndex();

    /// <summary><c>GetSoundIndex( name )</c>.</summary>
    int SoundIndex(string name);

    /// <summary><c>IsValidIndex( index )</c>.</summary>
    bool IsValidIndex(int index);

    /// <summary><c>GetSoundName( index )</c>.</summary>
    string? SoundName(int index);

    /// <summary><c>InternalGetParametersForSound( index )-&gt;ShouldPreload()</c>.</summary>
    bool ShouldPreload(int index);

    /// <summary>The entry's waves (each through <c>GetWaveName</c>), or null for no entry.</summary>
    IReadOnlyList<string>? Waves(int index);

    /// <summary><c>enginesound->PrecacheSound( sample, preload )</c>.</summary>
    bool PrecacheSound(string sample, bool preload);

    /// <summary><c>enginesound->IsSoundPrecached( sample )</c>.</summary>
    bool IsSoundPrecached(string sample);

    /// <summary><c>enginesound->PrefetchSound( sample )</c>.</summary>
    void PrefetchSound(string sample);

    /// <summary>Whether the precache window is open (<c>CBaseEntity::IsPrecacheAllowed()</c>).</summary>
    bool PrecacheAllowed();

    /// <summary><c>filesystem->FileExists( path, pathId )</c>.</summary>
    bool FileExists(string path, string pathId);

    /// <summary><c>filesystem->CreateDirHierarchy( path, pathId )</c>.</summary>
    void CreateDirHierarchy(string path, string pathId);

    /// <summary><c>filesystem->Open( path, options, pathId )</c>: 0 for none.</summary>
    nint Open(string path, string options, string pathId);

    /// <summary><c>filesystem->Write( bytes, size, handle )</c>.</summary>
    int Write(ReadOnlySpan<byte> bytes, nint handle);

    /// <summary><c>filesystem->Close( handle )</c>.</summary>
    void Close(nint handle);
}

/// <summary>
/// <see cref="ISoundPrecacheEngine"/> over the forwarder wrappers and the
/// level area: each member is one call.
/// </summary>
public sealed class SoundPrecacheEngine(IVtableSoundEmitter scripts, IVtableEngineSoundServer sound,
                                        IVtableBaseFileSystem files, IVtableFileSystem dirs, IEngineLevel level)
    : ISoundPrecacheEngine
{
    public bool ModInit() => scripts.ModInit();

    public void ModShutdown() => scripts.ModShutdown();

    public void AddSoundOverrides(string scriptFile) => scripts.AddSoundOverrides(scriptFile);

    public void ClearSoundOverrides() => scripts.ClearSoundOverrides();

    public void Flush() => scripts.Flush();

    public int First() => scripts.First();

    public int Next(int index) => scripts.Next(index);

    public int InvalidIndex() => scripts.InvalidIndex();

    public int SoundIndex(string name) => scripts.SoundIndex(name);

    public bool IsValidIndex(int index) => scripts.IsValidIndex(index);

    public string? SoundName(int index) => scripts.SoundName(index);

    public bool ShouldPreload(int index) => scripts.ShouldPreload(index);

    public IReadOnlyList<string>? Waves(int index) => scripts.Entry(index)?.Waves;

    public bool PrecacheSound(string sample, bool preload) => sound.PrecacheSound(sample, preload);

    public bool IsSoundPrecached(string sample) => sound.IsSoundPrecached(sample);

    public void PrefetchSound(string sample) => sound.PrefetchSound(sample);

    public bool PrecacheAllowed() => level.IsPrecacheAllowed();

    public bool FileExists(string path, string pathId) => files.FileExists(path, pathId);

    public void CreateDirHierarchy(string path, string pathId) => dirs.CreateDirHierarchy(path, pathId);

    public nint Open(string path, string options, string pathId) => files.Open(path, options, pathId);

    public int Write(ReadOnlySpan<byte> bytes, nint handle) => files.Write(bytes, handle);

    public void Close(nint handle) => files.Close(handle);
}
