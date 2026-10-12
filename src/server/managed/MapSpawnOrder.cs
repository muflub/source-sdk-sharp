//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Frozen;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The order a map's entities spawn in — stock's classname priority list from
/// <c>SortSpawnListByHierarchy</c> (<c>game/server/mapentities.cpp</c>), moved
/// to the side of the boundary that decides things.
///
/// <para>
/// <b>Why it had to move: the list had gone quiet.</b> Stock builds a
/// <c>CStringRegistry</c> of thirteen classnames and uses it to break ties in
/// <c>CompareSpawnOrder</c>, over <c>pSpawnList</c> — the CBaseEntity spawn
/// list. Eleven of those thirteen classnames are managed entities now, and a
/// managed entity is never in that list: it is queued in
/// <c>g_PendingNonBaseSpawns</c> and spawned, in MAP-FILE ORDER, after
/// everything else. So the tie-break still ran and no longer had anything to
/// break, and nothing said so.
/// </para>
///
/// <para>
/// <b>Measured on the two shipped maps</b>, counting pairs where an entity stock
/// spawns LATER appears earlier in the map file:
/// </para>
///
/// <code>
/// dm_lockdown  605 entities, 100 prop_physics (7), phys_hinge + phys_lengthconstraint (8)
///              189 inverted pairs
/// ss_sandbox  1005 entities,  49 prop_physics, 9 constraint-family, 1 func_wall (10)
///              183 inverted pairs
/// </code>
///
/// <para>
/// <b>The values are stock's, verbatim, including the two it comments.</b> They
/// are not a scale anybody designed — they are an ordering, and the only thing
/// that matters about them is which is larger. HIGHER SPAWNS FIRST, which is
/// stock's own comment on the list ("Highest string ID spawns first") and the
/// opposite of what the comparison function's return values read like at a
/// glance.
/// </para>
/// </summary>
public sealed class MapSpawnOrder : IServerMapSpawnOrder
{
    /// <summary>
    /// What a classname that is not in the table sorts as. Stock's is stated in
    /// its own comment — "by default, anything not in this list has priority
    /// -1" — and it is what <c>CompareSpawnOrder</c> also uses for an entity
    /// that has already gone.
    /// </summary>
    public const int Unlisted = -1;

    private static readonly FrozenDictionary<string, int> s_priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        // Stock's list, in stock's order, with stock's two comments.
        ["func_wall"] = 10,
        ["scripted_sequence"] = 9,
        ["phys_hinge"] = 8,
        ["phys_ballsocket"] = 8,
        ["phys_slideconstraint"] = 8,
        ["phys_constraint"] = 8,
        ["phys_pulleyconstraint"] = 8,
        ["phys_lengthconstraint"] = 8,
        ["phys_ragdollconstraint"] = 8,
        ["info_mass_center"] = 8,          // spawn these before physbox/prop_physics
        ["trigger_vphysics_motion"] = 8,   // spawn these before physbox/prop_physics
        ["prop_physics"] = 7,
        ["prop_ragdoll"] = 7,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// CASE-INSENSITIVE, unlike the global state table's name lookup, and the
    /// difference is stock's rather than an inconsistency: this reads through
    /// <c>CStringRegistry::GetStringID</c>, which lowercases both sides
    /// (<c>public/stringregistry.cpp</c>), where the global state table compares
    /// interned <c>CUtlSymbol</c>s built case-sensitively.
    /// </summary>
    public int PriorityOf(string? className)
        => className is not null && s_priority.TryGetValue(className, out int p) ? p : Unlisted;

    /// <inheritdoc/>
    public int Order(IReadOnlyList<string?> classNames, Span<int> order)
    {
        if (order.Length < classNames.Count)
            return classNames.Count;

        // STABLE, and that is the whole of the second half of stock's rule.
        // CompareSpawnOrder answers 0 for two entities of equal depth and equal
        // priority, and qsort with a comparison that answers 0 leaves them
        // where they were -- which for a map load means MAP-FILE ORDER. An
        // unstable sort here would shuffle the 400-odd unlisted entities of a
        // real map against each other on every load, which is a far bigger
        // change than the one being made.
        //
        // OrderByDescending is documented stable, so equal priorities keep the
        // index order they came in with.
        int n = 0;
        foreach (int i in Enumerable.Range(0, classNames.Count)
                                    .OrderByDescending(i => PriorityOf(classNames[i])))
        {
            order[n++] = i;
        }

        return n;
    }
}
