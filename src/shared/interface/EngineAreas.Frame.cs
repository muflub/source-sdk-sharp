namespace SourceSharp.Abi;

/// <summary>
/// The irreducible native steps a server frame is made of.
///
/// <para>
/// <b>An area of steps, not of capabilities</b> — the same kind of area as
/// <see cref="IEngineLevel"/> and for the same reason. plan_gamedll.md Part 4's
/// rule is that native exposes one <c>extern "C"</c> per irreducible step and
/// C# owns the order, the conditions and the early returns, and <i>a step is
/// never a decision</i>. Every member below is one line of
/// <c>CServerGameDLL::GameFrame</c> or <c>::PreClientUpdate</c> and decides
/// nothing.
/// </para>
///
/// <para>
/// <b>There is deliberately no <c>GameFrame()</c> member.</b> The frame's order
/// is the single most-read thing in a game DLL and it runs sixty-six times a
/// second; one member that ran all fifteen would put it back in C++.
/// </para>
///
/// <para>
/// <b>Where the managed frame is, because it is not a member here.</b> The named
/// list's two per-frame passes are managed's own since plan_server_native_v2
/// T4e (<c>ServerSystems</c>): the physics frame and the managed frame run in
/// the post-entity-think pass in the order kServerSystems[] gave them, each
/// inside <see cref="BeginModelCacheLock"/>, with <see cref="DrainEntitySlots"/>
/// after. The third managed point, <c>FramePostThink</c>, is inside
/// <see cref="EndGameFrame"/>, which is where stock reaches it from too.
/// </para>
///
/// <para>
/// <b>Server only in effect.</b> The native slots behind these are filled on the
/// server and left null on the client, which has no <c>GameFrame</c> to have
/// steps of. The interface is registered on both sides like every other area.
/// </para>
/// </summary>
public interface IEngineFrame
{
    /// <summary>
    /// Whether this server simulates on every other tick. A QUESTION, not a
    /// decision: the branch it feeds is in C#, so the alternate-tick arm reads
    /// as the four lines stock writes rather than as a native early return.
    /// </summary>
    bool IsSimulatingOnAlternateTicks();

    /// <summary>
    /// The simulation tick counter — <c>gpGlobals-&gt;tickcount</c>, loaded
    /// through <c>Globals</c> when the engine's struct is bound.
    ///
    /// <para>
    /// It said "a step rather than a <c>Globals</c> read, because
    /// <c>IEngineEnvironment</c> is registered from its parameterless
    /// constructor" until plan_crossings.md Phase X1, which is the phase that
    /// made that sentence false: the pointer crosses now and
    /// <c>Globals.IsAvailable</c> is true in a live session. The slot is kept
    /// for the fallback and as the divergence reference.
    /// </para>
    /// </summary>
    int TickCount();

    /// <summary>
    /// The engine's frame time, read — a load through <c>Globals</c> where the
    /// setter beside it stays a crossing.
    ///
    /// <para>
    /// The asymmetry is the point rather than an oversight. Reading
    /// <c>gpGlobals-&gt;frametime</c> is the relocation of a load; WRITING it is
    /// a mutation of the engine's own struct in the middle of its frame, and
    /// that goes through the shim, which is the single place a reader can find
    /// every writer of it.
    /// </para>
    /// </summary>
    float GetFrameTime();

    /// <summary>
    /// The engine's frame time, written. A setter rather than a
    /// <c>ScaleFrameTime</c>, so that the <c>* 2.0f</c> stock applies for an
    /// alternate tick is visible in C# beside the condition that asks for it.
    /// </summary>
    void SetFrameTime(float frameTime);

    /// <summary>
    /// Refreshes every connected client's per-frame data. Stock's comment here
    /// is "FIXME: Should this only occur on the final tick?", carried across
    /// unanswered — answering an open question inside a transliteration would
    /// be a change disguised as a port.
    /// </summary>
    void UpdateAllClientData();

    /// <summary>
    /// The rules' end of frame. <b>The third managed frame point is inside
    /// this</b> — <c>CSourceSharpGameRules::EndGameFrame</c> calls
    /// <c>FramePostThink</c> and then drains the entity slots — which is where
    /// stock reaches it from too.
    ///
    /// <para>
    /// The <c>g_pGameRules</c> null test stays native. It is not a policy: it
    /// is the fact that the rules object does not exist between levels, and a
    /// C# caller would need a second crossing to ask and would get a stale
    /// answer.
    /// </para>
    /// </summary>
    void EndGameFrame();

    /// <summary>Services the stats uploader's connection.</summary>
    void GamestatsUpdateConnection();

    /// <summary>
    /// Draws every queued debug overlay. What makes an NDebugOverlay call from
    /// managed code visible at all. Not behind <c>_DEBUG</c>: stock's own
    /// comment there is "allow this in release for now".
    /// </summary>
    void DrawAllDebugOverlays();

    /// <summary>
    /// The parallel hierarchy's queued player commands — plan_gamedll_after.md
    /// Phase A3, the fifth "C" system. Its own step so that C# owns the order
    /// against <see cref="StandaloneUpdate"/>.
    /// </summary>
    void StandalonePlayerCommands();

    /// <summary>
    /// The parallel hierarchy's attachment composition and deferred deletes —
    /// everything that has to see what the thinks did. The second half; see
    /// <see cref="StandaloneUpdate"/>.
    /// </summary>
    void StandaloneFinish();

    /// <summary>
    /// One <c>NetworkStateChanged</c> per entity whose managed state was written
    /// this frame: the managed row's native half after the managed frame, when
    /// kServerSystems[] walked it (plan_server_native_v2 T4e).
    /// </summary>
    void DrainEntitySlots();

    /// <summary>
    /// <c>mdlcache-&gt;BeginLock()</c>: the <c>MDLCACHE_CRITICAL_SECTION</c> each
    /// system's stage ran inside when the walks were native (stock's
    /// igamesystem.cpp takes it per system). Paired with
    /// <see cref="EndModelCacheLock"/> around a managed stage.
    /// </summary>
    void BeginModelCacheLock();

    /// <summary><c>mdlcache-&gt;EndLock()</c>; see <see cref="BeginModelCacheLock"/>.</summary>
    void EndModelCacheLock();
}
