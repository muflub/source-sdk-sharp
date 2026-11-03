//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// What a round's rules keep across a map cleanup: stock's two virtuals,
/// <c>RoundCleanupShouldIgnore</c> (a live entity the cleanup leaves standing)
/// and <c>ShouldCreateEntity</c> (a map class it creates again). The core's
/// answer is <see cref="HL2MPMapCleanupFilter"/>; a mode with rules of its own
/// hands its own to <see cref="IMapCleanup.CleanUpMap"/> (TF2's is the
/// teamplay list with TF's).
/// </summary>
public interface IMapCleanupFilter
{
    /// <summary>True when <paramref name="entity"/> stays standing through the cleanup.</summary>
    bool RoundCleanupShouldIgnore(ScriptedEntity entity);

    /// <summary>True when a map block of <paramref name="className"/> is created again.</summary>
    bool ShouldCreateEntity(string className);
}

/// <summary>
/// A mod's own additions to whichever filter a cleanup runs with (D10
/// layering): every class a hook names is kept standing and not created again,
/// as if the round's preserve list named it. TF2's list is its rules'; a mod
/// layered on it (Descent) adds its own here rather than replacing that list.
/// </summary>
public interface IMapCleanupPreserve
{
    /// <summary>True when every entity of <paramref name="className"/> is preserved.</summary>
    bool Preserves(string className);
}

/// <summary>What one <see cref="IMapCleanup.CleanUpMap"/> did, with stock's <c>mp_showcleanedupents</c> counts.</summary>
public readonly record struct MapCleanupReport(
    int EntitiesBefore, int EdictsBefore, int Removed, int Kept, int EventsCleared,
    int Freed, MapRespawn Respawn, int EntitiesAfter, int EdictsAfter);

/// <summary>Stock's <c>CleanUpMap</c>: the round reset that brings the map back as it loaded.</summary>
public interface IMapCleanup
{
    /// <summary>Removes every entity the filter does not keep and creates the map's entities again.</summary>
    MapCleanupReport CleanUpMap(IMapCleanupFilter filter);
}

/// <summary>
/// <b>Stock's <c>CleanUpMap</c></b> — <c>CTeamplayRoundBasedRules::CleanUpMap</c>
/// (teamplayroundbased_gamerules.cpp) and <c>CHL2MPRules::CleanUpMap</c>
/// (hl2mp_gamerules.cpp), which are one sequence over two preserve lists (tf2
/// ruling N2):
///
/// <list type="number">
/// <item><description>every entity the filter does not keep is removed
/// (<c>UTIL_Remove</c>: marked, so its teardown runs as for any removal, and
/// as for a level change);</description></item>
/// <item><description>the event queue is cleared (<c>g_EventQueue.Clear</c>),
/// so no output a removed entity queued reaches the new map;</description></item>
/// <item><description>the marked entities are really freed
/// (<c>gEntList.CleanupDeleteList</c>), which frees their edicts;</description></item>
/// <item><description><c>engine-&gt;AllowImmediateEdictReuse()</c>, so those
/// edicts can be handed out at once;</description></item>
/// <item><description>the map is parsed again with the filter
/// (<c>MapEntity_ParseAllEntities</c> over the map entity string), each entity
/// into the edict it had at load when that edict is free
/// (<see cref="MapSpawner.Respawn"/>).</description></item>
/// </list>
///
/// <para>
/// <c>PurgeDeferredPooledStrings</c>, between 3 and 4 in the teamplay order, has
/// nothing to purge here: the pool's deferred list had no writer and went
/// (gamestringpool.cpp). The players are never walked: they are not in the
/// directory as map entities and every list keeps them. The world is native and
/// kept by every list (<c>worldspawn</c>).
/// </para>
///
/// <para>
/// The event queue is cleared at the teamplay position (before the delete);
/// HL2MP clears it after. An event a removed entity's own teardown queues
/// therefore survives here and would not in HL2MP (the divergence row).
/// </para>
/// </summary>
public sealed class MapCleanup(EntityDirectory directory, MapSpawner spawner, IEngineEntities entities,
                               IEdictSlots slots, ILog log,
                               IHooks<IMapCleanupPreserve>? preserveHooks = null) : IMapCleanup
{
    /// <inheritdoc/>
    public MapCleanupReport CleanUpMap(IMapCleanupFilter filter)
    {
        IReadOnlyList<IMapCleanupPreserve> preserve = preserveHooks?.All ?? [];

        int entitiesBefore = Entities();
        int edictsBefore = Edicts();

        // ---- 1. Get rid of every entity the rules do not keep. -------------
        int removed = 0, kept = 0;
        foreach (var entity in directory.Snapshot())
        {
            if (filter.RoundCleanupShouldIgnore(entity) || Preserved(preserve, entity.ClassName))
            {
                kept++;
                continue;
            }

            if (directory.Remove(entity.Handle) == RemovalMark.Marked)
                removed++;
        }

        // ---- 2. Clear out the event queue. ---------------------------------
        int events = directory.Io.Count;
        directory.Io.Clear();

        // ---- 3. Really remove the entities, so their slots are free below. -
        long freedBefore = directory.RemovedTotal;
        entities.CleanupDeleteList();
        int freed = (int)(directory.RemovedTotal - freedBefore);

        // ---- 4. The freed edicts may be reused at once. --------------------
        slots.AllowImmediateReuse();

        // ---- 5. Now reload the map entities. -------------------------------
        var respawn = spawner.Respawn(
            className => filter.ShouldCreateEntity(className) && !Preserved(preserve, className),
            slots.InUse);

        var report = new MapCleanupReport(entitiesBefore, edictsBefore, removed, kept, events, freed,
                                          respawn, Entities(), Edicts());

        log.Msg($"[SourceSharp/MapCleanup] CleanUpMap: {report.EntitiesBefore} entities ({report.EdictsBefore} edicts); "
              + $"removed {report.Removed}, kept {report.Kept}, {report.EventsCleared} queued event(s) cleared; "
              + $"re-created {respawn.Created} map entities ({respawn.AtOldSlot} in their old edict, "
              + $"{respawn.Elsewhere} in another, {respawn.Unslotted} with none at load; {respawn.Skipped} preserved, "
              + $"{respawn.Failed} failed); "
              + $"{report.EntitiesAfter} entities ({report.EdictsAfter} edicts)\n");

        return report;
    }

    // stock's NumberOfEntities and NumberOfEdicts: the world is one of each and
    // never in the directory; an entity has an edict when its entry is visible.
    private int Entities() => directory.Count + 1;

    private int Edicts() => directory.Snapshot().Count(e => !e.Handle.IsInvisibleRange) + 1;

    private static bool Preserved(IReadOnlyList<IMapCleanupPreserve> preserve, string className)
    {
        foreach (var hook in preserve)
        {
            if (hook.Preserves(className))
                return true;
        }

        return false;
    }
}
