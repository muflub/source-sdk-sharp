using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// One hit that did damage to an entity that runs the damage rules
/// (<see cref="ScriptedEntity.RunsDamageRules"/>): who, by whom, how much the
/// entity answered it took (after the <see cref="IDamageModifier"/> fold and
/// its own rule), the health that leaves floored at zero, what kind of damage
/// it was, and whether the shot was critical.
/// </summary>
public readonly record struct EntityHurt(EntityHandle Victim, EntityHandle Attacker, int Taken, int HealthLeft,
                                         DamageType Kind = default, bool Critical = false);

/// <summary>
/// Told of each damaging hit on an entity that runs the damage rules, once
/// the entity has answered what it took, on the game thread:
/// <see cref="IPlayerHurtHook"/>'s twin for a victim that is not a player. A
/// hit the entity took nothing of is not told. Order among several is
/// <see cref="IOrderedHook"/>'s.
/// </summary>
public interface IEntityHurtHook
{
    void OnEntityHurt(in EntityHurt hurt);
}

/// <summary>One death of an entity that runs the damage rules, as <see cref="IEntityDeathHook"/> is told it.</summary>
/// <param name="Victim">The entity whose health ran out.</param>
/// <param name="Attacker">The killer; not a valid entity when the world killed it.</param>
/// <param name="Origin">Where it died, read before its own <see cref="ScriptedEntity.OnKilled"/> could move it.</param>
public readonly record struct EntityDeath(EntityHandle Victim, EntityHandle Attacker, Vector Origin);

/// <summary>
/// The death of an entity that runs the damage rules, as a hook:
/// <see cref="IPlayerDeathHook"/>'s twin for a victim that is not a player,
/// called once per death, after the entity's own
/// <see cref="ScriptedEntity.OnKilled"/>. Order among several is
/// <see cref="IOrderedHook"/>'s.
/// </summary>
public interface IEntityDeathHook
{
    void OnEntityDied(EntityDeath death);
}
