namespace SourceSharp.Abi;

//-----------------------------------------------------------------------------
// The game DLL, split by subject rather than mirrored.
//
// IServerGameDLL is ONE native interface because it is what the engine loads,
// not because its 48 methods are one subject. A C# author changing the game
// description should not be reading past SaveWriteFields to find it. So the
// slots fan out into interfaces named after what a mod author is doing when
// they touch one (plan_server.md Part 1; plan_gamedll.md Phase 3).
//
// Four here, growing to ten as the client, entity and tags interfaces land.
// One class implements them all for now -- see ServerGameDll -- because
// splitting one class into four before there is weight in any of them is a
// guess about where the weight lands. The INTERFACES are the seam that
// matters: they are what the bootstrap's thunks resolve, and what a reader
// opens to find where a decision lives.
//-----------------------------------------------------------------------------

/// <summary>
/// The mod coming up and going down. Native: <c>DLLInit</c>'s epilogue,
/// <c>GameInit</c>, <c>PostInit</c>, <c>GameShutdown</c>, <c>GetTickInterval</c>.
///
/// <para>
/// <b>There is no <c>SystemStop</c>, and that is a decision.</b> The adapter's
/// <c>OnSystemStop</c> is the partner of the <i>loader</i> — it unloads the
/// managed side through the existing <c>Shutdown</c> slot — so a second managed
/// "stop" beside it would be two names for one moment. See
/// docs/gamedll-ledger.md, Phase 2.
/// </para>
/// </summary>
public interface IServerLifetime
{
    /// <summary>
    /// <c>DEFAULT_TICK_INTERVAL</c> (public/const.h:31). Fifteen milliseconds.
    ///
    /// <para>
    /// <b>Here rather than on the implementation, because two sides need it.</b>
    /// <c>ServerGameDll</c> answers with it when <c>-tickrate</c> did not move
    /// it, and <c>Entry.ServerTickInterval</c> answers with it when there is no
    /// server module at all — and <c>SourceSharp.Bootstrap</c> references only
    /// <c>SourceSharp.Abi</c>, so a copy of the number in each is a copy that
    /// can drift.
    /// </para>
    /// </summary>
    public const float DefaultTickInterval = 0.015f;

    /// <summary>
    /// The runtime is up and the shell's <c>DLLInit</c> is finishing. The first
    /// lifecycle call into a runtime that already exists.
    /// </summary>
    /// <returns>False to fail <c>DLLInit</c> and refuse to start the game.</returns>
    bool SystemStart();

    /// <summary>Stock's <c>GameInit</c>. False to fail the game's start.</summary>
    bool GameStart();

    /// <summary>Stock's <c>PostInit</c>: every system has had its init.</summary>
    void GamePostStart();

    /// <summary>Stock's <c>GameShutdown</c>.</summary>
    void GameStop();

    /// <summary>
    /// Seconds per tick. Stock's <c>GetTickInterval</c>, which returns
    /// <c>DEFAULT_TICK_INTERVAL</c> unless <c>-tickrate</c> moved it.
    ///
    /// <para>
    /// <b>Read ONCE, at <c>DLLInit</c>.</b> Stock's is a <c>const</c> method the
    /// engine asks exactly once, and a tick interval that changes mid-run is not
    /// a feature — every duration already computed against the old one silently
    /// means something else. A property rather than a settable field for the
    /// same reason: the implementation decides it once and answers.
    /// </para>
    /// </summary>
    float TickInterval { get; }
}

/// <summary>
/// <b>Which arm of <c>LevelInit</c> a load takes</b> (plan_server_native_v2 T4e).
/// Stock's <c>CServerGameDLL::LevelInit</c> (gameinterface.cpp) picks it from its
/// two bools and the old level: <c>loadGame</c> with an old level is a
/// transition, <c>loadGame</c> alone a saved game, otherwise <c>background</c>
/// picks a background map over a new game. The shell used to choose in C++ and
/// call one of four slots; it now forwards the three facts and this chooses.
/// </summary>
public static class MapLoadArms
{
    /// <summary>The arm, from <c>LevelInit</c>'s <c>loadGame</c>, whether
    /// <c>pOldLevel</c> was non-null, and <c>background</c>.</summary>
    public static MapLoadType For(bool loadGame, bool hasOldLevel, bool background)
    {
        if (loadGame)
            return hasOldLevel ? MapLoadType.Transition : MapLoadType.LoadGame;

        return background ? MapLoadType.Background : MapLoadType.NewGame;
    }
}

/// <summary>
/// A map's load, activation and teardown. Native: the four arms of
/// <c>LevelInit</c>, <c>ServerActivate</c>, <c>LevelShutdown</c> and
/// <c>CreateNetworkStringTables</c>.
///
/// <para>
/// <b>Four load methods, and <see cref="MapLoad"/> over them.</b> The four arms
/// are genuinely different: <c>Background</c> differs from <c>NewGame</c> only
/// in <c>eLoadType</c>, while <c>Transition</c> and <c>SaveGame</c> are the save
/// cluster this port refuses (Part 7). The shell resolved <c>LevelInit</c>'s two
/// bools and called one of four slots until plan_server_native_v2 T4e; now it
/// forwards them, <see cref="MapLoadArms.For"/> picks the arm and
/// <see cref="MapLoad"/> runs it.
/// </para>
/// </summary>
public interface IServerLevel
{
    /// <summary>A level transition — the save cluster. False aborts the load.</summary>
    bool MapLoadTransition(in MapLoad load);

    /// <summary>A save being restored — the save cluster. False aborts the load.</summary>
    bool MapLoadSaveGame(in MapLoad load);

    /// <summary>A background map. <c>NewGame</c> with a different load type.</summary>
    bool MapLoadBackground(in MapLoad load);

    /// <summary>A fresh map. The arm every multiplayer load takes.</summary>
    bool MapLoadNewGame(in MapLoad load);

    /// <summary>
    /// <c>IServerGameDLL::LevelInit</c> itself (plan_server_native_v2 T4e): the
    /// load type <see cref="MapLoadArms.For"/> chose is written to the engine's
    /// globals first, as stock writes <c>gpGlobals-&gt;eLoadType</c> before
    /// anything else in the arm, and then that arm runs.
    /// </summary>
    bool MapLoad(in MapLoad load, MapLoadType how);

    /// <summary>
    /// The PRE-ENTITY half of level init: the map is loading and NONE of its
    /// entities exists yet.
    ///
    /// <para>
    /// <b>Added by plan_gamedll_after.md Phase A3, and until then nothing
    /// reached that half at all.</b> Part 3a's stage split gave
    /// <c>IGameLifecycle</c> an <c>OnLevelInitPreEntity</c> and
    /// <c>LifecycleDriver</c> a <c>LevelInitPreEntity</c>, and
    /// <c>ServerModule</c> only ever called the PostEntity one — so a
    /// participant that declared the pre-entity stage was silently never run.
    /// </para>
    ///
    /// <para>
    /// <b>What makes it different from the four <c>MapLoad*</c> arms above:</b>
    /// those are <c>IServerGameDLL::LevelInit</c>, which the engine calls
    /// BEFORE the map's entity lump is parsed at all. This is stock's
    /// <c>LevelInitPreEntity</c> game-system stage, reached from inside the
    /// WORLD entity's own precache — which is later, and is the only moment at
    /// which the precache window is open and no map entity has spawned. A
    /// participant that needs to precache something has this and nothing else.
    /// </para>
    /// </summary>
    /// <param name="mapName">The map, bare — no <c>maps/</c>, no <c>.bsp</c>.</param>
    void MapLoadPreEntity(string mapName);

    /// <summary>
    /// What a newly precached model brings with it: its animation-event sounds,
    /// its $keyvalues "Particles" systems and its animation-event particle
    /// effects, precached in stock's order. plan_server_novalve.md P2.4/P3, out
    /// of Precache_ModelComponents, which calls it and does nothing else.
    /// </summary>
    void ModelPrecacheComponents(string modelName);

    /// <summary>
    /// Stock's "warn on out of order precache" (CBaseEntity::PrecacheModel),
    /// for a model about to be precached: <paramref name="allowed"/> is whether
    /// the precache window is open. plan_server_native_v2 T4e: Precache_Model
    /// made this check in C++.
    /// </summary>
    void ModelPrecacheCheck(string modelName, bool allowed);

    /// <summary>
    /// The PRE-ENTITY half of level shutdown: the map is going away and its
    /// entities are still alive.
    ///
    /// <para>
    /// <b>Added by plan_gamedll_after.md Phase A3's fifth system, and until
    /// then nothing reached that half either.</b> The exact mirror of
    /// <see cref="MapLoadPreEntity"/>: Part 3a's split gave
    /// <c>IGameLifecycle</c> an <c>OnLevelShutdownPreEntity</c> and
    /// <c>LifecycleDriver</c> a <c>LevelShutdownPreEntity</c>, and the only
    /// things that ever called the driver method were two tests.
    /// </para>
    ///
    /// <para>
    /// <b>Not the same moment as <see cref="MapUnload"/>.</b> That one is
    /// <c>IServerGameDLL::LevelShutdown</c>, which owns the whole teardown
    /// SEQUENCE and calls the native passes in order. This is stock's
    /// <c>LevelShutdownPreEntity</c> game-system stage, reached from inside the
    /// backward walk of that sequence's pre-entity pass — which is the only
    /// moment at which the entities still exist and the world is already
    /// closing. A participant that has to let go of an entity has this and
    /// nothing else.
    /// </para>
    /// </summary>
    void MapUnloadPreEntity();

    /// <summary>Stock's <c>LevelShutdown</c>.</summary>
    void MapUnload();

    /// <summary>
    /// Stock's <c>ServerActivate</c>: the entities exist and are about to be
    /// activated.
    /// </summary>
    void ServerActivate();

    /// <summary>
    /// Stock's <c>CreateNetworkStringTables</c>. Not optional — see Part 8: a
    /// server that creates none has no downloadables, no decals and no sounds
    /// table, and nothing says so.
    /// </summary>
    void CreateNetworkStringTables();

    /// <summary>
    /// One of the map's worldspawn keys (plan_gamedll_after.md Part B2).
    ///
    /// <para>
    /// ON THE LEVEL INTERFACE BECAUSE IT IS THE MAP TALKING. worldspawn is
    /// edict 0 of every map and its keys arrive exactly once, while the entity
    /// lump is being parsed, between <c>MapLoad*</c> and
    /// <c>ServerActivate</c> — which is what this interface is a list of. It is
    /// not hosting (that is what the server tells the outside world) and it is
    /// not a frame.
    /// </para>
    ///
    /// <para>
    /// THE SHIM ASKS AND DOES NOT TELL. <c>CWorld::KeyValue</c> offers every key
    /// it does not itself answer, because a list of the world's networked key
    /// names on the C++ side would be a second copy of a declaration that lives
    /// in C#. False means "not one of mine", and the shim then falls through to
    /// <c>CBaseEntity::KeyValue</c> exactly as it always did.
    /// </para>
    /// </summary>
    bool WorldKeyValue(EntityHandle world, string key, string value);
}

/// <summary>
/// The server tick. Native: <c>GameFrame</c> and <c>PreClientUpdate</c>.
///
/// <para>
/// <b><c>Think</c> is deliberately absent.</b> The engine's <c>Think</c> is the
/// one entry called even when no level is loaded, and this build uses it for
/// exactly one thing — servicing a queued <c>cs_sv_reload</c>, which is a
/// native concern about swapping the managed assemblies and cannot be answered
/// by the assembly being swapped. See docs/gamedll-ledger.md, "the reload
/// service point is Think".
/// </para>
///
/// <para>
/// <b>This is not <see cref="IFrameLifecycle"/>.</b> That interface is the three
/// points <i>inside</i> a frame, which every service may join. This is the frame
/// itself, which the game DLL owns and which decides the order those three run
/// in.
/// </para>
/// </summary>
public interface IServerFrame
{
    /// <summary>
    /// One server frame. <paramref name="simulating"/> is false when the server
    /// is paused or hibernating and entities must not think.
    /// </summary>
    void GameFrame(bool simulating);

    /// <summary>
    /// Just before client snapshots go out. The shell drops the
    /// <c>!simulating</c> case, so this is only called when it is true.
    /// </summary>
    void PreClientUpdate();
}

/// <summary>
/// How the server describes itself to the world — the server browser, mostly.
/// Native: <c>GetGameDescription</c>, <c>ShouldHideServer</c>,
/// <c>GetServerBrowserMapOverride</c>, <c>GetServerBrowserGameData</c> and
/// <c>IsManualMapChangeOkay</c>.
///
/// <para>
/// <b>All three string answers are read by the engine AFTER the call
/// returns.</b> The engine is handed a <c>const char *</c> and keeps it, so the
/// managed side never owns the storage: it writes into a caller-supplied
/// <c>char *pOut, int nOutSize</c> and the shell returns its own member buffer.
/// That is the convention <c>ServerGetGameDescription</c> already uses, and
/// <see cref="ListingBuffer"/> is the one implementation of it.
/// </para>
///
/// <para>
/// <b><c>IsManualMapChangeOkay</c> is here rather than on an
/// <c>IServerMapCatalogue</c>, and the two plans disagree about that.</b>
/// plan_gamedll.md Part 6's table puts it on this interface; plan_server.md
/// Part 1 puts it on <c>IServerMapCatalogue</c> with the four other map-listing
/// methods. It is here because Part 6 is the part being built and the catalogue
/// interface does not exist yet; when it lands, this moves. Recorded in
/// docs/gamedll-ledger.md rather than settled quietly.
/// </para>
/// </summary>
public interface IServerListing
{
    /// <summary>The name shown in the server browser.</summary>
    string GameDescription { get; }

    /// <summary>Whether to keep this server out of the master list.</summary>
    bool ShouldHideServer { get; }

    /// <summary>
    /// A map name to show instead of the real one, or null for the real one.
    /// </summary>
    string? BrowserMapOverride { get; }

    /// <summary>
    /// Arbitrary game data for the server browser entry, or null for none.
    /// </summary>
    string? BrowserGameData { get; }

    /// <summary>
    /// Whether an operator may change the map by hand right now.
    /// </summary>
    /// <param name="reason">
    /// Why not, when the answer is false. Null when it is true. The engine reads
    /// it after the call, so it crosses through <see cref="ListingBuffer"/> like
    /// the other strings rather than as a pointer.
    /// </param>
    bool IsManualMapChangeOkay(out string? reason);
}

/// <summary>
/// The server as a hosted process — plan_gamedll.md Part 10's three loose ends.
/// Native: <c>OnQueryCvarValueFinished</c>, <c>SetServerHibernation</c> and
/// <c>Status</c>.
///
/// <para>
/// <b>A fifth interface rather than three additions to the other four</b>,
/// because none of the three is what any of them is about. A cvar query coming
/// back is not the server describing itself to a browser; hibernation is not a
/// map's lifecycle; and <c>status</c> is not a frame. What they share is that
/// the engine, or the person running the server, is asking the mod about the
/// process rather than about the game.
/// </para>
///
/// <para>
/// <b>Two of the three are unreachable in this build and are still here.</b>
/// Nothing calls <c>StartQueryCvarValue</c>, so nothing can answer it; a listen
/// server does not hibernate. The shell counts every call to both, so
/// "unreachable" is a number somebody can read after a map rather than an
/// assertion — see docs/gamedll-ledger.md, Phase 8. The slots exist so that the
/// day one IS reached the answer is a mod's and not a stub's.
/// </para>
/// </summary>
public interface IServerHosting
{
    /// <summary>
    /// The engine has finished a <c>StartQueryCvarValue</c> and is handing back
    /// what a client's cvar said.
    /// </summary>
    /// <param name="cookie">The token <c>StartQueryCvarValue</c> returned.</param>
    /// <param name="entIndex">
    /// The player's entity index, or -1 when the engine passed no edict — which
    /// it does for a query whose client left before it completed.
    /// </param>
    /// <param name="status">
    /// Why there is or is not a value: <c>ValueIntact</c>, <c>CvarNotFound</c>,
    /// <c>NotACvar</c>, <c>CvarProtected</c>.
    /// </param>
    /// <param name="cvarName">The cvar that was asked about.</param>
    /// <param name="cvarValue">
    /// Its value, or null for every status but <see cref="QueryCvarStatus.ValueIntact"/>.
    /// </param>
    void QueryCvarValueFinished(int cookie, int entIndex, QueryCvarStatus status,
                                string cvarName, string? cvarValue);

    /// <summary>
    /// The server is entering or leaving hibernation — a dedicated server idling
    /// with nobody connected.
    ///
    /// <para>
    /// <b>Not the inverse of <see cref="IServerLevel.ServerActivate"/>.</b> The
    /// shell's TODO asked whether it was; it is not. Stock stores this as a flag
    /// a dedicated server consults while idle, where <c>ServerActivate</c> is a
    /// per-map event fired once the entities exist. They are not two halves of
    /// one thing, they do not alternate, and on a listen server this one never
    /// arrives at all.
    /// </para>
    /// </summary>
    void SetHibernating(bool hibernating);

    /// <summary>
    /// The lines this mod wants to appear under the engine's <c>status</c>
    /// console command, or null for none.
    ///
    /// <para>
    /// <b>Lines out, not a printer in.</b> Stock hands the game rules a
    /// <c>print</c> callback, and that callback is a C <i>variadic</i> function
    /// pointer — a calling convention this boundary cannot carry. So the
    /// direction is inverted and the shell does the printing, which costs
    /// nothing because everything a mod wants to say here is already text.
    /// </para>
    /// </summary>
    IEnumerable<string>? StatusLines { get; }

    // ---- plan_gamedll_after.md Phase A2: Part 11a2's three gaps ------------
    //
    // ON THIS INTERFACE AND NOT ON THE OTHER FOUR, by the same test that put
    // Part 10's three here: none of them is the server describing itself to a
    // browser, none is a map's lifecycle, and none is a frame. All three are
    // the engine asking the mod about the PROCESS — its Steam session, and the
    // caches it holds over the engine's own data.
    //
    // AND NONE OF THEM IS COUNTED, which is the difference from Part 10's two.
    // Part 7's counter turns "nothing can reach this" into a measurement; these
    // three are reached BY DESIGN — the engine calls them in normal operation —
    // so a counter would assert a claim nobody is making and a warning would
    // fire on every startup saying the opposite of what is true.

    /// <summary>
    /// The engine has flushed its model cache. Every <c>CStudioHdr</c> this DLL
    /// still holds is now built over studio data that is gone.
    ///
    /// <para>
    /// <b>The default implementation is the whole port.</b> It calls
    /// <c>Entities.InvalidateMdlCacheAll()</c>, which is stock's
    /// <c>gEntList</c> walk. That walk stays native because what it frees —
    /// <c>CBaseAnimating::m_pStudioHdr</c> — is a native member freed by a
    /// native virtual, and no managed walk reaches it. plan_gamedll.md Part
    /// 11a2 proposes an <c>IMdlCacheEvents</c> walk over MANAGED entities
    /// instead; its own caveat says the two populations differ, and measured,
    /// the managed one holds none of this state at all. A managed-only version
    /// would read as a fix and change nothing.
    /// </para>
    /// </summary>
    void InvalidateMdlCache();

    /// <summary>
    /// Steam is available: the game-server context has just been re-acquired
    /// and a mod may now register tags, list on the master server, or report.
    ///
    /// <para>
    /// <b>IT REPEATS.</b> The engine calls this whenever Steam becomes
    /// available, including after a reconnect, which is why the native half is
    /// a <c>Clear()</c> then an <c>Init()</c> rather than an initialise. A
    /// handler that registers server tags here registers them again on every
    /// reconnect, so handlers must be idempotent.
    /// </para>
    /// </summary>
    void SteamGameServerActivated();

    /// <summary>
    /// Steam is going away. Anything a mod wants to say to it must be said
    /// before this method returns.
    ///
    /// <para>
    /// <b>The order inverts against its partner and that is the design.</b> On
    /// the way up native acquires and then managed hears; here managed runs
    /// first and clears the context as its last act, because a handler that ran
    /// after the clear would have nothing to read. Stock makes the same
    /// argument with an entire game-system stage —
    /// <c>LevelShutdownPreClearSteamAPIContext</c> exists for no other reason.
    /// </para>
    /// </summary>
    void SteamGameServerShutdown();
}

/// <summary>
/// Why a queried client cvar does or does not have a value. Mirrors the
/// engine's <c>EQueryCvarValueStatus</c> (public/eiface.h).
/// </summary>
public enum QueryCvarStatus
{
    /// <summary>The value came back and is in hand.</summary>
    ValueIntact = 0,

    /// <summary>The client has no cvar by that name.</summary>
    CvarNotFound = 1,

    /// <summary>The name exists on the client but is a command, not a cvar.</summary>
    NotACvar = 2,

    /// <summary>The cvar is marked protected and the client refused to say.</summary>
    CvarProtected = 3,
}

/// <summary>
/// The decal script, answered from managed code — plan_gamedll_after.md
/// Phase A3.
///
/// <para>
/// <b>This is stock's <c>IDecalEmitterSystem</c>, moved.</b>
/// <c>CDecalEmitterSystem</c> was row 11 of <c>kServerSystems[]</c> and is not a
/// row any more: the KeyValues load of <c>scripts/decals_subrect.txt</c>, the
/// name table, the weighted pick and the game-material translation all live
/// here. What answers <c>decalsystem</c> on the server is a three-method shim
/// (<c>shell_decals.cpp</c>) that forwards to these, so every existing native
/// caller reaches managed code without being edited.
/// </para>
///
/// <para>
/// <b>The CLIENT keeps its own native copy and must.</b> <c>decals.cpp</c>
/// compiles into <c>client.so</c> too and its impact effects read the same
/// table; the file simply left <c>server_csharp.vpc</c>. T1's rule — carving a
/// shared file's server arm compiles green and silently stops initialising the
/// client's — is met by subtraction rather than by surgery.
/// </para>
/// </summary>
public interface IServerDecals
{
    /// <summary>
    /// A weighted pick over one named group, as the engine's precache index.
    /// <c>-1</c> for an unknown name, an empty group, or a group whose entries
    /// have not been precached — which are all the same answer to the caller
    /// and all mean "do not decal".
    /// </summary>
    int IndexForName(string name);

    /// <summary>
    /// The name an index was precached under, or null when no entry carries it.
    ///
    /// <para>
    /// <b>Null, not empty.</b> Stock answers <c>""</c> and the SHIM is what
    /// turns null back into that, so the contract native callers see is
    /// unchanged while the managed side keeps "there is no such entry"
    /// distinguishable from "the entry is blank".
    /// </para>
    /// </summary>
    string? NameForIndex(int index);

    /// <summary>
    /// Stock's <c>TranslateDecalForGameMaterial</c>: which decal group an impact
    /// on a given game material should use. Null when there is no translation,
    /// which the shim turns back into stock's "return the name you were given".
    /// </summary>
    /// <param name="gameMaterial">
    /// The surface's game material. An <c>int</c> and not a <c>char</c> because
    /// it is a byte used as a KEY and <c>char</c>'s signedness is not fixed by
    /// the ABI it crosses.
    /// </param>
    string? TranslateForGameMaterial(string decalName, int gameMaterial);
}

/// <summary>
/// propdata.txt's breakable-chunk table, answered from managed code —
/// plan_gamedll_after.md Phase A3.
///
/// <para>
/// <b>Half of <c>CPropData</c>, and the half with no entity in it.</b> The
/// <c>BreakableModels</c> section is a list of named groups of gib models; a
/// group is picked by name and a member of it at random. That is a name table,
/// which is what Part 9a's ownership test sends to C#.
/// </para>
///
/// <para>
/// <b>The other half did not move, and the blocker is named.</b> The
/// propdata.txt KeyValues TREE is still native, because
/// <c>SourceSharp_EntityGetPropData</c> walks a MODEL's own <c>prop_data</c>
/// section — the engine's studio data — into its <c>base</c> chain, and
/// <c>CPropData::ParsePropFromKV</c> writes the result through
/// <c>IBreakableWithPropData</c> on a native <c>CBaseEntity</c>. Both belong to
/// whichever lane makes <c>prop_physics</c> and <c>func_break</c> managed entity
/// classes. Argued at the site in <c>shell_propdata.cpp</c> as well, because a
/// verdict that lives only in a table is one the next reader of the file never
/// sees.
/// </para>
/// </summary>
public interface IServerPropData
{
    /// <summary>
    /// One model from a named chunk group, at random. Null for an unknown group
    /// or a propdata file that did not load — which is what stock answers, and
    /// what the shim turns <c>-1</c> back into.
    /// </summary>
    /// <param name="maxSize">
    /// Stock's cap on which member may be picked, <c>-1</c> for no cap. It
    /// bounds the INDEX rather than any measured size, and that is stock's own
    /// behaviour rather than a reading of it: the groups are documented in
    /// propdata.txt as "sorted in order of smallest to largest", so a low index
    /// is a small gib.
    /// </param>
    string? RandomChunkModel(string section, int maxSize);

    /// <summary>
    /// A model's prop data from its <c>$keyvalues</c> text: the health, and
    /// the explosive damage and radius, resolved through propdata.txt's
    /// <c>base</c> chain, each null where nothing in the chain sets it.
    /// ParsePropFromKV writes only what a level names over what the prop
    /// already holds, so a set 0 (a metal base's unbreakable health) is a
    /// value and an unset one is not.
    /// </summary>
    PropDataValues ModelPropValues(string? modelKeyValueText);

    /// <summary>
    /// What kind of prop data a model's <c>$keyvalues</c> text holds: stock's
    /// PARSE_* answer out of CBaseProp::ParsePropData, which CBaseProp::Spawn
    /// deletes a prop by.
    /// </summary>
    PropDataParse ModelPropDataKind(string? modelKeyValueText);

    /// <summary>
    /// A model's prop interactions (CPropData::ParsePropFromKV): its
    /// <c>physgun_interactions</c>, <c>fire_interactions</c> and
    /// <c>world_interactions</c>, with those of the propdata.txt sections its
    /// <c>base</c> chain names. None for a model with no prop_data.
    /// </summary>
    PropInteractions ModelInteractions(string? modelKeyValueText);

    /// <summary>
    /// A model's damage modifiers: its prop_data's <c>dmg.bullets</c>,
    /// <c>dmg.club</c> and <c>dmg.explosive</c> over its base chain's, each 1
    /// where nothing sets it (CBreakableProp's constructor).
    /// </summary>
    PropDamageModifiers ModelDamageModifiers(string? modelKeyValueText);

    /// <summary>
    /// CPropData::ParsePropFromBase: a propdata.txt section, by name, read as
    /// a func_breakable reads its "propdata". Its health and explosion, each
    /// null where its chain is silent, and its damage modifiers over
    /// <paramref name="start"/>. False for a name the file does not have.
    /// </summary>
    bool BaseSectionData(string sectionName, PropDamageModifiers start, out PropDataValues values,
                         out PropDamageModifiers modifiers);
}

/// <summary>
/// A model's prop_data health, explosion and multiplayer_break, each null
/// where its chain is silent.
/// </summary>
public readonly record struct PropDataValues(int? Health, float? ExplosiveDamage, float? ExplosiveRadius,
                                             MultiplayerBreak? MultiplayerBreak = null);

/// <summary>Stock's mp_break_t: which DLL makes a broken prop's pieces in multiplayer.</summary>
public enum MultiplayerBreak
{
    /// <summary>MULTIPLAYER_BREAK_DEFAULT: the clients.</summary>
    Default,

    /// <summary>MULTIPLAYER_BREAK_SERVERSIDE.</summary>
    ServerSide,

    /// <summary>MULTIPLAYER_BREAK_CLIENTSIDE.</summary>
    ClientSide,

    /// <summary>MULTIPLAYER_BREAK_BOTH.</summary>
    Both,
}

/// <summary>What a breakable multiplies bullet, club and blast damage by (IBreakableWithPropData's GetDmgMod*).</summary>
public readonly record struct PropDamageModifiers(float Bullet, float Club, float Explosive);

/// <summary>
/// Stock's <c>propdata_interactions_t</c> (props_shared.h), one bit each in
/// stock's order, which is the order sPropdataInteractionSections reads them.
/// </summary>
[Flags]
public enum PropInteractions : uint
{
    None = 0,

    /// <summary>physgun_interactions onworldimpact stick.</summary>
    PhysgunWorldStick = 1u << 0,

    /// <summary>physgun_interactions onfirstimpact break.</summary>
    PhysgunFirstBreak = 1u << 1,

    /// <summary>physgun_interactions onfirstimpact paintsplat.</summary>
    PhysgunFirstPaint = 1u << 2,

    /// <summary>physgun_interactions onfirstimpact impale.</summary>
    PhysgunFirstImpale = 1u << 3,

    /// <summary>physgun_interactions onlaunch spin_none.</summary>
    PhysgunLaunchSpinNone = 1u << 4,

    /// <summary>physgun_interactions onlaunch spin_zaxis.</summary>
    PhysgunLaunchSpinZ = 1u << 5,

    /// <summary>physgun_interactions onbreak explode_fire.</summary>
    PhysgunBreakExplode = 1u << 6,

    /// <summary>physgun_interactions damage none.</summary>
    PhysgunDamageNone = 1u << 7,

    /// <summary>fire_interactions flammable yes.</summary>
    FireFlammable = 1u << 8,

    /// <summary>fire_interactions explosive_resist yes.</summary>
    FireExplosiveResist = 1u << 9,

    /// <summary>fire_interactions ignite halfhealth.</summary>
    FireIgniteHalfHealth = 1u << 10,

    /// <summary>physgun_interactions onpickup create_flare.</summary>
    PhysgunCreateFlare = 1u << 11,

    /// <summary>physgun_interactions allow_overhead yes.</summary>
    PhysgunAllowOverhead = 1u << 12,

    /// <summary>world_interactions onworldimpact bloodsplat.</summary>
    WorldBloodSplat = 1u << 13,
}

/// <summary>Stock's <c>PARSE_*</c> (props_shared.h).</summary>
public enum PropDataParse
{
    /// <summary>Parsed: the model is a physics prop's and must be one.</summary>
    Succeeded,

    /// <summary>Parsed, and the model may also be static or dynamic (allowstatic).</summary>
    SucceededAllowedStatic,

    /// <summary>No prop_data: the model must be static or dynamic.</summary>
    NoData,

    /// <summary>prop_data that names a base propdata.txt does not have.</summary>
    BadData,
}

/// <summary>
/// The world's record of what recently made a noise — <c>CSoundEnt</c>'s list,
/// as the two managed entities that read and write it see it.
///
/// <para>
/// plan_gamedll_after.md Phase A3. <c>game/server/soundent.cpp</c> has left
/// <c>server_csharp.vpc</c>; the pool, the two lists and the sweep are
/// <c>SoundEnt</c> in SourceSharp.Server.
/// </para>
///
/// <para>
/// <b>THIS REPLACES TWO ENGINE SLOTS, and that is the finding rather than the
/// mechanism.</b> <c>SoundEntQuery</c> and <c>SoundEntInsert</c> were in
/// <c>IEngineSound</c> — the area whose test is "what the ENGINE owns: an
/// interface it exposes, or a callback it calls into". Neither was ever true of
/// them: the list was a game-DLL array in a game-DLL file, and the only reason
/// managed code had to ask the engine for it was that the array was on the
/// other side of the boundary. With the array here they are two ordinary
/// method calls and the slots are gone.
/// </para>
/// </summary>
public interface IServerSoundEnt
{
    /// <summary>
    /// Every active sound matching <paramref name="typeMask"/>, written into
    /// the caller's buffer in list order.
    ///
    /// <para>
    /// <b>The whole answer at once rather than a cursor</b>, which is the shape
    /// the radius queries already settled on: the caller wants the loudest and
    /// throws the rest away, and the throwing away is what decides the cost.
    /// </para>
    /// </summary>
    /// <returns>How many entries were written; never more than the span holds.</returns>
    int Query(SoundType typeMask, System.Span<HeardSound> into);

    /// <summary>
    /// Record one noise. <c>CSoundEnt::InsertSound</c>, and the same call the
    /// native shim makes for <c>FireBullets</c> and the blast radius.
    /// </summary>
    /// <param name="channel">
    /// <see cref="SoundChannel.Unspecified"/> allocates a new slot; any other
    /// value updates this owner's sound on that channel if it has one, which is
    /// what stops a repeating noise from filling the pool.
    /// </param>
    void Insert(int type, Vector origin, int volume, float duration,
                EntityHandle owner = default, int channel = SoundChannel.Unspecified,
                EntityHandle target = default);

    // ---- the SHIM's half ---------------------------------------------------
    //
    // The three below are shell_soundent.cpp's and nothing else calls them.
    // They are on this interface rather than on a second one because the pool
    // has ONE owner and splitting the interface would make it look as though it
    // had two — but the halves are labelled, because a map-facing entity
    // calling Shutdown() would be a bug that compiles.

    /// <summary>
    /// <c>CSoundEnt::InitSoundEnt</c>: clear the pool, chain it into the free
    /// list, reserve one never-expiring slot per client, and arm the first
    /// sweep a second out.
    /// </summary>
    void Initialize(int maxClients, float curTime);

    /// <summary>
    /// <c>CSoundEnt::ShutdownSoundEnt</c>. Idempotent: two callers on the way
    /// out of a level both spell it, and stock's was idempotent for the same
    /// reason.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// <c>CSound::Reset</c> on the reserved slot of the leaving client at
    /// <paramref name="clientEntIndex"/> (<c>CSoundEnt::ClientSoundIndex</c>).
    /// An index outside the pool is silently nothing.
    /// </summary>
    void ResetClientSoundOf(int clientEntIndex);
}

/// <summary>
/// Sound envelopes — <c>CSoundControllerImp</c>, as the shim sees it.
///
/// <para>
/// plan_gamedll_after.md Phase A3, second pair. <c>game/shared/soundenvelope.cpp</c>
/// has left <c>server_csharp.vpc</c>; the ramps, the sound list, the command
/// queue and the envelope-point expansion are <c>SoundController</c> in
/// SourceSharp.Server.
/// </para>
///
/// <para>
/// <b>EVERY METHOD HERE IS THE SHIM'S, and there is no map-facing half.</b>
/// That is the difference from <see cref="IServerSoundEnt"/>, whose two halves
/// are labelled because entities call one of them: nothing managed creates a
/// sound patch, because creating one needs a recipient filter, and a filter is
/// native. The interface exists so that each of the thirteen thunks resolves
/// exactly one subject.
/// </para>
///
/// <para>
/// <b>A patch is an <c>int</c> handle</b> and never a pointer. The native object
/// belongs to the shim, its address is never seen here, and a handle is never
/// reused — so a stale one resolves to nothing rather than to whichever patch
/// took its place.
/// </para>
/// </summary>
public interface IServerSoundController : ISoundPatchRamps
{
    /// <summary>A patch has been built natively and is now tracked here.</summary>
    void Create(int patch);

    /// <summary>
    /// Ramp the volume to zero, and either arm a shutdown time or queue a
    /// DESTROY for the same moment. The zero-time-and-destroy case never
    /// reaches here: it is a destroy, and a destroy frees a native object.
    /// </summary>
    void FadeOut(int patch, float deltaTime, bool destroyOnFadeout);

    /// <summary>
    /// Stop it now, drop its queued ramps, unlist it. The patch survives —
    /// <see cref="Destroy"/> is what forgets it.
    /// </summary>
    void Shutdown(int patch);

    /// <summary>Queue one ramp for <paramref name="executeDeltaTime"/> seconds from now.</summary>
    void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value);

    /// <summary>Drop every queued ramp belonging to one patch.</summary>
    void CommandClear(int patch);

    /// <summary>
    /// Expand a list of envelope POINTS into queued ramps and answer the total
    /// duration.
    /// </summary>
    /// <param name="points">
    /// Four floats per point — amplitudeMin, amplitudeMax, durationMin,
    /// durationMax — which is <c>envelopePoint_t</c>'s layout, flattened by the
    /// shim because this ABI does not carry game structs.
    /// </param>
    float PlayEnvelope(int patch, int command, System.ReadOnlySpan<float> points, int count);

    /// <summary>Shut every live patch down and empty both lists.</summary>
    void SystemReset();

    /// <summary>The envelope's current pitch.</summary>
    float GetPitch(int patch);

    /// <summary>The envelope's current volume.</summary>
    float GetVolume(int patch);
}

/// <summary>
/// <c>IServerGameEnts</c>' managed half — plan_server.md Phase 8b,
/// docs/plan-audit.md item C4.
///
/// <para>
/// <b>Its own subject rather than a member of <see cref="IServerLevel"/>.</b>
/// The engine asks these of the ENTITY LIST and not of the load or the frame,
/// and <c>IServerGameEnts</c> is a separate interface on the engine boundary
/// for exactly that reason. plan_server.md Part 4 lists three sequences on it —
/// <c>MarkEntitiesAsTouching</c>, <c>FreeContainingEntity</c> and
/// <c>CheckTransmit</c> — so this grows two more methods and is not a one-method
/// interface for long.
/// </para>
/// </summary>
public interface IServerEntities
{
    /// <summary>
    /// Which hierarchy answers for this client, and what its 3D-skybox area is.
    ///
    /// <para>
    /// <c>CServerGameEnts::CheckTransmit</c> asks this once per client per
    /// frame, before its walk, and RETURNS EARLY when it answers false — which
    /// is stock's <c>if ( !pRecipientEntity &amp;&amp; !bStandaloneRecipient )
    /// return;</c> with the disjunction moved to where a reader can see it.
    /// </para>
    ///
    /// <para>
    /// The walk itself stays native, by the plan's own rule: it runs once per
    /// edict per client per frame and decides nothing that is not already an
    /// <c>FL_EDICT_*</c> flag.
    /// </para>
    /// </summary>
    /// <param name="clientEdictIndex">
    /// The recipient's edict index. 0 is stock's fallback for "the engine named
    /// no client", not a sentinel for "none".
    /// </param>
    /// <param name="area">
    /// The visleaf area of the client's 3D skybox. <b>Written only when this
    /// returns true</b> — area 0 is a real area and cannot double as "no
    /// recipient".
    /// </param>
    bool SkyboxAreaForClient(int clientEdictIndex, out int area);

    /// <summary>
    /// The edict this client is sent every snapshot whatever its PVS says, or
    /// -1 for none: stock <c>CBasePlayer::ShouldTransmit</c>'s first arm
    /// ("allow me to introduce myself to, err, myself"), the recipient's own
    /// player, ahead of the EF_NODRAW / observer rule and the PVS test.
    /// Asked by <c>CheckTransmit</c> only after <see cref="SkyboxAreaForClient"/>
    /// found the recipient.
    /// </summary>
    int TransmitAlwaysForClient(int clientEdictIndex);
}

/// <summary>
/// <c>IServerGameClients</c>' managed half — plan_server.md Phase 7,
/// docs/plan-audit.md item C5.
///
/// <para>
/// <b>Its own subject rather than a member of <see cref="IServerEntities"/>.</b>
/// The engine asks these about ONE PLAYER, not about the entity list, and
/// <c>IServerGameClients</c> is a separate interface on the engine boundary for
/// exactly that reason — three <c>EXPOSE_</c> lines point at it independently
/// of <c>IServerGameEnts</c>. C5 lists eight sequences on it, so this is the
/// first method of several rather than a one-method interface.
/// </para>
///
/// <para>
/// <b>What is deliberately NOT here.</b> <c>GetPlayerState</c> hands the ENGINE
/// a <c>CPlayerState *</c> that it reads and writes through for the life of the
/// client — <c>fixangle</c>, <c>v_angle</c> and the network name — so there is
/// no value to return and nothing for a decision to be about. It stays native
/// and this paragraph is the record of why, not an omission.
/// </para>
/// </summary>
public interface IServerClients
{
    /// <summary>
    /// Which hierarchy answers for this client's ears, and where they are.
    ///
    /// <para>
    /// <c>CServerGameClients::ClientEarPosition</c> asks this once — the engine
    /// calls it to place the sound listener, never inside a loop over entities
    /// — and <c>Assert(0)</c>s and writes <c>vec3_origin</c> when it answers
    /// false, which is stock's own miss path.
    /// </para>
    ///
    /// <para>
    /// <b>STANDALONE FIRST, and that is not
    /// <see cref="IServerEntities.SkyboxAreaForClient"/>'s order.</b> The two
    /// bodies were written the two ways round: <c>CheckTransmit</c> only
    /// reached for the standalone player when <c>GetContainingEntity</c> came
    /// back NULL, and <c>ClientEarPosition</c> reached for it first. Both are
    /// reproduced. Here the difference is load-bearing rather than cosmetic —
    /// the SDK arm's miss path is an assert that Phase 10 recorded as FIRING
    /// once the managed weapon machine began emitting sounds on standalone
    /// players, so asking the SDK hierarchy first would put it back.
    /// </para>
    /// </summary>
    /// <param name="clientEdictIndex">
    /// The client's edict index, or 0 for a NULL edict — which claims neither
    /// hierarchy, so this answers false and the caller asserts, exactly as the
    /// old body did.
    /// </param>
    /// <param name="ears">
    /// Where the listener goes. <b>Written only when this returns true</b> —
    /// the world origin is a real position and cannot double as "no client",
    /// because the caller asserts before writing it.
    /// </param>
    bool EarPositionForClient(int clientEdictIndex, out Vector ears);

    /// <summary>
    /// ClientSetupVisibility's area portal walk for one client (plan_server_native_v2
    /// T4d): every portal's server answer to the engine, the client's portal bits
    /// into <paramref name="portalBits"/>. -1, or a portal number too large for the
    /// bits, which the caller raises as an Error.
    /// </summary>
    int AreaPortalVisibility(Vector origin, float fovDistanceAdjustFactor, Span<byte> portalBits);
}

/// <summary>
/// The template store, as the ONE thing outside SourceSharp.Server that reaches
/// it sees it — the map parse, which claims a template long before any managed
/// caller has a name for it.
///
/// <para>
/// plan_server_all.md Wave 1 / plan-audit D11. <c>game/server/TemplateEntities.cpp</c>
/// has left <c>server_csharp.vpc</c>; the store, the entity-I/O rewriting and
/// the instance counter are <c>TemplateStore</c> in SourceSharp.Server.
/// </para>
///
/// <para>
/// <b>One method, and it is the direction the store cannot arrange for
/// itself.</b> Everything else a template is asked — reconnect this group,
/// schedule these precaches, give me the text for this instance — is a managed
/// caller talking to a managed object and needs no contract. This is the other
/// way round: <c>MapEntity_ParseAllEntities</c> is standing inside the parse
/// with an entity's keyvalue text in its hand and no managed caller anywhere
/// above it, and that text has to reach the store before the entity spawns.
/// </para>
/// </summary>
public interface IServerTemplates
{
    /// <summary>
    /// Stores one entity's map text as a template and answers its index.
    ///
    /// <para>
    /// Stock's <c>Templates_AddNamed</c>, and the two calls that reach it are
    /// stock's two: the parse's own <c>IsTemplate()</c> branch, and the sweep a
    /// <c>point_template</c> makes over the entities it named.
    /// </para>
    /// </summary>
    /// <param name="name">
    /// The entity's targetname. <b>An entity with no name is refused with
    /// -1</b>, which is stock's answer and stock's reason: a template is looked
    /// up by targetname and one without a name could never be found again.
    /// </param>
    /// <param name="className">Only ever used to word the warning above.</param>
    /// <param name="mapData">
    /// The entity's block of the BSP's entity lump, verbatim. Copied here — the
    /// caller's buffer is the map lump and does not outlive the parse.
    /// </param>
    int Add(string? name, string? className, byte[] mapData);
}
