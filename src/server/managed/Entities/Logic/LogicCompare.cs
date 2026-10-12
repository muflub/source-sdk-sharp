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
/// Compares a value against a reference and reports how they relate.
/// </summary>
[ScriptedEntity(EntityNames.LogicCompare)]
public sealed class LogicCompare : ScriptedEntity
{
    public readonly Output OnLessThan = new();
    public readonly Output OnEqualTo = new();
    public readonly Output OnNotEqualTo = new();
    public readonly Output OnGreaterThan = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicCompareVars"/>.</summary>
    [ScriptVars] public LogicCompareVars Vars = new();

    [ScriptedEvent] private void OnSetValue(float value) => Vars.Value = value;
    [ScriptedEvent] private void OnSetValueCompare(float value) { Vars.Value = value; OnCompare(); }
    [ScriptedEvent] private void OnSetCompareValue(float value) => Vars.CompareValue = value;

    [ScriptedEvent] private void OnCompare()
    {
        if (Vars.Value == Vars.CompareValue)
        {
            OnEqualTo.Fire(Vars.Value);
            return;
        }

        // NotEqualTo fires alongside the direction, not instead of it: a map may
        // care only that they differ.
        OnNotEqualTo.Fire(Vars.Value);

        if (Vars.Value > Vars.CompareValue)
            OnGreaterThan.Fire(Vars.Value);
        else
            OnLessThan.Fire(Vars.Value);
    }
}

/// <summary>
/// The map keys <see cref="LogicCompare"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicCompareVars
{
    [VarName("InitialValue")]
    public float Value { get; set; }

    public float CompareValue { get; set; }
}
