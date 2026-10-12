using System.Runtime.Loader;

namespace SourceSharp.Abi;

/// <summary>
/// <b>WHAT A SHELL-LESS ENTITY KEEPS IN PLACE OF A SHELL</b> —
/// <c>plan_entity.md</c> step 2e, §3.3.
///
/// <para>
/// A class of kind <see cref="ScriptedEntityKind.Logical"/> creates no native
/// object at all: no <c>CStandaloneEntity</c>, no entry in the engine's entity
/// list, no engine block. It is "a managed object that exists only in the
/// directory". The eight keys the shell used to claim for itself
/// (<c>CStandaloneEntity::KeyValueShellOnly</c>) have to land somewhere, and
/// this is where.
/// </para>
///
/// <para>
/// <b>It is deliberately NOT a managed copy of the engine block.</b> The block
/// (§3.2) exists so that a shell's getters can be a load at a constant offset
/// on a path the engine takes per trace and per draw. Nothing takes that path
/// for an entity the engine cannot see — it is not in the partition, it is not
/// networked, it is not drawn — so the whole reason for a blittable struct at
/// offset zero is absent, and the same information as ordinary managed fields
/// costs no layout contract and no fingerprint.
/// </para>
/// </summary>
public sealed class ShellLessEntity
{
    public ShellLessEntity(EntityHandle handle, string className)
    {
        Handle = handle;
        ClassName = className;
    }

    /// <summary>
    /// The handle this state belongs to, kept so a lookup can check the SERIAL
    /// and not only the entry.
    /// </summary>
    /// <remarks>
    /// Without it a stale handle to a freed shell-less entity would be answered
    /// out of whatever took the entry — the same use-after-free
    /// <see cref="EntityDirectory.Resolve"/> refuses, arrived at by a different
    /// table.
    /// </remarks>
    public EntityHandle Handle { get; }

    /// <summary>The classname the map placed, which is what a find-by-class matches.</summary>
    public string ClassName { get; }

    /// <summary><c>targetname</c>. Also fed to the directory's name index, as the shell's arm was.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>spawnflags</c> — <c>m_nSpawnFlags</c>.</summary>
    public int SpawnFlags { get; set; }

    /// <summary><c>health</c> — <c>m_iHealth</c>.</summary>
    public int Health { get; set; }

    /// <summary><c>origin</c> — the block's <c>Head.Origin</c>.</summary>
    public Vector Origin { get; set; }

    /// <summary><c>angles</c> — the block's <c>Head.Angles</c>.</summary>
    public Vector Angles { get; set; }

    /// <summary>
    /// <c>model</c>, recorded and never applied.
    ///
    /// <para>
    /// The shell recorded it too and for the same reason — precaching belongs to
    /// level load and placing belongs to <c>Spawn</c>. A logical entity has
    /// nothing to draw, so this exists so that a map key is not silently lost
    /// rather than because anything reads it.
    /// </para>
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Where it was when <c>Spawn</c> ran — <c>SourceSharp_EntityGetSpawnOrigin</c>.</summary>
    public Vector SpawnOrigin { get; set; }

    /// <inheritdoc cref="SpawnOrigin"/>
    public Vector SpawnAngles { get; set; }

    /// <summary>Whether <c>Spawn</c> has run. A second spawn is refused, as the shell's is.</summary>
    public bool HasSpawned { get; set; }

    /// <summary>
    /// <b><c>CStandaloneEntity::KeyValueShellOnly</c>, transliterated</b> — the
    /// eight keys an entity has because it is an entity rather than because its
    /// class declared them.
    ///
    /// <para>
    /// Answers whether the key was claimed, which is what the native arm
    /// answers, and for the same use: it is <b>not</b> a filter. Every key still
    /// goes on to <c>ScriptedEntityRegistry.KeyValue</c> afterwards, unclaimed
    /// keys included, because that is what feeds <c>ReportUnclaimed</c> and
    /// <c>IsNearMiss</c> — the near-miss report exists to catch a mapper who
    /// typed a key one character away from a declared one, and those are
    /// precisely the keys a filter drops first (step 2a's refuted route).
    /// </para>
    ///
    /// <para>
    /// <b><c>parentname</c> and <c>parentattachment</c> are claimed here and go
    /// no further</b>, exactly as the shell claimed them: parenting is not
    /// something a class opts into, so a key claimed in one class would have to
    /// be claimed in all of them and the one that forgot would silently drop a
    /// mapper's link. They are RECORDED rather than resolved — the entity named
    /// may not exist yet, and neither end has the position the offset is
    /// measured from until <c>Spawn</c> has run.
    /// </para>
    /// </summary>
    public bool ApplyShellKey(string key, string value)
    {
        if (key.Equals("origin", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseTriple(value, out var v))
                Origin = v;

            return true;
        }

        if (key.Equals("angles", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseTriple(value, out var v))
                Angles = v;

            return true;
        }

        if (key.Equals("targetname", StringComparison.OrdinalIgnoreCase))
        {
            Name = value;
            return true;
        }

        if (key.Equals("parentname", StringComparison.OrdinalIgnoreCase))
        {
            ParentName = value;
            return true;
        }

        if (key.Equals("parentattachment", StringComparison.OrdinalIgnoreCase))
        {
            ParentAttachmentName = value;
            return true;
        }

        if (key.Equals("spawnflags", StringComparison.OrdinalIgnoreCase))
        {
            SpawnFlags = ParseInt(value);
            return true;
        }

        if (key.Equals("health", StringComparison.OrdinalIgnoreCase))
        {
            Health = ParseInt(value);
            return true;
        }

        if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
        {
            Model = value;
            return true;
        }

        return false;
    }

    /// <summary>The <c>parentname</c> the map wrote, unresolved. Null when it wrote none.</summary>
    public string? ParentName { get; private set; }

    /// <inheritdoc cref="ParentName"/>
    public string? ParentAttachmentName { get; private set; }

    /// <summary>
    /// <c>sscanf( szValue, "%f %f %f", … ) == 3</c>, and the <c>== 3</c> is the
    /// part that matters: the shell left the value ALONE when the string did not
    /// hold three numbers, rather than writing a partly parsed vector.
    /// </summary>
    internal static bool TryParseTriple(string value, out Vector result)
    {
        result = default;

        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;

        if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        result = new Vector(x, y, z);
        return true;
    }

    /// <summary><c>atoi</c>: leading number, and zero for anything else. Never throws.</summary>
    internal static int ParseInt(string value)
    {
        int i = 0;
        while (i < value.Length && char.IsWhiteSpace(value[i]))
            i++;

        int start = i;
        if (i < value.Length && (value[i] == '-' || value[i] == '+'))
            i++;

        int digits = i;
        while (i < value.Length && value[i] >= '0' && value[i] <= '9')
            i++;

        if (i == digits)
            return 0;

        return long.TryParse(value.AsSpan(start, i - start), out long parsed)
            ? unchecked((int)parsed)
            : 0;
    }
}

/// <summary>
/// What a side has to be able to answer about its shell-less entities for the
/// static engine surface to stop crossing for them —
/// <c>plan_entity.md</c> step 2e.
/// </summary>
public interface IShellLessSide
{
    /// <summary>Whether a class registered on this side is of kind <see cref="ScriptedEntityKind.Logical"/>.</summary>
    bool ClassIsShellLess(string className);

    /// <summary>
    /// Makes one, keyed with <paramref name="packedKeys"/> if the caller has
    /// them (<c>key\0value\0…</c>, the map spawner's payload).
    /// <see cref="EntityHandle.None"/> if the class is not shell-less or the
    /// invisible range is full.
    /// </summary>
    EntityHandle Create(string className, ReadOnlySpan<byte> packedKeys);

    /// <summary>The state behind a handle, or null for anything that is not a shell-less entity of this side.</summary>
    ShellLessEntity? StateOf(EntityHandle handle);

    /// <summary>Runs the entity's <c>Spawn</c>. Ignores a handle that is not one of these.</summary>
    void Spawn(EntityHandle handle);

    /// <summary>One key after creation — <c>ent_create</c>'s route, and a template's.</summary>
    bool KeyValue(EntityHandle handle, string key, string value);

    /// <summary>Appends every shell-less entity whose classname matches. Answers how many.</summary>
    int FindByClassName(string query, List<EntityHandle> into);

    /// <summary>Appends every shell-less entity whose targetname matches. Answers how many.</summary>
    int FindByName(string query, List<EntityHandle> into);

    /// <summary>
    /// The single entity a name lookup answers: an entity with a shell before
    /// a shell-less one; shelled entities in creation order, shell-less ones by
    /// ascending entry (<see cref="EntityDirectory.FirstByName"/>).
    /// <see cref="EntityHandle.None"/> for no match.
    /// </summary>
    EntityHandle FindFirstNamed(string query);

    /// <summary>
    /// Every entity a name lookup answers, in <see cref="FindFirstNamed"/>'s
    /// order (<see cref="EntityDirectory.AllByName"/>), appended to
    /// <paramref name="into"/>. Answers how many.
    /// </summary>
    int FindAllNamed(string query, List<EntityHandle> into, int limit);

    /// <summary>
    /// Every entity a name lookup answers, as an allocation-free snapshot to
    /// <c>foreach</c> over (<see cref="NameMatches"/> says what the snapshot
    /// promises when the loop changes the entity set).
    /// </summary>
    NameMatches Named(string query, int limit);

    /// <summary>How many are live, for the readout that keeps the safety zero honest.</summary>
    int LiveCount { get; }

    /// <summary>How many have ever been allocated and released, for the same readout.</summary>
    (long Allocated, long Released) AllocationTotals { get; }
}

/// <summary>
/// <b>WHERE A SHELL-LESS ENTITY IS ANSWERED FROM</b> — the route from the
/// engine surface to the side's own directory, <c>plan_entity.md</c> step 2e.
///
/// <para>
/// <b>AN INSTANCE, ONE PER ENGINE SIDE</b> (plan_abi_hotreload D7). It was a
/// static class, because <c>Engine</c> is static and a directory is an
/// INSTANCE; so two managed game instances in one process would have answered
/// each other's shell-less entities. Each <see cref="EngineSide"/> now owns
/// one, its container registers it (<c>EngineServices.AddTo</c>), and the
/// registry that answers binds itself into the one it is constructed with.
/// The same holds for <see cref="EntityRemovalRoutes"/> and
/// <see cref="EntityIoRoutes"/>.
/// </para>
///
/// <para>
/// <b>NO SIDE GUARD ANY MORE, BECAUSE THE SIDE IS STRUCTURAL.</b> The static
/// had to check <c>Engine.ActiveSide</c> on every use: a listen server ran
/// both sides through one static, and a client-side
/// <c>Engine.EntityGetOrigin</c> on a handle whose entry matched a server
/// shell-less entity would have been answered out of the server's directory.
/// Now the client's <see cref="EngineSide"/> has its own route, which no
/// registry binds, so that question cannot reach the server's answers.
/// </para>
///
/// <para>
/// <b>An unbound route claims nothing, and that direction is the safe one.</b>
/// A missed route costs a crossing that answers about an entity native code has
/// never heard of, which is visible: it is counted by
/// <see cref="EngineSide.InvisibleRangeReach"/>. The other direction would answer a
/// managed zero for a real, live, networked entity.
/// </para>
/// </summary>
public sealed class ShellLessRoutes
{
    /// <summary>
    /// The bound side's answers, or null.
    /// </summary>
    /// <remarks>
    /// NAMED <c>Answers</c> AND NOT <c>Side</c>: a member called <c>Side</c>
    /// would hide the <see cref="Abi.Side"/> TYPE inside this class.
    /// </remarks>
    public IShellLessSide? Answers { get; private set; }

    /// <summary>Binds one side's answers. The registry's constructor is the one caller.</summary>
    public void Bind(IShellLessSide answers) => Answers = answers;

    /// <summary>Drops the route, and the two hierarchy sinks with it.</summary>
    public void Unbind()
    {
        Answers = null;
        ParentNameSink = null;
        ParentAttachmentNameSink = null;
    }

    /// <summary>
    /// Drops whichever of the three belong to a side that is unloading, and
    /// answers how many went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NOT <see cref="Unbind"/>.</b> While <c>Engine</c> holds the two
    /// <see cref="EngineSide"/>s statically (until plan_abi_hotreload D8), this
    /// route outlives the module that bound it, and
    /// <see cref="ManagedCaches.Forget"/> asks every side's route; the client
    /// unloading must not drop the server's.
    /// </para>
    /// <para>
    /// <b>The three are tested separately</b> even though one caller binds all
    /// three. <see cref="Answers"/> is an instance and the two sinks are
    /// delegates, so a side that bound only the sinks -- which is what a test
    /// harness does -- would otherwise keep them forever.
    /// </para>
    /// </remarks>
    internal int Forget(AssemblyLoadContext going)
    {
        int dropped = 0;

        if (Answers is not null && ManagedCaches.CameFrom(Answers.GetType(), going))
        {
            Answers = null;
            dropped++;
        }

        if (ManagedCaches.CameFrom(ParentNameSink, going))
        {
            ParentNameSink = null;
            dropped++;
        }

        if (ManagedCaches.CameFrom(ParentAttachmentNameSink, going))
        {
            ParentAttachmentNameSink = null;
            dropped++;
        }

        return dropped;
    }

    /// <summary>The route, if a side has bound one.</summary>
    public IShellLessSide? Mine => Answers;

    /// <summary>
    /// Where a shell-less entity's <c>parentname</c> goes.
    ///
    /// <para>
    /// <b>A second route rather than a method on <see cref="IShellLessSide"/>,
    /// because parenting is not the registry's.</b> The driver belongs to
    /// <c>StandaloneHierarchy</c> and not to the entity registry, so it sets
    /// this itself, where it is built, on the route its composition hands it.
    /// </para>
    ///
    /// <para>
    /// Unset is not an error and not silent: the name stays recorded on
    /// <see cref="ShellLessEntity.ParentName"/>, which is what a test reads.
    /// </para>
    /// </summary>
    public Action<EntityHandle, string>? ParentNameSink { get; set; }

    /// <inheritdoc cref="ParentNameSink"/>
    public Action<EntityHandle, string>? ParentAttachmentNameSink { get; set; }

    /// <summary>
    /// The state behind a handle when this side owns it as a shell-less
    /// entity — the ONE question every guarded engine accessor asks.
    /// </summary>
    /// <remarks>
    /// <b>The entry test comes first and it is the whole cost on the hot path.</b>
    /// A handle in an edict entry can never be one of these, and that is a
    /// compare against a constant; only a handle from 2048..8191 pays for the
    /// dictionary-free array lookup behind <see cref="IShellLessSide.StateOf"/>.
    /// </remarks>
    public ShellLessEntity? State(EntityHandle handle)
    {
        if (handle.Value == uint.MaxValue)
            return null;

        if (!handle.IsInvisibleRange)
            return null;

        return Answers?.StateOf(handle);
    }

    /// <summary>Whether this handle names a shell-less entity of this side.</summary>
    public bool Claims(EntityHandle handle) => State(handle) is not null;

    /// <summary>Whether a class of this name creates no shell at all.</summary>
    public bool ClassIsShellLess(string? className)
        => className is { Length: > 0 } && Answers is { } side && side.ClassIsShellLess(className);

    // The count behind SoundsPlacedHere. Only ever goes up.
    private long _soundsPlacedHere;

    /// <summary>
    /// <b>How many sounds were placed from a shell-less entity's OWN POSITION
    /// instead of crossing to native code to ask where it is</b> —
    /// plan_entity.md step 8e-sound.
    ///
    /// <para>
    /// <b>It is the managed twin of <c>logical.nativeReach</c>.</b> The safety
    /// claim on this half of the entity range is "a handle from the
    /// engine-invisible range never reaches native", which is a ZERO — and a
    /// build that stopped emitting these sounds altogether would report the same
    /// zero. So this is the WORK and <c>nativeReach</c> is the cost, side by
    /// side: 8 placed here and 0 reached is a result, 0 placed and 0 reached is
    /// a build that stopped doing the thing.
    /// </para>
    ///
    /// <para>
    /// It only ever goes up, so a reader compares a delta. Kept here and not
    /// cleared by <see cref="Unbind"/>.
    /// </para>
    /// </summary>
    public long SoundsPlacedHere => System.Threading.Interlocked.Read(ref _soundsPlacedHere);

    /// <inheritdoc cref="SoundsPlacedHere"/>
    internal void NoteSoundPlacedHere()
        => System.Threading.Interlocked.Increment(ref _soundsPlacedHere);

    /// <summary>
    /// <b>WHAT A NATIVE CALLER ASKS WHEN IT WANTS AN ENTITY THAT IS IN NO
    /// LIST</b> — <c>plan_entity.md</c> step 8e-entfire.
    ///
    /// <para>
    /// Every native find walks <c>CEntInfo</c>, and a shell-less entity has no
    /// C++ object to have an entry — so <c>ent_fire env_shake StartShake</c>
    /// answered "nothing is called that and nothing is of that class" in the
    /// same second <c>debug-api ents env_shake</c> reported one. This side holds
    /// the only index that has one, and the two <c>ShellLessFind*</c> ABI slots
    /// are this method behind a pointer.
    /// </para>
    ///
    /// <para>
    /// <b>SIZE-THEN-FILL, and the two numbers are deliberately different.</b>
    /// The ANSWER is how many matched, whatever <paramref name="into"/> could
    /// hold; <paramref name="into"/> takes as many as fit. An empty span is
    /// therefore the QUERY form — "does anything answer to this?" — which is
    /// what <c>ent_fire</c> asks before it queues an event, and it needs no
    /// separate entry point to be a question rather than a fetch. Returning the
    /// FILLED count instead would make an undersized buffer look like a
    /// smaller world, which is the silent truncation this step exists to end
    /// rather than to re-introduce one buffer along.
    /// </para>
    ///
    /// <para>
    /// The matching rule is the index's own, which is
    /// <c>EntityNameMatch.Match</c> — the managed spelling of
    /// <c>EntityNames_Match</c> — so a query answers the same set here as it
    /// does on the two hierarchies that have a C++ object.
    /// </para>
    /// </summary>
    /// <param name="query">A targetname or a classname, wildcards included.</param>
    /// <param name="byClassname">Which index to ask. The event queue asks them
    /// in two different places, names first, so this is a parameter and not a
    /// merged search.</param>
    /// <param name="into">Where the handles go. May be empty.</param>
    public int Find(string? query, bool byClassname, Span<uint> into)
    {
        if (query is not { Length: > 0 })
            return 0;

        if (Mine is not { } side)
            return 0;

        var found = new List<EntityHandle>();

        if (byClassname)
            side.FindByClassName(query, found);
        else
            side.FindByName(query, found);

        int fill = found.Count < into.Length ? found.Count : into.Length;

        for (int i = 0; i < fill; i++)
            into[i] = found[i].Value;

        return found.Count;
    }
}
