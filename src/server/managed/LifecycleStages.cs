//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// Where a service sits in the LEVEL sequence — <see cref="IGameLifecycle"/>.
///
/// <para>
/// <b>Named, so the order can be read rather than reconstructed.</b> These used
/// to be bare integers on each participant, which meant working out the
/// sequence required opening seven files and sorting the numbers by hand. The
/// order is here, in one list, in the order it runs.
/// </para>
///
/// <para>
/// <b>Spaced by 100.</b> Room to insert between any two without renumbering
/// anything — and renumbering is the operation to avoid, because a participant
/// whose number moved still compiles, still runs, and simply runs somewhere
/// else.
/// </para>
///
/// <para>
/// <b>The interfaces still take an int.</b> A mod's own participant is not in
/// this enum and must still be able to place itself, including between two
/// stages that are named here — which a value spaced by 100 leaves room for and
/// an enum-typed field would forbid. So this names the positions the mod itself
/// uses without closing the set.
/// </para>
/// </summary>
public enum LifecycleStage
{
    /// <summary>
    /// The interface default: whatever a participant that never said.
    ///
    /// First, and deliberately so — a participant with no opinion running
    /// before the ones that do is the safe direction, because the ones that do
    /// have an opinion picked their position relative to each other.
    /// </summary>
    Unspecified = 0,

    // ---- plan_entity.md Phase 4 exit, lane p4-lifecycle ---------------------
    //
    // BEFORE Rules, AND THAT IS THE WHOLE POINT OF THE NUMBER rather than a
    // free slot at the front. The only two stages this participant declares are
    // the level-shutdown pair, and all they do is shut and reopen the window
    // that stops a level teardown being read as "every entity with a globalname
    // just died" (CBaseEntity::UpdateOnRemove, baseentity.cpp:2542).
    //
    // LifecycleDriver dispatches EVERY stage in ascending order -- the shutdown
    // passes are NOT walked backwards here, whatever kServerSystems[] did -- so
    // the window has to open at a number lower than every participant that
    // destroys an entity in the pre-entity shutdown pass. Standalone (1100) is
    // one; Rules, Pickups, Corpses, Players and Arsenal all clear state that
    // holds entity handles. 50 is below all of them and below the interface
    // default (Unspecified = 0) is not available, because a participant that
    // never said anything must stay first.

    /// <summary>
    /// The cross-level global state table's write window: shut while a level
    /// tears down, open again once it has.
    /// </summary>
    GlobalState = 50,

    /// <summary>
    /// The round, FIRST OF THE GAME'S OWN PARTICIPANTS. Stock's order rather
    /// than a preference: per-round state that other participants read while
    /// clearing their own has to be cleared before they look at it.
    ///
    /// <para>
    /// It read "FIRST" until <see cref="GlobalState"/> was inserted below it.
    /// Nothing about the reason moved — the global state table reads no
    /// per-round state and clears none; all it does before this point is shut a
    /// write window.
    /// </para>
    /// </summary>
    Rules = 100,

    // ---- tf2 stage 0.7 (the mode proxy, ruling U1) --------------------------

    /// <summary>
    /// The mode proxy's pick, BEFORE <see cref="Rules"/> and that is the whole
    /// number: the rules' level-init reset goes to the picked mode's round
    /// machine, so the mode has to be named before the rules ask it anything.
    /// Fifty between GlobalState and Rules is the spacing's own device — the
    /// enum's comment says renumbering a participant is the operation to
    /// avoid, and inserting is what the spacing is for.
    /// </summary>
    GameModes = 75,

    /// <summary>Map and runtime pickups.</summary>
    Pickups = 200,

    /// <summary>Corpses, which hold entity handles.</summary>
    Corpses = 300,

    /// <summary>The player roster and the death cycle.</summary>
    Players = 400,

    // ---- tf2 stage 0.7 (the per-player composite, rulings D9/D10) ----------

    /// <summary>
    /// The per-player composite's level-shutdown detach: beside
    /// <see cref="Players"/> and after it by the spacing's own midpoint —
    /// the composite holds per-player state, so it ends with the player
    /// roster, and nothing else at level scope reads components. Also the
    /// composite's <see cref="IClientLifecycle.ClientLifecycleOrder"/>:
    /// attach when the client is active, and the client participants in the
    /// core are one (Arsenal, 600), so the value only has to stay unique.
    /// </summary>
    PlayerComponents = 450,

    /// <summary>The last button mask seen from each player.</summary>
    PlayerInput = 500,

    /// <summary>Inventories, which hold handles that do not survive the map.</summary>
    Arsenal = 600,

    /// <summary>Soundscape identity tokens, which restart with the map.</summary>
    Soundscapes = 700,

    /// <summary>
    /// The area portal records (plan_server_native_v2 T4d), whose portal numbers
    /// are the map's: a new map's func_areaportals create their own.
    /// </summary>
    AreaPortals = 750,

    /// <summary>
    /// The level's default light styles (plan_server_native_v2 T4d), written at
    /// level init before any map entity spawns, as CWorld::Precache wrote them.
    /// </summary>
    DefaultLightStyles = 760,

    // ---- plan_gamedll_after.md Phase A3 -------------------------------------
    //
    // The two "C" game systems that became managed. Both are PRE-ENTITY
    // participants and neither of the seven above is, so in practice these two
    // are ordered only against each other — but the values are STOCK'S ORDER
    // rather than an arbitrary pair, because that is the one thing about them
    // that was a decision.
    //
    // In kServerSystems[] CPropData was row 9 and CDecalEmitterSystem row 11,
    // and no row between them declared a pre-entity stage
    // (CDataObjectAccessSystem, CDirtySpatialPartitionEntityList and
    // CSourceSharpStandaloneSystem all declare none). So propdata parses before
    // the decal script here for the same reason it did there, and the
    // pre-entity pass runs in the order it always ran in.

    /// <summary>propdata.txt: the breakable-chunk table and its precache.</summary>
    PropData = 800,

    /// <summary>The decal script: the name table, and the per-map precache.</summary>
    Decals = 900,

    // ---- plan_gamedll_after.md Phase A3, second pair -------------------------
    //
    // ONE VALUE FOR TWO SYSTEMS, and the missing one is the decision worth
    // recording. This pair moved CSoundControllerImp and CSoundEnt out of
    // kServerSystems[], and only the controller declares a LEVEL stage here:
    // the measured stage matrix (docs/gamedll-ledger.md, Phase 7) has
    // CSoundControllerImp overriding LevelShutdownPreEntity and CSoundEnt
    // overriding FrameUpdatePreEntityThink and NOTHING ELSE.
    //
    // CSoundEnt's init and teardown are reached BY NAME, from the world's spawn
    // and the level teardown, exactly as stock reached them -- soundent.cpp's
    // own comment says why it is not on the game-system hooks as well: "Moving
    // the work to the game system hooks as well would run it twice." Giving it
    // a level stage here to make the pair symmetric would be inventing the
    // second call that comment exists to prevent.

    /// <summary>Sound envelopes: the live patches and their queued ramps.</summary>
    SoundController = 1000,

    // ---- plan_gamedll_after.md Phase A3, the FIFTH and last "C" system ------
    //
    // AFTER SoundController, AND THAT IS STOCK'S ORDER RATHER THAN A GAP TO
    // FILL. The only two participants that declare a level-shutdown pre-entity
    // stage are these two, so the value only orders them against each other --
    // and in Phase 7's measured list CSoundControllerImp was row 18 and
    // CSourceSharpStandaloneSystem row 15. The pre-entity shutdown pass is
    // walked BACKWARDS, so 18 ran before 15, so the controller resets before
    // the hierarchy is torn down. Lower runs first here, so the hierarchy is
    // the higher number.

    /// <summary>
    /// The parallel hierarchy's teardown: every standalone entity deleted while
    /// the world it is in still exists.
    /// </summary>
    Standalone = 1100,

    /// <summary>
    /// The managed server skeletons (plan physics_bones_native_zero step 1.2):
    /// every entity's bone state and every model image copied this level go
    /// at level shutdown. AFTER Standalone, so the hierarchy's teardown --
    /// which can still ask a parent where an attachment is -- has finished
    /// asking before the skeletons it would ask go.
    /// </summary>
    Skeletons = 1150,

    // ---- plan_server_all.md Wave 1 / docs/plan-audit.md D11 ----------------
    //
    // The template store, which was CTemplatesHook, row 9 of kServerSystems[].
    //
    // THE NUMBER ONLY HAS TO BE A NUMBER, and that is measured rather than
    // shrugged at. The two stages this participant declares are
    // LevelInitPreEntity and LevelShutdownPostEntity; the only other declarer
    // of either is PropData's pre-entity half, which loads propdata.txt and
    // never touches a template. Nothing in the game reads the store between
    // those two points except the map parse itself, which is a LEVEL STEP and is
    // ordered against the other level steps by ServerGameDll rather than by this
    // enum. So it goes last, where a value nobody depends on is least likely to
    // be read as one somebody does.

    /// <summary>
    /// The template store: the keyvalue text of every claimed template, and the
    /// list of the ones still waiting to be precached.
    /// </summary>
    Templates = 1200,

    /// <summary>
    /// The viewer's world fog. plan_entity.md step 8e-render.
    ///
    /// <para>
    /// It is here only for the SHUTDOWN half: the fog a level's master
    /// controller published must be forgotten between maps, or the first poll
    /// of the next level compares the new controller against the old one's
    /// numbers and skips the publish whenever they happen to match — which
    /// leaves every player on the new map carrying the previous map's fog in
    /// their blob and nothing anywhere saying so.
    /// </para>
    /// </summary>
    WorldFog = 1300,

    /// <summary>
    /// The 3D skybox service (<c>WorldSkybox</c>) — the <c>defect-skybox</c>
    /// lane. Beside <see cref="WorldFog"/> and for the same half of the same
    /// reason: it has no level-init work either, and what it needs is the
    /// SHUTDOWN — a map's sky camera must be forgotten between levels, or the
    /// first poll of the next map compares a new camera against the old one's
    /// numbers and skips the write whenever they happen to match, leaving every
    /// player carrying the previous map's skybox with nothing saying so.
    /// </summary>
    WorldSkybox = 1400,

    /// <summary>
    /// PhysicsCallbacks (plan_physics_managed B2): managed's vphysics callback
    /// tables go on the level's environment at pre-entity init, and its record
    /// of the environment goes at shutdown. Its order against the others does
    /// not matter: nothing else touches the environment's handlers.
    /// </summary>
    PhysicsCallbacks = 1500,

    /// <summary>
    /// PhysicsEntitySolver (plan_physics_managed A9): a participant only for
    /// the save tier, so its live solvers cross a reload. It declares no level
    /// stage, so its order against the others does not matter.
    /// </summary>
    PhysicsSolvers = 1600,

    /// <summary>
    /// PlayerShadowRecovery (plan_physics_managed B7): a participant only for
    /// the save tier, so each player's last valid origin (stock's saved
    /// m_oldOrigin) crosses a reload. No level stage; its order does not matter.
    /// </summary>
    PlayerShadows = 1700,

    /// <summary>
    /// PhysicsSolver (plan_physics_bones_native_zero 2.1): managed's
    /// IPhysicsCollisionSolver answers. Its level state (the disabled pairs,
    /// the penetration ledger, the paused window) goes with the environment at
    /// shutdown and the paused window opens at pre-entity init; nothing else
    /// reads that state, so its order against the others does not matter.
    /// </summary>
    PhysicsSolver = 1800,

    /// <summary>
    /// PhysicsConstraints (plan_physics_bones_native_zero 2.4): the constraint
    /// and spring table drops its pointers with the environment at shutdown,
    /// and carries its rows across a reload. Nothing else reads the table, so
    /// its order against the others does not matter.
    /// </summary>
    PhysicsConstraints = 1900,

    /// <summary>
    /// PhysicsMotionControllers (plan_physics_bones_native_zero 2.4): the
    /// motion-controller table, for the same two reasons and with the same
    /// indifference to order.
    /// </summary>
    PhysicsMotionControllers = 2000,

    /// <summary>
    /// PhysWorldReport (plan_physics_bones_native_zero 2.3): the SS-PHYSWORLD
    /// report's automatic run at post-entity init, behind
    /// <c>sourcesharp_physworld_report</c>. It reads the environment and
    /// changes nothing, so its order against the others does not matter; it
    /// ran in CPhysicsHook::LevelInitPostEntity, before every managed
    /// participant, and nothing between the two writes what it reads.
    /// </summary>
    PhysWorldReport = 2100,

    /// <summary>
    /// PhysicsSoundBatch (plan_physics_bones_native_zero 2.3): the queued
    /// impact and break sounds go with the level (CPhysicsHook's
    /// LevelShutdownPostEntity emptied them). Nothing else reads the queues,
    /// so its order against the others does not matter.
    /// </summary>
    PhysicsSoundBatch = 2200,

    /// <summary>
    /// PhysicsFrame (plan_physics_bones_native_zero 2.3): the level is paused
    /// for physics between its pre- and post-entity init (CPhysicsHook's
    /// m_bPaused). Nothing else reads the flag and no frame runs between the
    /// two inits, so its order against the others does not matter.
    /// </summary>
    PhysicsFrame = 2300,
}

/// <summary>
/// Where a service sits in the FRAME sequence — <see cref="IFrameLifecycle"/>.
///
/// <para>
/// <b>A separate enum from <see cref="LifecycleStage"/>, on purpose.</b> A
/// service's position in the level sequence says nothing about where it belongs
/// in a frame, and the two real cases prove it: Soundscapes is LAST at level
/// scope (it only cares about shutdown) and FIRST in the frame. One shared enum
/// would let a frame position be written where a level position was meant, and
/// both are ints, so nothing would say so.
/// </para>
///
/// <para>
/// <b>The order below is the order two native hooks ran in.</b> The first two
/// were ServerModule.Frame's own list, reached from
/// CSourceSharpSystem::FrameUpdatePostEntityThink. The last three were
/// ServerModule.OnGameRulesThink, reached from CSourceSharpGameRules::Think --
/// a later hook, because the gamesystem is a file-static constructed at DLL load
/// while the rules object is constructed at level init, so the gamesystem
/// registered first. Merging the two lists had to preserve that, which is why
/// Soundscapes and Arsenal come before Rules here.
/// </para>
/// </summary>
public enum FrameStage
{
    /// <inheritdoc cref="LifecycleStage.Unspecified"/>
    Unspecified = 0,

    // ---- plan_gamedll_after.md Phase A3, the FIFTH and last "C" system ------
    //
    // BELOW EVERY OTHER FRAME STAGE, AND THE NUMBER IS THE WHOLE POSITION
    // ARGUMENT. CSourceSharpStandaloneSystem was row 11 of kServerSystems[] and
    // the SourceSharpManaged row is 12, with NOTHING between them -- so its
    // post-entity think ran immediately before every participant below, all of
    // which run from inside the managed row's own post-think body. Fifty puts
    // the hierarchy back at the front of that body, which is the same place.
    //
    // FIFTY RATHER THAN A HUNDRED-SPACED VALUE, and that is what the spacing is
    // for. Renumbering the five below to make room is the operation this enum's
    // own comment says to avoid: a participant whose number moved still
    // compiles, still runs, and simply runs somewhere else.
    //
    // It does NOT sit at Unspecified, which is also below a hundred. Unspecified
    // is where a participant that forgot to declare a stage lands, at most one
    // participant may leave it, and StageUniqueness refuses a second -- so
    // taking it would spend the one slot reserved for "no opinion" on a
    // participant whose position is the point.

    /// <summary>
    /// The parallel hierarchy's own frame: queued player commands, then touch
    /// links, movement, flight, due thinks, hierarchy composition and deferred
    /// deletes. Everything below observes what this produced.
    /// </summary>
    Standalone = 50,

    /// <summary>Which soundscape each player is hearing.</summary>
    Soundscapes = 100,

    /// <summary>Carried weapons ride their owners: PVS reads their origin.</summary>
    Arsenal = 200,

    /// <summary>The win check — where a mod decides the round is over.</summary>
    Rules = 300,

    // ---- tf2 stage 0.7 (the mode proxy's warmup) ----------------------------

    /// <summary>
    /// The mode proxy's warmup, BEFORE <see cref="Rules"/> and the number is
    /// the position argument: a switch's warmup ends by STARTING the round,
    /// and the win check of a round that has not started must not run — a
    /// frag collected while the players are still waiting must not end it.
    /// 250, between Arsenal and Rules, is the spacing's own device; the five
    /// recorded participants keep their numbers.
    /// </summary>
    GameModes = 250,

    /// <summary>The death cycle. It has to see a player the tick they die.</summary>
    Players = 400,

    /// <summary>
    /// The per-player composite's tick: after <see cref="Players"/> — a
    /// component reads the frame's death-cycle outcome, not the other way
    /// round — and before the pickup walk, which reads none of it. 450 is
    /// the spacing's own midpoint; the recorded participants keep their
    /// numbers.
    /// </summary>
    PlayerComponents = 450,

    /// <summary>Pickup respawns, and the first-frame placement of the map's own.</summary>
    Pickups = 500,

    // ---- plan_gamedll_after.md Phase A3, second pair -------------------------
    //
    // THESE TWO ARE NOT AT THE SAME POINT OF THE FRAME as the five above, which
    // is what makes their numbers nearly free. Every stage above runs at
    // OnThink; SoundEnt runs at OnPreThink and SoundController at
    // OnPreClientUpdate, and the driver sorts once and dispatches per point --
    // so a number here only orders a participant against others at the SAME
    // point, and at those two points each of these is currently alone.
    //
    // Numbered anyway, and in stock's order, because "currently alone" is a
    // property of today's build and a stage value is forever.

    /// <summary>The sound list's expiry sweep. Stock's FrameUpdatePreEntityThink.</summary>
    SoundEnt = 600,

    /// <summary>
    /// The queued physics impact and break sounds (PhysicsSoundBatch,
    /// plan_physics_bones_native_zero 2.3), at OnPreClientUpdate. Stock's
    /// CPhysicsHook::PreClientUpdate, which was the FIRST row of the native
    /// walk; the managed participants run after that walk, so this sits below
    /// every other OnPreClientUpdate participant to keep the physics sounds
    /// ahead of the sound controller.
    /// </summary>
    PhysicsSounds = 650,

    /// <summary>Envelope ramps and the command queue. Stock's PreClientUpdate.</summary>
    SoundController = 700,

    /// <summary>
    /// What fog each viewer is standing in, into each player's networked state.
    /// plan_entity.md step 8e-render.
    ///
    /// <para>
    /// AT OnPreClientUpdate BECAUSE THAT IS WHERE STOCK PUTS IT. Stock's
    /// <c>UpdateAllClientData</c> — "fills in CClientData values for local
    /// player just before sending over wire" — runs from
    /// <c>CServerGameClients::PreClientUpdate</c>, and copying per-viewer view
    /// state into the player is exactly what it does. Numbered after
    /// <see cref="SoundController"/> only because the two share a point and a
    /// stage value has to be unique; nothing about the fog depends on the
    /// envelopes.
    /// </para>
    /// </summary>
    WorldFog = 800,

    /// <summary>
    /// The 3D skybox service (<c>WorldSkybox</c>) — the <c>defect-skybox</c>
    /// lane. <b>At OnPreClientUpdate for a stronger reason than the fog's:</b>
    /// this is the stage stock's own <c>UpdateAllClientData</c> runs at, and
    /// the skybox is the thing stock's <c>ClientData_Update</c> is FOR
    /// (playerlocaldata.cpp's body is the sky camera and nothing else). So this
    /// is not "near enough", it is the same point in the frame doing the same
    /// job into a different carrier.
    ///
    /// <para>
    /// After <see cref="WorldFog"/> only because a stage value has to be
    /// unique. The two are independent: the skybox's fog is a separate volume
    /// from the world's, and a map may have either, both or neither.
    /// </para>
    /// </summary>
    WorldSkybox = 900,

    /// <summary>
    /// Each player's client-data update (<c>PlayerClientData</c>): the frame's
    /// Damage message. Stock's <c>CHL2_Player::UpdateClientData</c>, which
    /// sends what the frame's hits summed to. At OnPreClientUpdate, the point
    /// stock's own client-data walk runs at, after every hit of the frame has
    /// landed; after <see cref="WorldSkybox"/> only because a stage value has
    /// to be unique.
    /// </summary>
    PlayerClientData = 1000,
}

/// <summary>
/// Where a mod's participants order themselves (plan_host_sdk §2.2): after
/// every core stage, at <see cref="First"/> + n. Outside the stage enums on
/// purpose: a mod cannot add members to them, and their values keep a fixed
/// gap. <see cref="StageUniqueness"/> still refuses two participants at one
/// number, across mods too. The core never takes a value at or above this.
/// </summary>
public static class ModStages
{
    public const int First = 1_000_000;
}
