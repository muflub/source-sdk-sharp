using System.Runtime.Loader;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a class as a map entity implemented in C#.
///
/// The native side has no knowledge of any particular entity: it registers
/// whatever these attributes declare and instantiates one shell class for all of
/// them. Adding an entity kind is a class here and nothing else.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ScriptedEntityAttribute : Attribute
{
    public ScriptedEntityAttribute(string className, ScriptedEntityKind kind = ScriptedEntityKind.Logical)
    {
        ClassName = className;
        Kind = kind;
    }

    /// <summary>Class name as it appears in a map, e.g. "logic_timer".</summary>
    public string ClassName { get; }

    /// <summary>
    /// What kind of thing it physically is. See <see cref="ScriptedEntityKind"/>.
    ///
    /// <para>
    /// This used to choose between native shells; there is one shell now, and
    /// the kind chooses the four values it is installed with — server-only or
    /// networked, solid type, solid flags, and whether touch reports every tick
    /// or only the edges.
    /// </para>
    /// </summary>
    public ScriptedEntityKind Kind { get; }
}

/// <summary>
/// Binds a method to an input a map can fire at the entity.
///
/// The method is named for the event it handles — <c>OnEnable</c> — and the
/// "On" is dropped to give the name a map fires, "Enable". Reading as a handler
/// at the declaration and as an input from the map is the point; pass a name
/// explicitly for the rare input whose name is not a valid identifier. Matching
/// is case-insensitive. The method may
/// take nothing, a <see cref="float"/>, a <see cref="string"/>, or an
/// <see cref="InputData"/> for the cases that need the activator too — whichever
/// says what the input actually carries.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class ScriptedEventAttribute : Attribute
{
    public ScriptedEventAttribute(string? name = null)
    {
        Name = name;
    }

    /// <summary>Input name as a map fires it, or null to use the method name without its "On".</summary>
    public string? Name { get; }
}

/// <summary>
/// Everything an input carries. Taken as a parameter only by inputs that need
/// more than the value.
/// </summary>
public readonly struct InputData
{
    public InputData(string value, float number, EntityHandle activator)
    {
        Value = value;
        Number = number;
        Activator = activator;
    }

    /// <summary>The value as text, which is how the map wrote it.</summary>
    public string Value { get; }

    /// <summary>The same value as a number, 0 if it is not one.</summary>
    public float Number { get; }

    /// <summary>Whatever caused this input to fire, which may not be valid.</summary>
    public EntityHandle Activator { get; }
}

/// <summary>
/// The name a map uses for an output whose field cannot carry it: one that
/// would hide a member, as logic_relay's "OnSpawn" would its OnSpawn handler
/// and a button's "OnDamaged" its damage virtual. On the field, so a class
/// is described under its map names before any instance exists.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class OutputNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// An output a mapper can connect to, declared as a field on a
/// <see cref="ScriptedEntity"/>. The field's own name is the name the mapper
/// sees, so there is nothing to keep in step:
///
/// <code>public readonly Output OnTimer = new();</code>
///
/// Outputs are the one thing that must be declared rather than discovered at
/// use: Source binds a map's connections by walking the entity's description
/// when the map loads, so the set has to be known before any map is parsed.
/// </summary>
public sealed class Output
{
    private ScriptedEntity? _owner;

    /// <summary>Mapper-visible name: the declaring field's, or its <see cref="OutputNameAttribute"/>'s.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Slot in the owning class's output list, which is what the native side fires.</summary>
    internal int Index { get; private set; } = -1;

    internal void Bind(ScriptedEntity owner, string name, int index)
    {
        _owner = owner;
        Name = name;
        Index = index;
    }

    /// <summary>How many times this output has fired. Answers "did that connection ever run?".</summary>
    public int FireCount { get; private set; }

    /// <summary>The value carried by the last firing, or null if it carried none.</summary>
    public string? LastValue { get; private set; }

    /// <summary>
    /// Puts the tally back after a hot reload, and touches nothing else.
    ///
    /// <para>
    /// There is deliberately no way to restore a CONNECTION here, and the reason
    /// CHANGED in step 2a. It used to be that a connection could not be lost
    /// because it was never managed: a map's
    /// <c>"OnDamaged" "plug2,EnableMotion,,0,-1"</c> was parsed by the native
    /// shell's own <c>KeyValue</c> into a <c>CEventAction</c> list on
    /// <c>COutputEvent m_Outputs[]</c>, native memory a managed reload could
    /// not reach — which also meant a one-shot's remaining firings, and any
    /// event already in flight, were beyond saving.
    /// </para>
    ///
    /// <para>
    /// Both are managed now and both ARE carried, but not from here: a
    /// connection belongs to the entity's row in
    /// <see cref="EntityDirectory"/>, not to this tally, and the whole I/O half
    /// is saved and restored in one piece by
    /// <see cref="EntityDirectory.SaveIo"/>. What this still carries is the
    /// count of what has already happened, which is what answers "did that
    /// connection ever run?" and is worth carrying so the answer does not reset
    /// to zero every time somebody reloads.
    /// </para>
    /// </summary>
    internal void Restore(int fireCount, string? lastValue)
    {
        FireCount = fireCount;
        LastValue = lastValue;
    }

    /// <summary>Fires this output with no value.</summary>
    public void Fire() => Fire((string?)null);

    /// <summary>
    /// Fires this output naming what CAUSED it, carrying no value.
    ///
    /// The activator is what a downstream input reads to decide who it is acting
    /// on behalf of — a trigger telling a game_text WHICH player walked in, or a
    /// path_track telling a func_trackautochange WHICH train arrived. It may name
    /// an entity of either hierarchy.
    ///
    /// Omitting it does NOT mean "nobody". An output fired from inside an input
    /// handler carries whoever caused that input onward, and one fired from a
    /// think with no cause at all falls back to the firing entity, which is what
    /// the SDK writes as FireOutput( this, this ). Naming one here overrides
    /// both, and is only needed where stock passes an activator explicitly.
    /// </summary>
    public void Fire(EntityHandle activator) => Fire(null, activator);

    /// <summary>Fires this output carrying a value, for outputs that pass one on.</summary>
    public void Fire(string? value) => Fire(value, EntityHandle.None);

    /// <summary>Fires this output carrying a value and naming what caused it.</summary>
    public void Fire(string? value, EntityHandle activator)
    {
        if (_owner is null)
            return;

        FireCount++;
        LastValue = value;

        // THE OUTPUT NEVER LEAVES MANAGED CODE ANY MORE -- plan_entity.md step
        // 2a. This was Engine.FireScriptedOutput: out to
        // CStandaloneEntity::FireNamedOutput, which resolved the name against
        // the list C# had declared, walked a native CEventAction list C# could
        // not see, and posted into g_EventQueue -- from where the input came
        // BACK in through ENTITY_OP_INPUT to reach another C# entity. Two
        // crossings to carry a message from one managed object to another, and
        // a connection list that no reload could reach. The connections and the
        // queue are both in the directory now, so this costs none.
        _owner.Owner?.FireOutput(_owner.Handle, Name, value, activator);

        // After the queue, not before: a subscriber watching the log should see
        // an output reported in the order the game actually did things.
        _owner.Owner?.RaiseOutputFired(_owner, Name, value);
    }

    /// <summary>Fires this output carrying a number.</summary>
    public void Fire(float value) => Fire(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Base for a map entity whose behaviour lives in C#. One instance exists per
/// entity in the map, created when it spawns.
///
/// Everything is optional — an entity that only needs to respond to inputs
/// overrides <see cref="OnInput"/> and nothing else.
/// </summary>
public abstract class ScriptedEntity
{
    /// <summary>The entity this instance drives.</summary>
    public EntityHandle Handle { get; internal set; }

    /// <summary>Class name the map used, which may be one of several for a shared implementation.</summary>
    public string ClassName { get; internal set; } = string.Empty;

    /// <summary>
    /// CBaseEntity::SetClassname: the name this entity reports and is found by
    /// from here on, in the engine and in the directory. What it was made as
    /// stays, so a rename is only ever to another name of the same class.
    /// </summary>
    protected void SetClassName(string className)
    {
        Entities.SetClassName(Handle, className);
        if (Owner is { } owner)
            owner.Directory.SetClassName(this, className);
        else
            ClassName = className;
    }

    /// <summary>
    /// The name the mapper gave this entity, if any.
    ///
    /// <para>
    /// THE DIRECTORY'S INDEX FIRST (step 2a), and the engine only for what the
    /// index cannot hold. The index is fed by the <c>targetname</c> keyvalue
    /// and is what every output already resolves its target against, so asking
    /// it is both free and the same answer; <c>Entities.GetName</c> is a
    /// crossing per read, and this property is read on every unhandled-input
    /// message and every save record. Two answers for one question is a hazard,
    /// so the fall-back is narrow: an entity named through some route other than
    /// a map key.
    /// </para>
    /// </summary>
    public string Name
    {
        get
        {
            string named = Owner?.Directory.NameOf(Handle) ?? string.Empty;
            return named.Length > 0 ? named : Entities.GetName(Handle);
        }
    }

    // The registry this instance belongs to. Needed so one entity can find
    // another by name — a trigger asking the filter that decides who it
    // responds to, which is the whole reason filters exist.
    internal ScriptedEntityRegistry? Owner { get; set; }

    /// <summary>
    /// This entity's log: its registry's, over its side's console (statics to
    /// zero: it was the static Log). An entity no registry owns (one a fact
    /// built on its own) prints to standard error.
    /// </summary>
    protected internal ILog Log => Owner?.Log ?? (_unownedLog ??= new StandardErrorLog());

    private ILog? _unownedLog;

    /// <summary>
    /// The engine, for the calls an entity makes about ITSELF: where it is,
    /// what it is called, how much health it has left.
    ///
    /// <para>
    /// On the base class rather than in every constructor, and the line is
    /// drawn at the SUBJECT rather than at convenience. This is the one area
    /// whose subject is always this entity, which is why the base class already
    /// wraps three of its calls as <see cref="Health"/>,
    /// <see cref="SpawnFlags"/> and <see cref="Name"/> — it is of the same
    /// nature as <see cref="Handle"/>, which nobody would inject either. Of 147
    /// entity classes that touch the engine, 93 touch this one.
    /// </para>
    ///
    /// <para>
    /// Every OTHER area is a CONSTRUCTOR PARAMETER, because every other area
    /// acts on something else — the world, another entity, a player, the
    /// screen — and a class that does that should say so in its signature,
    /// where a reader and the compiler both see it. A base class needing one
    /// makes its subclasses declare and pass it down; that cascade is the price
    /// of the dependency being visible, and it is paid by 146 classes of which
    /// most take one parameter and the widest takes seven.
    /// </para>
    ///
    /// <para>
    /// Set by the registry from its side's container (plan_abi_hotreload D8).
    /// There is no fallback: an instance nobody bound has no engine, and asking
    /// it for one is an error naming the area rather than a call into whichever
    /// side happened to be active.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <c>internal</c> as well for one reader: the registry's AddOutput hands a
    /// keyvalue to the entity's OWN KeyValue, which is this area's.
    /// </remarks>
    protected internal IEngineEntities Entities => _entities ?? throw Unbound(nameof(IEngineEntities));

    private IEngineEntities? _entities;

    /// <summary>Handed over by the registry. See <see cref="Entities"/>.</summary>
    internal void BindEngine(IEngineEntities entities) => _entities = entities;

    /// <summary>
    /// The name lookup (<see cref="IEngineEntityNames"/>), a seam of its own
    /// since plan_shim_areas_split phase 4, bound with <see cref="Entities"/>
    /// and for its reason.
    /// </summary>
    protected internal IEngineEntityNames NameLookup => _entityNames ?? throw Unbound(nameof(IEngineEntityNames));

    private IEngineEntityNames? _entityNames;

    // The areas this base's own helpers (SetModel, Mass, MakePickup, SetVisible
    // and the rest) act through, bound with Entities and for its reason: the
    // subject is always this entity. Private, so a subclass that takes one of
    // these areas by constructor keeps its own name for it.
    private IEngineEntityRender? _render;
    private IEngineEntityPhysics? _physics;
    private IEnginePlayers? _players;
    private IEngineHost? _host;

    // The side's surfaceprop table (plan_abi_hotreload Phase 1: it was a
    // static over Engine.Current). Optional, as the static was: with none, a
    // surface answers default (no material), which is what an unattached side
    // answered.
    private PhysicsSurfaces? _surfaces;

    private IEngineEntityRender Render => _render ?? throw Unbound(nameof(IEngineEntityRender));
    private IEngineEntityPhysics Physics => _physics ?? throw Unbound(nameof(IEngineEntityPhysics));
    private IEnginePlayers Players => _players ?? throw Unbound(nameof(IEnginePlayers));

    /// <summary>
    /// Every area the base's helpers reach, from one container: the registry's
    /// at creation, or a test's (plan_abi_hotreload D8).
    /// </summary>
    internal void BindAreas(IServiceLocator engine)
    {
        _entities = engine.Require<IEngineEntities>();
        _entityNames = engine.Require<IEngineEntityNames>();
        _movement = engine.Require<IEngineBrushMovement>();
        _render = engine.Require<IEngineEntityRender>();
        _physics = engine.Require<IEngineEntityPhysics>();
        _players = engine.Require<IEnginePlayers>();
        _host = engine.Require<IEngineHost>();
        _surfaces = engine.Resolve(typeof(PhysicsSurfaces)) as PhysicsSurfaces;
    }

    // How this entity's restore re-checks a saved handle: against its own
    // side's engine (plan_abi_hotreload D8).
    private SavedHandles SavedHandles
        => new(_host ?? throw Unbound(nameof(IEngineHost)), Entities);

    private InvalidOperationException Unbound(string area)
        => new($"{GetType().Name} has no {area}: it was not built by a registry, and nothing bound one");

    /// <summary>
    /// Moving as a BRUSH MOVER — travel to a place, turn to a facing, spin,
    /// stop. A second bound area rather than a constructor parameter, for
    /// <see cref="Entities"/>'s reason exactly: <see cref="MoveTo"/>,
    /// <see cref="RotateTo"/> and <see cref="Spin"/> are helpers ON THIS BASE,
    /// so every one of the two dozen classes that moves would otherwise have to
    /// declare and pass the area down to reach its own base's method.
    ///
    /// <para>
    /// plan_entity.md step 2g. It used to be four static <c>Engine</c> calls
    /// into a native stepper. The stepper is
    /// <c>SourceSharp.Server/EntityMotion.cs</c> now, and the server module
    /// registers itself for this contract; the fallback below is the engine, so
    /// a test that has never heard of any of it still resolves.
    /// </para>
    /// </summary>
    protected IEngineBrushMovement Movement => _movement ?? throw Unbound(nameof(IEngineBrushMovement));

    private IEngineBrushMovement? _movement;

    /// <summary>Handed over by the registry. See <see cref="Movement"/>.</summary>
    internal void BindBrushMovement(IEngineBrushMovement movement) => _movement = movement;

    /// <summary>
    /// The <c>[ScriptVars]</c> sets this entity holds, in the order a key is
    /// offered to them. Found once when the instance is created rather than per
    /// key, since a map hands an entity every key it has and the answer cannot
    /// change between two of them.
    /// </summary>
    public ScriptedVarSet[] VarSets { get; internal set; } = Array.Empty<ScriptedVarSet>();

    /// <summary>
    /// The <c>[NetState]</c> objects this entity holds, in declaration order.
    ///
    /// Kept for the same reason <see cref="VarSets"/> is, and it used to be
    /// thrown away: the registry bound them and dropped the array, so anything
    /// wanting to walk an entity's networked state had to reflect for it again.
    /// The client's half has always kept them.
    /// </summary>
    public NetState[] NetStates { get; internal set; } = Array.Empty<NetState>();

    /// <summary>
    /// The <c>Output</c> objects this entity declares, in slot order.
    ///
    /// The WIRING is not in them and never was — a connection's target, input,
    /// parameter, delay and remaining fire count are a native
    /// <c>CEventAction</c> list on the shell, parsed straight out of the map's
    /// keyvalue. What an <c>Output</c> holds is its name, its slot, and the
    /// tally of what it has done.
    /// </summary>
    public Output[] Outputs { get; internal set; } = Array.Empty<Output>();

    //-------------------------------------------------------------------------
    // Hot reload: everything about this entity that must outlive the assembly.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Writes this entity's state into a form that survives a managed reload.
    ///
    /// <para>
    /// A RELOAD DESTROYS EVERY MANAGED OBJECT AND KEEPS EVERYTHING NATIVE. The
    /// entity, its edict, its blob, its output connections and its place in the
    /// world all survive untouched; this object does not. Recreating it without
    /// carrying anything across would give the map's <c>func_door</c> back with
    /// stock's speed instead of the mapper's, because a map's keyvalues are
    /// handed over during the parse and kept nowhere — and an entity that
    /// silently reverted to its declared defaults is a worse failure than one
    /// that stopped working, since nothing about it looks wrong.
    /// </para>
    ///
    /// <para>
    /// THE DEFAULT COVERS EVERYTHING THAT CAN BE DISCOVERED: every declared map
    /// key the map actually wrote, with the stamp that says when; every
    /// networked field, by name and by raw bits; and every output's tally. What
    /// it cannot discover is a plain field that is neither a map key nor
    /// networked — a door's current position on its path, a counter's count —
    /// and that is the only thing an override is for.
    /// </para>
    ///
    /// <para>
    /// <b>An override must call <c>base.SaveState(state)</c></b>, and the one
    /// that forgets is caught rather than trusted: the base sets
    /// <see cref="SavedEntity.DefaultRan"/>, the registry checks it, and a class
    /// that skipped it is named in a warning and has the default run for it
    /// anyway. Losing every map key on one class because somebody wrote an
    /// override is exactly the quiet wrongness this whole mechanism exists to
    /// stop.
    /// </para>
    ///
    /// <para>
    /// Values go across as text and nothing else. This object lives in the ABI,
    /// which lives in the default load context, so anything of the side's it
    /// held would keep the whole side alive forever — see <see cref="SavedEntity"/>.
    /// </para>
    /// </summary>
    public virtual void SaveState(SavedEntity state) => SaveDeclaredState(state);

    /// <summary>
    /// The discoverable half, reachable without going through the virtual.
    ///
    /// The registry calls it when an override skipped <c>base.SaveState</c>, so
    /// that the mistake costs a warning rather than every map key on the class.
    /// </summary>
    internal void SaveDeclaredState(SavedEntity state)
    {
        state.DefaultRan = true;

        foreach (var set in VarSets)
        {
            foreach (var key in set.Descriptors)
            {
                // Only what the map WROTE. A key still holding its declared
                // initialiser is restored by the new instance's own
                // initialiser, and replaying it would make the entity report a
                // key as set that nobody ever set.
                if (!key.IsSet(set))
                    continue;

                state.SaveVar(key.Name, key.Save(set), key.LastSetAt(set));
            }
        }

        foreach (var netState in NetStates)
            NetStateTransfer.Save(netState, state);

        foreach (var output in Outputs)
            state.SaveOutput(output.Name, output.FireCount, output.LastValue);

        SaveMarkedState(state);
        SaveCustomState(state);

        SaveNativeState(state);
    }

    /// <summary>
    /// The key every entity's custom record is carried under, inside the
    /// record's own local store.
    ///
    /// <para>
    /// PREFIXED RATHER THAN A STORE OF ITS OWN, because the local store is
    /// already carried by BOTH paths — the reload and
    /// <c>SavedEntityCodec</c>'s v4 block — so a class's custom state reaches a
    /// <c>.sav</c> with no format change and no second mechanism. The prefix is
    /// what keeps it from colliding with a hand-written <c>Set</c> in an
    /// override.
    /// </para>
    /// </summary>
    internal const string CustomPrefix = "custom.";

    /// <summary>
    /// This class's <see cref="SaveFieldAttribute"/> members: through the
    /// registry's catalog, which remembers them per type for its load
    /// (statics to zero), or walked once for an entity no registry owns.
    /// </summary>
    private SaveFieldInfo[] MarkedFields()
        => Owner is { } owner ? owner.SaveFields.For(GetType()) : SaveFieldCatalog.Build(GetType(), Log);

    /// <summary>
    /// The MARKED tier: every field or property carrying
    /// <see cref="SaveFieldAttribute"/>.
    ///
    /// <para>
    /// Derived from the attribute and from nothing else —
    /// <see cref="SaveFieldCatalog"/> is the one reader, so there is no second
    /// list to drift against. A value that will not serialise is named and
    /// skipped rather than taking the entity's whole record down with it.
    /// </para>
    /// </summary>
    private void SaveMarkedState(SavedEntity state)
    {
        foreach (SaveFieldInfo marked in MarkedFields())
        {
            object? value = marked.Read(this);

            if (value is null)
            {
                state.Set(marked.Key, string.Empty);
                continue;
            }

            // A LEAF GOES THROUGH THE SAME SPELLING THE MAP KEYS USE, so a
            // vector written here reads the way a vector written anywhere else
            // in this record reads. Anything else is an object, and goes
            // through the graph writer with its refusals.
            if (value is string or bool or int or float or Vector or EntityHandle
                or byte or sbyte or short or ushort or uint or long or ulong or double
                or QAngle || value.GetType().IsEnum)
            {
                state.Set(marked.Key, SaveGraph.LeafText(value));
                continue;
            }

            string? text = SaveGraph.Write(value, $"{ClassName} '{Name}'", marked.Key, Log);

            if (text is not null)
                state.Set(marked.Key, text);
        }
    }

    /// <summary>
    /// The CUSTOM tier: a class that implements <see cref="ISaveCustom"/> and
    /// hands over state the declarations cannot describe.
    ///
    /// <para>
    /// A record of its own, flattened into the local store — see
    /// <see cref="CustomPrefix"/>. The record is dropped when the class wrote
    /// nothing, so a class that implements the interface and has nothing to say
    /// on this entity costs one row count and no keys.
    /// </para>
    /// </summary>
    private void SaveCustomState(SavedEntity state)
    {
        if (this is not ISaveCustom custom)
            return;

        var record = new ReloadRecord(GetType().FullName ?? ClassName);
        var access = new SaveRecordAccess(record, $"{ClassName} '{Name}'", Log);

        try
        {
            custom.OnSave(access);
        }
        catch (Exception e)
        {
            // NAMED AND SKIPPED, exactly as Collect treats a throwing
            // SaveState: one class's bug must not cost this entity its
            // DECLARED state, which is already in the record by now.
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': OnSave threw ({e.Message}); " +
                           $"its custom state is not carried. Its declared state still is\n");
            return;
        }

        if (record.IsEmpty)
            return;

        ReloadRecordText.Flatten(record, CustomPrefix, state.Set);
    }

    /// <summary>
    /// Reads the NATIVE half of this entity into the record — where it is, how
    /// fast it is going, what health it has left.
    ///
    /// <para>
    /// <b>WHY THERE IS ANYTHING TO DO HERE AT ALL.</b> The native half of an
    /// entity was never written to a <c>.sav</c>: <c>CShellGameDll::Save</c>
    /// makes one call, which writes this block, <c>WriteSaveHeaders</c> writes
    /// nothing, and the file has no <c>ETABLE</c>. Measured twice from opposite
    /// directions — <c>tools/save-live-gate</c> damaged a
    /// <c>func_breakable_surf</c> from 5 to 4, saved, loaded and got 5 back,
    /// and stage 3 could not build a player-health differential for the same
    /// reason: the health both could reach was <c>Vars.Health</c>, the map key,
    /// and the damage was in the shell's <c>m_iHealth</c>.
    /// </para>
    ///
    /// <para>
    /// <b>THROUGH <see cref="Entities"/>, WHICH IS WHY THE LIST IS THIS LIST.</b>
    /// Every field here is one the bound <see cref="IEngineEntities"/> can both
    /// read and write. That is not a coincidence and it is not a shortcut: it
    /// is what makes the whole section drivable against the engine double, so
    /// the field list and the re-apply order below are held by facts rather
    /// than by a game nobody can run in CI. What was left out for want of a
    /// direction, or of a binding, is listed on <see cref="NativeField"/>.
    /// </para>
    ///
    /// <para>
    /// VIRTUAL SO A CLASS CAN DECLINE. An entity whose position is not its own
    /// — one that is carried, or that computes where it belongs on every think
    /// — is better off coming back and working it out than being put down where
    /// it happened to be. Nothing in the tree overrides it yet, and the hook is
    /// here because the alternative is somebody adding a bool.
    /// </para>
    /// </summary>
    protected internal virtual void SaveNativeState(SavedEntity state)
    {
        // NOTHING, RATHER THAN ZEROES, WHEN THERE IS NO ENGINE. Every getter
        // below answers `default` with no native side under this object -- and
        // `default` for an origin is (0,0,0), which is a REAL COORDINATE and
        // not an absence. A record that carried it would put every entity in
        // the middle of the map on restore, plausibly, with nothing to say so.
        // An EMPTY native store is how "nothing was read" is spelled.
        if (!HasNativeSide)
            return;

        // IN THE ORDER LoadNativeState PUTS THEM BACK, which is not a
        // requirement -- a dictionary has no order and the restore reads by
        // name -- and is done because the two lists are read together and a
        // field present in one and missing from the other is the failure
        // NativeField's constants exist to prevent. Two lists in one order can
        // be diffed by eye; two lists in two orders cannot.
        state.SaveNative(NativeField.SpawnFlags, Entities.GetSpawnFlags(Handle));

        (Vector mins, Vector maxs) = Entities.GetBounds(Handle);

        state.SaveNative(NativeField.Mins, mins);
        state.SaveNative(NativeField.Maxs, maxs);

        state.SaveNative(NativeField.SolidType, (int)Entities.GetSolidType(Handle));
        state.SaveNative(NativeField.SolidFlags, (int)Entities.GetSolidFlags(Handle));

        state.SaveNative(NativeField.Origin, Entities.GetOrigin(Handle));
        state.SaveNative(NativeField.Angles, Entities.GetAngles(Handle));
        state.SaveNative(NativeField.Velocity, Entities.GetVelocity(Handle));
        state.SaveNative(NativeField.AngularVelocity, Entities.GetAngularVelocity(Handle));
        state.SaveNative(NativeField.Gravity, Entities.GetGravity(Handle));
        state.SaveNative(NativeField.Health, Entities.GetHealth(Handle));
        state.SaveNative(NativeField.Armor, Entities.GetArmor(Handle));

        // SECONDS FROM NOW, which is what the getter answers and what the
        // setter takes. A load restarts curtime, so an absolute think time
        // would name a moment that has gone.
        state.SaveNative(NativeField.NextThink, NextThinkDelay());
    }

    /// <summary>
    /// Whether this object has a native entity under it to read and write.
    ///
    /// <para>
    /// The same split <c>NetState.Bind</c> and <c>RestoreFrom</c> make, and for
    /// the same reason: "what is this entity's origin" is a question with a
    /// real answer only when there is something to ask, and a registry can be
    /// driven with no native side at all.
    /// </para>
    ///
    /// <para>
    /// VIRTUAL SO THE SECTION CAN BE TESTED AGAINST THE ENGINE DOUBLE. Without
    /// it the only observable behaviour with no engine is "nothing happens",
    /// which every possible implementation satisfies — including one that reads
    /// no fields and one that applies them in the wrong order. A fixture that
    /// says yes and is handed a <c>RecordingEngine</c> exercises the real
    /// bodies above and below, in the real order, and that is the only way the
    /// order is a fact rather than a comment.
    /// </para>
    /// </summary>
    protected internal virtual bool HasNativeSide => _host?.IsInitialized ?? false;

    /// <summary>
    /// Puts the native half back, in an order that is the whole of this
    /// method's content.
    ///
    /// <para>
    /// <b>IT RUNS LAST, AFTER EVERYTHING ELSE A RESTORE DOES, AND THAT IS THE
    /// POINT.</b> A savegame load in this port is a <c>map</c> plus an overlay:
    /// <c>MapLoadSaveGame</c> and <c>MapLoadNewGame</c> call the identical
    /// <c>SpawnTheMap</c>, and the savegame arm's only difference is one extra
    /// <c>LoadSaveGameState</c> afterwards. So by the time this runs the map
    /// lump has already given every one of these fields a value, and the
    /// entity's own <c>LoadState</c> may have given some of them another. The
    /// saved value has to overwrite both, or this is the original bug one layer
    /// down: a position written into a record and then thrown away by the
    /// spawn.
    /// </para>
    ///
    /// <para>
    /// <b>AND THE ORDER WITHIN IT IS NOT THE DICTIONARY'S.</b> A dictionary has
    /// no order; these are applied in a written-down sequence because two of
    /// them interact:
    /// </para>
    ///
    /// <list type="number">
    /// <item><description>
    /// <b>Origin, then angles</b> — the transform, before anything expressed in
    /// its frame.
    /// </description></item>
    /// <item><description>
    /// <b>Then velocity and angular velocity</b>, because moving an entity can
    /// zero or perturb them: a <c>SetOrigin</c> on a simulated object goes
    /// through the teleport funnel, and restoring the motion first would hand
    /// it to the placement to undo.
    /// </description></item>
    /// <item><description>
    /// <b>Health and armour last</b>, because everything above can CHANGE them.
    /// Putting an entity back where it was can drop it into the
    /// <c>trigger_hurt</c> it was standing in, and that damage arrives before
    /// this method is done. Health written first would be the health the world
    /// then modified; written last it is the health that was saved.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>A FIELD THE RECORD DOES NOT CARRY IS LEFT ALONE.</b> Not defaulted:
    /// an older save, or one written by a build that did not know about a
    /// field, must leave the spawn's value standing rather than replace it with
    /// a zero. <see cref="SavedEntity.HasNative"/> is the question, and every
    /// line below asks it.
    /// </para>
    /// </summary>
    protected internal virtual void LoadNativeState(SavedEntity state)
    {
        if (!HasNativeSide)
            return;

        // 1. THE FLAGS THE CLASS'S OWN LOGIC READS, before anything physical.
        //    Nothing below reads or writes them, so this is not a dependency;
        //    it is first because it is the field that is not a measurement.
        if (state.HasNative(NativeField.SpawnFlags))
            Entities.SetSpawnFlags(Handle, state.NativeInt(NativeField.SpawnFlags));

        // 2. THE SHAPE, and bounds before solidity within it. SetSolidity is
        //    the call that CREATES or DESTROYS the spatial-partition handle
        //    (CStandaloneEntity::UpdatePartition), and it inserts using
        //    whatever bounds are current at that moment; SetCollisionBounds
        //    only MOVES an element that already exists. Bounds first therefore
        //    guarantees the one insert carries the saved box. The other order
        //    is not wrong so much as it is an insert with the spawn's box
        //    followed by a move, which is the same answer by luck.
        //
        //    BOTH KEYS OR NEITHER, in each pair. A box is two corners and a
        //    solidity is a type and its flags; half of either is not a smaller
        //    restore, it is a shape nobody saved.
        if (state.HasNative(NativeField.Mins) && state.HasNative(NativeField.Maxs))
        {
            Entities.SetBounds(Handle,
                               state.NativeVector(NativeField.Mins),
                               state.NativeVector(NativeField.Maxs));
        }

        if (state.HasNative(NativeField.SolidType) && state.HasNative(NativeField.SolidFlags))
        {
            Entities.SetSolidity(Handle,
                                 (SolidType)state.NativeInt(NativeField.SolidType),
                                 (SolidFlags)state.NativeInt(NativeField.SolidFlags));
        }

        // 3. The transform, before anything expressed in its frame -- and now
        //    also after the shape it is resolved against.
        //    A TELEPORT, because it moves the physics object as well as the
        //    entity. A load here is a fresh map plus this overlay, so a prop's
        //    physics object stands at its SPAWN pose; SetOrigin alone left it
        //    there, the next physics update dragged the entity back, and a
        //    crate put back under a shelf was ejected into it. Not zeroed: the
        //    record's own velocity is step 4.
        if (state.HasNative(NativeField.Origin))
        {
            Vector? angles = state.HasNative(NativeField.Angles)
                ? state.NativeVector(NativeField.Angles)
                : null;

            Entities.Teleport(Handle, state.NativeVector(NativeField.Origin), angles,
                              zeroVelocity: false);
        }
        else if (state.HasNative(NativeField.Angles))
        {
            Entities.SetAngles(Handle, state.NativeVector(NativeField.Angles));
        }

        // 4. Then the motion, which moving the entity can zero or perturb.
        if (state.HasNative(NativeField.Velocity))
            Entities.SetVelocity(Handle, state.NativeVector(NativeField.Velocity));

        if (state.HasNative(NativeField.AngularVelocity))
            Entities.SetAngularVelocity(Handle, state.NativeVector(NativeField.AngularVelocity));

        // 5. Gravity with the motion it governs. ITS POSITION IS FREE and that
        //    is stated so nobody infers a constraint that is not there: nothing
        //    else in this method reads or writes it, and nothing it does
        //    changes anything else. It is here because this is where a reader
        //    looks for it.
        if (state.HasNative(NativeField.Gravity))
            Entities.SetGravity(Handle, state.NativeFloat(NativeField.Gravity));

        // 6. Health and armour after everything that can CHANGE them: putting
        //    an entity back where it was can drop it into the trigger_hurt it
        //    was standing in, and that damage arrives before this method is
        //    done.
        if (state.HasNative(NativeField.Health))
            Entities.SetHealth(Handle, state.NativeInt(NativeField.Health));

        if (state.HasNative(NativeField.Armor))
            Entities.SetArmor(Handle, state.NativeInt(NativeField.Armor));

        // 7. AND THE SCHEDULE LAST, after health rather than with it, for the
        //    same shape of reason one step along: everything above can
        //    RESCHEDULE it. A solidity change re-inserts into the partition and
        //    a placement re-runs the touch sweep; a touch fires an input, and
        //    an input can call SetNextThink. The saved schedule has to be the
        //    one left standing, so it is written after the last thing that
        //    could overwrite it.
        if (state.HasNative(NativeField.NextThink))
            SetNextThink(state.NativeFloat(NativeField.NextThink));
    }

    /// <summary>
    /// Puts back what <see cref="SaveState"/> carried across, on a brand new
    /// instance of a brand new build.
    ///
    /// <para>
    /// Runs AFTER the instance exists and is bound, and BEFORE anything asks it
    /// to do anything. It does not re-run <c>OnSpawn</c>: the entity already
    /// spawned, in the world that is still standing, and spawning it twice would
    /// re-precache, re-emit and re-announce everything it did the first time.
    /// </para>
    ///
    /// <para>
    /// Four things can no longer fit and each has an answer. A key that the new
    /// build no longer declares is DROPPED and named. A key that is new was
    /// never saved and comes up holding its own initialiser, reporting
    /// correctly that nobody set it. A key whose type changed fails to parse and
    /// is reported by the same path a bad map value takes — never coerced. And a
    /// networked field whose width or kind changed is dropped rather than
    /// reinterpreted.
    /// </para>
    ///
    /// <para>
    /// The same rule as <see cref="SaveState"/>: an override calls
    /// <c>base.LoadState(state)</c>, and one that does not is named.
    /// </para>
    /// </summary>
    public virtual void LoadState(SavedEntity state) => LoadDeclaredState(state);

    /// <summary>
    /// The discoverable half of the restore, reachable without going through the
    /// virtual. See <see cref="SaveDeclaredState"/>.
    /// </summary>
    internal void LoadDeclaredState(SavedEntity state)
    {
        state.DefaultRan = true;

        foreach (var pair in state.Vars)
        {
            bool claimed = false;

            foreach (var set in VarSets)
            {
                if (!set.OnKeyDataUpdate(pair.Key, pair.Value.Value))
                    continue;

                // The value is back; the HISTORY is not, until this. Replaying
                // a key stamps it with the time of the replay, so without this
                // a var the map wrote at 0.0 would come back claiming it was
                // written when the reload happened.
                //
                // ONLY IF THE REPLAY ACTUALLY TOOK, and that is not the same
                // question as whether the key was CLAIMED. OnKeyDataUpdate
                // answers "this is my key" and returns true for a value it
                // could not read, deliberately, so that the complaint names the
                // right entity — and a stamp written anyway would turn a
                // refused value into a key reporting itself set, holding the
                // class's default, with a timestamp. Exactly the shape of thing
                // somebody would spend an afternoon on.
                RestoreStamp(set, pair.Key, pair.Value.LastSetAt);

                claimed = true;
                break;
            }

            if (!claimed)
            {
                Log.Warning($"[SourceSharp] reload: {ClassName} '{Name}' no longer declares " +
                               $"the map key '{pair.Key}' (was '{pair.Value.Value}'); it is " +
                               $"dropped rather than carried\n");
            }
        }

        foreach (var netState in NetStates)
            NetStateTransfer.Load(netState, state, ClassName, Name, Log);

        foreach (var output in Outputs)
        {
            if (state.Outputs.TryGetValue(output.Name, out var saved))
                output.Restore(saved.FireCount, saved.LastValue);
        }

        LoadMarkedState(state);
        LoadCustomState(state);
    }

    /// <inheritdoc cref="SaveMarkedState"/>
    private void LoadMarkedState(SavedEntity state)
    {
        foreach (SaveFieldInfo marked in MarkedFields())
        {
            if (!state.Has(marked.Key))
            {
                // NEW IN THIS BUILD. Never saved, so it comes up holding its own
                // initialiser and reports correctly that nobody set it. Not a
                // warning: it is the ordinary case for a field somebody added.
                continue;
            }

            if (!marked.CanWrite)
            {
                Log.Warning($"[SourceSharp] {ClassName} '{Name}': [SaveField] " +
                               $"'{marked.Key}' was carried but {marked.DeclaredBy}." +
                               $"{marked.MemberName} cannot be written, so it is NOT restored. " +
                               $"A get-only member is saved for a reader and comes back " +
                               $"holding whatever this build computes\n");
                continue;
            }

            string raw = state.GetString(marked.Key);

            if (raw.Length == 0)
            {
                if (!marked.MemberType.IsValueType)
                    marked.Write(this, null);
                continue;
            }

            if (TryParseMarked(raw, marked.MemberType, out object? value))
            {
                if (marked.MemberType == typeof(EntityHandle))
                    value = SavedHandles.Resolve((EntityHandle)value!);

                marked.Write(this, value);
                continue;
            }

            object? built = SaveGraph.Read(raw, marked.MemberType, $"{ClassName} '{Name}'",
                                           marked.Key, SavedHandles, Log);

            if (built is not null)
                marked.Write(this, built);
        }
    }

    /// <summary>
    /// One marked value out of its text, for the types the map-key spelling
    /// already covers. False means "this is not a leaf", which is the caller's
    /// signal to go through the graph reader — NOT "this value is bad".
    /// </summary>
    private static bool TryParseMarked(string raw, Type type, out object? value)
    {
        value = null;

        if (type == typeof(string)) { value = raw; return true; }

        if (type == typeof(bool))
        {
            if (!ScriptedVarParse.Bool(raw, out bool b)) return false;
            value = b; return true;
        }

        if (type == typeof(int))
        {
            if (!ScriptedVarParse.Int(raw, out int i)) return false;
            value = i; return true;
        }

        if (type == typeof(float))
        {
            if (!ScriptedVarParse.Float(raw, out float f)) return false;
            value = f; return true;
        }

        if (type == typeof(Vector))
        {
            if (!ScriptedVarParse.Vector(raw, out var v)) return false;
            value = v; return true;
        }

        if (type == typeof(QAngle))
        {
            if (!ScriptedVarParse.QAngle(raw, out var q)) return false;
            value = q; return true;
        }

        if (type == typeof(EntityHandle))
        {
            if (!uint.TryParse(raw, System.Globalization.NumberStyles.Integer,
                               ReloadRecord.Culture, out uint h))
            {
                return false;
            }

            value = new EntityHandle(h); return true;
        }

        if (type.IsEnum || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong) || type == typeof(double))
        {
            if (!long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                               ReloadRecord.Culture, out long n))
            {
                if (type == typeof(double)
                    && double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                       ReloadRecord.Culture, out double d))
                {
                    value = d;
                    return true;
                }

                return false;
            }

            try
            {
                value = type.IsEnum
                    ? Enum.ToObject(type, n)
                    : Convert.ChangeType(n, type, ReloadRecord.Culture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc cref="SaveCustomState"/>
    private void LoadCustomState(SavedEntity state)
    {
        if (this is not ISaveCustom custom)
            return;

        var record = ReloadRecordText.Unflatten(GetType().FullName ?? ClassName,
                                                CustomPrefix, state.Local);

        try
        {
            custom.OnRestore(new SaveRecordAccess(record, $"{ClassName} '{Name}'", Log, SavedHandles));
        }
        catch (Exception e)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': OnRestore threw " +
                           $"({e.Message}); its custom state is not put back. Its declared " +
                           $"state already is\n");
        }
    }

    /// <summary>
    /// Puts one key's set-time back where it was.
    ///
    /// The descriptor's <c>Stamp</c> reaches a field private to the generated
    /// set class, which is where it has to be — the alternative was a
    /// process-wide "pretend the clock says this" flag around the replay, and a
    /// global that changes what a clock reports is a trap for whatever else runs
    /// while it is set.
    /// </summary>
    private static void RestoreStamp(ScriptedVarSet set, string key, float at)
    {
        foreach (var descriptor in set.Descriptors)
        {
            if (!string.Equals(descriptor.Name, key, StringComparison.OrdinalIgnoreCase))
                continue;

            // The replay left it unset, which means the value did not parse and
            // the declared default is still in place. Stamping it would say the
            // opposite.
            if (descriptor.IsSet(set))
                descriptor.Stamp(set, at);

            return;
        }
    }

    /// <summary>
    /// The managed entity a mapper named, if it is one of ours and of the type
    /// asked for. Null covers all three ways this can fail — no such name, not
    /// a C# entity, wrong kind — because a map naming something that is not
    /// there is a mapping mistake rather than an error worth throwing over.
    /// </summary>
    /// <summary>
    /// The managed half of an entity this one made.
    ///
    /// <para>
    /// By HANDLE rather than by name, because an entity created at runtime
    /// usually has no name — <c>env_laser</c> builds its beam and never calls it
    /// anything.
    /// </para>
    /// </summary>
    protected T? FindByHandle<T>(EntityHandle handle) where T : ScriptedEntity
        => (Entities.IsValid(handle) && Owner is not null) ? Owner.Get(handle) as T : null;

    protected T? FindByName<T>(string name) where T : ScriptedEntity
    {
        if (name.Length == 0 || Owner is null)
            return null;

        // The owner's own name index (plan_abi_hotreload D7): what the static
        // Engine.FindEntityByName asked through the shell-less route, which was
        // bound to this same registry.
        var handle = Owner.FindFirstNamed(name);
        return Entities.IsValid(handle) ? Owner.Get(handle) as T : null;
    }

    /// <summary>
    /// This entity's health. Read and written through the engine rather than
    /// kept here, because the engine is what damage and death check.
    /// </summary>
    protected int Health
    {
        get => Entities.GetHealth(Handle);
        set => Entities.SetHealth(Handle, value);
    }

    /// <summary>Spawnflags the map set. Read from the engine, which claims the key itself.</summary>
    public int SpawnFlags => Entities.GetSpawnFlags(Handle);

    /// <summary>
    /// CBaseEntity::ObjectCaps, its use bits: what a player's +USE may do with
    /// this entity (PlayerUse). An entity with a parent answers the parent's
    /// usable bits, so a use on a part reaches the thing it is part of; a class
    /// that is used itself adds its own.
    /// </summary>
    public virtual UseCaps ObjectCaps
        => FindByHandle<ScriptedEntity>(Entities.GetParent(Handle)) is { } parent
            ? parent.ObjectCaps & UseCaps.Usable
            : UseCaps.None;

    /// <summary>
    /// Spawnflags as an entity's own enum.
    ///
    /// <para>
    /// The engine hands these over as a bare int, and an int says nothing about
    /// WHOSE bits it is holding: 64 means "rotate about roll" to func_platrot,
    /// "everything else touches me" to a trigger, and "start on" to env_spark.
    /// Each entity declares a [Flags] enum of the bits its FGD defines and
    /// exposes one property built from this, so its tests read as
    /// <c>Flags.HasFlag(...)</c> against a type that names the domain, and a bit
    /// borrowed from another entity will not compile.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT a "does this entity have flag X" helper. Such a helper
    /// has to pick between ANY and ALL for a multi-bit argument, and whichever
    /// it picks is invisible at the call site -- which is exactly the mistake
    /// <c>HasFlag</c> invites, since it means ALL where stock Source's
    /// <c>flags &amp; (A|B)</c> means ANY. Handing back the typed value instead
    /// forces the call site to say which it means.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Spawnflags are 32 bits on both sides of the ABI, so the enum has to be
    /// int-backed. <see cref="Unsafe.BitCast{TFrom, TTo}"/> is the conversion
    /// that says so -- it refuses a differently sized enum outright rather than
    /// reading the wrong width.
    /// </remarks>
    protected TFlag SpawnFlagsAs<TFlag>() where TFlag : unmanaged, Enum
        => Unsafe.BitCast<int, TFlag>(SpawnFlags);

    /// <summary>Called once, after the map has applied every key.</summary>
    public virtual void OnSpawn() { }

    /// <summary>
    /// <c>CBaseEntity::Activate</c>, for this hierarchy — plan_entity.md §3.5
    /// and step 2d.
    ///
    /// <para>
    /// The second half of a map entity's birth, and it is a DIFFERENT moment
    /// from <see cref="OnSpawn"/> for the reason stock has both:
    /// <c>Spawn</c> runs while the map is still being built, so an entity that
    /// wants to find another one by name has to wait — <c>ServerActivate</c> runs
    /// once every entity in the map exists and has spawned.
    /// </para>
    ///
    /// <para>
    /// It arrives with NO CROSSING: <c>MapSpawner</c> holds the entities it made
    /// and walks them in spawn order, in managed memory. Nothing native is asked
    /// and nothing native is told.
    /// </para>
    ///
    /// <para>
    /// A source a <c>point_template</c> ate is never activated, because it never
    /// spawned.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>Named for stock's <c>ServerActivate</c> and not <c>OnActivate</c></b>,
    /// which is a name the map already owns: <c>OnActivate</c> is an OUTPUT a
    /// mapper wires (<c>PhysicsCannister</c> declares one), and SSVAR005 refuses
    /// a class whose output name hides a method for exactly the reason that
    /// matters here — the output would silently never fire. Three classes also
    /// already had a private <c>OnActivate</c> of their own.
    /// </remarks>
    public virtual void OnServerActivate() { }

    /// <summary>
    /// A key from the map that neither the engine nor a a <c>[ScriptVars]</c> key
    /// field claimed. Only needed for keys that cannot be expressed as a field —
    /// indexed sets like logic_case's, say. Return true if consumed.
    /// </summary>
    public virtual bool OnKeyValue(string key, string value) => false;

    /// <summary>
    /// An input no <see cref="ScriptedEventAttribute"/> method claimed. Only needed
    /// for inputs whose name is not known ahead of time. Return true if handled
    /// — returning false lets the engine's own handling run.
    /// </summary>
    public virtual bool OnInput(string name, string value, float number, EntityHandle activator) => false;

    /// <summary>
    /// Back to a fresh round, WITHOUT being rebuilt from the map string. The
    /// round reset (tf2 stage 0.7) walks the live entities and calls this
    /// instead of stock's <c>CleanUpMap</c>, which respawns the map from its
    /// entity string: managed state no map string ever held — a logic_case's
    /// own counters, a mode's own bookkeeping — survives a rebuild only by
    /// accident and must be cleared on purpose. The no-op default is the
    /// answer for an entity with no per-round state, which is most of them;
    /// opting in is per class, and the walk's own divergence row is in
    /// docs/divergences.md.
    /// </summary>
    public virtual void RoundReset() { }

    /// <summary>
    /// A player walked into this entity. Only item entities are triggers, so
    /// only they ever see this — and they see it every tick the player stands
    /// inside, which a pickup must account for rather than assume one call.
    /// </summary>
    public virtual void OnTouch(EntityHandle toucher) { }

    /// <summary>
    /// A player left a volume. Only brush entities can report this — an item is
    /// a point trigger and has no inside to leave.
    /// </summary>
    public virtual void OnEndTouch(EntityHandle toucher) { }

    /// <summary>
    /// Whether this entity hears about every toucher, or only about players.
    ///
    /// False by default because that is what almost everything wants: a pickup
    /// exists to be collected by somebody, and a crate rolling over it must not
    /// take it. A class that genuinely responds to anything — a pressure plate,
    /// a trigger that counts props — says so here.
    ///
    /// <para>
    /// This used to be decided in C++, for every entity, with no way to change
    /// it. It is a rule about the game, so it is stated in the game's language.
    /// </para>
    /// </summary>
    public virtual bool TouchedByAnything => false;

    /// <summary>
    /// Whether this class hears about EVERY FRAME of an overlap, or only about
    /// the two edges of it.
    ///
    /// <para>
    /// <b>THIS WAS <c>StandaloneClassDesc_t::bTouchEveryTick</c>, DECIDED IN
    /// C++</b> — <c>scripted_kinds.h</c> set it from the entity's KIND, true for
    /// an item and false for everything else, for every entity in the mod and
    /// with no way for a class to say otherwise. <c>plan_entity.md</c> step 2b
    /// makes it a managed per-class choice, which is what it always was: how
    /// often a class wants to be told is a fact about what the class does.
    /// </para>
    ///
    /// <para>
    /// <b>The default reproduces the C++ table exactly</b>, so that moving the
    /// decision moved no behaviour. An item is a point trigger a player stands
    /// ON, and a pickup that heard only the arrival could not be collected by
    /// somebody who was already standing there when it respawned — which is
    /// the case a deathmatch map produces every round. Everything else is an
    /// edge: a trigger fires when you enter and when you leave, and being told
    /// sixty times a second in between is work with no question behind it.
    /// </para>
    ///
    /// <para>
    /// Overriding it to true costs one <see cref="OnTouch"/> call per frame of
    /// overlap and NO crossing — the whole diff runs inside the frame's one
    /// <c>GameFrame</c> crossing (<see cref="EntityDirectory.TouchFrame"/>).
    /// </para>
    /// </summary>
    public virtual bool TouchEveryTick => RegisteredKind == ScriptedEntityKind.Item;

    /// <summary>
    /// What kind of thing this class was registered as — the
    /// <see cref="ScriptedEntityAttribute"/>'s kind, stamped on the instance
    /// when it is built.
    ///
    /// <para>
    /// ON THE INSTANCE rather than looked up per question, because the two
    /// things that ask it — <see cref="TouchEveryTick"/> above and any class
    /// overriding it — ask on a hot path and the registration is already in
    /// hand at the one moment the answer can change, which is never after
    /// construction.
    /// </para>
    ///
    /// <para>
    /// NAMED <c>RegisteredKind</c> AND NOT <c>Kind</c>: an entity class is free
    /// to have a <c>Kind</c> of its own about its own subject
    /// (<c>PhysicsForceEntity.Kind</c> is which FORCE it applies), and a base
    /// property that silently hides one is how a class ends up reading the
    /// wrong field. The compiler said so; the name is the answer.
    /// </para>
    /// </summary>
    public ScriptedEntityKind RegisteredKind { get; internal set; }

    /// <summary>
    /// The solver resolved an impact this entity was part of.
    ///
    /// Not a touch, and the difference is what makes it useful: a touch is the
    /// engine noticing two volumes overlap, while this reports where the two
    /// actually met and how hard.
    ///
    /// The two materials are Source's surfaceprops "game material" — for the
    /// face that was struck and for the other model's own default. They
    /// disagree often enough to matter, and which one counts is a game rule
    /// rather than an engine fact, so both come through.
    /// <see cref="SurfaceMaterial.Unknown"/> means the solver had none.
    /// </summary>
    public virtual void OnPhysicsCollision(EntityHandle other, Vector contact, float speed,
                                           SurfaceMaterial surfaceMaterial,
                                           SurfaceMaterial modelMaterial) { }

    /// <summary>
    /// Stock's CBaseEntity::VPhysicsCollision( index, pEvent ): one side of a
    /// solved impact, with the whole record (8a's motion tail for impact
    /// damage) and which side this entity is. Delivered after the step.
    /// </summary>
    public virtual void OnVPhysicsCollision(in PhysicsEvent ev, int index)
    {
        // The older, narrower hook (PhysMagnet's), which had no caller: the
        // other side, where they met, how hard, and the struck surface's
        // material. The record carries one surface per side, so the struck
        // face's material stands for both arguments.
        uint other = index == 0 ? ev.Entity1 : ev.Entity0;
        int otherSurface = (int)(index == 0 ? ev.SurfaceProps1 : ev.SurfaceProps0);
        var material = (SurfaceMaterial)(_surfaces?.Of(otherSurface) ?? default).Material;

        OnPhysicsCollision(other == PhysicsEvent.NoEntity ? EntityHandle.None : new EntityHandle(other),
                           ev.Point, ev.Speed, material, material);
    }

    /// <summary>
    /// Stock's VPhysicsShadowCollision( index, pEvent ): this entity's
    /// shadow-controlled object (a player's or NPC's) was hit. Delivered after
    /// the step, only to a side that is a shadow and only when the solver
    /// called it a shadow collision. Nothing by default, as on CBaseEntity.
    /// </summary>
    public virtual void OnVPhysicsShadowCollision(in PhysicsEvent ev, int index) { }

    /// <summary>
    /// How hard the world squeezes this entity's shadow after the step: the
    /// stress half of stock's CBasePlayer::VPhysicsUpdate (a
    /// <see cref="PhysicsEventKind.Stress"/> event). Recorded for players'
    /// hulls only. Nothing by default.
    /// </summary>
    public virtual void OnVPhysicsStress(in PhysicsEvent ev) { }

    /// <summary>
    /// The solver woke this entity's physics object. Stock's
    /// IPhysicsObjectEvent::ObjectWake, and the first VPhysicsUpdate a prop
    /// sees awake. Delivered after the step, with the rest of the frame's
    /// physics events.
    /// </summary>
    public virtual void OnPhysicsWake() { }

    /// <summary>The solver put this entity's physics object to sleep. ObjectSleep.</summary>
    public virtual void OnPhysicsSleep() { }

    /// <summary>
    /// The shell just created this entity's physics object, before the solver
    /// has simulated it: what stock's CreateVPhysics decides at creation (start
    /// asleep, mass scale) is decided here.
    /// </summary>
    public virtual void OnPhysicsCreated() { }

    /// <summary>
    /// Whether the registry gives this entity a simulated object after its
    /// spawn when it made none (a Prop kind's VPhysicsInitNormal). False for a
    /// class whose spawn decides its own object or none, as CDynamicProp's
    /// CreateVPhysics does from its solid type.
    /// </summary>
    public virtual bool SpawnsPhysics => true;

    /// <summary>
    /// <c>PhysicsSolidMaskForEntity</c>: the contents this entity's physics
    /// object collides with (ShouldCollide's contents clause). MASK_SOLID;
    /// an item adds player clips, a missile hitboxes.
    /// </summary>
    public virtual uint PhysicsSolidMask => SourceSharp.Server.EnginePushWorld.MaskSolid;

    /// <summary>
    /// Once per physics frame while the solver is moving this entity's object:
    /// stock's VPhysicsUpdate, called for each object on the active list.
    /// Delivered with the next drain.
    /// </summary>
    public virtual void OnPhysicsUpdate() { }

    /// <summary>
    /// A movement this entity started has finished. Where to go next is the
    /// entity's business — a door decides here whether it is now open or shut.
    /// </summary>
    public virtual void OnMoveDone() { }

    /// <summary>
    /// Something is in the way of a movement. What to do about it — reverse,
    /// crush, or wait — is a game rule, which is why it is asked here rather
    /// than settled by the engine.
    /// </summary>
    public virtual void OnBlocked(EntityHandle other) { }

    /// <summary>
    /// The mover's blocker has changed to <paramref name="other"/>: stock's
    /// StartBlocked, said once when a push first fails against it, before
    /// that frame's <see cref="OnBlocked"/>.
    /// </summary>
    public virtual void OnStartBlocked(EntityHandle other) { }

    /// <summary>The blocker a push last failed against is no longer in the way: stock's EndBlocked.</summary>
    public virtual void OnEndBlocked() { }

    /// <summary>
    /// Stock's <c>m_takedamage != DAMAGE_NO</c>, which decides, for one, whether
    /// a crossbow bolt counts this as a body (CCrossbowBolt::BoltTouch). The
    /// families' own defaults: a prop takes damage (CBreakableProp: DAMAGE_YES,
    /// or DAMAGE_EVENTS_ONLY with no health), a brush only when it is given
    /// health (a button or a door, as their Spawns rule), and nothing else; a
    /// class whose stock Spawn says otherwise answers for itself.
    /// </summary>
    public virtual bool TakesDamage => RegisteredKind switch
    {
        ScriptedEntityKind.Prop => true,
        ScriptedEntityKind.Brush => Entities.GetHealth(Handle) > 0,
        _ => false,
    };

    /// <summary>
    /// Stock's <c>GetMoveType() == MOVETYPE_PUSH</c>, which its brush entities'
    /// Spawns set and which decides, for one, that a crossbow bolt does not
    /// stick in this (CCrossbowBolt::BoltTouch's static-architecture test).
    /// The shell reports MOVETYPE_NONE for every managed brush, on purpose
    /// (standalone_movetype.h: claiming PUSH would hand it to the physics
    /// solver), so a reader of stock's test asks this. The classes answering
    /// yes are the ones stock's ss_sandbox census measured as PUSH
    /// (tools/stock-ab ab_movetypes); everything else answers no.
    /// </summary>
    public virtual bool MovesAsPusher => false;

    /// <summary>
    /// <c>FL_UNBLOCKABLE_BY_PLAYER</c>: a player in this mover's way is pushed
    /// where the push says even when it does not fit, rather than blocking it.
    /// </summary>
    public virtual bool UnblockableByPlayer => false;

    /// <summary>
    /// Something damaged this entity. Return how much damage it actually takes —
    /// zero to ignore it entirely. Whether a breakable yields to a crowbar and
    /// shrugs off bullets is a rule, which is why the answer is here.
    /// </summary>
    public virtual int OnDamaged(EntityHandle attacker, float damage, DamageType damageType) => 0;

    /// <summary>
    /// The same blow, with WHERE it landed and which way it was going.
    ///
    /// <para>
    /// A second virtual rather than more arguments on the first, because almost
    /// nothing needs these: a barrel takes the same damage wherever it is hit.
    /// The default hands straight to <see cref="OnDamaged"/>, so a class that
    /// does not care never sees them and none of the existing ones changed.
    /// </para>
    ///
    /// <para>
    /// Glass is what wants them: a bullet shatters the pane it struck and leaves
    /// the rest of the window standing, and without a position the best a
    /// surface could do is fall down all at once. Both are zero for damage with
    /// no place — a trigger_hurt hurts all of you at once.
    /// </para>
    ///
    /// <para>
    /// <paramref name="hitGroup"/> is the studio hitbox group a bullet's trace
    /// stopped on (stock's <c>tr.hitgroup</c>, which <c>TraceAttack</c> reads),
    /// carried by <see cref="DamageHitGroups"/>; <see cref="HitGroup.Generic"/>
    /// for anything that was not a traced shot. What a head is worth is the
    /// victim's rule.
    /// </para>
    /// </summary>
    public virtual int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                   Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => OnDamaged(attacker, damage, damageType);

    /// <summary>
    /// The blow pushes the physics object: stock's
    /// <c>CBaseEntity::VPhysicsTakeDamage</c> (baseentity.cpp:1804-1866),
    /// which <c>CBaseEntity::OnTakeDamage</c> calls for a MOVETYPE_VPHYSICS
    /// entity (:1526-1531). A physics class calls it from
    /// <see cref="OnDamagedAt"/> where its stock <c>OnTakeDamage</c> reaches
    /// the base, after its own refusals (plan_physics_managed A30).
    ///
    /// <para>
    /// No push for damage that carries no force (<c>Damage_NoPhysicsForce</c>
    /// or DMG_GENERIC, <see cref="SourceSharp.Server.DamageForceRules.ImpartsForce"/>). The
    /// player-held mass arm is the physcannon's, N/A here.
    /// </para>
    /// </summary>
    protected void VPhysicsTakeDamage(DamageType damageType, Vector position, Vector force)
    {
        // EFL_NO_DAMAGE_FORCES: CBaseEntity::OnTakeDamage skips the push.
        if (_noDamageForces || !SourceSharp.Server.DamageForceRules.ImpartsForce(damageType))
            return;

        Physics.ApplyForceOffset(Handle, force, position);
    }

    /// <summary>
    /// The solver broke a constraint: stock's IPhysicsConstraintEvent::
    /// ConstraintBroken, which CPhysConstraintEvents hands to the entity that
    /// owns the constraint (physconstraint.cpp:1602-1618). Answers whether this
    /// entity owns it (plan_physics_managed A23).
    /// </summary>
    public virtual bool OnPhysicsConstraintBroken(PhysicsId constraint) => false;

    /// <summary>
    /// FSOLID_NOT_SOLID on or off, as stock's Add/RemoveSolidFlags: the collision
    /// rules (and so a player's shadow) read it. One call; the class decides.
    /// </summary>
    protected void SetNotSolid(bool notSolid) => Physics.SetNotSolid(Handle, notSolid);

    /// <summary>FSOLID_NOT_SOLID on another entity, as FollowEntity sets it on what it attaches.</summary>
    protected void SetNotSolid(EntityHandle entity, bool notSolid) => Physics.SetNotSolid(entity, notSolid);

    // EFL_NO_DAMAGE_FORCES, which the two inputs below set and clear.
    private bool _noDamageForces;

    /// <summary>
    /// CBaseEntity::InputDisableDamageForces: EFL_NO_DAMAGE_FORCES, so damage
    /// no longer pushes this entity (baseentity.cpp:4559; A38).
    /// </summary>
    [ScriptedEvent]
    public void OnDisableDamageForces() => _noDamageForces = true;

    /// <summary>CBaseEntity::InputEnableDamageForces (baseentity.cpp:4554).</summary>
    [ScriptedEvent]
    public void OnEnableDamageForces() => _noDamageForces = false;

    /// <summary>This entity's health reached zero.</summary>
    public virtual void OnKilled(EntityHandle attacker) { }

    /// <summary>
    /// This entity is going away. The handle still resolves here, so anything
    /// that needs one last look at the world can take it.
    /// </summary>
    public virtual void OnRemoved() { }

    /// <summary>Called when a scheduled think comes due. See <see cref="SetNextThink"/>.</summary>
    public virtual void OnThink() { }

    /// <summary>
    /// Called when a think scheduled in a NAMED context comes due — see
    /// <see cref="SetContextThink"/>.
    ///
    /// <para>
    /// <b>A NAME AND NOT A DELEGATE, and that is a hot-reload decision.</b>
    /// Stock's <c>SetContextThink( &amp;C::Func, time, "name" )</c> stores a
    /// member-function pointer; the managed equivalent would store a delegate,
    /// and a delegate cannot be written into a save or carried across a rebuild
    /// of the assembly that declared it (<c>plan.md</c> §7). A context NAME is
    /// a string: it survives both, and the class dispatches on it here.
    /// </para>
    ///
    /// <para>Unhandled contexts do nothing, which is what a base class can say.</para>
    /// </summary>
    public virtual void OnThink(string context) { }

    /// <summary>
    /// The one entry point <see cref="EntityDirectory.RunFrame"/> uses, so the
    /// null-means-default rule is written once rather than at the call site.
    /// </summary>
    internal void RunThink(string? context)
    {
        if (context is null)
            OnThink();
        else
            OnThink(context);
    }

    /// <summary>
    /// The think heap this entity books into.
    ///
    /// <para>
    /// Handed over by the registry from its <see cref="EntityDirectory"/>. It
    /// falls back to one of its own for an instance built outside a registry —
    /// the same fallback <see cref="Entities"/> makes, and for the same reason:
    /// the alternative is that <see cref="SetNextThink"/> silently does nothing
    /// there, and a silent no-op is what §7 forbids. An unowned entity's
    /// bookings are real and readable; nothing runs them, because nothing is
    /// running a frame.
    /// </para>
    /// </summary>
    protected ThinkScheduler Thinks { get; private set; } = new();

    /// <summary>Handed over by the registry. See <see cref="Thinks"/>.</summary>
    internal void BindThinks(ThinkScheduler scheduler) => Thinks = scheduler;

    // plan_engineapi_vtables B5: an entity's "now" is its registry's clock.
    // An entity no registry owns (a bare test fixture) reads 0, which is what
    // the static clock answered with no engine behind it.
    private float Now => Owner?.Now ?? 0f;

    /// <summary>The registry's clock, for a subclass that stamps state with the time (0 with no registry).</summary>
    protected float GameTime => Now;

    /// <summary>
    /// Schedules <see cref="OnThink()"/> for <paramref name="delay"/> seconds
    /// from now. A negative delay stops the timer, which is the spelling the
    /// deleted <c>SetScriptedNextThink</c> slot used and which callers still
    /// pass.
    ///
    /// <para>
    /// <b>NO CROSSING.</b> This used to be an ABI call that reached into the
    /// native shell to write <c>m_flNextThink</c>; the schedule now lives in
    /// managed code and the whole of think — booking it and running it — is
    /// inside the one <c>GameFrame</c> crossing (<c>plan_entity.md</c> §3.4,
    /// step 1g).
    /// </para>
    /// </summary>
    protected void SetNextThink(float delay)
        => Thinks.Schedule(Handle, null, delay < 0.0f ? -1.0f : Now + delay);

    /// <summary>Cancels any scheduled think in the default context.</summary>
    protected void StopThinking() => Thinks.Cancel(Handle, null);

    /// <summary>
    /// Schedules <see cref="OnThink(string)"/> for <paramref name="delay"/>
    /// seconds from now in <paramref name="context"/>, independently of the
    /// default schedule and of every other context.
    /// </summary>
    protected void SetContextThink(string context, float delay)
        => Thinks.Schedule(Handle, context, delay < 0.0f ? -1.0f : Now + delay);

    /// <summary>
    /// Cancels one named context, leaving the default schedule and every other
    /// context standing.
    /// </summary>
    protected void StopThinking(string context) => Thinks.Cancel(Handle, context);

    /// <summary>
    /// Seconds from now until this entity's next think in
    /// <paramref name="context"/>, or -1 when it is not scheduled.
    ///
    /// <para>
    /// SECONDS FROM NOW rather than the absolute time, because that is what the
    /// save state carries and why: a load restarts <c>curtime</c>, so an
    /// absolute think time would name a moment that has gone.
    /// </para>
    /// </summary>
    public float NextThinkDelay(string? context = null)
    {
        float when = Thinks.NextThinkTime(Handle, context);
        return when < 0.0f ? -1.0f : MathF.Max(0.0f, when - Now);
    }

    /// <summary>Gives this entity a model. Must happen during spawn, so it can be precached.</summary>
    protected void SetModel(string model) => Render.SetModel(Handle, model);

    /// <summary>Makes this entity a trigger of the given half-extents that a player can walk into.</summary>
    protected void MakePickup(float sizeX, float sizeY, float sizeZ)
        => Players.MakePickup(Handle, sizeX, sizeY, sizeZ);

    /// <summary>Shows or hides this pickup without destroying it.</summary>
    protected void SetPickupEnabled(bool enabled) => Players.SetPickupEnabled(Handle, enabled);

    /// <summary>Where the mapper drew this brush. Movement is described relative to it.</summary>
    protected Vector SpawnOrigin => Entities.GetSpawnOrigin(Handle);

    /// <summary>The orientation the mapper drew this brush at.</summary>
    protected Vector SpawnAngles => Entities.GetSpawnAngles(Handle);

    /// <summary>Sets this entity's orientation.</summary>
    protected void SetAngles(Vector angles) => Entities.SetAngles(Handle, angles);

    /// <summary>Moves this brush to a position at a constant speed.</summary>
    protected void MoveTo(Vector destination, float speed)
        => Movement.MoveToPosition(Handle, destination, speed);

    /// <summary>Where this entity is. Read and written through the engine.</summary>
    protected Vector Origin
    {
        get => Entities.GetOrigin(Handle);
        set => Entities.SetOrigin(Handle, value);
    }

    /// <summary>How heavy this is, if the engine is simulating it.</summary>
    protected float Mass
    {
        get => Physics.GetMass(Handle);
        set => Physics.SetMass(Handle, value);
    }

    /// <summary>
    /// Makes this prop solid but never simulated. Only meaningful during spawn.
    /// </summary>
    protected void MakePhysicsStatic() => Physics.SetPhysicsStatic(Handle, true);

    /// <summary>
    /// Stock's <c>VPhysicsInitNormal</c>: a simulated object from this
    /// entity's model, made as the entity's solidity and collision group say.
    /// False when the model has none.
    /// </summary>
    protected bool InitPhysics(bool asleep = false) => Physics.InitPhysics(Handle, asleep);

    /// <summary>
    /// Stock's <c>VPhysicsInitStatic</c> as a brush calls it: an object the
    /// solver collides with, this brush's SOLID_BSP and movetype kept.
    /// </summary>
    protected bool InitStaticPhysics() => Physics.InitStaticPhysics(Handle);

    /// <summary>This entity's solid type and flags, as its spawn decides them.</summary>
    protected void SetSolidity(SolidType solidType, SolidFlags solidFlags) => Entities.SetSolidity(Handle, solidType, solidFlags);

    /// <summary>Stock's <c>CollisionProp()->UseTriggerBounds</c> on this entity.</summary>
    protected void UseTriggerBounds(bool enable, float bloat, bool uniform = false) => Entities.UseTriggerBounds(Handle, enable, bloat, uniform);

    /// <summary>
    /// Stock's <c>VPhysicsInitShadow( false, false )</c> as a mover calls it:
    /// an object that follows this entity, its solidity and movetype kept.
    /// </summary>
    protected bool InitShadowPhysics() => Physics.InitShadowPhysics(Handle);

    /// <summary>Whether this entity's physics object collides at all.</summary>
    protected void EnablePhysicsCollisions(bool enable) => Physics.EnableCollisions(Handle, enable);

    /// <summary>Stock's <c>IPhysicsObject::BecomeTrigger</c>: the solver reports what overlaps this entity's object.</summary>
    protected void BecomePhysicsTrigger(bool isTrigger) => Physics.BecomePhysicsTrigger(Handle, isTrigger);

    /// <summary>Which collision group this entity is in: what it collides with and what passes through it.</summary>
    protected void SetCollisionGroup(CollisionGroup group) => Physics.SetCollisionGroup(Handle, group);

    /// <summary>
    /// Plays a named animation. Returns false if the model has no such
    /// sequence, which a caller usually wants to know: the map named something
    /// the artist did not build.
    /// </summary>
    protected bool PlaySequence(string name)
    {
        int sequence = Render.LookupSequence(Handle, name);
        if (sequence < 0)
            return false;

        Render.SetSequence(Handle, sequence);
        return true;
    }

    /// <summary>Whether the engine moves this at all. It stays solid either way.</summary>
    protected void EnableMotion(bool enable) => Physics.EnableMotion(Handle, enable);

    /// <summary>Spins this brush continuously, in degrees per second.</summary>
    protected void Spin(Vector degreesPerSecond) => Movement.Spin(Handle, degreesPerSecond);

    /// <summary>Rotates this brush to an orientation at a constant speed.</summary>
    protected void RotateTo(Vector destination, float speed)
        => Movement.MoveToAngles(Handle, destination, speed);

    /// <summary>Whether this brush is drawn.</summary>
    protected void SetVisible(bool visible) => Render.SetVisible(Handle, visible);

    /// <summary>
    /// The solid type <see cref="SetSolidity(BrushSolidity)"/> gives this brush:
    /// SOLID_BSP, or SOLID_VPHYSICS for the movers stock's Spawn makes so
    /// (doors, func_movelinear, func_rotating, the rotating buttons,
    /// func_brush, func_tracktrain). A SOLID_VPHYSICS mover is one vphysics
    /// may block (CBaseEntity::VPhysicsUpdatePusher); box traces meet its
    /// collision model, rays its brushes.
    /// </summary>
    protected virtual SolidType BrushSolidType => SolidType.Bsp;

    /// <summary>
    /// SetSolid( SOLID_NONE ): out of every partition list, as stock leaves a
    /// volume nothing traces or touches (func_illusionary, func_occluder, the
    /// weather and dust volumes). Its bounds still answer.
    /// </summary>
    protected void SetSolidNone(SolidFlags flags = SolidFlags.None) => Entities.SetSolidity(Handle, SolidType.None, flags);

    /// <summary>What this brush does to something walking into it.</summary>
    protected void SetSolidity(BrushSolidity solidity)
    {
        if (BrushSolidType == SolidType.Bsp)
        {
            Physics.SetBrushSolidity(Handle, solidity);
            return;
        }

        Entities.SetSolidity(Handle, BrushSolidType, solidity switch
        {
            BrushSolidity.None => SolidFlags.NotSolid,
            BrushSolidity.Trigger => SolidFlags.NotSolid | SolidFlags.Trigger,
            _ => SolidFlags.None,
        });
    }

    //-------------------------------------------------------------------------
    // PARENTING.
    //
    // Being carried by something else: the model on the plug, the cap on the
    // fan, the ornament on the tree. The entity keeps a fixed offset in its
    // parent's frame and follows it from then on, including when the parent is
    // itself carried by something.
    //
    // The parent may be on either hierarchy — a managed entity or one of the
    // map's own — and a caller does not have to know which. What is NOT
    // supported is the other direction: one of the map's own entities carried by
    // a managed one. <see cref="SetParent(EntityHandle,int)"/> answers false and
    // says so on the console rather than failing silently.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Makes <paramref name="parent"/> carry this entity. An attachment of 0 —
    /// the default — hangs it from the parent's origin; a positive one hangs it
    /// from that numbered frame on the parent's model, which moves as the model
    /// animates. Look one up with <see cref="LookupAttachment"/>.
    ///
    /// <para>
    /// Answers false when the link was refused: a cycle, an entity that has not
    /// spawned, or a parent that no longer exists.
    /// </para>
    /// </summary>
    protected bool SetParent(EntityHandle parent, int parentAttachment = 0)
        => Entities.SetParent(Handle, parent, parentAttachment);

    /// <summary>
    /// The same, by name, which is what a map writes. Answers false when nothing
    /// is called that — reported by the caller, because "the mapper's typo" and
    /// "the parent has been removed" want different messages.
    /// </summary>
    protected bool SetParent(string parentName, int parentAttachment = 0)
    {
        if (parentName.Length == 0)
        {
            ClearParent();
            return true;
        }

        var parent = Entities.FindByName(parentName);

        // None rather than IsValid: FindByName has just resolved it, and asking
        // IsValid would resolve the same handle again to learn the same thing.
        return parent != EntityHandle.None && SetParent(parent, parentAttachment);
    }

    /// <summary>
    /// Stops anything carrying this entity. It stays exactly where it is —
    /// unparenting is not a move, and a decoration that jumped back to its map
    /// position when released would be worse than one that stays put.
    /// </summary>
    protected void ClearParent() => Entities.SetParent(Handle, EntityHandle.None);

    /// <summary>What carries this entity, or <see cref="EntityHandle.None"/>.</summary>
    protected EntityHandle Parent => Entities.GetParent(Handle);

    /// <summary>Whether anything carries this entity.</summary>
    protected bool IsParented => Entities.IsValid(Entities.GetParent(Handle));

    /// <summary>
    /// Which frame on the parent this entity hangs from: 0 for the parent's own
    /// origin, otherwise a one-based attachment index.
    /// </summary>
    protected int ParentAttachment => Entities.GetParentAttachment(Handle);

    /// <summary>
    /// Where this entity sits in its PARENT's frame, rather than in the world's.
    ///
    /// <para>
    /// Setting it is not a teleport, and the difference matters: writing an
    /// absolute origin of a carried entity means "put it here once, and relative
    /// to the parent from then on", so the offset is re-derived. Writing a local
    /// origin replaces the offset itself, which is what sliding a child along its
    /// parent wants. Reading it answers the world origin when nothing is carrying
    /// this entity, so the value always means something.
    /// </para>
    /// </summary>
    protected Vector LocalOrigin
    {
        get { Entities.GetLocalTransform(Handle, out var origin, out _); return origin; }
        set => Entities.SetLocalTransform(Handle, value, null);
    }

    /// <summary>Which way this entity faces in its parent's frame.</summary>
    protected Vector LocalAngles
    {
        get { Entities.GetLocalTransform(Handle, out _, out var angles); return angles; }
        set => Entities.SetLocalTransform(Handle, null, value);
    }

    /// <summary>Removes this entity from the world.</summary>
    protected void Remove()
    {
        // THE OWNER'S OWN REMOVAL ROUTE FIRST (plan_abi_hotreload D7): the
        // registry that built this entity marks it in its directory. That is
        // what Engine.RemoveEntity reached through the static route, which the
        // same registry bound; an entity with no owner, or one the directory
        // does not hold, still goes to the engine.
        if (Owner is not null && Owner.Removal.Remove(Handle))
            return;

        Entities.Remove(Handle);
    }

    /// <summary>
    /// <c>Kill</c>, the input every entity in the SDK answers to.
    ///
    /// <para>
    /// Stock declares it on <c>CBaseEntity</c> itself, so it is not a feature of
    /// any particular class — it is part of what "an entity" means to a mapper,
    /// and every <c>ent_fire &lt;anything&gt; Kill</c> ever written assumes it.
    /// The parallel hierarchy took it away: <c>CStandaloneEntity::AcceptInput</c>
    /// deliberately claims no inputs of its own, on the correct grounds that what
    /// killing MEANS is a rule and rules are managed code's. That left the rule
    /// belonging to C# and declared by nobody, so a managed entity told to die
    /// answered <c>accepted: 0</c> and stayed exactly where it was.
    /// </para>
    ///
    /// <para>
    /// Declared here rather than restored to the shell, which is the same
    /// argument reaching the other conclusion: it IS in managed code now, on the
    /// base every scripted entity derives from, and it is <c>virtual</c> — a
    /// class for which dying means something other than vanishing (breaking into
    /// gibs, playing out a death, refusing while a round is live) overrides it
    /// and the mapper's connection still lands.
    /// </para>
    /// </summary>
    [ScriptedEvent]
    public virtual void OnKill() => Remove();

    /// <summary>
    /// <c>AddOutput</c>: WIRE THIS ENTITY UP, OR WRITE ONE OF ITS KEYS, WHILE
    /// THE GAME IS RUNNING.
    ///
    /// <para>
    /// <b>THIS WAS A DECLARED REFUSAL AND THE REASON HAS GONE.</b>
    /// <c>ContractBaseline.Universal</c> listed <c>input|AddOutput</c> among the
    /// inputs "<c>CBaseEntity</c> declares and NOTHING managed answers", which
    /// was true while the connection table lived in native memory: there was
    /// nothing on this side to hang a connection on. Step 2a moved the whole
    /// table into <c>EntityDirectory</c>, and hanging one more on it at runtime
    /// is the SAME call the map's own parse makes.
    /// </para>
    ///
    /// <para>
    /// <b>WHAT IT IS FOR is the thing nothing else can do.</b> Without it a
    /// test, a console or a gate can only exercise the wiring a map happened to
    /// declare — which is why <c>tools/live-gate</c> could assert that 191
    /// connections survived a hot reload and could not assert that ONE of them
    /// still worked. A connection made here is indistinguishable from a declared
    /// one afterwards, so a test of it is a test of the real path.
    /// </para>
    ///
    /// <para>
    /// <b>STOCK'S TWO ARMS, BOTH OF THEM.</b>
    /// <c>CBaseEntity::InputAddOutput</c> reads the value's first token: if it
    /// names a declared OUTPUT the rest is a connection in the map's own
    /// spelling, <c>target:input:parameter:delay:timesToFire</c>; otherwise the
    /// whole thing is a KEYVALUE, which is the form
    /// <c>ent_fire worldspawn AddOutput "detailmaterial x"</c> takes. Both are
    /// reproduced, because an <c>AddOutput</c> that silently did nothing with
    /// the second is exactly the no-op <c>plan.md</c> §7 forbids.
    /// </para>
    ///
    /// <para>
    /// VIRTUAL, like <see cref="OnKill"/> and for its reason: a class that means
    /// something else by being wired at runtime overrides it, and the mapper's
    /// connection still lands.
    /// </para>
    /// </summary>
    [ScriptedEvent]
    public virtual void OnAddOutput(string value)
    {
        // THE ENTITY'S OWN REGISTRY (plan_abi_hotreload D7): it was a static
        // route bound by whichever registry was built last. Every entity a
        // registry builds has its Owner; one without answers false and is
        // reported below, as an unbound route was.
        if (Owner is null || !Owner.AddOutput(Handle, this, value))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': AddOutput '{value}' names " +
                           $"neither one of this class's outputs nor one of its keys; " +
                           $"nothing was wired and nothing was written\n");
        }
    }

    /// <summary>Parses a numeric key, leaving the fallback in place if the map wrote nonsense.</summary>
    protected static float ParseFloat(string value, float fallback = 0.0f)
        => float.TryParse(value, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : fallback;

    /// <summary>
    /// Three numbers in a string, each falling back on its own.
    ///
    /// <para>
    /// FOR INPUT VALUES, not for map keys: a <c>[ScriptVars]</c> property typed
    /// <see cref="Vector"/> or <see cref="QAngle"/> is converted once when the
    /// key arrives and never parsed again. What is left needing this is the
    /// handful of inputs a map FIRES that carry several numbers in one string —
    /// SetLinearForceAngles, SetVelocityLimitTime — which have no declared type
    /// to be read into.
    /// </para>
    ///
    /// <para>
    /// Lenient where <see cref="ScriptedVarParse.Vector"/> is strict, and the
    /// difference is deliberate: this is what stock's UTIL_StringToFloatArray
    /// does with a short or malformed argument list, and an input arriving
    /// mid-game has nowhere useful to refuse to.
    /// </para>
    ///
    /// <para>
    /// It lived on TriggerPush, and a second copy of it on ShadowControl, with
    /// entities reaching across to one or the other. Neither was a trigger's
    /// business or a shadow's.
    /// </para>
    /// </summary>
    protected internal static Vector ParseVector(string value, Vector fallback = default)
    {
        string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Span<float> result = stackalloc float[3] { fallback.X, fallback.Y, fallback.Z };

        for (int i = 0; i < 3 && i < parts.Length; i++)
        {
            if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v))
            {
                result[i] = v;
            }
        }

        return new Vector(result[0], result[1], result[2]);
    }

    /// <summary>Parses an integer key, leaving the fallback in place if the map wrote nonsense.</summary>
    protected static int ParseInt(string value, int fallback = 0)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int i) ? i : fallback;
}

/// <summary>One map key a class declares, and the type it reads into.</summary>
public readonly record struct ScriptedFieldInfo(string Name, string TypeName);

/// <summary>
/// A map key on a live instance, with what it currently holds — and whether the
/// map ever wrote it.
///
/// <para>
/// <paramref name="IsSet"/> is the answer a value alone cannot give. A key
/// sitting at 100 because the map said 100 and a key sitting at 100 because
/// nobody mentioned it look identical in a value column, and telling them apart
/// is most of what a mapper wants from this view: it is the difference between
/// "my key did nothing" and "my key never arrived".
/// </para>
/// </summary>
public readonly record struct ScriptedVarInfo(
    string Name, string TypeName, string Value, bool IsSet = false,
    float LastSetAt = ScriptedVarSet.Never);

/// <summary>
/// An output on a live instance. The counts are what make this worth showing:
/// "did this connection ever fire, and with what" is the question a broken map
/// actually poses.
/// </summary>
public readonly record struct ScriptedOutputInfo(string Name, int FireCount, string? LastValue);

/// <summary>A declared entity class: what a map may send it, and what it sends back.</summary>
public readonly record struct ScriptedClassInfo(
    string ClassName,
    string Kind,
    string TypeName,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<ScriptedFieldInfo> Vars,
    int LiveCount);

/// <summary>One entity that exists right now, and everything known about it.</summary>
public readonly record struct ScriptedInstanceInfo(
    string ClassName,
    uint Handle,
    int Index,
    bool IsValid,
    Vector Origin,
    int Health,
    int SpawnFlags,
    IReadOnlyList<ScriptedVarInfo> Vars,
    IReadOnlyList<ScriptedOutputInfo> Outputs);

/// <summary>
/// Discovers <see cref="ScriptedEntityAttribute"/> classes, declares each to the
/// engine along with its outputs, and routes every callback to the instance for
/// the entity concerned.
/// </summary>
public sealed class ScriptedEntityRegistry : IEntityDirectory, IShellLessSide, IRouteBinder
{
    /// <summary>
    /// What one attributed class contributes: how to make it, its outputs in
    /// order, and the map keys that bind straight to fields.
    /// </summary>
    private sealed record Registration(
        Type Type,
        ScriptedEntityKind Kind,
        FieldInfo[] OutputFields,
        string[] OutputNames,
        FieldInfo[] VarSetFields,
        ScriptedVarDescriptor[][] VarSetKeys,
        Dictionary<string, MethodInfo> Inputs);

    private readonly Dictionary<string, Registration> _byClassName = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Every live entity, by handle entry — <c>plan_entity.md</c> §3.4/§3.5.
    ///
    /// <para>
    /// THIS WAS A <c>Dictionary&lt;uint, ScriptedEntity&gt;</c> and step 1g
    /// replaced it, because §3.4 names it: "The per-callback dictionary lookup
    /// the registries do today becomes an array index, because the directory is
    /// an array indexed by handle entry." The directory also owns the think
    /// heap, which is the other half of the same step.
    /// </para>
    /// </summary>
    private readonly EntityDirectory _directory;

    /// <summary>
    /// The <c>[SaveField]</c> members per entity class, remembered for this
    /// registry's load (statics to zero: it was a process-wide memo that had
    /// to forget a side's types at unload).
    /// </summary>
    internal SaveFieldCatalog SaveFields { get; }

    /// <summary>The <c>[NetState]</c> fields per entity class, likewise.</summary>
    internal NetStateFields NetStateFields { get; }

    /// <summary>
    /// This registry's log: its side's, over that side's console (statics to
    /// zero: it was the static Log). Its entities, its var sets and its
    /// catalogs print through it.
    /// </summary>
    internal ILog Log { get; }

    /// <summary>The lines this registry's console prints (a fact's way to read what it logged).</summary>
    internal ConsoleLines Lines => _side.Lines;

    // The server composition's liveness mirror (plan_abi_hotreload D7): a
    // removal is recorded in it even when the entity has no managed instance.
    private readonly LivenessMirror _liveness;

    /// <summary>
    /// Hands the directory the two things its I/O half cannot know on its own
    /// (step 2a).
    ///
    /// <para>
    /// <b>Delivery</b>, because the directory decides WHICH entity and WHEN and
    /// this side decides what an input MEANS — the <c>[ScriptedEvent]</c> table,
    /// the <c>OnInput</c> fall-back and the nesting guard are all here.
    /// </para>
    ///
    /// <para>
    /// <b><c>!player</c></b>, because <c>FindEntityProcedural</c> answers it
    /// with <c>UTIL_PlayerByIndex(1)</c> and player one is an engine question.
    /// A test replaces this with a handle of its own; nothing else should.
    /// </para>
    /// </summary>
    /// <param name="liveness">
    /// The server composition's liveness mirror, which this registry's removals
    /// are recorded in (plan_abi_hotreload D7, mirrors half).
    /// </param>
    /// <param name="shellLess">The side's shell-less route, which this registry answers.</param>
    /// <param name="io">The side's I/O route: delivery and the service pass.</param>
    /// <param name="removal">The side's removal route.</param>
    /// <param name="side">
    /// The side's entity engine, for the engine calls no area carries: freeing
    /// a frame's shells, the shell-less reach and binding net states
    /// (plan_abi_hotreload D8). The root builds it over its own engine, so this
    /// registry never names the engine itself (plan_shim_areas_split phase 8).
    /// </param>
    /// <param name="engine">
    /// The side's container, which holds its engine areas: what this registry
    /// registers classes and asks about players through, and what an entity it
    /// builds is bound to when <see cref="Services"/> does not cover an area
    /// (plan_abi_hotreload D8). Never the active side's.
    /// </param>
    /// <remarks>
    /// <b>THE ROUTES ARE THE SIDE'S, HANDED IN</b> (plan_abi_hotreload D7). They
    /// were static classes this constructor bound; they are the
    /// <see cref="EngineSide"/>'s own instances now, registered in the side's
    /// container by <c>EngineServices.AddTo</c>, so the composition that builds
    /// this registry binds it into exactly the routes its engine reads, and a
    /// second composition's registry binds into its own. The mirror is the
    /// composition's likewise.
    /// </remarks>
    public ScriptedEntityRegistry(LivenessMirror liveness, ShellLessRoutes shellLess, EntityIoRoutes io,
                                  EntityRemovalRoutes removal, IServiceLocator engine, ServerEntityEngine side)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(side);
        _engine = engine;
        _side = side;

        // The side's connection id stamps (statics to zero): its state's,
        // which outlive the reload this registry may be restored by.
        _directory = new EntityDirectory(side.Log, side.Stamps);
        Log = side.Log;
        SaveFields = new SaveFieldCatalog(Log);
        NetStateFields = new NetStateFields(Log);

        ArgumentNullException.ThrowIfNull(liveness);
        ArgumentNullException.ThrowIfNull(shellLess);
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(removal);

        _liveness = liveness;
        _shellLess = shellLess;
        _io = io;
        _removal = removal;

        _directory.InputDelivery = (target, input, value, number, activator)
            => Input(target, input, value ?? string.Empty, number, activator);

        _directory.PlayerProvider = () => _engine.Require<IEnginePlayers>().GetHandle(1);

        // AND THE ROUTE THE DELETED SLOT LEFT BEHIND. IEngineEntities.FireInput
        // was one line into the EntityFireInput ABI slot; step 2a deletes the
        // slot, and the answer is this directory's.
        io.Bind(_directory.InputDelivery);

        // AND THE ROUTE THE FRAME DRIVER SERVICES THE QUEUE THROUGH -- the
        // sub-ioqueue lane. Stock runs the event queue from
        // CServerGameDLL::GameFrame (gameinterface.cpp:1383), which is
        // ServerGameDll.GameFrame here, and that driver is handed the same route.
        io.BindServiceQueue(_directory.ServiceIo);

        // AND THE SAME AGAIN FOR REMOVAL -- plan_entity.md step 2c. §3.5 puts
        // the mark in the directory, so the engine side's removal needs a route
        // to this instance.
        removal.Bind(_directory.Remove, _directory.FreeRemoved);

        // WHAT A REMOVED ENTITY IS TOLD, and it is a managed call now rather
        // than the ENTITY_OP_REMOVED crossing ~CStandaloneEntity used to make
        // for every single entity it freed. Ordered by the directory: the links
        // first, then this.
        _directory.RemovalNotify = NotifyRemoved;

        // AND THE ONE CROSSING THE WHOLE FRAME'S REMOVALS COST.
        _directory.ShellDestroyer = _side.DestroyShells;

        // AND THE SHELL-LESS ENTITIES -- plan_entity.md step 2e: a class of
        // kind Logical has to be answered out of THIS registry.
        shellLess.Bind(this);
    }

    /// <summary>
    /// A registry over routes of its own, which no engine side reads: for
    /// tests that drive the registry directly. The game builds one with the
    /// side's routes. Its engine is the one the test hands it (a side over the
    /// default areas, <c>TestEngines.Server</c>), never the process's.
    /// </summary>
    internal ScriptedEntityRegistry((ServiceContainer Engine, ServerEntityEngine Side) engine)
        : this(new LivenessMirror(), new ShellLessRoutes(), new EntityIoRoutes(), new EntityRemovalRoutes(), engine)
    {
    }

    /// <summary>A registry over routes of its own that records into <paramref name="liveness"/>: for tests.</summary>
    internal ScriptedEntityRegistry(LivenessMirror liveness, (ServiceContainer Engine, ServerEntityEngine Side) engine)
        : this(liveness, new ShellLessRoutes(), new EntityIoRoutes(), new EntityRemovalRoutes(), engine)
    {
    }

    /// <summary>A registry over the given routes whose removals go to a mirror of its own: for tests.</summary>
    internal ScriptedEntityRegistry(ShellLessRoutes shellLess, EntityIoRoutes io, EntityRemovalRoutes removal,
                                    (ServiceContainer Engine, ServerEntityEngine Side) engine)
        : this(new LivenessMirror(), shellLess, io, removal, engine)
    {
    }

    private ScriptedEntityRegistry(LivenessMirror liveness, ShellLessRoutes shellLess, EntityIoRoutes io,
                                   EntityRemovalRoutes removal, (ServiceContainer Engine, ServerEntityEngine Side) engine)
        : this(liveness, shellLess, io, removal, engine.Engine, engine.Side)
    {
    }

    // The side this registry frees shells through and asks the shell-less
    // reach of: the composition's, or a test's unattached one. Through
    // ServerEntityEngine, never the EngineSide itself (P1 run 3).
    private readonly ServerEntityEngine _side;

    /// <summary>That side, for a fact.</summary>
    internal ServerEntityEngine Engine => _side;

    // The side's container this registry's engine comes from (see the constructor).
    private readonly IServiceLocator _engine;

    // The side's routes this registry bound itself into (see the constructor).
    private readonly ShellLessRoutes _shellLess;
    private readonly EntityIoRoutes _io;
    private readonly EntityRemovalRoutes _removal;

    /// <summary>The shell-less route this registry answers (plan_abi_hotreload D7).</summary>
    public ShellLessRoutes ShellLess => _shellLess;

    /// <summary>The I/O route this registry delivers through.</summary>
    public EntityIoRoutes Io => _io;

    /// <summary>The removal route this registry marks through.</summary>
    public EntityRemovalRoutes Removal => _removal;

    /// <summary>
    /// Takes this registry back out of its side's routes, leaving any route a
    /// LATER registry has bound alone. The composition calls it from
    /// <c>Shutdown</c>, so an engine side that outlives the module (the static
    /// <c>Engine</c>, until plan_abi_hotreload D8) does not keep the dead
    /// registry, and through it the side's load context, alive.
    /// </summary>
    /// <returns>How many routes were dropped (0..3).</returns>
    public int Detach()
    {
        int dropped = 0;

        if (ReferenceEquals(_shellLess.Answers, this))
        {
            _shellLess.Unbind();
            dropped++;
        }

        if (ReferenceEquals(_io.Input, _directory.InputDelivery))
        {
            _io.Unbind();
            dropped++;
        }

        if (_removal.Mark is { } mark && ReferenceEquals(mark.Target, _directory))
        {
            _removal.Unbind();
            dropped++;
        }

        return dropped;
    }

    /// <summary>
    /// Whether this registry's entity classes came from a load context that is
    /// unloading — which makes the whole registry dead, and the four static
    /// routes its constructor bound dead with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REGISTRATIONS, NOT THIS OBJECT'S OWN TYPE. This class is declared in
    /// the ABI, which lives in a context that outlives every side, so
    /// <c>ManagedCaches.CameFrom(GetType(), going)</c> is false for it and was
    /// the first attempt at this — the ALC still leaked and the pin-finder
    /// still named these registrations. What belongs to a side is what it put
    /// IN here.
    /// </para>
    /// <para>
    /// ANY, not ALL: a registry only ever holds one side's classes (only the
    /// server has one at all), and a mixed registry would be a bug this should
    /// report by unbinding rather than by keeping half a dead side alive.
    /// </para>
    /// </remarks>
    bool IRouteBinder.BelongsTo(AssemblyLoadContext going) => BelongsTo(going);

    internal bool BelongsTo(AssemblyLoadContext going)
    {
        foreach (var registration in _byClassName.Values)
        {
            if (ManagedCaches.CameFrom(registration.Type, going))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The directory this registry keeps, so the frame can run its think pass
    /// and the dev API can read it. See <see cref="_directory"/>.
    /// </summary>
    public EntityDirectory Directory => _directory;

    /// <summary>
    /// The records the last <see cref="RestoreFromSave(IReadOnlyList{SavedEntity})"/>
    /// could not place, kept for one later pass.
    ///
    /// <para>
    /// <b>WHY A RESTORE HAS TO BE ABLE TO WAIT.</b> <c>Restore</c> runs inside
    /// <c>engine-&gt;LoadGameState</c>, which runs inside <c>LevelInit</c> —
    /// before <c>ServerActivate</c> and before any client is active. Two whole
    /// groups of entity do not exist yet at that instant and cannot:
    /// </para>
    ///
    /// <list type="number">
    /// <item>the ones the managed level init creates in
    /// <c>ServerActivate</c> — 10 <c>ss_pickup</c> and 9 <c>beam</c> on
    /// <c>dm_lockdown</c>, measured;</item>
    /// <item><b>the player and its kit</b> — <c>player</c>,
    /// <c>predicted_viewmodel</c>, <c>ss_weapon</c> — which cannot exist before a
    /// client is active, because the edict they bind to is a client's.</item>
    /// </list>
    ///
    /// <para>
    /// Matching those records against the world as it stands at <c>Restore</c>
    /// is asking a question the world cannot answer yet, and the answer that
    /// came back was 100 records "matched no entity". They are HELD here
    /// instead and re-matched once, from the client-active point, which is
    /// after both groups exist.
    /// </para>
    ///
    /// <para>
    /// <b>ONCE, AND THEN DROPPED.</b> <see cref="ApplyDeferredRestore()"/>
    /// empties this list whether or not everything in it placed. A record that
    /// is still homeless after the deferred pass is homeless — replaying the
    /// leftovers on the next client to activate would lay one save's records
    /// over a world that has moved on since.
    /// </para>
    /// </summary>
    private readonly List<SavedEntity> _deferred = new();

    /// <summary>
    /// How many records are waiting for <see cref="ApplyDeferredRestore()"/>.
    /// Zero at every moment except between a savegame <c>Restore</c> and the
    /// client-active pass that follows it.
    /// </summary>
    public int DeferredRestoreCount => _deferred.Count;

    /// <summary>
    /// Declares every attributed class in every assembly loaded beside
    /// <paramref name="assembly"/> to the engine. Must run before a map loads,
    /// which managed startup satisfies. Returns the number declared.
    /// </summary>
    public int RegisterAll(Assembly assembly)
    {
        int count = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, Log))
        {
            // Several map names may map to one class. The SDK does this itself —
            // momentary_door is registered to CFuncMoveLinear — and saying so
            // with a second attribute beats a subclass that exists only to be
            // renamed.
            var attrs = type.GetCustomAttributes<ScriptedEntityAttribute>().ToArray();
            if (attrs.Length == 0)
                continue;

            var attr = attrs[0];

            if (type.IsAbstract || type.IsInterface)
            {
                Log.Warning($"[SourceSharp] [ScriptedEntity({attr.ClassName})] on abstract type {type.Name}; skipped\n");
                continue;
            }

            if (!typeof(ScriptedEntity).IsAssignableFrom(type))
            {
                Log.Warning($"[SourceSharp] [ScriptedEntity({attr.ClassName})] on {type.Name}, which does not derive from ScriptedEntity; skipped\n");
                continue;
            }

            // Declaration order, so a class's outputs keep stable slots across
            // builds. MetadataToken is the order the fields appear in the source
            // and is fixed in the compiled image.
            var allFields = FieldsIncludingBase(type);

            var fields = allFields
                .Where(f => f.FieldType == typeof(Output))
                .OrderBy(f => f.MetadataToken)
                .ToArray();

            // An output may carry a name of its own, and registration happens
            // before any instance exists — so a throwaway instance is built to
            // read what each field actually declared.
            var names = OutputNamesFor(fields);

            // The declared map keys, which live in [ScriptVars] classes the
            // entity holds — found exactly as [NetState] fields are, and for the
            // same reason: the declaration is written where the thing is rather
            // than inferred from its type. Leaving the marker off is a BUILD
            // error rather than a quiet entity; the generator reports a var set
            // field that carries none.
            //
            // A class and its base may each hold one, and normally do. They are
            // separate objects rather than an inheriting pair, so a base's keys
            // are declared once and every entity deriving from it answers to
            // them without saying so — which is exactly what the old reflective
            // walk up the base chain did, expressed as ordinary C# instead.
            //
            // MOST DERIVED FIRST, which is the order FieldsIncludingBase already
            // walks in and the reason this does NOT sort by MetadataToken the
            // way the outputs above do. A token orders fields within one class
            // and says nothing across two, so sorting by it would have decided a
            // shadowed key's winner by where the classes happened to land in the
            // image. Derived first makes it a rule instead: a derived class's
            // key wins, which is what a reader expects of an override, and the
            // generator warns (SSVAR010) whenever there is a winner to pick.
            var varSetFields = allFields
                .Where(f => f.GetCustomAttribute<ScriptVarsAttribute>() is not null)
                .ToArray();

            var varSetKeys = DescribeVarSets(type, varSetFields, Log);

            var inputs = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in MethodsIncludingBase(type))
            {
                var input = method.GetCustomAttribute<ScriptedEventAttribute>();
                if (input is null)
                    continue;

                if (!IsBindableInput(method))
                {
                    Log.Warning($"[SourceSharp] {type.Name}.{method.Name} has [ScriptedEvent] but a signature that cannot be bound; skipped\n");
                    continue;
                }

                inputs[input.Name ?? InputNameFor(method, type, Log)] = method;
            }

            // Registered once per name, all sharing the one description.
            foreach (var each in attrs)
            {
                _byClassName[each.ClassName] = new Registration(
                    type, each.Kind, fields, names, varSetFields, varSetKeys, inputs);

                int declared = varSetKeys.Sum(k => k.Length);
                Log.Msg($"[SourceSharp] entity class '{each.ClassName}': {names.Length} output(s), {declared} var(s), {inputs.Count} event(s)\n");
                _engine.Require<IEngineScripted>().RegisterEntityClass(each.ClassName, names, each.Kind);

                // WHICH OPS THIS CLASS ACTUALLY WANTS (plan_entity.md §3.4).
                //
                // Computed by reflection from the class itself rather than
                // declared, because the class already declares it by overriding
                // OnThink or OnTouch and a second declaration is a second thing
                // to keep in step. The shell tests the bit before crossing, so
                // an op this mask leaves off costs ZERO managed transitions --
                // which is the mechanism §5's crossing budget is paid back
                // with.
                //
                // AFTER RegisterScriptedEntity for the same name, because that
                // is what creates the class the mask is stored on. A mask for a
                // name that was never registered is dropped on the far side.
                //
                // The bound inputs go in as well as the OnInput override: a
                // [ScriptedEvent] method is reached WITHOUT passing through
                // OnInput, so a class that binds one and overrides nothing
                // still has to hear inputs.
                _engine.Require<IEngineScripted>().RegisterEntityClassOps(each.ClassName,
                    EntityOpMask.For(type, inputs.Count > 0));

                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Whether a classname was declared by a [ScriptedEntity] in this build.
    ///
    /// Asked when deciding whether to publish a class's networked state: a state
    /// declared for a class nobody registered would put a table on the wire that
    /// no entity can ever claim.
    /// </summary>
    public bool IsRegistered(string className) => _byClassName.ContainsKey(className);

    /// <summary>
    /// Every classname registered here.
    ///
    /// <para>
    /// Wanted since Step 6 of plan_nettable_generic.md. m_szClassname used to be
    /// replicated, so the client read an entity's name off the wire whatever its
    /// class was; m_nTypeId is a hash, and a hash is only a name if the far side
    /// has a table to look it up in — so every class on the hierarchy has to be
    /// published, not only the handful that declare networked state.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<string> ClassNames => _byClassName.Keys;

    /// <summary>
    /// The container this registry builds entities from, or null to build them
    /// with their parameterless constructors alone.
    ///
    /// <para>
    /// A property set by the module rather than a constructor argument, because
    /// of what has to happen in what order: the registry is created before the
    /// module has any services to put in a container, and <c>RegisterAll</c>
    /// has to run at DLL init because the engine builds each class's entity
    /// description from it. Assigning it afterwards keeps both true.
    /// </para>
    /// </summary>
    public IServiceLocator? Services
    {
        get => _services;
        set { _services = value; _resolver = null; _clock = null; }
    }

    // plan_engineapi_vtables B5: the time this registry stamps outputs, thinks
    // and saves with -- its container's clock, not the static Engine's.
    private IGameClock? _clock;

    internal float Now => (_clock ??= Resolver.Require<IGameClock>()).Now;

    // What this registry's var sets stamp a map key with: its own Now, read
    // when the key arrives, so a Services swap is seen as Now sees it.
    private IGameClock? _varsClock;
    private IGameClock VarsClock => _varsClock ??= new RegistryClock(this);

    private sealed class RegistryClock(ScriptedEntityRegistry registry) : IGameClock
    {
        public float Now => registry.Now;
    }

    // The areas the registry asks the engine through ITSELF -- whether there
    // is one, whether a toucher is a player, an entity's index, registering a
    // class -- from its side's container, which is the one it was built with
    // (plan_abi_hotreload D8). Not from Services: what the registry asks is a
    // question about the side, and a test's container of fakes is for the
    // entities it builds, exactly as when these were static calls.
    private IEngineHost? _host;
    private IEngineEntities? _entities;

    private IEngineHost HostArea => _host ??= _engine.Require<IEngineHost>();
    private IEngineEntities EntityArea => _entities ??= _engine.Require<IEngineEntities>();

    private IServiceLocator? _services;

    /// <summary>
    /// Builds one entity class, through the container.
    ///
    /// <para>
    /// No container is treated as an EMPTY one rather than as a separate path,
    /// and that is not tidiness. <c>Activator.CreateInstance</c> was what stood
    /// here, and it throws on a class with no parameterless constructor — so a
    /// single entity class declared with a dependency, in a build whose module
    /// had not been handed a container, would take the whole map load with it.
    /// An empty container refuses that one class by name and every other entity
    /// in the map still spawns.
    /// </para>
    ///
    /// <para>
    /// The constructor plan is cached HERE rather than left to the container,
    /// because the container may be a test's stub with no cache of its own —
    /// and because this is where the cost lands: a busy map places hundreds of
    /// one entity class, and reflecting over its constructor once per instance
    /// is the expensive half of making one.
    /// </para>
    /// </summary>
    private bool Activate(Type type, [NotNullWhen(true)] out ScriptedEntity? instance)
    {
        instance = null;

        if (!_activators.TryGetValue(type, out var activator))
        {
            try
            {
                _activators[type] = activator = ServiceActivator.For(type);
            }
            catch (ServiceResolutionException failure)
            {
                Log.Warning($"[SourceSharp] {type.Name} cannot be built: {failure.Message}\n");
                return false;
            }
        }

        object made;

        try
        {
            // The caller's container first, the real engine behind it. See
            // FallbackLocator: a test registering one fake area must still get
            // the other twenty-one its entity's base classes ask for.
            made = activator.Create(Resolver);
        }
        catch (ServiceResolutionException failure)
        {
            Log.Warning($"[SourceSharp] {type.Name} cannot be built: {failure.Message}\n");
            return false;
        }
        catch (TargetInvocationException failure)
        {
            // The entity's own constructor threw. Reported the same way rather
            // than let through, because from here the two are one event — this
            // class produced no instance — and letting it out would end the
            // map load over one entity.
            Log.Warning($"[SourceSharp] {type.Name} threw while being built: " +
                           $"{failure.InnerException?.Message ?? failure.Message}\n");
            return false;
        }

        if (made is not ScriptedEntity built)
        {
            Log.Warning($"[SourceSharp] could not instantiate {type.Name}\n");
            return false;
        }

        instance = built;
        return true;
    }

    // Worked out once per entity class. See Activate.
    private readonly Dictionary<Type, ServiceActivator> _activators = new();


    // Rebuilt only when Services changes, which is once, at init. The
    // caller's container first, then this registry's own engine: a test that
    // registers one fake area still gets the rest from the side it was built
    // over, and never from whichever side is active (plan_abi_hotreload D8).
    private IServiceLocator? _resolver;

    internal IServiceLocator Resolver
        => _resolver ??= Services is null ? _engine : new FallbackLocator(Services, _engine);


    /// <summary>
    /// Runs the entity's own spawn, creating the instance first if the map has
    /// not already caused one to exist.
    ///
    /// Note what this does NOT do: it is not where the instance comes from for
    /// a map entity. Source parses every keyvalue before it calls Spawn, so by
    /// the time this runs the map has usually already assigned the fields.
    /// </summary>
    public void Spawn(EntityHandle handle, string className)
    {
        var instance = EnsureInstance(handle, className);
        instance?.OnSpawn();

        // AFTER the class's own spawn, which is what sets the model and may
        // already have made an object (or a static one): a class whose kind
        // the solver moves gets an object now. The class is the registered
        // one even when its instance failed to build, as the shell's was.
        if (SpawnPhysics is { } physics && instance?.SpawnsPhysics != false
            && _byClassName.TryGetValue(className, out var reg))
            physics.Spawned(handle, reg.Kind);
    }

    /// <summary>
    /// The managed physics init the spawn asks for a Prop kind's object
    /// (native's CStandaloneEntity::Spawn did, after OnManagedSpawn). Set by
    /// the composition once vphysics answers; null asks nothing.
    /// </summary>
    public SourceSharp.Server.EntityPhysicsCreation? SpawnPhysics { get; set; }

    /// <summary>
    /// <c>CServerGameDLL::ServerActivate</c>'s walk, for one entity —
    /// plan_entity.md step 2d. Answers whether there was an instance to activate.
    /// </summary>
    /// <remarks>
    /// <b>Find and NOT EnsureInstance</b>, which is the opposite call from
    /// <see cref="Spawn"/> and is the difference between the two moments: an
    /// activate reaches an entity that has already been keyed and spawned, so an
    /// entity with no instance by now is one whose class failed to build. Making
    /// one here would hand a freshly constructed object an <c>OnServerActivate</c> and
    /// no <c>OnSpawn</c>, in a build where every declared key had already been
    /// dropped on the floor.
    /// </remarks>
    public bool Activate(EntityHandle handle)
    {
        if (_directory.Resolve(handle) is not { } instance)
            return false;

        instance.OnServerActivate();
        return true;
    }

    /// <summary>
    /// The instance for an entity, created on first use.
    ///
    /// Created on demand rather than at spawn because of the order Source
    /// works in: an entity is constructed, then handed every keyvalue in the
    /// map, and only then spawned. An instance that appeared at spawn missed
    /// the map's values entirely — every declared key in every map sat at its
    /// declared default, silently, because a dropped key looks exactly like a
    /// key the map never wrote.
    ///
    /// Unknown class names are reported rather than ignored: it means the
    /// engine created something from a registration we no longer have.
    /// </summary>
    private ScriptedEntity? EnsureInstance(EntityHandle handle, string className)
    {
        // NO CLASSNAME COMPARISON HERE, AND THAT IS ARGUED RATHER THAN
        // OVERLOOKED. This method's className argument comes from the ENGINE on
        // both of its live call sites -- Spawn and KeyValue -- where the handle
        // and the name describe the same entity by construction, and a refusal
        // on this path would drop a map's keyvalues on a disagreement that
        // cannot happen. The call site where the name comes off a FILE is the
        // restore, and the comparison is there, in Apply, in front of this
        // call. See Apply for what it is defending against.
        if (_directory.Resolve(handle) is { } existing)
            return existing;

        if (!_byClassName.TryGetValue(className, out var reg))
        {
            Log.Warning($"[SourceSharp] spawned '{className}' has no ScriptedEntity class\n");
            return null;
        }

        // Built through the container when the module gave us one, so an entity
        // can ask for the systems it needs in its own constructor instead of
        // reaching for a static. A class that takes nothing costs the same
        // either way: the constructor plan is worked out once per class and
        // kept, and for a parameterless one it is a bare Invoke.
        //
        // A class the container cannot build is reported and skipped, and this
        // is the one place that decision matters: the entity still exists
        // natively, so the map still has the thing the mapper placed — it just
        // has no managed half, exactly as an unregistered classname does. The
        // alternative, letting the resolution failure out, takes down whatever
        // was loading the map.
        if (!Activate(reg.Type, out var instance))
            return null;

        instance.Handle = handle;
        instance.ClassName = className;
        instance.Owner = this;

        // What the class was REGISTERED as -- step 2b. The default
        // TouchEveryTick reads it, and it is the one thing about the class the
        // instance could not otherwise answer for itself.
        instance.RegisteredKind = reg.Kind;

        // Before anything can call through it, and for the same reason the net
        // state binding below happens here: a property read on an unbound
        // instance would go somewhere that is not the engine, silently.
        //
        // The real engine when the container holds none, which is the
        // no-container case rather than a fault — it is what the entity would
        // have called directly before any of this existed.
        instance.BindAreas(Resolver);

        // The registry's own think heap, for the same reason and at the same
        // moment: an entity that books a think against a scheduler nobody runs
        // has a schedule that never comes due, silently.
        instance.BindThinks(_directory.Think);


        // Bound before anything can set a property on it, which is the whole
        // reason it happens here rather than at spawn: a value written to an
        // unbound state goes into managed memory and nowhere else, silently.
        // BOUND TO THE BLOB ONLY IF THERE IS ONE -- step 2e. A class of kind
        // Logical has no shell, so it has no native allocation and no networked
        // payload; NetState.Bind on one would be a named exception rather than
        // a silent nothing, which is right for a class that declared networked
        // state it can never send, and wrong as the default for every logical
        // entity in the map. BindLocal keeps the state in managed memory, which
        // is exactly what "server-only" has always meant.
        instance.NetStates = reg.Kind != ScriptedEntityKind.Logical
            ? _side.AttachBound(instance, handle, NetStateFields)
            : NetStateBinding.AttachLocal(instance, handle, NetStateFields);

        // Same reason, one step earlier in the entity's life: a var set that is
        // null when the first keyvalue arrives drops the map's value on the
        // floor, and a dropped key looks exactly like a key the map never wrote.
        instance.VarSets = AttachVarSets(instance, reg);

        // Give every declared output its name and slot, so Fire() needs no
        // arguments at the call site.
        var outputs = new List<Output>(reg.OutputFields.Length);

        for (int i = 0; i < reg.OutputFields.Length; i++)
        {
            if (reg.OutputFields[i].GetValue(instance) is Output output)
            {
                output.Bind(instance, reg.OutputNames[i], i);
                outputs.Add(output);
            }
            else
            {
                Log.Warning($"[SourceSharp] {reg.Type.Name}.{reg.OutputFields[i].Name} was not initialised; use = new()\n");
            }
        }

        // Kept rather than discarded, for the same reason the states above are:
        // a reload has to walk them, and reflecting for them a second time would
        // be a second answer to a question already answered here.
        instance.Outputs = outputs.ToArray();

        _directory.Bind(handle, instance);
        return instance;
    }

    /// <summary>
    /// Offers a map key to each var set the entity holds, and hands what none
    /// of them claims to the entity itself.
    ///
    /// <para>
    /// The declared sets come first because that is the contract: the override
    /// exists for what a declaration cannot express — logic_case's sixteen
    /// numbered keys, and little else.
    /// </para>
    ///
    /// <para>
    /// NOTHING REFLECTS HERE ANY MORE. This was a dictionary from key name to
    /// FieldInfo, built per class at registration and walked per key per entity,
    /// with a converter beside it deciding what the field's type meant. All of
    /// it is now the generated OnKeyDataUpdate — which the compiler checked, and
    /// which knows the whole declared set well enough to notice a key nobody
    /// claimed.
    /// </para>
    /// </summary>
    public bool KeyValue(EntityHandle handle, string className, string key, string value)
    {
        // Not Find: this is the call that arrives BEFORE spawn, and creating
        // the instance here is the whole reason a map's values reach managed
        // code at all.
        var instance = EnsureInstance(handle, className);
        if (instance is null)
            return false;

        // ---- THE I/O KEYS, BEFORE ANYTHING ELSE SEES THEM (step 2a) --------
        //
        // Two kinds of key are claimed here rather than by a class, and both
        // are claimed for the same reason parentname is claimed by the shell:
        // they are part of what "an entity" means to a mapper, not part of what
        // any particular class is.
        //
        // TARGETNAME feeds the directory's name index, which is what every
        // output resolves its target against. It is ALSO left to fall through,
        // because a class is entitled to react to being named and nothing here
        // knows whether one does.
        if (string.Equals(key, "targetname", StringComparison.OrdinalIgnoreCase))
            _directory.SetName(handle, value);

        // A CONNECTION. A map writes one exactly like an input: the KEY is the
        // output's name and the VALUE is the action. The native shell used to
        // find these by asking StandaloneOutputIndex against the same list the
        // class registered; here the list IS the class's, so the lookup is one
        // indirection shorter and there is no second side to disagree with.
        //
        // Claimed and returned, unlike targetname: an output name is this
        // class's own, so nothing downstream has a use for it, and letting it
        // fall through would put every connection in the map through
        // ReportUnclaimed's near-miss check.
        // A KEY THE CLASS ALSO DECLARES AS A VAR WINS OVER THE OUTPUT, which is
        // stock's answer and NOT a preference. Stock resolves the collision by
        // DATADESC ORDER inside ParseKeyvalue: DEFINE_OUTPUT sets FTYPEDESC_KEY
        // like a keyfield does, so both are candidates and the first field
        // declared takes the key.
        //
        // env_global is the case that has it (logicentities.cpp:1001-1019):
        //
        //     DEFINE_KEYFIELD( m_counter, FIELD_INTEGER, "counter" ),   // 4th
        //     ...
        //     DEFINE_OUTPUT( m_outCounter, "Counter" ),                 // 12th
        //
        // and the names differ only in case, which neither side compares. Stock
        // gives the number to m_counter; without this the connection branch
        // takes it first and a mapper's `"counter" "5"` becomes a connection
        // with no delimiter in it -- silently, since a connection to nothing is
        // the ordinary case and nothing reports one.
        //
        // Narrow on purpose: it changes NOTHING for a class that does not
        // declare both spellings, which is every class in the tree but that one.
        if (IsDeclaredOutput(instance, key) && !DeclaresVar(instance, key))
        {
            _directory.Connect(handle, key, value);
            return true;
        }

        // LOWERED ONCE, HERE, for all of the entity's sets, and then offered
        // through OnLoweredKey — the inside door, which takes the fold as
        // already done. A class and its bases each hold a set, which is how
        // fifteen triggers read TriggerVolume's "wait", so going through the
        // public OnKeyDataUpdate would fold the same key two or three times per
        // offer. This is the only caller that takes this door; everything else
        // takes the public one and is folded for.
        //
        // The ORIGINAL spelling carries on below. Everything past this point is
        // read by a person — an entity's own override matching the map's text, a
        // message naming the key a mapper typed — and none of them should be
        // handed a key the map never wrote.
        string lowered = ScriptedVarSet.LowerKey(key);

        foreach (var set in instance.VarSets)
        {
            if (set.OfferLowered(lowered, value))
                return true;
        }

        if (instance.OnKeyValue(key, value))
            return true;

        if (TakeBaseKey(instance, handle, lowered, value))
            return true;

        _byClassName.TryGetValue(instance.ClassName, out var reg);
        ReportUnclaimed(instance, reg, key);
        return false;
    }

    /// <summary>
    /// The keys CBaseEntity and CBaseAnimating take for every class that does
    /// not take them itself: disableshadows (CBaseEntity::KeyValue's
    /// EF_NOSHADOW, any entity), and for a model entity (a prop or an item)
    /// skin, body (and SetBodyGroup) and modelscale, CBaseAnimating's
    /// keyfields, set as the map writes them before the spawn.
    /// </summary>
    private bool TakeBaseKey(ScriptedEntity instance, EntityHandle handle, string lowered, string value)
    {
        bool animating = instance.RegisteredKind is ScriptedEntityKind.Prop or ScriptedEntityKind.Item;

        switch (lowered)
        {
            case "disableshadows":
                if (Atoi(value) != 0 && Services?.Resolve(typeof(SourceSharp.Server.IEntityEffectBits)) is SourceSharp.Server.IEntityEffectBits bits)
                    bits.Set(handle, EntityEffect.NoShadow, true);
                return true;

            case "skin" when animating:
                if (Services?.Resolve(typeof(IServerSkins)) is IServerSkins skins)
                    skins.SetSkin(handle, Atoi(value));
                return true;

            // m_nBody: its keyfield, and SetBodyGroup, the input that is also a key.
            case "body" or "setbodygroup" when animating:
                if (Services?.Resolve(typeof(IServerSkins)) is IServerSkins bodies)
                    bodies.SetBody(handle, Atoi(value));
                return true;

            case "modelscale" when animating:
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float scale)
                    && Services?.Resolve(typeof(IEngineEntityRender)) is IEngineEntityRender render)
                    render.SetModelScale(handle, scale);
                return true;
        }

        return false;
    }

    /// <summary>atoi: the leading integer, 0 when there is none.</summary>
    private static int Atoi(string value)
    {
        ReadOnlySpan<char> s = value.AsSpan().TrimStart();
        int end = s.Length > 0 && (s[0] == '-' || s[0] == '+') ? 1 : 0;
        while (end < s.Length && char.IsAsciiDigit(s[end]))
            end++;
        return int.TryParse(s[..end], System.Globalization.NumberStyles.AllowLeadingSign,
                            System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    /// <summary>
    /// A key nothing managed claimed.
    ///
    /// <para>
    /// THIS IS THE POINT OF THE GENERATED DISPATCH, and it is deliberately here
    /// rather than inside it: only this level knows that the entity's own
    /// <see cref="ScriptedEntity.OnKeyValue"/> also declined, and only this
    /// level has every set's keys to compare against.
    /// </para>
    ///
    /// <para>
    /// NOT A WARNING BY DEFAULT, because unclaimed is normal. Source hands both
    /// sides every key — see CSourceSharpScriptedLogical::KeyValue — so a
    /// perfectly healthy entity declines "origin", "angles", "targetname" and a
    /// dozen more on every spawn. Warning on those would be a firehose, and a
    /// firehose is how a real message gets ignored.
    /// </para>
    ///
    /// <para>
    /// A NEAR MISS IS DIFFERENT and IS warned about. A key that differs from a
    /// declared one by a single character is not an engine key that happens to
    /// pass through; it is a mapper who meant one of ours. That is the failure
    /// the reflective binding could never report — it returned "not mine" for a
    /// typo and for "origin" alike — and it is the whole reason the declared set
    /// is now known at compile time.
    /// </para>
    ///
    /// <para>
    /// Everything else is a <see cref="EngineSide.Msg"/> under <c>developer</c>,
    /// which is what a DevMsg is: free unless somebody is looking. Never a
    /// throw — one bad key must not take a map load down.
    /// </para>
    /// </summary>
    private void ReportUnclaimed(ScriptedEntity instance, Registration? reg, string key)
    {
        if (reg is null)
            return;

        foreach (var set in reg.VarSetKeys)
        {
            foreach (var declared in set)
            {
                if (IsNearMiss(key, declared.Name))
                {
                    Log.Warning($"[SourceSharp] {instance.ClassName}: no key '{key}'" +
                                   $" — did you mean '{declared.Name}'?\n");
                    return;
                }
            }
        }

        if (DeveloperLevel <= 0)
            return;

        var known = reg.VarSetKeys.SelectMany(s => s).Select(d => d.Name)
                       .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        Log.Msg($"[SourceSharp] {instance.ClassName}: no key '{key}'" +
                   $" (declares: {string.Join(", ", known)})\n");
    }

    /// <summary>
    /// Whether two key names differ by at most one character — one substituted,
    /// one inserted or one dropped. Not a general edit distance: bounding it at
    /// one is what keeps "a mapper's typo" apart from "a different word", and it
    /// answers in a single pass without allocating.
    /// </summary>
    internal static bool IsNearMiss(string written, string declared)
    {
        if (Math.Abs(written.Length - declared.Length) > 1)
            return false;

        // Equal is not a near miss; it is a hit, and this is only reached when
        // nothing claimed the key.
        if (string.Equals(written, declared, StringComparison.OrdinalIgnoreCase))
            return false;

        // Too short to tell a typo from a different name: "on" and "in" are both
        // plausible keys, and guessing between them is worse than saying nothing.
        if (declared.Length < 4)
            return false;

        int i = 0, j = 0;
        bool spent = false;

        while (i < written.Length && j < declared.Length)
        {
            if (char.ToLowerInvariant(written[i]) == char.ToLowerInvariant(declared[j]))
            {
                i++;
                j++;
                continue;
            }

            if (spent)
                return false;

            spent = true;

            if (written.Length == declared.Length) { i++; j++; }
            else if (written.Length > declared.Length) { i++; }
            else { j++; }
        }

        // Whatever is left is the one edit, if it has not been spent already.
        return spent || (written.Length - i) + (declared.Length - j) == 1;
    }

    /// <summary>
    /// The <c>developer</c> convar, read at most once per level.
    ///
    /// Cached because the unclaimed path is the COMMON path — most of a map's
    /// keys belong to the engine — and a console lookup per key per entity is a
    /// few thousand string conversions per map load to answer a question whose
    /// answer almost never changes mid-level.
    /// </summary>
    private int _developer = -1;

    private int DeveloperLevel
    {
        get
        {
            if (_developer < 0)
            {
                _developer = int.TryParse(NativeConsole.ValueOf(_engine.Require<IEngineConsole>(), "developer"), out int level)
                    ? level : 0;
            }

            return _developer;
        }
    }

    /// <summary>
    /// Creates any var set the class forgot to initialise and tells each which
    /// entity class it belongs to, so its messages name something a mapper can
    /// find.
    /// </summary>
    private ScriptedVarSet[] AttachVarSets(ScriptedEntity instance, Registration reg)
    {
        if (reg.VarSetFields.Length == 0)
            return Array.Empty<ScriptedVarSet>();

        var sets = new List<ScriptedVarSet>(reg.VarSetFields.Length);

        foreach (var field in reg.VarSetFields)
        {
            if (field.GetValue(instance) is not ScriptedVarSet set)
            {
                if (Activator.CreateInstance(field.FieldType) is not ScriptedVarSet created)
                {
                    Log.Warning($"[SourceSharp] {reg.Type.Name}.{field.Name} could not be " +
                                   $"created; its map keys will never arrive\n");
                    continue;
                }

                set = created;

                if (!field.IsInitOnly)
                    field.SetValue(instance, set);
            }

            set.OwnerName = instance.ClassName;
            set.Clock = VarsClock;
            set.Log = Log;
            sets.Add(set);
        }

        return sets.ToArray();
    }

    /// <summary>
    /// What each var set on a class declares, read once at registration.
    ///
    /// <para>
    /// The descriptor table is static per set type, so a throwaway instance is
    /// enough to reach it — and reaching it here rather than per entity is what
    /// keeps the debugging views below from reflecting over anything.
    /// </para>
    /// </summary>
    private static ScriptedVarDescriptor[][] DescribeVarSets(Type owner, FieldInfo[] fields, ILog log)
    {
        var described = new ScriptedVarDescriptor[fields.Length][];

        for (int i = 0; i < fields.Length; i++)
        {
            described[i] = Array.Empty<ScriptedVarDescriptor>();

            try
            {
                if (Activator.CreateInstance(fields[i].FieldType) is ScriptedVarSet probe)
                    described[i] = probe.Descriptors.ToArray();
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp] {owner.Name}.{fields[i].Name} is a " +
                               $"{fields[i].FieldType.Name} that could not be described: {e.Message}\n");
            }
        }

        return described;
    }

    /// <summary>
    /// Fields of a type and everything it derives from. GetFields does not
    /// return private fields of base classes, and the base declares keys of its
    /// own that every entity should answer to.
    /// </summary>
    private static List<FieldInfo> FieldsIncludingBase(Type type)
    {
        var result = new List<FieldInfo>();

        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            result.AddRange(t.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        return result;
    }

    /// <summary>
    /// How deep an input may nest before it is treated as runaway.
    ///
    /// Inputs legitimately nest: an input fires an output that fires another
    /// entity's input, and a chain a few deep is ordinary map logic. What is not
    /// ordinary is a cycle, and a cycle here is close to undiagnosable without
    /// this — it overflows the stack, and CoreCLR answers a stack overflow by
    /// printing "Stack overflow." and aborting the process WITHOUT running any
    /// handler, so there is no exception, no trace, and no clue which entity did
    /// it. (That is exactly how the use key on a crate presented: the shell's
    /// Use went through AcceptInput, CBaseEntity's own "Use" input handler
    /// called Use again, and the game simply died.)
    ///
    /// Sixty-four is well past any real chain and far short of the stack.
    /// </summary>
    private const int MaxInputDepth = 64;

    // This registry's input nesting (statics to zero: it was a thread-static
    // every registry on the thread shared). Inputs run on the game thread.
    private int _inputDepth;

    /// <summary>
    /// Routes an input to the method bound to its name, falling back to the
    /// entity's own handler for anything unclaimed.
    /// </summary>
    /// <summary>
    /// Routes a physics collision to the entity that was in it.
    ///
    /// Not a touch: this is the solver reporting an impact it has already
    /// resolved, so it carries a contact point and a speed that a touch does
    /// not have.
    /// </summary>
    /// <summary>The solver woke this entity's physics object.</summary>
    public void PhysicsWake(EntityHandle handle) => Find(handle)?.OnPhysicsWake();

    /// <summary>
    /// The solver broke a constraint; its owner hears it. The ring names the
    /// constraint, not its owner, and a break is rare, so the owner is found by
    /// asking (plan_physics_managed A23). Answers whether anyone owned it.
    /// </summary>
    public bool ConstraintBroken(PhysicsId constraint)
    {
        foreach (var instance in _directory.Entities)
        {
            if (instance.OnPhysicsConstraintBroken(constraint))
                return true;
        }
        return false;
    }

    /// <summary>The solver put this entity's physics object to sleep.</summary>
    public void PhysicsSleep(EntityHandle handle) => Find(handle)?.OnPhysicsSleep();

    /// <summary>
    /// The solver moved this entity's object this frame (an Update event, one
    /// per object on its active list). Answers whether anyone was told.
    /// </summary>
    public bool PhysicsUpdate(EntityHandle handle)
    {
        var instance = Find(handle);
        if (instance is null)
            return false;

        instance.OnPhysicsUpdate();
        return true;
    }

    /// <summary>The shell created this entity's physics object (EntityOp.PhysicsCreated).</summary>
    public bool PhysicsCreated(EntityHandle handle)
        => PhysicsCreated(Resolve(handle));

    /// <inheritdoc cref="PhysicsCreated(EntityHandle)"/>
    public bool PhysicsCreated(in EntityRef entity)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        instance.OnPhysicsCreated();
        return true;
    }

    /// <summary>One side of a solved impact to the entity on it (VPhysicsCollision).</summary>
    public void PhysicsCollision(EntityHandle handle, in PhysicsEvent ev, int index)
        => Find(handle)?.OnVPhysicsCollision(in ev, index);

    /// <summary>One side of a solved impact to the shadow-controlled entity on it (VPhysicsShadowCollision).</summary>
    public void PhysicsShadowCollision(EntityHandle handle, in PhysicsEvent ev, int index)
        => Find(handle)?.OnVPhysicsShadowCollision(in ev, index);

    /// <summary>A shadow's stress after the step to the entity it belongs to (B5).</summary>
    public void PhysicsStress(EntityHandle handle, in PhysicsEvent ev)
        => Find(handle)?.OnVPhysicsStress(in ev);

    public bool Input(EntityHandle handle, string name, string value, float number, EntityHandle activator)
        => Input(Resolve(handle), name, value, number, activator);

    /// <inheritdoc cref="Input(EntityHandle, string, string, float, EntityHandle)"/>
    public bool Input(in EntityRef entity, string name, string value, float number, EntityHandle activator)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        // Thrown rather than merely refused, so it surfaces as an exception with
        // a stack through the handler in Entry — which turns a silent abort into
        // a named entity and a named input.
        if (_inputDepth >= MaxInputDepth)
        {
            throw new InvalidOperationException(
                $"input '{name}' on {instance.ClassName} '{instance.Name}' nested past " +
                $"{MaxInputDepth} levels; this is a cycle, not a chain");
        }

        _inputDepth++;

        try
        {
            if (_byClassName.TryGetValue(instance.ClassName, out var reg)
                && reg.Inputs.TryGetValue(name, out var method))
            {
                method.Invoke(instance, ArgumentsFor(method, value, number, activator));
                return true;
            }

            return instance.OnInput(name, value, number, activator);
        }
        finally
        {
            _inputDepth--;
        }
    }

    /// <summary>Current input nesting. For tests, which have no engine to nest through.</summary>
    internal int InputDepth => _inputDepth;

    /// <summary>
    /// Raised whenever any of this registry's outputs fires (statics to zero:
    /// it was one static event every registry in the process raised).
    ///
    /// Here rather than in the development API because this is the one place
    /// every firing passes through, and an output that fired is the single most
    /// useful thing a map debugger can be told about -- a connection that never
    /// ran looks exactly like one that ran and did nothing.
    ///
    /// Raised on the game thread, since that is the only place Fire is legal.
    /// Null costs a branch on a path that is not hot.
    /// </summary>
    public event Action<ScriptedEntity, string, string?>? OutputFired;

    /// <summary>Raises <see cref="OutputFired"/> for one of this registry's outputs.</summary>
    internal void RaiseOutputFired(ScriptedEntity entity, string output, string? value)
        => OutputFired?.Invoke(entity, output, value);

    /// <summary>
    /// <inheritdoc cref="ScriptedEntity.OnAddOutput" path="/summary/para[1]"/>
    ///
    /// <para>
    /// The value is <c>&lt;name&gt; &lt;rest&gt;</c>. If <c>&lt;name&gt;</c> is
    /// one of this class's declared outputs the rest is a connection in the
    /// map's own spelling —
    /// <c>target:input:parameter:delay:timesToFire</c> — and goes through the
    /// SAME <c>EntityDirectory.Connect</c> the map's parse uses, so a
    /// runtime-made connection is indistinguishable from a declared one
    /// afterwards. That matters for exactly the reason this method exists: it
    /// is then carried across a hot reload by <c>SaveIo</c> like any other, and
    /// a test of that is a test of the real path.
    /// </para>
    ///
    /// <para>
    /// Otherwise the whole value is a KEYVALUE, which is stock's other arm and
    /// the one <c>ent_fire worldspawn AddOutput "detailmaterial x"</c> takes.
    /// </para>
    ///
    /// <para>
    /// Answers true when it did one of the two, and false when it did neither —
    /// which reaches the caller as an input the entity did not accept, rather
    /// than as a quiet success.
    /// </para>
    /// </summary>
    internal bool AddOutput(EntityHandle handle, ScriptedEntity instance, string value)
    {
        string text = value?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return false;

        // NO SPACE IS THE ENGINE'S OWN REFUSAL, and it is refused here for its
        // reason: CBaseEntity::InputAddOutput warns "AddOutput input fired with
        // bad string" and does nothing at all when strchr(' ') finds nothing.
        int space = text.IndexOf(' ');

        if (space <= 0)
            return false;

        string head = text.Substring(0, space);

        // AND EVERY COLON BECOMES A COMMA, which is not a convenience: it is
        // what the engine does, in exactly this place, and the comment there
        // says why -- "Has to be done this way because Hammer doesn't allow ,'s
        // inside parameters" (baseentity.cpp:6630). EventAction.Parse splits on
        // ESC or a COMMA and never on a colon, so without this swap every field
        // after the first would land inside the TARGET's name and the
        // connection would aim at an entity called "door:Open::0:-1".
        string rest = text.Substring(space + 1).Trim().Replace(':', ',');

        // AN OUTPUT, BY THE CLASS'S OWN SPELLING. IsDeclaredOutput is the same
        // test the keyvalue path makes, so "which names are outputs" has one
        // answer in this file rather than two that can disagree.
        if (rest.Length > 0 && IsDeclaredOutput(instance, head))
        {
            _directory.Connect(handle, OutputNameOf(instance, head), rest);
            return true;
        }

        // THE KEYVALUE ARM, through the ENTITY'S OWN KeyValue -- stock's
        // InputAddOutput ends in `KeyValue( sOutputName, sChar+1 )` on the entity
        // itself. Entities.SetKeyValue is that: for a native shell,
        // CStandaloneEntity::KeyValue (the shell's arm, which keeps the name
        // GetName reads, then the unconditional offer back to this registry);
        // for a shell-less entity, the shell-less state and then this registry.
        // Offering it to this registry alone renamed an entity in the name index
        // and nowhere else: FindByName found the new name, GetName said the old
        // one, and the shell's claim of "targetname" never counted, so the
        // input was reported as naming neither an output nor a key.
        return new SourceSharp.Server.EntityKeyValues(instance.Entities).Apply(handle, head, rest);
    }

    /// <summary>
    /// The declared output's own spelling of its name, so a connection made
    /// under a different case is hung where the output will look for it.
    ///
    /// <para>
    /// <c>IsDeclaredOutput</c> matches case-insensitively because a mapper's
    /// text does; the connection table is keyed by the name the OUTPUT fires
    /// under. Storing the caller's spelling would hang a connection nothing
    /// ever fires — which is the silent half-working shape this file keeps
    /// meeting.
    /// </para>
    /// </summary>
    private static string OutputNameOf(ScriptedEntity instance, string name)
    {
        foreach (Output output in instance.Outputs)
        {
            if (string.Equals(output.Name, name, StringComparison.OrdinalIgnoreCase))
                return output.Name;
        }

        return name;
    }

    /// <summary>
    /// The mapper-visible name of each output, which is the field's name unless
    /// the Output was constructed with one of its own.
    /// </summary>
    private static string[] OutputNamesFor(FieldInfo[] fields)
        => fields.Select(f => f.GetCustomAttribute<OutputNameAttribute>()?.Name ?? f.Name).ToArray();

    /// <summary>
    /// The input name a handler answers to: its own name with the "On" dropped.
    /// A handler that is not named for an event still binds, under its full
    /// name, but is reported — the convention is what makes the map-facing name
    /// readable from the declaration.
    /// </summary>
    private static string InputNameFor(MethodInfo method, Type owner, ILog log)
    {
        if (method.Name.Length > 2 && method.Name.StartsWith("On", StringComparison.Ordinal))
            return method.Name[2..];

        log.Warning($"[SourceSharp] {owner.Name}.{method.Name} is a [ScriptedEvent] but is not named On...; bound as '{method.Name}'\n");
        return method.Name;
    }

    /// <summary>Methods of a type and everything it derives from.</summary>
    private static List<MethodInfo> MethodsIncludingBase(Type type)
    {
        var result = new List<MethodInfo>();

        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            result.AddRange(t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        }

        return result;
    }

    /// <summary>The signatures an input method may have. Checked once, at registration.</summary>
    private static bool IsBindableInput(MethodInfo method)
    {
        var parameters = method.GetParameters();

        return parameters.Length switch
        {
            0 => true,
            1 => parameters[0].ParameterType == typeof(float)
              || parameters[0].ParameterType == typeof(string)
              || parameters[0].ParameterType == typeof(InputData),
            _ => false,
        };
    }

    private static object?[] ArgumentsFor(MethodInfo method, string value, float number, EntityHandle activator)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return Array.Empty<object?>();

        var type = parameters[0].ParameterType;

        if (type == typeof(float))
            return new object?[] { number };

        if (type == typeof(string))
            return new object?[] { value };

        return new object?[] { new InputData(value, number, activator) };
    }

    public void Think(EntityHandle handle) => Think(Resolve(handle));

    /// <summary>
    /// The same think, against an entity already resolved (plan_entity.md
    /// §3.4). This is the shape every op below now has: the handle is looked up
    /// ONCE per crossing, by <see cref="Resolve(EntityHandle)"/>, and what the
    /// op gets is the answer.
    /// </summary>
    public void Think(in EntityRef entity) => entity.Entity?.OnThink();

    /// <summary>
    /// Routes a touch, and reports whether an instance took it — the caller has
    /// its own pickups to fall back on for entities this does not own.
    ///
    /// <para>
    /// The "players only" rule lives HERE rather than in the shell, and that is
    /// a deliberate move: whether a crate rolling over a health kit collects it
    /// is a game rule, and the native side had been quietly deciding it for
    /// every entity in the mod. A class that wants every toucher — a trigger
    /// that counts crates, a pressure plate — overrides
    /// <see cref="ScriptedEntity.TouchedByAnything"/> and gets them.
    /// </para>
    /// </summary>
    public bool Touch(EntityHandle handle, EntityHandle toucher)
        => Touch(Resolve(handle), toucher);

    /// <inheritdoc cref="Touch(EntityHandle, EntityHandle)"/>
    public bool Touch(in EntityRef entity, EntityHandle toucher)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        if (!DeliverTouchTo(instance, toucher))
            return true;

        instance.OnTouch(toucher);
        return true;
    }

    /// <summary>
    /// Whether a toucher is one this entity cares about.
    ///
    /// The rule is "players only unless the class says otherwise", and it lives
    /// here rather than in the shell because it is a rule: whether a crate
    /// rolling over a health kit collects it is a decision about the game, and
    /// C++ had been making it for every entity in the mod with no way to say
    /// otherwise.
    ///
    /// <para>
    /// With no engine to ask — the tests — the touch is DELIVERED. A guard that
    /// cannot be evaluated must not silently swallow the call it guards; that
    /// turns "the ABI is missing a function" into "every pickup stopped
    /// working" with nothing anywhere to say why.
    /// </para>
    /// </summary>
    private bool DeliverTouchTo(ScriptedEntity instance, EntityHandle toucher)
        => instance.TouchedByAnything
        || !HostArea.IsInitialized
        || EntityArea.IsPlayer(toucher);

    /// <summary>
    /// Drops the instance behind a removed entity, and tells it first.
    ///
    /// Without this the map's entities accumulate for the life of the level:
    /// anything that removes itself when it fires would leave its instance
    /// behind, and a round restart that recreates the map — which this mod does
    /// not do today, but HL2MP's CleanUpMap does — would add a whole fresh set
    /// every round.
    /// </summary>
    public bool Removed(EntityHandle handle) => Removed(Resolve(handle));

    /// <summary>
    /// THE DIRECTORY'S REMOVAL NOTIFY — <c>plan_entity.md</c> step 2c. Tells one
    /// entity it is going, and writes the liveness record, WITHOUT unbinding it.
    ///
    /// <para>
    /// <b>The unbind is deliberately not here</b>, which is the one way this
    /// differs from <see cref="Removed(EntityHandle)"/>. The directory unbinds
    /// the whole batch after every entity in it has been told, so what an
    /// entity's teardown can still resolve does not depend on where it sits in
    /// the batch. Unbinding here would make the first entity's <c>OnRemoved</c>
    /// see a world the last one's does not.
    /// </para>
    ///
    /// <para>
    /// <b>The liveness record is written here because nothing else writes it any
    /// more.</b> It used to be the <c>finally</c> of the inbound
    /// <c>EntityOp.Removed</c> thunk, and that op no longer arrives: the shell
    /// destructor stopped announcing a removal when the announcement became a
    /// managed call. A mirror that kept saying "alive" about a freed entity is
    /// exactly the wrong direction for that record to fail in
    /// (<see cref="LivenessMirror"/>), so it is written even when the entity has
    /// no managed instance at all.
    /// </para>
    /// </summary>
    public void NotifyRemoved(EntityHandle handle)
    {
        try
        {
            _directory.Resolve(handle)?.OnRemoved();
        }
        finally
        {
            // In a finally for the reason the thunk's was: the one case where a
            // left-behind "alive" record does real damage is exactly the case
            // where the teardown threw.
            _liveness.NoteRemoved(handle);
        }
    }

    /// <inheritdoc cref="Removed(EntityHandle)"/>
    public bool Removed(in EntityRef entity)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        instance.OnRemoved();
        _directory.Unbind(entity.Handle);
        return true;
    }

    /// <summary>
    /// Routes damage, and reports how much the entity accepted.
    /// <paramref name="hitGroup"/> is the open <see cref="DamageHitGroups"/>
    /// scope's, which the op does not carry: Generic for damage with no trace.
    /// </summary>
    public int Damaged(EntityHandle handle, EntityHandle attacker, float damage, int damageType,
                       Vector position = default, Vector force = default,
                       HitGroup hitGroup = HitGroup.Generic)
        => Damaged(Resolve(handle), attacker, damage, damageType, position, force, hitGroup);

    /// <inheritdoc cref="Damaged(EntityHandle, EntityHandle, float, int, Vector, Vector, HitGroup)"/>
    public int Damaged(in EntityRef entity, EntityHandle attacker, float damage, int damageType,
                       Vector position = default, Vector force = default,
                       HitGroup hitGroup = HitGroup.Generic)
        // The one place the engine's raw DMG_ word becomes typed. Native hands
        // it over as an int and every managed reader wants the domain.
        => entity.Entity?.OnDamagedAt(attacker, damage, (DamageType)damageType, position, force, hitGroup) ?? 0;

    /// <summary>Routes an entity's health reaching zero.</summary>
    public bool Killed(EntityHandle handle, EntityHandle attacker)
        => Killed(Resolve(handle), attacker);

    /// <inheritdoc cref="Killed(EntityHandle, EntityHandle)"/>
    public bool Killed(in EntityRef entity, EntityHandle attacker)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        instance.OnKilled(attacker);
        return true;
    }

    /// <summary>Routes a finished movement.</summary>
    public bool MoveDone(EntityHandle handle) => MoveDone(Resolve(handle));

    /// <inheritdoc cref="MoveDone(EntityHandle)"/>
    public bool MoveDone(in EntityRef entity)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        instance.OnMoveDone();
        return true;
    }

    /// <summary>Routes an obstruction.</summary>
    public bool Blocked(EntityHandle handle, EntityHandle other)
        => Blocked(Resolve(handle), other);

    /// <inheritdoc cref="Blocked(EntityHandle, EntityHandle)"/>
    public bool Blocked(in EntityRef entity, EntityHandle other)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        instance.OnBlocked(other);
        return true;
    }

    /// <summary>Routes a player leaving a volume.</summary>
    public bool EndTouch(EntityHandle handle, EntityHandle toucher)
        => EndTouch(Resolve(handle), toucher);

    /// <inheritdoc cref="EndTouch(EntityHandle, EntityHandle)"/>
    public bool EndTouch(in EntityRef entity, EntityHandle toucher)
    {
        var instance = entity.Entity;
        if (instance is null)
            return false;

        if (!DeliverTouchTo(instance, toucher))
            return true;

        instance.OnEndTouch(toucher);
        return true;
    }



    /// <summary>
    /// Whether <paramref name="outputName"/> is one of the outputs
    /// <paramref name="instance"/>'s class declared.
    ///
    /// <para>
    /// Case-insensitive, because <c>StandaloneOutputIndex</c> was and a map
    /// writing <c>"ontrigger"</c> has always connected to <c>OnTrigger</c>.
    /// Asked of the REGISTRATION rather than of the instance's bound
    /// <see cref="ScriptedEntity.Outputs"/>, because those two can differ: an
    /// <c>Output</c> field the class forgot to initialise is warned about and
    /// dropped from the instance's array, and a map that connects to it should
    /// still have its connection claimed rather than reported as a mystery key.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether the class declares <paramref name="key"/> as a map KEY — a
    /// <c>[ScriptVars]</c> property, under its mapper-visible name.
    ///
    /// <para>
    /// Only <see cref="KeyValue"/> asks, and only to settle the one collision an
    /// output name can have with a key name. See the comment there.
    /// </para>
    /// </summary>
    internal bool DeclaresVar(ScriptedEntity instance, string key)
    {
        if (!_byClassName.TryGetValue(instance.ClassName, out var reg))
            return false;

        foreach (var set in reg.VarSetKeys)
        {
            foreach (var declared in set)
            {
                if (string.Equals(declared.Name, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    internal bool IsDeclaredOutput(ScriptedEntity instance, string outputName)
    {
        if (!_byClassName.TryGetValue(instance.ClassName, out var reg))
            return false;

        foreach (string declared in reg.OutputNames)
        {
            if (string.Equals(declared, outputName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Fires one entity's output — the managed half of what
    /// <c>CStandaloneEntity::FireNamedOutput</c> used to do, minus the crossing
    /// and minus the slot number.
    ///
    /// <para>
    /// An output name a class never declared is REPORTED rather than ignored,
    /// which is the one thing the native version did that was worth keeping:
    /// "a mapper's connection that silently never fires is the hardest kind of
    /// map bug to find, and this is the one place that can see it happen".
    /// A name that IS declared but has no connection is silent, because that is
    /// the ordinary case — most outputs in most maps are wired to nothing.
    /// </para>
    /// </summary>
    /// <returns>How many events were posted.</returns>
    public int FireOutput(EntityHandle handle, string outputName, string? value,
                          EntityHandle activator)
    {
        var instance = Find(handle);

        if (instance is not null && !IsDeclaredOutput(instance, outputName))
        {
            Log.Warning($"[SourceSharp] entity '{instance.ClassName}' has no output named " +
                           $"'{outputName}'\n");
            return 0;
        }

        return _directory.FireOutput(handle, outputName, value, activator, Now);
    }

    /// <summary>
    /// Maps an output name to its slot. The native side asks us because we are
    /// the side that declared the list. -1 for an unknown name.
    /// </summary>
    public int ResolveOutput(EntityHandle handle, string outputName)
    {
        var instance = Find(handle);
        if (instance is null)
            return -1;

        return _byClassName.TryGetValue(instance.ClassName, out var reg)
            ? Array.FindIndex(reg.OutputNames, n => string.Equals(n, outputName, StringComparison.OrdinalIgnoreCase))
            : -1;
    }

    /// <summary>The instance driving an entity, or null if none is bound.</summary>
    public ScriptedEntity? Get(EntityHandle handle) => Find(handle);

    /// <summary>
    /// The instance a mapper named, or null.
    ///
    /// Two steps, and the first one is the ENGINE's: a name belongs to an
    /// entity, not to a managed object, so the name is resolved to a handle by
    /// the side that keeps the name table and only then looked up here. Which
    /// is also why an empty name is answered before asking: the engine's search
    /// treats it as a wildcard, and a keyvalue nobody filled in must not
    /// silently select the first entity in the map.
    /// </summary>
    public ScriptedEntity? FindByName(string name)
    {
        if (name.Length == 0)
            return null;

        // THE DIRECTORY'S OWN INDEX FIRST (step 2a). It is fed by the
        // targetname keyvalue, it is the index every output already resolves
        // against, and asking it costs no crossing where Engine.FindEntityByName
        // costs one per call. Two answers for one question is a hazard, so the
        // engine is asked only for what the index cannot hold: an entity named
        // through some route other than a map key, and the world.
        var found = new List<EntityHandle>(1);

        if (_directory.FindByName(name, found) > 0)
            return Find(found[0]);

        var handle = FindFirstNamed(name);
        return EntityArea.IsValid(handle) ? Find(handle) : null;
    }

    /// <summary>
    /// Every live instance of a classname.
    ///
    /// <para>
    /// The managed answer to <c>gEntList.FindEntityByClassname</c>, and it
    /// exists because that call CANNOT see the parallel hierarchy: it walks the
    /// engine's own entity list, where every managed entity appears as one of a
    /// handful of shell classes rather than under the name a map wrote. Two
    /// managed entities that have to find each other by classname have to do it
    /// here — <c>env_smokestack</c> finding its <c>env_particlelight</c>s is the
    /// first pair that does.
    /// </para>
    ///
    /// <para>
    /// Answered from the LIVE table rather than the engine, so it sees exactly
    /// the entities this registry is driving and nothing else. Unordered, which
    /// is what a dictionary walk is; a caller that needs an order has to say so.
    /// </para>
    /// </summary>
    public IEnumerable<ScriptedEntity> OfClass(string className)
    {
        foreach (var instance in _directory.Entities)
        {
            if (string.Equals(instance.ClassName, className, StringComparison.OrdinalIgnoreCase))
                yield return instance;
        }
    }

    /// <summary>Drops instances whose entity is gone, called when a level ends.</summary>
    //-------------------------------------------------------------------------
    // Introspection.
    //
    // What a map author or a debugger wants to know about an entity class is
    // exactly what registration already worked out — its inputs, its outputs and
    // its map keys — so this reads back the registration rather than reflecting
    // again. The alternative, walking attributes on demand, would answer a
    // little differently from what was actually registered, which is the one
    // thing a debugging view must never do.
    //-------------------------------------------------------------------------

    /// <summary>Every declared entity class, with what a map can send it and what it sends back.</summary>
    public IReadOnlyList<ScriptedClassInfo> DescribeClasses()
    {
        var result = new List<ScriptedClassInfo>(_byClassName.Count);

        foreach (var (className, reg) in _byClassName)
        {
            int live = 0;
            foreach (var instance in _directory.Entities)
            {
                if (string.Equals(instance.ClassName, className, StringComparison.OrdinalIgnoreCase))
                    live++;
            }

            result.Add(new ScriptedClassInfo(
                className,
                reg.Kind.ToString(),
                reg.Type.Name,
                reg.Inputs.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
                reg.OutputNames.ToArray(),
                reg.VarSetKeys.SelectMany(s => s)
                    .Select(d => new ScriptedFieldInfo(d.Name, d.TypeName))
                    .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                live));
        }

        return result.OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Every live instance, with its map-key values and what each output has
    /// done so far.
    ///
    /// Touches the engine for position and health, so it has to run on the game
    /// thread — the caller is responsible for that, as it is for every other
    /// read of live entity state.
    /// </summary>
    public IReadOnlyList<ScriptedInstanceInfo> DescribeInstances()
    {
        var result = new List<ScriptedInstanceInfo>(_directory.Count);

        foreach (var instance in _directory.Entities)
        {
            _byClassName.TryGetValue(instance.ClassName, out var reg);

            // Through the generated accessors, which is why nothing here
            // reflects: the descriptor table already carries a typed reader, an
            // IsSet and a stamp per key.
            var vars = instance.VarSets
                .SelectMany(set => set.Descriptors.Select(d => new ScriptedVarInfo(
                    d.Name, d.TypeName, d.Read(set), d.IsSet(set), d.LastSetAt(set))))
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var outputs = reg is null
                ? Array.Empty<ScriptedOutputInfo>()
                : reg.OutputFields.Select(f => f.GetValue(instance) as Output)
                    .Where(o => o is not null)
                    .Select(o => new ScriptedOutputInfo(o!.Name, o.FireCount, o.LastValue))
                    .ToArray();

            // A handle whose entity has been removed still sits in the directory until
            // the level ends. Saying so is more useful than dropping it: an
            // entity that vanished when it should not have is the thing being
            // looked for.
            bool valid = EntityArea.IsValid(instance.Handle);

            result.Add(new ScriptedInstanceInfo(
                instance.ClassName,
                instance.Handle.Value,
                valid ? EntityArea.GetIndex(instance.Handle) : -1,
                valid,
                valid ? EntityArea.GetOrigin(instance.Handle) : Vector.Zero,
                valid ? EntityArea.GetHealth(instance.Handle) : 0,
                valid ? instance.SpawnFlags : 0,
                vars,
                outputs));
        }

        return result
            .OrderBy(i => i.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Index)
            .ToArray();
    }

    public void Clear()
    {
        _directory.Clear();

        // The wait list goes with the world it was matched against. A held
        // record whose world has been cleared can never place, and keeping it
        // would let one save's leftovers reach the NEXT map's client-active.
        _deferred.Clear();

        // Re-read next time it is wanted. A level change is the one moment
        // somebody plausibly turned developer on to watch the next map load.
        _developer = -1;
    }

    private ScriptedEntity? Find(EntityHandle handle)
        => _directory.Resolve(handle);

    /// <summary>
    /// ONE LOOKUP PER CROSSING (plan_entity.md §3.4).
    ///
    /// <para>
    /// The thirteen entry points above each used to open with a
    /// <see cref="Find"/> of their own. They take an <see cref="EntityRef"/>
    /// now, and this is the one place that builds one: the dispatch thunk
    /// resolves the handle once and every op in that crossing reads the answer.
    /// </para>
    ///
    /// <para>
    /// Still a dictionary. §3.4 wants an array indexed by handle entry, which
    /// is <c>EntityDirectory</c> and is step 1g's; the point of resolving HERE
    /// rather than at each call site is that 1g changes one line and no call
    /// site at all.
    /// </para>
    /// </summary>
    public EntityRef Resolve(EntityHandle handle)
        => new(handle, Find(handle), Span<byte>.Empty);

    /// <summary>
    /// The same, carrying the entity's blob — which the native caller already
    /// has in hand, so handing it over costs nothing and saves the managed side
    /// a round trip to fetch what it was already holding.
    /// </summary>
    public EntityRef Resolve(EntityHandle handle, Span<byte> blob)
        => new(handle, Find(handle), blob);

    //-------------------------------------------------------------------------
    // Hot reload.
    //
    // The entity survives; the object driving it does not. These are the two
    // halves that make that a code swap rather than a lobotomy -- without them a
    // reloaded server keeps every entity in the world, keeps every one of them
    // replicating, and quietly stops all of them thinking, taking inputs and
    // firing outputs, with nothing about any of them looking wrong.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every live entity's state, in a form that outlives this assembly.
    ///
    /// <para>
    /// One entity's failure is not the reload's: a class whose SaveState throws
    /// is reported and skipped, and the other four hundred still come across.
    /// The alternative — one exception ending the walk — would lose everything
    /// after whichever entity happened to be first in the dictionary, which is
    /// an order nobody controls.
    /// </para>
    /// </summary>
    /// <summary>
    /// What the last <see cref="SaveForReload"/> carried, and what the last
    /// <see cref="RestoreAfterReload"/> put back.
    ///
    /// <para>
    /// KEPT SO THE REPORT DOES NOT HAVE TO RE-WALK, and so the two halves of it
    /// are measured by ONE method (<c>ReloadStore.Measure</c>). Two loops
    /// counting the same thing disagree the first time one of them is edited,
    /// and "carried 472, restored 471" would then mean nothing.
    /// </para>
    ///
    /// <para>
    /// They hold records, which hold only strings — see <see cref="SavedEntity"/>
    /// — so keeping them across the unload pins nothing. They are cleared on the
    /// next save.
    /// </para>
    /// </summary>
    public IReadOnlyList<SavedEntity> LastCollected { get; private set; } = Array.Empty<SavedEntity>();

    /// <inheritdoc cref="LastCollected"/>
    ///
    /// <para>
    /// <b>THE RECORDS THAT WERE REBUILT, NOT THE RECORDS THAT WERE OFFERED</b>,
    /// and the distinction is the whole value of the number. This used to be
    /// assigned the list <see cref="ReloadCarrier.Take"/> handed over, one line
    /// before the walk that rebuilds them — so
    /// <c>status.reload.restored.entities</c> equalled
    /// <c>status.reload.saved.entities</c> BY CONSTRUCTION, and
    /// <c>tools/live-gate</c>'s "every carried entity was put back" assertion
    /// could not fail. It passed on a run whose own console said
    /// <c>481 entity(ies) saved, 480 restored</c>.
    /// </para>
    ///
    /// <para>
    /// It is the same fault the SAVE side already carries a comment about —
    /// "MEASURED FROM THE STORE, NOT FROM WHAT THE DRIVER HANDED OVER … it was
    /// counting the offer rather than the deposit" (<c>ServerModule</c>) — and
    /// the restore side never got the fix. <see cref="RestoreFrom"/> owns both
    /// this and <see cref="LastRestoreFailures"/> now, because it is the only
    /// place that knows which arm each record took.
    /// </para>
    public IReadOnlyList<SavedEntity> LastRestored { get; private set; } = Array.Empty<SavedEntity>();

    /// <summary>
    /// How many records the last <see cref="RestoreFrom"/> could NOT rebuild,
    /// and the class name of the first of them.
    ///
    /// <para>
    /// Reported as a number rather than only as a log line for the reason the
    /// tally itself exists: an entity standing in the world with no managed
    /// half stops thinking, stops taking inputs and stops firing outputs, and
    /// nothing about it looks wrong. A warning in a 480-line reload scrolls; a
    /// counter a gate can assert on does not.
    /// </para>
    ///
    /// <para>
    /// A record whose entity is GONE is not a failure and is not counted here —
    /// it is dropped, said so, and there is nothing left to rebuild. This counts
    /// only the records whose entity is still standing and whose managed half
    /// could not be put back on it.
    /// </para>
    /// </summary>
    public int LastRestoreFailures { get; private set; }

    /// <inheritdoc cref="LastRestoreFailures"/>
    public string? LastRestoreFirstFailure { get; private set; }

    /// <inheritdoc cref="LastCollected"/>
    public SavedIo? LastSavedIo { get; private set; }

    /// <param name="store">The side's reload store (statics to zero): its state's, handed in by the module.</param>
    public int SaveForReload(ReloadStore store)
    {
        List<SavedEntity> states = Collect();

        LastCollected = states;

        ReloadCarrier.Put(store, states);

        // AND THE SHELL-LESS ENTITIES THEMSELVES -- step 2e, and it is not the
        // same kind of record as anything else here.
        //
        // Every other record is an entity's STATE, put back onto an entity the
        // native world kept standing across the unload. A shell-less entity has
        // no native half: the unload does not leave it holding stale values, it
        // DELETES it, and RestoreFrom then drops its state with "this handle
        // names nothing" -- correctly, and for the wrong reason.
        //
        // Measured before this line existed: the live gate's hot-reload step
        // read "395 entities, was 476". 81 lost, which is exactly dm_lockdown's
        // shell-less count.
        ReloadCarrier.PutShellLess(store, ShellLessSnapshot());

        // THE I/O HALF, WHICH IS NOT PER ENTITY AND SO CANNOT RIDE ONE (step
        // 2a). A queued event names a target that may not exist yet, an
        // activator that may be a player, and a caller that may be about to be
        // removed; it belongs to the directory, not to any one row. Saved with
        // each delay measured as SECONDS REMAINING, because the reload does not
        // stop the clock.
        //
        // This is what makes a delayed output survive a reload, which
        // plan_entity.md names as step 2a's gate. Before it the queue was
        // g_EventQueue -- native memory holding EHANDLEs -- and a two-second
        // output posted before a reload was delivered afterwards by code that
        // no longer existed.
        LastSavedIo = _directory.SaveIo(Now);

        ReloadCarrier.PutIo(store, LastSavedIo);

        return states.Count;
    }

    /// <summary>
    /// The same walk, answering the records instead of putting them anywhere.
    ///
    /// <para>
    /// SPLIT OUT FOR SAVE/RESTORE (docs/managed-saverestore.md), and split
    /// rather than copied on purpose. What this loop does that a second copy
    /// would get wrong is not the obvious part: it is the two failure arms —
    /// an override that did not chain to <c>base.SaveState</c>, and one that
    /// threw. Both are silent in the running game and both are handled here,
    /// once.
    /// </para>
    ///
    /// <para>
    /// A SAVE MUST NOT TOUCH THE RELOAD STORE, which is the reason this is not
    /// just <see cref="SaveForReload"/> with the result read back. The store is
    /// a handover between two halves of one reload; a save writing into it
    /// would leave records there for a reload that is not coming, and
    /// <c>ReloadStore.Take</c> would hand them to the next one.
    /// </para>
    /// </summary>
    public List<SavedEntity> Collect()
    {
        var states = new List<SavedEntity>(_directory.Count);

        // A2. See the report below the walk.
        int skipped = 0;
        string? firstSkipped = null;

        foreach ((EntityHandle handle, ScriptedEntity instance) in _directory.Bindings)
        {
            // THE INDEX IS TAKEN HERE AND NOWHERE ELSE, because here is the
            // only place the entity is still standing. It is what a SAVE
            // re-identifies by; a reload matches on the handle and never reads
            // it. Engine.EntityGetIndex answers -1 with no engine, which is
            // the test harness's case and is exactly what RestoreFromSave
            // treats as "this record cannot be placed".
            var state = new SavedEntity(handle.Value, instance.ClassName, instance.Name,
                                        EntityArea.GetIndex(handle));

            try
            {
                instance.SaveState(state);

                // An override that did not chain to base would have dropped
                // every map key, every networked field and every output on this
                // class -- and the entity would come back looking perfectly
                // ordinary with the class's declared defaults in it. Named, and
                // then done for it.
                if (!state.DefaultRan)
                {
                    Log.Warning($"[SourceSharp] {instance.GetType().Name}.SaveState did not " +
                                   $"call base.SaveState; its declared keys, networked fields " +
                                   $"and outputs were saved anyway\n");

                    instance.SaveDeclaredState(state);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SourceSharp] {instance.ClassName} '{instance.Name}': " +
                               $"SaveState threw ({e.Message}); this entity's state is not " +
                               $"carried across the reload\n");

                skipped++;
                firstSkipped ??= instance.ClassName;
                continue;
            }

            states.Add(state);
        }

        // A2: WHAT WAS DROPPED IS SAID ONCE MORE, AS A TOTAL.
        //
        // The per-entity warning above already exists and is not enough, which
        // is the review finding this answers. In a 473-entity walk those lines
        // scroll, and at the end of it a save that quietly omitted two entities
        // and one that carried all of them print exactly the same nothing --
        // and from outside, a .sav missing two entities and a .sav that worked
        // are indistinguishable until the world comes back wrong. That is the
        // failure class this lane has been bitten by twice.
        //
        // A COUNT AND A NAME, NOT A REFUSAL. Refusing the whole collect would
        // reverse the decision this method's own header argues for and is right
        // about: one class's bug must not cost the player the other 472
        // entities, nor the ability to save at all -- and this walk serves the
        // hot reload too, where a refusal would throw away every entity in the
        // world because one of them threw. So it is reported and not fatal.
        //
        // BOTH NUMBERS AND THE FIRST NAME. "1 of 2" says how bad; the classname
        // says where to look, because by the time anybody reads a count the
        // per-entity line naming the culprit has scrolled away.
        if (skipped > 0)
        {
            Log.Warning($"[SourceSharp] collect: {states.Count} of {_directory.Count} entities " +
                           $"carried; {skipped} SKIPPED because SaveState threw, first " +
                           $"'{firstSkipped}'. A block that silently omits an entity restores " +
                           "a world missing it, and from outside that looks like a save that " +
                           "worked\n");
        }

        return states;
    }

    /// <summary>
    /// Recreates the managed half of every entity that had one, and puts its
    /// state back.
    ///
    /// <para>
    /// NOT THROUGH SPAWN. The entity already spawned, in a world that is still
    /// standing; running <c>OnSpawn</c> again would re-precache, re-emit and
    /// re-announce everything it did the first time. What runs is the same
    /// construction the registry does on a map's first keyvalue, and then the
    /// restore.
    /// </para>
    ///
    /// <para>
    /// AN ENTITY THAT DIED IN THE MEANTIME cannot happen today — nothing runs
    /// between the save and the restore but the unload and the load — and is
    /// still checked, because "cannot happen" is what the entity list said about
    /// half the things in docs/divergences.md.
    /// </para>
    /// </summary>
    /// <param name="store">The side's reload store, the one <see cref="SaveForReload"/> wrote.</param>
    public int RestoreAfterReload(ReloadStore store)
    {
        // THE I/O HALF FIRST, and the order is the point rather than a
        // preference: RestoreFrom recreates the managed instances, and
        // recreating one BINDS it, and a bind evicting a stale entry cancels
        // that handle's queued events (EntityDirectory.Bind). Restoring the
        // queue afterwards would be restoring it into a directory that has just
        // finished throwing events away; restoring it first puts every event
        // back under the handle its entity is about to reappear at.
        // AND THE SHELL-LESS ENTITIES BEFORE EITHER -- step 2e, and the order
        // is load-bearing twice over. RestoreFrom asks Engine.EntityIsValid of
        // every handle and drops the record of one that answers no; a
        // shell-less handle answers no until its state is back, because the
        // route is what makes it valid at all. And RestoreIo puts events and
        // connections back under handles whose entities have to exist for a
        // find-by-name to reach them.
        int existing = RestoreShellLess(ReloadCarrier.TakeShellLess(store));

        var (events, connections) = _directory.RestoreIo(ReloadCarrier.TakeIo(store),
                                                         Now);

        // LastRestored IS NOT ASSIGNED HERE ANY MORE, and that line is the one
        // the live gate's hot-reload step was reading. It used to be
        // `LastRestored = rows` on the line above the walk, so the restored
        // tally was the SAVED tally under another name and
        // "every carried entity was put back" was an equality between a value
        // and itself. RestoreFrom owns it now, because it is the only code that
        // knows which records came back.
        var rows = ReloadCarrier.Take(store);

        int restored = RestoreFrom(rows);

        // INTO THE DEFAULT CONTEXT, where it can still be read after the next
        // unload -- and where a gate can assert on it. `saved - restored` alone
        // is not this number: a record whose entity DIED between the two is
        // dropped and that is the reload working. This is the other case.
        store.NoteRestoreFailures(LastRestoreFailures, LastRestoreFirstFailure);

        if (existing > 0)
            Log.Msg($"[SourceSharp] reload: {existing} shell-less entity(ies) carried across\n");

        if (events > 0 || connections > 0)
        {
            Log.Msg($"[SourceSharp] reload: {events} I/O event(s) and {connections} " +
                       $"connection(s) carried across\n");
        }

        return restored;
    }

    /// <summary>
    /// The same rebuild, from records the caller supplies.
    ///
    /// <para>
    /// Split out beside <see cref="Collect"/> and for the same reason: a
    /// restore from a <c>.sav</c> has to run the identical walk, including the
    /// gone-entity check and the two failure arms, and a second copy of it
    /// would drift on exactly those.
    /// </para>
    /// </summary>
    public int RestoreFrom(IReadOnlyList<SavedEntity> saved)
    {
        int restored = 0;
        int failed = 0;
        string? firstFailed = null;

        // THE RECORDS THAT CAME BACK, built as the walk goes rather than taken
        // from the input. See LastRestored for what reading the input instead
        // cost: an equality assertion in the live gate between a number and
        // itself, green across a reload that dropped the player.
        var rebuilt = new List<SavedEntity>(saved.Count);

        LastRestored = rebuilt;
        LastRestoreFailures = 0;
        LastRestoreFirstFailure = null;

        foreach (var state in saved)
        {
            var handle = new EntityHandle(state.Handle);

            // WITH NO ENGINE AT ALL this check is skipped, which is the same
            // split NetState.Bind makes and for the same reason: "this handle
            // names nothing" is a real answer only when there is something to
            // ask, and the test harness drives a registry with no native side
            // under it. An engine that IS up and says the entity is gone is
            // taken at its word.
            if (HostArea.IsInitialized && !EntityArea.IsValid(handle))
            {
                Log.Warning($"[SourceSharp] reload: entity {state.Handle} " +
                               $"('{state.ClassName}') is gone; its state is dropped\n");
                continue;
            }

            // FALSE: a reload does NOT put the native half back, and that is
            // the same split SavedEntity.Index already makes. A reload keeps
            // the native world standing -- the entity never went anywhere, so
            // there is nothing to restore -- and the values in the record were
            // read before the unload, so writing them back could only be a
            // no-op or STALE. A barrel that rolled a foot while the assembly
            // swapped would be teleported back to where it was, by a mechanism
            // whose whole purpose is to be invisible.
            // ONE ENTITY'S FAILURE IS NOT THE RELOAD'S, which is the rule
            // Collect already applies on the SAVE side and which this side did
            // not apply at all.
            //
            // MEASURED, and it is why this arm exists: with a client connected,
            // tools/live-gate's hot-reload step read
            //
            //   EXCEPTION IN ServerReloadRestore
            //   BlobTooShortException: PlayerNetState needs 652 byte(s) and
            //   entity 2 ('player') offers 0
            //   server: managed reload complete -- 478 saved, -1 restored
            //
            // -- ONE entity with a zero-length blob threw out of NetState.Bind,
            // the exception unwound the whole walk, and 477 other entities that
            // had nothing wrong with them were never restored. The world then
            // ran with an empty registry: nothing thinking, nothing taking an
            // input, the map's pickups placed a second time. The gate before
            // this lane asserted `entities >= before` and passed.
            //
            // NAMED AND SKIPPED, exactly as the save side names and skips a
            // class whose SaveState throws. A count and a first name at the
            // end, because in a 478-entity walk the per-entity lines scroll.
            try
            {
                if (Apply(handle, state, "reload", restoreNativeState: false))
                {
                    restored++;
                    rebuilt.Add(state);
                }
                else
                {
                    // APPLY SAID NO WITHOUT THROWING, which is a refusal it has
                    // already named — a classname that no longer matches the
                    // index, a class the container cannot build. It is still an
                    // entity standing in the world with no managed half, so it
                    // is counted with the throwers rather than passed over: the
                    // whole point of this number is that the two failure shapes
                    // are indistinguishable from outside.
                    failed++;
                    firstFailed ??= state.ClassName;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SourceSharp] reload: {state.ClassName} '{state.Name}' " +
                               $"({state.Handle}) could not be rebuilt ({e.Message}); its " +
                               $"state is dropped and the rest of the world is restored\n");

                failed++;
                firstFailed ??= state.ClassName;
            }
        }

        LastRestoreFailures = failed;
        LastRestoreFirstFailure = firstFailed;

        // SAID ONCE MORE AS A TOTAL, for Collect's reason: the per-entity lines
        // scroll, and from outside a reload that quietly dropped two entities
        // and one that carried them all print the same nothing.
        if (failed > 0)
        {
            Log.Warning($"[SourceSharp] reload: {restored} of {saved.Count} entit(ies) " +
                           $"restored; {failed} FAILED to rebuild, first '{firstFailed}'. " +
                           $"An entity that is standing in the world with no managed half " +
                           $"stops thinking, stops taking inputs and stops firing outputs, " +
                           $"and nothing about it looks wrong\n");
        }

        return restored;
    }

    /// <summary>
    /// The restore a SAVE needs, and it is a different question from the one
    /// <see cref="RestoreFrom"/> answers.
    ///
    /// <para>
    /// <b>A HANDLE OUT OF A <c>.sav</c> NAMES NOTHING.</b> A reload keeps the
    /// native world standing, so every handle in its records still points at
    /// the entity it was taken from — the design is right for what it was built
    /// for. A load tears the world down and builds it again, and the serial
    /// half of every handle advances: measured on <c>dm_lockdown</c>,
    /// <b>0 of 470</b> handles survived a save/load and every one of the 470
    /// indices did. So this walk re-identifies by <see cref="SavedEntity.Index"/>
    /// — the spawn ordinal, which a map respawned from the same lump reproduces
    /// — and never by the handle.
    /// </para>
    ///
    /// <para>
    /// <b>AND THE INDEX IS NEVER TRUSTED ALONE.</b> An index that now holds a
    /// different class is a record that must not be replayed: the keys the two
    /// share by name would be applied and the rest dropped, which loads without
    /// an error and gives back a subtly wrong world.
    /// <see cref="EnsureInstance"/> refuses that, and a record whose index
    /// names nothing is dropped and counted rather than passed over in silence.
    /// </para>
    ///
    /// <para>
    /// THE INDEX MAP IS BUILT FROM THE REGISTRY rather than asked of the
    /// engine, because the registry already holds every handle that could
    /// possibly match — <see cref="Collect"/> walks the same dictionary — and
    /// an index-to-handle lookup would be a new ABI slot for an answer already
    /// in this process.
    /// </para>
    /// </summary>
    /// <returns>How many records were put back.</returns>
    public int RestoreFromSave(IReadOnlyList<SavedEntity> saved)
        => RestoreFromSave(saved, LiveWorld());

    /// <summary>
    /// One live entity, as the two keys a restore matches on.
    ///
    /// <para>
    /// A RECORD OF WHAT THE ENGINE ANSWERED, and it exists because the engine
    /// is the only thing that can answer either one: an index comes from
    /// <c>entindex()</c> and a name from the entity's <c>targetname</c>. That
    /// is also why the matching takes this list rather than reading it — a
    /// registry can be driven with no native side under it, and the matching is
    /// the half worth testing.
    /// </para>
    /// </summary>
    public readonly record struct LiveEntity(EntityHandle Handle, int Index, string Name);

    /// <summary>
    /// Every live entity with the two keys the engine can answer for it. The
    /// index is -1 for a server-only entity, which holds no edict and therefore
    /// has no index at all — 81 of dm_lockdown's 473, measured.
    /// </summary>
    public List<LiveEntity> LiveWorld()
    {
        var world = new List<LiveEntity>(_directory.Count);

        foreach ((EntityHandle handle, ScriptedEntity instance) in _directory.Bindings)
            world.Add(new LiveEntity(handle, EntityArea.GetIndex(handle), instance.Name));

        return world;
    }

    /// <inheritdoc cref="RestoreFromSave(IReadOnlyList{SavedEntity})"/>
    public int RestoreFromSave(IReadOnlyList<SavedEntity> saved,
                               IReadOnlyList<LiveEntity> world)
    {
        // A NEW SAVE REPLACES THE OLD WAIT LIST. Anything still held from a
        // previous load belongs to a world that is gone.
        _deferred.Clear();

        int restored = Place(saved, world, "restore", _deferred);

        if (_deferred.Count > 0)
        {
            // COUNTED AND SAID, because the alternative is a smaller positive
            // number that no caller compares against what was saved -- a
            // partial restore reported as a success.
            //
            // AND IT IS NOT "DROPPED" ANY MORE. Two whole groups of entity
            // cannot exist at this instant (see _deferred): the ones the
            // managed level init creates in ServerActivate, and the player
            // with its kit. Calling that "dropped" here was true of the code
            // and false of the situation.
            Log.Warning($"[SourceSharp] restore: {_deferred.Count} of {saved.Count} saved " +
                           "entities matched no entity in this world by index or by " +
                           "name; they are HELD for the deferred pass at client-active\n");
        }

        return restored;
    }

    /// <summary>
    /// The second half of a savegame restore: the records the first pass could
    /// not place, matched again against the world as it stands NOW.
    ///
    /// <para>
    /// <b>THIS IS WHAT PUTS THE PLAYER BACK.</b> The engine does not call
    /// <c>ClientPutInServer</c> on a savegame load: it calls it only on a
    /// fresh spawn, and calls <c>ClientActive</c> either way, with
    /// <c>bLoadGame</c> true on a load. Stock can afford that because ITS
    /// <c>Restore</c> creates the player out of the save's entity table
    /// (<c>CBasePlayer::CreatePlayer</c> in
    /// <c>CEntitySaveRestoreBlockHandler::Restore</c>). This port writes no
    /// entity table, so the player is created at client-active instead — and
    /// by then the first pass is long over and its record is in
    /// <c>_deferred</c>.
    /// </para>
    ///
    /// <para>
    /// <b>THE LIST IS EMPTIED WHETHER OR NOT IT PLACED.</b> A record still
    /// homeless after this pass names nothing that is ever going to exist in
    /// this world, and holding it for the NEXT client to activate would replay
    /// one save's records over a world that has moved on.
    /// </para>
    /// </summary>
    /// <returns>How many of the held records were put back.</returns>
    public int ApplyDeferredRestore() => ApplyDeferredRestore(LiveWorld());

    /// <inheritdoc cref="ApplyDeferredRestore()"/>
    public int ApplyDeferredRestore(IReadOnlyList<LiveEntity> world)
    {
        if (_deferred.Count == 0)
            return 0;

        // SNAPSHOT AND CLEAR BEFORE THE WALK, not after: Apply runs managed
        // LoadState, which can create entities and can throw, and a list being
        // walked while a callee could reach it is the shape that produces the
        // bug nobody reproduces.
        var pending = _deferred.ToArray();
        _deferred.Clear();

        var homeless = new List<SavedEntity>();
        int restored = Place(pending, world, "deferred restore", homeless);

        if (homeless.Count > 0)
        {
            Log.Warning($"[SourceSharp] deferred restore: {homeless.Count} of " +
                           $"{pending.Length} held records still matched no entity; " +
                           "their state is dropped for good\n");
        }

        Log.Msg($"[SourceSharp] deferred restore: {restored} of {pending.Length} " +
                   "held records placed\n");

        return restored;
    }

    /// <summary>
    /// The match, and it is ONE body for both passes on purpose.
    ///
    /// <para>
    /// The deferred pass differs from the first in nothing but WHEN it runs and
    /// what it does with the leftovers. A second copy of this walk would drift
    /// on the two keys and on the ambiguity rule, which are the parts that were
    /// measured rather than chosen.
    /// </para>
    /// </summary>
    /// <param name="unplaced">Every record that matched nothing, appended.</param>
    /// <returns>How many records were applied.</returns>
    private int Place(IReadOnlyList<SavedEntity> saved,
                      IReadOnlyList<LiveEntity> world,
                      string operation,
                      List<SavedEntity> unplaced)
    {
        var byIndex = new Dictionary<int, EntityHandle>(world.Count);

        // NAMES ARE THE SECOND KEY AND THEY ARE NEEDED, which is a measurement
        // rather than belt-and-braces. 81 of dm_lockdown's 473 entities have
        // NO ENTITY INDEX -- they are server-only, hold no edict, and
        // SourceSharp_EntityGetIndex answers -1 for them by design
        // (shell_entityapi.cpp: "an index is the number the CLIENT knows an
        // entity by"). 38 env_soundscape, 9 logic_relay, 9
        // info_player_deathmatch and the rest. An index-only restore drops
        // every one of them.
        //
        // A NAME IS ONLY A KEY WHEN IT IS UNIQUE. A map may place ten entities
        // called the same thing, and a restore that picked whichever the
        // dictionary happened to hold would be the same silent mis-application
        // the classname guard exists to stop -- so a repeated name is struck
        // out rather than resolved arbitrarily.
        var byName = new Dictionary<string, EntityHandle>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (LiveEntity live in world)
        {
            if (live.Index >= 0)
                byIndex[live.Index] = live.Handle;

            if (string.IsNullOrEmpty(live.Name)
                || _directory.Resolve(live.Handle) is not { } instance)
                continue;

            string key = instance.ClassName + "\u0000" + live.Name;

            if (!byName.TryAdd(key, live.Handle))
                ambiguous.Add(key);
        }

        foreach (string key in ambiguous)
            byName.Remove(key);

        int restored = 0;

        foreach (var state in saved)
        {
            EntityHandle handle;

            // THE INDEX FIRST, because it is the key that exists for every
            // entity the client can see and it is the one measured stable
            // across a rebuild. The name is the fallback for the entities that
            // have no index at all, NOT an alternative to it.
            if (state.Index >= 0 && byIndex.TryGetValue(state.Index, out handle))
            {
                // found
            }
            else if (!string.IsNullOrEmpty(state.Name)
                     && byName.TryGetValue(state.ClassName + "\u0000" + state.Name, out handle))
            {
                // found by targetname, which is unique among live entities of
                // this class or it would not be in the map
            }
            else
            {
                unplaced.Add(state);
                continue;
            }

            // TRUE on both restore passes. A load tore the world down and built
            // it again from the map lump, so every one of these fields is
            // holding the SPAWN's value and the saved one has to overwrite it.
            if (Apply(handle, state, operation, restoreNativeState: true))
                restored++;
        }

        return restored;
    }

    /// <summary>
    /// One record onto one entity — the part a reload and a restore share, and
    /// the part a second copy would get wrong. What differs between them is
    /// only how the entity was FOUND, which is why that is above this and this
    /// is one body.
    /// </summary>
    /// <param name="restoreNativeState">
    /// Whether to put the entity's NATIVE half back as well — true for a
    /// savegame restore, false for a hot reload. See the two call sites, which
    /// each say why.
    /// </param>
    private bool Apply(EntityHandle handle, SavedEntity state, string operation,
                       bool restoreNativeState)
    {
        // THE CLASSNAME IS COMPARED BEFORE THE INSTANCE IS TOUCHED, and this
        // is the one place in the registry where a classname arrives from
        // OUTSIDE the engine -- out of a .sav, or out of a reload store written
        // by an assembly that is gone. EnsureInstance below returns whatever
        // instance the handle names WITHOUT looking at the name, which is right
        // for the two engine-driven callers and wrong here.
        //
        // WHAT IT DEFENDS AGAINST, precisely. A record that lands on a
        // different entity has LoadDeclaredState replay its map keys onto it:
        // keys the other class does not declare are dropped, and any it shares
        // BY NAME -- health, speed, wait -- are applied. The count goes up, no
        // warning is printed, and a door ends up with a barrel's health. That
        // is the silent-corruption failure this whole lane is arranged against,
        // and it loads without an error.
        //
        // Refused rather than replaced: the instance standing here belongs to
        // the entity that is actually there, and destroying it to make the
        // record fit would be losing the live world to satisfy a file.
        if (_directory.Resolve(handle) is { } standing
            && !string.Equals(standing.ClassName, state.ClassName,
                              StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning($"[SourceSharp] {operation}: entity {handle.Value} is a " +
                           $"'{standing.ClassName}' and the record says " +
                           $"'{state.ClassName}'; the record is DROPPED rather than " +
                           "replayed onto the wrong entity\n");
            return false;
        }

        var instance = EnsureInstance(handle, state.ClassName);

        if (instance is null)
            return false;   // EnsureInstance has already said why

        // THE NAME GOES BACK INTO THE DIRECTORY'S INDEX (step 2a), and it is
        // not cosmetic. The index is fed by the targetname keyvalue, and a
        // restore does not replay a map's keys -- so without this line an
        // entity comes back with every value it had and NO NAME, and the first
        // thing that notices is a delayed output arriving at a target that no
        // longer answers to what the map called it. That is the reload half of
        // this step's own gate.
        //
        // Only when the record HAS one: an unnamed entity must not be given the
        // empty string as a name, which would put it in the index under "".
        if (state.Name.Length > 0)
            _directory.SetName(handle, state.Name);

        state.DefaultRan = false;

        // WHAT THIS ARRANGEMENT IS, BEFORE IT RUNS: the class's own LoadState,
        // and then the NATIVE OVERLAY, in that order and never the other one.
        //
        // A savegame load in this port is a `map` plus that overlay --
        // MapLoadSaveGame and MapLoadNewGame call the identical SpawnTheMap,
        // and the savegame arm differs by one extra LoadSaveGameState
        // afterwards. So the spawn has already set every native field from the
        // lump, and a class's LoadState may have set some of them again from
        // its own keys: the live gate's func_breakable_surf carries a `Health`
        // map key holding 5 while the shell held the damaged 4. Whichever runs
        // last wins, and the saved value is the one that has to. That is also
        // why the overlay is here rather than inside LoadDeclaredState, which
        // runs in the middle of the class's own restore.
        //
        // AND IT RUNS EVEN WHEN LoadState THREW, which is why the catch below
        // sets a flag instead of returning. An entity whose class blew up
        // halfway through its own restore is exactly the one that must not ALSO
        // come back standing somewhere it never was; the position is the half
        // that has nothing to do with the class that threw. The return value is
        // unchanged -- that record did not fully apply and is still not counted.
        bool applied = true;

        try
        {
            instance.LoadState(state);

            if (!state.DefaultRan)
            {
                Log.Warning($"[SourceSharp] {instance.GetType().Name}.LoadState did not " +
                               $"call base.LoadState; its declared keys, networked fields " +
                               $"and outputs were restored anyway\n");

                instance.LoadDeclaredState(state);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[SourceSharp] {operation}: {state.ClassName} '{state.Name}': " +
                           $"LoadState threw ({e.Message}); this entity is back but its " +
                           "state is not\n");

            applied = false;
        }

        // ...and the overlay last. See the note above the try.
        if (restoreNativeState)
            instance.LoadNativeState(state);

        return applied;
    }

    //=========================================================================
    // SHELL-LESS LOGICAL ENTITIES -- plan_entity.md step 2e, §3.3.
    //
    // "Entities the engine cannot see have no shell at all. A logic_relay, a
    //  filter, a math_counter, a point_template, a phys_constraint are managed
    //  objects that exist only in the directory."
    //
    // This region is the whole of what a class of kind Logical needs that a
    // class with a shell gets from native code: a handle, the eight keys
    // CStandaloneEntity::KeyValueShellOnly claimed, a Spawn, and an answer to
    // find-by-class and find-by-name. Everything else -- the instance, the
    // keyvalues, the outputs, the inputs, the think heap, the I/O queue, the
    // name index, the removal mark -- already worked on any handle and needed
    // no change at all, because 1c sized the directory for the whole 8,192-entry
    // range and 2a/2b/2c never asked whether an entry was an edict.
    //=========================================================================

    /// <summary>
    /// Whether a registered class creates no shell — <c>plan_entity.md</c> §3.3.
    ///
    /// <para>
    /// <b>The kind IS the decision and there is no second flag.</b>
    /// <see cref="ScriptedEntityKind.Logical"/> means "no model, no collision,
    /// not networked", which is exactly the set of reasons §3.3 gives for a
    /// shell existing at all: "a shell is created only for an entity that is
    /// networked, in the partition, or attached to a physics object". A class
    /// that needs any of those declares a different kind.
    /// </para>
    /// </summary>
    public bool ClassIsShellLess(string className)
        => _byClassName.TryGetValue(className, out var reg)
           && reg.Kind == ScriptedEntityKind.Logical;

    /// <inheritdoc/>
    public ShellLessEntity? StateOf(EntityHandle handle) => _directory.ShellLessAt(handle);

    /// <summary>
    /// Every shell-less entity's state, for the reload store — step 2e.
    /// </summary>
    /// <remarks>
    /// The STATE objects themselves and not copies of them: the reload carrier
    /// encodes them into bytes (<see cref="ReloadCarrier.PutShellLess"/>), so
    /// nothing here outlives the directory they came from.
    /// </remarks>
    public List<ShellLessEntity> ShellLessSnapshot()
    {
        var all = new List<ShellLessEntity>(_directory.ShellLessCount);

        foreach (var state in _directory.ShellLessEntities)
            all.Add(state);

        return all;
    }

    /// <summary>
    /// Puts every shell-less entity back, at the handle it had — step 2e.
    /// Answers how many.
    ///
    /// <para>
    /// <b>The same handle, and that is the whole point.</b> A reload keeps the
    /// native world standing, so every handle anything else is holding — a
    /// queued output's target, a parent link, a connection this side is about
    /// to restore — still names the entity it named before. Allocating a fresh
    /// entry would leave all of those pointing at nothing, which is exactly the
    /// shape the save/load path has to work around and the reload path never
    /// has to.
    /// </para>
    ///
    /// <para>
    /// <b>THE INSTANCE IS BUILT HERE AND NOT LEFT TO <see cref="RestoreFrom"/>,
    /// because the two records are not the same record.</b> This one says the
    /// entity EXISTS; the <see cref="SavedEntity"/> one says what its fields
    /// held. <see cref="Collect"/> deliberately SKIPS an entity whose
    /// <c>SaveState</c> threw — so that one class's bug does not cost the world
    /// the other 472 — and that entity has no state record at all. Leaving the
    /// instance to the state walk would bring it back as a row in the
    /// shell-less table with nothing bound to it: counted as live, resolving to
    /// null, unreachable by name and impossible to remove.
    /// </para>
    ///
    /// <para>
    /// <see cref="RestoreFrom"/> then finds the instance already standing and
    /// puts its state onto it, which is what <see cref="EnsureInstance"/> does
    /// for every entity that already has one.
    /// </para>
    /// </summary>
    public int RestoreShellLess(IReadOnlyList<ShellLessEntity> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);

        int back = 0;

        foreach (var state in saved)
        {
            if (!_directory.AttachShellLess(state.Handle, state))
                continue;

            if (EnsureInstance(state.Handle, state.ClassName) is null)
            {
                // The class is gone from the new assembly. Give the entry back
                // rather than leaving a row nothing is bound to -- the same arm
                // Create takes, for the same reason.
                _directory.DetachShellLess(state.Handle);
                continue;
            }

            // The name index, for the reason Create feeds it: an output
            // resolves its target against it. Apply would set it too, from the
            // STATE record -- and an entity Collect skipped has no state record,
            // which is exactly the entity this line is for.
            if (state.Name.Length > 0)
                _directory.SetName(state.Handle, state.Name);

            back++;
        }

        return back;
    }

    /// <inheritdoc/>
    public int LiveCount => _directory.ShellLessCount;

    /// <summary>
    /// How many REGISTERED CLASSES make no shell, which is a property of the
    /// build rather than of the map.
    ///
    /// <para>
    /// Reported beside the live count because the two answer different
    /// questions and a reader will otherwise conflate them: 132 classes declare
    /// kind <c>Logical</c> and <c>dm_lockdown</c> places 81 of those entities.
    /// A build that stopped registering them reads zero here; a MAP with none
    /// of them reads zero in <see cref="LiveCount"/> only.
    /// </para>
    /// </summary>
    public int ShellLessClassCount
    {
        get
        {
            int count = 0;

            foreach (var reg in _byClassName.Values)
            {
                if (reg.Kind == ScriptedEntityKind.Logical)
                    count++;
            }

            return count;
        }
    }

    /// <inheritdoc/>
    public (long Allocated, long Released) AllocationTotals
        => (_directory.ShellLessAllocated, _directory.ShellLessReleased);

    /// <summary>
    /// <b>2e's THIRD GATE CLAUSE, AS A NUMBER</b> — how many handles from the
    /// engine-invisible range have reached native code.
    ///
    /// <para>
    /// Counted natively, in <c>CServerEntityResolver::Resolve</c>, and only forwarded
    /// here: a managed counter could count nothing but the routes managed code
    /// already knows it takes, so it would read zero for exactly the case the
    /// number exists to catch. Read it beside <see cref="LiveCount"/>.
    /// </para>
    /// </summary>
    public int ShellLessNativeReach => _side.InvisibleRangeReach();

    /// <summary>
    /// Makes one shell-less entity: an entry out of the engine-invisible range,
    /// the shell's own keys applied in managed memory, and the class's instance.
    ///
    /// <para>
    /// <b>The order is the shell's, kept deliberately.</b> Native created the
    /// object, ran <c>KeyValueShellOnly</c> over the map's keys, and only then
    /// offered them to managed code — so an <c>OnKeyValue</c> has always been
    /// able to read <c>origin</c>, <c>spawnflags</c> and <c>targetname</c> off
    /// the entity it is being called about. Building the instance before the
    /// keys were applied would quietly change that for 132 classes at once.
    /// </para>
    ///
    /// <para>
    /// <b>An entry is given BACK when the class cannot be built.</b> That is not
    /// tidiness: the allocator would otherwise lose one entry per failure, for
    /// the life of the map, and the symptom would be the invisible range filling
    /// up on a long session with nothing in any log to say why.
    /// </para>
    /// </summary>
    public EntityHandle Create(string className, ReadOnlySpan<byte> packedKeys)
    {
        if (!ClassIsShellLess(className))
            return EntityHandle.None;

        var handle = _directory.AllocateInvisible();

        // None, which default(EntityHandle) also is: the allocator never hands
        // out entry zero (every entry it hands out is at least 2048), and this
        // compares against the sentinel it CAN produce.
        if (handle == EntityHandle.None)
            return EntityHandle.None;

        var state = new ShellLessEntity(handle, className);

        foreach (var (key, value) in UnpackKeys(packedKeys))
        {
            if (state.ApplyShellKey(key, value))
                ForwardShellKey(handle, state, key);
        }

        if (!_directory.AttachShellLess(handle, state))
            return EntityHandle.None;

        if (EnsureInstance(handle, className) is null)
        {
            _directory.DetachShellLess(handle);
            return EntityHandle.None;
        }

        return handle;
    }

    /// <inheritdoc/>
    public bool KeyValue(EntityHandle handle, string key, string value)
    {
        if (_directory.ShellLessAt(handle) is not { } state)
            return false;

        bool claimed = state.ApplyShellKey(key, value);

        if (claimed)
            ForwardShellKey(handle, state, key);

        // AND THE MANAGED ARM, UNCONDITIONALLY -- exactly as
        // CStandaloneEntity::KeyValue offers every key to managed code whether
        // or not the shell claimed it. Two statements and not `claimed || …`,
        // which would skip the offer for every key the shell arm takes; 2d made
        // the same call on the native side for the same reason.
        bool managed = KeyValue(handle, state.ClassName, key, value);

        return claimed || managed;
    }

    /// <summary>
    /// The two shell keys that are not just a value: <c>targetname</c> feeds the
    /// directory's name index and the two parenting keys feed the hierarchy.
    ///
    /// <para>
    /// <c>targetname</c> reaches the index through
    /// <c>ScriptedEntityRegistry.KeyValue</c> as well, for every entity in the
    /// map, and that is where it lands for one made by the map spawner. This
    /// covers the OTHER caller — a runtime <c>EntitySetKeyValue</c>, and the
    /// creation payload, neither of which goes through that path before the
    /// instance exists.
    /// </para>
    /// </summary>
    private void ForwardShellKey(EntityHandle handle, ShellLessEntity state, string key)
    {
        if (key.Equals("targetname", StringComparison.OrdinalIgnoreCase))
        {
            // THE NAME INDEX, which is what every output resolves its target
            // against and what !activator, a targetname lookup and the whole of
            // 2a's I/O half stand on. SetName is idempotent (it drops the old
            // index entry first), so the later per-key delivery to
            // ScriptedEntityRegistry.KeyValue -- which the map spawner makes for
            // every key -- sets the same name a second time and changes nothing.
            //
            // Doing it here as well is not belt and braces: an entity created
            // through MapSpawnCreate is NAMED by its payload, and between that
            // call and the spawner's own key walk it would otherwise be
            // unfindable. A template instantiating its sources goes through the
            // same call and never makes that walk at all.
            _directory.SetName(handle, state.Name);
            return;
        }

        if (key.Equals("parentname", StringComparison.OrdinalIgnoreCase)
            && state.ParentName is { } parent)
        {
            _shellLess.ParentNameSink?.Invoke(handle, parent);
        }
        else if (key.Equals("parentattachment", StringComparison.OrdinalIgnoreCase)
                 && state.ParentAttachmentName is { } attachment)
        {
            _shellLess.ParentAttachmentNameSink?.Invoke(handle, attachment);
        }
    }

    /// <summary>
    /// <c>CStandaloneEntity::Spawn</c>'s first two lines, and then the class's
    /// own <c>OnSpawn</c>.
    ///
    /// <para>
    /// The spawn origin is remembered BEFORE anything can move it, which is the
    /// shell's own comment: movement is described relative to where the mapper
    /// drew the entity, and by the time something asks, the current origin is no
    /// longer that.
    /// </para>
    ///
    /// <para>
    /// A second spawn does nothing, which is what the map spawner's own
    /// <c>HasSpawned</c> flag already guaranteed for a map entity — this is the
    /// same guard for one created at runtime and spawned twice.
    /// </para>
    /// </summary>
    public void Spawn(EntityHandle handle)
    {
        if (_directory.ShellLessAt(handle) is not { } state || state.HasSpawned)
            return;

        state.HasSpawned = true;
        state.SpawnOrigin = state.Origin;
        state.SpawnAngles = state.Angles;

        Spawn(handle, state.ClassName);
    }

    /// <inheritdoc/>
    public int FindByClassName(string query, List<EntityHandle> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (string.IsNullOrEmpty(query))
            return 0;

        int found = 0;

        foreach (var state in _directory.ShellLessEntities)
        {
            // EntityNameMatch, which is the wildcard rule
            // FindEntityByClassname uses -- so "*" answers everything and
            // "item_*" answers a family, on both halves of the list alike.
            if (!EntityNameMatch.Match(query, state.ClassName))
                continue;

            into.Add(state.Handle);
            found++;
        }

        return found;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public EntityHandle FindFirstNamed(string query)
        => _directory.FirstByName(query, IsShellLessEntity);

    /// <inheritdoc/>
    public int FindAllNamed(string query, List<EntityHandle> into, int limit)
        => _directory.AllByName(query, IsShellLessEntity, into, limit);

    /// <inheritdoc/>
    public NameMatches Named(string query, int limit)
    {
        EntityHandle[] buffer = System.Buffers.ArrayPool<EntityHandle>.Shared.Rent(16);
        int count = _directory.CollectByName(query, IsShellLessEntity, ref buffer, limit);

        return new NameMatches(buffer, count, IsAliveEntity);
    }

    // Allocated once per registry rather than per lookup: NameMatches exists to
    // allocate nothing, and a lambda here would be a closure per call.
    private Func<ScriptedEntity, bool>? _isShellLessEntity;
    private Func<EntityHandle, bool>? _isAliveEntity;

    private Func<ScriptedEntity, bool> IsShellLessEntity
        => _isShellLessEntity ??= entity => ClassIsShellLess(entity.ClassName);

    private Func<EntityHandle, bool> IsAliveEntity
        => _isAliveEntity ??= handle => _directory.Resolve(handle) is not null;

    public int FindByName(string query, List<EntityHandle> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (string.IsNullOrEmpty(query))
            return 0;

        int found = 0;

        foreach (var state in _directory.ShellLessEntities)
        {
            if (state.Name.Length == 0 || !EntityNameMatch.Match(query, state.Name))
                continue;

            into.Add(state.Handle);
            found++;
        }

        return found;
    }

    /// <summary>
    /// Walks a <c>key\0value\0key\0value\0…</c> payload — the map spawner's, and
    /// the same bytes <c>SourceSharp_MapSpawnCreate</c> walks on the native side.
    /// </summary>
    /// <remarks>
    /// Map text cannot hold a NUL — the entity lump is a C string in the BSP —
    /// so the separator cannot collide with a value. A trailing key with no
    /// value is dropped rather than paired with the end of the buffer.
    /// </remarks>
    internal static List<(string Key, string Value)> UnpackKeys(ReadOnlySpan<byte> packed)
    {
        var pairs = new List<(string, string)>();

        int at = 0;

        while (at < packed.Length)
        {
            int keyEnd = packed[at..].IndexOf((byte)0);
            if (keyEnd < 0)
                break;

            string key = System.Text.Encoding.UTF8.GetString(packed.Slice(at, keyEnd));
            at += keyEnd + 1;

            if (at > packed.Length)
                break;

            int valueEnd = packed[at..].IndexOf((byte)0);
            if (valueEnd < 0)
                break;

            string value = System.Text.Encoding.UTF8.GetString(packed.Slice(at, valueEnd));
            at += valueEnd + 1;

            pairs.Add((key, value));
        }

        return pairs;
    }

}
