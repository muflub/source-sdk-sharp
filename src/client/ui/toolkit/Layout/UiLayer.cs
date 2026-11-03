using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The open screens of one side (plan_client_ui §5.7): from DI, sorted by layer
/// and order, described into one context each frame and drawn by the backend in
/// one list. Open and close by name. On a windowed layer a
/// <see cref="UiScreen.Chrome"/> screen (the taskbar) is shown exactly while
/// the <see cref="ChromeWith"/> screen is up (the main or pause menu), or,
/// with none named, while some window is open; above every window. A window
/// can be minimised:
/// open, its state kept, but not drawn until it is opened again.
/// </summary>
public sealed class UiLayer : IUiLayer
{
    private readonly UiScreen[] _screens;
    private readonly bool[] _open;
    private readonly bool[] _held;
    private readonly bool[] _chrome;
    private readonly bool[] _minimised;
    private long _openEpoch;
    private readonly UiContext _context;
    private readonly IUiBackend _backend;
    private readonly UiWindows? _windows;
    private readonly int[] _order;
    private readonly string?[] _closing = new string?[8];

    public UiLayer(IEnumerable<UiScreen> screens, UiContext context, IUiBackend backend, UiWindows? windows = null)
    {
        _screens = screens.OrderBy(s => s.Layer).ThenBy(s => s.Order).ToArray();
        _chrome = _screens.Select(s => windows is not null && s.Chrome).ToArray();
        _open = _screens.Select((s, i) => s.OpenByDefault && !_chrome[i]).ToArray();
        _held = new bool[_screens.Length];
        _minimised = new bool[_screens.Length];
        _order = Enumerable.Range(0, _screens.Length).ToArray();
        _context = context;
        _backend = backend;
        _windows = windows;
    }

    /// <summary>
    /// A screen's place in the draw order: by layer, then windows above the
    /// rest in their z-order, then Order; chrome above every window (drawn
    /// last, so hit first).
    /// </summary>
    private long SortKey(int i)
    {
        var s = _screens[i];
        if (_chrome[i])
            return ((long)s.Layer << 40) + (1L << 33) + s.Order;
        int rank = _windows is not null && s is IUiWindowed w ? _windows.Rank(w.WindowId) : -1;
        return ((long)s.Layer << 40) + (rank >= 0 ? (1L << 32) + rank : (long)s.Order + (1L << 31));
    }

    public UiContext Context => _context;

    /// <summary>Changes whenever a screen opens, closes, minimises or comes back: a source that lists them caches on it.</summary>
    public long OpenEpoch => _openEpoch;

    /// <summary>Whether a screen is a window of this layer (not its chrome).</summary>
    public bool IsWindow(int index) => _windows is not null && _screens[index] is IUiWindowed && !_chrome[index];

    /// <summary>Whether the screen at <paramref name="index"/> (in <see cref="Screens"/>) is open.</summary>
    public bool IsOpenAt(int index) => _open[index];

    /// <summary>Whether the screen at <paramref name="index"/> is minimised.</summary>
    public bool IsMinimisedAt(int index) => _minimised[index];

    /// <summary>
    /// The screen the chrome comes and goes with: the host's main menu, so the
    /// taskbar is up exactly while the main or pause menu is, windows or not,
    /// and never in play. Null: the chrome shows while some window is open.
    /// </summary>
    public string? ChromeWith { get; set; }

    /// <summary>Whether a chrome screen is shown this frame.</summary>
    private bool ChromeShown
    {
        get
        {
            for (int i = 0; i < _screens.Length; i++)
                if (_chrome[i] && _open[i])
                    return true;
            return false;
        }
    }

    /// <summary>Shows each chrome screen exactly while the <see cref="ChromeWith"/> screen is open and drawn (or, with none, while some window is open), whichever way they came and went.</summary>
    private void SyncChrome()
    {
        bool any = false;
        for (int i = 0; i < _screens.Length && !any; i++)
            any = _open[i] && !_chrome[i] && (ChromeWith is { } with ? _screens[i].Name == with && !_held[i] : IsWindow(i));
        for (int i = 0; i < _screens.Length; i++)
            if (_chrome[i] && _open[i] != any)
            {
                _open[i] = any;
                _openEpoch++;
            }
        if (!any && _windows is not null)
            _windows.DockReserved = 0f;   // a hidden bar reserves nothing
    }

    /// <summary>The layer's screens in draw order — read once by the host to
    /// collect each screen's <see cref="UiScreen.Panel"/> claim (TF2 stage
    /// 3.2's team menu). Not a per-frame path.</summary>
    public IReadOnlyList<UiScreen> Screens => _screens;

    public void Frame(UiLayerKind layer, in ScreenInfo screen, double now)
    {
        _context.BeginFrame(screen, now);
        SyncChrome();

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

        // What the open screens replace, before any of them describes
        // (UiScreen.Takes). Visibility is not asked: a screen standing down
        // (the scoreboard's render group) still owns what it replaced, or the
        // core hud would resurface under it — stock's hidden hud element comes
        // back for nothing (hud.cpp:288-303).
        for (int i = 0; i < _screens.Length; i++)
        {
            var s = _screens[i];
            if (s.Layer != layer || !_open[i] || _held[i] || _minimised[i])
                continue;
            var takes = s.Takes;
            for (int t = 0; t < takes.Count; t++)
                _context.Take(takes[t]);
        }

        for (int k = 0; k < _order.Length; k++)
        {
            int i = _order[k];
            var s = _screens[i];
            if (s.Layer != layer || !_open[i] || _held[i] || _minimised[i] || !s.Visible)
                continue;
            // While the chrome is up a plain screen is laid out above its band (a
            // window keeps clear of it itself, through DockReserved).
            _context.ChromeBandDu = _windows is not null && !_chrome[i] && !IsWindow(i) && ChromeShown ? _windows.DockReserved : 0f;
            var ui = _context.BeginScreen(s.Name, s.Layer);
            s.Describe(ref ui);
            _context.EndScreen();
        }
        _context.ChromeBandDu = 0f;
        _context.EndFrame();
        _backend.Draw(_context.List.AsDrawList());

        // A window that asked to close (Esc, its close button) closes its screen.
        if (_windows is not null)
        {
            int n = _windows.TakeCloseRequests(_closing);
            for (int c = 0; c < n; c++)
                for (int i = 0; i < _screens.Length; i++)
                    if (_screens[i] is IUiWindowed w && w.WindowId == _closing[c])
                        SetOpen(_screens[i].Name, false);
        }
    }

    public void Input(in UiInputEvent input) => _context.Input(input);

    public void Open(string name)
    {
        SetOpen(name, true);
        int at = IndexOf(name);
        if (_minimised[at])
        {
            _minimised[at] = false;
            _openEpoch++;
        }
        // An opened window comes up on top.
        foreach (var s in _screens)
            if (s.Name == name && s is IUiWindowed w)
                _windows?.Raise(w.WindowId);
    }

    public void Close(string name) => SetOpen(name, false);

    /// <summary>Every window but <paramref name="except"/> held, or let go: open, undrawn, taking no input.</summary>
    public void HoldWindows(bool held, string except)
    {
        for (int i = 0; i < _screens.Length; i++)
            if (IsWindow(i) && _screens[i].Name != except)
                _held[i] = held;
    }

    /// <summary>Every window but <paramref name="except"/> shut.</summary>
    public void CloseWindows(string except)
    {
        for (int i = 0; i < _screens.Length; i++)
            if (_open[i] && IsWindow(i) && _screens[i].Name != except)
                Close(_screens[i].Name);
    }

    /// <summary>
    /// Minimises an open window: it stays open with its state but draws
    /// nothing and goes to the bottom of the windows; <see cref="Open"/> brings
    /// it back on top. Not a window, or not open: nothing.
    /// </summary>
    public void Minimise(string name)
    {
        int i = IndexOf(name);
        if (i < 0 || !_open[i] || !IsWindow(i) || _minimised[i])
            return;
        _minimised[i] = true;
        _openEpoch++;
        _windows!.Lower(((IUiWindowed)_screens[i]).WindowId);
    }

    public bool IsMinimised(string name)
    {
        int i = IndexOf(name);
        return i >= 0 && _minimised[i];
    }

    /// <summary>The names of the open screens, in draw order (chrome, the taskbar, is not among them).</summary>
    public IEnumerable<string> OpenScreens()
    {
        for (int i = 0; i < _screens.Length; i++)
            if (_open[i] && !_chrome[i])
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
            if (Has(name) && !_chrome[IndexOf(name)])
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
                if (_open[i] && !_held[i] && !_minimised[i] && _screens[i].Visible && _screens[i].WantsMouse)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Holds a screen hidden, or lets it go: a held screen stays open, keeping
    /// its state, but neither draws nor wants the mouse — the way the engine
    /// hides the client viewport's panels while its GameUI is up, without
    /// closing them. A name the layer lacks is no screen to hold.
    /// </summary>
    public void Hold(string name, bool held)
    {
        int i = IndexOf(name);
        if (i >= 0)
            _held[i] = held;
    }

    public bool IsHeld(string name)
    {
        int i = IndexOf(name);
        return i >= 0 && _held[i];
    }

    /// <summary>A screen of this layer was closed, by name — whichever way
    /// the close came: a named <see cref="Close"/>, the window's Escape or
    /// close button. The host subscribes to tell the screen, the way a
    /// stock panel learns its hide from the same call that hides it.</summary>
    public event Action<string>? Closed;

    private void SetOpen(string name, bool open)
    {
        int i = IndexOf(name);
        if (i < 0)
            throw new ArgumentException($"no UI screen named '{name}'", nameof(name));
        bool was = _open[i];
        _open[i] = open;
        if (was != open)
            _openEpoch++;
        if (!open)
            _minimised[i] = false;
        if (was && !open)
            Closed?.Invoke(name);
    }

    private int IndexOf(string name)
    {
        for (int i = 0; i < _screens.Length; i++)
            if (_screens[i].Name == name)
                return i;
        return -1;
    }
}
