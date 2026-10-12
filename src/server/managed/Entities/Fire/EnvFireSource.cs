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
/// env_firesource: heat with no flame.
///
/// <para>
/// It warms every <c>env_fire</c> in its radius, lit or not, which is how a map
/// starts a fire from something that is not itself on fire — a burning pipe, a
/// heater, a magnifying glass. Whether a warmed fire actually catches is the
/// fire's business, not this one's: it has to get past that fire's ignition
/// point first.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvFiresource)]
public sealed class EnvFireSource : ScriptedEntity
{
    /// <summary>The module's fires, which a source feeds.</summary>
    public EnvFireSource(IEntityRoster<EnvFire> fires)
    {
        _fires = fires;
    }

    private readonly IEntityRoster<EnvFire> _fires;

    /// <summary>What a map may set on this entity. See <see cref="EnvFireSourceVars"/>.</summary>
    [ScriptVars] public EnvFireSourceVars Vars = new();

    private const float ThinkInterval = 0.25f;
    private const int StartOnFlag = 1;

    private bool _enabled;

    public override void OnSpawn()
    {
        if ((SpawnFlags & StartOnFlag) != 0)
            TurnOn();
    }

    public override void OnThink()
    {
        if (!_enabled)
            return;

        SetNextThink(ThinkInterval);

        // Heat is a rate, so it is scaled by the interval — the same value
        // applied whole every quarter second would be four times the heat the
        // map asked for.
        float heat = Vars.Damage * ThinkInterval;

        foreach (var fire in EnvFire.InRadius(_fires, Origin, Vars.Radius))
            fire.AddHeat(heat);
    }

    private void TurnOn()
    {
        if (_enabled)
            return;

        _enabled = true;
        SetNextThink(0f);
    }

    private void TurnOff()
    {
        if (!_enabled)
            return;

        _enabled = false;
        StopThinking();
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => TurnOn();

    [ScriptedEvent("Disable")]
    private void OnDisable() => TurnOff();
}

/// <summary>
/// The map keys <see cref="EnvFireSource"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvFireSourceVars
{
    /// <summary>Heat per second. Multiplied by the interval, not applied whole.</summary>
    [VarName("firedamage")]
    public float Damage { get; set; }

    [VarName("fireradius")]
    public float Radius { get; set; }
}
