using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The map's mode switch (plan 0.7's generic name, in the core): a mapper
/// wires anything — a trigger, a logic_case's output, the round-end chain — to
/// this entity's <c>SwitchTo &lt;id&gt;</c> input, and the map changes the
/// game's mode through it, at runtime, with no code (D6: "a map entity can
/// change the mode at runtime").
///
/// <para>
/// It owns nothing beyond the plumbing: the switch itself is
/// <see cref="GameModes.SwitchTo"/>'s — flush, keep the players, setup,
/// warmup, round — and an unknown id is that method's log line and unchanged
/// game, exactly as for <c>sv_mode</c>. Stock has no equivalent entity (HL2MP
/// has no runtime mode), which is why the class name is ours to choose
/// (<c>game_mode_switch</c>, the plan's) and no Valve header is attached.
/// </para>
/// </summary>
[ScriptedEntity("game_mode_switch")]
public sealed class GameModeSwitch : ScriptedEntity
{
    private readonly GameModes? _modes;

    public GameModeSwitch(IServiceLocator services)
    {
        // Null is a composition without the proxy (an entity built by a test
        // that never wired the module's services): the input can then do
        // nothing, and says nothing rather than pretending.
        _modes = services.Resolve<GameModes>();
    }

    /// <summary>
    /// Switch to the named mode, the same route <c>sv_mode</c> takes. The
    /// mapper's <c>SwitchTo</c> input, value form: the id is the input's
    /// string ("SwitchTo <c>arena</c>" in the map's I/O). An empty value is
    /// the unknown-id arm and SwitchTo's log line, so it is passed through.
    /// </summary>
    [ScriptedEvent] private void OnSwitchTo(string value) => _modes?.SwitchTo(value.Trim());
}
