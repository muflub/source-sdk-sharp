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
/// CLogicBranchList (logicentities.cpp): logic_branch_listener. Its Branch01..16
/// keys name the logic_branches it watches. It registers with each when it
/// activates, and reports when all of them are true, all false, or mixed,
/// only when that answer changes. Test reports it again regardless.
/// </summary>
[ScriptedEntity(EntityNames.LogicBranchListener)]
[ClaimsKeys("Branch", 1, 16)]
public sealed class LogicBranchListener : ScriptedEntity
{
    public readonly Output OnAllTrue = new();
    public readonly Output OnAllFalse = new();
    public readonly Output OnMixed = new();

    private const int MaxBranchNames = 16;

    /// <summary>m_nLogicBranchNames.</summary>
    private readonly string[] _names = new string[MaxBranchNames];

    /// <summary>m_LogicBranchList.</summary>
    private readonly List<EntityHandle> _branches = new();

    [SaveField("branches")]
    private EntityHandle[] BranchesSaved
    {
        get => _branches.ToArray();
        set
        {
            _branches.Clear();
            _branches.AddRange(value.Where(h => h != EntityHandle.None));
        }
    }

    /// <summary>LogicBranchListenerLastState_t.</summary>
    private enum LastState { NotInit, AllTrue, AllFalse, Mixed }

    [SaveField] private LastState _last;

    public override bool OnKeyValue(string key, string value)
    {
        if (key.Length == 8 && key.StartsWith("branch", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(6), out int index)
            && index >= 1 && index <= MaxBranchNames)
        {
            _names[index - 1] = value;
            return true;
        }

        return false;
    }

    /// <summary>CLogicBranchList::Activate: register with every logic_branch each name finds.</summary>
    public override void OnServerActivate()
    {
        foreach (string? name in _names)
        {
            if (string.IsNullOrEmpty(name))
                continue;

            // Through the registry's own index: a logic_branch is a logical
            // entity, which is where it is found.
            if (Owner is null)
                return;

            using NameMatches found = Owner.Named(name, int.MaxValue);
            foreach (EntityHandle entity in found)
            {
                if (FindByHandle<LogicBranch>(entity) is { } branch)
                {
                    branch.AddListener(Handle);
                    _branches.Add(entity);
                }
                else
                {
                    Log.Msg($"logic_branchlist {Name} refers to entity {name}, which is not a logic_branch\n");
                }
            }
        }
    }

    [ScriptedEvent("_OnLogicBranchChanged")]
    private void OnBranchChanged(InputData input) => DoTest(input.Activator);

    [ScriptedEvent("_OnLogicBranchRemoved")]
    private void OnBranchRemoved(InputData input)
    {
        _branches.Remove(input.Activator);

        // See if this logic_branch's deletion affects the final result.
        DoTest(input.Activator);
    }

    /// <summary>InputTest: force an output.</summary>
    [ScriptedEvent]
    private void OnTest(InputData input)
    {
        _last = LastState.NotInit;
        DoTest(input.Activator);
    }

    /// <summary>CLogicBranchList::DoTest: fire only when the new result differs from the last.</summary>
    private void DoTest(EntityHandle activator)
    {
        bool oneTrue = false, oneFalse = false;

        foreach (EntityHandle entity in _branches)
        {
            if (FindByHandle<LogicBranch>(entity) is { Value: true })
                oneTrue = true;
            else
                oneFalse = true;
        }

        if (oneTrue && !oneFalse)
        {
            if (_last != LastState.AllTrue)
            {
                OnAllTrue.Fire(activator);
                _last = LastState.AllTrue;
            }
        }
        else if (oneFalse && !oneTrue)
        {
            if (_last != LastState.AllFalse)
            {
                OnAllFalse.Fire(activator);
                _last = LastState.AllFalse;
            }
        }
        else if (_last != LastState.Mixed)
        {
            OnMixed.Fire(activator);
            _last = LastState.Mixed;
        }
    }
}
