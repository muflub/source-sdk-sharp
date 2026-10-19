// An engine a test can hand to an entity.
//
// THE REASON THE INTERFACES EXIST AT ALL. Before them, an entity in a test
// called Engine directly, there was no engine in the process, and every call
// returned 0 or an empty string — silently. So a test could assert what an
// entity COMPUTED but never what it asked the engine to DO, and a check written
// against an engine call could not fail. This project has shipped three such
// checks; each passed happily for weeks.
//
// It RECORDS by default rather than throwing, and that choice is worth stating
// because the other one is tempting. A double that throws on anything
// un-stubbed tells you about the calls you FORGOT; a double that records tells
// you about the calls that HAPPENED, which is the assertion actually wanted —
// "the trigger asked the engine to hurt whoever walked in, for 25, of this
// type". Strict is available for the rarer and stricter question, "and it asked
// for nothing else".

using System.Text;

namespace SourceSharp.Abi;

/// <summary>One call an entity made, as the log records it.</summary>
public readonly record struct EngineCall(string Member, object?[] Args)
{
    /// <summary>An argument by position, or null if it was not recorded.</summary>
    public object? Arg(int index) => index >= 0 && index < Args.Length ? Args[index] : null;

    /// <summary>An argument by position, typed. Default if it is absent or another type.</summary>
    public T ArgAs<T>(int index) => Arg(index) is T value ? value : default!;

    public override string ToString()
        => $"{Member}({string.Join(", ", Args.Select(a => a?.ToString() ?? "null"))})";
}

/// <summary>
/// Raised by a <see cref="RecordingEngine"/> in strict mode, for a call whose
/// answer nothing stubbed.
/// </summary>
public sealed class UnstubbedEngineCallException : Exception
{
    public UnstubbedEngineCallException(string member)
        : base($"the engine was asked for '{member}' and nothing stubbed an answer; " +
               $"call Returns(\"{member}\", ...) or clear Strict")
    {
    }
}

/// <summary>
/// Every engine area, faked: records what was asked and answers what it was told
/// to.
///
/// <para>
/// Implemented EXPLICITLY, every member, which is not a style choice. Three
/// areas declare <c>Count()</c> and two declare <c>Destroy(uint)</c> — springs
/// and constraints are separate tables precisely because the engine keeps them
/// separate — so a single implicit implementation would serve all of them and a
/// test could not tell which table it had just counted. Explicit members give
/// each its own entry in the log.
/// </para>
///
/// <para>
/// A span argument is recorded as its LENGTH. It cannot be stored — a ref
/// struct does not box — and the length is the part a test asserts anyway.
/// </para>
/// </summary>
public sealed class RecordingEngine :
    IGameClock,
    IEngineConsole,
    IEngineHost,
    IEnginePrecache,
    IEngineGameEvents,
    IEngineNetworking,
    IUserMessageSender,
    IEngineScripted,
    IEngineEntities,
    IEngineEntityNames,
    IEngineOverlaps,
    IEngineEntityPhysics,
    IEngineEntityRender,
    IEngineDamage,
    IEngineGameRulesPolicy,
    IEngineCollisionRules,
    IEngineBrushMovement,
    IEnginePlayers,
    IEnginePlayerMovement,
    IEngineWeapons,
    IEngineHud,
    IEngineDiagnostics,
    IEngineWorldQuery,
    IEngineSound,
    IEngineEffects,
    IEngineVisibility,
    IEngineEnvironment,
    IEngineFrame,
    IEngineLevel,
    ILevelFiles,
    IEngineStringTables,
    IEngineFiles
{

    private readonly List<EngineCall> _calls = new();
    private readonly Dictionary<string, object?> _answers = new(StringComparer.Ordinal);
    // "Clock.Now" is let through Strict by default (plan_engineapi_vtables B5):
    // the registry stamps every output, think and save with its container's
    // clock now, where it used to read the static clock -- which answered 0 with
    // no engine behind it, and which no test ever had to stub. A read has no
    // effect to catch; a test that cares about the time still Returns() it.
    private readonly HashSet<string> _ignored = new(StringComparer.Ordinal) { "Clock.Now" };

    /// <summary>Every call made, in order.</summary>
    public IReadOnlyList<EngineCall> Calls => _calls;

    /// <summary>
    /// Whether a call with no stubbed answer is an error.
    ///
    /// <para>
    /// ON by default, and that is the whole point. A double that answers zero
    /// to anything nobody stubbed has rebuilt the silence it was built to
    /// replace: the test passes, the entity read a value the test never chose,
    /// and nothing says so. Throwing makes every test state out loud which
    /// engine reads its entity actually depends on. The noise is the
    /// information.
    /// </para>
    ///
    /// <para>
    /// Only calls that ASK are affected. A void call is the entity telling the
    /// engine something and has no answer to stub, so it is always recorded and
    /// never refused — which is why a test asserting what an entity did needs no
    /// setup at all, and only a test whose entity READS the world has to say
    /// what it reads.
    /// </para>
    ///
    /// <para>
    /// Clear it for a test that genuinely does not care, and say why where you
    /// clear it.
    /// </para>
    /// </summary>
    public bool Strict { get; set; } = true;

    /// <summary>
    /// A strict double with the universal reads stubbed and nothing else
    /// (<c>TestServices.Mock</c> explains which and why). It lives here, not in
    /// the suites' helpers, because the doubles are built once per side
    /// (plan_shim_areas_split phase 3): a suite that drives both sides asks each
    /// side's build for its own default the same way.
    /// </summary>
    public static RecordingEngine Default()
        => new RecordingEngine()
            .Returns("Entities.GetSpawnFlags", 0)
            .Returns("Entities.GetName", string.Empty)
            .Returns("Entities.GetClassName", string.Empty)
            .Returns("Host.ActiveSide", Side.Server)
            // The fifth universal read (plan_abi_hotreload D8): every entity
            // asks whether there is a native side before saving or restoring
            // one (ScriptedEntity.HasNativeSide). A double is not a running
            // engine, so no; a fact that wants the native section says so.
            .Returns("Host.IsInitialized", false);

    /// <summary>Stubs the answer to a member, or to one of its out parameters.</summary>
    public RecordingEngine Returns(string member, object? value)
    {
        _answers[member] = value;
        return this;
    }

    /// <summary>
    /// Stubs a member that answers something DIFFERENT each call, by handing it
    /// a factory instead of a value.
    ///
    /// <para>
    /// The case this exists for is a registration handle. Native gives out a
    /// distinct one per registration, and a double answering the same number to
    /// every call is indistinguishable from that only while a test registers one
    /// thing. <c>TempEntRegister</c> stubbed at a constant 5 was fine at three
    /// temp entities and ambiguous at forty-four: every class held handle 5, so
    /// looking one up by handle answered whichever had been published last, and
    /// three tests that had been asserting a lookup started asserting
    /// registration order instead — passing for the wrong reason first, then
    /// failing when a later lane added a class.
    /// </para>
    /// </summary>
    public RecordingEngine ReturnsEachCall(string member, Func<object?> factory)
    {
        _answers[member] = factory;
        return this;
    }

    /// <summary>
    /// Says that this test does not care what a member answers: it gets the
    /// type's default rather than a refusal.
    ///
    /// <para>
    /// The honest form of the escape hatch, and better than stubbing a value
    /// the test does not use. "I ignore what the world query returns" is a
    /// statement a reader can check; <c>Returns("World.TraceLine", default)</c>
    /// looks like a value somebody chose, and a later reader cannot tell
    /// whether it mattered.
    /// </para>
    ///
    /// <para>
    /// It is still per-member and still written down. Clearing
    /// <see cref="Strict"/> wholesale is the thing this exists to avoid.
    /// </para>
    /// </summary>
    /// <summary>
    /// What <c>WorldQuery.EntitiesInSphere</c> writes into the caller's span,
    /// as native does; the count answered is how many fitted. Without it the
    /// span is left as it came, and its elements are
    /// <see cref="EntityHandle.None"/>.
    /// </summary>
    public RecordingEngine FillsSphere(params EntityHandle[] found)
    {
        _sphere = found;
        return this;
    }

    private EntityHandle[]? _sphere;

    private int EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into)
    {
        // Or stubbed as "WorldQuery.EntitiesInSphere.into": an EntityHandle[],
        // or a factory for one that can read the call just recorded (the
        // pusher's facts answer per box).
        if (_sphere is null && _answers.ContainsKey("WorldQuery.EntitiesInSphere.into"))
        {
            RecordVoid("WorldQuery.EntitiesInSphere", center, radius, into.Length);
            EntityHandle[] found = Value<EntityHandle[]>("WorldQuery.EntitiesInSphere.into") ?? Array.Empty<EntityHandle>();
            int count = Math.Min(found.Length, into.Length);
            found.AsSpan(0, count).CopyTo(into);
            return count;
        }

        int answered = Record<int>("WorldQuery.EntitiesInSphere", center, radius, into.Length);
        if (_sphere is null)
            return answered;

        int n = Math.Min(_sphere.Length, into.Length);
        _sphere.AsSpan(0, n).CopyTo(into);
        return n;
    }

    public RecordingEngine Ignores(params string[] members)
    {
        foreach (var member in members)
            _ignored.Add(member);

        return this;
    }

    /// <summary>
    /// Runs <paramref name="observe"/> at the moment a member is called, after
    /// the call is recorded.
    ///
    /// <para>
    /// For state that rides BESIDE a call rather than in its arguments — a
    /// scope the caller holds open around it (DamageHitGroups,
    /// DamageInflictors). The log can say the call happened; only a look taken
    /// during the call can say what was open while it did.
    /// </para>
    /// </summary>
    public RecordingEngine WhenCalled(string member, Action<EngineCall> observe)
    {
        _observers[member] = observe;
        return this;
    }

    private readonly Dictionary<string, Action<EngineCall>> _observers = new(StringComparer.Ordinal);

    /// <summary>Forgets every call, keeping the stubbed answers.</summary>
    public void Clear() => _calls.Clear();

    /// <summary>Whether a member was called at all.</summary>
    public bool Called(string member) => _calls.Any(c => c.Member == member);

    /// <summary>How many times it was called.</summary>
    public int CountOf(string member) => _calls.Count(c => c.Member == member);

    /// <summary>The last call to a member, or null if there was none.</summary>
    public EngineCall? Last(string member)
    {
        for (int i = _calls.Count - 1; i >= 0; i--)
        {
            if (_calls[i].Member == member)
                return _calls[i];
        }

        return null;
    }

    /// <summary>Every call to a member, in order.</summary>
    public IEnumerable<EngineCall> AllOf(string member) => _calls.Where(c => c.Member == member);

    /// <summary>
    /// The whole log, one call per line.
    ///
    /// For the failure message rather than for an assertion: a test that
    /// expected a call and did not get it is nearly always looking at an entity
    /// that took a different branch, and the branch it took is visible here.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder();

        foreach (var call in _calls)
            text.Append(call).Append('\n');

        return text.ToString();
    }

    private void RecordVoid(string member, params object?[] args)
        => Add(new EngineCall(member, args));

    /// <summary>
    /// A call a side's own double (one this class cannot implement, because
    /// its contract lives in a side assembly) puts in this ordered log.
    /// </summary>
    public void RecordCall(string member, params object?[] args) => RecordVoid(member, args);

    /// <summary>
    /// A call recorded and answered as the double's own members are: what a
    /// recording stand-in for a SERVER table (RecordingJointTables) uses, since
    /// those tables are not engine areas and this assembly cannot see them.
    /// </summary>
    public T RecordAnswer<T>(string member, params object?[] args) => Record<T>(member, args);

    /// <summary>A scripted answer read without recording a call: an out value beside a recorded one.</summary>
    public T Answer<T>(string key) => Value<T>(key);

    private T Record<T>(string member, params object?[] args)
    {
        Add(new EngineCall(member, args));
        return Value<T>(member);
    }

    private void Add(EngineCall call)
    {
        _calls.Add(call);
        if (_observers.TryGetValue(call.Member, out var observe))
            observe(call);
    }

    private T Value<T>(string key)
    {
        if (_answers.TryGetValue(key, out var answer))
        {
            // A factory is called per lookup; a value is the same every time.
            if (answer is Func<object?> each)
                answer = each();

            return answer is T typed ? typed : default!;
        }

        if (Strict && !_ignored.Contains(key))
            throw new UnstubbedEngineCallException(key);

        return default!;
    }


    /// <summary>
    /// The clock, which is not an engine AREA but is wanted by the same tests
    /// for the same reason: one double covering everything a test's entity can
    /// reach means one ordered log of the whole conversation.
    /// </summary>
    float IGameClock.Now => Record<float>("Clock.Now");

    //-------------------------------------------------------------------------
    // The areas. Generated shape, one line per member where one line will do.
    //-------------------------------------------------------------------------

    // Console
    void IEngineConsole.ExecuteServerCommand(string command) => RecordVoid("Console.ExecuteServerCommand", command);
    string? IEngineConsole.FindConVarValue(string name) => Record<string?>("Console.FindConVarValue", name);
    string IEngineConsole.GetConVarValue(int id) => Record<string>("Console.GetConVarValue", id);
    void IEngineConsole.Msg(string message) => RecordVoid("Console.Msg", message);
    int IEngineConsole.RegisterConCommand(string name, string help, ConVarFlags flags)
        => Record<int>("Console.RegisterConCommand", name, help, flags);
    int IEngineConsole.RegisterConVar(string name, string defaultValue, string help, int flags, float? min, float? max) => Record<int>("Console.RegisterConVar", name, defaultValue, help, flags, min, max);
    void IEngineConsole.SendClientCommand(EntityHandle player, string command) => RecordVoid("Console.SendClientCommand", player, command);
    void IEngineConsole.SetConVarValue(int id, string value) => RecordVoid("Console.SetConVarValue", id, value);
    void IEngineConsole.SetConVarValueByName(string name, string value) => RecordVoid("Console.SetConVarValueByName", name, value);
    int IEngineConsole.Snapshot() => Record<int>("Console.Snapshot");
    bool IEngineConsole.SnapshotEntry(int index, out NativeConsoleEntry entry)
    {
        entry = Value<NativeConsoleEntry>("Console.SnapshotEntry.entry");
        return Record<bool>("Console.SnapshotEntry", index);
    }
    void IEngineConsole.Warning(string message) => RecordVoid("Console.Warning", message);

    // Host
    // The abstract environment (plan_gamedll.md Part 0a #1). STRICT like every
    // other area: a test whose object asks the engine for an interface, or reads
    // curtime out of the live globals, has to say so.
    nint IEngineEnvironment.GetInterface(string name) => Record<nint>("Environment.GetInterface", name);
    Globals IEngineEnvironment.Globals => Record<Globals>("Environment.Globals");

    Side IEngineHost.ActiveSide => Record<Side>("Host.ActiveSide");
    float IEngineHost.CurrentTime => Record<float>("Host.CurrentTime");
    void IEngineHost.EndRound() => RecordVoid("Host.EndRound");
    bool IEngineHost.IsInitialized => Record<bool>("Host.IsInitialized");
    string IEngineHost.ManagedDirectory => Record<string>("Host.ManagedDirectory");
    float IEngineHost.RandomFloat(float min, float max) => Record<float>("Host.RandomFloat", min, max);
    int IEngineHost.RandomInt(int min, int max) => Record<int>("Host.RandomInt", min, max);
    bool IEngineHost.IsDedicatedServer => Record<bool>("Host.IsDedicatedServer");
    bool IEngineHost.SteamGameServerReacquire() => Record<bool>("Host.SteamGameServerReacquire");
    void IEngineHost.SteamGameServerClear() => RecordVoid("Host.SteamGameServerClear");
    void IEngineHost.SteamGameServerRequestGameplayStats() => RecordVoid("Host.SteamGameServerRequestGameplayStats");
    bool IEngineHost.SteamGameServerLoggedOn() => Record<bool>("Host.SteamGameServerLoggedOn");

    // Level -- plan_gamedll.md Part 4's nine steps, seven of which live here.
    // The lump is recorded as its LENGTH, like every other span argument: a ref
    // struct does not box, and the length is what a test asserts anyway.
    void IEngineLevel.ResetServerTime() => RecordVoid("Level.ResetServerTime");
    void IEngineLevel.ParseParticleManifest() => RecordVoid("Level.ParseParticleManifest");
    void IEngineLevel.ParseParticleManifestForMap(string mapName) => RecordVoid("Level.ParseParticleManifestForMap", mapName);
    // THE BYTES AND NOT THE LENGTH -- plan_entity.md step 2d. What reaches this
    // step is no longer the whole lump: MapSpawner keeps the blocks whose class
    // the managed registry owns and hands down the residue, so a fact about this
    // call has to be able to say WHICH blocks arrived. A length cannot.
    bool IEngineLevel.SpawnWorld(IReadOnlyList<KeyValuePair<string, string>> keys) => Record<bool>("Level.SpawnWorld", keys.ToArray());
    void IEngineLevel.PrecachePointTemplates(IReadOnlyList<TemplateSpawn> templates) => RecordVoid("Level.PrecachePointTemplates", templates);
    void IEngineLevel.SetLowViolence(string mapName) => RecordVoid("Level.SetLowViolence", mapName);
    void IEngineLevel.LoadMessageOfTheDay() => RecordVoid("Level.LoadMessageOfTheDay");

    // Phase 7 -- ServerActivate's and LevelShutdown's steps. Same shape: the
    // recording is the CALL LIST, which is what lets one fact assert an order
    // spanning two areas as a single sequence.
    int IEngineLevel.ResetDeleteList() => Record<int>("Level.ResetDeleteList");
    void IEngineLevel.ActivateAllEntities() => RecordVoid("Level.ActivateAllEntities");
    void IEngineLevel.InitPostEntitySystems() => RecordVoid("Level.InitPostEntitySystems");
    void IEngineLevel.SetPrecacheAllowed(bool allowed) => RecordVoid("Level.SetPrecacheAllowed", allowed);
    void IEngineLevel.ShutdownPreClearSteamSystems() => RecordVoid("Level.ShutdownPreClearSteamSystems");
    void IEngineLevel.ClearSteamAPIContext() => RecordVoid("Level.ClearSteamAPIContext");
    void IEngineLevel.ShutdownPreEntitySystems() => RecordVoid("Level.ShutdownPreEntitySystems");
    void IEngineLevel.ShutdownStandaloneEntities() => RecordVoid("Level.ShutdownStandaloneEntities");
    void IEngineLevel.ShutdownSoundEnt() => RecordVoid("Level.ShutdownSoundEnt");
    void IEngineLevel.ClearEntityList() => RecordVoid("Level.ClearEntityList");
    void IEngineLevel.ShutdownPostEntitySystems() => RecordVoid("Level.ShutdownPostEntitySystems");
    void IEngineLevel.UncacheAllParticleSystems() => RecordVoid("Level.UncacheAllParticleSystems");
    void IEngineLevel.RecreateParticleDictionary() => RecordVoid("Level.RecreateParticleDictionary");

    // Phase A3 -- CPropData's two remaining native steps.
    void IEngineLevel.PropDataLoadFile() => RecordVoid("Level.PropDataLoadFile");
    void IEngineLevel.PropDataClearFile() => RecordVoid("Level.PropDataClearFile");
    bool IEngineLevel.LoadSaveGameState(string mapName, bool createPlayers) => Record<bool>("Level.LoadSaveGameState", mapName, createPlayers);
    byte[]? ILevelFiles.ReadFileOnPath(string path, string pathId) => Record<byte[]?>("LevelFiles.ReadFileOnPath", path, pathId);

    // Files -- plan_gamedll_after.md Phase A3. A test that does not stub a read
    // gets the strict double's refusal by NAME, which is the answer that says
    // "this participant went looking for a game script" out loud.
    int IEngineFiles.GameFileSize(string path) => Record<int>("Files.GameFileSize", path);
    string? IEngineFiles.ReadGameFile(string path) => Record<string?>("Files.ReadGameFile", path);
    byte[]? IEngineFiles.ReadGameFileBytes(string path) => Record<byte[]?>("Files.ReadGameFileBytes", path);

    // Only the GAME path, as IEngineFiles' default body answered before
    // plan_shim_areas_split phase 4 (interface assemblies hold no bodies).
    byte[]? IEngineFiles.ReadFileOnPath(string path, string pathId)
        => string.Equals(pathId, "GAME", StringComparison.Ordinal) ? ((IEngineFiles)this).ReadGameFileBytes(path) : null;

    // Frame -- plan_gamedll.md Part 4's fifteen steps. The recording is the
    // CALL LIST, which is what lets one fact assert the frame's order as a
    // sequence rather than as a set.
    bool IEngineFrame.IsSimulatingOnAlternateTicks() => Record<bool>("Frame.IsSimulatingOnAlternateTicks");
    int IEngineFrame.TickCount() => Record<int>("Frame.TickCount");
    float IEngineFrame.GetFrameTime() => Record<float>("Frame.GetFrameTime");
    void IEngineFrame.SetFrameTime(float frameTime) => RecordVoid("Frame.SetFrameTime", frameTime);
    void IEngineFrame.SetServerCurTime() => RecordVoid("Frame.SetServerCurTime");
    void IEngineFrame.ServerSystemsPreThink() => RecordVoid("Frame.ServerSystemsPreThink");
    void IEngineFrame.PhysicsRunThinkFunctions(bool simulating) => RecordVoid("Frame.PhysicsRunThinkFunctions", simulating);
    void IEngineFrame.ServerSystemsPostThink() => RecordVoid("Frame.ServerSystemsPostThink");
    void IEngineFrame.ServiceEventQueue() => RecordVoid("Frame.ServiceEventQueue");
    void IEngineFrame.UpdateAllClientData() => RecordVoid("Frame.UpdateAllClientData");
    void IEngineFrame.EndGameFrame() => RecordVoid("Frame.EndGameFrame");
    void IEngineFrame.FireNetworkPropertyEvents() => RecordVoid("Frame.FireNetworkPropertyEvents");
    void IEngineFrame.GamestatsUpdateConnection() => RecordVoid("Frame.GamestatsUpdateConnection");
    void IEngineFrame.DrawAllDebugOverlays() => RecordVoid("Frame.DrawAllDebugOverlays");
    void IEngineFrame.ServerSystemsPreClientUpdate() => RecordVoid("Frame.ServerSystemsPreClientUpdate");
    void IEngineFrame.StandalonePlayerCommands() => RecordVoid("Frame.StandalonePlayerCommands");
    void IEngineFrame.StandaloneUpdate() => RecordVoid("Frame.StandaloneUpdate");
    void IEngineFrame.StandaloneFinish() => RecordVoid("Frame.StandaloneFinish");
    int IEngineFrame.DeliverToSdkEntities(string target, string input, string? value)
        => Record<int>("Frame.DeliverToSdkEntities", target, input, value);

    // String tables -- plan_gamedll_after.md Phase A8's mod-declared route.
    //
    // THE ID-ADDRESSED AREA, which is internal and is what the six ABI slots
    // are. A test does not use these directly: it declares a [StringTable],
    // registers it through StringTableRegistry against this double, and drives
    // the SHIPPED StringTableHandle -- so the re-find-by-name policy under test
    // is the one the game runs, and the "StringTables.Find" in front of every
    // operation in the log is that policy, visible.
    int IEngineStringTables.Create(string name, int maxEntries) => Record<int>("StringTables.Create", name, maxEntries);
    int IEngineStringTables.Find(string name) => Record<int>("StringTables.Find", name);
    int IEngineStringTables.AddString(int tableId, string value) => Record<int>("StringTables.AddString", tableId, value);
    int IEngineStringTables.FindStringIndex(int tableId, string value) => Record<int>("StringTables.FindStringIndex", tableId, value);
    string? IEngineStringTables.GetString(int tableId, int stringIndex) => Record<string>("StringTables.GetString", tableId, stringIndex);
    int IEngineStringTables.GetNumStrings(int tableId) => Record<int>("StringTables.GetNumStrings", tableId);

    // Precache
    int IEnginePrecache.PrecacheDecal(string name) => Record<int>("Precache.PrecacheDecal", name);
    void IEnginePrecache.PrecacheMaterial(string material) => RecordVoid("Precache.PrecacheMaterial", material);
    void IEnginePrecache.PrecacheParticleSystem(string name) => RecordVoid("Precache.PrecacheParticleSystem", name);
    int IEnginePrecache.PrecacheModel(string model) => Record<int>("Precache.PrecacheModel", model);
    int IEnginePrecache.ModelFrameCount(int modelIndex) => Record<int>("Precache.ModelFrameCount", modelIndex);

    // NOT Record<ModelType>, which throws when unstubbed. Ten of the seventeen
    // temp entity spawn helpers open by asking about their model, so a test of
    // any one of them would otherwise have to describe a model before it could
    // describe anything else -- and the unstubbed answer, Bad, is exactly the
    // "no such model" case every helper already has to handle. That makes the
    // default a useful test rather than a required stub.
    ModelType IEnginePrecache.ModelTypeOf(int modelIndex)
    {
        RecordVoid("Precache.ModelTypeOf", modelIndex);

        return _answers.ContainsKey("Precache.ModelTypeOf")
             ? Value<ModelType>("Precache.ModelTypeOf")
             : ModelType.Bad;
    }
    void IEnginePrecache.PrecacheSound(string soundName) => RecordVoid("Precache.PrecacheSound", soundName);
    int IEnginePrecache.BreakPieceCount(string model) => Record<int>("Precache.BreakPieceCount", model);

    /// <summary>
    /// The break list, which is the one member here whose answer depends on an
    /// ARGUMENT rather than only on the member.
    ///
    /// <para>
    /// The stub table is keyed by name alone, so a test that wants three
    /// different pieces cannot express them one call at a time. It stubs the
    /// whole list under <c>Precache.BreakPieceAt.pieces</c> and this indexes
    /// it — which is what the engine does too, so a test reading piece 2 gets
    /// piece 2 rather than whatever was stubbed last.
    /// </para>
    ///
    /// <para>
    /// The return value is therefore computed rather than stubbed: it says
    /// whether the list HAS that piece, which is the whole of what native's
    /// answer means.
    /// </para>
    /// </summary>
    bool IEnginePrecache.BreakPieceAt(string model, int index, float defaultBurstScale,
                                      CollisionGroup defaultCollisionGroup,
                                      out string pieceModel, out BreakPiece piece)
    {
        // First, so an unstubbed list is a refusal naming the member rather
        // than a silent empty answer.
        var pieces = Value<IReadOnlyList<(string Model, BreakPiece Piece)>>("Precache.BreakPieceAt.pieces");

        pieceModel = string.Empty;
        piece = default;

        RecordVoid("Precache.BreakPieceAt", model, index, defaultBurstScale, defaultCollisionGroup);

        if (pieces is null || index < 0 || index >= pieces.Count)
            return false;

        pieceModel = pieces[index].Model;
        piece = pieces[index].Piece;
        return true;
    }

    // GameEvents
    bool IEngineGameEvents.EventGetBool(IntPtr handle, string key) => Record<bool>("GameEvents.EventGetBool", handle, key);
    float IEngineGameEvents.EventGetFloat(IntPtr handle, string key) => Record<float>("GameEvents.EventGetFloat", handle, key);
    int IEngineGameEvents.EventGetInt(IntPtr handle, string key) => Record<int>("GameEvents.EventGetInt", handle, key);
    string IEngineGameEvents.EventGetString(IntPtr handle, string key) => Record<string>("GameEvents.EventGetString", handle, key);
    void IEngineGameEvents.ListenForGameEvent(string name) => RecordVoid("GameEvents.ListenForGameEvent", name);
    IntPtr IEngineGameEvents.EventCreate(string name) => Record<IntPtr>("GameEvents.EventCreate", name);
    void IEngineGameEvents.EventSetInt(IntPtr handle, string key, int value) => RecordVoid("GameEvents.EventSetInt", handle, key, value);
    void IEngineGameEvents.EventSetFloat(IntPtr handle, string key, float value) => RecordVoid("GameEvents.EventSetFloat", handle, key, value);
    void IEngineGameEvents.EventSetBool(IntPtr handle, string key, bool value) => RecordVoid("GameEvents.EventSetBool", handle, key, value);
    void IEngineGameEvents.EventSetString(IntPtr handle, string key, string value) => RecordVoid("GameEvents.EventSetString", handle, key, value);
    bool IEngineGameEvents.EventFire(IntPtr handle) => Record<bool>("GameEvents.EventFire", handle);
    void IEngineGameEvents.EventFree(IntPtr handle) => RecordVoid("GameEvents.EventFree", handle);

    // Networking
    PayloadAgreement IEngineNetworking.CheckPayloadAgreement(EntityHandle h) => Record<PayloadAgreement>("Networking.CheckPayloadAgreement", h);
    EntityState IEngineNetworking.EntityGetState(EntityHandle entity, out MoveType moveType)
    {
        // Recorded FIRST, so a factory for the out parameter can read which
        // entity it is being asked about from Calls.
        EntityState state = Record<EntityState>("Networking.EntityGetState", entity);
        moveType = Value<MoveType>("Networking.EntityGetState.moveType");
        return state;
    }
    Span<byte> IEngineNetworking.GetEntityBlob(EntityHandle h)
    {
        RecordVoid("Networking.GetEntityBlob", h);
        return default;
    }
    void IEngineNetworking.MarkEntityStateChanged(EntityHandle h) => RecordVoid("Networking.MarkEntityStateChanged", h);
    void IEngineNetworking.MarkEntityBlockChanged(EntityHandle h, int blockOffset) => RecordVoid("Networking.MarkEntityBlockChanged", h, blockOffset);
    int IEngineNetworking.NetHierarchyDeclare(int blobBytes) => Record<int>("Networking.NetHierarchyDeclare", blobBytes);
    int IEngineNetworking.WorldNetDeclare(int blobBytes) => Record<int>("Networking.WorldNetDeclare", blobBytes);
    int IEngineNetworking.NetPublishType(string className, int shell, uint schemaHash, int layoutBytes, out uint nativeTypeId)
    {
        nativeTypeId = Value<uint>("Networking.NetPublishType.nativeTypeId");
        return Record<int>("Networking.NetPublishType", className, shell, schemaHash, layoutBytes);
    }
    NetTierInfo? IEngineNetworking.NetTierAt(int shell, int tier) => Record<NetTierInfo?>("Networking.NetTierAt", shell, tier);
    void IUserMessageSender.Send(string key, ReadOnlySpan<byte> payload, int player, bool reliable) => RecordVoid("UserMessages.Send", key, payload.Length, player, reliable, payload.ToArray());
    bool IEngineNetworking.SendServerMessage(string key, ReadOnlySpan<byte> payload) => Record<bool>("Networking.SendServerMessage", key, payload.ToArray());

    // Scripted
    List<TemplatePlacement> IEngineScripted.TemplateCapture(string sourceName, bool removeSources, int limit) => Record<List<TemplatePlacement>>("Scripted.TemplateCapture", sourceName, removeSources, limit);
    List<EntityHandle> IEngineScripted.TemplateSpawnKeyed(IReadOnlyList<KeyedSpawn> spawns, int limit) => Record<List<EntityHandle>>("Scripted.TemplateSpawnKeyed", spawns, limit);
    void IEngineScripted.RegisterEntityClass(string className, string[] outputNames, ScriptedEntityKind kind) => RecordVoid("Scripted.RegisterEntityClass", className, outputNames, kind);
    void IEngineScripted.RegisterEntityClassOps(string className, uint opMask) => RecordVoid("Scripted.RegisterEntityClassOps", className, opMask);

    // Entities
    void IEngineEntities.AddBaseVelocity(EntityHandle entity, Vector velocity) => RecordVoid("Entities.AddBaseVelocity", entity, velocity);
    void IEngineEntities.PositionChanged(EntityHandle entity, Vector previousOrigin) => RecordVoid("Entities.PositionChanged", entity, previousOrigin);
    void IEngineEntities.ApplyVelocityImpulse(EntityHandle h, Vector impulse, bool clearGround) => RecordVoid("Entities.ApplyVelocityImpulse", h, impulse, clearGround);
    EntityHandle IEngineEntities.Create(string className) => Record<EntityHandle>("Entities.Create", className);
    EntityHandle IEngineEntities.CreateFromMap(string className, byte[] packedKeys) => Record<EntityHandle>("Entities.CreateFromMap", className, packedKeys);
    int IEngineEntities.Count() => Record<int>("Entities.Count");
    void IEngineEntities.PurgeMapRefs() => RecordVoid("Entities.PurgeMapRefs");
    void IEngineEntities.CleanupDeleteList() => RecordVoid("Entities.CleanupDeleteList");
    int IEngineEntities.InvalidateMdlCacheAll() => Record<int>("Entities.InvalidateMdlCacheAll");
    int IEngineEntities.UpdateVisibilityAll() => Record<int>("Entities.UpdateVisibilityAll");
    IEnumerable<EntityHandle> IEngineEntities.FindAllByClassname(string className) => Record<IEnumerable<EntityHandle>>("Entities.FindAllByClassname", className);
    List<EntityHandle> IEngineEntities.FindAllByName(string name, int limit) => Record<List<EntityHandle>>("Entities.FindAllByName", name, limit);
    EntityHandle IEngineEntities.FindByName(string name) => Record<EntityHandle>("Entities.FindByName", name);
    NameMatches IEngineEntityNames.Named(string name, int limit)
        => NameMatches.Copy(Record<List<EntityHandle>>("Entities.Named", name, limit));
    bool IEngineEntities.FireInput(EntityHandle target, string input, string? value, EntityHandle activator) => Record<bool>("Entities.FireInput", target, input, value, activator);
    Vector IEngineEntities.GetAngles(EntityHandle h) => Record<Vector>("Entities.GetAngles", h);
    Vector IEngineEntities.GetAngularVelocity(EntityHandle entity) => Record<Vector>("Entities.GetAngularVelocity", entity);
    int IEngineEntities.GetArmor(EntityHandle entity) => Record<int>("Entities.GetArmor", entity);
    (Vector Mins, Vector Maxs) IEngineEntities.GetBounds(EntityHandle entity) => Record<(Vector Mins, Vector Maxs)>("Entities.GetBounds", entity);
    Vector IEngineEntities.GetBoundsSize(EntityHandle entity) => Record<Vector>("Entities.GetBoundsSize", entity);
    string IEngineEntities.GetClassName(EntityHandle h) => Record<string>("Entities.GetClassName", h);
    Vector IEngineEntities.GetEyeAngles(EntityHandle h) => Record<Vector>("Entities.GetEyeAngles", h);
    Vector IEngineEntities.GetEyePosition(EntityHandle h) => Record<Vector>("Entities.GetEyePosition", h);
    int IEngineEntities.GetHealth(EntityHandle h) => Record<int>("Entities.GetHealth", h);
    int IEngineEntities.GetIndex(EntityHandle h) => Record<int>("Entities.GetIndex", h);
    string IEngineEntities.GetName(EntityHandle entity) => Record<string>("Entities.GetName", entity);
    Vector IEngineEntities.GetOrigin(EntityHandle h) => Record<Vector>("Entities.GetOrigin", h);
    Vector IEngineEntities.GetSpawnAngles(EntityHandle entity) => Record<Vector>("Entities.GetSpawnAngles", entity);
    int IEngineEntities.GetSpawnFlags(EntityHandle entity) => Record<int>("Entities.GetSpawnFlags", entity);
    Vector IEngineEntities.GetSpawnOrigin(EntityHandle entity) => Record<Vector>("Entities.GetSpawnOrigin", entity);
    Vector IEngineEntities.GetVelocity(EntityHandle h) => Record<Vector>("Entities.GetVelocity", h);
    Vector IEngineEntities.GetWorldSpaceCenter(EntityHandle h) => Record<Vector>("Entities.GetWorldSpaceCenter", h);
    bool IEngineEntities.IsPlayer(EntityHandle h) => Record<bool>("Entities.IsPlayer", h);
    bool IEngineEntities.IsValid(EntityHandle h) => Record<bool>("Entities.IsValid", h);
    void IEngineEntities.Remove(EntityHandle entity) => RecordVoid("Entities.Remove", entity);
    void IEngineEntities.SetAngles(EntityHandle entity, Vector angles) => RecordVoid("Entities.SetAngles", entity, angles);
    void IEngineEntities.SetAngularVelocity(EntityHandle entity, Vector degreesPerSecond) => RecordVoid("Entities.SetAngularVelocity", entity, degreesPerSecond);
    void IEngineEntities.SetArmor(EntityHandle entity, int armor) => RecordVoid("Entities.SetArmor", entity, armor);
    void IEngineEntities.SetHealth(EntityHandle h, int health) => RecordVoid("Entities.SetHealth", h, health);
    bool IEngineEntities.SetKeyValue(EntityHandle entity, string key, string value) => Record<bool>("Entities.SetKeyValue", entity, key, value);
    void IEngineEntities.SetOrigin(EntityHandle h, Vector origin) => RecordVoid("Entities.SetOrigin", h, origin);
    void IEngineEntities.SetOwner(EntityHandle h, EntityHandle owner) => RecordVoid("Entities.SetOwner", h, owner);
    void IEngineEntities.SetVelocity(EntityHandle h, Vector velocity) => RecordVoid("Entities.SetVelocity", h, velocity);
    void IEngineEntities.Spawn(EntityHandle entity) => RecordVoid("Entities.Spawn", entity);
    void IEngineEntities.Teleport(EntityHandle h, Vector origin, Vector? angles, bool zeroVelocity) => RecordVoid("Entities.Teleport", h, origin, angles, zeroVelocity);
    bool IEngineEntities.SetParent(EntityHandle child, EntityHandle parent, int parentAttachment) => Record<bool>("Entities.SetParent", child, parent, parentAttachment);
    EntityHandle IEngineEntities.GetParent(EntityHandle child) => Record<EntityHandle>("Entities.GetParent", child);
    int IEngineEntities.GetParentAttachment(EntityHandle child) => Record<int>("Entities.GetParentAttachment", child);
    void IEngineEntities.SetLocalTransform(EntityHandle child, Vector? origin, Vector? angles) => RecordVoid("Entities.SetLocalTransform", child, origin, angles);
    void IEngineEntities.GetLocalTransform(EntityHandle child, out Vector origin, out Vector angles)
    {
        origin = Value<Vector>("Entities.GetLocalTransform.origin");
        angles = Value<Vector>("Entities.GetLocalTransform.angles");
        RecordVoid("Entities.GetLocalTransform", child);
    }

    // What managed parenting asks of the world (step 2h, made live). Recorded
    // like everything else here so a test can drive Hierarchy against a script
    // rather than a running game -- which is the whole reason IHierarchyWorld
    // has an engine-backed implementation and a recorded one rather than one.
    ParentProbe IEngineEntities.ParentProbe(EntityHandle entity)
    {
        RecordVoid("Entities.ParentProbe", entity);
        return Value<ParentProbe>("Entities.ParentProbe.bits");
    }

    bool IEngineEntities.GetAttachmentFrame(EntityHandle entity, int attachment,
                                            out Vector origin, out QAngle angles)
    {
        origin = Value<Vector>("Entities.GetAttachmentFrame.origin");
        angles = Value<QAngle>("Entities.GetAttachmentFrame.angles");
        RecordVoid("Entities.GetAttachmentFrame", entity, attachment);
        return Value<bool>("Entities.GetAttachmentFrame");
    }

    int IEngineEntities.LookupAttachmentAny(EntityHandle entity, string attachmentName)
    {
        RecordVoid("Entities.LookupAttachmentAny", entity, attachmentName);
        return Value<int>("Entities.LookupAttachmentAny");
    }

    EntityHandle IEngineEntities.FindBaseByName(string name)
    {
        RecordVoid("Entities.FindBaseByName", name);
        return Value<EntityHandle>("Entities.FindBaseByName");
    }

    /// <summary>
    /// EMPTY, ALWAYS, and recorded. There is no engine memory behind a recorded
    /// engine, and a scratch array handed back here would be a block a test
    /// could write into and nothing could read — which reads as the write having
    /// landed. An empty span is what "there is no block here" already means
    /// everywhere else.
    /// </summary>
    // The empty answers of a side with no lists and no replay, as the
    // interface's default bodies gave before plan_shim_areas_split phase 4.
    IInterpolationBlocks IEngineEntities.InterpolationBlocks => NoInterpolationBlocks.Instance;
    IRenderPolicyBlocks IEngineEntities.RenderPolicyBlocks => NoRenderPolicyBlocks.Instance;
    IClientReplayEngine IEngineEntities.ClientReplay => NoClientReplayEngine.Instance;

    Span<byte> IEngineEntities.GetEngineBlock(EntityHandle entity)
    {
        RecordVoid("Entities.GetEngineBlock", entity);
        return Span<byte>.Empty;
    }

    void IEngineEntities.DestroyPhysics(EntityHandle entity)
        => RecordVoid("Entities.DestroyPhysics", entity);

    /// <summary>
    /// NO PAIRS, ALWAYS, and recorded — <see cref="IEngineEntities.GetEngineBlock"/>'s
    /// rule applied to the overlap report. There is no spatial partition behind
    /// a recorded engine, so there is nothing it could truthfully answer.
    ///
    /// <para>
    /// <b>AND THE TOUCH TESTS DO NOT GO THROUGH IT.</b> A fake that answered
    /// pairs would be a second implementation of the thing under test, and a
    /// fake that answered <c>default</c> where the slot answers something else
    /// is how eight of step 2f's facts came to measure the wrong path.
    /// <c>TouchLinkTests</c> hands spans straight to
    /// <see cref="EntityDirectory.TouchFrame"/>; what this records is that a
    /// caller ASKED, which is what <c>OverlapReader</c> is tested on.
    /// </para>
    /// </summary>
    int IEngineOverlaps.OverlapReport(Span<OverlapPair> into)
    {
        RecordVoid("Entities.OverlapReport", into.Length);
        return 0;
    }

    // The native half of a savegame. Recorded under Entities.* even where
    // another area offers the same call, because these are the names the save
    // section actually goes through and a test asserting its order asserts
    // these.
    float IEngineEntities.GetGravity(EntityHandle entity) => Record<float>("Entities.GetGravity", entity);
    void IEngineEntities.SetGravity(EntityHandle entity, float gravity) => RecordVoid("Entities.SetGravity", entity, gravity);
    SolidFlags IEngineEntities.GetSolidFlags(EntityHandle entity) => Record<SolidFlags>("Entities.GetSolidFlags", entity);
    SolidType IEngineEntities.GetSolidType(EntityHandle entity) => Record<SolidType>("Entities.GetSolidType", entity);
    void IEngineEntities.SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags) => RecordVoid("Entities.SetSolidity", entity, solidType, solidFlags);
    void IEngineEntities.SetBounds(EntityHandle entity, Vector mins, Vector maxs) => RecordVoid("Entities.SetBounds", entity, mins, maxs);
    void IEngineEntities.SetSpawnFlags(EntityHandle entity, int spawnFlags) => RecordVoid("Entities.SetSpawnFlags", entity, spawnFlags);

    // EntityPhysics
    void IEngineEntityPhysics.ApplyForceCenter(EntityHandle entity, Vector force) => RecordVoid("EntityPhysics.ApplyForceCenter", entity, force);
    void IEngineEntityPhysics.ApplyForceOffset(EntityHandle entity, Vector force, Vector worldPoint) => RecordVoid("EntityPhysics.ApplyForceOffset", entity, force, worldPoint);
    void IEngineEntityPhysics.ApplyTorque(EntityHandle entity, Vector torque) => RecordVoid("EntityPhysics.ApplyTorque", entity, torque);
    void IEngineEntityPhysics.BecomePhysicsTrigger(EntityHandle entity, bool isTrigger) => RecordVoid("EntityPhysics.BecomePhysicsTrigger", entity, isTrigger);
    void IEngineEntityPhysics.EnableMotion(EntityHandle entity, bool enable) => RecordVoid("EntityPhysics.EnableMotion", entity, enable);
    void IEngineEntityPhysics.Wake(EntityHandle entity) => RecordVoid("EntityPhysics.Wake", entity);
    void IEngineEntityPhysics.Sleep(EntityHandle entity) => RecordVoid("EntityPhysics.Sleep", entity);
    void IEngineEntityPhysics.AddGameFlags(EntityHandle entity, uint flags) => RecordVoid("EntityPhysics.AddGameFlags", entity, flags);
    float IEngineEntityPhysics.GetMass(EntityHandle entity) => Record<float>("EntityPhysics.GetMass", entity);
    bool IEngineEntityPhysics.InitPhysics(EntityHandle entity, bool asleep, bool debris) => Record<bool>("EntityPhysics.InitPhysics", entity, asleep, debris);
    void IEngineEntityPhysics.SetBounce(EntityHandle h, int bounces) => RecordVoid("EntityPhysics.SetBounce", h, bounces);
    void IEngineEntityPhysics.SetBrushSolidity(EntityHandle entity, BrushSolidity solidity) => RecordVoid("EntityPhysics.SetBrushSolidity", entity, solidity);
    CollisionGroup IEngineEntityPhysics.GetCollisionGroup(EntityHandle entity) => Record<CollisionGroup>("EntityPhysics.GetCollisionGroup", entity);
    SolidFlags IEngineEntityPhysics.GetSolidFlags(EntityHandle entity) => Record<SolidFlags>("EntityPhysics.GetSolidFlags", entity);
    void IEngineEntityPhysics.SetCollisionGroup(EntityHandle entity, CollisionGroup group) => RecordVoid("EntityPhysics.SetCollisionGroup", entity, group);
    void IEngineEntityPhysics.SetFriction(EntityHandle h, float friction) => RecordVoid("EntityPhysics.SetFriction", h, friction);
    void IEngineEntityPhysics.SetGravity(EntityHandle h, float gravity) => RecordVoid("EntityPhysics.SetGravity", h, gravity);
    void IEngineEntityPhysics.SetMass(EntityHandle entity, float mass) => RecordVoid("EntityPhysics.SetMass", entity, mass);
    void IEngineEntityPhysics.SetNotSolid(EntityHandle entity, bool notSolid) => RecordVoid("EntityPhysics.SetNotSolid", entity, notSolid);

    nint IEngineEntityPhysics.PhysicsObject(EntityHandle entity) => Record<nint>("EntityPhysics.PhysicsObject", entity);
    nint IEngineEntityPhysics.EnvironmentPointer() => Record<nint>("EntityPhysics.EnvironmentPointer");

    Vector IEngineEntityPhysics.CollideExtent(nint physicsObject, Vector direction) => Record<Vector>("EntityPhysics.CollideExtent", physicsObject, direction);
    Vector IEngineEntityPhysics.Inertia(nint physicsObject) => Record<Vector>("EntityPhysics.Inertia", physicsObject);
    nint IEngineEntityPhysics.WorldObject() => Record<nint>("EntityPhysics.WorldObject");
    void IEngineEntityPhysics.SetPhysicsStatic(EntityHandle entity, bool isStatic) => RecordVoid("EntityPhysics.SetPhysicsStatic", entity, isStatic);

    // EntityRender
    string IEngineEntityRender.GetModelName(EntityHandle entity) => Record<string>("EntityRender.GetModelName", entity);
    string? IEngineEntityRender.GetModelKeyValueText(string modelName) => Record<string?>("EntityRender.GetModelKeyValueText", modelName);
    int IEngineEntityRender.GetTextureFrame(EntityHandle entity) => Record<int>("EntityRender.GetTextureFrame", entity);
    int IEngineEntityRender.LookupSequence(EntityHandle entity, string name) => Record<int>("EntityRender.LookupSequence", entity, name);
    int IEngineEntityRender.LookupSequenceByActivity(EntityHandle entity, string activityName) => Record<int>("EntityRender.LookupSequenceByActivity", entity, activityName);
    float IEngineEntityRender.SequenceDuration(EntityHandle entity, int sequence) => Record<float>("EntityRender.SequenceDuration", entity, sequence);
    float IEngineEntityRender.SequenceGroundSpeed(EntityHandle entity, int sequence) => Record<float>("EntityRender.SequenceGroundSpeed", entity, sequence);
    void IEngineEntityRender.SetCustomDraw(EntityHandle entity, Vector mins, Vector maxs) => RecordVoid("EntityRender.SetCustomDraw", entity, mins, maxs);
    void IEngineEntityRender.SetBlending(EntityHandle entity, bool translucent, bool ignoreZ, int alpha) => RecordVoid("EntityRender.SetBlending", entity, translucent, ignoreZ, alpha);
    void IEngineEntityRender.SetShadowCast(EntityHandle entity, ShadowCastChoice choice) => RecordVoid("EntityRender.SetShadowCast", entity, choice);
    ShadowCast IEngineEntityRender.ShadowCast(EntityHandle entity, out ShadowCastChoice asked, out bool hasShadow)
    {
        asked = Value<ShadowCastChoice>("EntityRender.ShadowCast.asked");
        hasShadow = Value<bool>("EntityRender.ShadowCast.hasShadow");
        return Record<ShadowCast>("EntityRender.ShadowCast", entity);
    }
    void IEngineEntityRender.SetFade(EntityHandle entity, FadeStyle style, float startDistance, float endDistance, float minimumAlpha) => RecordVoid("EntityRender.SetFade", entity, style, startDistance, endDistance, minimumAlpha);
    void IEngineEntityRender.SetModel(EntityHandle entity, string model) => RecordVoid("EntityRender.SetModel", entity, model);
    void IEngineEntityRender.SetOverlayModel(EntityHandle entity, int modelIndex) => RecordVoid("EntityRender.SetOverlayModel", entity, modelIndex);

    void IEngineEntityRender.SetModelScale(EntityHandle entity, float scale) => RecordVoid("EntityRender.SetModelScale", entity, scale);

    void IEngineEntityRender.SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex) => RecordVoid("EntityRender.SetBoneMergeSource", entity, sourceEntityIndex);
    int IEngineEntityRender.LookupAttachment(EntityHandle entity, string name) => Record<int>("EntityRender.LookupAttachment", entity, name);
    bool IEngineEntityRender.GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles)
    {
        origin = Value<Vector>("EntityRender.GetAttachment.origin");
        angles = Value<Vector>("EntityRender.GetAttachment.angles");
        return Record<bool>("EntityRender.GetAttachment", entity, attachment);
    }
    int IEngineEntityRender.AttachmentCount(EntityHandle entity) => Record<int>("EntityRender.AttachmentCount", entity);
    string IEngineEntityRender.AttachmentName(EntityHandle entity, int attachment) => Record<string>("EntityRender.AttachmentName", entity, attachment);
    StandaloneBones? IEngineEntityRender.BoneMatrices(EntityHandle entity)
    {
        RecordVoid("EntityRender.BoneMatrices", entity);

        // Null, and there is no recorded value to hand back instead. A bone
        // cache is a skeleton, not a scalar -- the recorder's Value<T> table is
        // keyed by name and holds one answer, and a plausible fake skeleton is
        // exactly the thing a caller of this cannot tell from a real one.
        return null;
    }

    bool IEngineEntityRender.IsPoseable(EntityHandle entity) => Record<bool>("EntityRender.IsPoseable", entity);

    string IEngineEntityRender.StudioName(EntityHandle entity) => Record<string>("EntityRender.StudioName", entity);

    int IEngineEntityRender.BoneCount(EntityHandle entity, out int cached)
    {
        cached = Value<int>("EntityRender.BoneCount.cached");
        return Record<int>("EntityRender.BoneCount", entity);
    }
    int IEngineEntityRender.PoseParameterCount(EntityHandle entity) => Record<int>("EntityRender.PoseParameterCount", entity);
    int IEngineEntityRender.LookupPoseParameter(EntityHandle entity, string name) => Record<int>("EntityRender.LookupPoseParameter", entity, name);
    string IEngineEntityRender.PoseParameterName(EntityHandle entity, int parameter) => Record<string>("EntityRender.PoseParameterName", entity, parameter);
    bool IEngineEntityRender.PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end)
    {
        start = Value<float>("EntityRender.PoseParameterRange.start");
        end = Value<float>("EntityRender.PoseParameterRange.end");
        return Record<bool>("EntityRender.PoseParameterRange", entity, parameter);
    }
    float IEngineEntityRender.SetPoseParameter(EntityHandle entity, int parameter, float value) => Record<float>("EntityRender.SetPoseParameter", entity, parameter, value);
    float IEngineEntityRender.GetPoseParameter(EntityHandle entity, int parameter) => Record<float>("EntityRender.GetPoseParameter", entity, parameter);
    bool IEngineEntityRender.IsDormant(EntityHandle entity) => Record<bool>("EntityRender.IsDormant", entity);
    int IEngineEntityRender.SequenceEventCount(EntityHandle entity, int sequence) => Record<int>("EntityRender.SequenceEventCount", entity, sequence);
    bool IEngineEntityRender.SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts)
    {
        sequence = Value<int>("EntityRender.SequenceState.sequence");
        cycle = Value<float>("EntityRender.SequenceState.cycle");
        duration = Value<float>("EntityRender.SequenceState.duration");
        looping = Value<bool>("EntityRender.SequenceState.looping");
        starts = Value<int>("EntityRender.SequenceState.starts");
        return Record<bool>("EntityRender.SequenceState", entity);
    }
    int IEngineEntityRender.ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains)
    {
        computed = Value<int>("EntityRender.ProceduralBones.computed");
        jiggle = Value<int>("EntityRender.ProceduralBones.jiggle");
        ikChains = Value<int>("EntityRender.ProceduralBones.ikChains");
        return Record<int>("EntityRender.ProceduralBones", entity);
    }
    void IEngineEntityRender.SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart)
        => RecordVoid("EntityRender.SetAnimLayer", entity, slot, sequence, weight, playbackRate, order, restart);
    int IEngineEntityRender.AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle)
    {
        sequence = Value<int>("EntityRender.AnimLayer.sequence");
        weight = Value<float>("EntityRender.AnimLayer.weight");
        order = Value<int>("EntityRender.AnimLayer.order");
        cycle = Value<float>("EntityRender.AnimLayer.cycle");
        return Record<int>("EntityRender.AnimLayer", entity, slot);
    }
    bool IEngineEntityRender.SequenceEvent(EntityHandle entity, int sequence, int index, out float cycle, out int eventId, out int type, out string name, out string options)
    {
        cycle = Value<float>("EntityRender.SequenceEvent.cycle");
        eventId = Value<int>("EntityRender.SequenceEvent.eventId");
        type = Value<int>("EntityRender.SequenceEvent.type");
        name = Value<string>("EntityRender.SequenceEvent.name") ?? string.Empty;
        options = Value<string>("EntityRender.SequenceEvent.options") ?? string.Empty;
        return Record<bool>("EntityRender.SequenceEvent", entity, sequence, index);
    }
    void IEngineEntityRender.SetSequence(EntityHandle entity, int sequence) => RecordVoid("EntityRender.SetSequence", entity, sequence);
    void IEngineEntityRender.SetViewModel(EntityHandle entity, bool enable) => RecordVoid("EntityRender.SetViewModel", entity, enable);
    void IEngineEntityRender.PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart) => RecordVoid("EntityRender.PlaySequence", entity, sequence, playbackRate, restart);
    void IEngineEntityRender.SetSkin(EntityHandle entity, int skin) => RecordVoid("EntityRender.SetSkin", entity, skin);
    void IEngineEntityRender.SetTextureFrame(EntityHandle entity, int frame) => RecordVoid("EntityRender.SetTextureFrame", entity, frame);
    void IEngineEntityRender.SetTransmitAlways(EntityHandle entity, bool always) => RecordVoid("EntityRender.SetTransmitAlways", entity, always);
    bool IEngineEntityRender.DetectInSkybox(EntityHandle entity) => Record<bool>("EntityRender.DetectInSkybox", entity);
    void IEngineEntityRender.SetVisible(EntityHandle entity, bool visible) => RecordVoid("EntityRender.SetVisible", entity, visible);

    // Damage
    void IEngineDamage.Apply(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType) => RecordVoid("Damage.Apply", victim, attacker, damage, damageType);
    void IEngineDamage.ApplyAt(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType, Vector force, Vector position) => RecordVoid("Damage.ApplyAt", victim, attacker, damage, damageType, force, position);
    void IEngineDamage.SetForceServerRagdoll(EntityHandle entity, bool force) => RecordVoid("Damage.SetForceServerRagdoll", entity, force);
    TakeDamageMode IEngineDamage.SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => Record<TakeDamageMode>("Damage.SetTakeDamageMode", entity, mode);
    void IEngineDamage.RegisterRules(DamageRuleMasks masks) => RecordVoid("Damage.RegisterRules", masks);

    // GameRulesPolicy
    void IEngineGameRulesPolicy.RegisterPolicy(GameRulesPolicyBlob policy) => RecordVoid("GameRulesPolicy.RegisterPolicy", policy);

    // CollisionRules
    // Recorded is received: the recorder is this side's native, so it reports delivery.
    bool IEngineCollisionRules.RegisterCollisionRules(uint[] rows)
    {
        RecordVoid("CollisionRules.RegisterCollisionRules", rows);
        return true;
    }
    void IEngineDamage.TakeDamage(EntityHandle entity, EntityHandle attacker, float damage, DamageType damageType, Vector? direction, Vector forceOrigin, bool physicsOnly) => RecordVoid("Damage.TakeDamage", entity, attacker, damage, damageType, direction, forceOrigin, physicsOnly);

    // BrushMovement
    void IEngineBrushMovement.MoveToAngles(EntityHandle entity, Vector destination, float speed) => RecordVoid("BrushMovement.MoveToAngles", entity, destination, speed);
    void IEngineBrushMovement.MoveToPosition(EntityHandle entity, Vector destination, float speed) => RecordVoid("BrushMovement.MoveToPosition", entity, destination, speed);
    void IEngineBrushMovement.SetSurfaceEdges(EntityHandle entity, byte[] packed, int count) => RecordVoid("BrushMovement.SetSurfaceEdges", entity, packed, count);
    void IEngineBrushMovement.StopMoving(EntityHandle entity) => RecordVoid("BrushMovement.StopMoving", entity);
    void IEngineBrushMovement.Spin(EntityHandle entity, Vector degreesPerSecond) => RecordVoid("BrushMovement.Spin", entity, degreesPerSecond);

    // Players
    void IEnginePlayers.AddScore(EntityHandle player, int points, bool allowNegative) => RecordVoid("Players.AddScore", player, points, allowNegative);
    void IEnginePlayers.PlaceAtSpawn(EntityHandle player, Vector origin, Vector angles) => RecordVoid("Players.PlaceAtSpawn", player, origin, angles);
    bool IEnginePlayers.DeployLastWeapon(EntityHandle player) => Record<bool>("Players.DeployLastWeapon", player);
    bool IEnginePlayers.DropActiveWeapon(EntityHandle player, Vector velocity) => Record<bool>("Players.DropActiveWeapon", player, velocity);
    EntityHandle IEnginePlayers.FindByUserId(int userId) => Record<EntityHandle>("Players.FindByUserId", userId);
    string IEnginePlayers.GetActiveWeapon(EntityHandle player) => Record<string>("Players.GetActiveWeapon", player);
    EntityHandle IEnginePlayers.GetActiveWeaponEntity(EntityHandle player) => Record<EntityHandle>("Players.GetActiveWeaponEntity", player);

    EntityHandle IEnginePlayers.GetWeaponEntity(EntityHandle player, int slot) => Record<EntityHandle>("Players.GetWeaponEntity", player, slot);

    EntityHandle IEnginePlayers.GetViewModelEntity(EntityHandle player) => Record<EntityHandle>("Players.GetViewModelEntity", player);
    EntityHandle IEnginePlayers.GetHandle(int playerIndex) => Record<EntityHandle>("Players.GetHandle", playerIndex);
    EntityHandle IEnginePlayers.GetCommandClient() => Record<EntityHandle>("Players.GetCommandClient");
    ObserverMode IEnginePlayers.GetObserverMode(EntityHandle player) => Record<ObserverMode>("Players.GetObserverMode", player);
    Vector IEnginePlayers.GetShootOrigin(EntityHandle player) => Record<Vector>("Players.GetShootOrigin", player);
    int IEnginePlayers.GetTeam(EntityHandle player) => Record<int>("Players.GetTeam", player);
    int IEnginePlayers.GetUserId(EntityHandle player) => Record<int>("Players.GetUserId", player);
    int IEnginePlayers.GiveAmmo(EntityHandle player, int count, string ammoName) => Record<int>("Players.GiveAmmo", player, count, ammoName);
    bool IEnginePlayers.GiveItem(EntityHandle player, string className) => Record<bool>("Players.GiveItem", player, className);
    bool IEnginePlayers.HolsterActiveWeapon(EntityHandle player) => Record<bool>("Players.HolsterActiveWeapon", player);
    bool IEnginePlayers.IsAlive(EntityHandle player) => Record<bool>("Players.IsAlive", player);
    bool IEnginePlayers.BoxInPvs(EntityHandle player, Vector mins, Vector maxs) => Record<bool>("Players.BoxInPvs", player, mins, maxs);
    void IEnginePlayers.MakePickup(EntityHandle entity, float sizeX, float sizeY, float sizeZ) => RecordVoid("Players.MakePickup", entity, sizeX, sizeY, sizeZ);
    void IEnginePlayers.RemoveAllItems(EntityHandle player, bool stripSuit) => RecordVoid("Players.RemoveAllItems", player, stripSuit);
    void IEnginePlayers.Respawn(EntityHandle player) => RecordVoid("Players.Respawn", player);
    void IEnginePlayers.SetFov(EntityHandle player, EntityHandle requester, int fov, float rate) => RecordVoid("Players.SetFov", player, requester, fov, rate);
    void IEnginePlayers.SetObserverMode(EntityHandle player, ObserverMode mode) => RecordVoid("Players.SetObserverMode", player, mode);
    void IEnginePlayers.SetTeam(EntityHandle player, int team) => RecordVoid("Players.SetTeam", player, team);
    void IEnginePlayers.SetPickupEnabled(EntityHandle entity, bool enabled) => RecordVoid("Players.SetPickupEnabled", entity, enabled);
    void IEnginePlayers.SetViewEntity(EntityHandle player, EntityHandle view) => RecordVoid("Players.SetViewEntity", player, view);

    // plan_server.md Phase 7 / plan-audit C5. Both halves of
    // ClientEarPosition's pair, so a fixture can make the standalone read miss
    // and the SDK one answer -- and, more to the point, prove that the
    // standalone one is asked FIRST, which is the whole content of the
    // crossing and is the reverse of the skybox pair's order below.
    bool IEnginePlayers.TryStandalonePlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = Value<Vector>("Players.TryStandalonePlayerEarPosition.ears");
        return Record<bool>("Players.TryStandalonePlayerEarPosition", edictIndex);
    }

    bool IEnginePlayers.TrySdkPlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = Value<Vector>("Players.TrySdkPlayerEarPosition.ears");
        return Record<bool>("Players.TrySdkPlayerEarPosition", edictIndex);
    }

    // PlayerMovement
    void IEnginePlayerMovement.ClearMovementConstraint(EntityHandle player) => RecordVoid("PlayerMovement.ClearMovementConstraint", player);
    void IEnginePlayerMovement.ForceButtons(EntityHandle player, InputButtons buttons, bool forced) => RecordVoid("PlayerMovement.ForceButtons", player, buttons, forced);
    InputButtons IEnginePlayerMovement.GetButtons(EntityHandle player) => Record<InputButtons>("PlayerMovement.GetButtons", player);
    void IEnginePlayerMovement.RegisterParams(MovementParams parameters) => RecordVoid("PlayerMovement.RegisterParams", parameters);
    void IEnginePlayerMovement.SetAllowAutoMovement(EntityHandle player, bool allow) => RecordVoid("PlayerMovement.SetAllowAutoMovement", player, allow);
    void IEnginePlayerMovement.SetButtonsDisabled(EntityHandle player, InputButtons buttons, bool disabled) => RecordVoid("PlayerMovement.SetButtonsDisabled", player, buttons, disabled);
    void IEnginePlayerMovement.SetFrozen(EntityHandle player, bool frozen) => RecordVoid("PlayerMovement.SetFrozen", player, frozen);
    void IEnginePlayerMovement.SetLaggedMovement(EntityHandle player, float value) => RecordVoid("PlayerMovement.SetLaggedMovement", player, value);
    void IEnginePlayerMovement.SetMovementConstraint(EntityHandle player, Vector centre, float radius, float width, float speedFactor) => RecordVoid("PlayerMovement.SetMovementConstraint", player, centre, radius, width, speedFactor);
    void IEnginePlayerMovement.SetSlowMovement(EntityHandle player, bool slow) => RecordVoid("PlayerMovement.SetSlowMovement", player, slow);
    MoveType IEnginePlayerMovement.SetMoveType(EntityHandle player, MoveType moveType) => Record<MoveType>("PlayerMovement.SetMoveType", player, moveType);
    void IEnginePlayerMovement.SnapEyeAngles(EntityHandle player, Vector pitchYawRoll) => RecordVoid("PlayerMovement.SnapEyeAngles", player, pitchYawRoll);

    // Weapons
    void IEngineWeapons.RegisterAmmoType(string name, DamageType damageType, int maxCarry, int tracerType, float damageForce, float damage) => RecordVoid("Weapons.RegisterAmmoType", name, damageType, maxCarry, tracerType, damageForce, damage);

    // Hud
    void IEngineHud.DrawWorldText(Vector origin, int line, string text, float duration, int r, int g, int b, int a) => RecordVoid("Hud.DrawWorldText", origin, line, text, duration, r, g, b, a);
    bool IEngineHud.DrawEntityOverlay(EntityHandle entity, int line, string? text, float duration, int r, int g, int b, int a) => Record<bool>("Hud.DrawEntityOverlay", entity, line, text ?? string.Empty, duration, r, g, b, a);
    (uint Drawn, uint Clipped, uint NoViewer, uint NoOverlay, bool ViewerSet) IEngineHud.OverlayReadout() => Record<(uint, uint, uint, uint, bool)>("Hud.OverlayReadout");
    void IEngineHud.OverlayResetCounts() => RecordVoid("Hud.OverlayResetCounts");

    // IEngineDiagnostics: recorded; the spans are left as the caller passed them.
    int IEngineDiagnostics.PlayerShadowCounts(Span<int> into) => Record<int>("Diagnostics.PlayerShadowCounts");
    int IEngineDiagnostics.ExitWatchArm(string recordPath) => Record<int>("Diagnostics.ExitWatchArm", recordPath);
    int IEngineDiagnostics.ExitWatchRead(Span<int> into) => Record<int>("Diagnostics.ExitWatchRead");
    int IEngineDiagnostics.ExitWatchDisarm() => Record<int>("Diagnostics.ExitWatchDisarm");
    void IEngineHud.AddLineOverlay(Vector start, Vector end, int r, int g, int b, bool noDepthTest, float duration) => RecordVoid("Hud.AddLineOverlay", start, end, r, g, b, noDepthTest, duration);
    int IEngineHud.LocalPlayerHealth => Record<int>("Hud.LocalPlayerHealth");
    Vector? IEngineHud.LocalPlayerOrigin => Record<Vector?>("Hud.LocalPlayerOrigin");
    int IEngineHud.LocalPlayerIndex => Record<int>("Hud.LocalPlayerIndex");
    string IEngineHud.Localise(string token) => Record<string>("Hud.Localise", token);
    string IEngineHud.LookupBinding(string binding) => Record<string>("Hud.LookupBinding", binding);
    TextMessage? IEngineHud.TextMessageGet(string name) => Record<TextMessage?>("Hud.TextMessageGet", name);
    void IEngineHud.ViewPunch(Vector origin, Vector punch, float radius, bool inAir) => RecordVoid("Hud.ViewPunch", origin, punch, radius, inAir);
    ViewportFacts IEngineHud.ViewportQuery(string panel) => Record<ViewportFacts>("Hud.ViewportQuery", panel);
    void IEngineHud.ViewportShow(string panel, bool show, IReadOnlyList<KeyValuePair<string, string>> data, ViewportActions actions) => RecordVoid("Hud.ViewportShow", panel, show, data.ToList(), actions);

    // WorldQuery
    (Vector Right, Vector Up) IEngineWorldQuery.AngleVectorsFromForward(Vector forward) => Record<(Vector Right, Vector Up)>("WorldQuery.AngleVectorsFromForward", forward);
    int IEngineWorldQuery.EntitiesInSphere(Vector center, float radius, Span<EntityHandle> into) => EntitiesInSphere(center, radius, into);
    int IEngineWorldQuery.GetArea(Vector origin) => Record<int>("WorldQuery.GetArea", origin);
    HullResult IEngineWorldQuery.TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask, EntityHandle ignore) => Record<HullResult>("WorldQuery.TraceHull", start, end, mins, maxs, mask, ignore);
    EntityHullTrace IEngineWorldQuery.EntityTraceHull(EntityHandle entity, Vector start, Vector end, Vector mins, Vector maxs, uint mask, int collisionGroup) => Record<EntityHullTrace>("WorldQuery.EntityTraceHull", entity, start, end, mins, maxs, mask, collisionGroup);
    TraceResult IEngineWorldQuery.TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore) => Record<TraceResult>("WorldQuery.TraceLine", start, end, mask, ignore);
    SurfaceTraceResult IEngineWorldQuery.TraceLineSurface(Vector start, Vector end, TraceMask mask, EntityHandle ignore) => Record<SurfaceTraceResult>("WorldQuery.TraceLineSurface", start, end, mask, ignore);
    ClientTraceResult IEngineWorldQuery.ClientTraceLine(Vector start, Vector end, TraceMask mask) => Record<ClientTraceResult>("WorldQuery.ClientTraceLine", start, end, mask);
    // THE FILTER AND THE PASS ENTITY ARE RECORDED, not dropped: a test that
    // asserts a trace was WORLD-ONLY is asserting the difference between a gib
    // that bounces off a door and one that falls through it, and a recording
    // that forgot which filter ran could not tell them apart.
    ClientTraceResult IEngineWorldQuery.ClientTraceLine(Vector start, Vector end, TraceMask mask, TraceFilter filter, int passEntityIndex) => Record<ClientTraceResult>("WorldQuery.ClientTraceLine", start, end, mask, filter, passEntityIndex);
    // C-Phase 3 lane DECALS. STRICT, deliberately: this is the question
    // C_BaseEntity::AddDecal's switch is made from, and a double that answered
    // "no such entity" by default would turn every decal test into a test of
    // the early return. A test that means "there is no entity 47" says so.
    ModelType? IEngineWorldQuery.ClientEntityModelType(int entIndex) => Record<ModelType?>("WorldQuery.ClientEntityModelType", entIndex);
    // THE THREE OUTS ARE EACH STUBBED SEPARATELY, and the ANSWER is stubbed
    // too. A test about a fizz is a test about WHICH RECTANGLE the bubbles were
    // spread over, so a double that answered a default box would make every
    // such assertion agree with itself -- which is the one failure mode
    // docs/te-lane-contract.md says a green suite cannot show you.
    bool IEngineWorldQuery.ClientEntityModelBounds(int entityIndex, out Vector mins,
                                                   out Vector maxs, out Vector angles)
    {
        RecordVoid("WorldQuery.ClientEntityModelBounds", entityIndex);
        mins = Value<Vector>("WorldQuery.ClientEntityModelBounds.Mins");
        maxs = Value<Vector>("WorldQuery.ClientEntityModelBounds.Maxs");
        angles = Value<Vector>("WorldQuery.ClientEntityModelBounds.Angles");
        return Value<bool>("WorldQuery.ClientEntityModelBounds");
    }

    Contents IEngineWorldQuery.PointContents(Vector origin) => Record<Contents>("WorldQuery.PointContents", origin);
    EntityHandle IEngineWorldQuery.TracePlayerAim(EntityHandle player, float range, out Vector hitPoint)
    {
        hitPoint = Value<Vector>("WorldQuery.TracePlayerAim.hitPoint");
        return Record<EntityHandle>("WorldQuery.TracePlayerAim", player, range);
    }
    WaterBelow IEngineWorldQuery.WaterSurfaceBelow(Vector start) => Record<WaterBelow>("WorldQuery.WaterSurfaceBelow", start);
    string IEngineWorldQuery.WorldChapterTitle() => Record<string>("WorldQuery.WorldChapterTitle");
    int IEngineWorldQuery.EntityModelSurfaceProp(EntityHandle entity) => Record<int>("WorldQuery.EntityModelSurfaceProp", entity);
    int IEngineWorldQuery.SurfacePropIndex(string name) => Record<int>("WorldQuery.SurfacePropIndex", name);
    SurfaceMaterial IEngineWorldQuery.SurfacePropGameMaterial(int surfaceProp) => Record<SurfaceMaterial>("WorldQuery.SurfacePropGameMaterial", surfaceProp);

    // Sound
    void IEngineSound.EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch) => RecordVoid("Sound.EmitAmbientSound", entIndex, origin, soundName, volume, soundLevel, flags, pitch);
    bool IEngineSound.EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume) => Record<bool>("Sound.EmitWorldSoundScript", soundName, origin, channel, volume);
    void IEngineSound.EmitSoundAtPosition(string soundName, Vector origin) => RecordVoid("Sound.EmitSoundAtPosition", soundName, origin);
    void IEngineSound.EntityEmitSound(EntityHandle entity, string soundName) => RecordVoid("Sound.EntityEmitSound", entity, soundName);
    void IEngineSound.ClientEmitSound(EntityHandle entity, string soundName, Vector? origin) => RecordVoid("Sound.ClientEmitSound", entity, soundName, origin);
    void IEngineSound.EntityStopSound(EntityHandle entity, string soundName) => RecordVoid("Sound.EntityStopSound", entity, soundName);
    void IEngineSound.SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption) => RecordVoid("Sound.SoundPatchEmit", patch, volume01, pitch, flags, startTime, emitCaption);
    void IEngineSound.SoundPatchStop(int patch) => RecordVoid("Sound.SoundPatchStop", patch);
    void IEngineSound.SoundPatchState(int patch, out int entIndex, out bool filterActive)
    {
        entIndex = Value<int>("Sound.SoundPatchState.entIndex");
        filterActive = Value<bool>("Sound.SoundPatchState.filterActive");
        RecordVoid("Sound.SoundPatchState", patch);
    }
    void IEngineSound.SoundPatchFree(int patch) => RecordVoid("Sound.SoundPatchFree", patch);
    SoundScriptParams? IEngineSound.SoundScriptParameters(string soundName) => Record<SoundScriptParams?>("Sound.SoundScriptParameters", soundName);
    int IEngineSound.SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel) => Record<int>("Sound.SoundPatchCreate", entity, channel, soundName, soundLevel);
    string? IEngineSound.SurfaceScrapeSound(int surfaceProps, bool smooth) => Record<string?>("Sound.SurfaceScrapeSound", surfaceProps, smooth);

    // Effects
    void IEngineEffects.BloodEffect(Vector origin, Vector direction, BloodColor color, float amount, bool stream, BloodSpray sprayFlags) => RecordVoid("Effects.BloodEffect", origin, direction, color, amount, stream, sprayFlags);
    void IEngineEffects.BubbleEffect(EntityHandle entity, int count, float speed) => RecordVoid("Effects.BubbleEffect", entity, count, speed);

    // The description is recorded WHOLE rather than by name, because what a test
    // asserts about a registration is the shape it published — the table, the
    // base and the field list — and a name says none of that.
    int IEngineEffects.TempEntRegister(TeClass description) => Record<int>("Effects.TempEntRegister", description);

    // A span cannot be boxed into the log, so the blob is recorded as its
    // LENGTH beside the filter and the delay. What a test asks of a playback is
    // usually "which class, to whom, when, and how many bytes" — and the bytes
    // themselves can be asserted by writing them through TempEntWriter, which
    // needs no engine.
    //
    // A COPY OF THE BYTES IS APPENDED, at index 4, and the length stays where it
    // was so no existing assertion moves. Writing the blob in a test to compare
    // with the blob the test just wrote is a check that cannot fail: it is one
    // person's arithmetic twice. Some of what an ITempEnts method does happens
    // BETWEEN its argument and the writer — DT_TEDust's direction is normalised
    // on the way in, because four bits over [-1, 1] would otherwise clamp a
    // longer vector into a different direction — and that step is only visible
    // in what actually went.
    //
    // A SECOND case, from DT_EffectData: m_nSurfaceProp is written as
    // value + 1 -- a send proxy that no longer exists (plan 2B). The writer
    // is handed the answer, so a test driving the writer directly would be
    // asserting its own arithmetic. The plus one is observable only here.
    void IEngineEffects.TempEntPlayback(int handle, ReadOnlySpan<byte> blob, TeFilter filter, float delay) => RecordVoid("Effects.TempEntPlayback", handle, blob.Length, filter, delay, blob.ToArray());
    int IEngineEffects.EffectDispatchIndex(string name) => Record<int>("Effects.EffectDispatchIndex", name);

    void IEngineEffects.ClientEnergySplash(Vector position, Vector direction, bool explosive) => RecordVoid("Effects.ClientEnergySplash", position, direction, explosive);

    // C-Phase 2's fifteen backends. One line each, arguments in the order the
    // slot takes them -- which is the whole value of these entries: a test can
    // assert what argument 8 of a seventeen-argument beam call was, and that is
    // the only place a transposition between IEngineEffects and Engine.cs is
    // visible without a running renderer.
    void IEngineEffects.ClientSparks(Vector position, int magnitude, int trailLength, Vector? direction) => RecordVoid("Effects.ClientSparks", position, magnitude, trailLength, direction);
    void IEngineEffects.ClientRicochet(Vector position, Vector direction) => RecordVoid("Effects.ClientRicochet", position, direction);
    void IEngineEffects.ClientMetalSparks(Vector position, Vector direction) => RecordVoid("Effects.ClientMetalSparks", position, direction);
    void IEngineEffects.ClientSmoke(Vector origin, int modelIndex, float scale, float frameRate) => RecordVoid("Effects.ClientSmoke", origin, modelIndex, scale, frameRate);
    void IEngineEffects.ClientMuzzleFlash(Vector origin, Vector angles, float scale, int type) => RecordVoid("Effects.ClientMuzzleFlash", origin, angles, scale, type);
    void IEngineEffects.ClientDecalShoot(int decalIndex, int entIndex, Vector position, Vector? sAxis, int flags) => RecordVoid("Effects.ClientDecalShoot", decalIndex, entIndex, position, sAxis, flags);
    void IEngineEffects.ClientPlayerDecalShoot(Vector position, int player, int entIndex) => RecordVoid("Effects.ClientPlayerDecalShoot", position, player, entIndex);
    int IEngineEffects.ClientDecalIndexFromName(string name) => Record<int>("Effects.ClientDecalIndexFromName", name);

    // THE PARTICLE-SYSTEM TEMP ENTITIES (C-Phase 3, lane CP). Eight named
    // primitives, not one kind+params slot; Sparks reuses ClientSparks above.
    void IEngineEffects.ClientExplosionFireball(Vector origin) => RecordVoid("Effects.ClientExplosionFireball", origin);

    // THE LEGACY TEMP ENTITY POOL (plan_tmp.md Part 5, C-Phase 4).
    //
    // The two that ANSWER are stubbed the ordinary way. ReadLife needs two
    // answers out of one call, so the life it hands back has a key of its own
    // and only ONE line goes in the log -- a read that appeared twice would
    // make every "how many times did the pool look at this slot" assertion
    // wrong. It is also the one key here that does not throw when unstubbed:
    // a test that only wants to see WHICH slots were read should not have to
    // describe a tempent to do it.
    int IEngineEffects.TempEntPoolCapacity() => Record<int>("Effects.TempEntPoolCapacity");
    void IEngineEffects.TempEntPoolDestroy(int slot) => RecordVoid("Effects.TempEntPoolDestroy", slot);
    bool IEngineEffects.TempEntPoolSimulate(int slot, float frameTime, int frameNumber) => Record<bool>("Effects.TempEntPoolSimulate", slot, frameTime, frameNumber);
    bool IEngineEffects.TempEntPoolAddVisible(int slot) => Record<bool>("Effects.TempEntPoolAddVisible", slot);
    bool IEngineEffects.TempEntPoolWriteLife(int slot, in TempEntLife life)
    {
        // The life is logged as an ARGUMENT, boxed like every other value the
        // log carries, so a test can assert what the pool wrote back rather
        // than only that it wrote.
        RecordVoid("Effects.TempEntPoolWriteLife", slot, life);
        return Value<bool>("Effects.TempEntPoolWriteLife");
    }

    bool IEngineEffects.TempEntPoolReadLife(int slot, out TempEntLife life)
    {
        RecordVoid("Effects.TempEntPoolReadLife", slot);

        life = _answers.ContainsKey("Effects.TempEntPoolReadLife.Life")
             ? Value<TempEntLife>("Effects.TempEntPoolReadLife.Life")
             : default;

        return Value<bool>("Effects.TempEntPoolReadLife");
    }

    bool IEngineEffects.TempEntPoolReadTransform(int slot, out TempEntTransform transform)
    {
        RecordVoid("Effects.TempEntPoolReadTransform", slot);

        transform = _answers.ContainsKey("Effects.TempEntPoolReadTransform.Transform")
                  ? Value<TempEntTransform>("Effects.TempEntPoolReadTransform.Transform")
                  : default;

        return Value<bool>("Effects.TempEntPoolReadTransform");
    }

    // THE TRANSFORM IS LOGGED AS AN ARGUMENT, for TempEntPoolWriteLife's
    // reason: a test asserting "the tempent ended the frame HERE" is asserting
    // what was written, and a recording that kept only the slot could not say.
    bool IEngineEffects.TempEntPoolWriteTransform(int slot, in TempEntTransform transform)
        => Record<bool>("Effects.TempEntPoolWriteTransform", slot, transform);

    float IEngineEffects.CurrentGravity() => Record<float>("Effects.CurrentGravity");
    bool IEngineEffects.ClientEntityOrigin(int entityIndex, out Vector origin)
    {
        RecordVoid("Effects.ClientEntityOrigin", entityIndex);
        origin = Value<Vector>("Effects.ClientEntityOrigin.Origin");
        return Value<bool>("Effects.ClientEntityOrigin");
    }

    // C-PHASE 5's four, on the same pool. The spawn record is logged as an
    // ARGUMENT for TempEntPoolWriteLife's reason -- a test asserting "the
    // helper spawned something" is worth much less than one asserting WHAT it
    // spawned, and every spawn-helper [Fact] in this port reads the record back
    // out of this log.
    //
    // The two model questions those helpers open with are recorded with the
    // PRECACHE area above, not here: ModelFrameCount is an existing slot that
    // gained a client arm, and ModelTypeOf is declared beside it.

    bool IEngineEffects.TempEntPoolCreate(int slot, Vector origin, int modelIndex, int priority)
    {
        RecordVoid("Effects.TempEntPoolCreate", slot, origin, modelIndex, priority);
        return Value<bool>("Effects.TempEntPoolCreate");
    }

    bool IEngineEffects.TempEntPoolWriteSpawn(int slot, in TempEntSpawn spawn)
    {
        RecordVoid("Effects.TempEntPoolWriteSpawn", slot, spawn);
        return Value<bool>("Effects.TempEntPoolWriteSpawn");
    }

    string IEngineEffects.EffectDispatchName(int index) => Record<string>("Effects.EffectDispatchName", index);
    // C-Phase 3 lane CX's three. The fourth, ClientTraceLine, is recorded with
    // the other world queries above -- a trace is not an effect.
    void IEngineEffects.ClientDecalStaticProp(Vector start, Vector end, int staticPropIndex, int decalIndex) => RecordVoid("Effects.ClientDecalStaticProp", start, end, staticPropIndex, decalIndex);
    bool IEngineEffects.ClientIsLowViolence() => Record<bool>("Effects.ClientIsLowViolence");
    string IEngineEffects.ClientDecalNameFromIndex(int decalIndex) => Record<string?>("Effects.ClientDecalNameFromIndex", decalIndex) ?? string.Empty;
    // C-Phase 3 lane DECALS's two drawing arms. The third slot of that lane,
    // ClientEntityModelType, is recorded with the world queries above -- what
    // an entity's model IS is a question, not an effect.
    void IEngineEffects.ClientDecalStudioModel(int entIndex, Vector rayStart, Vector rayEnd, int decalIndex) => RecordVoid("Effects.ClientDecalStudioModel", entIndex, rayStart, rayEnd, decalIndex);
    void IEngineEffects.ClientDecalBrushModel(int entIndex, Vector decalCenter, int decalIndex) => RecordVoid("Effects.ClientDecalBrushModel", entIndex, decalCenter, decalIndex);

    // C-Phase 6 lane FRAME's two. The statics record comes back through a
    // named out value for TempEntPoolReadLife's reason: a test that wants to
    // say "this tempent has bounceFactor 0.5" needs somewhere to say it.
    bool IEngineEffects.TempEntPoolReadStatics(int slot, out TempEntStatics statics)
    {
        RecordVoid("Effects.TempEntPoolReadStatics", slot);

        statics = _answers.ContainsKey("Effects.TempEntPoolReadStatics.Statics")
                ? Value<TempEntStatics>("Effects.TempEntPoolReadStatics.Statics")
                : default;

        return Value<bool>("Effects.TempEntPoolReadStatics");
    }

    Vector IEngineEffects.ClientWindSpeed(float time) => Record<Vector>("Effects.ClientWindSpeed", time);
    int IEngineEffects.DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => Record<int>("Effects.DrawSprite", owner, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale, hdrColorScale, glowProxySize);
    int IEngineEffects.DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize) => Record<int>("Effects.DrawSpriteOffset", owner, offset, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale, hdrColorScale, glowProxySize);
    bool IEngineEffects.ParticleSystemStart(EntityHandle owner, string name) => Record<bool>("Effects.ParticleSystemStart", owner, name);
    void IEngineEffects.ParticleSystemStop(EntityHandle owner) => RecordVoid("Effects.ParticleSystemStop", owner);
    bool IEngineEffects.ParticleSystemFollowPoints(EntityHandle owner, int pointMask) => Record<bool>("Effects.ParticleSystemFollowPoints", owner, pointMask);
    void IEngineEffects.SpriteTrailUpdate(EntityHandle owner, int renderMode, bool visible) => RecordVoid("Effects.SpriteTrailUpdate", owner, renderMode, visible);
    void IEngineEffects.SpriteTrailDestroy(EntityHandle owner) => RecordVoid("Effects.SpriteTrailDestroy", owner);
    void IEngineEffects.DustEffect(Vector origin, Vector direction, float size, float speed) => RecordVoid("Effects.DustEffect", origin, direction, size, speed);
    void IEngineEffects.ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity) => RecordVoid("Effects.ImpactEffect", start, end, surfaceProp, damageType, entity);
    void IEngineEffects.FunnelEffect(Vector origin, int spriteIndex, bool reversed) => RecordVoid("Effects.FunnelEffect", origin, spriteIndex, reversed);
    void IEngineEffects.MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type) => RecordVoid("Effects.MuzzleFlashEffect", origin, angles, scale, type);
    bool IEngineEffects.PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance, bool permanent, bool lowPriority) => Record<bool>("Effects.PaintDecal", texture, origin, angles, projectDistance, permanent, lowPriority);
    void IEngineEffects.SparkEffect(Vector origin, int magnitude, int trailLength, bool makeSound, Vector? direction) => RecordVoid("Effects.SparkEffect", origin, magnitude, trailLength, makeSound, direction);
    void IEngineEffects.ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate, int flags, int radius, int magnitude) => RecordVoid("Effects.ExplosionEffect", origin, modelIndex, scale, frameRate, flags, radius, magnitude);
    void IEngineEffects.SplashEffect(Vector origin, float scale, bool inSlime) => RecordVoid("Effects.SplashEffect", origin, scale, inSlime);
    void IEngineEffects.TracerEffect(Vector start, Vector end) => RecordVoid("Effects.TracerEffect", start, end);

    // Visibility
    void IEngineVisibility.ActivateOccluder(int occluderIndex, bool active) => RecordVoid("Visibility.ActivateOccluder", occluderIndex, active);
    uint IEngineVisibility.AreaPortalCreate(int portalNumber, int portalVersion) => Record<uint>("Visibility.AreaPortalCreate", portalNumber, portalVersion);
    void IEngineVisibility.AreaPortalDestroy(uint portal) => RecordVoid("Visibility.AreaPortalDestroy", portal);
    void IEngineVisibility.AreaPortalSetOpen(uint portal, bool open) => RecordVoid("Visibility.AreaPortalSetOpen", portal, open);
    void IEngineVisibility.AreaPortalSetWindow(uint portal, EntityHandle entity, float fadeDistance) => RecordVoid("Visibility.AreaPortalSetWindow", portal, entity, fadeDistance);
    void IEngineVisibility.ClearSkyboxParams() => RecordVoid("Visibility.ClearSkyboxParams");
    void IEngineVisibility.ClientSetWorldRenderState(WorldRenderState which, ReadOnlySpan<float> values) => RecordVoid("Visibility.ClientSetWorldRenderState", which, values.Length);
    int IEngineVisibility.ColorCorrectionAdd(string lookupFile) => Record<int>("Visibility.ColorCorrectionAdd", lookupFile);
    void IEngineVisibility.ColorCorrectionRemove(int handle) => RecordVoid("Visibility.ColorCorrectionRemove", handle);
    void IEngineVisibility.ColorCorrectionSetWeight(int handle, float weight) => RecordVoid("Visibility.ColorCorrectionSetWeight", handle, weight);
    void IEngineVisibility.DynamicLightRelease(int key) => RecordVoid("Visibility.DynamicLightRelease", key);
    void IEngineVisibility.DynamicLightUpdate(in DynamicLight light) => RecordVoid("Visibility.DynamicLightUpdate", light);
    void IEngineVisibility.SetLightStyle(int style, string pattern) => RecordVoid("Visibility.SetLightStyle", style, pattern);
    void IEngineVisibility.SetSkyboxParams(in SkyboxParams parameters) => RecordVoid("Visibility.SetSkyboxParams", parameters);
    void IEngineVisibility.SetViewerFogParams(in FogParams parameters) => RecordVoid("Visibility.SetViewerFogParams", parameters);
    void IEngineVisibility.ClearViewerFogParams() => RecordVoid("Visibility.ClearViewerFogParams");
    void IEngineVisibility.SetViewerSkyboxParams(in SkyboxParams parameters) => RecordVoid("Visibility.SetViewerSkyboxParams", parameters);
    void IEngineVisibility.ClearViewerSkyboxParams() => RecordVoid("Visibility.ClearViewerSkyboxParams");

    // plan_server.md Phase 8b / plan-audit C4. Both halves of CheckTransmit's
    // recipient pair, so a fixture can make the SDK read miss and the standalone
    // one answer -- which is the ONLY interesting case, and the one the old
    // #ifdef in C++ existed for.
    bool IEngineVisibility.TrySdkPlayerSkyboxArea(int edictIndex, out int area)
    {
        area = Value<int>("Visibility.TrySdkPlayerSkyboxArea.area");
        return Record<bool>("Visibility.TrySdkPlayerSkyboxArea", edictIndex);
    }

    bool IEngineVisibility.TryStandalonePlayerSkyboxArea(int edictIndex, out int area)
    {
        area = Value<int>("Visibility.TryStandalonePlayerSkyboxArea.area");
        return Record<bool>("Visibility.TryStandalonePlayerSkyboxArea", edictIndex);
    }


}