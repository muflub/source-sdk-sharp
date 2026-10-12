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
/// Singles out one enemy, by name or class, and optionally only while it is
/// close enough to matter.
///
/// Stock this is an NPC's question, asked while it picks who to fight, and it
/// reads backwards until that is clear: anything that is NOT the named enemy
/// passes untouched, because the filter exists to constrain one target and say
/// nothing about the rest. That is kept, because a map written against stock
/// depends on it.
///
/// What is not kept is the AI half. There are no NPCs in this mod, so nothing
/// has an acquired enemy or a squad: the crowding limit, the never-lose-a-target
/// spawnflag and the hysteresis the outer radius exists for all need a fight
/// already in progress, and there is none. The distance is measured from the
/// filter entity instead of from whoever asked, which is a position the mapper
/// placed on purpose and the only one this side of the boundary knows.
/// </summary>
[ScriptedEntity(EntityNames.FilterEnemy)]
public sealed class FilterEnemy : FilterBase
{
    /// <summary>What a map may set on this entity. See <see cref="FilterEnemyVars"/>.</summary>
    [ScriptVars] public FilterEnemyVars FilterEnemyVars = new();

    private const string PlayerName = "!player";

    // Each test below answers "did this criterion come out the way Negated
    // asked for", and FilterBase inverts the whole answer afterwards — the same
    // double application stock does, kept so that a map's Negated means what it
    // meant before.
    protected override bool Matches(EntityHandle entity)
    {
        if (!PassesName(entity))
            return true;

        return PassesProximity(entity);
    }

    private bool PassesName(EntityHandle entity)
    {
        if (FilterEnemyVars.EnemyName.Length == 0)
            return true;

        // A player has no targetname to match, so the map writes !player and
        // the filter recognises it. Anything that is not a player falls through
        // to the ordinary compare, where it will not match.
        if (string.Equals(FilterEnemyVars.EnemyName, PlayerName, StringComparison.OrdinalIgnoreCase) && Entities.IsPlayer(entity))
            return !Negated;

        bool matched = string.Equals(Entities.GetName(entity), FilterEnemyVars.EnemyName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Entities.GetClassName(entity), FilterEnemyVars.EnemyName, StringComparison.OrdinalIgnoreCase);

        return Negated ? !matched : matched;
    }

    /// <summary>
    /// Whether the entity is near enough. Two radii rather than one because an
    /// NPC that had already picked its fight kept it until the enemy got
    /// properly far away, and one radius makes that decision flicker on the
    /// boundary. Nothing holds an enemy here, so the candidate is never the
    /// already-acquired one and the near radius always decides — the outer one
    /// still takes part, because writing it smaller than the inner one swaps
    /// the pair and that is how a mapper reverses which is which.
    /// </summary>
    private bool PassesProximity(EntityHandle entity)
    {
        if (FilterEnemyVars.Radius <= 0f)
            return true;

        float larger = FilterEnemyVars.OuterRadius;
        if (larger == 0f)
            larger = Negated ? FilterEnemyVars.Radius * 0.5f : FilterEnemyVars.Radius * 2.0f;

        float smaller = FilterEnemyVars.Radius;
        if (smaller > larger)
            (smaller, larger) = (larger, smaller);

        float distance = Negated ? larger : smaller;
        bool within = (Origin - Entities.GetOrigin(entity)).LengthSquared <= distance * distance;

        return Negated ? !within : within;
    }
}

/// <summary>
/// The map keys <see cref="FilterEnemy"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterEnemyVars
{
    /// <summary>A targetname or a classname. "!player" means any player.</summary>
    [VarName("filtername")]
    public string EnemyName { get; set; } = string.Empty;

    /// <summary>How close counts. Zero measures no distance at all.</summary>
    [VarName("filter_radius")]
    public float Radius { get; set; }

    /// <summary>The far edge of the hysteresis pair. See <see cref="PassesProximity"/>.</summary>
    [VarName("filter_outer_radius")]
    public float OuterRadius { get; set; }

    /// <summary>How many squadmates may share one enemy. Inert: nothing here has a squad.</summary>
    [VarName("filter_max_per_enemy")]
    public int MaxPerEnemy { get; set; }
}
