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
/// Reports whether every value it has been given is equal — either to each other
/// or to a configured reference.
/// </summary>
[ScriptedEntity(EntityNames.LogicMulticompare)]
public sealed class LogicMultiCompare : ScriptedEntity
{
    public readonly Output OnEqual = new();
    public readonly Output OnNotEqual = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicMultiCompareVars"/>.</summary>
    [ScriptVars] public LogicMultiCompareVars Vars = new();

    private readonly List<int> _received = new();

    [ScriptedEvent] private void OnInputValue(float value) => _received.Add((int)value);

    [ScriptedEvent] private void OnCompareValues()
    {
        if (_received.Count == 0)
        {
            OnNotEqual.Fire();
            return;
        }

        // Without a configured reference the first value received becomes it,
        // which is how the entity answers "are these all the same?".
        int reference = Vars.CompareToValue ? Vars.IntegerValue : _received[0];

        if (_received.All(v => v == reference))
            OnEqual.Fire();
        else
            OnNotEqual.Fire();

        _received.Clear();
    }
}

/// <summary>
/// The map keys <see cref="LogicMultiCompare"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicMultiCompareVars
{
    public int IntegerValue { get; set; }

    [VarName("ShouldComparetoValue")]
    public bool CompareToValue { get; set; }
}
