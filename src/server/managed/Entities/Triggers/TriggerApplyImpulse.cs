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
/// Throws whoever is inside once, in a fixed direction. Unlike a push it does
/// not act continuously — it is a kick, not a wind.
/// </summary>
[ScriptedEntity(EntityNames.TriggerApplyImpulse, ScriptedEntityKind.Brush)]
public sealed class TriggerApplyImpulse : TriggerVolume
{
    public TriggerApplyImpulse(IGameClock clock)
        : base(clock)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="TriggerApplyImpulseVars"/>.</summary>
    [ScriptVars] public TriggerApplyImpulseVars TriggerApplyImpulseVars = new();

    /// <summary>
    /// The kick direction, computed ONCE in <see cref="OnSpawn"/> from the map's
    /// angle key, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, a rebuilt trigger would kick everything
    /// along <c>Vector.Zero</c> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _direction;

    public override void OnSpawn()
    {
        base.OnSpawn();
        _direction = Angles.Forward(TriggerApplyImpulseVars.ImpulseDir);
    }

    // Only on the input, not on touch: the map decides when the kick happens.
    [ScriptedEvent("ApplyImpulse")] private void OnImpulseRequested()
    {
        foreach (var toucher in Inside)
        {
            if (Entities.IsValid(toucher))
                Entities.ApplyVelocityImpulse(toucher, _direction * TriggerApplyImpulseVars.Force, clearGround: true);
        }
    }

    protected override void Fire(EntityHandle toucher) => OnTrigger.Fire();
}

/// <summary>
/// The map keys <see cref="TriggerApplyImpulse"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerApplyImpulseVars
{
    [VarName("impulse_dir")]
    public QAngle ImpulseDir { get; set; }

    public float Force { get; set; } = 300f;
}
