//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// THE PHYSICS EVENT RING, CONSUMED — plan_entity.md §3.8.
///
/// <para>
/// Called once per simulation frame with everything the solver reported, in the
/// order it reported it. <see cref="PhysicsCollisionRules"/> decides; this class
/// counts and emits, and has nothing of its own to decide. The split is on
/// purpose: every rule is reachable from an xUnit fact with no game running,
/// and what is left here is thin enough that a reader can see it is thin.
/// </para>
///
/// <para>
/// <b>WHAT IT DOES NOT DO YET, STATED RATHER THAN SILENTLY DROPPED.</b>
/// </para>
/// <para>
/// <b>The impact SOUND</b> is played (physics stock parity C3). Each managed
/// side queues its own surface against the other's through
/// <see cref="IPhysicsCollisionWorld.ImpactSound"/>, which is stock's
/// PhysicsImpactSound into the batch CPhysicsHook plays every 50 ms from the
/// surfaceprop's impactSoft/impactHard script handles. The batch emits from
/// the queued position, so a managed entity needs no edict for it. A side with
/// no managed entity (the world) queues none, as with shake and dust below.
/// </para>
/// <para>
/// <b>Touch, wake and sleep.</b> Recorded by native, handed over here, and
/// COUNTED but not acted on. They are reported by <c>cs_sv_physics_events</c>
/// so the next lane sees what is available rather than rediscovering it — the
/// mistake this lane exists to undo was a carrier built with no consumer and
/// nothing saying so.
/// </para>
/// </summary>
public sealed class PhysicsCollisionResponse
{
    private readonly IPhysicsCollisionWorld _world;

    /// <summary>How many events of each kind have arrived since the last reset.</summary>
    private readonly long[] _seen = new long[(int)PhysicsEventKind.Unknown + 1];

    /// <summary>Total events handed over since the last reset.</summary>
    public long Events { get; private set; }

    /// <summary>Collisions that passed all three of stock's gates.</summary>
    public long Responded { get; private set; }

    /// <summary>Screen shakes emitted.</summary>
    public long Shakes { get; private set; }

    /// <summary>Dust puffs emitted.</summary>
    public long Dusts { get; private set; }

    /// <summary>
    /// Impact sounds queued into the engine's batch, one per managed side of a
    /// collision that made a sound (physics stock parity C3).
    /// </summary>
    public long SoundsQueued { get; private set; }

    /// <param name="world">Where the surfaces are read and the effects go.</param>
    public PhysicsCollisionResponse(IPhysicsCollisionWorld world)
    {
        _world = world;
    }

    /// <summary>How many events of one kind have arrived since the last reset.</summary>
    public long SeenOfKind(PhysicsEventKind kind)
        => _seen[(int)kind];

    /// <summary>Zero every counter. <c>cs_sv_physics_events reset</c>.</summary>
    public void Reset()
    {
        Array.Clear(_seen);
        Events = 0;
        Responded = 0;
        Shakes = 0;
        Dusts = 0;
        SoundsQueued = 0;
    }

    /// <summary>
    /// One event from a callback managed answers itself (plan_physics_managed
    /// B2.2): PhysicsCollisionImpacts' POST_COLLISION, answered in the
    /// callback as stock's PostCollision called VPhysicsCollision there.
    /// Counted as an event. The only way into the response: the ring's Drain
    /// had no producer after 2.3 and went (2026-10-12).
    /// </summary>
    public void Respond(in PhysicsEvent ev)
    {
        Events++;
        _seen[(int)ev.Kind]++;

        if (ev.Kind == PhysicsEventKind.PostCollision)
            Collision(in ev);
    }

    /// <summary>
    /// One impact, both sides.
    ///
    /// <para>
    /// STOCK RUNS ITS RESPONSE ONCE PER SIDE and so does this: a collision
    /// between two crates shakes and dusts twice, once from each crate's point
    /// of view, because the two sides can have different masses and hit
    /// different materials.
    /// </para>
    ///
    /// <para>
    /// A SIDE WITH NO MANAGED ENTITY IS SKIPPED, and that is a stated
    /// divergence from stock rather than an oversight. Stock's own
    /// <c>PostCollision</c> returns for an object whose game data is not a
    /// <c>CBaseEntity</c>, so the SDK half of a mixed pair gets no response from
    /// either side — before this lane neither half did. What changes here is
    /// that the MANAGED half now gets one.
    /// </para>
    ///
    /// <para>
    /// <b>ONLY WHAT STOCK CALLS VPhysicsCollision FOR.</b> Stock's response is
    /// the body of <c>CBaseEntity::VPhysicsCollision</c>, which
    /// <c>CCollisionEvent::PostCollision</c> calls only on the solver's verdict
    /// (physics.cpp:1893-1914): an entity hitting itself gets one call, for
    /// side 0, and only <c>if ( pEvent-&gt;isCollision )</c>; two shadows
    /// meeting are no collision; otherwise each side when <c>isCollision</c>.
    /// <see cref="PhysicsEventRouter"/> applies the same three arms to the
    /// entity hooks, so the two consumers of one record agree.
    /// </para>
    /// </summary>
    private void Collision(in PhysicsEvent ev)
    {
        bool self = ev.HasEntity0 && ev.Entity0 == ev.Entity1;
        bool collision = self ? ev.IsCollision : ev.IsCollision && !(ev.IsShadow(0) && ev.IsShadow(1));
        if (!collision)
            return;

        MoveSurface s0 = _world.Surface((int)ev.SurfaceProps0);
        MoveSurface s1 = _world.Surface((int)ev.SurfaceProps1);

        if (!PhysicsCollisionRules.Responds(in ev, s0.Material, s1.Material))
            return;

        Responded++;

        CollisionSound? sound = PhysicsCollisionRules.Sound(in ev);

        for (int index = 0; index < (self ? 1 : 2); index++)
        {
            bool mine = index == 0 ? ev.HasEntity0 : ev.HasEntity1;
            if (!mine)
                continue;

            // Each side plays its own surface against the other's, as stock's
            // per-entity VPhysicsCollision calls PhysCollisionSound.
            if (sound is CollisionSound s)
            {
                _world.ImpactSound(new EntityHandle(index == 0 ? ev.Entity0 : ev.Entity1), s.Channel,
                                   (int)(index == 0 ? ev.SurfaceProps0 : ev.SurfaceProps1),
                                   (int)(index == 0 ? ev.SurfaceProps1 : ev.SurfaceProps0),
                                   s.Volume, s.Speed);
                SoundsQueued++;
            }

            if (PhysicsCollisionRules.Shake(in ev, index) is CollisionShake shake)
            {
                _world.ScreenShake(in shake);
                Shakes++;
            }

            // The material of the surface being HIT, which is the other side's
            // — stock passes `physprops->GetSurfaceData( surfaceProps[otherIndex] )`
            // as `phit`. Reading its own would dust against the crate rather
            // than against the floor.
            int hitMaterial = index == 0 ? s1.Material : s0.Material;
            if (PhysicsCollisionRules.Dust(hitMaterial, ev.Speed))
            {
                _world.DustEffect(ev.Point, DustDirection(in ev),
                                  PhysicsCollisionRules.DustSize, ev.Speed);
                Dusts++;
            }
        }
    }

    /// <summary>
    /// Which way the dust goes.
    ///
    /// <para>
    /// Stock randomises it — <c>vecVel.Random(-1,1); vecVel.z = RandomFloat(0.3,1); Normalize</c>
    /// — and this is a DETERMINISTIC SUBSTITUTE, not the same distribution:
    /// the contact normal, mirrored to point up, plus a fixed upward bias. It
    /// differs in two ways that are stated rather than hidden: every puff
    /// leaves the surface (stock sprays some into a wall), and the bias is one
    /// number where stock's z is spread over 0.3-1. Chosen because the normal
    /// is a fact the event carries and a fixed direction is one fewer thing a
    /// replay cannot reproduce.
    /// </para>
    ///
    /// <para>
    /// The engine asserts on a direction that is not unit length, so this
    /// always normalises and falls back to straight up for a degenerate normal
    /// — which is what a zeroed normal on a record with no contact data would
    /// otherwise produce.
    /// </para>
    /// </summary>
    internal static Vector DustDirection(in PhysicsEvent ev)
    {
        Vector d = ev.Normal;
        if (d.Z < 0f)
            d = new Vector(d.X, d.Y, -d.Z);
        d = new Vector(d.X, d.Y, d.Z + UpwardBias);

        float length = d.Length;
        if (length < 1e-4f)
            return new Vector(0f, 0f, 1f);

        return new Vector(d.X / length, d.Y / length, d.Z / length);
    }

    /// <summary>
    /// How far up the dust is nudged: the MEAN of stock's
    /// <c>RandomFloat( 0.3f, 1.0f )</c>, standing in for its spread.
    /// </summary>
    internal const float UpwardBias = 0.65f;
}

/// <summary>
/// The three things the collision response asks of the world.
///
/// <para>
/// A seam of its own rather than the two engine areas directly, for the reason
/// <c>IMoveWorld</c> gives for the movement loop: every question the response
/// asks and every effect it produces goes through one interface, so the whole
/// of <see cref="PhysicsCollisionResponse"/> is reachable from an xUnit fact
/// with no engine. It is three members wide because that is how much of the
/// world an impact touches.
/// </para>
/// </summary>
public interface IPhysicsCollisionWorld
{
    /// <summary>The surfaceprop at that index — <see cref="PhysicsSurfaces.Of"/>.</summary>
    MoveSurface Surface(int surfaceProps);

    /// <summary>Shake every screen in range.</summary>
    void ScreenShake(in CollisionShake shake);

    /// <summary>Throw a puff of dust. The direction must be unit length.</summary>
    void DustEffect(Vector origin, Vector direction, float size, float speed);

    /// <summary>
    /// Queue a physics impact sound for one side of a collision: stock's
    /// PhysicsImpactSound into the batch CPhysicsHook plays every 50 ms.
    /// </summary>
    void ImpactSound(EntityHandle entity, CollisionSoundChannel channel, int surfaceProps,
                     int surfacePropsHit, float volume, float speed);
}

/// <summary>
/// <see cref="IPhysicsCollisionWorld"/> over the real engine areas. Three
/// forwards and no decision — everything that decides is in
/// <see cref="PhysicsCollisionRules"/>.
/// </summary>
public sealed class EnginePhysicsCollisionWorld : IPhysicsCollisionWorld
{
    private readonly IEngineEffects _effects;
    private readonly IScreenEffects _hud;
    private readonly PhysicsSoundBatch _sounds;
    private readonly PhysicsSurfaces _surfaces;

    /// <summary>Source's CHAN_BODY and CHAN_STATIC.</summary>
    private const int ChanBody = 4, ChanStatic = 6;

    /// <param name="effects">Where dust is emitted.</param>
    /// <param name="hud">Where a screen shake is sent from.</param>
    /// <param name="sounds">Where an impact sound is queued (managed's batch since plan_physics_bones_native_zero 2.3).</param>
    /// <param name="surfaces">The side's surfaceprop table.</param>
    public EnginePhysicsCollisionWorld(IEngineEffects effects, IScreenEffects hud, PhysicsSoundBatch sounds,
                                       PhysicsSurfaces surfaces)
    {
        _effects = effects;
        _hud = hud;
        _sounds = sounds;
        _surfaces = surfaces;
    }

    /// <inheritdoc/>
    public MoveSurface Surface(int surfaceProps) => _surfaces.Of(surfaceProps);

    /// <inheritdoc/>
    public void ScreenShake(in CollisionShake shake)
        => _hud.Shake(shake.Origin, shake.Amplitude, shake.Frequency,
                            shake.Duration, shake.Radius);

    /// <inheritdoc/>
    public void DustEffect(Vector origin, Vector direction, float size, float speed)
        => _effects.DustEffect(origin, direction, size, speed);

    /// <inheritdoc/>
    public void ImpactSound(EntityHandle entity, CollisionSoundChannel channel, int surfaceProps,
                            int surfacePropsHit, float volume, float speed)
        => _sounds.AddImpactSound(entity, channel == CollisionSoundChannel.Body ? ChanBody : ChanStatic,
                                  surfaceProps, surfacePropsHit, volume, speed);
}
