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
/// CLogicBranch (logicentities.cpp): remembers a boolean and reports it on
/// demand. A map uses this to hold state that outlives whatever set it. Every
/// logic_branch_listener that registered with it is told, through the event
/// queue, each time the value changes.
/// </summary>
[ScriptedEntity(EntityNames.LogicBranch)]
public sealed class LogicBranch : ScriptedEntity
{
    public readonly Output OnTrue = new();
    public readonly Output OnFalse = new();

    /// <summary>What a map may set on this entity. See <see cref="LogicBranchVars"/>.</summary>
    [ScriptVars] public LogicBranchVars Vars = new();

    /// <summary>m_Listeners: the logic_branch_listeners told of a change.</summary>
    private readonly List<EntityHandle> _listeners = new();

    [SaveField("listeners")]
    private EntityHandle[] ListenersSaved
    {
        get => _listeners.ToArray();
        set
        {
            _listeners.Clear();
            _listeners.AddRange(value.Where(h => h != EntityHandle.None));
        }
    }

    /// <summary>m_bInValue.</summary>
    public bool Value => Vars.Value;

    /// <summary>CLogicBranch::AddLogicBranchListener: once each.</summary>
    public void AddListener(EntityHandle listener)
    {
        if (!_listeners.Contains(listener))
            _listeners.Add(listener);
    }

    // SetValue and Toggle are deliberately silent: they store without reporting,
    // so a map can seed state without side effects. The Test forms report.
    [ScriptedEvent] private void OnSetValue(InputData input) => UpdateValue(input.Number != 0f, input.Activator, fire: false);

    [ScriptedEvent] private void OnSetValueTest(InputData input) => UpdateValue(input.Number != 0f, input.Activator, fire: true);

    [ScriptedEvent] private void OnToggle(InputData input) => UpdateValue(!Vars.Value, input.Activator, fire: false);

    [ScriptedEvent] private void OnToggleTest(InputData input) => UpdateValue(!Vars.Value, input.Activator, fire: true);

    [ScriptedEvent] private void OnTest(InputData input) => FireValue(input.Activator);

    /// <summary>CLogicBranch::UpdateValue: a change queues _OnLogicBranchChanged to every listener, as this.</summary>
    private void UpdateValue(bool value, EntityHandle activator, bool fire)
    {
        if (Vars.Value != value)
        {
            Vars.Value = value;

            foreach (EntityHandle listener in _listeners)
                QueueInput(listener, "_OnLogicBranchChanged", 0f, Handle);
        }

        if (fire)
            FireValue(activator);
    }

    private void FireValue(EntityHandle activator)
    {
        if (Vars.Value)
            OnTrue.Fire(activator);
        else
            OnFalse.Fire(activator);
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
