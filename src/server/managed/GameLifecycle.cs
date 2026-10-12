namespace SourceSharp.Abi;

/// <summary>
/// One participant in the server's lifecycle.
///
/// <para>
/// <b>What this replaces.</b> <c>ServerModule.LevelInit</c> was a hand-written
/// list — reset the round, reset the pickups, reset the corpses, reset the
/// players, reset the buttons, reset the arsenal — and every one of those lines
/// is a service remembering to be remembered. A new service that needs clearing
/// on a map change joins that list or it does not, and nothing says which.
/// That is the same "did anyone remember to add it" failure the
/// <c>[Precache]</c> attribute was introduced to end, ten lines further down
/// the same method.
/// </para>
///
/// <para>
/// <b>So participation is declared, not wired.</b> A service implements this
/// and the container finds it: <c>ResolveAll&lt;IGameLifecycle&gt;()</c> returns
/// every registered service whose implementation implements it, and the driver
/// calls each in turn. Nothing has to be added to a list in another file.
/// </para>
///
/// <para>
/// <b>Every method has a default.</b> A participant implements only the events
/// it cares about, which matters because most care about exactly one — a
/// registry that clears on map change has nothing to say at shutdown. Empty
/// overrides written to satisfy an interface are noise, and noise is where a
/// missing implementation hides.
/// </para>
///
/// <para>
/// <b>THE SAVE STAGES EXIST NOW, AND THAT REVERSES A RECORDED DECISION.</b>
/// This comment used to read: "Three of stock's stages are deliberately
/// absent. <c>OnSave</c> and <c>OnRestore</c> belong to the save cluster, which
/// this port refuses (plan_gamedll.md Part 7) — adding the two callbacks would
/// put a stage on this interface whose only possible caller is code that will
/// not be written."
/// </para>
///
/// <para>
/// The premise is stale. The save cluster is LIVE: <c>SaveCollectEntities</c>
/// and <c>SaveRestoreEntities</c> are bound in
/// <c>SourceSharp.Bootstrap/Entry.cs</c>, <c>ServerModule.SaveCollect</c> and
/// <c>SaveRestore</c> implement them, and the live gate's <c>save-load</c> step
/// exercises the round trip. The caller exists. So <c>OnSave</c> and
/// <c>OnRestore</c> are written, on <see cref="ISaveCustom"/> — a participant
/// that has state to carry implements that interface and
/// <c>LifecycleDriver</c> finds it, which keeps this interface's own rule that
/// a participant implements only the events it cares about. The reversal is
/// recorded here rather than made quietly, because prose in this tree is
/// load-bearing and a comment asserting the opposite of the code is a failure
/// this project has already had.
/// </para>
///
/// <para>
/// <c>SafeRemoveIfDesired</c> IS still deliberately absent: it is a stock idiom
/// for a game system that wants to unregister itself mid-frame; nothing here
/// registers itself, so there is nothing for it to remove. Absent on purpose,
/// and recorded in <c>docs/gamedll-ledger.md</c> so the next reader finds a
/// decision rather than an omission.
/// </para>
/// </summary>
public interface IGameLifecycle
{
    /// <summary>
    /// Where this participant sits in the sequence. Lower runs first; ties keep
    /// registration order.
    ///
    /// <para>
    /// Zero for almost everything. It exists because a few orderings are real
    /// rather than incidental — state that another participant reads while
    /// clearing its own has to be cleared first — and the alternative to a
    /// number is a comment in one file about a line in another.
    /// </para>
    ///
    /// <para>
    /// A participant that does NOT set this is saying its order does not
    /// matter. If that turns out to be false the fix is a number here, not a
    /// re-shuffle of the registration lines in the module.
    /// </para>
    /// </summary>
    int LifecycleOrder => (int)LifecycleStage.Unspecified;

    /// <summary>
    /// The server is up and every system has had its <c>Init</c>. Stock's
    /// <c>IGameSystem::PostInit</c> (<c>igamesystem.h:35</c>).
    ///
    /// <para>
    /// <b>Nothing in this build implements it yet, and that is the point.</b>
    /// A stage the shell calls and nobody implements costs one empty pass; a
    /// stage that does not exist costs a change to the interface, the driver,
    /// the shell and every participant on the day something needs it. It is
    /// here because this is the cheap moment — see plan_gamedll.md Part 3a.
    /// </para>
    /// </summary>
    void OnPostInit() { }

    /// <summary>
    /// A map is loading and <b>the map's entities do not exist yet</b>. Stock's
    /// <c>LevelInitPreEntity</c> (<c>igamesystem.h:38</c>).
    ///
    /// <para>
    /// <b>This half is load-bearing and is why the split exists.</b> The things
    /// an entity reads while it spawns — the physics environment, the sound
    /// emitter's precache — have to be standing before the first
    /// <c>Spawn()</c> runs. A participant that does that work in the PostEntity
    /// half is a world whose entities spawned with no <c>physenv</c>, and the
    /// symptom shows up as physics that silently does nothing rather than as an
    /// error here.
    /// </para>
    /// </summary>
    /// <param name="mapName">The map, bare — no <c>maps/</c>, no <c>.bsp</c>.</param>
    void OnLevelInitPreEntity(string mapName) { }

    /// <summary>
    /// A map has loaded and its entities have been created. Stock's
    /// <c>LevelInitPostEntity</c> (<c>igamesystem.h:40</c>), and the half the
    /// activate walk belongs to.
    ///
    /// <para>
    /// <b>Defaults to <see cref="OnLevelInit"/>, which is what makes the split
    /// additive.</b> Every participant written before the split declared
    /// <c>OnLevelInit</c> and ran at exactly one moment; that moment is this
    /// one, so they keep running at it without being edited. A participant that
    /// overrides this instead is saying the same thing in the newer spelling.
    /// </para>
    /// </summary>
    /// <param name="mapName">The map, bare — no <c>maps/</c>, no <c>.bsp</c>.</param>
    void OnLevelInitPostEntity(string mapName) => OnLevelInit(mapName);

    /// <summary>
    /// A map has loaded. The map's entities DO exist by now.
    ///
    /// <para>
    /// <b>The PostEntity half under its original name.</b> Kept because it is
    /// what every participant in this build already implements, and renaming
    /// them all would move seven services in a change whose entire claim is
    /// that nothing moves. New participants should say which half they mean.
    /// </para>
    /// </summary>
    /// <param name="mapName">The map, bare — no <c>maps/</c>, no <c>.bsp</c>.</param>
    void OnLevelInit(string mapName) { }

    /// <summary>
    /// The level is up and clients can connect: called once per level, on the
    /// first game frame after <c>ServerActivate</c>, when the server socket is
    /// bound. What a hosting service is told is "map ready" (plan_host_sdk §2.8).
    /// </summary>
    void OnLevelReady(string mapName) { }

    /// <summary>
    /// The map is going away and its entities are still alive. Stock's
    /// <c>LevelShutdownPreEntity</c> (<c>igamesystem.h:43</c>).
    ///
    /// <para>
    /// Anything that wants to READ an entity on the way out belongs here — by
    /// the PostEntity half they have been deleted and a handle to one answers
    /// nothing.
    /// </para>
    /// </summary>
    void OnLevelShutdownPreEntity() { }

    /// <summary>
    /// The map has gone and its entities with it. Stock's
    /// <c>LevelShutdownPostEntity</c> (<c>igamesystem.h:45</c>).
    ///
    /// <para>
    /// Defaults to <see cref="OnLevelShutdown"/>, for
    /// <see cref="OnLevelInitPostEntity"/>'s reason.
    /// </para>
    /// </summary>
    void OnLevelShutdownPostEntity() => OnLevelShutdown();

    /// <summary>
    /// The map is going away. Handles do not survive it, so anything holding
    /// one has to drop it here or it will be holding a handle that may since
    /// have been reused by something else entirely.
    ///
    /// <para>The PostEntity half under its original name — see
    /// <see cref="OnLevelInit"/>.</para>
    /// </summary>
    void OnLevelShutdown() { }

    /// <summary>
    /// The server is shutting down for good. Distinct from
    /// <see cref="OnLevelShutdown"/>: that one runs on every map change.
    /// </summary>
    void OnShutdown() { }
}
