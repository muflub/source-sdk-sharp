using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>
/// Whether the UI's menu layer holds the mouse and keyboard (plan_client_ui
/// U3's UiSetMenuInput): game input then leaves both alone, so one event is
/// never acted on twice.
/// </summary>
public interface IMenuInputGate
{
    bool MenuHasInput { get; }
}

/// <summary>The gate when there is no menu layer: never closed.</summary>
public sealed class NoMenuInputGate : IMenuInputGate
{
    public bool MenuHasInput => false;
}
