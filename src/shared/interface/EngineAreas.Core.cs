// The engine, split into the areas it actually has.
// 
// An interface per area rather than one for all 273 members, because the point
// is what a class ASKS FOR: a trigger that takes IEngineDamage has said what it
// does to whoever walks in, and a reader of its constructor knows without
// opening the body. One giant interface would say only "it touches the engine".
//
// Every one of these is implemented twice over: by the real binding, which is
// one call per member into Engine's function-pointer table, and by whatever a
// test hands over instead. THAT is the reason for the whole exercise. Today a
// test that calls the engine gets 0 or empty back — silently, because there is
// no engine in a test process — so an entity test can assert what an entity
// computed but never what it asked the engine to DO. A check that cannot fail
// reads exactly like one that passes.

namespace SourceSharp.Abi;

/// <summary>
/// The console: what is printed to it, and the variables and commands it holds.
///
/// <para>
/// Not the mod's own convar declarations — <see cref="ConVarRegistry"/> owns
/// those. This is the engine's side of them: registering one, reading and writing
/// a value, and walking the engine's own table of everything else that exists.
/// </para>
/// </summary>
public interface IEngineConsole
{
    /// <summary>
    /// Runs a console command as if typed at the server console. Runs it now,
    /// not next frame — so it must be called on the game thread.
    /// </summary>
    void ExecuteServerCommand(string command);

    /// <inheritdoc cref="EngineSide.FindConVarValue"/>
    string? FindConVarValue(string name);

    /// <inheritdoc cref="EngineSide.GetConVarValue"/>
    string GetConVarValue(int id);

    /// <inheritdoc cref="EngineSide.Msg"/>
    void Msg(string message);

    /// <inheritdoc cref="EngineSide.RegisterConCommand"/>
    int RegisterConCommand(string name, string help, ConVarFlags flags);

    /// <inheritdoc cref="EngineSide.RegisterConVar"/>
    int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null);

    /// <inheritdoc cref="EngineSide.SendClientCommand"/>
    void SendClientCommand(EntityHandle player, string command);

    /// <inheritdoc cref="EngineSide.SetConVarValue"/>
    void SetConVarValue(int id, string value);

    /// <inheritdoc cref="EngineSide.SetNamedConVarValue"/>
    void SetConVarValueByName(string name, string value);

    /// <inheritdoc cref="EngineSide.ConsoleSnapshot"/>
    int Snapshot();

    /// <inheritdoc cref="EngineSide.ConsoleSnapshotEntry"/>
    bool SnapshotEntry(int index, out NativeConsoleEntry entry);

    /// <inheritdoc cref="EngineSide.Warning"/>
    void Warning(string message);
}

/// <summary>
/// The process and the match: what time it is, what was on the command line,
/// and where the managed assemblies were loaded from.
///
/// <para>
/// The odd one out is <see cref="EndRound"/>, which ends the MATCH rather than the
/// process. It sits here because there is nothing else it belongs to: a round is a
/// property of the session, not of any entity in it.
/// </para>
///
/// <para>
/// Nothing here marks which side is executing: no thread does since
/// plan_side_isolation P5, because each side runs its own copy of the Abi.
/// <see cref="ActiveSide"/> answers the side this binding was built for.
/// </para>
/// </summary>
public interface IEngineHost
{
    /// <summary>The side this binding serves: its own, never the thread's active one.</summary>
    Side ActiveSide { get; }

    /// <inheritdoc cref="EngineSide.CurrentTime"/>
    float CurrentTime { get; }

    /// <inheritdoc cref="EngineSide.EndRound"/>
    void EndRound();

    /// <inheritdoc cref="EngineSide.IsInitialized"/>
    bool IsInitialized { get; }

    /// <inheritdoc cref="EngineSide.ManagedDirectory"/>
    string ManagedDirectory { get; }

    /// <summary>
    /// A random number from the ENGINE's stream.
    ///
    /// <para>
    /// Not a convenience over System.Random, a correctness requirement: server
    /// randomness is replayed by demo playback and has to come out the same, and
    /// a managed generator keeps state the recording knows nothing about.
    /// Managed code may ask for a random number; it may not be the source of one.
    /// </para>
    /// </summary>
    float RandomFloat(float min, float max);

    /// <summary>
    /// An integer in <c>[min, max]</c>, both ends included, from the same
    /// engine stream (<c>IUniformRandomStream::RandomInt</c>). The client's
    /// effects draw from it where stock's <c>random-&gt;RandomInt</c> did.
    /// </summary>
    int RandomInt(int min, int max);

    // ---- plan_gamedll_after.md Phase A2 ------------------------------------
    //
    // THE STEAM GAME-SERVER CONTEXT IS THE PROCESS'S, NOT THE LEVEL'S, which is
    // why the three live here beside the command line and the active side and
    // not on IEngineLevel. The level shutdown clears it as one of its steps;
    // that step forwards to the same native body these do.

    /// <summary>
    /// Whether this process is a dedicated server. Stock reads it to decide
    /// whether to ask Steam for gameplay stats, and it is the difference
    /// between a hibernation notice that can arrive and one that cannot.
    /// </summary>
    bool IsDedicatedServer { get; }

    /// <summary>
    /// Clear the game server interface and fetch it afresh (Steam hands out a
    /// new one after a reconnect). True when there is a game server to talk to.
    /// C#'s own, through libsteam_api.so's flat API (SteamGameServerFlat).
    /// </summary>
    bool SteamGameServerReacquire();

    /// <summary>Drop the game server interface; the next activation fetches it again.</summary>
    void SteamGameServerClear();

    /// <summary>ISteamGameServer::GetGameplayStats, when there is a game server.</summary>
    void SteamGameServerRequestGameplayStats();

    /// <summary>ISteamGameServer::BLoggedOn; false when there is no game server.</summary>
    bool SteamGameServerLoggedOn();
}

/// <summary>
/// Precaching. Models, sounds, decals and materials, all of which must be asked
/// for before a level is running or the engine will refuse them.
///
/// <para>
/// Its own area because the RULE is the same for all four and belongs to none of
/// their subjects: precaching is about WHEN, not about what.
/// </para>
///
/// <para>
/// The two model readers are here for that same reason rather than in spite of
/// it. <see cref="ModelFrameCount"/> and <see cref="BreakPieceCount"/> both
/// answer "what did this file turn out to contain", and both exist because
/// something had to be asked DURING the precache window: the frame count sizes
/// a sprite that has just been precached, and the break list names the pieces
/// that must be precached next. Neither has an entity to hang off — the break
/// list in particular is read from a model NAME, because a piece's own pieces
/// are precached by recursing on a name with nothing behind it.
/// </para>
/// </summary>
public interface IEnginePrecache
{
    /// <inheritdoc cref="EngineSide.PrecacheDecal"/>
    int PrecacheDecal(string name);

    /// <summary>
    /// Precaches a material by name — "skybox/sky_day01_01rt", with no
    /// extension.
    ///
    /// <para>
    /// Not <see cref="PrecacheModel"/> with a .vmt, which is the neighbouring
    /// call and answers an INDEX. A model index is a number both sides of the
    /// wire agree on; a material is only ever a name the renderer resolves, and
    /// there is nothing to network.
    /// </para>
    /// </summary>
    void PrecacheMaterial(string material);

    /// <inheritdoc cref="EngineSide.PrecacheModel"/>
    int PrecacheModel(string model);

    /// <summary>
    /// How many frames a precached model has, by the index
    /// <see cref="PrecacheModel"/> answered. -1 if there is no such model.
    ///
    /// <para>
    /// Only a sprite ever has more than one. Stock's <c>CSprite::Spawn</c> takes
    /// this, subtracts one and keeps it as <c>m_flMaxFrame</c> — which is what
    /// the animation wraps at, and what a one-shot sprite compares against to
    /// switch itself off after a single cycle.
    /// </para>
    /// </summary>
    int ModelFrameCount(int modelIndex);

    /// <summary>
    /// What a precached model IS: <c>modelinfo-&gt;GetModelType</c>, by the index
    /// <see cref="PrecacheModel"/> answered.
    ///
    /// <para>
    /// <see cref="Abi.ModelType.Bad"/> for an index that names no model, and for
    /// the slot being unwired. Those are the same answer on purpose — both mean
    /// "there is nothing to ask about", and stock's callers all test the model
    /// pointer before asking, which is the test this stands in for.
    /// </para>
    ///
    /// <para>
    /// ONE READER TODAY, and it is worth naming: <c>CTempEnts::BreakModel</c>
    /// randomises <c>m_flFrame</c> for a <c>mod_sprite</c> gib and
    /// <c>m_nBody</c> for a <c>mod_studio</c> one. Getting that branch wrong is
    /// fifteen gibs that are all body 0 — the same chunk of wood fifteen times
    /// — which looks like a working effect.
    /// </para>
    /// </summary>
    ModelType ModelTypeOf(int modelIndex);

    /// <summary>
    /// A particle system, by name, so the client can build one.
    ///
    /// <para>
    /// The neighbour of <see cref="PrecacheModel"/> and
    /// <see cref="PrecacheMaterial"/> and interchangeable with neither: a
    /// particle system has its own string table, and a name that never reached
    /// it does not refuse at draw time — it is simply not there.
    /// </para>
    ///
    /// <para>
    /// Stock precaches the fire systems from <c>CFire::Precache</c>. That has
    /// not run since <c>env_fire</c> became a C# class, because
    /// <c>LINK_ENTITY_TO_CLASS( env_fire, CFire )</c> sits inside
    /// <c>#ifndef SOURCESHARP</c> and no <c>CFire</c> is ever constructed, and
    /// there was no other door to ask through.
    /// </para>
    /// </summary>
    void PrecacheParticleSystem(string name);

    /// <inheritdoc cref="EngineSide.PrecacheSound"/>
    void PrecacheSound(string soundName);

    /// <summary>How many break pieces <paramref name="model"/> declares (this side's <see cref="BreakModels"/>).</summary>
    int BreakPieceCount(string model);

    /// <summary>
    /// Break piece <paramref name="index"/> of <paramref name="model"/> (this side's
    /// <see cref="BreakModels"/>), with the model's defaults applied; false when
    /// there is no such piece.
    /// </summary>
    bool BreakPieceAt(string model, int index, float defaultBurstScale,
                      CollisionGroup defaultCollisionGroup,
                      out string pieceModel, out BreakPiece piece);
}

/// <summary>
/// The engine's own event bus: subscribing, and reading the fields of an event
/// that fired.
///
/// <para>
/// Reading is by name and by type because that is how the engine's events are
/// described. <c>[GameEvent]</c> classes are the typed layer on top; this is what
/// they are built from.
/// </para>
/// </summary>
/// <summary>
/// Facts only native code can record, for the dev API: the player-shadow
/// counters and the exit recorders (server only; the client answers zero).
/// </summary>
public interface IEngineDiagnostics
{
    /// <inheritdoc cref="EngineSide.PlayerShadowCounts"/>
    int PlayerShadowCounts(Span<int> into);

    /// <inheritdoc cref="EngineSide.ExitWatchArm"/>
    int ExitWatchArm(string recordPath);

    /// <inheritdoc cref="EngineSide.ExitWatchRead"/>
    int ExitWatchRead(Span<int> into);

    /// <inheritdoc cref="EngineSide.ExitWatchDisarm"/>
    int ExitWatchDisarm();
}

public interface IEngineGameEvents
{
    /// <inheritdoc cref="EngineSide.EventGetBool"/>
    bool EventGetBool(IntPtr handle, string key);

    /// <inheritdoc cref="EngineSide.EventGetFloat"/>
    float EventGetFloat(IntPtr handle, string key);

    /// <inheritdoc cref="EngineSide.EventGetInt"/>
    int EventGetInt(IntPtr handle, string key);

    /// <inheritdoc cref="EngineSide.EventGetString"/>
    string EventGetString(IntPtr handle, string key);

    /// <inheritdoc cref="EngineSide.ListenForGameEvent"/>
    void ListenForGameEvent(string name);

    /// <summary>
    /// A new game event by name (the server's CreateEvent), or zero for a name
    /// the game's event files do not declare, or on the client, which fires none.
    /// Fire it (<see cref="EventFire"/>, which frees it) or free it.
    /// </summary>
    IntPtr EventCreate(string name);

    /// <inheritdoc cref="EngineSide.EventSetInt"/>
    void EventSetInt(IntPtr handle, string key, int value);

    /// <inheritdoc cref="EngineSide.EventSetFloat"/>
    void EventSetFloat(IntPtr handle, string key, float value);

    /// <inheritdoc cref="EngineSide.EventSetBool"/>
    void EventSetBool(IntPtr handle, string key, bool value);

    /// <inheritdoc cref="EngineSide.EventSetString"/>
    void EventSetString(IntPtr handle, string key, string value);

    /// <summary>Fires an event (the engine's FireEvent, which frees it); whether the engine took it.</summary>
    bool EventFire(IntPtr handle);

    /// <summary>Frees an event that was created and will not be fired.</summary>
    void EventFree(IntPtr handle);
}

/// <summary>
/// Putting state on the wire: user messages, the type table, and the blob an
/// entity replicates.
///
/// <para>
/// The layer under <c>[NetState]</c> rather than a replacement for it. A class
/// declares what it networks and the generated code fills the blob; this is what
/// carries the result across.
/// </para>
/// </summary>
public interface IEngineNetworking
{
    /// <inheritdoc cref="EngineSide.CheckPayloadAgreement"/>
    PayloadAgreement CheckPayloadAgreement(EntityHandle h);

    /// <inheritdoc cref="EngineSide.EntityGetState"/>
    EntityState EntityGetState(EntityHandle entity, out MoveType moveType);

    /// <inheritdoc cref="EngineSide.GetEntityBlob"/>
    Span<byte> GetEntityBlob(EntityHandle h);

    /// <inheritdoc cref="EngineSide.MarkEntityStateChanged"/>
    void MarkEntityStateChanged(EntityHandle h);

    /// <inheritdoc cref="EngineSide.MarkEntityBlockChanged"/>
    void MarkEntityBlockChanged(EntityHandle h, int blockOffset);

    /// <inheritdoc cref="EngineSide.NetHierarchyDeclare"/>
    int NetHierarchyDeclare(int blobBytes);

    /// <inheritdoc cref="EngineSide.WorldNetDeclare"/>
    int WorldNetDeclare(int blobBytes);

    /// <inheritdoc cref="EngineSide.NetPublishType"/>
    int NetPublishType(string className, int shell, uint schemaHash, int layoutBytes, out uint nativeTypeId);

    /// <inheritdoc cref="EngineSide.NetTierAt"/>
    NetTierInfo? NetTierAt(int shell, int tier);

    /// <inheritdoc cref="EngineSide.SendServerMessage"/>
    bool SendServerMessage(string key, ReadOnlySpan<byte> payload);
}

/// <summary>
/// The bridge for C#-declared entities: registering a class, asking for a think,
/// firing an output, and the four halves of templating that cannot be C#.
///
/// <para>
/// Three of these are what every <c>[ScriptedEntity]</c> needs and are the most
/// load-bearing calls in the mod. The two Template ones serve exactly one class
/// — <c>PointTemplate</c> — and are here because they are what IT asks the
/// engine for: the keyvalue text of a map entity, and the parser that turns text
/// back into one in hierarchy order. Everything else a template does, up to and
/// including where each copy goes and which copy of the text gets parsed, is on
/// the managed side.
/// </para>
///
/// <para>
/// <b>There were FOUR</b> (plan_server_all.md Wave 1, docs/plan-audit.md D11).
/// <c>TemplateReconnectIO</c> and <c>TemplateSchedulePrecache</c> were both
/// text manipulation on a native store, and the store is
/// <c>SourceSharp.Server.TemplateStore</c> now — so both are one managed object
/// calling another and neither is a crossing.
/// </para>
/// </summary>
public interface IEngineScripted
{
    /// <summary>
    /// Turns every entity of a name into a template, and answers where each one
    /// stood.
    ///
    /// <para>
    /// Only meaningful DURING the map parse, in the window between the entities
    /// being read and the entities being spawned — which is the window a
    /// point_template's own Spawn runs in, and the whole reason any part of
    /// templating is still native. Outside it there is nothing to claim, and
    /// this answers with nothing.
    /// </para>
    /// </summary>
    /// <param name="removeSources">
    /// Whether the entities turned into templates are consumed. A template
    /// source is not usually meant to remain in the world; a map that wants it
    /// to says so with a spawnflag, and that decision is the caller's.
    /// </param>
    /// <returns>
    /// One entry per source found, each carrying the index the template store
    /// gave it and the place it stood in the world.
    /// </returns>
    List<TemplatePlacement> TemplateCapture(string sourceName, bool removeSources, int limit = 64);

    /// <inheritdoc cref="EngineSide.TemplateSpawnKeyed"/>
    List<EntityHandle> TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit = 64);

    // FireOutput WAS HERE, AND IS GONE WITH ITS SLOT -- plan_entity.md step 2a.
    // An output is fired by the entity that declares it (Output.Fire), which
    // posts into its directory's queue; there is nothing for an ENGINE area to
    // answer, because the engine is no longer involved.

    /// <inheritdoc cref="EngineSide.RegisterScriptedEntity"/>
    void RegisterEntityClass(string className, string[] outputNames, ScriptedEntityKind kind);

    /// <inheritdoc cref="EngineSide.RegisterClassOps"/>
    void RegisterEntityClassOps(string className, uint opMask);
}
