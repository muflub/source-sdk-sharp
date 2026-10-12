//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Passes entities heavier than a given mass.</summary>
[ScriptedEntity(EntityNames.FilterActivatorMassGreater)]
public sealed class FilterActivatorMassGreater : FilterBase
{
    public FilterActivatorMassGreater(IEngineEntityPhysics physics)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>What a map may set on this entity. See <see cref="FilterActivatorMassGreaterVars"/>.</summary>
    [ScriptVars] public FilterActivatorMassGreaterVars FilterActivatorMassGreaterVars = new();

    // Only a physics object has a mass, so a player or a trigger has nothing to
    // weigh and passes no weight filter — stock says so by asking for the
    // physics object first. The engine answers 0 for anything without one, and
    // no real physics object weighs 0, so the zero carries that same meaning
    // here. Without it a filter set to a negative mass would pass the world.
    protected override bool Matches(EntityHandle entity)
    {
        float mass = Physics.GetMass(entity);
        return mass > 0f && mass > FilterActivatorMassGreaterVars.FilterMass;
    }
}

/// <summary>
/// The map keys <see cref="FilterActivatorMassGreater"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterActivatorMassGreaterVars
{
    public float FilterMass { get; set; }
}
