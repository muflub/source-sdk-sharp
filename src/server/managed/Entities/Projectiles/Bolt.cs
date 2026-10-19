using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// ss_bolt: what the crossbow fires.
///
/// A single target hit, hard, with no splash — which is the crossbow's whole
/// character and the thing a hitscan stand-in could not express. It travels
/// fast enough to feel instant at duelling range and slow enough to be led at
/// the length of a map, and that difference IS the weapon.
/// </summary>
[ScriptedEntity(EntityNames.SsBolt, ScriptedEntityKind.Projectile)]
public sealed class Bolt : FlyingProjectile
{
    public Bolt(IEngineEntityPhysics physics, EntityMotion motion, IEngineDamage damage,
                ITempEnts temps, IEngineSound sound, IEngineEffects effects,
                IEngineNetworking networking, IEngineWorldQuery world)
        : base(physics, motion)
    {
        Damage = damage;
        Temps = temps;
        Sound = sound;
        Effects = effects;
        Networking = networking;
        World = world;
    }

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>Sending the stuck bolt to the clients that can see it.</summary>
    private readonly ITempEnts Temps;

    /// <summary>The hit cue, attached to the bolt as stock's EmitSound is.</summary>
    private readonly IEngineSound Sound;

    /// <summary>The impact-trace dispatch (the port of UTIL_ImpactTrace's end).</summary>
    private readonly IEngineEffects Effects;

    /// <summary>Asking whether what stopped the bolt is static architecture.</summary>
    private readonly IEngineNetworking Networking;

    /// <summary>The lines the landing asks the world.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>What a map may set on this entity. See <see cref="BoltVars"/>.</summary>
    [ScriptVars] public BoltVars Vars = new();

    /// <summary>
    /// A little, not none. A bolt that flew perfectly flat forever would be a
    /// hitscan with a delay on it; the drop is what makes range cost something.
    /// </summary>
    protected override float GravityScale => 0.15f;

    /// <summary>Stock HL2's bolt, so it looks like one without shipping a model.</summary>
    protected override string ProjectileModel => "models/crossbow_bolt.mdl";

    public readonly Output OnHit = new("OnHit");

    /// <summary>
    /// What a bolt does when it lands: <c>CCrossbowBolt::BoltTouch</c>
    /// (<c>game/server/hl2/crossbow.cpp:195</c>), minus the branches this
    /// build cannot reach and the ones the mover already owns.
    ///
    /// <para>
    /// <b>HIT A THING THAT CAN BE HURT</b> - stock's first arm, taken when the
    /// touched entity takes damage. The bolt hurts it, says so
    /// (<c>Weapon_Crossbow.BoltHitBody</c>), then traces <c>MASK_BLOCKLOS</c>
    /// from its rest point along forward*128 ignoring the victim. If that
    /// line stops in static architecture the bolt sticks: a
    /// <c>"BoltImpact"</c> tempent at the trace's end - which is what the
    /// client's <c>CrossbowBoltEffect</c> has been waiting for, drawing the
    /// bolt out of the wall behind its victim.
    /// </para>
    ///
    /// <para>
    /// <b>HIT THE WORLD</b> - stock's else, taken for a surface
    /// (<see cref="EntityHandle.None"/>: the invalid handle brushwork arrives
    /// as, per <see cref="FlyingProjectile.TouchedByAnything"/>). The touch
    /// trace's surface is re-asked for its flags: the sky guard first
    /// (<c>SURF_SKY</c> means silence, stock's own condition), then
    /// <c>Weapon_Crossbow.BoltHitWorld</c>, the stuck-bolt tempent at the
    /// resting place with index 0, and <c>UTIL_ImpactTrace</c>'s terminal
    /// dispatch - the managed port every hitscan already uses,
    /// <see cref="IEngineEffects.ImpactEffect"/> (Arsenal). The reflect arm
    /// and the two-second linger are in docs/divergences.md.
    /// </para>
    /// </summary>
    protected override void Impact(EntityHandle hit)
    {
        OnHit.Fire();

        Vector forward = Angles.Forward(Entities.GetAngles(Handle));

        if (hit != EntityHandle.None)
        {
            // Only if it hit something that can be hurt. A bolt in a wall is
            // the common case and must not be an error.
            Damage.Apply(hit, Attribution, Vars.Damage, DamageType.Bullet);

            // The body "thwack", on the bolt, as stock's EmitSound is.
            Sound.EntityEmitSound(Handle, "Weapon_Crossbow.BoltHitBody");

            // What is behind the victim is what holds the bolt. Stock starts
            // this at the bolt's own origin; the bolt's box is 2 units and
            // the line runs along its long axis out of the tip, so the trace
            // sees what the mover's own traces never did.
            Vector origin = Entities.GetOrigin(Handle);
            var tr2 = World.TraceLine(origin, origin + forward * 128f, TraceMask.BlockLos, hit);

            // Stock's guard, both halves: the line must stop, and stop in
            // static architecture - no entity the engine can name (brushwork),
            // or an entity pinned to MOVETYPE_NONE. A victim still walking
            // around does not hold a bolt.
            bool holdsBolts = tr2.Entity == EntityHandle.None;
            if (!holdsBolts)
            {
                // The entity is named but not the answer: only a thing pinned
                // to MOVETYPE_NONE - static architecture - holds a bolt.
                Networking.EntityGetState(tr2.Entity, out MoveType moveType);
                holdsBolts = moveType == MoveType.None;
            }

            if (tr2.Fraction != 1f && holdsBolts)
            {
                TeEffectData data = new()
                {
                    Origin = tr2.End,
                    // Stock's normal is forward (the model's facing), not the
                    // velocity: after gravity's drop the two differ, and the
                    // client hangs the bolt off this vector. AngleVectors'
                    // trig output is already unit length; stock's
                    // VectorNormalize is a no-op on it.
                    Normal = forward,
                    // tr2.fraction != 1.0, which is true here - the guard
                    // above is the same test, so this is stock's 1.
                    EntIndex = 1,
                };
                Temps.DispatchEffect(TeFilters.Pvs(tr2.End), 0f, BoltImpact, in data);
            }

            return;
        }


        // The world. Stock asks the touch trace for the surface; the touch
        // hands over only the entity, so the face the mover's step stopped on
        // is re-asked for: back along the approach - where the last frame's
        // step proved the air open - and through the face again. Ignoring
        // self, as the step trace does; the bolt's own box sits at the end
        // point on that line.
        Vector resting = Entities.GetOrigin(Handle);
        var face = World.TraceLineSurface(resting - forward * RetraceBack, resting + forward * RetraceFwd,
                                          TraceMask.Shot, Handle);

        // The sky guard, stock's first condition: a bolt into the skybox is
        // removed without a word. A re-trace that finds nothing is the same
        // silence - a face that vanished under the bolt holds no stuck bolt
        // and no decal.
        if (!face.Hit || (face.SurfaceFlags & SurfSky) != 0)
            return;

        Sound.EntityEmitSound(Handle, "Weapon_Crossbow.BoltHitWorld");

        TeEffectData stuck = new()
        {
            Origin = face.End,
            Normal = forward,
            EntIndex = 0,
        };
        Temps.DispatchEffect(TeFilters.Pvs(face.End), 0f, BoltImpact, in stuck);

        // UTIL_ImpactTrace's terminal dispatch - CBaseEntity::ImpactTrace's
        // "Impact" - through the port the hitscans already use, with its
        // backoff off the surface so the client can re-trace to the plane.
        Effects.ImpactEffect(face.End - forward * ImpactBackoff, face.End,
                             face.SurfaceProps, DamageType.Bullet, face.Entity);
    }

    /// <summary>The effect name the client's CrossbowBoltEffect is registered for.</summary>
    private const string BoltImpact = "BoltImpact";

    /// <summary>How far back off the resting place the surface re-trace starts: inside the last frame's proven-open step.</summary>
    private const float RetraceBack = 16f;

    /// <summary>How far through the face it ends, to cross it cleanly.</summary>
    private const float RetraceFwd = 8f;

    /// <summary>The impact effect's start backoff, as Arsenal's UTIL_ImpactTrace port uses (FuncBreakableSurf.ImpactBackoff).</summary>
    private const float ImpactBackoff = 32f;

    /// <summary>SURF_SKY (bspflags.h): the one surface bit stock's world branch tests.</summary>
    private const int SurfSky = 0x0004;
}

/// <summary>
/// The map keys <see cref="Bolt"/> declares.
/// </summary>
[ScriptVars]
public partial class BoltVars
{
    public float Damage { get; set; } = 100f;
}
