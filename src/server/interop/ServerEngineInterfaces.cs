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
    /// <summary><c>VEngineRandom001</c>, or null with the engine's reason.</summary>
    IUniformRandom? RandomStream(out string why);

    /// <summary><c>VModelInfoServer004</c>, or null with the engine's reason.</summary>
    IFwdModelInfo? ModelInfo(out string why);

    /// <summary><c>VBaseFileSystem011</c>, or null with the engine's reason.</summary>
    IFwdBaseFileSystem? BaseFileSystem(out string why);

    /// <summary><c>IVEngineServer</c>, or null with the engine's reason.</summary>
    IFwdEngineServer? EngineServer(out string why);

    /// <summary><see cref="ShimProbes.EngineClient"/>: expected to refuse on the server.</summary>
    string EngineClientProbe();

    /// <summary><see cref="ShimProbes.Frame"/>.</summary>
    string FrameProbe();

    /// <summary><see cref="ShimProbes.Level"/>.</summary>
    string LevelProbe();
}

/// <summary>The real <see cref="IServerEngineInterfaces"/>, over the composition's own engine.</summary>
internal sealed class ServerEngineInterfaces(ModuleEngine engine) : IServerEngineInterfaces
{
    private readonly ModuleEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public IUniformRandom? RandomStream(out string why) => UniformRandomStream.Acquire(_engine, out why);

    public IFwdModelInfo? ModelInfo(out string why) => SourceSharp.Abi.EngineModelInfo.Acquire(_engine, out why);

    public IFwdBaseFileSystem? BaseFileSystem(out string why) => EngineBaseFileSystem.Acquire(_engine, out why);

    public IFwdEngineServer? EngineServer(out string why) => SourceSharp.Abi.EngineServer.Acquire(_engine, out why);

    public string EngineClientProbe() => ShimProbes.EngineClient(_engine);

    public string FrameProbe() => ShimProbes.Frame(_engine);

    public string LevelProbe() => ShimProbes.Level(_engine);
}
