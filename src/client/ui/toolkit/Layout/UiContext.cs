using System.Runtime.InteropServices;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>Turns #Tokens into text. The game's is HudLocalise; previews parse mod_sharp_english.txt.</summary>
public interface IUiLocaliser
{
    /// <summary>The text for <paramref name="token"/> (with its leading #), or null when there is none.</summary>
    string? Find(string token);
}

/// <summary>
/// A mod's contribution to a screen's named slot (plan_client_ui §5.7a): the
/// screen says <c>ui.Slot("hud.bottom-left")</c>, every contributor that answers
/// for that name describes into the slot, in the order the container lists them
/// (mods.yaml listing order).
/// </summary>
public interface IUiSlotContributor
{
    bool Contributes(string slot);

    void Describe(ref UiFrame ui, string slot);
}

/// <summary>
/// The engine of a UI layer: the per-frame node arena, the id scopes, the state
/// kept between frames by id, input, layout and emit. One per layer, from DI.
/// Screens never see it: they get a <see cref="UiFrame"/>.
///
/// <para>
/// A frame is <see cref="BeginFrame"/>, then for each screen
/// <see cref="BeginScreen"/> / describe / <see cref="EndScreen"/> (which lays
/// the screen out and emits it into <see cref="List"/>), then
/// <see cref="EndFrame"/>, which keeps what input needs for the next frame.
/// Nothing here allocates once its arrays have grown to the busiest frame.
/// </para>
/// </summary>
public sealed partial class UiContext
{
    private readonly IUiLocaliser _localiser;
    private readonly IUiSlotContributor[] _contributors;

    private UiNode[] _nodes = new UiNode[256];
    private string?[] _keys = new string?[256];
    private int _nodeCount;

    private char[] _chars = new char[4096];
    private int _charCount;

    private UiSize[] _columns = new UiSize[64];
    private int _columnCount;

    // The open containers, innermost last; the scope ids, innermost last.
    private int[] _open = new int[64];
    private int _openDepth;
    private uint[] _scopes = new uint[64];
    private int _scopeDepth;

    // The roots of the screen being described: its main tree, then overlays.
    private int[] _roots = new int[16];
    private bool[] _modal = new bool[16];
    private int[] _rootZ = new int[16];

    // Design-size contexts (a window's body): what ui.DesignWidth and ui.Narrow read.
    private float[] _designW = new float[8], _designH = new float[8], _designNarrow = new float[8];
    private int _designDepth;
    private int _rootCount;

    private string _screenName = string.Empty;
    private int _screenElementStart;

    public UiContext(UiTheme theme, IUiFontMetrics metrics, IUiLocaliser localiser,
                     IEnumerable<IUiSlotContributor> contributors)
    {
        Theme = theme;
        FontMetrics = metrics;
        _localiser = localiser;
        _contributors = contributors.ToArray();
    }

    public UiTheme Theme { get; }

    private UiIconSet? _icons;

    /// <summary>The icon set (its art and atlas), read from the toolkit on first use.</summary>
    public UiIconSet Icons => _icons ??= UiIconSet.Embedded();

    public IUiFontMetrics FontMetrics { get; }

    /// <summary>What the last frame emitted.</summary>
    public UiRenderList List { get; } = new();

    /// <summary>What the lint reads about the last frame's elements.</summary>
    public UiFrameReport Report { get; } = new();

    /// <summary>
    /// Record each element's name into the list (previews, tests, /ui). Off in
    /// game: names cost characters every frame and only tools read them.
    /// </summary>
    public bool RecordNames { get; set; }

    public ScreenInfo Screen { get; private set; }

    public double Now { get; private set; }

    /// <summary>Seconds since the previous frame (0 on the first).</summary>
    public float Dt { get; private set; }

    private bool _hadFrame;

    public void BeginFrame(in ScreenInfo screen, double now)
    {
        Dt = _hadFrame ? (float)Math.Clamp(now - Now, 0, 0.25) : 0f;
        _hadFrame = true;
        Now = now;
        Screen = screen;
        List.Clear();
        List.Screen = screen;
        Report.Clear();
        ProcessInput();
    }

    /// <summary>Starts describing a screen: a root that fills the safe area, and an id scope named for it.</summary>
    public UiFrame BeginScreen(string name, UiLayerKind layer = UiLayerKind.Menu)
    {
        _screenName = name;
        _screenLayer = layer;
        _nodeCount = 0;
        _charCount = 0;
        _columnCount = 0;
        _openDepth = 0;
        _rootCount = 0;
        _scopeDepth = 0;
        _designDepth = 0;
        _lastWindowZ = 0;
        _scopes[_scopeDepth++] = Hash(2166136261u, name);

        int root = NewNode(NodeKind.Box, -1);
        ref var n = ref _nodes[root];
        n.W = Size.Fill;
        n.H = Size.Fill;
        n.Id = _scopes[0];
        _keys[root] = name;
        AddRoot(root, modal: false);
        Push(root);
        return new UiFrame(this);
    }

    private UiLayerKind _screenLayer;

    /// <summary>Lays the screen out and emits it.</summary>
    public void EndScreen()
    {
        if (_openDepth != 1)
        {
            int open = _open[_openDepth - 1];
            throw new InvalidOperationException(
                $"{_screenName}: {_openDepth - 1} container(s) left open at the end of Describe, innermost {Describe(open)}; close each with `using`");
        }
        _openDepth = 0;

        // Overlays in z order (windows by rank; equal z keeps the order they were described in).
        for (int a = 2; a < _rootCount; a++)
        {
            int root = _roots[a], z = _rootZ[a];
            bool modal = _modal[a];
            int b = a - 1;
            while (b >= 1 && _rootZ[b] > z)
            {
                _roots[b + 1] = _roots[b];
                _rootZ[b + 1] = _rootZ[b];
                _modal[b + 1] = _modal[b];
                b--;
            }
            _roots[b + 1] = root;
            _rootZ[b + 1] = z;
            _modal[b + 1] = modal;
        }

        for (int r = 0; r < _rootCount; r++)
            Measure(_roots[r]);

        var safe = SafeRect();
        for (int r = 0; r < _rootCount; r++)
        {
            int root = _roots[r];
            ref var n = ref _nodes[root];
            if (n.AnchorTarget >= 0)
                PlaceAgainst(root, safe);
            else if (r == 0)
                SetRect(root, safe.X, safe.Y, safe.W, safe.H);
            else
                PlaceInBox(root, safe.X, safe.Y, safe.W, safe.H);
            Arrange(root);
        }

        _screenElementStart = List.ElementCount;
        for (int r = 0; r < _rootCount; r++)
        {
            if (_modal[r])
                EmitScrim(_roots[r]);
            Emit(_roots[r], -1, 1f, FullScreen());
        }

        CheckCollisions();
        Report.Screens++;
    }

    /// <summary>Ends the frame: keeps what next frame's input is hit-tested against.</summary>
    public void EndFrame() => KeepHitTargets();

    // ---- the arena ----

    private int NewNode(NodeKind kind, int parent)
    {
        if (_nodeCount == _nodes.Length)
        {
            Array.Resize(ref _nodes, _nodes.Length * 2);
            Array.Resize(ref _keys, _keys.Length * 2);
        }

        int i = _nodeCount++;
        _nodes[i] = new UiNode
        {
            Parent = parent,
            FirstChild = -1,
            LastChild = -1,
            Next = -1,
            Kind = kind,
            Opacity = 1f,
            KeyNumber = -1,
            AnchorTarget = -1,
            MaxW = float.PositiveInfinity,
            PlaceX = float.NaN,
            Caret = -1,
            PlaceY = float.NaN,
            TextPaint = Tone.Text,
            Role = Toolkit.Text.Body,
        };
        _keys[i] = null;

        if (parent >= 0)
        {
            ref var p = ref _nodes[parent];
            if (p.LastChild >= 0)
                _nodes[p.LastChild].Next = i;
            else
                p.FirstChild = i;
            p.LastChild = i;
            int ordinal = p.ChildCount++;
            _nodes[i].Depth = _nodes[parent].Depth + 1;
            _nodes[i].Id = Hash(p.Id, ordinal);
        }

        return i;
    }

    internal ref UiNode Node(int index) => ref _nodes[index];

    internal int Current => _open[_openDepth - 1];

    /// <summary>A node under the open container.</summary>
    internal int Add(NodeKind kind) => NewNode(kind, Current);

    /// <summary>A container under the open one, opened.</summary>
    internal int Open(NodeKind kind)
    {
        int i = Add(kind);
        Push(i);
        return i;
    }

    private void Push(int node)
    {
        if (_openDepth == _open.Length)
            Array.Resize(ref _open, _open.Length * 2);
        _open[_openDepth++] = node;
    }

    internal void Close(int node)
    {
        if (_openDepth <= 1 || _open[_openDepth - 1] != node)
        {
            string open = _openDepth > 0 ? Describe(_open[_openDepth - 1]) : "nothing";
            throw new InvalidOperationException($"{_screenName}: closing {Describe(node)} but {open} is the open container");
        }
        _openDepth--;
        if (_nodes[node].PopsDesign && _designDepth > 0)
            _designDepth--;

        // A composite (a panel's body, a dialog's) closes the boxes it was built in.
        for (int k = _nodes[node].ClosesOuter; k > 0 && _openDepth > 1; k--)
            _openDepth--;
    }

    internal bool IsOpen(int node)
    {
        for (int i = 1; i < _openDepth; i++)
            if (_open[i] == node)
                return true;
        return false;
    }

    private void AddRoot(int node, bool modal)
    {
        if (_rootCount == _roots.Length)
        {
            Array.Resize(ref _roots, _roots.Length * 2);
            Array.Resize(ref _modal, _modal.Length * 2);
            Array.Resize(ref _rootZ, _rootZ.Length * 2);
        }
        _roots[_rootCount] = node;
        _modal[_rootCount] = modal;
        _rootZ[_rootCount] = _lastWindowZ + 1;
        _rootCount++;
    }

    /// <summary>Orders the overlay just opened among the screen's overlays: higher draws later and is hit first (a window's rank).</summary>
    internal void SetLastRootZ(int z)
    {
        _rootZ[_rootCount - 1] = z;
        _lastWindowZ = z;
    }

    // An overlay opened after a window (its dropdown, its suggestions) sits just above that window.
    private int _lastWindowZ;

    /// <summary>Starts a design-size context: until the node closes, ui.DesignWidth and ui.Narrow answer for this size.</summary>
    internal void PushDesign(int node, float widthDu, float heightDu, float narrowBelowDu)
    {
        if (_designDepth == _designW.Length)
        {
            Array.Resize(ref _designW, _designW.Length * 2);
            Array.Resize(ref _designH, _designH.Length * 2);
            Array.Resize(ref _designNarrow, _designNarrow.Length * 2);
        }
        _designW[_designDepth] = widthDu;
        _designH[_designDepth] = heightDu;
        _designNarrow[_designDepth] = narrowBelowDu;
        _designDepth++;
        _nodes[node].PopsDesign = true;
    }

    /// <summary>The design width layout choices are made by: the innermost window's, else the screen's.</summary>
    internal float DesignWidth => _designDepth > 0 ? _designW[_designDepth - 1] : UiScale.DesignWidth(Screen);

    /// <summary>The narrow breakpoint in force: the innermost window's, else the theme's.</summary>
    internal float NarrowBelowDu => _designDepth > 0 ? _designNarrow[_designDepth - 1] : Theme.NarrowBelowDu;

    internal float DesignHeight => _designDepth > 0 ? _designH[_designDepth - 1] : UiScale.DesignHeight(Screen);

    /// <summary>An overlay root: outside every parent's clip and order, drawn after the screen.</summary>
    internal int OpenOverlay(string key, bool modal)
    {
        int i = NewNode(NodeKind.Box, -1);
        ref var n = ref _nodes[i];
        n.Id = Hash(Scope, key);
        n.W = Size.Fill;
        n.H = Size.Fill;
        n.Flags |= UiElementFlags.Overlaps;
        _keys[i] = key;
        AddRoot(i, modal);
        Push(i);
        return i;
    }

    // ---- text arena ----

    internal int CharCount => _charCount;

    internal void EnsureChars(int more)
    {
        if (_charCount + more > _chars.Length)
            Array.Resize(ref _chars, Math.Max(_chars.Length * 2, _charCount + more));
    }

    internal Span<char> CharsFree => _chars.AsSpan(_charCount);

    internal void Advance(int count) => _charCount += count;

    internal void AppendChars(ReadOnlySpan<char> text, bool upper)
    {
        EnsureChars(text.Length);
        var dst = _chars.AsSpan(_charCount, text.Length);
        if (upper)
            text.ToUpperInvariant(dst);
        else
            text.CopyTo(dst);
        _charCount += text.Length;
    }

    internal Span<char> TextSpan(int start, int length) => _chars.AsSpan(start, length);

    internal ReadOnlySpan<char> TextOf(in UiNode n) => _chars.AsSpan(n.TextStart, n.TextLength);

    /// <summary>A localised token's text, or the token itself (flagged) when there is none.</summary>
    internal string Localise(string token, out bool found)
    {
        if (token.Length > 1 && token[0] == '#')
        {
            string? text = _localiser.Find(token);
            found = text is not null;
            return text ?? token;
        }

        found = false;
        return token;
    }

    // ---- columns ----

    internal (int Start, int Count) AddColumns(ReadOnlySpan<UiSize> columns)
    {
        if (_columnCount + columns.Length > _columns.Length)
            Array.Resize(ref _columns, Math.Max(_columns.Length * 2, _columnCount + columns.Length));
        columns.CopyTo(_columns.AsSpan(_columnCount));
        int start = _columnCount;
        _columnCount += columns.Length;
        return (start, columns.Length);
    }

    // ---- ids ----

    internal uint Scope => _scopes[_scopeDepth - 1];

    internal void PushScope(string key)
    {
        if (_scopeDepth == _scopes.Length)
            Array.Resize(ref _scopes, _scopes.Length * 2);
        _scopes[_scopeDepth] = Hash(_scopes[_scopeDepth - 1], key);
        _scopeDepth++;
    }

    internal void PushScope(int key)
    {
        if (_scopeDepth == _scopes.Length)
            Array.Resize(ref _scopes, _scopes.Length * 2);
        _scopes[_scopeDepth] = Hash(_scopes[_scopeDepth - 1], key);
        _scopeDepth++;
    }

    internal void PopScope() => _scopeDepth--;

    /// <summary>Gives a node a stable id from a key in the current scope.</summary>
    internal void SetKey(int node, string key)
    {
        _nodes[node].Id = Hash(Scope, key);
        _keys[node] = key;
        _nodes[node].Flags |= Keyed;
    }

    internal void SetKey(int node, int key)
    {
        _nodes[node].Id = Hash(Scope, key);
        _nodes[node].KeyNumber = key;
        _nodes[node].Flags |= Keyed;
    }

    /// <summary>Not a UiElementFlags fact the list reports: masked off at emit.</summary>
    internal const UiElementFlags Keyed = (UiElementFlags)0x8000;

    /// <summary>FNV-1a of a key under a seed.</summary>
    internal static uint Hash(uint seed, string key)
    {
        uint h = seed ^ 2166136261u;
        foreach (char c in key)
        {
            h ^= c;
            h *= 16777619u;
        }
        return h == 0 ? 1u : h;
    }

    internal static uint Hash(uint seed, int key)
    {
        uint h = seed ^ 0x9e3779b9u;
        h ^= (uint)key;
        h *= 16777619u;
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        return h == 0 ? 1u : h;
    }

    /// <summary>The id a key would have in the current scope: what a widget's state is found by.</summary>
    internal uint IdOf(string key) => Hash(Scope, key);

    // ---- state kept between frames, by id ----

    private readonly Dictionary<Type, object> _statePools = new();

    private sealed class StatePool<T> : IStatePool where T : struct
    {
        public readonly Dictionary<uint, T> Values = new();

        public int Size => System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

        public bool Carried => !System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        public void Write(BinaryWriter w) => CarryPool(this, w);

        public void Read(BinaryReader r, int count) => RestorePool(this, r, count);
    }

    /// <summary>Per-id state of type <typeparamref name="T"/>, created zeroed on first use and kept by the layer.</summary>
    internal ref T State<T>(uint id) where T : struct
    {
        if (!_statePools.TryGetValue(typeof(T), out var pool))
        {
            pool = new StatePool<T>();
            _statePools[typeof(T)] = pool;
        }

        return ref CollectionsMarshal.GetValueRefOrAddDefault(((StatePool<T>)pool).Values, id, out _);
    }

    private struct Eased
    {
        public float Value;
        public bool Set;
    }

    /// <summary>Eases the value under <paramref name="id"/> towards <paramref name="target"/>.</summary>
    internal float Animate(uint id, float target, Motion motion)
    {
        ref var e = ref State<Eased>(id);
        float duration = Theme.Duration(motion);
        if (!e.Set || duration <= 0f)
        {
            e.Value = target;
            e.Set = true;
            return target;
        }

        // Exponential approach: about 95% of the way in `duration`.
        float k = 1f - MathF.Exp(-3f * Dt / duration);
        e.Value += (target - e.Value) * k;
        if (MathF.Abs(target - e.Value) < 0.001f)
            e.Value = target;
        return e.Value;
    }

    // ---- slots ----

    internal void FillSlot(ref UiFrame ui, string slot)
    {
        foreach (var c in _contributors)
        {
            if (!c.Contributes(slot))
                continue;
            ui.Describe(c, slot);
        }
    }

    // ---- names, for reports ----

    /// <summary>A node's short name: its key, its row number, its text or its kind.</summary>
    internal string Describe(int node)
    {
        ref var n = ref _nodes[node];
        if (_keys[node] is { } key)
            return key;
        if (n.Kind == NodeKind.Text)
            return $"\"{TextOf(n)}\"";
        if (n.KeyNumber >= 0)
            return $"row {n.KeyNumber}";
        return n.Kind.ToString();
    }

    internal void Name(int node, string name) => _keys[node] = name;

    private UiRect SafeRect()
    {
        var s = Screen;
        return new UiRect(s.Safe.Left, s.Safe.Top, s.W - s.Safe.Left - s.Safe.Right, s.H - s.Safe.Top - s.Safe.Bottom);
    }

    private UiRect FullScreen() => new(0, 0, Screen.W, Screen.H);

    private uint[] _keyedIds = new uint[64];

    private void CheckCollisions()
    {
        // Keyed ids must be unique within a frame: two widgets with one id share
        // hover, focus and scroll, which is the bug sub-panel scopes exist to stop.
        // Sorted, so a 10,000-row list costs n log n and no allocation.
        int count = 0;
        for (int i = 0; i < _nodeCount; i++)
        {
            if ((_nodes[i].Flags & Keyed) == 0)
                continue;
            if (count == _keyedIds.Length)
                Array.Resize(ref _keyedIds, _keyedIds.Length * 2);
            _keyedIds[count++] = _nodes[i].Id;
        }
        var ids = _keyedIds.AsSpan(0, count);
        ids.Sort();
        for (int k = 1; k < count; k++)
        {
            if (ids[k] != ids[k - 1] || (k > 1 && ids[k - 2] == ids[k]))
                continue;
            // A duplicate: name the two nodes (rare; allocation here is fine).
            int first = -1;
            for (int i = 0; i < _nodeCount; i++)
            {
                if ((_nodes[i].Flags & Keyed) == 0 || _nodes[i].Id != ids[k])
                    continue;
                if (first < 0)
                    first = i;
                else
                {
                    Report.AddCollision(_screenName, ids[k], first, i, this);
                    break;
                }
            }
        }
    }
}
