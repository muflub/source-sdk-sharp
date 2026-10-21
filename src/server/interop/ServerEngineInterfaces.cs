namespace SourceSharp.Abi;

/// <summary>
/// The engine interfaces the server's <c>cs_vtable</c> asks for, each through
/// this composition's own interface factory (plan_abi_hotreload D8), and the
/// shim layer's probe lines (<see cref="ShimProbes"/>).
///
/// <para>
/// The command lives in <c>SourceSharp.Server</c> and takes this rather than
/// the engine (plan_shim_areas_split phase 8), so the logic never names
/// <c>ModuleEngine</c>; only the server's root builds the real one. It is
/// declared here and not in <c>SourceSharp.Server.Interface</c> because the
/// interfaces it hands back (<see cref="IUniformRandom"/>,
/// <see cref="IFwdModelInfo"/>, ...) live in the interop beside their
/// bindings.
/// </para>
/// </summary>
public interface IServerEngineInterfaces
{
    /// <summary><c>VEngineRandom001</c>: bound and what it answered, or REFUSED with the engine's reason.</summary>
    string RandomStreamProbe();

    /// <summary><c>VModelInfoServer004</c>: bound and what the world model answered, or REFUSED with the reason.</summary>
    string ModelInfoProbe();

    /// <summary><c>VBaseFileSystem011</c>: bound and the exists pair, or REFUSED with the reason.</summary>
    string BaseFileSystemProbe();

    /// <summary><c>IVEngineServer</c>: bound and its reads, or REFUSED with the reason.</summary>
    string EngineServerProbe();

    /// <summary><see cref="ShimProbes.EngineClient"/>: expected to refuse on the server.</summary>
    string EngineClientProbe();

    /// <summary><see cref="ShimProbes.Frame"/>.</summary>
    string FrameProbe();

    /// <summary><see cref="ShimProbes.Level"/>.</summary>
    string LevelProbe();
}

/// <summary>
/// The real <see cref="IServerEngineInterfaces"/>, over the composition's own
/// engine. Each probe ASKS the side's factory itself, every time, rather than
/// calling the registered interface: a probe's answer is whether the engine
/// serves the interface, and a refusal is a line it prints, never a throw.
/// </summary>
internal sealed class ServerEngineInterfaces(ModuleEngine engine) : IServerEngineInterfaces
{
    private readonly ModuleEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    private NativeInterfaceFactory Factory => _engine.Api.Interfaces;

    public string RandomStreamProbe()
        => Line("IUniformRandomStream", UniformRandomStream.Acquire(Factory, out string why), why,
                stream => $"RandomInt(7,7) = {stream.RandomInt(7, 7)}");

    // MODEL INDEX 1 IS THE WORLD, on every BSP -- so the index answers
    // itself, and the two by-index members are asked about it beside the
    // lookup. A brush model is mod_brush(1), which is the .bsp's own structure
    // and not something the engine chose, and a world model has exactly one frame.
    public string ModelInfoProbe()
        => Line("IVModelInfo", SourceSharp.Abi.EngineModelInfo.Acquire(Factory, out string why, SourceSharp.Abi.EngineModelInfo.ServerInterfaceVersion), why,
                info => $"ModelIndex(\"maps/dm_lockdown.bsp\") = {info.ModelIndex("maps/dm_lockdown.bsp")}, "
                        + $"TypeAt(1) = {info.TypeAt(1)}, "
                        + $"FrameCountAt(1) = {info.FrameCountAt(1)}, "
                        + $"FrameCountAt(0) = {info.FrameCountAt(0)}");

    // AND THE PAIR THAT TELLS AN EMPTY FILE FROM A MISSING ONE. Size answers 0
    // for both; exists is the question that separates them.
    public string BaseFileSystemProbe()
        => Line("IBaseFileSystem", EngineBaseFileSystem.Acquire(Factory, out string why), why,
                files => $"FileSize(\"gameinfo.txt\", \"MOD\") = {files.FileSize("gameinfo.txt", "MOD")}, "
                         + $"FileExists(\"gameinfo.txt\", \"GAME\") = {files.FileExists("gameinfo.txt", "GAME")}, "
                         + $"FileExists(\"nothing_is_here.txt\", \"GAME\") = {files.FileExists("nothing_is_here.txt", "GAME")}");

    // EntityCount is the EDICT count, held against the live gate's own census,
    // which counts managed entities: not the same population, and the probe
    // line printing both is what says so. IsDedicatedServer is false on the
    // listen server every gate here runs.
    public string EngineServerProbe()
        => Line("IVEngineServer", SourceSharp.Abi.EngineServer.Acquire(Factory, out string why), why,
                server => $"GameDir() = {server.GameDir()}, "
                          + $"IsDedicatedServer() = {server.IsDedicatedServer()}, "
                          + $"EntityCount() = {server.EntityCount()}, "
                          + $"GetArea(0,0,64) = {server.GetArea(new Vector(0f, 0f, 64f))}");

    public string EngineClientProbe() => ShimProbes.EngineClient(_engine);

    public string FrameProbe() => ShimProbes.Frame(_engine);

    public string LevelProbe() => ShimProbes.Level(_engine);

    // A refusal that is never printed is indistinguishable from a call nobody made.
    private static string Line<T>(string name, T? bound, string why, Func<T, string> call) where T : class
        => bound is null ? $"{name}: REFUSED — {why}" : $"{name}: bound, {call(bound)}";
}
