using System.Diagnostics;

using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Vgui;

/// <summary>The UI's scale on top of the screen's (plan_client_ui §5.1): for DPI and taste.</summary>
[ConVar("cs_ui_scale", "1", Flags = ConVarFlags.Archive,
        Help = "Scale the UI by this on top of the screen's own scale (0.5 .. 3)")]
public sealed class UiScaleConVar : FloatConVar
{
}

/// <summary>What the HUD layer's frames have cost, for /ui and the U2 report.</summary>
public sealed class UiFrameCost
{
    public long Frames { get; private set; }

    public long TotalTicks { get; private set; }

    public long MaxTicks { get; private set; }

    public long LastTicks { get; private set; }

    public double MeanMicroseconds => Frames == 0 ? 0 : TotalTicks * 1e6 / Stopwatch.Frequency / Frames;

    public double LastMicroseconds => LastTicks * 1e6 / Stopwatch.Frequency;

    public double MaxMicroseconds => MaxTicks * 1e6 / Stopwatch.Frequency;

    public void Add(long ticks)
    {
        Frames++;
        TotalTicks += ticks;
        LastTicks = ticks;
        if (ticks > MaxTicks)
            MaxTicks = ticks;
    }

    public void Reset() => (Frames, TotalTicks, MaxTicks, LastTicks) = (0, 0, 0, 0);
}

/// <summary>
/// THE UI AS THE CORE SEES IT (plan_client_ui U5): what the UI host assembly
/// builds in its own load context. The core never names a toolkit or screen
/// type, so a UI-only deploy replaces all of it without the core.
/// </summary>
public interface IUiRuntime : IUiClientEvents, IDisposable
{
    /// <summary>The server's VGUIMenu, before the native viewport: true when the UI took it.</summary>
    bool TakePanel(string name, bool show, IReadOnlyList<KeyValuePair<string, string>> data);

    /// <summary>Whether the UI has a screen for stock viewport panel <paramref name="panel"/>.</summary>
    bool Answers(string panel);

    bool Has(string screen);

    /// <summary>A new map (game_newmap): the viewport's screens close, as stock hid its panels.</summary>
    void NewMap();

    /// <summary>
    /// Whether the menu root holds the mouse and keyboard (what the menu layer
    /// asked UiSetMenuInput for): the game input's gate (lane c6-input).
    /// </summary>
    bool MenuInput { get; }

    void Open(string screen);

    void Close(string screen);

    /// <summary>The /client/{slot}/ui routes over this runtime.</summary>
    IDebugClientUi Debug { get; }

    /// <summary>Everything a reload carries: open screens, focus, per-id state, windows.</summary>
    byte[] Carry();

    /// <summary><see cref="Carry"/>'s bytes from the build before; answers the screens reopened.</summary>
    int Restore(byte[] carried);
}

/// <summary>What the UI host's composition is given: the core's sources and services.</summary>
public sealed record UiRuntimeServices(
    ServiceContainer Services, ILog Log, Hud Hud, ILocalArsenal Arsenal, UiScaleConVar? Scale,
    Func<IVtableEngineClient?> Engine, Func<IVtableInputSystem?> Input, Func<IClientStringTables?>? StringTables = null);

/// <summary>The UI host assembly's entry: the one public type implementing this is constructed and asked.</summary>
public interface IUiRuntimeFactory
{
    IUiRuntime Create(UiRuntimeServices services);
}

/// <summary>Opens or closes a UI screen by name: cs_ui_open console, cs_ui_open serverbrowser.</summary>
[ConCommand("cs_ui_open", Help = "Open a UI screen: scoreboard, teammenu, motd, console, serverbrowser, mouseoptions, keybindings")]
public sealed class UiOpenCommand(UiLoader ui, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string name = args.Trim();
        if (name.Length == 0 || ui.Current is not { } runtime || !runtime.Has(name))
        {
            log.Msg("usage: cs_ui_open <scoreboard|teammenu|motd|console|serverbrowser|mouseoptions|keybindings>\n");
            return;
        }
        runtime.Open(name);
    }
}

/// <summary>Closes a UI screen by name.</summary>
[ConCommand("cs_ui_close", Help = "Close a UI screen by name")]
public sealed class UiCloseCommand(UiLoader ui) : IConCommand
{
    public void Execute(string args) => ui.Current?.Close(args.Trim());
}
