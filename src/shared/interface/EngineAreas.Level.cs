namespace SourceSharp.Abi;

/// <summary>
/// The irreducible native steps a map load is made of.
///
/// <para>
/// <b>An area of steps, not of capabilities, and that is what makes it
/// different from every other <c>IEngine*</c> here.</b> The rest of them answer
/// questions or do one thing an entity asked for. These seven exist so that
/// <c>LevelInit</c>'s ORDER can live in C#: plan_gamedll.md Part 4's rule is
/// that native exposes one <c>extern "C"</c> per irreducible step and C# owns
/// the order, the conditions and the early returns, and <i>a step is never a
/// decision</i>. Every member below is one line of
/// <c>CServerGameDLL::LevelInit</c> and decides nothing.
/// </para>
///
/// <para>
/// <b>There is deliberately no <c>LevelInit()</c> member.</b> One member that
/// ran all seven would be the same C++ sequence with a C# name on it — the
/// order would stop being readable, the two refused save-cluster arms would go
/// back into the native branch they came from, and nothing here would be
/// testable without an engine.
/// </para>
///
/// <para>
/// Crossing the boundary seven times per map load rather than once is the
/// stated trade. It happens once per map. Part 13 is the one pass allowed to
/// reduce crossings and it runs after the whole thing works.
/// </para>
///
/// <para>
/// <b>Server only in effect.</b> The native slots behind these are filled on
/// the server and left null on the client, where there is no <c>LevelInit</c>
/// to have steps of. The interface is still registered on both sides, like
/// every other area, because an area that existed on one side would make an
/// entity's constructor tell you which side it was running on.
/// </para>
/// </summary>
public interface IEngineLevel
{

    /// <summary>
    /// Builds and spawns the world from the map's <c>worldspawn</c> block, given
    /// as its key/value pairs in block order (<c>classname</c> included). THE
    /// first entity of every level: without it nothing precaches and the first
    /// trace faults inside the engine.
    ///
    /// <para>
    /// <b>The pairs arrive parsed.</b> plan_server_novalve.md P5: this replaced
    /// <c>ParseAllEntities</c>, which handed native the residue as TEXT for it to
    /// walk. Finding the block is a decision about the text, so it is
    /// <c>MapSpawner</c>'s, over the lump it has already split; native
    /// creates the world, hands each pair to <c>CWorld::KeyValue</c> and spawns
    /// it. The old slot keeps its index and refuses.
    /// </para>
    /// </summary>
    /// <returns>
    /// True when the world was created and spawned; false when the factory gave
    /// nothing or the object did not claim the world singleton.
    /// </returns>
    bool SpawnWorld(IReadOnlyList<KeyValuePair<string, string>> keys);

    /// <summary>
    /// Precaches the contents of these templates. After the parse.
    ///
    /// <para>
    /// <b>Still ONE step, and still in stock's position.</b> The list it drains
    /// moved to managed code with the template store (plan_server_all.md Wave
    /// 1); what crosses is the text of each template rather than an instruction
    /// to go and find it. tools/gate5's recorded level-load sequence is
    /// unchanged by that, and has to be: a step that split into N would be a
    /// reordering nothing else in the tree can see.
    /// </para>
    ///
    /// <para>
    /// An empty list still makes the call, because the step is the record of
    /// having reached this point in the load and not of having had work to do.
    /// </para>
    /// </summary>
    void PrecachePointTemplates(IReadOnlyList<TemplateSpawn> templates);

    /// <summary>Applies this map's low-violence setting.</summary>
    void SetLowViolence(string mapName);

    /// <summary>Loads the MOTD into the info-panel string table.</summary>
    void LoadMessageOfTheDay();

    // ---- plan_gamedll.md Part 4, Phase 7 -------------------------------------
    //
    // ServerActivate's and LevelShutdown's steps, on the same terms as the seven
    // above: one member per irreducible step, and there is deliberately no
    // ServerActivate() or LevelShutdown() member that runs them in order.
    //
    // LevelShutdown is the case that makes the rule obvious rather than
    // doctrinaire. Stock's body is nine calls and the SEQUENCE is the entire
    // content of it -- steam context, pre-entity systems, the sound entity, the
    // entity list, post-entity systems, the precache close, the particle
    // dictionary. A single member would have crossed the boundary once and
    // carried across the one thing worth reading.

    /// <summary>Opens or closes the precache window.</summary>
    void SetPrecacheAllowed(bool allowed);

    /// <summary>
    /// Whether the precache window is open now: what <see cref="SetPrecacheAllowed"/>
    /// and the world's own precache wrote (plan_server_native_v2 T4a). The
    /// sound precache warns "Late precache of" when it is shut.
    /// </summary>
    bool IsPrecacheAllowed();

    /// <summary>
    /// Removes every entity on the parallel hierarchy — plan_gamedll_after.md
    /// Phase A3, the fifth "C" system. <c>gEntList</c> cannot see them, so
    /// without this an entity from the previous map survives into the next one.
    /// </summary>
    void ShutdownStandaloneEntities();

    /// <summary>
    /// Drops the AI sound entity. Its own step, because stock's comment is that
    /// this pointer "is going away now and is corrupting memory on level
    /// transitions/restarts" — so it has to go before the list is cleared.
    /// </summary>
    void ShutdownSoundEnt();

    /// <summary>
    /// Clears the entity list. It sits between the pre- and post-entity
    /// shutdown passes, which is what those two names are FOR.
    /// </summary>
    void ClearEntityList();

    // ---- plan_gamedll_after.md Phase A3 -------------------------------------
    //
    // CPropData's two remaining native steps, HERE rather than in an area of
    // their own, because they are what they look like: one runs at level init
    // pre-entity and one at level shutdown post-entity, which is what every
    // other member of this interface is.

    /// <summary>
    /// <c>engine-&gt;LoadGameState( mapName, createPlayers )</c> — the call that
    /// makes a saved world come back, and the one nothing in this build made.
    ///
    /// <para>
    /// <b>THE GAME DLL IS WHAT DRIVES A LOAD.</b> <c>LoadGameState</c> is
    /// <c>IVEngineServer</c>'s (<c>public/eiface.h:280</c>) and stock calls it
    /// from <c>CServerGameDLL::LevelInit</c>'s <c>loadGame</c> arm. That call
    /// is what drives the engine's load-game path:
    /// <c>ReadRestoreHeaders</c>, the <c>Save Header</c> and <c>LIGHTSTYLE</c>
    /// reads, and <c>Restore</c>, in that order. Stage 1 measured all three
    /// never firing; the reason was never that the engine had nothing to hand
    /// them.
    /// </para>
    /// </summary>
    /// <returns>
    /// Whether the engine restored the game state. FALSE is a load that found
    /// no level file or could not read it — what to do about that is the
    /// caller's, which is why this is a step and not a decision.
    /// </returns>
    bool LoadSaveGameState(string mapName, bool createPlayers);

    /// <summary>
    /// <c>gpGlobals-&gt;eLoadType = how</c>: stock's first line in each arm of
    /// <c>CServerGameDLL::LevelInit</c>. Which arm is C#'s
    /// (<c>MapLoadArms</c>, plan_server_native_v2 T4e); this writes it.
    /// </summary>
    void SetLoadType(MapLoadType how);

    // ---- plan_server_native_v2 T4e: the two native systems' stages -------
    //
    // kServerSystems[] and its walks are managed's now (ServerSystems): each
    // member below is one stage of one native system, which native carries
    // out under the model-cache lock its walk took.

    /// <summary><c>CPhysicsHook::Init</c>: the three vphysics interfaces, by
    /// name. False refuses the server's start-up.</summary>
    bool PhysicsInit();

    /// <summary><c>CPhysicsHook::LevelInitPreEntity</c>: the level's environment.</summary>
    void PhysicsLevelInitPreEntity();

    /// <summary><c>CPhysicsHook::LevelInitPostEntity</c>: the physics unpaused.</summary>
    void PhysicsLevelInitPostEntity();

    /// <summary><c>CPhysicsHook::LevelShutdownPreEntity</c>: quick delete on.</summary>
    void PhysicsLevelShutdownPreEntity();

    /// <summary><c>CPhysicsHook::LevelShutdownPostEntity</c>: the environment destroyed.</summary>
    void PhysicsLevelShutdownPostEntity();

    /// <summary><c>CGameStringPool::LevelShutdownPostEntity</c>: every pooled string freed.</summary>
    void StringPoolLevelShutdownPostEntity();

    /// <summary>
    /// Every entity control-block slot closed: the managed row's native half
    /// after the managed level's end.
    /// </summary>
    void ResetEntitySlots();
}

/// <summary>
/// Whole-file reads off a named search path — plan_server_novalve.md P2.1, for
/// the parsers that moved out of server.so (the particle manifests first).
/// Its own interface and not <see cref="IEngineLevel"/>'s, although both are
/// <c>IShimLevel</c> slots natively: IEngineLevel is the level load's ordered
/// STEPS, and a file read is not a step. Deliberately NOT named IEngine*: the
/// naming rule enrols every IEngineFoo as an area bound to an EngineFoo, and
/// this is a second face of <c>EngineLevel</c>, not an area.
/// </summary>
public interface ILevelFiles
{
    /// <summary>
    /// A whole file off one named search path, as bytes — plan_server_novalve.md
    /// P2.1. <c>"GAME"</c> and <c>"BSP"</c> are the two the particle manifests
    /// read (a map's own manifest can live in the BSP's pak lump).
    /// </summary>
    /// <returns>
    /// The file, or <c>null</c> when it is not on that path. An empty array is
    /// a real, empty file. Never a truncated read: a file that changes size
    /// between the size and the read answers <c>null</c>.
    /// </returns>
    byte[]? ReadFileOnPath(string path, string pathId);
}
