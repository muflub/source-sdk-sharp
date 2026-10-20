namespace SourceSharp.Client.ClientSystems;

/// <summary>
/// The client-system stages a kClientSystems[] row is walked through
/// (shell_clientsystem.h's CClientSystem, the per-frame half aside). The
/// native adapter numbers them the same way (SS_CLIENT_SYSTEM_STAGE_*,
/// shell_managed_clientsystems_client.cpp); ManagedClientSystemsAbiTests holds
/// the two lists equal.
/// </summary>
public enum ClientSystemStage
{
    Init = 0,
    PostInit = 1,
    Shutdown = 2,
    LevelInitPreEntity = 3,
    LevelInitPostEntity = 4,
    LevelShutdownPreEntity = 5,
    LevelShutdownPostEntity = 6,
}

/// <summary>
/// A client system whose row in kClientSystems[] is managed's: native's
/// adapter keeps the row's place in the walk and hands every stage here.
/// The defaults are CClientSystem's: Init succeeds, every other stage does
/// nothing.
/// </summary>
public abstract class ManagedClientSystem
{
    /// <summary>The row's name in kClientSystems[], which the adapter asks by.</summary>
    public abstract string Name { get; }

    /// <summary>False refuses the client's start-up, as a native row's Init does.</summary>
    public virtual bool Init() => true;

    public virtual void PostInit() { }

    public virtual void Shutdown() { }

    public virtual void LevelInitPreEntity() { }

    public virtual void LevelInitPostEntity() { }

    public virtual void LevelShutdownPreEntity() { }

    public virtual void LevelShutdownPostEntity() { }
}

/// <summary>
/// The managed rows (IManagedClientSystemsClient::Stage's answer): each stage a
/// native adapter is walked through reaches the system registered under the
/// row's name.
/// </summary>
public sealed class ManagedClientSystems : SourceSharp.Client.Interop.IClientSystemRows
{
    private readonly Dictionary<string, ManagedClientSystem> _systems = new(StringComparer.Ordinal);

    public void Register(ManagedClientSystem system) => _systems.Add(system.Name, system);

    public IReadOnlyCollection<ManagedClientSystem> Systems => _systems.Values;

    /// <summary>
    /// One stage of the row named <paramref name="name"/>: Init's answer (1 or
    /// 0), 1 for any other stage, -1 when no managed system has the name or the
    /// stage is not one.
    /// </summary>
    public int Stage(string name, int stage)
    {
        if (!_systems.TryGetValue(name, out ManagedClientSystem? system))
            return -1;
        switch ((ClientSystemStage)stage)
        {
            case ClientSystemStage.Init: return system.Init() ? 1 : 0;
            case ClientSystemStage.PostInit: system.PostInit(); return 1;
            case ClientSystemStage.Shutdown: system.Shutdown(); return 1;
            case ClientSystemStage.LevelInitPreEntity: system.LevelInitPreEntity(); return 1;
            case ClientSystemStage.LevelInitPostEntity: system.LevelInitPostEntity(); return 1;
            case ClientSystemStage.LevelShutdownPreEntity: system.LevelShutdownPreEntity(); return 1;
            case ClientSystemStage.LevelShutdownPostEntity: system.LevelShutdownPostEntity(); return 1;
            default: return -1;
        }
    }
}
