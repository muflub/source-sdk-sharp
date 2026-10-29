//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// CBaseEntity::TraceAttack, for an entity that answers a traced hit itself
/// rather than through TakeDamage: stock's CBreakableSurface, whose panes a
/// bullet shatters whatever its health and its take-damage mode say. A shot
/// that meets one hands it the hit instead of damaging it.
/// </summary>
public interface ITraceAttacked
{
    /// <summary>The traced hit: from <paramref name="start"/>, landing at <paramref name="end"/>.</summary>
    void TraceAttack(EntityHandle attacker, float damage, DamageType damageType, Vector start, Vector end);

    /// <summary>
    /// TraceAttackToTriggers' test: whether this is a not-solid trigger the
    /// ray from <paramref name="start"/> to <paramref name="end"/> crosses, and where.
    /// </summary>
    bool ShotThrough(Vector start, Vector end, out Vector hit);
}

/// <summary>What a traced shot does to the entities that answer one (<see cref="ITraceAttacked"/>).</summary>
public static class TraceAttacks
{
    /// <summary>
    /// CBaseEntity::TraceAttackToTriggers: every not-solid trigger along the
    /// shot's ray takes the hit where the ray crosses it.
    /// </summary>
    public static void ToTriggers(IReadOnlyList<ITraceAttacked> triggers, EntityHandle attacker, float damage,
                                  DamageType damageType, Vector start, Vector end)
    {
        for (int i = 0; i < triggers.Count; i++)
        {
            if (triggers[i].ShotThrough(start, end, out Vector hit))
                triggers[i].TraceAttack(attacker, damage, damageType, start, hit);
        }
    }
}
