using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The UI across a full client reload (plan_client_ui U2, U5; plan_client_novalve
/// §3.8): what the UI carries across its own reload (open screens, focus,
/// per-id state, windows), in one section of the client's.
/// </summary>
public sealed class UiCarryReload(UiLoader ui) : IReloadParticipant
{
    public void Save(ReloadWriter writer) => writer.Write(ui.Current?.Carry() ?? []);

    public void Restore(ReloadReader reader)
    {
        byte[] carried = reader.ReadBytes();
        if (carried.Length > 0)
            ui.Current?.Restore(carried);
    }
}

/// <summary><see cref="IDebugClientUi"/> over whichever UI is loaded now.</summary>
public sealed class UiDebugForwarder(UiLoader ui) : IDebugClientUi
{
    private static DebugClientUi None(bool menu)
        => new(false, 0, 0, 0, 0, 0, 0, 0, [], [], null, menu ? [] : null, false);

    public DebugClientUi Read(bool svg) => ui.Current?.Debug.Read(svg) ?? None(false);

    public DebugClientUi ReadMenu(bool svg) => ui.Current?.Debug.ReadMenu(svg) ?? None(true);

    public string Event(string argument) => ui.Current?.Debug.Event(argument) ?? "{\"error\":\"no UI loaded\"}";

    public IReadOnlyList<DebugClientUiFont> Fonts(bool sweep) => ui.Current?.Debug.Fonts(sweep) ?? [];
}
