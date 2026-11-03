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
public sealed record RadialEntry(string Label, Action? Choose = null, Func<RadialPage>? Open = null, string? Detail = null, bool Enabled = true);

/// <summary>A radial menu: what its hub says, and its choices, as many as it likes.</summary>
public sealed record RadialPage(string Title, IReadOnlyList<RadialEntry> Entries);

/// <summary>
/// The one radial menu up, and the way back up the ones that opened it. Opening
/// one puts the pointer at the screen's centre; a list longer than the ring is
/// paged, its last place opening the rest. Whoever owns a menu asks here: the
/// screen only draws and reports.
/// </summary>
public sealed class RadialMenus(IRadialPointer pointer) : IRadialMenuSource, IRadialMenuActions
{
    /// <summary>The most choices one ring shows; the screen's layout is laid out for as many.</summary>
    public const int MaxItems = 9;

    private readonly Stack<RadialPage> _way = new();
    private RadialPage? _top;
    private object? _owner;
    private Func<bool>? _stillWanted;

    public bool Showing
    {
        get
        {
            // What it was opened for has gone (the player died): it goes too.
            if (_top is not null && _stillWanted is { } wanted && !wanted())
                Close();
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
    public void Show(RadialPage page, object? owner = null, Func<bool>? stillWanted = null)
    {
        _way.Clear();
        _owner = owner;
        _stillWanted = stillWanted;
        _top = Fit(page);
        pointer.Centre();
    }

    /// <summary>
    /// As stock's voice keys do: the same owner's key again closes its menu; else it opens.
    /// </summary>
    public void Toggle(object owner, Func<RadialPage> page, Func<bool>? stillWanted = null)
    {
        if (Showing && Equals(_owner, owner))
            Close();
        else
            Show(page(), owner, stillWanted);
    }

    public void Close()
    {
        _way.Clear();
        _top = null;
        _owner = null;
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
            return;
        }
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
