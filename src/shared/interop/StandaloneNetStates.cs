//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a type as the networked state of one entity class on the parallel
/// hierarchy.
///
/// <para>
/// The type lives in the mod's SHARED code, and neither the server's entity
/// class nor the client's names it. That is deliberate. A SendTable and its RecvTable
/// are paired by name and order at connect, and the two halves of an entity are
/// different types in different assemblies — <c>TestBarrel</c> on the server and
/// <c>ClientTestBarrel</c> on the client. If each declared its own fields, the
/// two lists would be two lists, and this project has watched two hand-written
/// lists drift twice already.
/// </para>
///
/// <para>
/// So the fields are declared once, on a type both sides compile against and
/// neither side owns, and both build their half by walking the same list in the
/// same order. The classname on the attribute is what the entity is spawned as
/// and what both generated tables are keyed by.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class StandaloneNetStateAttribute : Attribute
{
    public StandaloneNetStateAttribute(string className)
    {
        ClassName = className;
    }

    /// <summary>The entity classname whose table this describes.</summary>
    public string ClassName { get; }

    /// <summary>
    /// Whether the entity this describes is allocated and networked AT A TIER.
    ///
    /// <para>
    /// True for every class this attribute was written for, and the default
    /// stays true so that no existing declaration changes meaning. False says
    /// the shim carrying this state allocates its own blob against its own base
    /// — today that is <c>worldspawn</c>, which is edict 0, owns brush model 0,
    /// and is what fifteen native files mean by <c>GetWorldEntity()</c>.
    /// </para>
    ///
    /// <para>
    /// STEP 8a CHECKED THIS PARAGRAPH'S REASONS AND BOTH HAD DECAYED. It used
    /// to say <c>CWorld</c> "must stay a <c>CBaseEntity</c>". It is not one and
    /// has not been since 4g: <c>game/server/world.h:339</c> reads
    /// <c>class CWorld : public IServerEntity</c>. And it said the flag changes
    /// "exactly one thing", arithmetic — that folding a 300-byte world into
    /// <see cref="StandaloneNetStates.HierarchyBlobBytes"/> would put those
    /// bytes on every entity on the hierarchy. MEASURED over the real
    /// declaration set: 77 declarations, 76 of them on the hierarchy, hierarchy
    /// blob <b>848 bytes</b>, worldspawn <b>800</b>. 800 is under 848, so
    /// folding it in TODAY COSTS NOTHING. The arithmetic argument is not wrong,
    /// it is currently vacuous, and a flag defended only by a vacuous argument
    /// is a flag somebody deletes.
    /// </para>
    ///
    /// <para>
    /// WHAT IS ACTUALLY LOAD-BEARING is the PUBLISH PATH, not the byte count. A
    /// declaration marked false is skipped by the on-hierarchy publish loop and
    /// goes out through <see cref="StandaloneNetStates.PublishOffHierarchy"/>
    /// and <c>IEngineNetworking.WorldNetDeclare</c> instead of
    /// <c>IEngineNetworking.NetHierarchyDeclare</c> — a different ABI slot, a table built
    /// by <c>shell_world_nettable.cpp</c> rather than by the tier builders, and
    /// no tier or type id. Remove the flag and the world's table is built by
    /// the wrong builder, or by neither: the on-hierarchy loop would then ask
    /// <c>hostedHere("worldspawn")</c> and skip it as "no entity class here
    /// yet". That is the world's net table, not four hundred bytes.
    /// </para>
    ///
    /// <para>
    /// AND IT IS NOT DUE TO CONVERGE. <c>plan_entity.md</c> §3.11 records an
    /// OWNER INSTRUCTION that the world is its own standalone class and not a
    /// <c>CStandaloneEntity</c> — "nothing in Phase 4 is obliged to converge
    /// them, and nothing should try" — enforced by
    /// <c>shim_underive_verdict_test.cpp</c> verdict 2. So step 8a, which
    /// deleted the rest of the two-hierarchy scaffolding, deliberately did NOT
    /// delete this. The name is the only thing it inherited from that
    /// scaffolding.
    /// </para>
    ///
    /// <para>
    /// Everything else about the declaration is unchanged and deliberately so:
    /// the fields are declared once on a type both sides compile against, the
    /// generator emits one layout, and both halves walk the same list in the
    /// same order. Both sides read this same flag out of this same shared
    /// assembly, so neither can disagree about which arithmetic applies.
    /// </para>
    /// </summary>
    public bool OnHierarchy { get; init; } = true;
}

/// <summary>
/// One class's networked state: its name, its type, and its layout.
///
/// <para>
/// CONSTRUCTED BY GENERATED CODE, with the layout already worked out. It used to
/// compute its own by reflecting over the type on first use — correct, and work
/// done once per process on each side to reach an answer that was fixed when the
/// declaration was written. The arithmetic lives in the generator now, and there
/// is one copy of it rather than one there and one here.
/// </para>
/// </summary>
public sealed class StandaloneNetState
{
    public StandaloneNetState(string className, Type type, int size, IReadOnlyList<NetFieldLayout> fields)
    {
        ClassName = className;
        Type      = type;
        Size      = size;
        Fields    = fields;
    }

    public string ClassName { get; }
    public Type Type { get; }

    /// <summary>The fields with their offsets in the instance blob, in wire order.</summary>
    public IReadOnlyList<NetFieldLayout> Fields { get; }

    /// <summary>Bytes the blob needs, including the padding the layout inserts.</summary>
    public int Size { get; }

    /// <summary>
    /// One field by name. Throws on a name this state never declared, which is
    /// a mistake in code rather than something to handle: a silent miss would
    /// be a value that is never written and never read, and the entity would
    /// replicate a zero with nothing to say why.
    /// </summary>
    public NetFieldLayout this[string name]
    {
        get
        {
            foreach (var field in Fields)
                if (field.Name == name)
                    return field;

            throw new KeyNotFoundException(
                $"'{ClassName}' declares no networked field named '{name}'");
        }
    }
}

/// <summary>
/// Every entity class on the parallel hierarchy that networks state of its own.
///
/// <para>
/// Most of them do not, and that is the point of the design rather than a gap:
/// the 111 classes that send nothing beyond origin, model, classname and angles
/// share one table and pay for nothing. A class only gets a table of its own by
/// declaring one here.
/// </para>
///
/// <para>
/// Discovered from this assembly and published by both modules from this one
/// list, in this one order — the same shape as the ammo table, the user messages
/// and the game events, and for the same reason: the client and the server must
/// walk the same sequence, and the surest way to make them is to give them one
/// sequence to walk.
/// </para>
/// </summary>
public static class StandaloneNetStates
{
    /// <summary>
    /// One state type's declaration: the table the generator wrote on it.
    ///
    /// <para>
    /// BY TYPE rather than by a scan, because the answer must not depend on
    /// anything having been discovered first. A state class asked about itself
    /// answers whether or not the module has started. Not memoized (statics to
    /// zero): the reflection here finds one field, it does not compute a
    /// layout, and every caller asks off the per-frame path.
    /// </para>
    /// </summary>
    public static StandaloneNetState Of(Type stateType)
    {
        var field = stateType.GetField("NetLayout",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (field?.GetValue(null) is not StandaloneNetState state)
        {
            throw new InvalidOperationException(
                $"{stateType.Name} has no generated NetLayout. A [StandaloneNetState] " +
                $"class must be partial, and its assembly must reference " +
                $"SourceSharp.Generators as an analyzer.");
        }

        return state;
    }

    /// <summary>
    /// Every declaration in every assembly loaded beside this one, in a fixed
    /// order — assembly name, then source declaration order, since reflection
    /// makes no ordering promise and MetadataToken is fixed in the compiled
    /// image.
    ///
    /// Takes an assembly rather than assuming one, the same way the user
    /// messages and game events are registered: this lives in the ABI now and
    /// the declarations live in the mod's shared code. What is passed is the
    /// side's game assembly, which the shared source compiles into
    /// (plan_side_isolation P7); that both sides build the same tables — a table
    /// only one side builds is the failure with no symptom — is held at build
    /// time by ProtocolSetTests.
    /// </summary>
    public static IReadOnlyList<StandaloneNetState> Discover(Assembly assembly, ILog log)
    {
        var found = new List<Type>();

        foreach (var type in LoadedAssemblies.Types(assembly, log))
        {
            if (type.GetCustomAttribute<StandaloneNetStateAttribute>() is null)
                continue;

            found.Add(type);
        }

        return Discover(found);
    }

    /// <summary>
    /// Discovers exactly <paramref name="found"/>, in key order whatever order
    /// they arrive in: what <see cref="Discover(Assembly, ILog)"/> does after its scan.
    /// </summary>
    internal static IReadOnlyList<StandaloneNetState> Discover(IEnumerable<Type> found)
    {
        // KEY ORDER (plan_side_isolation §2.1): by classname, which both sides
        // derive from the same declaration whatever assembly it compiled into.
        var states = found.Select(Of)
                          .OrderBy(s => s.ClassName, StringComparer.Ordinal)
                          .ToArray();

        // Pure (statics to zero): the by-name and by-id answers are a
        // NetStateCatalog's, which each composition builds over the same
        // declarations.
        return states;
    }

    /// <summary>
    /// A field kind as a small integer.
    ///
    /// <para>
    /// An explicit mapping and not a cast: <see cref="NetFieldKind"/> is ordered
    /// for reading and these values are not, because they were
    /// <c>SourceSharpFieldType</c> over the ABI and grew in the order the ABI
    /// needed them. A cast would compile, run, and fingerprint every field as a
    /// different kind.
    /// </para>
    ///
    /// <para>
    /// NATIVE NO LONGER RECEIVES THEM (Step 7). What they decide now is the
    /// SCHEMA HASH, which both sides compute over their own copy of the
    /// declarations — so renumbering one is still a wire change on whichever
    /// side is rebuilt, and it is still caught, by the hash rather than by a
    /// prop landing on the wrong type.
    /// </para>
    /// </summary>
    public static int TypeCodeOf(NetFieldKind kind) => kind switch
    {
        NetFieldKind.Int    => 0,
        NetFieldKind.Float  => 1,
        NetFieldKind.Vector => 2,
        NetFieldKind.Bool   => 3,
        NetFieldKind.String => 4,
        NetFieldKind.UInt   => 5,

        // APPENDED, never inserted. These numbers are what the schema hash mixes
        // for a field's kind, so renumbering one is a wire change for every
        // class that declares it — which is why 0..5 are the order the ABI
        // happened to grow in rather than the order the enum reads in, and why
        // these two are 6 and 7 and not wherever they would look tidiest.
        NetFieldKind.Color32 => 6,

        // ITS OWN CODE THOUGH THE BITS ARE A VECTOR'S. See NetFieldKind.QAngle:
        // the two sides disagreeing about which of the two a field is is a
        // disagreement about the VALUE, not about the bytes, and nothing else
        // would ever report it. Sharing Vector's 2 here would have made the hash
        // agree across exactly that disagreement.
        NetFieldKind.QAngle  => 7,

        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Describes every declared class to native, which builds its half of the
    /// pair. Returns how many classes were described.
    ///
    /// Called from both modules at startup, because a ServerClass and a
    /// ClientClass may only be created before the engine collects the class
    /// list — which is during DLL init and never again.
    /// </summary>
    /// <param name="networking">The publishing side's own networking area
    /// (plan_abi_hotreload D8), never whichever side is active.</param>
    /// <param name="hostedHere">
    /// Whether this side has an entity class for a classname.
    ///
    /// A state may be declared before the class it belongs to has been moved
    /// onto the hierarchy — the declaration is the safe half of the work and
    /// wants doing first. Publishing one anyway would put a ClientClass on the
    /// client with no ServerClass to pair with, or the reverse, which is the
    /// failure with no symptom. So a side publishes only what it can host, and
    /// says which ones it skipped.
    /// </param>
    public static int PublishAll(IEngineNetworking networking, string side, Assembly assembly,
                                 Func<string, bool> hostedHere, INetTypeRefusals refusals, ILog log)
        => PublishAll(networking, side, assembly, hostedHere, Array.Empty<string>(), refusals, log);

    /// <param name="hierarchyClasses">
    /// Every entity classname this side hosts on the hierarchy, whether or not
    /// it declares state.
    ///
    /// <para>
    /// NEEDED SINCE STEP 6, and it is the one thing about the swap that is not
    /// obvious. m_szClassname used to be replicated, so the receiving side read
    /// the name off the wire whatever the class was. m_nTypeId is a hash, and a
    /// hash is only a name if the far side has a table to look it up in — so
    /// the 111 classes that declare NOTHING have to be published too, or their
    /// entities arrive carrying four bytes nobody can turn back into a name and
    /// pair with no managed class at all.
    /// </para>
    /// </param>
    public static int PublishAll(IEngineNetworking networking, string side, Assembly assembly,
                                 Func<string, bool> hostedHere, IEnumerable<string> hierarchyClasses,
                                 INetTypeRefusals refusals, ILog log)
    {
        var all = Discover(assembly, log);

        // BEFORE anything is published, and from ALL of them rather than from
        // the ones this side hosts. See HierarchyBlobBytes and the ABI comment
        // on NetHierarchyDeclare: the generic payload's word count belongs to
        // the hierarchy, and a side that derived it from what it publishes
        // would derive a different number from the other side's.
        int blobBytes = HierarchyBlobBytes(all);
        int words = networking.NetHierarchyDeclare(blobBytes);

        // COUNTED THE WAY THE NUMBER IS COMPUTED. The max above skips the
        // declarations whose shim is not on the hierarchy, so reporting the
        // whole list beside it would name a population the byte count is not
        // over -- which is the shape of every "the meter says one thing and
        // measures another" finding in this ledger.
        int onHierarchy = all.Count(IsOnHierarchy);

        log.Msg($"[SourceSharp/{side}] hierarchy blob is {blobBytes}b over " +
                   $"{onHierarchy} declaration(s): {words} generic payload word(s) per class\n");

        int published = 0;

        foreach (var state in all)
        {
            // NOT ON THE HIERARCHY, so it has no tier and no type id to publish.
            // Its shim is a class of the SDK's and it builds its own table from
            // this same declaration -- see WorldNetTable, which is the one such
            // shim today. Skipped SILENTLY here rather than reported as
            // unhosted, because "no entity class here yet" is the opposite of
            // what is true: the entity class exists and is simply not one of
            // these.
            if (!IsOnHierarchy(state))
                continue;

            if (!hostedHere(state.ClassName))
            {
                log.Msg($"[SourceSharp/{side}] '{state.ClassName}' declares " +
                           $"{state.Fields.Count} networked field(s) and has no entity class " +
                           $"here yet; not published\n");
                continue;
            }

            published++;

            // THE TYPE, AND NOTHING ELSE (Step 7). A description of every field
            // used to follow this, and native kept a copy of it; there is one
            // copy of the layout now, on this side, and native is told only that
            // the type exists, what it fingerprints to and how big it is.
            PublishOne(networking, side, state.ClassName,
                       NetSchema.FromStandalone(EmptyTiers.Instance, state, published: true).Fields, state.Size,
                       refusals, log);

            log.Msg($"[SourceSharp/{side}] '{state.ClassName}' networks " +
                       $"{state.Fields.Count} field(s) of its own ({state.Size}b)\n");
        }

        // AND EVERY OTHER CLASS ON THE HIERARCHY. See the parameter's note: a
        // type id is only a name if the far side can look it up, and the classes
        // that declare nothing are the overwhelming majority.
        //
        // Their schema hash is the fingerprint of an EMPTY layout rather than
        // zero, and that is deliberate: zero is what an entity carries before
        // anything has stamped it, so a class whose real answer was zero could
        // not be told from one that was never published. Both sides compute the
        // empty fingerprint by the same function, so they agree.
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in all)
            declared.Add(state.ClassName);

        int stateless = 0;

        foreach (string className in hierarchyClasses)
        {
            if (declared.Contains(className) || !hostedHere(className))
                continue;

            PublishOne(networking, side, className, Array.Empty<NetSchemaField>(), 0, refusals, log);
            stateless++;
        }

        if (stateless > 0)
        {
            log.Msg($"[SourceSharp/{side}] published {stateless} hierarchy class(es) that " +
                       $"declare no state, so their type ids resolve to names on the far side\n");
        }

        return published;
    }

    /// <summary>
    /// Declares the state of every shim that is NOT on the parallel hierarchy —
    /// today, worldspawn's and nothing else (plan_gamedll_after.md Part B2).
    ///
    /// <para>
    /// A SEPARATE PASS BECAUSE IT IS A SEPARATE NUMBER.
    /// <see cref="PublishAll"/> hands native ONE blob size for the whole
    /// hierarchy, because <c>operator new</c> there is a static function with no
    /// idea which class the factory is about to make. A shim on the SDK's
    /// hierarchy has its own <c>operator new</c> and its own base, so its size
    /// is its own — and folding it into the hierarchy's max would put its bytes
    /// on a hundred-odd entities that have no use for them.
    /// </para>
    ///
    /// <para>
    /// IT REFUSES A SECOND ONE, LOUDLY, and that refusal is the honest shape of
    /// what the ABI can express today: <c>WorldNetDeclare</c> carries a SIZE and
    /// not a classname, so it can only ever describe one shim. A second
    /// off-hierarchy declaration is a real thing somebody might write and it
    /// needs a slot that names its class; going quiet about it would give the
    /// second one the first one's table.
    /// </para>
    ///
    /// <para>
    /// Called from both modules, inside the DLL-init window, for the reason
    /// everything else in that window is: a <c>ServerClass</c> links itself into
    /// <c>g_pServerClassHead</c> from its own constructor and the engine walks
    /// that list exactly once.
    /// </para>
    /// </summary>
    /// <returns>The payload word count native answered, or −1 on refusal.</returns>
    public static int PublishOffHierarchy(IEngineNetworking networking, string side, Assembly assembly,
                                          INetTypeRefusals refusals, ILog log)
    {
        var offHierarchy = Discover(assembly, log).Where(s => !IsOnHierarchy(s)).ToArray();

        if (offHierarchy.Length == 0)
            return -1;

        if (offHierarchy.Length > 1)
        {
            log.Warning(
                $"[SourceSharp/{side}] {offHierarchy.Length} declarations are marked " +
                $"OnHierarchy = false (" +
                string.Join(", ", offHierarchy.Select(s => s.ClassName)) +
                "), and the ABI can describe exactly one: WorldNetDeclare carries a size " +
                "and not a class name. Only the first is declared; the rest network " +
                "nothing.\n");
        }

        var state = offHierarchy[0];
        int words = networking.WorldNetDeclare(state.Size);

        log.Msg($"[SourceSharp/{side}] '{state.ClassName}' networks {state.Fields.Count} " +
                   $"field(s) of its own ({state.Size}b) off the hierarchy: " +
                   $"{words} payload word(s)\n");

        return words;
    }

    /// <summary>
    /// Publishes one type and checks native's hash against ours.
    ///
    /// <para>
    /// THE HASH CHECK IS THE POINT of doing this rather than calling the ABI
    /// directly. FNV-1a exists twice — <see cref="NetSchema.TypeIdOf"/> here and
    /// <c>SourceSharp_HashClassName</c> in generic_nettable.h — and two
    /// implementations of one function in two languages is the shape this tree
    /// keeps paying for. A parity test pins the corpus somebody thought of; this
    /// pins every name the build actually uses, at startup, before anything is
    /// on the wire.
    /// </para>
    /// </summary>
    private static void PublishOne(IEngineNetworking networking, string side, string className,
                                   IReadOnlyList<NetSchemaField> fields, int layoutBytes,
                                   INetTypeRefusals refusals, ILog log)
    {
        uint schemaHash = NetSchema.SchemaHashOf(fields, layoutBytes);

        int tier = networking.NetPublishType(className, (int)NetShellKind.Standalone,
                                             schemaHash, layoutBytes, out uint nativeId);

        // -1 with a zero id is an absent engine, which is the test harness and
        // not a failure. -1 with an id is native refusing the type — a name
        // collision, a layout past the largest tier, or a full table — and it
        // has already said so loudly in the console. Recorded as well, because
        // the reader who most needs to know is a panel that was not there when
        // it happened.
        if (tier < 0)
        {
            if (nativeId != 0)
                refusals.Record(className, nativeId, layoutBytes);

            return;
        }

        uint managedId = NetSchema.TypeIdOf(className);

        if (nativeId != managedId)
        {
            log.Warning(
                $"[SourceSharp/{side}] TYPE ID PARITY BROKEN for '{className}': native hashed " +
                $"0x{nativeId:X8} and C# hashed 0x{managedId:X8}. The two FNV-1a " +
                $"implementations have drifted, and every entity of this class will pair with " +
                $"the wrong managed type or with none.\n");
        }
    }

    /// <summary>
    /// How many blob bytes every entity on the parallel hierarchy is allocated:
    /// the greediest declaration, rounded up to sixteen.
    ///
    /// <para>
    /// ONE number for the whole hierarchy, because <c>operator new</c> on both
    /// shells is a static function with no idea which class the factory is
    /// about to make — so every entity there is sized for the greediest class
    /// whatever it is itself. That is what makes the generic payload's word
    /// count a property of the hierarchy rather than of a class, which is the
    /// shape the per-class native builders could not express.
    /// </para>
    ///
    /// <para>
    /// Over EVERY declaration, including ones whose entity class this side does
    /// not host. A state may be declared before one half of its entity has been
    /// ported, and the two sides then publish different sets; a number derived
    /// from what a side publishes would differ between them, and their payload
    /// prop counts with it. Both sides read this same shared assembly, so both
    /// reach this same number.
    /// </para>
    ///
    /// <para>
    /// Max of <c>Offset + Bytes</c>, aligned to 16. It used to mirror
    /// <c>SourceSharp_RuntimeBlobBytes</c> in runtime_netfields.h, which summed
    /// the same fields again on the far side of the ABI from the copy it had
    /// been shipped; Step 7 deleted that copy, so this arithmetic has one
    /// implementation and the two sides cannot reach different answers from the
    /// same declarations.
    /// </para>
    /// </summary>
    public static int HierarchyBlobBytes(IReadOnlyList<StandaloneNetState> states)
    {
        int max = 0;

        foreach (var state in states)
        {
            // A declaration whose shim is NOT on the hierarchy is not one of
            // the entities this number sizes. See
            // StandaloneNetStateAttribute.OnHierarchy: worldspawn's shim is a
            // CBaseEntity that allocates its own blob against its own base, so
            // folding its length into this max would put its bytes on every
            // entity that IS on the hierarchy and on none that needed them.
            if (!IsOnHierarchy(state))
                continue;

            foreach (var field in state.Fields)
                max = Math.Max(max, field.Offset + field.Bytes);
        }

        return (max + 15) & ~15;
    }

    /// <summary>
    /// Whether a declaration's shim lives on the parallel hierarchy.
    ///
    /// <para>
    /// Read off the attribute rather than carried on
    /// <see cref="StandaloneNetState"/>, so the generated <c>NetLayout</c> is
    /// unchanged and every existing declaration keeps the body it already had.
    /// It is asked once per state at discovery and at publish, both of which
    /// happen once per module load.
    /// </para>
    /// </summary>
    public static bool IsOnHierarchy(StandaloneNetState state)
        => state.Type.GetCustomAttribute<StandaloneNetStateAttribute>()?.OnHierarchy ?? true;
}

/// <summary>
/// The declared networked states BY CLASSNAME and BY TYPE ID, for the callers
/// that hold only a name or an id: native's layout asks (FieldOffset,
/// FieldBits) and the client's prediction meter (DescribeOffset, ClassNameOf).
///
/// <para>
/// STEP 7 PUT THE FIELD LIST HERE AND NOWHERE ELSE. Native used to keep its
/// own copy of every declared field — name, type, blob offset, bits, bytes —
/// shipped across the ABI by <c>NetFieldBegin</c>/<c>Add</c>/<c>Commit</c>,
/// and the client's surface renderer read a field's offset out of it. Two
/// copies of one layout is the shape this tree keeps paying for, and this one
/// would have drifted silently: a wrong offset reads a plausible number out
/// of a neighbouring field. Native asks this instead, through
/// <c>NetStateFieldOffset</c> on the managed API, so there is one copy.
/// </para>
///
/// <para>
/// AN INSTANCE PER COMPOSITION (statics to zero): it was three static maps
/// every Discover wrote, last side winning, and a memo of tolerances. Each
/// side's module builds one over the assembly that declares its states
/// (both sides: the same shared declarations) and hands it to what asks.
/// The declarations are read on first ask, so a class asked about before or
/// after its side published answers the same.
/// </para>
/// </summary>
public sealed class NetStateCatalog
{
    private readonly object _lock = new();
    private readonly Func<IEnumerable<StandaloneNetState>> _declared;
    private Dictionary<string, StandaloneNetState>? _byClassNameFilled;
    private Dictionary<uint, StandaloneNetState>? _byTypeIdFilled;
    private readonly Dictionary<Type, Dictionary<string, float>> _tolerances = new();

    /// <summary>A catalog of the states <paramref name="declarations"/> declares.</summary>
    public NetStateCatalog(Assembly declarations, ILog log)
        : this(() => StandaloneNetStates.Discover(declarations, log))
    {
        ArgumentNullException.ThrowIfNull(declarations);
    }

    /// <summary>A catalog of exactly <paramref name="states"/>.</summary>
    public NetStateCatalog(IEnumerable<Type> states)
        : this(() => StandaloneNetStates.Discover(states))
    {
    }

    private NetStateCatalog(Func<IEnumerable<StandaloneNetState>> declared) => _declared = declared;

    /// <summary>The declarations by entity classname.</summary>
    private Dictionary<string, StandaloneNetState> _byClassName
    {
        get
        {
            Fill();
            return _byClassNameFilled!;
        }
    }

    /// <summary>The declarations by type id (<see cref="NetSchema.TypeIdOf"/> of the classname).</summary>
    private Dictionary<uint, StandaloneNetState> _byTypeId
    {
        get
        {
            Fill();
            return _byTypeIdFilled!;
        }
    }

    private void Fill()
    {
        lock (_lock)
        {
            if (_byClassNameFilled is not null)
                return;

            var byName = new Dictionary<string, StandaloneNetState>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<uint, StandaloneNetState>();

            foreach (var state in _declared())
            {
                byName[state.ClassName] = state;
                byId[NetSchema.TypeIdOf(state.ClassName)] = state;
            }

            _byTypeIdFilled = byId;
            _byClassNameFilled = byName;
        }
    }

    /// <summary>
    /// Where a declared field sits INSIDE THE BLOB, or -1 for a class or a field
    /// this side does not know.
    ///
    /// <para>
    /// Blob-relative, deliberately. The two shells are unrelated C++ classes and
    /// their blobs start at different offsets within them, so the caller adds its
    /// own base — the same split the payload props have always had, where the
    /// word INDEX is what the two sides agree on and the offset is each side's
    /// own business.
    /// </para>
    ///
    /// <para>
    /// -1 rather than a throw: the caller is native, an exception unwinding into
    /// Source's C++ frames would corrupt the process, and "no such field" is a
    /// real answer for a renderer asking about a class it was compiled against
    /// and this build no longer declares.
    /// </para>
    /// </summary>
    public int FieldOffset(string className, string fieldName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(fieldName))
            return -1;

        StandaloneNetState? state;

        lock (_lock)
        {
            if (!_byClassName.TryGetValue(className, out state))
                return -1;
        }

        foreach (var field in state.Fields)
        {
            if (field.Name == fieldName)
                return field.Offset;
        }

        return -1;
    }

    /// <summary>
    /// Where inside that offset's dword the field's bits are — the shift in the
    /// low byte and the width above it — or -1 for a class or a field this side
    /// does not know.
    ///
    /// <para>
    /// THE OTHER HALF OF <see cref="FieldOffset"/>, and it exists because
    /// packing made an offset stop being a placement. Four of
    /// <c>func_breakable_surf</c>'s fields share word zero, so a native caller
    /// that asked where <c>Broken</c> is and read a byte there would get the
    /// pane count.
    /// </para>
    ///
    /// <para>
    /// A field that is not packed answers shift 0 and its full width, which is
    /// the truth rather than a sentinel: a caller that always shifts and masks
    /// then reads every field correctly and never has to ask which kind it has.
    /// A vector, a float and a string answer a width of 32 for the same reason —
    /// they are read as whole storage, and a caller reading one through the mask
    /// gets its first word, which is what it would have got anyway.
    /// </para>
    ///
    /// <para>
    /// Packed into one int because the alternative is two out-parameters across
    /// a function-pointer ABI, and every extra slot is a place the two sides can
    /// be built in different orders. The shift is at most 31 and the width at
    /// most 32, so both fit in a byte and the encoding cannot overflow.
    /// </para>
    /// </summary>
    public int FieldBits(string className, string fieldName)
    {
        if (string.IsNullOrEmpty(className) || string.IsNullOrEmpty(fieldName))
            return -1;

        StandaloneNetState? state;

        lock (_lock)
        {
            if (!_byClassName.TryGetValue(className, out state))
                return -1;
        }

        foreach (var field in state.Fields)
        {
            if (field.Name != fieldName)
                continue;

            int width = field.IsPacked ? field.Bits : Math.Min(field.Bytes * 8, 32);
            return (field.BitShift & 0xFF) | (width << 8);
        }

        return -1;
    }

    /// <summary>
    /// Which declared field a blob offset lands in, and how a misprediction of
    /// it is judged (Phase 9c3). The question the client's prediction meter
    /// asks when a compare has already failed — so this is never a hot path,
    /// and the layout stays where Step 7 put it: once, in the language that
    /// computes it. Shipping a field table to native was considered and is
    /// exactly the copy Step 7 deleted.
    ///
    /// <para>
    /// PACKED FIELDS SHARE A WORD, so a byte offset alone is ambiguous there:
    /// Thinks and Broken both claim the barrel's word at offset 4. The byte is
    /// resolved through the BIT ranges — byte N of a word holds bits 8N..8N+7,
    /// and the field whose shift/width overlaps them owns it. A byte of the
    /// word no field claims is padding, which is answer 0 like any other
    /// padding byte.
    /// </para>
    /// </summary>
    /// <returns>
    /// 1 — the offset is inside a declared field: every out param is filled,
    /// and the kind is <see cref="StandaloneNetStates.TypeCodeOf"/>'s numbering (the ABI's).
    /// 0 — the type is known but no field claims that byte (padding).
    /// -1 — no declaration with that type id on this side.
    /// </returns>
    public int DescribeOffset(uint typeId, int offset,
                                     out string fieldName, out int fieldStart,
                                     out int fieldBytes, out int kind, out float tolerance)
    {
        fieldName = ""; fieldStart = -1; fieldBytes = 0; kind = -1; tolerance = 0f;

        StandaloneNetState? state;
        lock (_lock)
        {
            if (!_byTypeId.TryGetValue(typeId, out state))
                return -1;
        }

        NetFieldLayout? hit = null;

        foreach (var field in state.Fields)
        {
            if (offset < field.Offset || offset >= field.Offset + field.Bytes)
                continue;

            if (field.IsPacked)
            {
                int bitLow  = (offset - field.Offset) * 8;
                int bitHigh = bitLow + 8;

                if (field.BitShift + field.Bits <= bitLow || field.BitShift >= bitHigh)
                    continue; // shares the word, does not own this byte
            }

            hit = field;
            break;
        }

        if (hit is not { } found)
            return 0;

        fieldName  = found.Name;
        fieldStart = found.Offset;
        fieldBytes = found.Bytes;
        kind       = StandaloneNetStates.TypeCodeOf(found.Kind);
        tolerance  = ToleranceOf(state, found.Name);
        return 1;
    }

    /// <summary>
    /// The entity class a type id names on this side, or empty.
    ///
    /// <para>
    /// A type id is what crosses the ABI and a class name is what a reader
    /// recognises. Empty rather than a placeholder for a type this side does not
    /// declare, so a caller decides how to say "unknown" rather than being handed
    /// a string that could be mistaken for a class.
    /// </para>
    /// </summary>
    public string ClassNameOf(uint typeId)
    {
        lock (_lock)
        {
            return _byTypeId.TryGetValue(typeId, out var state) ? state.ClassName : "";
        }
    }

    /// <summary>
    /// How many [Predicted] fields a type declares, by type id — the
    /// predictable-set gate (9c5): the replay rings an entity exactly when
    /// this is positive, so "predictable" is a C# declaration with a default
    /// of no, which is the plan's sizing rule stated as code. -1 for a type
    /// this side does not declare.
    /// </summary>
    public int PredictedFieldCount(uint typeId)
    {
        StandaloneNetState? state;
        lock (_lock)
        {
            if (!_byTypeId.TryGetValue(typeId, out state))
                return -1;
        }

        int count = 0;

        foreach (var property in state.Type.GetProperties())
        {
            if (property.GetCustomAttribute<PredictedAttribute>() is not null)
                count++;
        }

        return count;
    }

    /// <summary>
    /// The [Predicted] tolerance a state declared for a field, or 0 — exact,
    /// stock's default (predictioncopy.cpp:736 engages a tolerance only when
    /// it is positive). Cached per type; see _tolerances.
    /// </summary>
    private float ToleranceOf(StandaloneNetState state, string fieldName)
    {
        Dictionary<string, float>? map;

        lock (_lock)
        {
            if (!_tolerances.TryGetValue(state.Type, out map))
            {
                map = new Dictionary<string, float>(StringComparer.Ordinal);

                foreach (var property in state.Type.GetProperties())
                {
                    if (property.GetCustomAttribute<PredictedAttribute>() is { } predicted)
                        map[property.Name] = predicted.Tolerance;
                }

                _tolerances[state.Type] = map;
            }
        }

        return map.TryGetValue(fieldName, out float declared) ? declared : 0f;
    }

}
