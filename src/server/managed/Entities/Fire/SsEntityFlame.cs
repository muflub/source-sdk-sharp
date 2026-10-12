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
/// ss_entityflame: the fire attached to something that is burning.
///
/// <para>
/// It replaces <c>entityflame</c>, which was a native <c>CEntityFlame</c> paired
/// with a native <c>C_EntityFlame</c>. Between them they held the last three
/// references keeping <c>c_fire_smoke.cpp</c> on the client's link line, and
/// <c>EntityFlame.o</c> — 86 references into <c>baseentity.o</c>, almost all of
/// them the vtable of a class that DERIVED from the entity — on the server's.
/// Not one of those was a call a retype could have moved. The only way to stop
/// inheriting is to stop being a C++ entity.
/// </para>
///
/// <para>
/// AND IT UNBLOCKS THE BURN STATION. <c>env_entity_igniter</c> could not set the
/// sandbox's drum alight, because a native flame parents itself to what it burns
/// and <c>SetParent</c> takes a <c>CBaseEntity</c> — which every prop in this
/// mod stopped being. What the station demonstrated was the refusal. A managed
/// flame is on the same hierarchy as the drum and parents to it the ordinary
/// way, and to a native <c>prop_ragdoll</c> as well: the standalone hierarchy
/// gained a <c>CBaseEntity</c>-parent arm the day before this port.
/// </para>
///
/// <para>
/// WHY THE CLASSNAME IS <c>ss_</c>. <c>entityflame</c> is code-spawned and
/// nothing else: stock reaches it through <c>CreateEntityByName</c> from
/// <c>CEntityFlame::Create</c>, and the entity lumps of both shipped maps
/// contain zero of them. The name a mapper writes is
/// <c>env_entity_igniter</c>, which stock ALSO binds to <c>CEntityFlame</c> and
/// which this mod has had as its own managed class for some time. There is no
/// mapper-facing contract here to preserve. <c>ss_firesmoke</c>'s precedent.
/// </para>
///
/// <para>
/// WHAT IS NOT NETWORKED, which is everything. Stock's <c>DT_EntityFlame</c>
/// sends one prop — <c>m_hEntAttached</c> — and the client uses it for two
/// things: to hang the particle system's control points off the burning entity,
/// and to clear that entity's <c>FL_ONFIRE</c> and stop its sounds when the
/// flame goes. The first is answered by PARENTING, because a flame parented to
/// what it burns is already at the burning entity's origin and the client
/// already interpolates it there. The second is the server's job here and is
/// done here. So the wire carries an origin and nothing else, which is what a
/// flame is.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsEntityFlame, ScriptedEntityKind.Effect)]
public sealed class SsEntityFlame : ScriptedEntity
{
    public SsEntityFlame(IGameClock clock, IEngineDamage damage, IEngineSound sound,
                         IEngineNetworking net, IEngineWorldQuery world,
                         IEntityRoster<EnvFire> fires)
    {
        _fires = fires;
        Clock = clock;
        Damage = damage;
        Sound = sound;
        Net = net;
        World = world;
    }

    /// <summary>what the server thinks the time is.</summary>
    /// <summary>The module's fires, which a burning flame warms.</summary>
    private readonly IEntityRoster<EnvFire> _fires;

    private readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>the burning noise.</summary>
    private readonly IEngineSound Sound;

    /// <summary>asking an entity what kind of thing it is.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>asking the world what is standing in the fire.</summary>
    private readonly IEngineWorldQuery World;

    /// <summary>
    /// What this flame is attached to, which is also its parent.
    ///
    /// <para>
    /// Held rather than read back through <c>Parent</c> because the two answer
    /// differently at exactly the moment that matters: an entity that has been
    /// removed drops out of the parent registry, so <c>Parent</c> would be
    /// <see cref="EntityHandle.None"/> and the flame would have nothing to
    /// notice had gone. Stock keeps the same handle for the same reason and
    /// tests it the same way.
    /// </para>
    /// </summary>
    private EntityHandle _attached = EntityHandle.None;

    private float _size = FlameRules.MinimumSize;
    private float _expiresAt;
    private bool _burningSoundPlaying;

    /// <summary>How big this flame is, in world units. What its reach is half of.</summary>
    public float Size => _size;

    /// <summary>What it is burning.</summary>
    public EntityHandle Attached => _attached;

    /// <summary>When it goes out, on the server clock.</summary>
    public float ExpiresAt => _expiresAt;

    /// <summary>
    /// Attaches this flame to something and starts it burning — stock's
    /// <c>CEntityFlame::Create</c> and <c>AttachToEntity</c> in one, because
    /// there is no reason to have a flame that is attached to nothing.
    ///
    /// <para>
    /// The SIZE comes from the target's own bounds unless the caller overrode
    /// it, which is stock's rule; <see cref="FlameRules.SizeForBounds"/> holds
    /// the arithmetic and the guards.
    /// </para>
    ///
    /// <para>
    /// The parenting is what makes it a flame ON something rather than a flame
    /// NEAR something: it follows the drum that rolls away, and it is how the
    /// client knows where to draw without a handle on the wire.
    /// </para>
    /// </summary>
    public void AttachTo(EntityHandle target, float lifetime, float sizeOverride)
    {
        _attached = target;

        var (mins, maxs) = Entities.GetBounds(target);

        // The target's own width unless a level designer overrode it, which is
        // stock's rule. Both routes go through the same guard, because a number
        // out of a .vmf is no more trustworthy than a pair of bounds.
        _size = FlameRules.SizeFor(mins, maxs, sizeOverride);

        Entities.SetOrigin(Handle, Entities.GetOrigin(target));

        // Parented AFTER the position is set, so the local offset the hierarchy
        // records is zero rather than the distance from wherever this was made.
        if (!SetParent(target))
        {
            // Not fatal, and said out loud rather than swallowed: an unparented
            // flame burns at the place it was lit and never moves, which looks
            // like a flame that is drawing in the wrong place rather than like
            // a refusal.
            Log.Warning($"[SourceSharp] ss_entityflame: could not parent to what it is burning\n");
        }

        // Stock's split, and it is about what is burning rather than about how:
        // flesh and objects sound different. IsPlayer is the only "is it alive"
        // this build can still ask — MyNPCPointer answers NULL for everything
        // since the AI left.
        var state = Net.EntityGetState(target, out _);
        bool flesh = (state & EntityState.Player) != 0;

        Sound.EntityEmitSound(Handle, flesh ? FlameRules.BurningFleshSound : FlameRules.BurningObjectSound);
        _burningSoundPlaying = true;

        SetLifetime(lifetime);
        SetNextThink(FlameRules.DamageInterval);
    }

    /// <summary>
    /// Moves the deadline — stock's <c>SetLifetime</c>, which is a duration from
    /// now and not an absolute time, however its callers spell it.
    /// </summary>
    public void SetLifetime(float lifetime) => _expiresAt = FlameRules.ExpiryFor(Clock.Now, lifetime);

    /// <summary>
    /// Burn, hurt, warm — stock's <c>FlameThink</c>, on stock's interval.
    /// </summary>
    public override void OnThink()
    {
        // Rescheduled FIRST, as stock does, so that every early return below
        // still leaves the flame thinking.
        SetNextThink(FlameRules.DamageInterval);

        // What it was burning has gone: a gib that faded, a prop that broke, a
        // ragdoll the LRU dropped. Stock's `else UTIL_Remove( this )`.
        if (!Entities.IsValid(_attached))
        {
            Extinguish(quietly: true);
            return;
        }

        if (Clock.Now >= _expiresAt)
        {
            Extinguish(quietly: false);
            return;
        }

        Vector origin = Origin;
        float radius = FlameRules.DamageRadius(_size);

        // The thing that is alight, hurt PRECISELY. Stock does this separately
        // from the splash and excludes the victim from it, and says why: the
        // splash's falloff depends on where the flame sits relative to the
        // body, which is not something a burn rate should depend on.
        Damage.TakeDamage(_attached, Handle, FlameRules.DirectDamage, DamageType.Burn | DamageType.Direct);

        Splash(origin, radius);

        // And the fires nearby get warmer, which is how a burning thing sets
        // light to the room it is in. EnvFire owns the registry, which is where
        // the native flame's FireSystem_AddHeatInRadius ended up: it reached
        // through SourceSharp_NotifyFireHeatInRadius and back into this list.
        // Called directly, because both ends are C# now.
        foreach (var fire in EnvFire.InRadius(_fires, origin, radius))
            fire.AddHeat(FlameRules.HeatPerPass);
    }

    /// <summary>
    /// Everything else standing in the flame.
    ///
    /// <para>
    /// A managed sphere query rather than <c>Damage.RadiusDamage</c>, and the
    /// reason is the exclusion. Stock passes <c>m_hEntAttached</c> as
    /// <c>RadiusDamage</c>'s ignore so the burning entity is not hit twice; that
    /// argument is a <c>CBaseEntity *</c>, and the entity this flame is attached
    /// to is usually not one. Handed to the engine unexcluded, the drum would
    /// take the splash at point-blank on top of its direct damage — five times
    /// stock's burn rate, and a twenty-second fire would finish it in four.
    /// </para>
    ///
    /// <para>
    /// This is <see cref="EnvFire"/>'s own shape, which decided the same
    /// question the same way for the same reason: sphere query, line of sight,
    /// per-entity damage. What it does NOT reproduce is the engine's distance
    /// falloff — flat damage inside the radius, as env_fire's burn is. Over a
    /// radius that is half the width of the thing on fire the difference is a
    /// fraction of one hit point; over env_fire's much larger reach it was
    /// already judged acceptable.
    /// </para>
    /// </summary>
    private void Splash(Vector origin, float radius)
    {
        Span<EntityHandle> nearby = stackalloc EntityHandle[64];
        int count = World.EntitiesInSphere(origin, radius, nearby);

        for (int i = 0; i < count; i++)
        {
            var other = nearby[i];

            // The flame itself, and the thing it is attached to — which took
            // its damage directly a moment ago. Stock's ignore.
            if (other == Handle || other == _attached || !Entities.IsValid(other))
                continue;

            var state = Net.EntityGetState(other, out _);

            if ((state & EntityState.TakesDamage) == 0)
                continue;

            Vector where = Entities.GetOrigin(other);

            // Not through walls. The TARGET is the ignored entity rather than
            // the flame: anything else in the way should stop it.
            var seen = World.TraceLine(origin, where, TraceMask.Brushes, other);

            if (seen.Hit)
                continue;

            Damage.TakeDamage(other, Handle, FlameRules.RadiusDamage, DamageType.Burn);
        }
    }

    /// <summary>
    /// Puts the flame out and removes it.
    ///
    /// <para>
    /// <paramref name="quietly"/> is the difference between a fire burning down
    /// and a fire whose fuel was taken away. Stock plays "General.StopBurning"
    /// on the first and nothing on the second, because the second's entity has
    /// already gone and there is nowhere for the sound to come from — and a
    /// stop-burning noise from the map origin is worse than silence.
    /// </para>
    /// </summary>
    private void Extinguish(bool quietly)
    {
        if (_burningSoundPlaying)
        {
            Sound.EntityStopSound(Handle, FlameRules.BurningFleshSound);
            Sound.EntityStopSound(Handle, FlameRules.BurningObjectSound);
            _burningSoundPlaying = false;

            if (!quietly)
                Sound.EntityEmitSound(Handle, FlameRules.StopBurningSound);
        }

        _attached = EntityHandle.None;
        Entities.Remove(Handle);
    }

    /// <summary>
    /// The sound stops with the entity, however it went.
    ///
    /// Stock's <c>CEntityFlame::UpdateOnRemove</c>, which exists for the case
    /// this class cannot see coming: the level ending, or the flame being
    /// removed by something other than its own think.
    /// </summary>
    public override void OnRemoved()
    {
        if (!_burningSoundPlaying)
            return;

        Sound.EntityStopSound(Handle, FlameRules.BurningFleshSound);
        Sound.EntityStopSound(Handle, FlameRules.BurningObjectSound);
        _burningSoundPlaying = false;
    }
}
