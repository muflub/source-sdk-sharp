//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// What the managed side does at each game-system stage: the
/// <c>SourceSharpManaged</c> row of what was <c>kServerSystems[]</c>, plus the
/// physics row's frame, which is managed's own (plan_physics_bones_native_zero
/// 2.3). <see cref="ServerSystems"/> says when each runs; this says what.
/// </summary>
public interface IServerSystemRow
{
    /// <summary>The managed row's Init: the sound emitter system's script table.
    /// False stops the server, as the native row's false did.</summary>
    bool Init();

    /// <summary>The level's pre-entity half: the sound system's level start, then
    /// every pre-entity participant.</summary>
    void LevelInitPreEntity(string mapName);

    /// <summary>The managed level init: every <c>IGameLifecycle.OnLevelInit</c>.</summary>
    void LevelInitPostEntity(string mapName);

    /// <summary>The pre-entity half of the level's end.</summary>
    void LevelShutdownPreEntity();

    /// <summary>The managed level shutdown, and the side's level caches with it.</summary>
    void LevelShutdownPostEntity();

    /// <summary>The managed frame's pre-entity-think point.</summary>
    void FramePreThink(float frameTime);

    /// <summary>
    /// The entities' frame: the parallel hierarchy's movers, touches, thinks
    /// and deletes, before the physics step as stock's GameFrame runs
    /// Physics_RunThinkFunctions before FrameUpdatePostEntityThink.
    /// </summary>
    void EntitiesFrame(float frameTime);

    /// <summary>The managed frame: every <c>IFrameLifecycle.OnThink</c> after the entities'.</summary>
    void Frame(float frameTime);

    /// <summary>
    /// The CPhysicsHook row's per-frame stage: the physics step, which managed
    /// decides and makes (<c>PhysicsFrame</c>).
    /// </summary>
    void PhysicsFrame();
}

/// <summary>
/// <b><c>kServerSystems[]</c>, in C#</b> (plan_server_native_v2 T4e): the order
/// the server's game systems run their stages in, which was a C++ table walked
/// forwards for init and backwards for shutdown (shell_serversystems.cpp) after
/// stock's <c>IGameSystem</c> walks (igamesystem.cpp) and the hand-ordered
/// <c>IGameSystem::Add</c>s in <c>CServerGameDLL::DLLInit</c>
/// (gameinterface.cpp).
///
/// <para>
/// <b>Three rows, and two of stock's comments are what order them.</b> The
/// rows are <c>CGameStringPool</c>, <c>CPhysicsHook</c> and the managed row.
/// "The string system must init first + shutdown last": it is first in, and its
/// one stage, the post-entity shutdown's <c>FreeAll</c>, is the last call of
/// that pass. "Physics must occur before the sound envelope manager":
/// <c>CPhysicsHook::Init</c> resolves the vphysics interfaces before any managed
/// system (the sound envelope manager among them) starts, and its pre-entity
/// stage builds the environment before any managed participant's.
/// </para>
///
/// <para>
/// <b>Shutdown is the table backwards</b>, which is the property the native
/// walks had: the managed row tears down first, then physics, then the string
/// pool. <see cref="LifecycleDriver"/> runs its own participants in ascending
/// stage order for every pass, so the native rows are not lifecycle
/// participants: they are lines here, around the managed row.
/// </para>
///
/// <para>
/// <b>Each native stage is one <see cref="IEngineLevel"/> step</b> and native
/// takes the model-cache lock inside it; the managed row's stages take it
/// through <see cref="IEngineFrame.BeginModelCacheLock"/>, because stock's walks
/// took it per system and every managed stage ran inside it while the walk was
/// native. Stages no row declares — <c>PostInit</c>, <c>Shutdown</c>,
/// <c>LevelShutdownPreClearSteamAPIContext</c>, <c>FrameUpdatePreEntityThink</c>
/// on the native rows and <c>PreClientUpdate</c> on every row — have no line.
/// </para>
/// </summary>
public sealed class ServerSystems(IEngineLevel level, IEngineFrame frame, IServerSystemRow managed, ILog log)
{
    /// <summary>
    /// DLLInit's <c>InitAllSystems</c>, stopping at the first refusal:
    /// <c>CGameStringPool</c> (<c>CServerSystem::Init</c>, nothing to call),
    /// <c>CPhysicsHook</c>, then the managed row.
    /// </summary>
    public bool Init()
    {
        if (!level.PhysicsInit())
        {
            log.Warning("[SourceSharp] game system 'CPhysicsHook' refused to init; the server "
                      + "cannot come up without it\n");
            return false;
        }

        frame.BeginModelCacheLock();
        try
        {
            if (managed.Init())
                return true;
        }
        finally
        {
            frame.EndModelCacheLock();
        }

        log.Warning("[SourceSharp] game system 'SourceSharpManaged' refused to init; the server "
                  + "cannot come up without it\n");
        return false;
    }

    /// <summary>
    /// The pre-entity pass, from inside the world entity's own precache:
    /// physics builds the level's environment, then the managed row.
    /// </summary>
    public void LevelInitPreEntity(string mapName)
    {
        level.PhysicsLevelInitPreEntity();

        frame.BeginModelCacheLock();
        try { managed.LevelInitPreEntity(mapName); }
        finally { frame.EndModelCacheLock(); }
    }

    /// <summary>The post-entity pass at activation: physics unpauses, then the managed level init.</summary>
    public void LevelInitPostEntity(string mapName)
    {
        level.PhysicsLevelInitPostEntity();

        frame.BeginModelCacheLock();
        try { managed.LevelInitPostEntity(mapName); }
        finally { frame.EndModelCacheLock(); }
    }

    /// <summary>The pre-entity shutdown pass, backwards: the managed row, then physics.</summary>
    public void LevelShutdownPreEntity()
    {
        frame.BeginModelCacheLock();
        try { managed.LevelShutdownPreEntity(); }
        finally { frame.EndModelCacheLock(); }

        level.PhysicsLevelShutdownPreEntity();
    }

    /// <summary>
    /// The post-entity shutdown pass, backwards: the managed row and its slot
    /// table's reset, then physics destroys the environment, then the string
    /// pool frees every pooled string — last, as stock's comment requires.
    /// </summary>
    public void LevelShutdownPostEntity()
    {
        frame.BeginModelCacheLock();
        try
        {
            managed.LevelShutdownPostEntity();

            // The row's native half, after the managed shutdown rather than
            // before it: a state object is entitled to write during its
            // entity's teardown.
            level.ResetEntitySlots();
        }
        finally
        {
            frame.EndModelCacheLock();
        }

        level.PhysicsLevelShutdownPostEntity();
        level.StringPoolLevelShutdownPostEntity();
    }

    /// <summary>
    /// The pre-entity-think pass. Neither native row declares the stage, so
    /// the managed row is the whole pass.
    /// </summary>
    public void FramePreThink(float frameTime)
    {
        frame.BeginModelCacheLock();
        try { managed.FramePreThink(frameTime); }
        finally { frame.EndModelCacheLock(); }
    }

    /// <summary>
    /// The post-entity-think pass: the physics row's step, then the managed
    /// frame, then the entity slots drained -- after every managed write of
    /// the frame and before the snapshot.
    /// </summary>
    public void FramePostThink(float frameTime)
    {
        frame.BeginModelCacheLock();
        try
        {
            managed.EntitiesFrame(frameTime);
            managed.PhysicsFrame();
            managed.Frame(frameTime);
            frame.DrainEntitySlots();
        }
        finally
        {
            frame.EndModelCacheLock();
        }
    }
}

/// <summary>
/// The managed row, over the module that owns its stage bodies
/// (plan_server_native_v2 T4e). Each stage was a managed-built slot native's
/// walk called, and each slot's generated thunk caught what it threw; a stage
/// here catches and reports the same way (<see cref="ManagedVtable.Fail"/>), so
/// a fault in one stage still leaves the rest of the pass, and the frame, to
/// run.
/// </summary>
public sealed class ManagedSystemRow(ILog log, ISideModule module, Func<bool> soundInit,
                                     Action<string> preEntity, Action shutdownPreEntity, Action physicsFrame,
                                     LivenessMirror liveness, ModelDescriptors models, BreakModels breaks,
                                     Action<float>? entitiesFrame = null)
    : IServerSystemRow
{
    public bool Init()
    {
        try { return soundInit(); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.Init", e); return false; }
    }

    public void LevelInitPreEntity(string mapName)
    {
        try { preEntity(mapName); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.LevelInitPreEntity", e); }
    }

    public void LevelInitPostEntity(string mapName)
    {
        try
        {
            log.Msg("[SourceSharp] > managed ServerLevelInit\n");
            module.LevelInit(mapName);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.LevelInitPostEntity", e); }
    }

    public void LevelShutdownPreEntity()
    {
        try { shutdownPreEntity(); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.LevelShutdownPreEntity", e); }
    }

    /// <summary>
    /// The level's end, and the three tables keyed by this level's entities and
    /// models with it: this side's own.
    /// </summary>
    public void LevelShutdownPostEntity()
    {
        try
        {
            log.Msg("[SourceSharp] > managed ServerLevelShutdown\n");
            module.LevelShutdown();
            liveness.Clear();
            models.Clear();
            breaks.Clear();
        }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.LevelShutdownPostEntity", e); }
    }

    public void FramePreThink(float frameTime)
    {
        try { module.FramePreThink(frameTime); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.FramePreThink", e); }
    }

    public void Frame(float frameTime)
    {
        try { module.Frame(frameTime); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.Frame", e); }
    }

    public void EntitiesFrame(float frameTime)
    {
        try { entitiesFrame?.Invoke(frameTime); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.EntitiesFrame", e); }
    }

    public void PhysicsFrame()
    {
        try { physicsFrame(); }
        catch (Exception e) { ManagedVtable.Fail(log, "ServerSystems.PhysicsFrame", e); }
    }
}
