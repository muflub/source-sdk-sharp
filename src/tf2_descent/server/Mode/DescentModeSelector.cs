using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Mode;

/// <summary>
/// TF2 Descent plays <see cref="DescentMode"/> on every map: a chain member
/// (<see cref="IGameModeSelector"/>) after TF2's selector, which answers only
/// on a map with a TF objective, and before the core's own last answer. A
/// mode asked for by name (<c>sv_mode_next</c>) still comes first.
/// </summary>
[SideService(Side.Server)]
public sealed class DescentModeSelector : IGameModeSelector, IOrderedHook
{
    /// <summary>After TF2's band (999,000-999,999), at TF2 Descent's first mode stage.</summary>
    public const int Order = ModStages.First + 100;

    public int HookOrder => Order;

    public string? PickMode() => DescentMode.ModeId;
}

/// <summary>Whether the level plays <see cref="DescentMode"/>: what the mode's own rules ask before they act.</summary>
public interface IDescentModeState
{
    bool Active { get; }
}

/// <summary>The active mode as the core's mode layer has it.</summary>
[SideService(Side.Server, As = [typeof(IDescentModeState)])]
public sealed class DescentModeState(GameModes modes) : IDescentModeState
{
    public bool Active => modes.Active is DescentMode;
}
