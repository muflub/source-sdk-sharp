using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

// INPUT (plan_client_ui §5.6): events queue between frames and are resolved at
// the start of the next against the LAST frame's laid-out targets: the usual
// one-frame immediate-mode lag. Overlays were emitted last, so walking the
// targets backwards meets them first; a modal's blocker stops the walk, so
// nothing under a modal is hit.
public sealed partial class UiContext
{
    private struct HitTarget
    {
        public uint Id;
        public UiRect Rect;
        public UiRect Clip;
        public bool Scroll;
        public bool Blocker;
        public bool Thumb;
    }

    private UiInputEvent[] _queue = new UiInputEvent[32];
    private int _queued;

    private HitTarget[] _targets = new HitTarget[64];
    private int _targetCount;

    private uint[] _clicked = new uint[4];
    private int _clickedCount;
    private int[] _keyPresses = new int[8];
    private int _keyCount;
    private char[] _typed = new char[16];
    private int _typedCount;

    // Every key and button that went down this frame, Tab and the wheel's two
    // fake buttons too: what a key capture takes (plan_client_ui §6).
    private int[] _buttons = new int[8];
    private int _buttonCount;

    /// <summary>The wheel as inputsystem's fake buttons (ButtonCode.h MOUSE_WHEEL_UP, MOUSE_WHEEL_DOWN).</summary>
    public const int WheelUpButton = 112, WheelDownButton = 113;

    private uint _active;
    private uint _wheelTarget;
    private float _wheelDelta;

    /// <summary>Where the mouse is, in pixels.</summary>
    public float MouseX { get; private set; } = -1f;

    public float MouseY { get; private set; } = -1f;

    /// <summary>The target under the mouse as of the start of this frame (0 for none).</summary>
    public uint Hovered { get; private set; }

    /// <summary>Queues an event for the next frame.</summary>
    public void Input(in UiInputEvent e)
    {
        if (_queued == _queue.Length)
            Array.Resize(ref _queue, _queue.Length * 2);
        _queue[_queued++] = e;
    }

    /// <summary>The focused element or scroll region (0 for none): keys go to it, Tab moves it.</summary>
    public uint Focused { get; private set; }

    /// <summary>Focus moved by the keyboard this frame: the scroll region holding it brings it into view.</summary>
    internal bool FocusMovedByKey { get; private set; }

    /// <summary>What was held down at the start of this frame's input: a drag moves only once it has begun.</summary>
    internal uint ActiveBefore { get; private set; }

    /// <summary>The mouse's x at the start of this frame's input: what a dragged window moves by.</summary>
    internal float MouseXBefore { get; private set; }

    /// <summary>A button went down this frame, at (DownX, DownY): what raises a window.</summary>
    internal bool DownThisFrame { get; private set; }

    internal float DownX { get; private set; }

    internal float DownY { get; private set; }

    /// <summary>Counts frames: what lets a service run once a frame however many screens call it.</summary>
    public long FrameNumber { get; private set; }

    /// <summary>The pointer shape asked for this frame (set by window handles); it reaches the render list.</summary>
    public UiCursor Cursor { get; internal set; }

    /// <summary>The mouse's y at the start of this frame's input: what a dragged thumb moves by.</summary>
    internal float MouseYBefore { get; private set; }

    private void ProcessInput()
    {
        MouseYBefore = MouseY;
        MouseXBefore = MouseX;
        DownThisFrame = false;
        FrameNumber++;
        Cursor = UiCursor.Default;
        ActiveBefore = _active;
        FocusMovedByKey = false;
        _clickedCount = 0;
        _keyCount = 0;
        _typedCount = 0;
        _buttonCount = 0;
        _wheelTarget = 0;
        _wheelDelta = 0f;

        for (int i = 0; i < _queued; i++)
        {
            ref readonly var e = ref _queue[i];
            switch (e.Kind)
            {
                case UiInputKind.MouseMove:
                    MouseX = e.X;
                    MouseY = e.Y;
                    break;
                case UiInputKind.MouseDown:
                    MouseX = e.X;
                    MouseY = e.Y;
                    DownThisFrame = true;
                    AddButton(e.Code);
                    DownX = e.X;
                    DownY = e.Y;
                    _active = HitTest(e.X, e.Y, scroll: false);
                    // Focus follows the click: what was hit, else the scroll region under it.
                    Focused = _active != 0 && !IsThumbTarget(_active) ? _active : HitTest(e.X, e.Y, scroll: true);
                    break;
                case UiInputKind.MouseUp:
                {
                    MouseX = e.X;
                    MouseY = e.Y;
                    uint id = HitTest(e.X, e.Y, scroll: false);
                    if (id != 0 && id == _active)
                    {
                        if (_clickedCount == _clicked.Length)
                            Array.Resize(ref _clicked, _clicked.Length * 2);
                        _clicked[_clickedCount++] = id;
                    }
                    _active = 0;
                    break;
                }
                case UiInputKind.Wheel:
                    if (e.Delta != 0f)
                        AddButton(e.Delta > 0f ? WheelUpButton : WheelDownButton);
                    _wheelTarget = HitTest(e.X, e.Y, scroll: true);
                    _wheelDelta += e.Delta;
                    break;
                case UiInputKind.Key when e.Code == (int)UiKey.Tab:
                    AddButton(e.Code);
                    Focused = NextFocus(Focused);
                    FocusMovedByKey = true;
                    break;
                case UiInputKind.Key:
                    AddButton(e.Code);
                    if (_keyCount == _keyPresses.Length)
                        Array.Resize(ref _keyPresses, _keyPresses.Length * 2);
                    _keyPresses[_keyCount++] = e.Code;
                    break;
                case UiInputKind.Char:
                    if (_typedCount == _typed.Length)
                        Array.Resize(ref _typed, _typed.Length * 2);
                    _typed[_typedCount++] = e.Char;
                    break;
                case UiInputKind.FocusLost:
                    _active = 0;
                    break;
            }
        }

        _queued = 0;
        Hovered = HitTest(MouseX, MouseY, scroll: false);
    }

    private bool IsThumbTarget(uint id)
    {
        for (int i = 0; i < _targetCount; i++)
            if (_targets[i].Id == id)
                return _targets[i].Thumb;
        return false;
    }

    /// <summary>The interactive target after <paramref name="current"/> in last frame's order, wrapping.</summary>
    private uint NextFocus(uint current)
    {
        int at = -1;
        for (int i = 0; i < _targetCount; i++)
            if (_targets[i].Id == current)
                at = i;
        for (int k = 1; k <= _targetCount; k++)
        {
            ref readonly var t = ref _targets[(at + k + _targetCount) % _targetCount];
            if (!t.Scroll && !t.Blocker && !t.Thumb)
                return t.Id;
        }
        return current;
    }

    private static bool Inside(in UiRect r, float x, float y)
        => x >= r.X && y >= r.Y && x < r.X + r.W && y < r.Y + r.H;

    private uint HitTest(float x, float y, bool scroll)
    {
        for (int i = _targetCount - 1; i >= 0; i--)
        {
            ref readonly var t = ref _targets[i];
            if (t.Blocker)
                return 0;
            if (t.Scroll != scroll || !Inside(t.Rect, x, y) || !Inside(t.Clip, x, y))
                continue;
            return t.Id;
        }
        return 0;
    }

    /// <summary>Keeps this frame's targets (interactive and scrolling elements, and modal blockers) for the next.</summary>
    private void KeepHitTargets()
    {
        List.Cursor = Cursor;
        _targetCount = 0;
        var elements = List.AsDrawList().Elements;
        for (int i = 0; i < elements.Length; i++)
        {
            ref readonly var e = ref elements[i];
            bool blocker = Report.IsBlocker(i);
            bool interactive = (e.Flags & UiElementFlags.Interactive) != 0;
            bool scroll = Report.IsScroll(i);
            if (!blocker && !interactive && !scroll)
                continue;
            if (_targetCount == _targets.Length)
                Array.Resize(ref _targets, _targets.Length * 2);
            _targets[_targetCount++] = new HitTarget
            {
                Id = e.Id,
                Rect = e.Rect,
                Clip = Report.ClipOf(i),
                Scroll = scroll,
                Blocker = blocker,
                Thumb = Report.IsThumb(i),
            };
        }
    }

    internal bool WasClicked(uint id)
    {
        for (int i = 0; i < _clickedCount; i++)
            if (_clicked[i] == id)
                return true;
        return false;
    }

    internal bool IsPressed(uint id) => _active == id && id != 0;

    /// <summary>The characters typed since the last frame (a focused text field's to take).</summary>
    internal ReadOnlySpan<char> Typed => _typed.AsSpan(0, _typedCount);

    /// <summary>Where a target was laid out LAST frame, which is what a press this frame was made against.</summary>
    internal bool TryLastRect(uint id, out UiRect rect)
    {
        for (int i = 0; i < _targetCount; i++)
        {
            if (_targets[i].Id == id)
            {
                rect = _targets[i].Rect;
                return true;
            }
        }
        rect = default;
        return false;
    }

    internal float WheelFor(uint id) => _wheelTarget == id ? _wheelDelta : 0f;

    /// <summary>Whether <paramref name="code"/> was pressed since the last frame.</summary>
    internal bool KeyPressed(int code)
    {
        for (int i = 0; i < _keyCount; i++)
            if (_keyPresses[i] == code)
                return true;
        return false;
    }

    /// <summary>Takes a key press so nothing else in this frame sees it (Esc closes one page, not two).</summary>
    private void AddButton(int code)
    {
        if (_buttonCount == _buttons.Length)
            Array.Resize(ref _buttons, _buttons.Length * 2);
        _buttons[_buttonCount++] = code;
    }

    /// <summary>Every key and button that went down this frame, in order.</summary>
    internal ReadOnlySpan<int> Buttons => _buttons.AsSpan(0, _buttonCount);

    /// <summary>A mouse button is held on some target now.</summary>
    internal bool Holding => _active != 0;

    internal bool ConsumeKey(int code)
    {
        for (int i = 0; i < _keyCount; i++)
        {
            if (_keyPresses[i] != code)
                continue;
            _keyPresses[i] = _keyPresses[--_keyCount];
            return true;
        }
        return false;
    }
}
