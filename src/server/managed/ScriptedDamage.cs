using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The damage path of an entity that runs the damage rules
/// (<see cref="ScriptedEntity.RunsDamageRules"/>): what the registry's damage
/// and death routes do for it in place of the bare call.
///
/// <para>
/// <b>A HIT</b> is folded through the mods' <see cref="IDamageModifier"/>
/// chain (<see cref="IGameRules.ModifyDamage"/>, the fold a player's own
/// damage path asks) before the entity's
/// <see cref="ScriptedEntity.OnDamagedAt"/> is handed the number, so the
/// entity's own rule starts from the hit the mode's rules made of it. What
/// the entity answers is what it takes, as for any other; a hit it took
/// something of is then told to the <see cref="IEntityHurtHook"/> chain. A
/// fold that leaves nothing hands the entity zero.
/// </para>
///
/// <para>
/// <b>A DEATH</b> runs the entity's own <see cref="ScriptedEntity.OnKilled"/>
/// and then tells the <see cref="IEntityDeathHook"/> chain, with the origin
/// read first: the entity's own dying may move or park it.
/// </para>
///
/// <para>
/// The mods' <see cref="IDamageRule"/> chain is not asked: it is the player's
/// refusal (spawn protection, friendly fire, a mode's invulnerability), and
/// whether an entity takes a hit at all is its own answer.
/// </para>
/// </summary>
public sealed class ScriptedDamage(IGameRules rules, IEngineEntities entities, DamageHitGroups hits,
                                   IHooks<IEntityHurtHook> hurtHooks, IHooks<IEntityDeathHook> deathHooks)
{
    /// <summary>Routes one hit: the fold, the entity's answer, then the hurt hooks. Answers what the entity took.</summary>
    public int Damaged(ScriptedEntity victim, EntityHandle attacker, float damage, DamageType damageType,
                       Vector position, Vector force, HitGroup hitGroup)
    {
        float folded = rules.ModifyDamage(victim.Handle, attacker, damage, (int)damageType, hitGroup);

        // Zero or less is no damage (IDamageModifier); a NaN is none either.
        if (!(folded > 0f))
            folded = 0f;

        int taken = victim.OnDamagedAt(attacker, folded, damageType, position, force, hitGroup);

        if (taken > 0 && hurtHooks.All.Count > 0)
        {
            // The shell subtracts the answer from the health once this
            // returns (CStandaloneEntity::TakeDamage), so the health now is
            // the health before the hit.
            var hurt = new EntityHurt(victim.Handle, attacker, taken,
                                      Math.Max(0, entities.GetHealth(victim.Handle) - taken),
                                      damageType, hits.Critical);
            hurtHooks.Each(nameof(IEntityHurtHook.OnEntityHurt), h => h.OnEntityHurt(in hurt));
        }

        return taken;
    }

    /// <summary>Routes one death: the entity's own, then the death hooks.</summary>
    public void Killed(ScriptedEntity victim, EntityHandle attacker)
    {
        // WHERE IT DIED, read before anything else: the entity's own dying may
        // move it, park it or mark it for removal.
        Vector origin = entities.GetOrigin(victim.Handle);

        victim.OnKilled(attacker);

        if (deathHooks.All.Count > 0)
        {
            var death = new EntityDeath(victim.Handle, attacker, origin);
            deathHooks.Each(nameof(IEntityDeathHook.OnEntityDied), h => h.OnEntityDied(death));
        }
    }
}
