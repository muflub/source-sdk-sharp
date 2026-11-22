using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Vgui;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// Opening the Descent screens (plan_descent_logic UI1). Each window screen
// claims the viewport panel "descent_<screen>" (UiScreen.Panel), so the
// server opens one with the VGUIMenu it already sends for stock's panels;
// until a server package does, these client commands open and close them,
// and three keys open the windows a hero needs now. The screens live in
// TF2.Descent.Client.Ui, which this assembly does not reference: the names
// here are held to the screens' own by a fact (DescentUiCompositionTests).
//=============================================================================

/// <summary>The Descent window screens by name, and the panel each claims.</summary>
public static class DescentScreens
{
    /// <summary>The prefix of every Descent screen's panel: <c>descent_&lt;screen&gt;</c>.</summary>
    public const string PanelPrefix = "descent_";

    public const string CharSheet = "charsheet", Inventory = "inventory", SkillTree = "skilltree";

    /// <summary>Every Descent screen on the menu layer: what the commands open and close.</summary>
    public static IReadOnlyList<string> Names =>
    [
        "automap", CharSheet, "craftbench", "crate", "depthpicker", Inventory,
        "party", SkillTree, "spellbook", "stash", "vendor",
    ];

    /// <summary>The screen a word names: a screen name or its panel name; null for neither.</summary>
    public static string? Find(string word)
    {
        string name = word.StartsWith(PanelPrefix, StringComparison.OrdinalIgnoreCase) ? word[PanelPrefix.Length..] : word;
        foreach (string screen in Names)
            if (string.Equals(screen, name, StringComparison.OrdinalIgnoreCase))
                return screen;
        return null;
    }
}

/// <summary>Opens a Descent screen by name (<c>tf2_descent_ui inventory</c>, or its panel, <c>descent_inventory</c>).</summary>
[ConCommand("tf2_descent_ui", Help = "tf2_descent_ui <screen>: open a TF2 Descent screen (charsheet, inventory, skilltree, spellbook, party, automap, stash, vendor, craftbench, crate, depthpicker)")]
public sealed class DescentUiOpenCommand(UiLoader ui, ILog log)
{
    private IEnumerable<string> Screens() => DescentScreens.Names;

    public void Execute([Complete(From = nameof(Screens))] string screen = "")
    {
        if (DescentScreens.Find(screen) is not { } name)
        {
            log.Msg($"usage: tf2_descent_ui <{string.Join('|', DescentScreens.Names)}>\n");
            return;
        }
        if (ui.Current is not { } runtime || !runtime.Has(name))
        {
            log.Msg($"tf2_descent_ui: the UI has no {name} screen (is TF2.Descent.Client.Ui in the mod's client_ui?)\n");
            return;
        }
        runtime.Open(name);
    }
}

/// <summary>Closes a Descent screen by name, or with none named every open one.</summary>
[ConCommand("tf2_descent_ui_close", Help = "tf2_descent_ui_close [screen]: close a TF2 Descent screen, or every open one")]
public sealed class DescentUiCloseCommand(UiLoader ui, ILog log)
{
    private IEnumerable<string> Screens() => DescentScreens.Names;

    public void Execute([Complete(From = nameof(Screens))] string screen = "")
    {
        if (ui.Current is not { } runtime)
            return;
        if (screen.Length == 0)
        {
            foreach (string name in DescentScreens.Names)
                if (runtime.IsOpen(name))
                    runtime.Close(name);
            return;
        }
        if (DescentScreens.Find(screen) is not { } named)
        {
            log.Msg($"usage: tf2_descent_ui_close [{string.Join('|', DescentScreens.Names)}]\n");
            return;
        }
        runtime.Close(named);
    }
}

/// <summary>Opens a Descent screen that is closed and closes one that is open (<c>tf2_descent_ui_toggle automap</c>): what a key that shows a map does.</summary>
[ConCommand("tf2_descent_ui_toggle", Help = "tf2_descent_ui_toggle <screen>: open a TF2 Descent screen, or close it when it is open")]
public sealed class DescentUiToggleCommand(UiLoader ui, ILog log)
{
    private IEnumerable<string> Screens() => DescentScreens.Names;

    public void Execute([Complete(From = nameof(Screens))] string screen = "")
    {
        if (DescentScreens.Find(screen) is not { } name)
        {
            log.Msg($"usage: tf2_descent_ui_toggle <{string.Join('|', DescentScreens.Names)}>\n");
            return;
        }
        if (ui.Current is not { } runtime || !runtime.Has(name))
        {
            log.Msg($"tf2_descent_ui_toggle: the UI has no {name} screen (is TF2.Descent.Client.Ui in the mod's client_ui?)\n");
            return;
        }
        if (runtime.IsOpen(name))
            runtime.Close(name);
        else
            runtime.Open(name);
    }
}

// The three windows a hero needs now, on the keys an ARPG puts them on. Every
// letter is one of TF2's binds, so these take three of TF2's keys, as the
// skill keys took 4 to 9: C from +voice_menu_3 (TF2's third voice menu), I
// from showmapinfo, K from cl_decline_first_notification. Their Order comes
// after TF2's whole list (its last is 700) and the skills' (1010 to 1060), so
// on a fresh config these binds are the ones the keys end with.

[KeyAction("tf2_descent_ui charsheet", "Character sheet", Section = "Descent", Order = 1110, Keys = ["c"])]
public sealed class CharSheetKey;

[KeyAction("tf2_descent_ui inventory", "Inventory", Section = "Descent", Order = 1120, Keys = ["i"])]
public sealed class InventoryKey;

[KeyAction("tf2_descent_ui skilltree", "Skills", Section = "Descent", Order = 1130, Keys = ["k"])]
public sealed class SkillTreeKey;

// The automap (UI5) on M, an ARPG's map key, which it takes from TF2's
// open_charinfo_direct: TF2's loadout, which Descent keeps closed (HE1). TAB
// shows the map over the view instead (MapOverlay.cs). A toggle: the key that
// opens the map closes it.
[KeyAction("tf2_descent_ui_toggle automap", "Automap", Section = "Descent", Order = 1140, Keys = ["m"])]
public sealed class AutomapKey;
