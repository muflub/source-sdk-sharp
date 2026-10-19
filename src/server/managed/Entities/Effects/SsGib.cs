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
/// ss_gib: one thrown piece of debris — <c>plan_entity.md</c> step 4a.
///
/// <para>
/// IT REPLACES <c>CGib</c>, and the refusal it overturns is written down.
/// <c>gib_verdict_test.cpp</c> recorded, at length, why the gib was NOT ported
/// when the effects around it were: a gib is an entity rather than an effect —
/// it persists, it is simulated, it is drawn from per-entity render state and
/// it can be a jointed body — and the managed hierarchy could do none of those.
/// Three blockers were named. All three have since been answered, each by a
/// different lane, and each answer is recorded in that file beside the blocker
/// it closes:
/// </para>
///
/// <list type="number">
///   <item>a per-instance SKIN and MODEL SCALE on the standalone hierarchy,
///   riding the payload so the wire did not grow to pay for it;</item>
///   <item>the blood decals and impact sounds turned out to need no
///   physics-collision slot at all — <c>CGib::BounceGibTouch</c> returns
///   immediately for a solver-simulated gib in stock too, so both belong to
///   the TOUCH path, which every managed entity already has;</item>
///   <item>a client jointed body (the StandaloneBecomeRagdoll slot then; the client
///   ragdolls' <c>Become</c> since plan_physics_bones_native_zero Phase 3), built from an
///   entity's own model and its own current skeleton.</item>
/// </list>
///
/// <para>
/// WHICH KIND, AND WHY IT IS THE ONE THAT MATCHES STOCK EXACTLY.
/// <see cref="ScriptedEntityKind.Projectile"/> installs SOLID_BBOX with
/// FSOLID_NOT_STANDABLE, which is line for line what <c>CGib::Spawn</c> sets
/// (<c>SetSolid( SOLID_BBOX ); AddSolidFlags( FSOLID_NOT_STANDABLE )</c>). A
/// <see cref="ScriptedEntityKind.Prop"/> would have been SOLID_VPHYSICS from
/// the start, which is wrong for the two thirds of gibs that never get a
/// physics object — and the third that does gets SOLID_VPHYSICS anyway,
/// because <c>InitPhysics</c> sets it itself.
/// </para>
///
/// <para>
/// THE THREE SIMULATION MODES ARE STOCK'S THREE, and the third is not this
/// class. <c>GIB_SIMULATE_RAGDOLL</c> made a <c>CRagGib</c> in stock — a
/// DIFFERENT entity, not a configured <c>CGib</c> — and it makes an
/// <see cref="SsRagdoll"/> here, for the same reason: a jointed body is built
/// on the client from a model and a force, and has nothing in common with a
/// chunk that flies. See <see cref="Gibs"/>, which is where the three modes are
/// chosen between.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsGib, ScriptedEntityKind.Projectile)]
public sealed class SsGib : ScriptedEntity
{
    public SsGib(IEngineEntityPhysics physics, EntityMotion motion,
                 IEngineEffects effects, IEngineSound sound,
                 IEngineEntityRender render, IEngineHost host, BloodDecals blood,
                 IServerSkins skins)
    {
        _blood = blood;
        _skins = skins;
        _physics = physics;
        _motion = motion;
        _effects = effects;
        _sound = sound;
        _render = render;
        _host = host;
    }

    private readonly IEngineEntityPhysics _physics;
    private readonly EntityMotion _motion;
    private readonly IEngineEffects _effects;
    private readonly IEngineSound _sound;
    private readonly IEngineEntityRender _render;
    private readonly IEngineHost _host;
    private readonly BloodDecals _blood;
    private readonly IServerSkins _skins;

    /// <summary>
    /// <c>CGib::m_cBloodDecals</c>: how many bounces may leave a mark, one per
    /// bounce until none remain.
    /// </summary>
    public const int BloodDecalsPerGib = 5;

    /// <summary>
    /// How many times a point gib may come off a surface and keep going.
    ///
    /// <para>
    /// The same number as <see cref="BloodDecalsPerGib"/>, and that is the
    /// argument for it rather than a coincidence: stock gives a gib five decals
    /// "1 per bounce until none remain", so five is stock's own statement about
    /// how many bounces a gib is expected to get. A budget smaller than the
    /// decal count would leave decals that can never be spent.
    /// </para>
    ///
    /// <para>
    /// A COST IS RECORDED WITH IT. <c>MoveMath.ResolveFlight</c> — step 2f's
    /// integrator — reflects perfectly: <c>v - 2(v·n)n</c>, with no elasticity
    /// and no friction, where <c>CGib::Spawn</c> sets <c>SetFriction(0.55)</c>
    /// and stock's MOVECOLLIDE_FLY_BOUNCE sheds energy on every contact. So a
    /// point gib here skitters further before it settles than stock's does. The
    /// fix is an elasticity term in <c>ResolveFlight</c>, which is step 2f's
    /// file and not this one's. Recorded as a <c>ContractBaseline.Refusals</c> entry,
    /// which is where plan_entity.md §7 puts a difference somebody chose.
    /// </para>
    /// </summary>
    public const int PointGibBounces = BloodDecalsPerGib;

    /// <summary>
    /// <c>CGib::Spawn</c>'s <c>SetFriction(0.55)</c>, "deading the bounce a bit".
    /// Applied to the physics object when there is one; a point gib's bounce is
    /// the managed integrator's and does not read it (see
    /// <see cref="PointGibBounces"/>).
    /// </summary>
    public const float GibFriction = 0.55f;

    /// <summary>
    /// The mass stock assumes for a gib it is throwing, commented UNDONE there
    /// and kept because a map's velocity numbers were tuned against it.
    /// </summary>
    public const float AssumedGibMass = 200f;

    private BloodColor _bloodColor = BloodColor.Red;
    private SurfaceMaterial _material = SurfaceMaterial.Silent;
    private int _decalsLeft = BloodDecalsPerGib;
    private bool _solverOwned;

    /// <summary>
    /// A gib is hit by the world and by anything else it runs into, not only by
    /// players. Same reason <c>FlyingProjectile</c> gives: the world arrives as
    /// an invalid handle rather than as no call at all.
    /// </summary>
    public override bool TouchedByAnything => true;

    /// <summary>What this gib bleeds when it bounces. Exposed for the tests.</summary>
    internal BloodColor Bleeds => _bloodColor;

    /// <summary>What it sounds like when it hits something. Exposed for the tests.</summary>
    internal SurfaceMaterial Material => _material;

    /// <summary>Decals not yet spent. Exposed for the tests.</summary>
    internal int DecalsLeft => _decalsLeft;

    /// <summary>Whether the solver took it. Exposed for the tests.</summary>
    internal bool SolverOwned => _solverOwned;

    /// <summary>
    /// What the removal was armed for, or -1. Exposed for the reason
    /// <c>SsRagdoll.ThinkArmed</c> gives: <c>SetNextThink</c> goes through the
    /// STATIC engine, so a test's double never sees it, and a gib that never
    /// armed its own removal looks exactly like one that did.
    /// </summary>
    internal float ThinkArmed { get; private set; } = -1f;

    /// <summary>Whether it has taken itself out of the world. Exposed for the same reason.</summary>
    internal bool Gone { get; private set; }

    /// <summary>
    /// Thrown. The port of the tail of <c>SourceSharp_ShootGib</c> plus
    /// <c>CGib::Spawn</c>, and it runs AFTER the spawn because everything in it
    /// wants either the bound managed instance or the physics object the shell
    /// builds during that spawn.
    /// </summary>
    /// <param name="shot">Everything the shooter decided. See <see cref="GibShot"/>.</param>
    /// <param name="invisible">
    /// A sprite <c>shootmodel</c>: the gib is thrown but not drawn, which is
    /// what stock does with one. See <see cref="Gibs.Shoot"/>.
    /// </param>
    internal void Throw(in GibShot shot, bool invisible)
    {
        _bloodColor = (BloodColor)shot.BloodColor;
        _decalsLeft = BloodDecalsPerGib;

        // KEPT AS A SurfaceMaterial ALL THE WAY, AND THAT IS A BUG FIX.
        //
        // The ABI carries GibShot.Material as an int, and EnvShooter fills it
        // from MaterialFor(), which answers a SurfaceMaterial -- whose values
        // are the ASCII LETTERS the engine's surfaceprop tables use ('W' for
        // wood, 89 for glass). SourceSharp_ShootGib assigned that number
        // straight into CGib::m_material, which is a `Materials` (func_break.h:
        // matGlass = 0, matWood = 1, ... matNone = 10), and CGib::BounceGibTouch
        // then asked CBreakable::MaterialSound for a sound name for material 89.
        // That switch has no case for 89 and returns NULL, so the impact sound
        // was silently dead for every gib in the game -- and `m_material !=
        // matNone` was TRUE for 89, so the branch was entered every time and did
        // nothing. Nothing could see it: the only symptom is a sound that does
        // not play.
        //
        // Here the letter is what it always was and the lookup is by letter, so
        // the two ends cannot disagree about which encoding this field is in.
        _material = (SurfaceMaterial)shot.Material;

        // THROUGH THE INJECTED AREA AND NOT ScriptedEntity.SetVisible, which is
        // the same call by another road: the base's convenience goes through the
        // STATIC engine, so a test's double never sees it -- the same wall
        // SsRagdoll.ThinkArmed and PropPhysics.Gone both record.
        if (invisible)
            _render.SetVisible(Handle, false);

        _skins.SetSkin(Handle, shot.Skin);
        _render.SetModelScale(Handle, shot.Scale > 0f ? shot.Scale : 1f);

        // ZERO MEANS ONE, which is the engine's rule and not a guess:
        // CBaseEntity::PhysicsAddGravityMove reads `if ( GetGravity() )
        // ent_gravity = GetGravity(); else ent_gravity = 1.0;`. A gib whose
        // shooter left gravityscale unset must fall normally, and handing 0
        // to the managed integrator would make it fly flat forever --
        // EntityMotion.Launch reads a zero scale as MotionKind.Fly.
        float gravity = shot.GravityScale != 0f ? shot.GravityScale : 1f;

        _physics.SetGravity(Handle, gravity);
        _physics.SetFriction(Handle, GibFriction);
        _physics.SetCollisionGroup(Handle, CollisionGroup.Debris);

        // +/- 5% on the lifetime, from the ENGINE's random stream. A row of gibs
        // that all vanished on the same frame reads as a bug rather than as
        // debris, and the draw has to be the engine's so demo playback
        // reproduces it.
        float lifetime = shot.Lifetime * _host.RandomFloat(0.95f, 1.05f);

        Vector velocity = shot.Direction * shot.Speed;

        _solverOwned = shot.Simulation == (int)GibSimulation.Physics
                    && _physics.InitPhysics(Handle, asleep: false, debris: true);

        if (_solverOwned)
        {
            Entities.SetVelocity(Handle, velocity);

            // Tumble. Z is deliberately zero in stock: debris that spins about
            // its own vertical axis reads as a dropped prop rather than as
            // something thrown.
            float mass = _physics.GetMass(Handle);

            _physics.ApplyTorque(Handle, new Vector(
                shot.AngularVelocity * _host.RandomFloat(0.1f, 1.0f) * mass,
                shot.AngularVelocity * _host.RandomFloat(0.1f, 1.0f) * mass,
                0f));
        }
        else
        {
            // THE MOVE TYPE FOLLOWS THE OBJECT. A gib the solver would not take
            // -- a model with no collision data -- falls and bounces under game
            // physics instead of hanging in the air, which is a truthful
            // degradation rather than a substitute: the entity really is one
            // game physics moves now. InitPhysics has already said on the
            // console WHY it refused, naming the model, so this does not say it
            // again.
            Entities.SetVelocity(Handle, velocity);
            Entities.SetAngularVelocity(Handle, new Vector(
                _host.RandomFloat(100f, 200f),
                _host.RandomFloat(100f, 300f),
                0f));

            // NO OWNER PASSED, and it is not an oversight. EntityMotion's owner
            // is the entity whose hull a projectile is allowed to start inside;
            // a gib is thrown from a shooter that is usually a point in mid-air
            // with no hull at all, and stock's gib has no owner either.
            _motion.Launch(Handle, velocity, gravity, PointGibBounces, EntityHandle.None);
        }

        // EVERY gib arms its own removal, where stock arms one only for the
        // strict-remove case and otherwise leans on CGib::WaitTillLand ->
        // SUB_FadeOut. That difference is deliberate and it is the SAFER of the
        // two: this build has no fade-out think on the managed hierarchy, so a
        // gib that did not arm a removal would live until the map changed.
        // StrictRemove still decides WHETHER the player may be looking at it
        // when it goes -- see OnThink.
        ThinkArmed = lifetime;
        SetNextThink(lifetime);
    }


    /// <summary>
    /// It hit something. The port of <c>CGib::BounceGibTouch</c>.
    ///
    /// <para>
    /// A SOLVER-SIMULATED GIB DOES NOTHING HERE, and that is stock's rule
    /// rather than a limitation: <c>BounceGibTouch</c> opens with
    /// <c>if ( VPhysicsGetObject() ) return;</c>, so the decals and the impact
    /// sound have always belonged exclusively to the non-solver path.
    /// </para>
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (_solverOwned)
            return;

        if (_decalsLeft > 0 && _bloodColor != BloodColor.None)
        {
            // Up a bit, and trace down: stock's own comment, and the offsets
            // are stock's.
            Vector from = Origin + new Vector(0f, 0f, 8f);

            // The gib itself is ignored, as stock's trace passed `this`.
            _blood.Trace(from, from + new Vector(0f, 0f, -24f), _bloodColor, Handle);
            _decalsLeft--;
        }

        // One impact in three makes a noise, which is stock's
        // `random->RandomInt(0,2) == 0`. Every bounce chirping is what this is
        // avoiding.
        if (_material != SurfaceMaterial.Silent
            && _host.RandomFloat(0f, 3f) < 1f)
        {
            string? sound = MaterialSound(_material);

            if (sound != null)
                _sound.EntityEmitSound(Handle, sound);
        }
    }

    /// <summary>
    /// Its time is up. <c>CGib::DieThink</c> in a multiplayer game, which is
    /// <c>UTIL_Remove</c> and nothing else — the single-player branch fades it
    /// out instead, and this mod has no single-player.
    /// </summary>
    public override void OnThink()
    {
        Gone = true;
        Remove();
    }

    /// <summary>
    /// The sound script a material impact plays — the port of
    /// <c>CBreakable::MaterialSound</c>, whose only caller in this build was
    /// <c>CGib::BounceGibTouch</c>.
    ///
    /// <para>
    /// BY SurfaceMaterial AND NOT BY <c>Materials</c>. Stock switches on
    /// func_break.h's small-integer enum; this switches on the ASCII letters the
    /// surfaceprop tables actually use, because that is what the field carries
    /// on this side (see <see cref="Throw"/>). The mapping is stock's, case for
    /// case: web joins flesh, cinder block joins rocks, unbreakable glass joins
    /// glass, and a ceiling tile makes no sound at all.
    /// </para>
    /// </summary>
    internal static string? MaterialSound(SurfaceMaterial material) => material switch
    {
        SurfaceMaterial.Wood     => "Breakable.MatWood",
        SurfaceMaterial.Flesh    => "Breakable.MatFlesh",
        SurfaceMaterial.Computer => "Breakable.Computer",
        SurfaceMaterial.Glass    => "Breakable.MatGlass",
        SurfaceMaterial.Metal    => "Breakable.MatMetal",
        SurfaceMaterial.Rocks    => "Breakable.MatConcrete",
        _ => null,
    };
}
