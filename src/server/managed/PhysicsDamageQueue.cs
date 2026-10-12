//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The physics step's impact damage, queued and delivered after it: stock's
/// <c>CCollisionEvent::AddDamageEvent</c> and <c>UpdateDamageEvents</c>
/// (physics.cpp). An entity answering a collision (<c>PhysCallbackDamage</c>)
/// queues here; <see cref="PhysicsEventRouter"/> delivers the step's whole list
/// once every collision is heard.
///
/// <para>
/// <b>THE INFLICTOR GETS ITS VELOCITY BACK WHEN THE HIT CHANGED ITS VICTIM.</b>
/// The impact itself slowed the object that did the damage. If that damage
/// then killed the victim, removed it or made it non-solid, the object should
/// carry on through rather than bounce off something that is no longer there:
/// stock compares the victim's alive / marked-for-deletion / not-solid bits
/// around <c>TakeDamage</c> and, if they changed, puts the inflictor's
/// pre-impact velocity back, blended by its mass against the heaviest thing
/// it hit this step (<c>RestoreDamageInflictorState</c>), once per inflictor.
/// </para>
/// </summary>
public sealed class PhysicsDamageQueue : IPhysicsCrushes
{
    /// <summary>VPHYSICS_LARGE_OBJECT_MASS: an inflictor this heavy always gets all of it back.</summary>
    public const float LargeObjectMass = 500f;

    private readonly IEngineDamage _damage;
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;
    private readonly EntityDirectory _directory;

    private readonly List<Pending> _events = new();
    private readonly List<Inflictor> _inflictors = new();

    public PhysicsDamageQueue(IEngineDamage damage, IEngineEntities entities, IEnginePlayers players,
                              EntityDirectory directory)
    {
        _damage = damage;
        _entities = entities;
        _players = players;
        _directory = directory;
    }

    /// <summary>Events waiting for <see cref="Deliver"/>.</summary>
    public int Count => _events.Count;

    /// <summary>Inflictors whose velocity a delivery has put back, since the module started.</summary>
    public long RestoredTotal { get; private set; }

    /// <summary>
    /// One side's impact damage. The inflictor is the other side of
    /// <paramref name="ev"/>; its pre-impact motion is saved here, while the
    /// step is still queueing, and the heaviest victim it hits this step is
    /// what its velocity is blended against.
    /// </summary>
    public void Add(EntityHandle victim, EntityHandle attacker, float damage, DamageType damageType,
                    in PhysicsEvent ev, int victimIndex)
    {
        // AddDamageEvent: a victim already marked for deletion takes nothing.
        if (_directory.IsRemoved(victim))
            return;

        int other = 1 - victimIndex;
        uint inflictorRaw = other == 0 ? ev.Entity0 : ev.Entity1;

        // AddDamageEvent: no velocity to restore for an inflictor that is not
        // an entity or that the solver does not move.
        int inflictor = -1;
        if (inflictorRaw != PhysicsEvent.NoEntity && ev.IsMoveable(other))
        {
            var handle = new EntityHandle(inflictorRaw);
            float victimMass = victimIndex == 0 ? ev.Mass0 : ev.Mass1;

            inflictor = _inflictors.FindIndex(i => i.Handle == handle);
            if (inflictor >= 0)
            {
                // A bigger mass than any it hit so far this step.
                var state = _inflictors[inflictor];
                if (victimMass > state.OtherMassMax)
                    _inflictors[inflictor] = state with { OtherMassMax = victimMass };
            }
            else
            {
                uint flags = other == 0 ? ev.GameFlags0 : ev.GameFlags1;
                _inflictors.Add(new Inflictor(
                    handle,
                    other == 0 ? ev.Mass0 : ev.Mass1,
                    (flags & PhysicsGameFlags.SliceDamage) != 0,
                    ev.PreVelocity(other),
                    ev.PreAngularVelocity(other),
                    victimMass,
                    Restored: false));
                inflictor = _inflictors.Count - 1;
            }
        }

        _events.Add(new Pending(victim, attacker, damage, damageType, ev.Point, inflictor, Force: null));
    }

    /// <summary>
    /// A freeze crush (physics stock parity G): stock's ShouldFreezeObject
    /// queues DMG_CRUSH on debris something heavy rests on, from the object
    /// pressing hardest, with that contact's force and point
    /// (PhysCallbackDamage, so after the step). No inflictor to restore.
    /// </summary>
    public void AddCrush(EntityHandle victim, EntityHandle attacker, float damage, Vector force, Vector point)
    {
        // AddDamageEvent: a victim already marked for deletion takes nothing.
        if (_directory.IsRemoved(victim))
            return;

        Crushes++;
        _events.Add(new Pending(victim, attacker, damage, DamageType.Crush, point, Inflictor: -1, Force: force));
    }

    /// <summary>How many freeze crushes were queued, for cs_sv_physics_solver.</summary>
    public long Crushes { get; private set; }

    /// <summary>
    /// A stress crush (B5): stock's ApplyStressDamage, DMG_CRUSH from the world
    /// with no inflictor to restore, which stock applies inside VPhysicsUpdate
    /// during the same PhysFrame as the damage events. Delivered with them, in
    /// order. This ABI has no world attacker: None, which the engine side
    /// answers with the victim.
    /// </summary>
    public void AddStress(EntityHandle victim, float damage, Vector point)
    {
        StressCrushes++;
        _events.Add(new Pending(victim, EntityHandle.None, damage, DamageType.Crush, point, Inflictor: -1, Force: null));
    }

    /// <summary>How many stress crushes were queued, for cs_sv_physics_events.</summary>
    public long StressCrushes { get; private set; }

    /// <summary>
    /// UpdateDamageEvents: every queued hit in order, and the inflictor's
    /// velocity back after any hit that changed its victim's state.
    /// </summary>
    public void Deliver()
    {
        for (int i = 0; i < _events.Count; i++)
        {
            Pending hit = _events[i];

            int before = StateBits(hit.Victim);
            // A crush carries its contact force and point, as stock's
            // CTakeDamageInfo did; an impact carries its point.
            if (hit.Force is { } force)
                _damage.ApplyAt(hit.Victim, hit.Attacker, hit.Damage, hit.DamageType, force, hit.Point);
            else
                _damage.TakeDamage(hit.Victim, hit.Attacker, hit.Damage, hit.DamageType,
                                   direction: null, hit.Point);
            int after = StateBits(hit.Victim);

            if (hit.Inflictor >= 0 && before != after)
                Restore(hit.Inflictor);
        }

        _events.Clear();
        _inflictors.Clear();
    }

    /// <summary>
    /// The three facts stock tracks across the hit: alive, marked for
    /// deletion, not solid. Only a player has a life state here; any other
    /// entity is alive until it is removed.
    /// </summary>
    private int StateBits(EntityHandle victim)
    {
        bool alive = !_entities.IsPlayer(victim) || _players.IsAlive(victim);
        int bits = alive ? 0x1 : 0;
        bits |= _directory.IsRemoved(victim) ? 0x2 : 0;
        bits |= (_entities.GetSolidFlags(victim) & SolidFlags.NotSolid) != 0 ? 0x4 : 0;
        return bits;
    }

    private void Restore(int index)
    {
        Inflictor state = _inflictors[index];
        if (state.Restored)
            return;

        // So we only restore this one once.
        _inflictors[index] = state with { Restored = true };

        float blend = VelocityBlend(state.Mass, state.OtherMassMax, state.Slice);
        if (blend <= 0f)
            return;

        Vector velocity = _entities.GetVelocity(state.Handle);
        _entities.SetVelocity(state.Handle, state.SavedVelocity * blend + velocity * (1f - blend));

        // Stock blends the angular velocity in the object's own frame, where
        // the record saved it; the entity area reads and writes it in the
        // world's, so the saved one is turned into the world's first.
        Vector angles = _entities.GetAngles(state.Handle);
        Vector savedWorld = Transform.FromOriginAngles(Vector.Zero, new QAngle(angles.X, angles.Y, angles.Z))
                                     .TransformPoint(state.SavedAngularVelocity);
        Vector spin = _entities.GetAngularVelocity(state.Handle);
        _entities.SetAngularVelocity(state.Handle, savedWorld * blend + spin * (1f - blend));

        RestoredTotal++;
    }

    /// <summary>
    /// How much of its pre-impact velocity an inflictor gets back: all of it
    /// when it is large or a slicer, otherwise the mass ratio against the
    /// heaviest thing it hit, clamped to 0.1..10 and mapped onto 0..0.5 below
    /// one and 0.5..1 above.
    /// </summary>
    public static float VelocityBlend(float inflictorMass, float otherMassMax, bool slice)
    {
        if (inflictorMass >= LargeObjectMass || slice)
            return 1f;

        float otherMass = otherMassMax > 0f ? otherMassMax : 1f;
        float ratio = Math.Clamp(inflictorMass / otherMass, 0.1f, 10f);

        return ratio < 1f
            ? Remap(ratio, 0.1f, 1f, 0f, 0.5f)
            : Remap(ratio, 1f, 10f, 0.5f, 1f);
    }

    private static float Remap(float value, float a, float b, float c, float d)
        => c + (d - c) * (value - a) / (b - a);

    private readonly record struct Pending(EntityHandle Victim, EntityHandle Attacker, float Damage,
                                           DamageType DamageType, Vector Point, int Inflictor, Vector? Force);

    private readonly record struct Inflictor(EntityHandle Handle, float Mass, bool Slice,
                                             Vector SavedVelocity, Vector SavedAngularVelocity,
                                             float OtherMassMax, bool Restored);
}

/// <summary>Where a freeze crush is queued (the step's damage queue): delivered after the step.</summary>
public interface IPhysicsCrushes
{
    void AddCrush(EntityHandle victim, EntityHandle attacker, float damage, Vector force, Vector point);
}
