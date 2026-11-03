using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

// THE COMMANDS THE STOCK GAMEUI MODULE REGISTERED (docs/gameui-census.md §4),
// registered here since that module is never called (plan_gameui_managed.md
// G4). Each answers the current UI's services: a UI reload replaces them.

/// <summary><c>gamemenucommand &lt;name&gt;</c>: stock's game menu commands, through <see cref="GameMenuCommands"/>.</summary>
[ConCommand("gamemenucommand", Help = "Run a game menu command: OpenServerBrowser, OpenCreateMultiplayerGameDialog, OpenOptionsDialog, OpenPlayerListDialog, ResumeGame, Disconnect, Quit, QuitNoConfirm")]
public sealed class GameMenuCommandCommand(IServiceLocator services, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string name = args.Trim();
        if (name.Length == 0)
        {
            log.Msg("Usage: gamemenucommand <commandname>\n");
            return;
        }
        services.Resolve<GameMenuCommands>()?.Run(name);
    }
}

/// <summary>
/// <c>gameui_show_dialog &lt;dialog&gt;</c>: a screen by name opens (stock
/// showed its Xbox message dialogs by number, which answer inertly through
/// the GameUI host's ShowMessageDialog).
/// </summary>
[ConCommand("gameui_show_dialog", Help = "Show a dialog: a UI screen by name")]
public sealed class GameUiShowDialogCommand(UiLoader ui, IServiceLocator services) : IConCommand
{
    public void Execute(string args)
    {
        string name = args.Trim();
        if (name.Length == 0)
            return;
        if (uint.TryParse(name, out uint type))
            services.Resolve<GameUiHost>()?.ShowMessageDialog(type, 0);
        else if (ui.Current is { } runtime && runtime.Has(name))
            runtime.Open(name);
    }
}

/// <summary><c>gameui_hide_dialog &lt;dialog&gt;</c>: a screen by name closes.</summary>
[ConCommand("gameui_hide_dialog", Help = "Hide a dialog: a UI screen by name")]
public sealed class GameUiHideDialogCommand(UiLoader ui) : IConCommand
{
    public void Execute(string args)
    {
        string name = args.Trim();
        if (name.Length > 0 && ui.Current is { } runtime && runtime.Has(name))
            runtime.Close(name);
    }
}

/// <summary>
/// Stock's console dump: the console window's text to the first unused
/// condumpNNN.txt (000 to 999) on the mod's write path, a line each.
/// </summary>
public sealed class ConsoleDump(IFwdBaseFileSystem files)
{
    public const int MaxFiles = 1000;

    /// <summary>The file written, or null when all 1000 names are taken or the file would not open.</summary>
    public string? Write(IConsoleLogSource console)
    {
        string? name = null;
        for (int i = 0; i < MaxFiles && name is null; i++)
        {
            string candidate = $"condump{i:000}.txt";
            if (!files.FileExists(candidate, null))
                name = candidate;
        }
        if (name is null)
            return null;
        nint handle = files.Open(name, "wb", "MOD");
        if (handle == 0)
            return null;
        var text = new StringBuilder();
        for (int i = 0; i < console.LineCount; i++)
            text.Append(console.Line(i).Text).Append('\n');
        files.Write(Encoding.UTF8.GetBytes(text.ToString()), handle);
        files.Close(handle);
        return name;
    }
}

/// <summary><c>condump</c>: the console window's text to a file (<see cref="ConsoleDump"/>).</summary>
[ConCommand("condump", Help = "dump the text currently in the console to condumpXX.txt")]
public sealed class CondumpCommand(IServiceLocator services, IFwdBaseFileSystem files, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        if (services.Resolve<IConsoleLogSource>() is not { } console)
            return;
        if (new ConsoleDump(files).Write(console) is null)
            log.Msg("Can't condump! Too many existing condump output files in the gamedir!\n");
    }
}
