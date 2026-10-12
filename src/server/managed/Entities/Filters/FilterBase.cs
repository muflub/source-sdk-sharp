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
/// The filter entities, in C#.
///
/// A filter answers one question — does this entity count? — and other entities
/// ask it by name. That indirection is the whole point: a mapper wires one
/// filter to many triggers, and changes who they all respond to in one place.
///
/// Nothing here acts on the world. A filter has no position that matters, no
/// model and no think; it is a predicate a map can name.
/// </summary>
public abstract class FilterBase : ScriptedEntity
{
    public readonly Output OnPass = new();
    public readonly Output OnFail = new();

    /// <summary>What a map may set on this entity. See <see cref="FilterBaseVars"/>.</summary>
    [ScriptVars] public FilterBaseVars Vars = new();

    /// <summary>
    /// Whether this filter is inverted. Only for a subclass whose own criteria
    /// have to know — filter_enemy asks each of its tests to come out the way
    /// the mapper asked for, rather than inverting one answer at the end.
    /// </summary>
    protected bool Negated => Vars.Negated;

    /// <summary>The question itself, before negation.</summary>
    protected abstract bool Matches(EntityHandle entity);

    /// <summary>
    /// Whether an entity passes. This is what other entities call, and it is
    /// where negation is applied — a subclass answers its own question and
    /// never has to think about being inverted.
    /// </summary>
    public bool Passes(EntityHandle entity)
    {
        bool matched = Matches(entity);
        return Vars.Negated ? !matched : matched;
    }

    /// <summary>
    /// The damage question, and it is a different one: a damagefilter is asked
    /// about a blow, not about an entity.
    ///
    /// <para>
    /// The default answers it by asking about whoever struck — which is stock's
    /// own rule, not a convenience. <c>CBaseFilter::PassesDamageFilterImpl</c>
    /// runs the entity test against the attacker, so naming a
    /// filter_activator_name in <c>damagefilter</c> has always meant "damage
    /// from them", and only the filters that genuinely inspect the blow —
    /// filter_damage_type — override it.
    /// </para>
    /// </summary>
    public bool PassesDamage(DamageType damageType, EntityHandle attacker)
    {
        bool matched = MatchesDamage(damageType, attacker);
        return Vars.Negated ? !matched : matched;
    }

    /// <summary>The damage question itself, before negation.</summary>
    protected virtual bool MatchesDamage(DamageType damageType, EntityHandle attacker) => Matches(attacker);

    /// <summary>Asks the filter about an entity and reports the answer, for a map that wants to branch on it.</summary>
    [ScriptedEvent("TestActivator")]
    private void OnTestActivator(InputData input)
    {
        if (Passes(input.Activator))
            OnPass.Fire();
        else
            OnFail.Fire();
    }
}

/// <summary>
/// The map keys <see cref="FilterBase"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterBaseVars
{
    /// <summary>Inverts the answer. A filter that says "everyone except" is the same filter negated.</summary>
    public bool Negated { get; set; }
}
