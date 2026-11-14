using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Skills;

/// <summary>
/// The game's hits and kills as the skills' triggers (plan_descent_logic
/// HE6), over the core's hurt and death hooks for players and for entities
/// that run the damage rules (CR1):
/// <list type="bullet">
/// <item>a hit BY a hero: <c>on_hit</c>; with a melee weapon <c>on_melee_hit</c>,
/// and <c>on_melee_hit_airborne</c> while the hero is BlastJumping; on a player
/// off the ground <c>on_hit_airborne_target</c>. A hit a skill dealt
/// (<see cref="ISkillHits.Current"/>) carries its cast's on-hit effects to
/// the victim (EMP Charge's stun) and is no melee hit.</item>
/// <item>a kill by a hero: <c>on_melee_kill</c> with a melee weapon,
/// <c>on_kill_airborne</c> while BlastJumping.</item>
/// <item>a hit ON a hero: the rested clock (<c>no_damage_taken</c>) starts again.</item>
/// </list>
/// TF2's BlastJumping condition going on and off is the hero's BlastJumping
/// state (<c>blast_jumping</c>, and <c>blast_jump_landing</c> at its end).
/// Raised elsewhere: <c>per_10pct_health_missing</c>, <c>no_damage_taken</c> and
/// <c>dash_landing</c> by the runtime's tick. NOT raised: <c>on_direct_hit</c>
/// (a hit carries no word on whether it was the projectile's direct one) and
/// <c>on_knock_airborne</c> (a robot's knock needs the horde's bodies); and a
/// non-player victim is never known to be airborne.
/// </summary>
[SideService(Side.Server)]
public sealed class SkillTriggers(SkillRuntime runtime, SkillCasters casters, Heroes heroes, ISkillHits hits,
                                  SkillBinders binders, IEngineEntities entities, ILog log)
    : IPlayerHurtHook, IEntityHurtHook, IPlayerDeathHook, IEntityDeathHook, ITFConditionHook, IOrderedHook
{
    /// <summary>In TF2 Descent's skills block.</summary>
    public const int Order = ModStages.First + 630;

    public int HookOrder => Order;

    public void OnPlayerHurt(in PlayerHurt hurt) => Hit(hurt.Player, hurt.Attacker, hurt.Taken);

    public void OnEntityHurt(in EntityHurt hurt) => Hit(hurt.Victim, hurt.Attacker, hurt.Taken);

    public void OnPlayerDied(PlayerDeath death) => Killed(death.Player, death.Attacker);

    public void OnEntityDied(EntityDeath death) => Killed(death.Victim, death.Attacker);

    /// <summary>TF2's BlastJumping condition on a hero: the state's edges, as TF2 makes them (a jump can begin and end inside one tick's components).</summary>
    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Blastjumping && HeroIndex(player) is int index)
            runtime.SetBlastJumping(index, true);
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Blastjumping && HeroIndex(player) is int index)
            runtime.SetBlastJumping(index, false);
    }

    private void Hit(EntityHandle victim, EntityHandle attacker, int taken)
    {
        if (HeroIndex(victim) is int hurtHero)
            runtime.Hurt(hurtHero);

        if (attacker == victim || HeroIndex(attacker) is not int index || casters.Of(index) is not { } caster)
            return;

        if (hits.Current() is { } skillId)
        {
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Skills] {index} {skillId} hit #{victim.EntryIndex} for {taken}\n"));
            if (caster.LastCast.TryGetValue(skillId, out var cast))
                foreach (var effect in cast.OnHit)
                    binders.Afflict(cast, effect, victim);
            runtime.Trigger(index, "on_hit", victim);
            return;
        }

        runtime.Trigger(index, "on_hit", victim);
        if (caster.Family == "melee")
        {
            runtime.Trigger(index, "on_melee_hit", victim);
            if (caster.BlastJumping)
                runtime.Trigger(index, "on_melee_hit_airborne", victim);
        }

        if (entities.IsPlayer(victim) && binders.World.Airborne(victim))
            runtime.Trigger(index, "on_hit_airborne_target", victim);
    }

    private void Killed(EntityHandle victim, EntityHandle attacker)
    {
        if (attacker == victim || HeroIndex(attacker) is not int index || casters.Of(index) is not { } caster)
            return;

        if (caster.Family == "melee" && hits.Current() is null)
            runtime.Trigger(index, "on_melee_kill", victim);
        if (caster.BlastJumping)
            runtime.Trigger(index, "on_kill_airborne", victim);
    }

    private int? HeroIndex(EntityHandle e)
    {
        if (e == EntityHandle.None || !entities.IsPlayer(e))
            return null;
        int index = entities.GetIndex(e);
        return heroes.Of(index) is not null ? index : null;
    }
}
