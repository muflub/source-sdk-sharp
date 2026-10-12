using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The open screens of one side (plan_client_ui §5.7): from DI, sorted by layer
/// and order, described into one context each frame and drawn by the backend in
/// one list. Open and close by name.
/// </summary>
public sealed class UiLayer : IUiLayer
{
    private readonly UiScreen[] _screens;
    private readonly bool[] _open;
    private readonly UiContext _context;
    private readonly IUiBackend _backend;
    private readonly UiWindows? _windows;
    private readonly int[] _order;
    private readonly string?[] _closing = new string?[8];

    public UiLayer(IEnumerable<UiScreen> screens, UiContext context, IUiBackend backend, UiWindows? windows = null)
    {
        _screens = screens.OrderBy(s => s.Layer).ThenBy(s => s.Order).ToArray();
        _open = _screens.Select(s => s.OpenByDefault).ToArray();
        _order = Enumerable.Range(0, _screens.Length).ToArray();
        _context = context;
        _backend = backend;
        _windows = windows;
    }

    /// <summary>A screen's place in the draw order: by layer, then windows above the rest in their z-order, then Order.</summary>
    private long SortKey(int i)
    {
        var s = _screens[i];
        int rank = _windows is not null && s is IUiWindowed w ? _windows.Rank(w.WindowId) : -1;
        return ((long)s.Layer << 40) + (rank >= 0 ? (1L << 32) + rank : (long)s.Order + (1L << 31));
    }

    public UiContext Context => _context;

    public void Frame(UiLayerKind layer, in ScreenInfo screen, double now)
    {
        _context.BeginFrame(screen, now);

        // Windows last raised draw last (and are hit first); insertion sort: no allocation.
        for (int a = 1; a < _order.Length; a++)
        {
            int v = _order[a];
            long key = SortKey(v);
            int b = a - 1;
            while (b >= 0 && SortKey(_order[b]) > key)
            {
                _order[b + 1] = _order[b];
                b--;
            }
            _order[b + 1] = v;
        }

        for (int k = 0; k < _order.Length; k++)
        {
            int i = _order[k];
            var s = _screens[i];
            if (s.Layer != layer || !_open[i] || !s.Visible)
                continue;
            var ui = _context.BeginScreen(s.Name, s.Layer);
            s.Describe(ref ui);
            _context.EndScreen();
        }
        _context.EndFrame();
        _backend.Draw(_context.List.AsDrawList());

        // A window that asked to close (Esc, its close button) closes its screen.
        if (_windows is not null)
        {
            int n = _windows.TakeCloseRequests(_closing);
            for (int c = 0; c < n; c++)
                for (int i = 0; i < _screens.Length; i++)
                    if (_screens[i] is IUiWindowed w && w.WindowId == _closing[c])
                        _open[i] = false;
        }
    }

    public void Input(in UiInputEvent input) => _context.Input(input);

    public void Open(string name)
    {
        SetOpen(name, true);
        // An opened window comes up on top.
        foreach (var s in _screens)
            if (s.Name == name && s is IUiWindowed w)
                _windows?.Raise(w.WindowId);
    }

    public void Close(string name) => SetOpen(name, false);

    /// <summary>The names of the open screens, in draw order.</summary>
    public IEnumerable<string> OpenScreens()
    {
        for (int i = 0; i < _screens.Length; i++)
            if (_open[i])
                yield return _screens[i].Name;
    }

    /// <summary>
    /// What a UI-only reload carries of this layer (plan_client_ui U5): which
    /// screens are open, and its context's focus and per-id state.
    /// </summary>
    public byte[] Carry()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            var open = OpenScreens().ToArray();
            w.Write(open.Length);
            foreach (string name in open)
                w.Write(name);
            byte[] context = _context.Carry();
            w.Write(context.Length);
            w.Write(context);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// <see cref="Carry"/>'s bytes into this layer: each screen still on it is
    /// opened (one the new build dropped is skipped) and the context restored.
    /// Answers the screens opened.
    /// </summary>
    public int Restore(byte[] carried, Func<string, Type?> resolve)
    {
        using var r = new BinaryReader(new MemoryStream(carried));
        int count = r.ReadInt32(), opened = 0;
        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString();
            if (Has(name))
            {
                SetOpen(name, true);
                opened++;
            }
        }
        _context.Restore(r.ReadBytes(r.ReadInt32()), resolve);
        return opened;
    }

    /// <summary>Whether a screen by that name is on this layer.</summary>
    public bool Has(string name) => IndexOf(name) >= 0;

    public bool IsOpen(string name)
    {
        int i = IndexOf(name);
        return i >= 0 && _open[i];
    }

    public bool WantsMouse
    {
        get
        {
            for (int i = 0; i < _screens.Length; i++)
                if (_open[i] && _screens[i].Visible && _screens[i].WantsMouse)
                    return true;
            return false;
        }
    }

    private void SetOpen(string name, bool open)
    {
        int i = IndexOf(name);
        if (i < 0)
            throw new ArgumentException($"no UI screen named '{name}'", nameof(name));
        _open[i] = open;
    }

    private int IndexOf(string name)
    {
        for (int i = 0; i < _screens.Length; i++)
            if (_screens[i].Name == name)
                return i;
        return -1;
    }
}
