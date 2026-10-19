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
/// <b>Where the managed frame is, because it is not a member here.</b>
/// <see cref="ServerSystemsPreThink"/> and <see cref="ServerSystemsPostThink"/>
/// are the named list's two per-frame passes, and the managed frame is a
/// <i>row</i> of that list — so <c>FramePreThink</c> runs inside the first and
/// the managed frame inside the second, each with the same native neighbours
/// stock gives them. Part 4's sketch writes those two as inline C# calls around
/// the passes; measured, that would move the managed frame ahead of
/// <c>CSoundEnt</c>, <c>CRagdollLRURetirement</c> and <c>CEntityListSystem</c>,
/// none of which is a no-op. The third managed point,
/// <c>FramePostThink</c>, is inside <see cref="EndGameFrame"/>, which is where
/// stock reaches it from too.
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
    /// Stamps the rollback-independent server clock for this frame. Read by
    /// CBaseProjectile's collide-with-teammates think, which is why it is not
    /// just curtime at the point of use.
    /// </summary>
    void SetServerCurTime();

    /// <summary>
    /// The named list's pre-entity-think pass. The managed <c>FramePreThink</c>
    /// happens INSIDE it, because SourceSharpManaged is a row of that table.
    /// </summary>
    void ServerSystemsPreThink();

    /// <summary>
    /// Every entity's think for this frame. Between the two system passes,
    /// which is the whole reason they are named Pre- and PostEntityThink.
    /// </summary>
    void PhysicsRunThinkFunctions(bool simulating);

    /// <summary>
    /// The named list's post-entity-think pass. The managed frame — every
    /// <c>IFrameLifecycle.OnThink</c> and the dev API's pump — happens INSIDE
    /// it, at the position CSourceSharpSystem held.
    /// </summary>
    void ServerSystemsPostThink();

    /// <summary>
    /// Fires every entity I/O whose time has come. After the think pass, so an
    /// output written this frame is delivered this frame.
    /// </summary>
    void ServiceEventQueue();

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

    /// <summary>
    /// Fires the timed network-property events. Stock's comment: "Any entities
    /// that detect network state changes on a timer do it here."
    /// </summary>
    void FireNetworkPropertyEvents();

    /// <summary>Services the stats uploader's connection.</summary>
    void GamestatsUpdateConnection();

    /// <summary>
    /// Draws every queued debug overlay. What makes an NDebugOverlay call from
    /// managed code visible at all. Not behind <c>_DEBUG</c>: stock's own
    /// comment there is "allow this in release for now".
    /// </summary>
    void DrawAllDebugOverlays();

    /// <summary>The named list's PreClientUpdate pass.</summary>
    void ServerSystemsPreClientUpdate();

    /// <summary>
    /// The parallel hierarchy's queued player commands — plan_gamedll_after.md
    /// Phase A3, the fifth "C" system. Its own step so that C# owns the order
    /// against <see cref="StandaloneUpdate"/>.
    /// </summary>
    void StandalonePlayerCommands();

    /// <summary>
    /// The parallel hierarchy's touch links, movement and flight — everything a
    /// think should see the CURRENT frame's answer to.
    ///
    /// <para>
    /// The first half of what was one <c>StandaloneRunFrame</c> step. Step 1g
    /// took the thinks out of it: they are managed now
    /// (<see cref="EntityDirectory.RunFrame"/>) and they run BETWEEN this and
    /// <see cref="StandaloneFinish"/>. That is the whole reason it is two
    /// steps — the order around the thinks is load-bearing in both directions,
    /// and <c>shell_standalone_base.cpp</c> argues each half.
    /// </para>
    /// </summary>
    void StandaloneUpdate();

    /// <summary>
    /// The parallel hierarchy's attachment composition and deferred deletes —
    /// everything that has to see what the thinks did. The second half; see
    /// <see cref="StandaloneUpdate"/>.
    /// </summary>
    void StandaloneFinish();

    /// <summary>
    /// THE SDK HIERARCHY'S HALF OF ONE ENTITY I/O DELIVERY — the sub-ioqueue
    /// lane. Answers how many SDK entities took the input.
    ///
    /// <para>
    /// NOT a frame step. The managed I/O queue asks it for ONE event, and only
    /// when the managed directory answered nothing at all — so nothing can be
    /// delivered twice, and an idle frame never calls it. It exists because the
    /// compiled stock TUs still construct <c>CEntityDissolve</c>,
    /// <c>CRagdollProp</c>, <c>CRagdollBoogie</c> and
    /// <c>CPhysicsEntitySolver</c> at runtime, those are on the other
    /// hierarchy, and <c>CEventQueue::ServiceEvents</c> used to be the one
    /// thing that reached them.
    /// </para>
    ///
    /// <para>
    /// The activator does not cross: <c>AcceptInput</c> takes a
    /// <c>CBaseEntity *</c> and a managed activator has none to be, which is
    /// the degrade <c>activator.h</c> already describes and which the native
    /// queue already made.
    /// </para>
    /// </summary>
    int DeliverToSdkEntities(string target, string input, string? value);
}
