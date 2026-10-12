using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Reports when every logic_branch it watches becomes true, or stops being so.
/// </summary>
[ScriptedEntity(EntityNames.LogicBranchListener)]
public sealed class LogicBranchListener : ScriptedEntity
{
    public readonly Output OnAllTrue = new();
    public readonly Output OnAllFalse = new();
    public readonly Output OnMixed = new();

    private readonly Dictionary<uint, bool> _branches = new();

    [ScriptedEvent("_OnLogicBranchChanged")]
    private void OnBranchChanged(InputData input)
    {
        _branches[input.Activator.Value] = input.Number != 0.0f;
        OnTest();
    }

    [ScriptedEvent] private void OnTest()
    {
        if (_branches.Count == 0)
            return;

        if (_branches.Values.All(v => v))
            OnAllTrue.Fire();
        else if (_branches.Values.All(v => !v))
            OnAllFalse.Fire();
        else
            OnMixed.Fire();
    }
}
