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
/// Fires when whoever is inside looks at a named target for long enough. Maps
/// use it to react to a player noticing something.
/// </summary>
[ScriptedEntity(EntityNames.TriggerLook, ScriptedEntityKind.Brush)]
public sealed class TriggerLook : TriggerVolume
{
    public TriggerLook(IGameClock clock)
        : base(clock)
    {
    }

    public readonly Output OnTimeout = new();

    /// <summary>What a map may set on this entity. See <see cref="TriggerLookVars"/>.</summary>
    [ScriptVars] public TriggerLookVars TriggerLookVars = new();

    private const int UseVelocity = 256;
    private const float Interval = 0.1f;

    private float _lookedFor;
    private float _enteredAt;
    private bool _fired;
    private bool _timedOut;

    protected override void Fire(EntityHandle toucher)
    {
        _enteredAt = Clock.Now;
        SetNextThink(Interval);
    }

    public override void OnThink() => PumpWhileOccupied(Interval);

    protected override void WhileInside(EntityHandle toucher)
    {
        if (_fired || _timedOut || TriggerLookVars.Target.Length == 0)
            return;

        var lookTarget = Entities.FindByName(TriggerLookVars.Target);
        if (!Entities.IsValid(lookTarget))
            return;

        if (IsLookingAt(toucher, lookTarget))
        {
            _lookedFor += Interval;

            if (_lookedFor >= TriggerLookVars.LookTime)
            {
                _fired = true;
                OnTrigger.Fire();
            }

            return;
        }

        // Looking away resets the count rather than pausing it: the map asked
        // for a sustained look, not a cumulative one.
        _lookedFor = 0f;

        if (TriggerLookVars.Timeout > 0f && Clock.Now - _enteredAt >= TriggerLookVars.Timeout)
        {
            _timedOut = true;
            OnTimeout.Fire();
        }
    }

    /// <summary>
    /// Whether the toucher is facing the target closely enough. Measured from
    /// the EYE rather than the origin — a player's origin is at their feet, and
    /// from there anything overhead looks like it is behind them.
    /// </summary>
    private bool IsLookingAt(EntityHandle toucher, EntityHandle lookTarget)
    {
        // Some maps ask for the direction of travel instead, for a trigger that
        // reacts to heading somewhere rather than looking at it.
        Vector facing = (SpawnFlags & UseVelocity) != 0
            ? Entities.GetVelocity(toucher).Normalized
            : Angles.Forward(Entities.GetEyeAngles(toucher));

        var toTarget = (Entities.GetOrigin(lookTarget) - Entities.GetEyePosition(toucher)).Normalized;

        return facing.Dot(toTarget) > TriggerLookVars.FieldOfView;
    }

    public override void OnEndTouch(EntityHandle toucher)
    {
        base.OnEndTouch(toucher);

        // Leaving resets everything, so re-entering is a fresh attempt.
        _lookedFor = 0f;
        _timedOut = false;
    }
}

/// <summary>
/// The map keys <see cref="TriggerLook"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerLookVars
{
    /// <summary>Name of the thing to look at.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>How closely they must be facing it: a dot product, not an angle.</summary>
    public float FieldOfView { get; set; } = 0.9f;

    /// <summary>Seconds of looking needed.</summary>
    public float LookTime { get; set; } = 0.5f;

    /// <summary>Seconds after entering to give up and fire OnTimeout instead. Zero disables.</summary>
    public float Timeout { get; set; }
}
