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
/// Shoves whoever is inside. Jump pads, wind tunnels, conveyor floors.
/// </summary>
[ScriptedEntity(EntityNames.TriggerPush, ScriptedEntityKind.Brush)]
public sealed class TriggerPush : TriggerVolume
{
    public TriggerPush(IGameClock clock)
        : base(clock)
    {
    }

    /// <summary>
    /// trigger_push's own spawnflag. It used to sit in
    /// <see cref="TriggerSpawnflags"/>, which is the SHARED set every trigger
    /// reads to decide what counts as touching it -- and this bit means nothing
    /// to any other trigger. It is 128, one place above that set's top bit,
    /// which is what a private bit on a shared word looks like.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None     = 0b0000_0000,

        PushOnce = 0b1000_0000,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="TriggerPushVars"/>.</summary>
    [ScriptVars] public TriggerPushVars TriggerPushVars = new();

    /// <summary>
    /// The push direction, computed ONCE in <see cref="OnSpawn"/> from the map's
    /// angle key, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, a rebuilt trigger would push everything
    /// along <c>Vector.Zero</c> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _direction;

    private const float Interval = 0.1f;

    public override void OnSpawn()
    {
        base.OnSpawn();

        // The engine converts the angles to a world direction, rotates that into
        // entity space, then rotates it back out again at every push. The two
        // rotations cancel, so the world direction is simply the facing the map
        // asked for.
        _direction = Angles.Forward(TriggerPushVars.PushDir);

        if (TriggerPushVars.Speed == 0f)
            TriggerPushVars.Speed = 100f;
    }

    protected override void Fire(EntityHandle toucher)
    {
        Shove(toucher);

        // A one-shot pad launches and then gets out of the way.
        if (Flags.HasFlag(SpawnFlag.PushOnce))
        {
            Remove();
            return;
        }

        SetNextThink(Interval);
    }

    protected override void WhileInside(EntityHandle toucher) => Shove(toucher);

    public override void OnThink() => PumpWhileOccupied(Interval);

    private void Shove(EntityHandle toucher)
    {
        // Clearing the ground only when shoving upwards: something being pushed
        // along a floor should stay on it, but something pushed up stays stuck
        // unless the floor is let go of.
        Entities.ApplyVelocityImpulse(toucher, _direction * TriggerPushVars.Speed, clearGround: _direction.Z > 0f);
    }
}

/// <summary>
/// The map keys <see cref="TriggerPush"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerPushVars
{
    /// <summary>Which way to shove, written by the map as angles rather than a vector.</summary>
    public QAngle PushDir { get; set; }

    public float Speed { get; set; } = 100f;
}
