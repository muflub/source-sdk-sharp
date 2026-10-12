using System.Buffers.Binary;
using System.Text;

using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// A window's sizes, du: the default (used until it is moved or stored), the
/// limits, and the width below which its content uses its narrow arrangement
/// (ui.Narrow inside the window).
/// </summary>
public readonly record struct UiWindowSpec(float DefaultW, float DefaultH, float MinW, float MinH, float MaxW, float MaxH,
                                           float NarrowBelowDu);

/// <summary>A screen that is a window: the layer orders it by the window's rank and closes it when the window asks.</summary>
public interface IUiWindowed
{
    string WindowId { get; }
}

/// <summary>
/// The windows of a UI layer (plan_client_ui §5.7a, "Windows", user ruling
/// 2026-10-05): where each is and how big, in du from the screen's top-left;
/// their z-order (the last raised is on top); which asked to close. One per
/// layer, from DI, shared by every windowed screen. Placements come from the
/// <see cref="IUiWindowStore"/> the first time a window opens and go back to it
/// when a move or a resize ends; <see cref="Carry"/> and <see cref="Restore"/>
/// take them across a UI-only reload (the core's IReloadParticipant calls them).
/// Nothing here allocates per frame once each window has been seen.
/// </summary>
public sealed class UiWindows
{
    private sealed class State
    {
        public UiWindowPlacement Place;
        public bool Placed;
        public UiRect LastRectPx;
        public long LastFrame = -1;
        public UiWindowHandle Dragging;
    }

    private readonly IUiWindowStore _store;
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly List<string> _z = new();
    private readonly List<string> _closeRequests = new();
    private long _frame = -1;

    public UiWindows(IUiWindowStore store) => _store = store;

    /// <summary>The window on top (focused), or null.</summary>
    public string? Top => _z.Count > 0 ? _z[^1] : null;

    /// <summary>A window's rank: 0 at the bottom, higher on top; -1 for one never shown.</summary>
    public int Rank(string id) => _z.IndexOf(id);

    public void Raise(string id)
    {
        int at = _z.IndexOf(id);
        if (at == _z.Count - 1 && at >= 0)
            return;
        if (at >= 0)
            _z.RemoveAt(at);
        _z.Add(id);
    }

    /// <summary>Where a window is, if it has been placed.</summary>
    public bool TryGet(string id, out UiWindowPlacement placement)
    {
        if (_states.TryGetValue(id, out var s) && s.Placed)
        {
            placement = s.Place;
            return true;
        }
        placement = default;
        return false;
    }

    /// <summary>Asks the layer to close the window's screen (Esc, the close button).</summary>
    public void RequestClose(string id)
    {
        if (!_closeRequests.Contains(id))
            _closeRequests.Add(id);
    }

    /// <summary>The windows that asked to close since the last call; the layer closes their screens.</summary>
    public int TakeCloseRequests(Span<string?> into)
    {
        int n = Math.Min(into.Length, _closeRequests.Count);
        for (int i = 0; i < n; i++) into[i] = _closeRequests[i];
        _closeRequests.Clear();
        return n;
    }

    /// <summary>Whether the window asked to close this frame: its popups stop drawing at once.</summary>
    public bool Closing(string id) => _closeRequests.Contains(id);

    /// <summary>
    /// Once a frame, before the first window describes: a button that went down
    /// raises the topmost window under it (as of last frame's rects).
    /// </summary>
    internal void BeginFrame(UiContext ctx)
    {
        if (_frame == ctx.FrameNumber)
            return;
        _frame = ctx.FrameNumber;
        if (!ctx.DownThisFrame)
            return;
        for (int i = _z.Count - 1; i >= 0; i--)
        {
            var s = _states[_z[i]];
            if (s.LastFrame < ctx.FrameNumber - 1)
                continue;   // not shown last frame
            var r = s.LastRectPx;
            if (ctx.DownX >= r.X && ctx.DownY >= r.Y && ctx.DownX < r.X + r.W && ctx.DownY < r.Y + r.H)
            {
                Raise(_z[i]);
                return;
            }
        }
    }

    /// <summary>The window's state, placed from the store or its default the first time.</summary>
    internal ref UiWindowPlacement Place(string id, in UiWindowSpec spec, float screenW, float screenH, out bool fresh)
    {
        if (!_states.TryGetValue(id, out var s))
        {
            s = new State();
            _states[id] = s;
            _z.Add(id);   // a newly opened window comes up on top
        }
        fresh = !s.Placed;
        if (!s.Placed)
        {
            if (_store.TryLoad(id, out var stored))
                s.Place = stored;
            else
            {
                // Centred by the size the limits will give it (the widget's
                // clamp, applied first here): a default under a minimum wider
                // than the screen grows to the screen, and centred by the
                // smaller default it ran off the right edge unprompted.
                float w = Math.Clamp(spec.DefaultW, MathF.Min(spec.MinW, screenW), MathF.Min(spec.MaxW, screenW));
                float h = Math.Clamp(spec.DefaultH, MathF.Min(spec.MinH, screenH), MathF.Min(spec.MaxH, screenH));
                s.Place = new UiWindowPlacement((screenW - w) / 2, (screenH - h) / 2, w, h);
            }
            s.Placed = true;
        }
        return ref PlaceRef(s);
    }

    private static ref UiWindowPlacement PlaceRef(State s) => ref s.Place;

    internal void Seen(string id, long frame, in UiRect rectPx)
    {
        var s = _states[id];
        s.LastFrame = frame;
        s.LastRectPx = rectPx;
    }

    internal ref UiWindowHandle Dragging(string id) => ref _states[id].Dragging;

    /// <summary>A move or resize ended: keep it for the next session.</summary>
    internal void Store(string id) => _store.Save(id, _states[id].Place);

    // ---- across a UI-only reload ----

    /// <summary>The placements and the z-order, as bytes (little-endian, length-prefixed UTF-8).</summary>
    public byte[] Carry()
    {
        var bytes = new List<byte>();
        void Int(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); bytes.AddRange(b.ToArray()); }
        void Str(string v) { var u = Encoding.UTF8.GetBytes(v); Int(u.Length); bytes.AddRange(u); }
        void Flt(float v) => Int(BitConverter.SingleToInt32Bits(v));

        Int(_z.Count);
        foreach (var id in _z)
        {
            var p = _states[id].Place;
            Str(id);
            Int(_states[id].Placed ? 1 : 0);
            Flt(p.X); Flt(p.Y); Flt(p.W); Flt(p.H);
        }
        return bytes.ToArray();
    }

    public void Restore(byte[] carried)
    {
        int at = 0;
        int Int() { int v = BinaryPrimitives.ReadInt32LittleEndian(carried.AsSpan(at, 4)); at += 4; return v; }
        float Flt() => BitConverter.Int32BitsToSingle(Int());
        string Str() { int n = Int(); var v = Encoding.UTF8.GetString(carried, at, n); at += n; return v; }

        _states.Clear();
        _z.Clear();
        int count = Int();
        for (int i = 0; i < count; i++)
        {
            string id = Str();
            bool placed = Int() != 0;
            var p = new UiWindowPlacement(Flt(), Flt(), Flt(), Flt());
            _states[id] = new State { Place = p, Placed = placed };
            _z.Add(id);
        }
        if (at != carried.Length)
            throw new InvalidDataException($"window state: {carried.Length - at} byte(s) left over");
    }
}

/// <summary>Which part of a window the pointer is dragging.</summary>
[Flags]
public enum UiWindowHandle : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
    /// <summary>The title bar: the whole window moves.</summary>
    Move = 16,
}

/// <summary>Windows' placements kept in memory: samples and tests (the game's store writes the mod's config).</summary>
public sealed class MemoryWindowStore : IUiWindowStore
{
    private readonly Dictionary<string, UiWindowPlacement> _saved = new(StringComparer.Ordinal);

    public int Saves { get; private set; }

    public MemoryWindowStore With(string id, UiWindowPlacement placement)
    {
        _saved[id] = placement;
        return this;
    }

    public bool TryLoad(string windowId, out UiWindowPlacement placement) => _saved.TryGetValue(windowId, out placement);

    public void Save(string windowId, UiWindowPlacement placement)
    {
        _saved[windowId] = placement;
        Saves++;
    }
}
