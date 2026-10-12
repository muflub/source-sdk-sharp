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
/// Passes damage of a given type. Asked by things that take damage rather than
/// by triggers, which is why it compares a bit rather than an entity.
/// </summary>
[ScriptedEntity(EntityNames.FilterDamageType)]
public sealed class FilterDamageType : FilterBase
{
    /// <summary>What a map may set on this entity. See <see cref="FilterDamageTypeVars"/>.</summary>
    [ScriptVars] public FilterDamageTypeVars FilterDamageTypeVars = new();

    // The one filter that reads the blow rather than who threw it, which is why
    // it is the only one that overrides this.
    protected override bool MatchesDamage(DamageType damageType, EntityHandle attacker)
        // ANY of the kinds the mapper named, not all of them: a blow that
        // is bullet AND club must pass a filter set to either. Not HasFlag,
        // which would demand every bit the keyvalue holds.
        => (damageType & FilterDamageTypeVars.DamageType) != 0;

    // An entity is not a damage type, so the entity question has no meaningful
    // answer here. Said plainly rather than guessed at.
    protected override bool Matches(EntityHandle entity) => false;
}

/// <summary>
/// The map keys <see cref="FilterDamageType"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterDamageTypeVars
{
    /// <summary>
    /// Source's DMG_* bits, as the map writes them — the enum itself since plan
    /// Step 12, so the numbers a mapper typed are named where they are read.
    ///
    /// A map that sets a DMG_ bit this tree has not named still gets it: a flags
    /// keyvalue is a bitfield and every bit of it is kept, which is why
    /// ScriptedVarParse.Enum refuses an undefined number only for an enum of
    /// alternatives.
    /// </summary>
    public DamageType DamageType { get; set; }
}
