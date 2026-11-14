using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Descent.Server.Skills;

/// <summary>Where a hero's cast starts and the basis it fires along: the eyes and the aim.</summary>
public readonly record struct SkillAim(Vector Eyes, Vector Forward, Vector Right, Vector Up);

/// <summary>
/// One projectile a skill fires: its TF class, the hero it is fired by, where
/// and how fast, what its hit is worth, the team it flies for, and the weapon
/// name its damage is dealt as (the skill's own, <see cref="SkillHits.WeaponNameOf"/>,
/// so no carried item's attributes are read for it: R17).
/// </summary>
public readonly record struct SkillShot(string ClassName, EntityHandle Owner, Vector Origin, Vector Velocity,
                                        float Damage, int Team, string WeaponName);

/// <summary>
/// The engine work a skill's binders do (plan_descent_logic HE6), behind one
/// interface so each binder has a fact over a fake world. Every member is one
/// seam the TF2 port already has; nothing here decides.
/// </summary>
public interface ISkillWorld
{
    /// <summary>The server tick now.</summary>
    int Tick { get; }

    /// <summary>Seconds a server tick.</summary>
    float TickInterval { get; }

    SkillAim AimOf(EntityHandle player);

    Vector OriginOf(EntityHandle entity);

    int TeamOf(EntityHandle player);

    bool IsPlayer(EntityHandle entity);

    bool IsAlive(EntityHandle player);

    /// <summary>Off the ground and dry: TF's airborne.</summary>
    bool Airborne(EntityHandle player);

    /// <summary>The player carries TF's BlastJumping condition.</summary>
    bool BlastJumping(EntityHandle player);

    /// <summary>The item class of the weapon in the hands (the item's, else the entity's); null with none.</summary>
    string? ActiveItemClass(EntityHandle player);

    int Health(EntityHandle player);

    int MaxHealth(EntityHandle player);

    /// <summary>A projectile through TF2's spawner; <see cref="EntityHandle.None"/> when nothing was fired.</summary>
    EntityHandle Spawn(in SkillShot shot);

    /// <summary>A line as a bullet sees the world, passing <paramref name="pass"/>.</summary>
    TraceResult Trace(Vector start, Vector end, EntityHandle pass);

    /// <summary>One hit through the damage rules, dealt as <paramref name="weaponName"/>.</summary>
    void Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType type, Vector force, Vector at,
              string weaponName);

    /// <summary>A blast through TF2's blast applier (its falloff, its pushes), dealt as <paramref name="weaponName"/>.</summary>
    void Blast(Vector origin, float damage, float radius, EntityHandle attacker, string weaponName);

    /// <summary>What can be hurt within <paramref name="radius"/>: players, and entities that run the damage rules.</summary>
    IReadOnlyList<EntityHandle> Near(Vector origin, float radius);

    bool AddCondition(EntityHandle player, TFCond cond, float duration, EntityHandle provider);

    void RemoveCondition(EntityHandle player, TFCond cond);

    /// <summary>A velocity impulse that lets go of the ground (a launch).</summary>
    void Push(EntityHandle player, Vector impulse);

    /// <summary>The player is rocket jumping as TF2 counts one (its BlastJumping state, cleared on landing).</summary>
    void BeginBlastJump(EntityHandle player);

    /// <summary>Life back, up to the maximum; what was healed.</summary>
    int Heal(EntityHandle player, float amount);
}

/// <summary>The engine's <see cref="ISkillWorld"/>: TF2's spawner, blast applier, conditions and resupply.</summary>
[SideService(Side.Server, As = [typeof(ISkillWorld)])]
public sealed class EngineSkillWorld(IEngineEnvironment environment, IEngineEntities entities, IEnginePlayers players,
                                     IEngineWorldQuery world, IEngineDamage damage, IEntityDirectory directory,
                                     ITFProjectileSpawner spawner, TFBlastApplier blasts, TF2.Server.Combat.TFDamageScopes scopes,
                                     ITFPlayerConditions conditions, ITFPlayerResupply resupply, ITFWeaponOwners owners,
                                     TFPlayerCarriers carriers)
    : ISkillWorld
{
    private readonly EntityHandle[] _near = new EntityHandle[256];

    public int Tick => environment.Globals is { IsAvailable: true } g ? g.TickCount : 0;

    public float TickInterval => environment.Globals is { IsAvailable: true } g ? g.IntervalPerTick : 0.015f;

    public SkillAim AimOf(EntityHandle player)
    {
        Vector forward = Angles.Forward(entities.GetEyeAngles(player));
        (Vector right, Vector up) = Angles.RightUp(forward);
        return new SkillAim(entities.GetEyePosition(player), forward, right, up);
    }

    public Vector OriginOf(EntityHandle entity) => entities.GetOrigin(entity);

    public int TeamOf(EntityHandle player) => players.GetTeam(player);

    public bool IsPlayer(EntityHandle entity) => entities.IsPlayer(entity);

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public bool Airborne(EntityHandle player) => owners.OffGround(entities.GetIndex(player));

    public bool BlastJumping(EntityHandle player) => conditions.InCond(player, TFCond.Blastjumping);

    public string? ActiveItemClass(EntityHandle player)
    {
        if (owners.ActiveItemOf(entities.GetIndex(player)) is AttributeContainer { Item.ItemClass: { Length: > 0 } itemClass })
            return itemClass;
        string name = players.GetActiveWeapon(player);
        return name.Length > 0 ? name : null;
    }

    public int Health(EntityHandle player) => entities.GetHealth(player);

    public int MaxHealth(EntityHandle player) => resupply.MaxHealth(player);

    public EntityHandle Spawn(in SkillShot shot)
    {
        var spawn = new TFProjectileSpawn(shot.ClassName, shot.Owner, shot.Origin, shot.Velocity, shot.Damage)
        {
            Team = shot.Team,
            WeaponName = shot.WeaponName,
        };
        return spawner.Spawn(in spawn)?.Handle ?? EntityHandle.None;
    }

    public TraceResult Trace(Vector start, Vector end, EntityHandle pass) => world.TraceLine(start, end, TraceMask.Shot, pass);

    public void Hurt(EntityHandle victim, EntityHandle attacker, float amount, DamageType type, Vector force, Vector at,
                     string weaponName)
    {
        using var scope = scopes.Push(TFDamageCustom.None, TFWeaponId.None, weaponName);
        damage.ApplyAt(victim, attacker, amount, type, force, at);
    }

    public void Blast(Vector origin, float amount, float radius, EntityHandle attacker, string weaponName) =>
        blasts.Apply(origin, TFBlast.Of(amount, radius, DamageType.Blast), attacker, attacker, attacker,
                     TFWeaponId.Rocketlauncher, weaponName, critical: false);

    public IReadOnlyList<EntityHandle> Near(Vector origin, float radius)
    {
        int count = world.EntitiesInSphere(origin, radius, _near);
        var found = new List<EntityHandle>(count);
        for (int i = 0; i < count; i++)
        {
            EntityHandle e = _near[i];
            if (entities.IsPlayer(e) ? players.IsAlive(e) : directory.Get(e) is { RunsDamageRules: true })
                found.Add(e);
        }

        return found;
    }

    public bool AddCondition(EntityHandle player, TFCond cond, float duration, EntityHandle provider) =>
        conditions.Add(player, cond, duration, provider) is TFCondAdd.Added or TFCondAdd.Refreshed or TFCondAdd.Merged;

    public void RemoveCondition(EntityHandle player, TFCond cond) => conditions.Remove(player, cond);

    public void Push(EntityHandle player, Vector impulse) => entities.ApplyVelocityImpulse(player, impulse, clearGround: true);

    public void BeginBlastJump(EntityHandle player) =>
        carriers.Part<TFBlastJumpState>(entities.GetIndex(player))?.Set(TFBlastJump.RocketJumped);

    public int Heal(EntityHandle player, float amount) => resupply.TakeHealth(player, amount);
}
