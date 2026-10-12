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
/// Rescales a value from one range to another — turning, say, a health fraction
/// into a light brightness.
/// </summary>
[ScriptedEntity(EntityNames.MathRemap)]
public sealed class MathRemap : ScriptedEntity
{
    /// <summary>
    /// math_remap's own spawnflag. Also 1, and also unrelated to the other three
    /// entities that read a 1 off their own words.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        IgnoreOutOfRange = 0b0000_0001,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    public readonly Output OutValue = new();

    /// <summary>What a map may set on this entity. See <see cref="MathRemapVars"/>.</summary>
    [ScriptVars] public MathRemapVars Vars = new();

    private bool _ignoreOutOfRange;

    public override void OnSpawn()
    {
        _ignoreOutOfRange = Flags.HasFlag(SpawnFlag.IgnoreOutOfRange);

        // An empty input range has no meaningful mapping and would divide by
        // zero. Widening it keeps the entity inert rather than fatal.
        if (Vars.InMin == Vars.InMax)
            Vars.InMax = Vars.InMin + 1.0f;
    }

    [ScriptedEvent] private void OnEnable() => Vars.Disabled = false;
    [ScriptedEvent] private void OnDisable() => Vars.Disabled = true;

    [ScriptedEvent] private void OnInValue(float value)
    {
        if (Vars.Disabled)
            return;

        if (_ignoreOutOfRange && (value < Vars.InMin || value > Vars.InMax))
            return;

        float clamped = Math.Clamp(value, Math.Min(Vars.InMin, Vars.InMax), Math.Max(Vars.InMin, Vars.InMax));
        float t = (clamped - Vars.InMin) / (Vars.InMax - Vars.InMin);
        OutValue.Fire(Vars.OutMin + t * (Vars.OutMax - Vars.OutMin));
    }
}

/// <summary>
/// The map keys <see cref="MathRemap"/> declares.
/// </summary>
[ScriptVars]
public partial class MathRemapVars
{
    [VarName("in1")]
    public float InMin { get; set; }

    [VarName("in2")]
    public float InMax { get; set; } = 1.0f;

    [VarName("out1")]
    public float OutMin { get; set; }

    [VarName("out2")]
    public float OutMax { get; set; } = 1.0f;

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }
}
