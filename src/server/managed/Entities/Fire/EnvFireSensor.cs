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
/// env_firesensor: notices when it gets hot, and when it stops being hot.
///
/// <para>
/// It sums the heat of every LIT fire in its radius and compares that to a
/// level the map set. The interesting part is that the level has to be HELD:
/// crossing it for an instant does nothing, and only after <c>heattime</c>
/// seconds above it does the sensor fire. That is what separates "something is
/// burning nearby" from "this room is on fire", and it is why a map can use one
/// to open a sprinkler without the flicker of a passing flame setting it off.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvFiresensor)]
public sealed class EnvFireSensor : ScriptedEntity
{
    /// <summary>The module's fires, which a sensor measures.</summary>
    public EnvFireSensor(IEntityRoster<EnvFire> fires)
    {
        _fires = fires;
    }

    private readonly IEntityRoster<EnvFire> _fires;

    /// <summary>What a map may set on this entity. See <see cref="EnvFireSensorVars"/>.</summary>
    [ScriptVars] public EnvFireSensorVars Vars = new();

    private const int StartOnFlag = 1;

    public readonly Output OnHeatLevelStart = new();
    public readonly Output OnHeatLevelEnd = new();

    private bool _enabled;
    private bool _atLevel;
    private float _levelTime;

    public override void OnSpawn()
    {
        if ((SpawnFlags & StartOnFlag) != 0)
            TurnOn();
    }

    public override void OnThink()
    {
        if (!_enabled)
            return;

        // It samples four times over the window it is measuring, so the hold is
        // resolved to a quarter of its own length rather than to a fixed tick —
        // a sensor watching for ten seconds does not need to look ten times a
        // second. The floor stops a tiny heattime from thinking every frame.
        float interval = MathF.Max(Vars.TargetTime * 0.25f, 0.1f);
        SetNextThink(interval);

        float heat = 0f;

        foreach (var fire in EnvFire.InRadius(_fires, Origin, Vars.Radius, activeOnly: true))
            heat += fire.HeatLevel;

        if (heat >= Vars.TargetLevel)
        {
            _levelTime += interval;

            if (_levelTime >= Vars.TargetTime && !_atLevel)
            {
                _atLevel = true;
                OnHeatLevelStart.Fire();
            }
        }
        else
        {
            // Falling below resets the clock entirely rather than counting
            // down, so heat that comes and goes never accumulates into a hold.
            _levelTime = 0f;

            if (_atLevel)
            {
                _atLevel = false;
                OnHeatLevelEnd.Fire();
            }
        }
    }

    private void TurnOn()
    {
        if (_enabled)
            return;

        _enabled = true;
        _atLevel = false;
        _levelTime = 0f;
        SetNextThink(0f);
    }

    private void TurnOff()
    {
        if (!_enabled)
            return;

        _enabled = false;
        StopThinking();

        // Switching a sensor off ends the condition it was reporting. Stock
        // fires this deliberately: whatever the start opened should close.
        if (_atLevel)
        {
            _atLevel = false;
            OnHeatLevelEnd.Fire();
        }
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => TurnOn();

    [ScriptedEvent("Disable")]
    private void OnDisable() => TurnOff();
}

/// <summary>
/// The map keys <see cref="EnvFireSensor"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvFireSensorVars
{
    [VarName("fireradius")]
    public float Radius { get; set; }

    /// <summary>Summed heat that counts as hot.</summary>
    [VarName("heatlevel")]
    public float TargetLevel { get; set; }

    /// <summary>How long it has to stay hot before this says so.</summary>
    [VarName("heattime")]
    public float TargetTime { get; set; }
}
