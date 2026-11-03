using SourceSharp.Abi;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>Puts the pointer at the screen's centre, where a radial menu opens round it.</summary>
public interface IRadialPointer
{
    void Centre();
}

/// <summary>The pointer through the input layer.</summary>
public sealed class InputRadialPointer(SourceSharp.Client.Input.IInputHost input) : IRadialPointer
{
    public void Centre()
    {
        input.GetVguiScreenSize(out int width, out int height);
        input.SetCursorPos(width / 2, height / 2);
    }
}

/// <summary>A choice of a radial menu: what it says, and either what taking it does or the menu it opens.</summary>
public sealed record RadialEntry(string Label, Action? Choose = null, Func<RadialPage>? Open = null, string? Detail = null, bool Enabled = true, bool KeepOpen = false);

/// <summary>A radial menu: what its hub says, and its choices, as many as it likes.</summary>
public sealed record RadialPage(string Title, IReadOnlyList<RadialEntry> Entries);

/// <summary>The keys that open radial menus, which their binds cannot do while one holds the keyboard.</summary>
public interface IRadialKeys
{
    /// <summary>The radial menu command this button is bound to; null for any other.</summary>
    string? CommandOn(int buttonCode);

    void Run(string command);

    bool IsDown(int buttonCode);
}

/// <summary>The engine's binds and the input system's names for its buttons, over the mod's key actions.</summary>
public sealed class EngineRadialKeys(IEngineHud hud, IFwdInputSystem input, IFwdEngineClient engine, IReadOnlyList<string> commands, Func<int, bool> isDown) : IRadialKeys
{
    public string? CommandOn(int buttonCode)
    {
        if (input.ButtonName(buttonCode) is not { Length: > 0 } name)
            return null;
        foreach (string command in commands)
            if (string.Equals(hud.LookupBinding(command), name, StringComparison.OrdinalIgnoreCase))
                return command;
        return null;
    }

    public void Run(string command) => engine.SubmitConsoleLine(command);

    public bool IsDown(int buttonCode) => isDown(buttonCode);
}

/// <summary>
/// The one radial menu up, and the way back up the ones that opened it. Opening
/// one puts the pointer at the screen's centre; a list longer than the ring is
/// paged, its last place opening the rest. Whoever owns a menu asks here: the
/// screen only draws and reports.
/// </summary>
public sealed class RadialMenus(IRadialPointer pointer, IRadialKeys? keys = null) : IRadialMenuSource, IRadialMenuActions
{
    /// <summary>The most choices one ring shows; the screen's layout is laid out for as many.</summary>
    public const int MaxItems = 9;

    private readonly Stack<RadialPage> _way = new();
    private RadialPage? _top;
    private object? _owner;
    private Func<bool>? _stillWanted;
    private int _hover = -1;
    private string? _command;
    private string? _swappingTo;
    private int _swapButton = -1;
    private int _heldButton = -1;

    public bool Showing
    {
        get
        {
            // What it was opened for has gone (the player died): it goes too.
            if (_top is not null && _stillWanted is { } wanted && !wanted())
                Close();
            // A held menu swapped to: its key's release never reaches the bind, so it is watched.
            if (_top is not null && _heldButton >= 0 && keys is not null && !keys.IsDown(_heldButton))
                Release(_owner!);
            return _top is not null;
        }
    }

    public string Title => _top?.Title ?? "";

    public int ItemCount => _top?.Entries.Count ?? 0;

    public UiRadialItem Item(int i)
    {
        var e = _top!.Entries[i];
        return new UiRadialItem(e.Label, e.Detail, e.Enabled);
    }

    /// <summary>Whether <paramref name="owner"/>'s menu is the one up.</summary>
    public bool ShowingFor(object owner) => Showing && Equals(_owner, owner);

    /// <summary>Opens <paramref name="page"/> in place of whatever is up, the pointer at the centre.</summary>
    /// <param name="command">The command that opened it: pressing its key again closes it, as the bind itself cannot while the menu holds the keyboard.</param>
    public void Show(RadialPage page, object? owner = null, Func<bool>? stillWanted = null, string? command = null)
    {
        _command = command;
        _heldButton = command == _swappingTo ? _swapButton : -1;
        _swappingTo = null;
        _way.Clear();
        _owner = owner;
        _stillWanted = stillWanted;
        _top = Fit(page);
        _hover = -1;
        pointer.Centre();
    }

    /// <summary>
    /// As stock's voice keys do: the same owner's key again closes its menu; else it opens.
    /// </summary>
    public void Toggle(object owner, Func<RadialPage> page, Func<bool>? stillWanted = null, string? command = null)
    {
        if (Showing && Equals(_owner, owner))
            Close();
        else
            Show(page(), owner, stillWanted, command);
    }

    /// <summary>
    /// A held key let go: the choice under the pointer is taken. With the pointer still
    /// on the hub, or on one that cannot be chosen, nothing is, and the menu goes.
    /// A menu already gone, or another owner's, stays as it is.
    /// </summary>
    public void Release(object owner)
    {
        _heldButton = -1;
        if (!ShowingFor(owner))
            return;
        int hover = _hover;
        if (hover >= 0 && hover < _top!.Entries.Count && _top.Entries[hover].Enabled)
            Choose(hover);
        else
            Close();
    }

    public void Hover(int i) => _hover = i;

    /// <summary>
    /// A button pressed while a menu holds the keyboard. The key of the menu that is up
    /// closes it; another menu's key closes it and opens that one, as its bind would had
    /// it been let through.
    /// </summary>
    public bool Pressed(int buttonCode)
    {
        if (_top is null || keys?.CommandOn(buttonCode) is not { } command)
            return false;
        bool same = string.Equals(command, _command, StringComparison.OrdinalIgnoreCase);
        Close();
        if (same)
            return true;
        if (command.StartsWith('+'))
        {
            _swappingTo = command;
            _swapButton = buttonCode;
        }
        keys.Run(command);
        return true;
    }

    public void Close()
    {
        _way.Clear();
        _top = null;
        _owner = null;
        _command = null;
        _heldButton = -1;
        _stillWanted = null;
    }

    public void Choose(int i)
    {
        if (_top is null || i < 0 || i >= _top.Entries.Count)
            return;
        var e = _top.Entries[i];
        if (!e.Enabled)
            return;
        if (e.Open is { } open)
        {
            _way.Push(_top);
            _top = Fit(open());
            _hover = -1;
            return;
        }
        if (!e.KeepOpen)
            Close();
        e.Choose?.Invoke();
    }

    public void Back()
    {
        if (_way.TryPop(out var previous))
            _top = previous;
        else
            Close();
    }

    // A longer list than the ring: its first places, then "More" opening the rest.
    private static RadialPage Fit(RadialPage page)
    {
        if (page.Entries.Count <= MaxItems)
            return page;
        int keep = MaxItems - 1;
        var rest = new RadialPage(page.Title, [.. page.Entries.Skip(keep)]);
        return new RadialPage(page.Title, [.. page.Entries.Take(keep), new RadialEntry("#SS_UI_Radial_More", Open: () => rest)]);
    }
}
