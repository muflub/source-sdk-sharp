namespace SourceSharp.Abi;

/// <summary>
/// One entity's state, in a form that outlives the assembly that produced it.
///
/// <para>
/// THE ONE RULE, AND EVERY DECISION HERE FOLLOWS FROM IT: nothing in this class
/// may be an object from the side being unloaded. This type lives in the ABI,
/// which lives in the DEFAULT load context, and a strong reference from here to
/// one object of a side keeps that side's collectible context alive forever —
/// the leak <see cref="ManagedCaches"/> exists to prevent, whose symptom is not
/// an error but a reload that silently keeps running the old code.
/// </para>
///
/// <para>
/// So a value is a <see cref="string"/> and a key is a <see cref="string"/>, and
/// the writing API takes nothing else. It is not a serialisation format anybody
/// chose for elegance; it is the shape that cannot pin a context. The guard is
/// <c>ReloadTests.BindingAStateDoesNotKeepItsSideAlive</c> and
/// <c>UnloadTests</c>, which fail the moment something here holds a
/// <see cref="Type"/> or an entity.
/// </para>
///
/// <para>
/// FOUR STORES RATHER THAN ONE, because they are restored by four different
/// mechanisms and two of them carry more than a value. Map keys carry the stamp
/// that says WHEN the map wrote them; outputs carry a fire count that is not a
/// value at all. Namespacing them into one dictionary would have made a local
/// key called <c>var:speed</c> silently collide with a map key called
/// <c>speed</c>.
/// </para>
/// </summary>
public sealed class SavedEntity
{
    /// <summary>The entity this belongs to, as a bare handle value.</summary>
    public uint Handle { get; }

    /// <summary>The class name the map used, which is what recreates the instance.</summary>
    public string ClassName { get; }

    /// <summary>The mapper's name for it, kept only so a message can say which entity.</summary>
    public string Name { get; }

    /// <summary>
    /// The entity's INDEX at the moment it was collected, or -1 when there was
    /// no engine to ask.
    ///
    /// <para>
    /// <b>THIS IS THE RE-IDENTIFICATION KEY, AND <see cref="Handle"/> IS NOT.</b>
    /// A handle is an entry index packed with a SERIAL number, and a save/load
    /// tears the world down and builds it again — so every serial advances and
    /// not one handle out of a <c>.sav</c> names anything. Measured, on
    /// dm_lockdown: <b>0 of 470</b> handles survived a load, every serial
    /// exactly one higher, while the index of all 470 was unchanged and named
    /// the same classname. A restore that matched on the handle would therefore
    /// either drop every record (the serial fails validation) or — the worse
    /// half — resolve to whatever now occupies the slot and replay one entity's
    /// map keys onto another.
    /// </para>
    ///
    /// <para>
    /// The index is stable because it IS the spawn ordinal: a map respawned
    /// from the same lump creates the same entities in the same order. It is
    /// never trusted alone — <c>RestoreFromSave</c> requires
    /// <see cref="ClassName"/> to match as well, and refuses out loud when it
    /// does not.
    /// </para>
    ///
    /// <para>
    /// A RELOAD DOES NOT USE THIS and must not: a reload keeps the native world
    /// standing, so the handle is exactly right there and the index would be a
    /// weaker key for no reason.
    /// </para>
    /// </summary>
    public int Index { get; }

    public SavedEntity(uint handle, string className, string name, int index = -1)
    {
        Handle    = handle;
        ClassName = className;
        Name      = name;
        Index     = index;
    }

    //-------------------------------------------------------------------------
    // The NATIVE half of the entity.
    //-------------------------------------------------------------------------

    private readonly Dictionary<string, string> _native = new(StringComparer.Ordinal);

    /// <summary>
    /// What the SHELL held, by field name — the half of an entity that lives in
    /// C++ and that nothing in this record used to carry.
    ///
    /// <para>
    /// <b>WHY THIS EXISTS, AND IT IS NOT "THE ORIGIN WAS MISSING".</b> The
    /// native half of an entity was never written to a <c>.sav</c> at all.
    /// <c>ShellSave_WriteFields</c>/<c>ReadFields</c> are real, but they are
    /// entered only from the engine's own two vtable slots with the ENGINE's
    /// structs; nothing calls them for an entity.
    /// <c>CShellGameDll::Save</c> makes one call — <c>WriteManagedEntities</c>,
    /// which writes this record and nothing else — <c>WriteSaveHeaders</c>
    /// writes nothing, and the file carries no <c>ETABLE</c> block. Two
    /// independent measurements agree from opposite directions:
    /// <c>tools/save-live-gate</c> damaged a <c>func_breakable_surf</c> and the
    /// damage did not survive, and stage 3 could not build a player-health
    /// differential because the health it could reach was the map key rather
    /// than the native one. In the live gate's own save all thirteen
    /// <c>func_breakable_surf</c> records carry <c>Health 5</c> — the map's
    /// value, which the damage never touched — while the damage went into
    /// <c>CStandaloneEntity::m_iHealth</c>, which is nowhere in the file.
    /// </para>
    ///
    /// <para>
    /// <b>A DICTIONARY RATHER THAN TYPED FIELDS, AND THAT IS THE LOAD-BEARING
    /// DECISION.</b> A named origin and a named angle pair would have been a
    /// layout, and every native field added afterwards — health, velocity,
    /// solidity, spawnflags, a model — would have been another layout and
    /// another version number. This format's version numbers are scarce enough
    /// that 2 had to be burned outright. A count-prefixed store of name/value
    /// pairs makes a NEW NATIVE FIELD A NEW KEY rather than a new layout: it
    /// costs no bump, an older reader sees a key it does not apply rather than
    /// bytes it misparses, and the version stamp goes on meaning what it says.
    /// The field list below is therefore not the format's promise — the STORE
    /// is.
    /// </para>
    ///
    /// <para>
    /// <b>AND IT IS REPLAYED UNCONDITIONALLY, WHICH IS A DIFFERENT RULE FROM
    /// THE ONE BESIDE IT.</b> <see cref="Vars"/> carries only keys the map
    /// actually WROTE, because a key left at its declared initialiser is
    /// restored by the new instance's own initialiser and replaying it would
    /// make the entity report a key as set that nobody set. A native field has
    /// no such notion: the shell has no "was this written" bit, and every one
    /// of these fields is given a value by the map lump on every spawn whether
    /// the mapper mentioned it or not. So there is nothing to compare against
    /// and nothing to leave alone — the saved value is the value, and it is put
    /// back. Written down here because it is the opposite of the rule two
    /// stores away, and an inference that reads as an oversight is one somebody
    /// will "fix".
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> Native => _native;

    /// <summary>Records one native field, as text.</summary>
    public void SaveNative(string field, string value) => _native[field] = value ?? string.Empty;

    /// <inheritdoc cref="SaveNative(string, string)"/>
    public void SaveNative(string field, int value) => _native[field] = value.ToString(Culture);

    /// <inheritdoc cref="SaveNative(string, string)"/>
    public void SaveNative(string field, float value) => _native[field] = value.ToString(Culture);

    /// <inheritdoc cref="SaveNative(string, string)"/>
    public void SaveNative(string field, Vector value) => _native[field] = ScriptedVarParse.Save(value);

    /// <summary>Whether the record carries this native field at all.</summary>
    public bool HasNative(string field) => _native.ContainsKey(field);

    /// <summary>
    /// What was carried, or <paramref name="fallback"/>.
    ///
    /// <para>
    /// The same fallback-rather-than-throw rule as <see cref="GetInt"/>, and
    /// here it covers one more case: a record written before this build knew
    /// about a given native field carries no key for it, and the entity must
    /// come up holding whatever the spawn gave it rather than a zero.
    /// <see cref="HasNative"/> is what a caller uses to tell the two apart, and
    /// every restore below does.
    /// </para>
    /// </summary>
    public int NativeInt(string field, int fallback = 0)
        => _native.TryGetValue(field, out var v) && ScriptedVarParse.Int(v, out int r) ? r : fallback;

    /// <inheritdoc cref="NativeInt"/>
    public float NativeFloat(string field, float fallback = 0f)
        => _native.TryGetValue(field, out var v) && ScriptedVarParse.Float(v, out float r) ? r : fallback;

    /// <inheritdoc cref="NativeInt"/>
    public Vector NativeVector(string field, Vector fallback = default)
        => _native.TryGetValue(field, out var v) && ScriptedVarParse.Vector(v, out var r) ? r : fallback;

    //-------------------------------------------------------------------------
    // Map keys.
    //-------------------------------------------------------------------------

    /// <summary>A declared map key: what it holds, and when it was written.</summary>
    public readonly record struct SavedVar(string Value, float LastSetAt);

    private readonly Dictionary<string, SavedVar> _vars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every map key that was carried across, by the name the map uses.</summary>
    public IReadOnlyDictionary<string, SavedVar> Vars => _vars;

    /// <summary>
    /// Records a map key. Only keys the map actually WROTE are worth carrying:
    /// one left at its declared initialiser is restored by the new instance's
    /// own initialiser, and replaying it would make the new entity report a key
    /// as set that nobody ever set.
    /// </summary>
    public void SaveVar(string key, string value, float lastSetAt)
        => _vars[key] = new SavedVar(value, lastSetAt);

    //-------------------------------------------------------------------------
    // Networked state, by field name.
    //-------------------------------------------------------------------------

    private readonly Dictionary<string, string> _net = new(StringComparer.Ordinal);

    /// <summary>Every networked field that was carried across, by declared name.</summary>
    public IReadOnlyDictionary<string, string> Net => _net;

    /// <summary>
    /// Records one networked field.
    ///
    /// <para>
    /// WORTH DOING EVEN THOUGH THE BLOB SURVIVES, which is the part that reads
    /// like duplication and is not. The blob is native memory and a reload does
    /// not touch it — but it is only meaningful through the LAYOUT it was
    /// written with, so the moment a class is reshaped its bytes stop meaning
    /// anything and the reload zeroes them rather than let them be misread. By
    /// name, a field that still exists gets its value back even though it moved,
    /// which is strictly better than zero.
    /// </para>
    /// </summary>
    public void SaveNet(string field, string value) => _net[field] = value;

    //-------------------------------------------------------------------------
    // Outputs.
    //-------------------------------------------------------------------------

    /// <summary>What an output has done, which is not what it is wired to.</summary>
    public readonly record struct SavedOutput(int FireCount, string? LastValue);

    private readonly Dictionary<string, SavedOutput> _outputs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every output's runtime record, by mapper-visible name.</summary>
    public IReadOnlyDictionary<string, SavedOutput> Outputs => _outputs;

    /// <summary>
    /// Records what an output has done so far.
    ///
    /// <para>
    /// The WIRING is not here and must not be: an output's connections come from
    /// the map and are held natively, so they survive a managed reload without
    /// anybody carrying them. What dies with the managed object is the tally —
    /// how many times it has fired and what it last carried — which is what
    /// answers "did that connection ever run?" and is the only reason this is
    /// saved at all.
    /// </para>
    /// </summary>
    public void SaveOutput(string name, int fireCount, string? lastValue)
        => _outputs[name] = new SavedOutput(fireCount, lastValue);

    //-------------------------------------------------------------------------
    // Whatever the entity itself says it needs. The only half a default cannot
    // discover, and the reason SaveState is virtual.
    //-------------------------------------------------------------------------

    private readonly Dictionary<string, string> _local = new(StringComparer.Ordinal);

    /// <summary>Everything an entity class wrote by hand, by its own key.</summary>
    public IReadOnlyDictionary<string, string> Local => _local;

    private static readonly System.Globalization.CultureInfo Culture
        = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>Carries a string across the reload.</summary>
    public void Set(string key, string? value) => _local[key] = value ?? string.Empty;

    /// <summary>Carries a number across the reload.</summary>
    public void Set(string key, int value) => _local[key] = value.ToString(Culture);

    /// <summary>Carries a number across the reload.</summary>
    public void Set(string key, float value) => _local[key] = value.ToString(Culture);

    /// <summary>Carries a flag across the reload, spelled as a map spells one.</summary>
    public void Set(string key, bool value) => _local[key] = value ? "1" : "0";

    /// <summary>Carries a vector across the reload, spelled as a map spells one.</summary>
    public void Set(string key, Vector value) => _local[key] = ScriptedVarParse.Save(value);

    /// <summary>Carries an entity reference across the reload, as a bare handle.</summary>
    public void Set(string key, EntityHandle value) => _local[key] = value.Value.ToString(Culture);

    /// <summary>Whether this key was carried at all, which is not the same as what it holds.</summary>
    public bool Has(string key) => _local.ContainsKey(key);

    /// <summary>
    /// What was carried, or <paramref name="fallback"/>.
    ///
    /// <para>
    /// A FALLBACK RATHER THAN A THROW ON EVERY READER, because the case it
    /// covers is ordinary and expected: a field that is NEW in the build being
    /// loaded was never saved by the build being replaced, and it must come up
    /// holding its own initialiser rather than taking the process down. A value
    /// that is present and unreadable is the other case and is reported.
    /// </para>
    /// </summary>
    public string GetString(string key, string fallback = "")
        => _local.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback = 0)
        => _local.TryGetValue(key, out var v) && ScriptedVarParse.Int(v, out int r) ? r : fallback;

    public float GetFloat(string key, float fallback = 0f)
        => _local.TryGetValue(key, out var v) && ScriptedVarParse.Float(v, out float r) ? r : fallback;

    public bool GetBool(string key, bool fallback = false)
        => _local.TryGetValue(key, out var v) && ScriptedVarParse.Bool(v, out bool r) ? r : fallback;

    public Vector GetVector(string key, Vector fallback = default)
        => _local.TryGetValue(key, out var v) && ScriptedVarParse.Vector(v, out var r) ? r : fallback;

    public EntityHandle GetHandle(string key)
        => _local.TryGetValue(key, out var v) && uint.TryParse(v, System.Globalization.NumberStyles.Integer, Culture, out uint r)
            ? new EntityHandle(r)
            : EntityHandle.None;

    //-------------------------------------------------------------------------
    // What the default implementation did, so an override that forgot to call
    // it can be noticed rather than quietly losing every declared key.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Set by <c>ScriptedEntity.SaveState</c>'s and <c>LoadState</c>'s default
    /// bodies. The registry checks it: an override that does not chain to
    /// <c>base</c> would otherwise drop every map key, every networked field and
    /// every output on that class, and the entity would come back looking
    /// perfectly ordinary with stock's defaults in it.
    /// </summary>
    public bool DefaultRan { get; set; }

    /// <summary>
    /// How much was carried, for the reload's own report.
    ///
    /// <para>
    /// THE NATIVE SECTION IS NOT IN THIS NUMBER, deliberately. A reload never
    /// applies one — the native half of the entity never went away, so there is
    /// nothing to put back and writing it back could only be a no-op or stale —
    /// so counting it here would make the number mean one thing to the reload's
    /// report and another to a save's, which is worse than it being short.
    /// </para>
    /// </summary>
    public int Count => _vars.Count + _net.Count + _outputs.Count + _local.Count;

    /// <summary>
    /// The key and value text this record holds, as UTF-16 chars.
    ///
    /// <para>
    /// FOR THE RELOAD REPORT, and it is a different question from
    /// <see cref="Count"/>. "472 entities, 3,918 fields" says the walk ran;
    /// "3,918 fields, 210KB" says what it cost. A count cannot tell one map key
    /// from a serialised 8KB string, and the cap that exists to bound the second
    /// (<c>SavedEntityCodec.MaxStringBytes</c>) has nothing that reports how
    /// close anything gets to it.
    /// </para>
    ///
    /// <para>
    /// THE NATIVE SECTION IS COUNTED HERE, unlike in <see cref="Count"/>, and
    /// the asymmetry is deliberate rather than an oversight. Count answers "how
    /// much will a RELOAD put back", which excludes the native half because a
    /// reload never applies it. This answers "how much is being carried", and
    /// the native half is carried — it is in the record, it is in the <c>.sav</c>,
    /// and a size that left it out would understate a save by most of its bulk.
    /// </para>
    /// </summary>
    public int ByteCount
    {
        get
        {
            int n = (ClassName.Length + Name.Length) * sizeof(char);

            foreach (var (k, v) in _native)
                n += (k.Length + v.Length) * sizeof(char);
            foreach (var (k, v) in _vars)
                n += (k.Length + v.Value.Length) * sizeof(char);
            foreach (var (k, v) in _net)
                n += (k.Length + v.Length) * sizeof(char);
            foreach (var (k, v) in _outputs)
                n += (k.Length + (v.LastValue?.Length ?? 0)) * sizeof(char);
            foreach (var (k, v) in _local)
                n += (k.Length + v.Length) * sizeof(char);

            return n;
        }
    }
}

/// <summary>
/// The names <see cref="SavedEntity.Native"/> uses, in one place.
///
/// <para>
/// CONSTANTS RATHER THAN LITERALS because the writer and the reader are two
/// methods in two directions and a typo in one of them is a field that saves
/// and never restores — which looks exactly like a field that was never saved,
/// and is the failure this whole lane keeps meeting.
/// </para>
///
/// <para>
/// <b>THIS LIST IS NOT THE FORMAT.</b> The store is. A field added here is a new
/// KEY in an existing count-prefixed store, so it costs no version bump and an
/// older reader meets a key it does not know rather than bytes it misparses.
/// That property is the reason <see cref="SavedEntity.Native"/> is a dictionary
/// — see its header.
/// </para>
///
/// <para>
/// <b>WHAT IS DELIBERATELY ABSENT, each for a reason and not for want of
/// room:</b>
/// </para>
///
/// <list type="bullet">
/// <item><description>
/// <b>A parent, and an owner.</b> Both are ENTITY REFERENCES, and a handle out
/// of a <c>.sav</c> names nothing: measured on <c>dm_lockdown</c>, 0 of 470
/// handles survived a load because every serial advances when the world is
/// rebuilt. Restoring one would either fail validation or — the worse half —
/// resolve to whatever now occupies the slot and parent an entity to a
/// stranger. That is precisely the silent mis-application
/// <see cref="SavedEntity.Index"/> exists to prevent, arriving through a field
/// instead of a key. Carrying a reference needs an encoding a restore can
/// re-resolve (an index AND a classname, as the record's own matching does),
/// and that is its own change.
/// </description></item>
/// <item><description>
/// <b>A model.</b> <c>ScriptedEntity.SetModel</c> states its own contract:
/// "Must happen during spawn, so it can be precached." A restore runs after the
/// spawn, so setting a model there either needs a precache the restore cannot
/// do or quietly sets one the level has not precached. Whether that is safe is
/// a measurement nobody has taken; it is not a line of code.
/// </description></item>
/// <item><description>
/// <b>The collision group — AND THE REASON CHANGED, which is worth saying
/// because the old one has stopped being true.</b> It was left out of the
/// first cut because its two directions live on
/// <c>IEngineEntityPhysics</c>, which <c>ScriptedEntity</c> does not bind. It
/// stays out for a better reason: <b>the pair does not round trip.</b>
/// <c>Engine.EntityGetCollisionGroup</c> is MANY-TO-ONE on purpose — this mod
/// names 5 of the engine's 22 groups (<c>collision_group_names.h</c>, and its
/// lookup falls back to row 0) and reads every other one back as
/// <see cref="CollisionGroup.None"/>. So saving a group and restoring it would
/// write <c>COLLISION_GROUP_NONE</c> over whatever the entity actually had,
/// for 17 of the 22 — including <c>COLLISION_GROUP_PLAYER</c>, which is what
/// the player this section exists to bring back is in. That is not a field
/// left unrestored; it is a field CORRUPTED by the restore, which is worse
/// than the bug the section was built to fix. The binding is no longer the
/// obstacle: three of the nine calls the section uses are a second spelling on
/// <see cref="IEngineEntities"/> of a call another area also offers, and a
/// fourth could have been. It would need a lossless pair — either an engine
/// group carried as a number nothing renames, or a table that is total — and
/// that is its own change. <c>CollisionGroupParityTests</c> asserts the loss is
/// real rather than leaving this paragraph to be believed.
/// </description></item>
/// </list>
/// </summary>
public static class NativeField
{
    /// <summary>
    /// The shell's <c>m_iHealth</c>. <b>The measured one:</b>
    /// <c>tools/save-live-gate</c> damaged a <c>func_breakable_surf</c> from 5
    /// to 4, saved, loaded, and got 5 back — because the 5 in the file was
    /// <c>Vars.Health</c>, the map key, and the 4 was here.
    /// </summary>
    public const string Health = "health";

    /// <summary>The shell's armour, which is health's other half for a player.</summary>
    public const string Armor = "armor";

    /// <summary>
    /// Where it was, in WORLD space.
    ///
    /// <para>
    /// <b>ABSOLUTE, NOT LANDMARK-RELATIVE, AND THAT IS A DEPARTURE FROM STOCK
    /// THAT IS ARGUED RATHER THAN OVERLOOKED.</b> Stock's
    /// <c>FIELD_POSITION_VECTOR</c> subtracts the save's landmark
    /// (<c>CSave::WritePositionVector</c>, <c>game/shared/saverestore.cpp</c>)
    /// so a coordinate survives a TRANSITION into a map whose origin is
    /// elsewhere. Three reasons that transform does not belong here: this port
    /// has no transition — <c>BuildAdjacentMapList</c> and
    /// <c>CreateEntityTransitionList</c> both still refuse on every load — so a
    /// relative coordinate would be half a transform whose other half does not
    /// exist; <see cref="SavedEntityCodec"/> is engine-free and has no
    /// <c>CSaveRestoreData</c> to ask for a landmark, so it would have to be
    /// passed in, and a caller that passed the wrong one would displace the
    /// whole world by a constant and load without complaint; and this build's
    /// <c>SaveInit</c> sets <c>fUseLandmark = false</c>, which makes stock's own
    /// subtraction the identity anyway (see
    /// <c>src/sourcesharp/game/shared/save_field_codec.h</c>, which implements
    /// the transform for the engine's OWN datamap fields and says why).
    /// </para>
    ///
    /// <para>
    /// THREE FLOATS AND NOT A QUANTISED FORM, for the reason quantising is
    /// proposed and then regretted: the error does not stay put. A save is
    /// loaded, played, and saved again from the loaded position, so the
    /// rounding is applied once per cycle and a prop settles into the floor
    /// over a session rather than in one hop. Stock's position field is three
    /// floats too, and there is no byte budget being defended — the transform
    /// is tens of bytes against a record already hundreds wide.
    /// </para>
    /// </summary>
    public const string Origin = "origin";

    /// <summary>
    /// Which way it faced.
    ///
    /// <para>
    /// WITH THE ORIGIN AND NOT IN A LATER VERSION. A barrel that rolled did not
    /// only translate; putting it back where it stopped and not which way up it
    /// stopped is the same "loads fine and is not the saved world" failure one
    /// step smaller. Note what this does NOT restore, said out loud: a PLAYER's
    /// view. An entity's angles are not a player's eye angles, so a restored
    /// player stands where it stood and looks where the spawn left it looking.
    /// </para>
    /// </summary>
    public const string Angles = "angles";

    /// <summary>How fast it was going, which is why a rolling barrel keeps rolling.</summary>
    public const string Velocity = "velocity";

    /// <summary>And how fast it was spinning.</summary>
    public const string AngularVelocity = "avelocity";

    //-------------------------------------------------------------------------
    // THE SEVEN THAT WERE MISSING A DIRECTION, AND NO FORMAT VERSION MOVED.
    //
    // Each of these was named in the absence list above as readable but not
    // writable, or writable but not readable, through the managed ABI. Six
    // appended ABI slots later they are neither, and adding them here cost
    // exactly what the header of SavedEntity.Native said it would: a KEY each.
    // The version stamp is untouched, an older reader meets a name it does not
    // apply, and a build that predates one of these leaves the spawn's value
    // standing because HasNative says the record does not carry it.
    //
    // This is the property being exercised rather than merely claimed, which
    // is why the list above is not the format and this comment is here.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The spawnflags, as the engine's own bits.
    ///
    /// <para>
    /// <b>AND IT IS HONESTLY A NO-OP TODAY, which is written down rather than
    /// left for someone to discover.</b> A record is matched to a live entity
    /// by index and classname or by targetname, so the entity it lands on is
    /// the one the same map lump just spawned with the same flags — and
    /// nothing in this build changes them afterwards, because until the setter
    /// below existed nothing COULD. It is carried anyway for the reason stock
    /// saves <c>m_spawnflags</c>: an entity that consumes a flag at runtime
    /// (<c>m_spawnflags &amp;= ~SF_...</c>) has state in it, that is now
    /// expressible, and a key costs its own name and value in a store built to
    /// make exactly that trade cheap.
    /// </para>
    /// </summary>
    public const string SpawnFlags = "spawnflags";

    /// <summary>
    /// The collision box, as OFFSETS from the origin — this half is the low
    /// corner.
    ///
    /// <para>
    /// TWO KEYS AND NOT A SIZE. A size can only be centred on the origin and
    /// most prop models put their origin on the BASE, so a box rebuilt from a
    /// size sinks half through the floor. Stock saves the same pair, on
    /// <c>CCollisionProperty</c>.
    /// </para>
    /// </summary>
    public const string Mins = "mins";

    /// <summary>The high corner. See <see cref="Mins"/>.</summary>
    public const string Maxs = "maxs";

    /// <summary>
    /// What shape it presents to a trace — the engine's own
    /// <c>SolidType_t</c>.
    ///
    /// <para>
    /// UNTRANSLATED, and that is what lets it be saved at all. Compare the
    /// collision group in the absence list above, which is the same kind of
    /// positional enumeration and is refused here precisely because it is
    /// carried through a partial table. This one is the engine's number going
    /// out and the engine's number coming back.
    /// </para>
    /// </summary>
    public const string SolidType = "solidtype";

    /// <summary>
    /// The <c>FSOLID_</c> bits that qualify the shape — whether it is solid at
    /// all, whether it is a trigger.
    ///
    /// <para>
    /// The pair to <see cref="SolidType"/>, and the two are ONE decision:
    /// <c>Entities.SetSolidity</c> takes both and the shell runs
    /// <c>UpdatePartition</c> once over the pair, which creates or destroys
    /// the entity's spatial-partition handle. So the restore applies them
    /// together or not at all — a record carrying one and not the other leaves
    /// both alone, which is the only reading of "left alone" that does not
    /// half-apply a decision.
    /// </para>
    ///
    /// <para>
    /// <b>THE ONE OF THESE SEVEN THAT IS VISIBLE IN PLAY.</b> A
    /// <c>func_brush</c> a mapper's logic has made non-solid, a trigger that
    /// disabled itself: the map lump spawns both solid again, and before this
    /// key a load put the wall back.
    /// </para>
    /// </summary>
    public const string SolidFlags = "solidflags";

    /// <summary>
    /// Its gravity scale, a multiple of the world's.
    ///
    /// <para>
    /// NOT DEAD WEIGHT, and this is the one to point at when somebody asks
    /// whether these keys earn their bytes: <c>trigger_gravity</c> sets a
    /// toucher's scale on the way in and restores it on the way out, and
    /// <c>target_changegravity</c> sets it outright. A player saved while
    /// standing in one comes back falling at the wrong rate without this, and
    /// the trigger will never fire the restoring half because the player never
    /// leaves a volume it was never in.
    /// </para>
    /// </summary>
    public const string Gravity = "gravity";

    /// <summary>
    /// How long until its next think — SECONDS FROM NOW, not an absolute time.
    ///
    /// <para>
    /// <b>THE ONE FIELD WHOSE UNITS ARE PART OF THE DECISION.</b> A savegame
    /// load restarts <c>curtime</c>, so an absolute <c>m_flNextThink</c> read
    /// on one map and written on the next names a moment that has already gone
    /// — every restored entity would think on the first frame — or one minutes
    /// away. A remaining delay means the same thing in both frames. Stock gets
    /// to the same place by another road: <c>FIELD_TIME</c> is written relative
    /// to the save's time and read back relative to the load's.
    /// </para>
    ///
    /// <para>
    /// NEGATIVE MEANS NOT SCHEDULED, which is what the setter this pairs with
    /// already reads a negative as. An OVERDUE think reads 0 rather than a
    /// small negative number, because a small negative number handed back
    /// would stop the timer instead of running it.
    /// </para>
    /// </summary>
    public const string NextThink = "nextthink";

}
