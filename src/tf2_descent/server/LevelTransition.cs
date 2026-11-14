using SourceSharp.Abi;

namespace TF2.Descent.Server;

/// <summary>
/// <c>logic_level_transition</c>: map-tools' ModEntityContract v1 (the class
/// the linker writes into each generated level's transition rooms, and the one
/// tf2_descent_town's mine uses). Server-only. Its <c>Transition</c> input sends
/// the activating player along <c>direction</c>; <c>Enable</c> and
/// <c>Disable</c> gate it; <c>OnTransition</c> fires when a request is made.
/// <c>map</c> is the linker's record of where it leads: the service decides
/// the actual destination. In the town a way down opens the depth picker
/// instead (plan_descent_logic WO8, mine plan E1): the hero picks the depth,
/// and the picker's choice travels. On a boss level the way down is closed while the boss
/// stands (mine plan D6, HO13: <see cref="Horde.Boss.IBossExit"/>): a hero who asks is told why
/// and nothing fires.
/// </summary>
[ScriptedEntity("logic_level_transition")]
public sealed class LevelTransition(TravelService travel, Town.TownService town, Horde.Boss.IBossExit bossExit) : ScriptedEntity
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

        int player = Entities.GetIndex(input.Activator);
        if (direction == TravelDirection.Down && !town.InTown && !bossExit.MayLeave(player))
            return;

        OnTransition.Fire();
        if (direction == TravelDirection.Down && town.InTown && town.OpenDepth(player))
            return;
        travel.Request(player, direction, DepthOf(Vars.Map));
    }

    /// <summary>
    /// The depth a transition's <c>map</c> names, <c>tf2_descent-&lt;depth&gt;</c>
    /// (the town's cage names <c>tf2_descent-1</c>), or 0 when it names none.
    /// Read only for a descent from the town; a level's way down always leads
    /// to the next depth.
    /// </summary>
    public static int DepthOf(string map)
    {
        const string prefix = "tf2_descent-";
        if (!map.StartsWith(prefix, StringComparison.Ordinal))
            return 0;

        ReadOnlySpan<char> rest = map.AsSpan(prefix.Length);
        int dash = rest.IndexOf('-');
        return int.TryParse(dash < 0 ? rest : rest[..dash], System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out int depth)
            ? depth
            : 0;
    }
}

[ScriptVars]
public partial class LevelTransitionVars
{
    public string Direction { get; set; } = "down";

    public string Map { get; set; } = string.Empty;

    public bool StartDisabled { get; set; }
}
