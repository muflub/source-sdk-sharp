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
/// Reports how close the nearest occupant is to a named point, as a fraction of
/// a radius. Maps drive continuous things with it — a sound getting louder, a
/// light brightening as somebody approaches.
/// </summary>
[ScriptedEntity(EntityNames.TriggerProximity, ScriptedEntityKind.Brush)]
public sealed class TriggerProximity : TriggerVolume
{
    public TriggerProximity(IGameClock clock)
        : base(clock)
    {
    }

    public readonly Output NearestEntityDistance = new();

    /// <summary>What a map may set on this entity. See <see cref="TriggerProximityVars"/>.</summary>
    [ScriptVars] public TriggerProximityVars TriggerProximityVars = new();

    // Every tick, because the value is meant to track movement smoothly. It only
    // runs while somebody is inside, so an empty volume costs nothing.
    private const float Interval = 0.0f;

    private float _lastReported = -1f;

    protected override void Fire(EntityHandle toucher) => SetNextThink(Interval);

    public override void OnThink() => PumpWhileOccupied(Interval);

    //-------------------------------------------------------------------------
    // Measured once per think over everyone inside, not once per occupant —
    // hence the pass over Inside here rather than work in WhileInside.
    //-------------------------------------------------------------------------
    protected override void WhileInside(EntityHandle toucher)
    {
        if (TriggerProximityVars.MeasureTarget.Length == 0 || TriggerProximityVars.Radius <= 0f)
            return;

        var measure = Entities.FindByName(TriggerProximityVars.MeasureTarget);
        if (!Entities.IsValid(measure))
            return;

        var origin = Entities.GetOrigin(measure);
        float nearest = float.MaxValue;

        foreach (var inside in Inside)
        {
            if (!Entities.IsValid(inside))
                continue;

            float distance = (Entities.GetOrigin(inside) - origin).Length;
            if (distance < nearest)
                nearest = distance;
        }

        if (nearest > TriggerProximityVars.Radius)
            return;

        // Only on change: this runs every tick, and a map wiring it to something
        // expensive should not pay for a value that has not moved.
        float fraction = nearest / TriggerProximityVars.Radius;
        if (fraction == _lastReported)
            return;

        _lastReported = fraction;
        NearestEntityDistance.Fire(fraction);
    }
}

/// <summary>
/// The map keys <see cref="TriggerProximity"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerProximityVars
{
    public string MeasureTarget { get; set; } = string.Empty;

    public float Radius { get; set; } = 256f;
}
