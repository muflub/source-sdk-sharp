namespace SourceSharp.Abi;

/// <summary>
/// Server-only hooks that server_base.vpc leaves for the mod to decide.
/// Implemented alongside <see cref="ISideModule"/> by the server module.
///
/// These exist because a Source mod must answer them in C++; SourceSharp
/// answers them in C# and keeps only a thin shell native-side.
/// </summary>
public interface IServerModule
{
    /// <summary>Name shown in the server browser.</summary>
    string GetGameDescription();

    void OnClientPutInServer(int entIndex, string playerName);
    void OnClientActive(int entIndex, bool loadGame);

    /// <summary>A client's SourceSharp net message; true when a participant claimed it.</summary>
    bool OnClientNetMessage(int entIndex, string key, ReadOnlySpan<byte> payload);

    /// <summary>
    /// ClientSettingsChanged: the client's userinfo changed. True when the
    /// player takes the name in its <c>name</c> convar (the native SetPlayerName).
    /// </summary>
    bool OnClientSettingsChanged(int entIndex);

    // --- gameplay decisions ---

    /// <summary>A player finished spawning and is in a valid state.</summary>
    void OnPlayerSpawn(EntityHandle player);

    /// <summary>
    /// Which spawn point a player should enter the map on, or
    /// <see cref="EntityHandle.None"/> to leave it to the engine.
    ///
    /// Asked from inside the engine's own spawn sequence, before the player is
    /// made solid — see OnSelectSpawnPoint in sourcesharp.h for why that
    /// matters and what happened when it was done afterwards.
    /// </summary>
    EntityHandle SelectSpawnPoint(EntityHandle player);

    /// <summary>
    /// The ground under a player changed to a different kind of stuff.
    ///
    /// <para>
    /// Pushed from the movement code and only on CHANGE, so this is a crossing
    /// per footstep onto different ground rather than one per tick.
    /// <paramref name="gameMaterial"/> is a surfaceprop game material — one
    /// character per broad class — and zero means the player is not on the
    /// ground at all.
    /// </para>
    /// </summary>
    void OnPlayerSurfaceChanged(EntityHandle player, int gameMaterial);

    void OnPlayerKilled(EntityHandle player, EntityHandle attacker);

    /// <summary>
    /// Decides whether a connecting client is admitted. Return null to allow, or
    /// a rejection reason to refuse. The shell allows by default if this throws,
    /// so a fault here can never lock every player out.
    /// </summary>
    string? OnClientConnect(string name, string address);

    /// <summary>
    /// First refusal on a client command. Anything not reported as handled falls
    /// through to the stock handler.
    /// </summary>
    CommandResult OnClientCommand(EntityHandle player, string command);

    // OnNetEntitySpawned went with the deleted shell, and its ABI slot was
    // REAPED at 240 — see host/sourcesharp.h.

    /// <summary>
    /// Whether an attack may hurt this player at all. Friendly fire and spawn
    /// protection are both this question. Deferring uses the stock rule.
    /// </summary>
    Decision CanTakeDamage(EntityHandle player, EntityHandle attacker, float damage, int damageType);

    /// <summary>The player finished spawning. Loadout is handed out here.</summary>
    void OnPlayerSpawned(EntityHandle player);

    /// <summary>A pickup was touched. <paramref name="other"/> walked into it.</summary>
    void OnEntityTouched(EntityHandle entity, EntityHandle other);

    /// <summary>
    /// Runs per player per tick with the buttons held that tick. Where an
    /// attack is decided.
    /// </summary>
    void OnPlayerRunCommand(EntityHandle player, int buttons);

    /// <summary>
    /// EVERY THING THAT HAPPENS TO AN ENTITY, THROUGH ONE CALL
    /// (plan_entity.md §3.4, step 1f).
    ///
    /// <para>
    /// The twelve <c>OnScripted*</c> members below it are still here and are
    /// still what this routes to; what changed is the ROUTE. The native shell
    /// no longer has twelve slots to call, it has one, and the op number is
    /// what makes the override bitmask possible: the shell tests
    /// <c>1u &lt;&lt; op</c> against the class descriptor before crossing, so an
    /// op a class does not override costs zero managed transitions.
    /// </para>
    ///
    /// <para>
    /// <paramref name="blob"/> is the entity's own allocation, engine block
    /// first, valid only for this call — the implementation wraps it in an
    /// <see cref="EntityRef"/>, which the compiler forbids escaping.
    /// </para>
    ///
    /// <para>
    /// The return is the op's answer: whether C# claimed a keyvalue or an
    /// input, how much damage landed, a filter's tri-state, 0 otherwise.
    /// </para>
    ///
    /// <para>
    /// A DEFAULT BODY of 0, so a module that has no entities at all — and every
    /// test double — is not obliged to write one.
    /// </para>
    /// </summary>
    int Dispatch(EntityOp op, EntityHandle entity, Span<byte> blob, in EntityOpArgs args) => 0;

    /// <summary>A C#-declared map entity spawned, and needs an instance bound to it.</summary>
    void OnScriptedSpawn(EntityHandle entity, string className);

    /// <summary>A map key the engine did not claim. True if the entity consumed it.</summary>
    bool OnScriptedKeyValue(EntityHandle entity, string className, string key, string value);

    /// <summary>An input fired at a C#-declared entity. True if handled.</summary>
    bool OnScriptedInput(EntityHandle entity, string name, string value, float number, EntityHandle activator);

    /// <summary>A scheduled think came due.</summary>
    void OnScriptedThink(EntityHandle entity);

    /// <summary>An entity is being removed; drop the instance behind it.</summary>
    void OnScriptedRemoved(EntityHandle entity);

    /// <summary>Damage on a C#-declared entity. Returns how much was actually taken.</summary>
    int OnScriptedDamaged(EntityHandle entity, EntityHandle attacker, float damage, int damageType,
                          Vector position, Vector force);

    /// <summary>A C#-declared entity's health reached zero.</summary>
    void OnScriptedKilled(EntityHandle entity, EntityHandle attacker);

    /// <summary>A moving brush arrived where it was sent.</summary>
    void OnScriptedMoveDone(EntityHandle entity);

    /// <summary>A moving brush met something it could not push.</summary>
    void OnScriptedBlocked(EntityHandle entity, EntityHandle other);

    /// <summary>A player left a brush volume.</summary>
    void OnScriptedEndTouch(EntityHandle entity, EntityHandle other);

    /// <summary>
    /// Whether a managed filter admits something, asked by an SDK entity that
    /// named it: 1 passes, 0 fails, -1 when the handle is not a filter at all.
    ///
    /// <para>
    /// The last one is not an error. A mapper's <c>filtername</c> may land on
    /// anything, and the SDK's answer to a name that is not a filter has always
    /// been to behave as though none were set — so the caller can treat -1 the
    /// same way, and only this side knows which classes are filters.
    /// </para>
    ///
    /// <para>
    /// <paramref name="entity"/> is the ATTACKER when the question is about
    /// damage, because that is the entity CBaseFilter tests.
    /// </para>
    /// </summary>
    int OnScriptedFilterPasses(EntityHandle filter, EntityHandle entity, int damageType, bool damage);

    /// <summary>
    /// How much of a jump a player gets: 1 is the ordinary jump, 0 refuses it.
    ///
    /// Declared on BOTH module interfaces because movement is predicted and the
    /// question is asked on both sides. A rule that answers differently on the
    /// two makes the player rubber-band.
    /// </summary>
    float GetJumpScale(in JumpRequest request);

    /// <summary>
    /// How slippery the ground under a player is. Runs on BOTH sides, every
    /// movement frame the player is grounded, so it must decide from its
    /// arguments alone.
    /// </summary>
    float GetFrictionScale(int playerIndex, float friction, float speed,
                           bool ducking, int groundMaterial);

    /// <summary>
    /// How fast a player may move right now. Runs on both sides; asked after
    /// the engine's own speed factors.
    /// </summary>
    float GetMaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial);

    //-------------------------------------------------------------------------
    // The game DLL, in the five subjects it splits into.
    //
    // A PROPERTY PER SUBJECT RATHER THAN ONE, and that is the point rather than
    // verbosity. The bootstrap's thunks resolve an INTERFACE and land on it:
    // ServerGameFrame reaches IServerFrame and can reach nothing else, so the
    // thunk table reads as a map of which subject each engine slot belongs to.
    // Handing out one combined object instead would put the grouping back in a
    // comment.
    //
    // All five are the same instance today (ServerGameDll implements them all),
    // and callers must not assume that: Part 13 splits the class when a member
    // belongs to one interface and not the others, and these properties are
    // exactly the seam it splits along.
    //-------------------------------------------------------------------------

    /// <summary>The mod coming up and going down.</summary>
    IServerLifetime Lifetime { get; }

    /// <summary>A map's load, activation and teardown.</summary>
    IServerLevel Level { get; }

    /// <summary>The server tick.</summary>
    IServerFrame Frames { get; }

    /// <summary>
    /// Hands the engine's console lines buffered since the last drain to the
    /// side's listeners (console-batch): once per server frame, after it.
    /// </summary>
    void DrainConsole();

    /// <summary>How the server describes itself to the world.</summary>
    IServerListing Listing { get; }

    /// <summary>
    /// The server as a hosted process: cvar queries coming back, hibernation,
    /// and the lines under <c>status</c>. plan_gamedll.md Part 10.
    /// </summary>
    IServerHosting Hosting { get; }

    /// <summary>
    /// propdata.txt's breakable-chunk table — plan_gamedll_after.md Phase A3.
    ///
    /// <para>
    /// <b>A subject of its own rather than a member of one of the five.</b>
    /// Neither a lifetime, a level, a frame, a listing nor a hosted process: it
    /// is a GAME SYSTEM that used to be a row of <c>kServerSystems[]</c>. The
    /// property exists so each thunk still resolves exactly one subject.
    /// </para>
    /// </summary>
    IServerPropData PropData { get; }

    /// <summary>
    /// The world's record of what recently made a noise —
    /// plan_gamedll_after.md Phase A3, second pair. A subject of its own, for
    /// <see cref="PropData"/>'s reason.
    ///
    /// <para>
    /// <b>It has TWO kinds of caller and one of them is not the shell.</b>
    /// <c>shell_soundent.cpp</c> asks it the three lifecycle questions, and
    /// <c>env_microphone</c> and <c>ai_sound</c> — both managed entities — ask
    /// it the two the map cares about. The second pair used to cross the
    /// boundary for those, through two <c>IEngineSound</c> slots that are gone.
    /// </para>
    /// </summary>
    IServerSoundEnt SoundEnt { get; }

    /// <summary>
    /// Sound envelopes — the same phase's other system. A subject of its own, for
    /// <see cref="PropData"/>'s reason, and unlike <see cref="SoundEnt"/> it has
    /// exactly one kind of caller: nothing managed creates a sound patch,
    /// because creating one needs a recipient filter and a filter is native.
    /// </summary>
    IServerSoundController SoundController { get; }

    /// <summary>
    /// <c>IServerGameEnts</c>' managed half — plan_server.md Phase 8b,
    /// docs/plan-audit.md C4. A subject of its own, for <see cref="PropData"/>'s
    /// reason: the engine asks these of the ENTITY LIST, which is neither the
    /// lifetime, the level, the frame nor the listing.
    /// </summary>
    IServerEntities Entities { get; }

    /// <summary>
    /// <c>IServerGameClients</c>' managed half — plan_server.md Phase 7,
    /// docs/plan-audit.md C5. An eleventh subject, for <see cref="Entities"/>'
    /// reason: the engine asks these about ONE PLAYER, which is neither the
    /// entity list, the lifetime, the level, the frame nor the listing.
    /// </summary>
    IServerClients Clients { get; }

    /// <summary>
    /// The template store — plan_server_all.md Wave 1, docs/plan-audit.md D11.
    /// A twelfth subject, for <see cref="Entities"/>' reason: what asks it is
    /// the MAP PARSE, standing inside <c>MapEntity_ParseAllEntities</c> with an
    /// entity's keyvalue text and no managed caller above it.
    /// </summary>
    IServerTemplates Templates { get; }

    /// <summary>
    /// The cross-level global state table — plan_entity.md Phase 4 exit, lane
    /// p4-lifecycle. A THIRTEENTH subject, for <see cref="Templates"/>' reason
    /// and one more of its own.
    ///
    /// <para>
    /// It is not a lifetime, a level, a frame or a listing because it
    /// deliberately outlives all four: the table is the one piece of game state
    /// that survives a level change on purpose, which is the whole reason
    /// <see cref="IServerGlobalState.GetMap"/> exists.
    /// </para>
    ///
    /// <para>
    /// <b>Its callers are transitional and will die with SCC 11.</b>
    /// <c>game/server/globalstate.cpp</c>'s eleven exported free functions
    /// forward here, for four consumers that are all stock TUs on their way out
    /// of the build (<c>baseentity.cpp</c>, <c>entitylist.cpp</c>,
    /// <c>util.cpp</c>) plus <c>world_adapter.cpp</c>. Managed entities that
    /// read global state — <c>logic_auto</c>, <c>multisource</c> — resolve this
    /// contract from the container and cross nothing.
    /// </para>
    /// </summary>
    IServerGlobalState GlobalState { get; }

    /// <c>IServerGameDLL::Think</c> — once per frame, <b>even with no level
    /// loaded</b>.
    ///
    /// <para>
    /// <b>On the MODULE and not on <see cref="Frames"/>, deliberately.</b> The
    /// four properties above are the game's seam — a lifetime, a level, a
    /// frame, a listing — and this is none of them: it is the module's own
    /// housekeeping, and today its whole content is the dev API's command pump,
    /// which the module already documents as "NOT A PARTICIPANT". Putting it on
    /// <see cref="IServerFrame"/> would say it is a frame point, and the one
    /// thing it must never become is a fifth place the game ticks.
    /// </para>
    ///
    /// <para>
    /// Nothing reached from here may assume a level is loaded. That is the
    /// whole reason it is not <c>GameFrame</c>.
    /// </para>
    /// </summary>
    void EngineThink();
}
