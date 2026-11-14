using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Motion;
using TF2.Server.Projectiles;

namespace TF2.Descent.Server.Horde.Attacks;

/// <summary>
/// What a robot's attack asks of the engine (plan_descent_logic 3.8, HO7): a shot's line and a
/// swing's box from the body's eye (the body itself ignored), whether a thing is a player and
/// whether an entity is still there, a hit dealt in a robot's name, a TF projectile launched in
/// its name, and a sound on its body. The server answers it from the engine; a fact answers its
/// own.
/// </summary>
public interface IRobotAttackWorld
{
    /// <summary>A shot's line (hitboxes, the world, props, bodies) from <paramref name="from"/> to <paramref name="to"/>, past <paramref name="self"/>.</summary>
    TraceResult Line(Vector from, Vector to, EntityHandle self);

    /// <summary>A swing's box of half-width <paramref name="half"/> swept from <paramref name="from"/> to <paramref name="to"/>, past <paramref name="self"/>.</summary>
    TraceResult Box(Vector from, Vector to, float half, EntityHandle self);

    bool IsPlayer(EntityHandle entity);

    /// <summary>Whether <paramref name="entity"/> is still there (a projectile not yet gone).</summary>
    bool Exists(EntityHandle entity);

    /// <summary>Deals <paramref name="damage"/> to <paramref name="victim"/> in <paramref name="attacker"/>'s name; answers what it took.</summary>
    int Hurt(EntityHandle victim, EntityHandle attacker, float damage, DamageType bits, Vector direction);

    /// <summary>Launches a TF projectile owned by <paramref name="owner"/> on <paramref name="team"/>; answers its entity, or none when the engine refused it.</summary>
    EntityHandle Launch(string className, EntityHandle owner, Vector origin, Vector velocity, float damage, int team);

    /// <summary>Plays <paramref name="sound"/> on <paramref name="entity"/>.</summary>
    void Sound(EntityHandle entity, string sound);

    /// <summary>The sequence of <paramref name="entity"/>'s model for <paramref name="activity"/>, and its length in seconds; -1 when the model has none.</summary>
    (int Sequence, float Seconds) Gesture(EntityHandle entity, string activity);
}

/// <summary>The engine's answers to <see cref="IRobotAttackWorld"/>: <see cref="TraceMask.Shot"/> lines and boxes, the damage call, CR6's spawner, the sound emitter.</summary>
[SideService(Side.Server, As = [typeof(IRobotAttackWorld)])]
public sealed class EngineRobotAttackWorld(IEngineWorldQuery world, IEngineEntities entities, IEngineDamage damage, ITFProjectileSpawner spawner,
                                           IEngineSound sound, IEngineEntityRender render) : IRobotAttackWorld
{
    public (int Sequence, float Seconds) Gesture(EntityHandle entity, string activity)
    {
        int seq = render.LookupSequenceByActivity(entity, activity);
        return seq < 0 ? (-1, 0f) : (seq, render.SequenceDuration(entity, seq));
    }

    public TraceResult Line(Vector from, Vector to, EntityHandle self) => world.TraceLine(from, to, TraceMask.Shot, self);

    public TraceResult Box(Vector from, Vector to, float half, EntityHandle self)
    {
        var h = new Vector(half, half, half);
        var r = world.TraceBox(from, to, h * -1f, h, TraceMask.Shot, self);
        return new TraceResult(r.Hit, r.Fraction, r.End, r.Normal, r.Entity);
    }

    public bool IsPlayer(EntityHandle entity) => entity != EntityHandle.None && entities.IsPlayer(entity);

    public bool Exists(EntityHandle entity) => entity != EntityHandle.None && entities.IsValid(entity);

    public int Hurt(EntityHandle victim, EntityHandle attacker, float amount, DamageType bits, Vector direction) =>
        damage.TakeDamageAnswered(victim, attacker, amount, bits, direction);

    public EntityHandle Launch(string className, EntityHandle owner, Vector origin, Vector velocity, float amount, int team) =>
        spawner.Spawn(new TFProjectileSpawn(className, owner, origin, velocity, amount) { Team = team })?.Handle ?? EntityHandle.None;

    public void Sound(EntityHandle entity, string name) => sound.EntityEmitSound(entity, name);
}

/// <summary>Where a lent body's gun is: its eye, and its aim (the facing plus the torso's twist and bend, the motion's dials).</summary>
public static class RobotAim
{
    /// <summary>The body's eye: its feet raised by the brains' eye height at its scale (the senses' eye).</summary>
    public static Vector Eye(RobotBody body) =>
        new(body.Position.X, body.Position.Y, body.Position.Z + HordeBrains.EyeHeight * body.Scale);

    /// <summary>The body's aim as engine angles (pitch positive down): yaw is the facing less the torso's yaw dial, pitch the negated torso pitch dial.</summary>
    public static AiAngles Angles(RobotBody body) =>
        new(-body.State.BodyPitch, MotionAngles.Normalise(body.Yaw - body.State.BodyYaw), 0f);

    /// <summary>The unit vector of the body's aim.</summary>
    public static Vector Forward(RobotBody body) => HordeVectors.ToEngine(HordeView.Forward(Angles(body)));
}
