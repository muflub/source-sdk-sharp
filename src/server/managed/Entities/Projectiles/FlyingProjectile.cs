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
/// What every flying projectile does, which is the same three things.
///
/// It leaves, it hits something, it is gone. The differences between a bolt and
/// a rocket are all in what "hits something" means, so that is the only thing
/// left abstract.
/// </summary>
public abstract class FlyingProjectile : ScriptedEntity
{
    public FlyingProjectile(IEngineEntityPhysics physics, EntityMotion motion)
    {
        Physics = physics;
        Motion = motion;
    }

    /// <summary>this entity as the solver sees it.</summary>
    protected readonly IEngineEntityPhysics Physics;

    /// <summary>
    /// What actually flies it — <c>plan_entity.md</c> step 2f,
    /// <see cref="EntityMotion"/>.
    ///
    /// <para>
    /// Taken as a constructor argument, like every other service an entity
    /// uses, rather than reached for through a static. The alternative
    /// considered was leaving the launch to the shell and having the managed
    /// pass discover flyers by asking each entity whether it is flying, which
    /// is a native call per entity per frame — the exact regression step 1g
    /// removed.
    /// </para>
    /// </summary>
    protected readonly EntityMotion Motion;
    /// <summary>
    /// Who fired it. Kept so a kill is credited to a player rather than to a
    /// piece of ammunition, which is what the scoreboard would otherwise say.
    /// </summary>
    protected EntityHandle Shooter { get; private set; } = EntityHandle.None;

    /// <summary>
    /// Stops a projectile acting twice.
    ///
    /// Touch is not a single event: an entity moving fast enough to cross a
    /// corner can touch the wall and the floor in one tick, and a rocket that
    /// exploded once per touch would do double damage in a corner. Removal is
    /// deferred to the end of the frame, so the flag is what actually prevents
    /// it, not the Remove.
    /// </summary>
    private bool _spent;

    /// <summary>How far it falls. Zero flies flat; see <see cref="ScriptedEntityKind.Projectile"/>.</summary>
    protected abstract float GravityScale { get; }

    /// <summary>
    /// How many times it may come off a surface and keep going. None, for
    /// everything but the energy ball: a rocket that hit a wall is over, which
    /// is the whole difference between the two weapons.
    /// </summary>
    protected virtual int Bounces => 0;

    /// <summary>
    /// Everything, and the world counts.
    ///
    /// The default rule is players only, which is right for a pickup and wrong
    /// for this: a bolt hits a wall, a floor or a crate, and a player almost
    /// never. The world arrives as an invalid handle rather than as no call at
    /// all, which is what <see cref="Impact"/> is written to expect.
    /// </summary>
    public override bool TouchedByAnything => true;

    protected abstract string ProjectileModel { get; }

    /// <summary>What it does to whatever it ran into. <paramref name="hit"/> may be the world.</summary>
    protected abstract void Impact(EntityHandle hit);

    public override void OnSpawn()
    {
        SetModel(ProjectileModel);
        Physics.SetGravity(Handle, GravityScale);
    }

    /// <summary>
    /// Aims it and sends it, after it has spawned.
    ///
    /// Direction is passed in rather than read from the shooter's eyes here,
    /// because the launcher already worked it out and reading it twice invites
    /// the two answers to differ by a frame of mouse movement.
    /// </summary>
    internal void FiredBy(EntityHandle shooter, Vector velocity)
    {
        Shooter = shooter;

        // Told to the SHELL as well, not just kept here. The step trace that
        // moves a projectile is what decides it has hit something, and it runs
        // before OnTouch — so "not the shooter" has to be a rule the trace
        // follows, not one this class applies afterwards.
        Entities.SetOwner(Handle, shooter);

        // Angles as well as velocity, or the model flies sideways: nothing
        // turns a projectile to face its travel, so where it POINTS is whatever
        // it was spawned with unless it is told.
        Entities.SetAngles(Handle, Angles.FromVector(velocity));
        Entities.SetVelocity(Handle, velocity);

        // AND TO THE INTEGRATOR, which is what actually moves it now
        // (plan_entity.md step 2f). SetVelocity above still goes to the shell,
        // and is not redundant: it is what EntityGetVelocity, the save state
        // and the mover's "whose velocity is this" rule all read.
        Motion.Launch(Handle, velocity, GravityScale, Bounces, shooter);
    }

    public override void OnTouch(EntityHandle toucher)
    {
        // Never the shooter. A projectile spawns just in front of the eye and a
        // player who is moving forward can catch up with a slow one, which
        // reads as the rocket launcher killing whoever fires it.
        if (toucher == Shooter)
            return;

        if (PassesThrough(toucher))
            return;

        if (_spent)
            return;

        _spent = true;
        Impact(toucher);
        Remove();
    }

    /// <summary>
    /// Whether this is something to fly THROUGH rather than go off on.
    ///
    /// A trigger is a volume that reports being entered; it is not an obstacle,
    /// and it is very often invisible. Stock says so twice — the first lines of
    /// <c>CMissile::MissileTouch</c> and of <c>CCrossbowBolt::BoltTouch</c> are
    /// this same test — and the rule did not survive the port, which is a real
    /// bug and not a tidiness point: dm_lockdown wraps its spawn points in
    /// trigger_multiple, so an RPG fired from one detonated twenty-two units in
    /// front of the shooter's own eye. Measured: 159 damage at 40 units, and
    /// then the blast reached the explosive barrels either side of the spawn.
    /// That is the whole of "the RPG kills its own shooter".
    ///
    /// "But DO hit weapons" is stock's own exception and is carried: a dropped
    /// weapon is a trigger, because that is how it notices a foot, and shooting
    /// one off a ledge is a thing a player may reasonably try.
    ///
    /// Stock's other exception — "some NPCs are triggers that can take damage"
    /// — is not carried, and deliberately. It is written as
    /// <c>m_takedamage != DAMAGE_NO</c>, and everything on this hierarchy is
    /// offered damage and answers for itself, so the test is true for every
    /// managed trigger in the mod and would swallow the rule whole. There are
    /// no NPCs in this build for it to be right about.
    /// </summary>
    private bool PassesThrough(EntityHandle toucher)
    {
        // No special case for the world, and none is needed either way: the
        // world arrives as worldspawn, which is solid and not a trigger, and a
        // handle that resolves to nothing answers no flags at all. Both fall
        // out of the test below as "stop on it", which is the safe answer for
        // anything a projectile cannot identify.
        var flags = Physics.GetSolidFlags(toucher);

        if ((flags & (SolidFlags.Trigger | SolidFlags.VolumeContents)) == 0)
            return false;

        return Physics.GetCollisionGroup(toucher) != CollisionGroup.Weapon;
    }

    /// <summary>Who to credit: the shooter if they are still around, else the projectile.</summary>
    protected EntityHandle Attribution => Entities.IsValid(Shooter) ? Shooter : Handle;
}
