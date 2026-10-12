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
/// Reports how fast something is moving along an axis, as a number a map can
/// feed to anything that takes one.
/// </summary>
[ScriptedEntity(EntityNames.PointVelocitysensor)]
public sealed class PointVelocitySensor : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="PointVelocitySensorVars"/>.</summary>
    [ScriptVars] public PointVelocitySensorVars Vars = new();

    public readonly Output Velocity = new("Velocity");

    private EntityHandle _watched = EntityHandle.None;
    private Vector _direction;
    private float _last = float.NaN;

    public override void OnSpawn()
    {
        _direction = (Vars.Axis - Origin).Normalized;

        if (Vars.Enabled)
            SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (!Vars.Enabled)
            return;

        if (!Entities.IsValid(_watched))
        {
            _watched = Entities.FindByName(Vars.Target);

            if (!Entities.IsValid(_watched))
            {
                Log.Warning($"[SourceSharp] point_velocitysensor '{Name}': no entity named '{Vars.Target}'\n");
                return;
            }
        }

        // An unset axis means "any direction", which stock reports as a flat 1
        // rather than a speed. Kept, because a map built against that behaviour
        // is comparing to 1.
        float speed = _direction.LengthSquared > 0f
            ? Entities.GetVelocity(_watched).Dot(_direction)
            : 1f;

        if (speed != _last)
        {
            Velocity.Fire(speed);
            _last = speed;
        }

        SetNextThink(0f);
    }

    [ScriptedEvent("Enable")]
    private void OnEnable()
    {
        if (Vars.Enabled)
            return;

        Vars.Enabled = true;
        SetNextThink(0f);
    }

    [ScriptedEvent("Disable")]
    private void OnDisable()
    {
        Vars.Enabled = false;
        StopThinking();
    }
}

/// <summary>
/// The map keys <see cref="PointVelocitySensor"/> declares.
/// </summary>
[ScriptVars]
public partial class PointVelocitySensorVars
{
    public string Target { get; set; } = string.Empty;

    /// <summary>A world point; the axis is the line from here to it.</summary>
    public Vector Axis { get; set; }

    public bool Enabled { get; set; } = true;
}
