namespace SourceSharp.Abi;

/// <summary>
/// A service that wants to run every frame.
///
/// <para>
/// The counterpart to <see cref="IGameLifecycle"/>, and found the same way: the
/// container is asked who implements this, not a list somebody has to remember
/// to add to.
/// </para>
///
/// <para>
/// <b>Three points, because the frame has three and they are not
/// interchangeable.</b> The server's frame is, in order (gameinterface.cpp
/// CServerGameDLL::GameFrame):
/// </para>
/// <list type="number">
///   <item><description>
///     <c>IGameSystem::FrameUpdatePreEntityThinkAllSystems()</c> — nothing has
///     moved yet this frame. This is <see cref="OnPreThink"/>, and it is where
///     you set up what the entity thinks are about to read.
///   </description></item>
///   <item><description>
///     <c>Physics_RunThinkFunctions()</c> — every entity thinks.
///   </description></item>
///   <item><description>
///     <c>IGameSystem::FrameUpdatePostEntityThinkAllSystems()</c> — the world
///     has moved and settled. This is <see cref="OnThink"/>, and it is where
///     the stock rules class ran its own Think: it is the point at which a mod
///     decides the round is over, because the frame's movement has happened
///     and the round-ending condition can actually be true.
///   </description></item>
///   <item><description>
///     the event queue, the delete list, and the client data update.
///   </description></item>
///   <item><description>
///     <c>g_pGameRules-&gt;EndGameFrame()</c> — the last thing the game DLL
///     does. This is <see cref="OnPostThink"/>. It runs after the entity I/O
///     queue has fired, which is what makes it different from
///     <see cref="OnThink"/> rather than a second copy of it: an input
///     delivered this frame has been acted on by the time you get here.
///   </description></item>
/// </list>
///
/// <para>
/// <b>Every method is defaulted</b>, so a participant implements only the point
/// it needs and the other two cost it nothing.
/// </para>
///
/// <para>
/// <b>These run every frame</b>, which is the one thing that makes this
/// interface different to write against. Work here is per-tick work: it is
/// measured in ticks per second, not in map loads. A participant that only
/// needs to act when something changes should be driven by that change.
/// </para>
/// </summary>
public interface IFrameLifecycle
{
    /// <summary>
    /// Lower runs first. Ties keep container registration order.
    ///
    /// <para>
    /// Separate from <see cref="IGameLifecycle.LifecycleOrder"/> on purpose: a
    /// service's position in the level sequence says nothing about where it
    /// belongs in a frame, and a class implementing both would otherwise have
    /// one number doing two unrelated jobs.
    /// </para>
    /// </summary>
    int FrameOrder => (int)FrameStage.Unspecified;

    /// <summary>Before any entity has thought this frame.</summary>
    void OnPreThink(float frameTime) { }

    /// <summary>After every entity has thought this frame.</summary>
    void OnThink(float frameTime) { }

    /// <summary>
    /// The end of the game DLL's frame, after the entity I/O queue has fired.
    /// </summary>
    void OnPostThink(float frameTime) { }

    /// <summary>
    /// The last point of the server frame, after the entities have thought and
    /// before the engine builds each client's update. Stock's
    /// <c>IGameSystemPerFrame::PreClientUpdate</c> (<c>igamesystem.h:124</c>).
    ///
    /// <para>
    /// <b>Nothing in this build implements it yet, and that is the point.</b>
    /// It is the frame twin of <see cref="IGameLifecycle.OnPostInit"/>: a stage
    /// the shell calls and nobody implements costs one empty pass, whereas a
    /// stage that does not exist costs an interface change on the day something
    /// needs it. See plan_gamedll.md Part 3a.
    /// </para>
    ///
    /// <para>
    /// <b>Distinct from <see cref="OnPostThink"/>.</b> PostThink is the end of
    /// the game DLL's own work; this runs after it and is the last moment
    /// anything the client will see this frame can still be changed.
    /// </para>
    /// </summary>
    void OnPreClientUpdate(float frameTime) { }
}
