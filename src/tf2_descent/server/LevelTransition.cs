using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// <c>logic_level_transition</c>: map-tools' ModEntityContract v1 (the class
/// the linker writes into each generated level's transition rooms, and the one
/// tf2_descent_town's mine uses). Server-only. Its <c>Transition</c> input sends
/// the activating player along <c>direction</c>; <c>Enable</c> and
/// <c>Disable</c> gate it; <c>OnTransition</c> fires when a request is made.
/// <c>map</c> is the linker's record of where it leads: the service decides
/// the actual destination.
/// </summary>
[ScriptedEntity("logic_level_transition")]
public sealed class LevelTransition(TravelService travel) : ScriptedEntity
{
    public readonly Output OnTransition = new();

    [ScriptVars] public LevelTransitionVars Vars = new();

    private bool _disabled;

    public override void OnSpawn() => _disabled = Vars.StartDisabled;

    [ScriptedEvent] private void OnEnable() => _disabled = false;

    [ScriptedEvent] private void OnDisable() => _disabled = true;

    [ScriptedEvent("Transition")]
    private void OnTransitionInput(InputData input)
    {
        if (_disabled || input.Activator.Equals(EntityHandle.None))
            return;

        var direction = string.Equals(Vars.Direction, "up", StringComparison.OrdinalIgnoreCase)
            ? TravelDirection.Up
            : TravelDirection.Down;

        OnTransition.Fire();
        travel.Request(Entities.GetIndex(input.Activator), direction);
    }
}

[ScriptVars]
public partial class LevelTransitionVars
{
    public string Direction { get; set; } = "down";

    public string Map { get; set; } = string.Empty;

    public bool StartDisabled { get; set; }
}
