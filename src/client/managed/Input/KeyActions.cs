using System.Reflection;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Input;

/// <summary>
/// One thing a key can do, declared on a class as a convar is: its console line
/// and the keys a fresh player gets for it, and with <see cref="Label"/> a row of
/// the keyboard page (a #Token or plain words), under <see cref="Section"/>, placed
/// by <see cref="Order"/>. Without a label it is bound and not listed.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KeyActionAttribute(string command, string? label = null) : Attribute
{
    public string Command => command;

    public string? Label => label;

    public string? Section { get; init; }

    public int Order { get; init; }

    public string[] Keys { get; init; } = [];
}

/// <summary>
/// The running mod's key actions (stock's scripts/kb_act.lst and kb_def.lst, as
/// classes). A mod assembly that declares any replaces the core's whole list, and
/// its mods add to it by order: the rows a game has are not the core's plus its own.
/// </summary>
public sealed class KeyActions : IUiKeyActions
{
    private readonly UiKeyAction[] _rows;
    private readonly (string Key, string Command)[] _binds;

    private KeyActions(UiKeyAction[] rows, (string Key, string Command)[] binds)
    {
        _rows = rows;
        _binds = binds;
    }

    /// <summary>The actions among <paramref name="types"/>: the mods' when any declare one, else <paramref name="core"/>'s.</summary>
    public static KeyActions Of(IEnumerable<Type> types, Assembly core)
    {
        var all = types.Select(t => (Type: t, Action: t.GetCustomAttribute<KeyActionAttribute>()))
                       .Where(a => a.Action is not null).ToList();
        var mods = all.Where(a => a.Type.Assembly != core).ToList();
        var actions = (mods.Count > 0 ? mods : all).Select(a => a.Action!).OrderBy(a => a.Order).ToList();
        return new KeyActions(
            [.. actions.Where(a => a.Label is not null).Select(a => new UiKeyAction(a.Command, a.Label!, a.Section))],
            [.. actions.SelectMany(a => a.Keys.Select(k => (k, a.Command)))]);
    }

    public IReadOnlyList<UiKeyAction> Actions() => _rows;

    /// <summary>The keys a fresh player gets, each with the command it runs.</summary>
    public IReadOnlyList<(string Key, string Command)> DefaultBinds => _binds;

    /// <summary>
    /// Binds the defaults when the player has no config.cfg. Called in the client
    /// DLL's Init, before the engine reads its configuration: finding keys bound,
    /// the engine does not fall back to kb_def.lst, and writes them to the new
    /// config. A player with a config keeps it.
    /// </summary>
    public void BindDefaults(bool hasConfig, Action<string> submit)
    {
        if (hasConfig)
            return;
        foreach (var (key, command) in _binds)
            submit($"bind \"{key}\" \"{command}\"");
    }
}
