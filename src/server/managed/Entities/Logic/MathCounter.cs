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
/// Holds a number, clamped to a range, and reports it. The workhorse of map
/// scripting: counting pickups, tracking progress, gating on a threshold.
/// </summary>
[ScriptedEntity(EntityNames.MathCounter)]
public sealed class MathCounter : ScriptedEntity
{
    public readonly Output OutValue = new();
    public readonly Output OnHitMin = new();
    public readonly Output OnHitMax = new();
    public readonly Output OnGetValue = new();

    /// <summary>What a map may set on this entity. See <see cref="MathCounterVars"/>.</summary>
    [ScriptVars] public MathCounterVars Vars = new();

    // Limits only report on the transition. A counter sitting at its maximum
    // would otherwise fire OnHitMax on every further Add.
    private bool _atMin;
    private bool _atMax;

    [ScriptedEvent] private void OnAdd(float amount) => SetTo(Vars.Value + amount);
    [ScriptedEvent] private void OnSubtract(float amount) => SetTo(Vars.Value - amount);
    [ScriptedEvent] private void OnMultiply(float amount) => SetTo(Vars.Value * amount);
    [ScriptedEvent] private void OnDivide(float amount) => SetTo(amount != 0.0f ? Vars.Value / amount : Vars.Value);
    [ScriptedEvent] private void OnSetValue(float value) => SetTo(value);
    [ScriptedEvent] private void OnSetValueNoFire(float value) => Vars.Value = Clamp(value);
    [ScriptedEvent] private void OnSetHitMax(float value) { Vars.Max = value; SetTo(Vars.Value); }
    [ScriptedEvent] private void OnSetHitMin(float value) { Vars.Min = value; SetTo(Vars.Value); }
    [ScriptedEvent] private void OnEnable() => Vars.Disabled = false;
    [ScriptedEvent] private void OnDisable() => Vars.Disabled = true;

    /// <summary>Reports without changing anything, for a map reading the counter at a moment of its choosing.</summary>
    [ScriptedEvent("GetValue")] private void OnReportValue() => OnGetValue.Fire(Vars.Value);

    private void SetTo(float newValue)
    {
        if (Vars.Disabled)
            return;

        Vars.Value = Clamp(newValue);

        // Limits are only meaningful if the map set them: min == max == 0 is the
        // default and means unbounded, not pinned to zero.
        bool bounded = Vars.Min != 0.0f || Vars.Max != 0.0f;

        if (bounded && Vars.Value >= Vars.Max)
        {
            if (!_atMax) OnHitMax.Fire();
            _atMax = true;
        }
        else _atMax = false;

        if (bounded && Vars.Value <= Vars.Min)
        {
            if (!_atMin) OnHitMin.Fire();
            _atMin = true;
        }
        else _atMin = false;

        OutValue.Fire(Vars.Value);
    }

    private float Clamp(float v)
        => (Vars.Min == 0.0f && Vars.Max == 0.0f) ? v : Math.Clamp(v, Vars.Min, Vars.Max);
}

/// <summary>
/// The map keys <see cref="MathCounter"/> declares.
/// </summary>
[ScriptVars]
public partial class MathCounterVars
{
    [VarName("StartValue")]
    public float Value { get; set; }

    public float Min { get; set; }

    public float Max { get; set; }

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }
}
