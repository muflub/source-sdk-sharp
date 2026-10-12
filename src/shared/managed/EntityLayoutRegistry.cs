using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Every entity layout declared in an assembly, plus a fingerprint covering all
/// of them.
///
/// Client and server generate their datatables independently from this metadata,
/// with no shared C++ header to keep them honest — so a divergence would show up
/// only as the two sides quietly disagreeing about what bytes mean. The combined
/// fingerprint is exchanged at join so a divergence is caught there instead.
/// </summary>
public sealed class EntityLayoutRegistry
{
    /// <param name="log">Where a layout disagreement is said (statics to zero).</param>
    public EntityLayoutRegistry(ILog log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    private readonly ILog _log;

    private IReadOnlyList<EntityLayout> _layouts = Array.Empty<EntityLayout>();

    /// <summary>Layouts in discovery order, which is declaration order.</summary>
    public IReadOnlyList<EntityLayout> Layouts => _layouts;

    /// <summary>Number of layouts discovered, sent alongside the fingerprint.</summary>
    public int Count => _layouts.Count;

    /// <summary>
    /// Fingerprint over every layout, in order. Computed once during
    /// <see cref="DiscoverAll"/> rather than on each read: it goes on the wire at
    /// every join, and recomputing it would mean rehashing every field again for
    /// a value that cannot change until the next discovery.
    /// </summary>
    public uint Fingerprint { get; private set; }

    /// <summary>
    /// The SECOND hash on the join wire: the engine block's layout
    /// (plan_entity.md §3.2), covering the struct declared once in C# and once
    /// in C++.
    ///
    /// <para>
    /// Not part of <see cref="Fingerprint"/> and deliberately so. That one
    /// covers the per-entity layouts discovered by reflection and moves when a
    /// mod adds an entity; this one covers a single fixed struct the ENGINE
    /// reads, and a mismatch in it means something different — not "these two
    /// builds disagree about an entity" but "these two builds disagree about
    /// the memory the shell getters load from". Folding them together would
    /// leave the refusal unable to say which.
    /// </para>
    ///
    /// <para>
    /// A build-wide constant, not a per-registry value: it does not depend on
    /// what was discovered. It reads through the registry because the registry
    /// is what the join handshake already holds.
    /// </para>
    /// </summary>
    public uint BlockFingerprint => EngineBlockFingerprint.Current;

    /// <summary>
    /// Discovers every type with at least one [EntityField] property, in every
    /// assembly loaded beside <paramref name="assembly"/>, builds its layout,
    /// and computes the combined fingerprint.
    ///
    /// Ordered by assembly name and then metadata token — source declaration
    /// order — for the same reason the fields within a layout are: reflection
    /// order carries no guarantee, and the fingerprint depends on the sequence.
    ///
    /// <paramref name="assembly"/> is the assembly both sides share. A layout
    /// found outside it goes into the fingerprint on one side only, which is
    /// precisely the divergence the fingerprint exists to catch — so it is
    /// warned about where it is cheap to fix rather than at a refused join.
    /// </summary>
    public int DiscoverAll(Assembly assembly)
    {
        var found = new List<Type>();

        foreach (var type in LoadedAssemblies.Types(assembly, _log))
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            // Fields as well as properties: the blittable-struct form declares
            // fields, and missing those would silently drop a whole entity's
            // layout from the fingerprint — which is the one thing this check
            // exists to catch.
            if (!HasEntityField(type))
                continue;

            found.Add(type);
        }

        return Discover(found);
    }

    /// <summary>
    /// Builds layouts for exactly <paramref name="types"/>, in key order
    /// whatever order they arrive in: what <see cref="DiscoverAll"/> does after
    /// its scan.
    /// </summary>
    internal int Discover(IEnumerable<Type> types)
    {
        var found = new List<Type>(types);

        // KEY ORDER (plan_side_isolation §2.1): the fingerprint is a hash over
        // this sequence, so it is ordered by the type's full name, which both
        // sides share, rather than by the assembly it was compiled into.
        found.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

        var layouts = new List<EntityLayout>(found.Count);
        foreach (var type in found)
            layouts.Add(EntityLayout.Build(type));

        _layouts    = layouts;
        Fingerprint = ComputeFingerprint(found, layouts);

        return _layouts.Count;
    }

    private static bool HasEntityField(Type type)
    {
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.GetCustomAttribute<EntityFieldAttribute>() is not null)
                return true;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetCustomAttribute<EntityFieldAttribute>() is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Mixes each layout's own fingerprint together with its type name, so two
    /// layouts with identical shapes but belonging to different entities stay
    /// distinguishable.
    /// </summary>
    private static uint ComputeFingerprint(List<Type> types, List<EntityLayout> layouts)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime       = 16777619u;

        uint hash = offsetBasis;

        for (int i = 0; i < types.Count; i++)
        {
            foreach (char c in types[i].Name)
            {
                hash ^= (byte)c;
                hash *= prime;
            }

            uint fp = layouts[i].Fingerprint;
            for (int b = 0; b < 4; b++)
            {
                hash ^= (byte)(fp >> (b * 8));
                hash *= prime;
            }
        }

        return hash;
    }

    /// <summary>
    /// Compares the pair of fingerprints received from the other side and
    /// reports the result. Returns true when BOTH agree.
    ///
    /// <para>
    /// Both, not either: the two hashes cover different things (see
    /// <see cref="BlockFingerprint"/>) and a build can get one right and the
    /// other wrong. Accepting on the layout hash alone would make the block
    /// hash decorative, which is the failure mode the block hash was added
    /// early to avoid.
    /// </para>
    /// </summary>
    public bool Verify(string side, uint theirFingerprint, int theirCount, uint theirBlockFingerprint)
    {
        uint mine      = Fingerprint;
        uint mineBlock = BlockFingerprint;

        if (mine == theirFingerprint && theirCount == Count && mineBlock == theirBlockFingerprint)
        {
            _log.Msg($"[SourceSharp/{side}] entity layout check OK: " +
                       $"{Count} layout(s), fingerprint 0x{mine:X8}, " +
                       $"engine block 0x{mineBlock:X8}\n");
            return true;
        }

        // Loud rather than fatal: the mismatch means every replicated field on
        // these entities is suspect, but killing the process gives less to go on
        // than naming both fingerprints does.
        _log.Warning(
            $"[SourceSharp/{side}] ENTITY LAYOUT MISMATCH — client and server " +
            $"generated different datatables.\n" +
            $"  mine:  {Count} layout(s), fingerprint 0x{mine:X8}, " +
            $"engine block 0x{mineBlock:X8}\n" +
            $"  other: {theirCount} layout(s), fingerprint 0x{theirFingerprint:X8}, " +
            $"engine block 0x{theirBlockFingerprint:X8}\n" +
            $"  Replicated entity state will be misread. The two sides must be " +
            $"built from the same SourceSharp.Shared.\n");
        return false;
    }
}
