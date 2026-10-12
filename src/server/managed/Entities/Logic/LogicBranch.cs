using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Remembers a boolean and reports it on demand. A map uses this to hold state
/// that outlives whatever set it.
/// </summary>
[ScriptedEntity(EntityNames.LogicBranch)]
public sealed class LogicBranch : ScriptedEntity
{
    public readonly Output OnTrue = new();
    public readonly Output OnFalse = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicBranchVars"/>.</summary>
    [ScriptVars] public LogicBranchVars Vars = new();

    // SetValue is deliberately silent: it stores without reporting, so a map can
    // seed state without side effects. Test is what reports.
    [ScriptedEvent] private void OnSetValue(float value) => Vars.Value = value != 0.0f;
    [ScriptedEvent] private void OnSetValueTest(float value) { Vars.Value = value != 0.0f; OnTest(); }
    [ScriptedEvent] private void OnToggle() => Vars.Value = !Vars.Value;
    [ScriptedEvent] private void OnToggleTest() { Vars.Value = !Vars.Value; OnTest(); }

    [ScriptedEvent] private void OnTest()
    {
        if (Vars.Value)
            OnTrue.Fire();
        else
            OnFalse.Fire();
    }
}

/// <summary>
/// The map keys <see cref="LogicBranch"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicBranchVars
{
    [VarName("InitialValue")]
    public bool Value { get; set; }
}
