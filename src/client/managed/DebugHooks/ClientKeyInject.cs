using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Input;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

namespace SourceSharp.Client;

/// <summary>
/// One key press, down and then up, through the client's key-input path — the
/// two asks the shell makes of every key the engine offers, in its order
/// (shell_input.cpp <c>CSourceSharpInput::KeyEvent</c>: the input half, and on
/// its no-decision the client mode, whose walk <see cref="ClientKeyInput"/>
/// answers and whose hud-menu leg is the <see cref="IHudMenuKeys"/> menus) —
/// and, when the input half's answer is the menu layer's claim, the press the
/// engine's vgui root forwards into the UI as well. A console <c>slot1</c> is
/// the bind's command and no menu ever sees it; this is the event the key's
/// press is, so a boot or a test can choose a menu entry the way a keyboard
/// does.
///
/// <para>
/// The argument is the key's binding, the words the engine hands the client
/// mode for the key pressed (<c>slot1</c>, <c>+attack</c>): the route does not
/// keep a bind table, the engine's own key press remains the binder's word.
/// The decision each leg made is logged, the client mode's no-decision
/// reported as the shell reports it: the engine runs the binding.
/// </para>
/// </summary>
[ConCommand("cs_key", Help = "Deliver one key press, down then up, through the client's key path: cs_key <binding>")]
public sealed class ClientKeyInject(ClientInputService input, IClientKeyEvents keys, IServiceLocator services,
                                    IEngineEnvironment environment, ILog log)
{
    private readonly ClientInputService _input = input;
    private readonly IClientKeyEvents _keys = keys;
    private readonly IServiceLocator _services = services;
    private readonly IEngineEnvironment _environment = environment;
    private readonly ILog _log = log;

    public void Execute(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            _log.Msg("usage: cs_key <binding>\n");
            return;
        }

        string name = binding.Trim();
        // KEY_NONE: the camera's intercept is the mouse keys' question, and a
        // key name's code is the engine's to hand a real press.
        const int keyCode = 0;
        // The client mode's two facts, as the shell computes them for it: the
        // console's screen, and the game's size.
        bool console = _services.Resolve<GameUiHost>()?.IsConsoleVisible() ?? false;
        int maxClients = _environment is { Globals.IsAvailable: true } env ? env.Globals.MaxClients : 1;
        ClientKeyBinding parsed = UiClientBinding.BindingOf(Encoding.UTF8.GetBytes(name));

        int down = Leg(down: true);
        int up = Leg(down: false);
        _log.Msg($"[SourceSharp/Client] cs_key {name}: down {Word(down)}, up {Word(up)}\n");

        return;

        int Leg(bool down)
        {
            // The input half; its no-decision is the client mode's to answer,
            // whose own no-decision the shell reports as the engine's run.
            int answer = _input.Input.KeyEvent(down ? 1 : 0, keyCode, name);
            if (answer >= 0)
            {
                // A named press's only zero is the menu layer's claim (the
                // camera's intercept is the mouse keys'): the press is the
                // menu's, and the engine's vgui root has it too — its
                // OnKeyCodePressed the one key event the panels forward, the
                // number-row code the slot's binding names, which the radial's
                // numbered choices take. The client mode's walk is not asked
                // again: the claim is final.
                if (down && answer == ClientKeyInput.Used)
                {
                    int code = UiCodeOf(parsed);
                    if (code != 0 && _services.Resolve<IUiClientEvents>() is { } ui)
                        ui.Input((int)UiInputKind.Key, 0, 0, code, 0, 0);
                }
                return answer;
            }
            int decision = _keys.KeyInput(down, parsed, console, maxClients);
            return decision < 0 ? ClientKeyInput.EngineRuns : decision;
        }
    }

    /// <summary>The number-row key a slot binding's press carries, the codes
    /// the UI's screens consume (UiKey is vgui's ButtonCode_t): the route
    /// carries the press the menu layer claimed as that key. Any other
    /// binding's key is the engine's to name, and the leg carries none.</summary>
    internal static int UiCodeOf(ClientKeyBinding binding) => binding switch
    {
        ClientKeyBinding.Slot10 => (int)UiKey.Key0,
        >= ClientKeyBinding.Slot1 and <= ClientKeyBinding.Slot9
            => (int)UiKey.Key0 + (binding - ClientKeyBinding.Slot1) + 1,
        _ => 0,
    };

    /// <summary>The decision as the shell's own words have it (ClientKeyInput's constants).</summary>
    private static string Word(int decision)
        => decision == ClientKeyInput.EngineRuns ? "engine runs it"
         : decision == ClientKeyInput.Used ? "used"
         : $"no decision ({decision})";
}
