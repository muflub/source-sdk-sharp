using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>An id two keyed elements share in one frame.</summary>
public readonly record struct UiIdCollision(string Screen, uint Id, string First, string Second);

/// <summary>
/// What the lint and the input pass need to know about each element beyond the
/// render list: its type role and tones, the clip it was drawn under, how far
/// its text overflowed. Parallel to the list's elements, reused every frame.
/// </summary>
public sealed class UiFrameReport
{
    private Text[] _role = new Text[256];
    private Tone[] _textTone = new Tone[256];
    private Tone[] _fillTone = new Tone[256];
    private byte[] _bits = new byte[256];
    private UiRect[] _clip = new UiRect[256];
    private float[] _overflow = new float[256];
    private float[] _fade = new float[256];
    private readonly List<UiIdCollision> _collisions = new();

    private const byte Large = 1, Blocker = 2, Hidden = 4, ScrollBit = 8, ThumbBit = 16, MovableBit = 32;

    /// <summary>How many screens this frame described.</summary>
    public int Screens { get; internal set; }

    public IReadOnlyList<UiIdCollision> Collisions => _collisions;

    internal void Clear()
    {
        Screens = 0;
        if (_collisions.Count > 0)
            _collisions.Clear();
    }

    private void Ensure(int i)
    {
        if (i < _role.Length)
            return;
        int n = Math.Max(_role.Length * 2, i + 1);
        Array.Resize(ref _role, n);
        Array.Resize(ref _textTone, n);
        Array.Resize(ref _fillTone, n);
        Array.Resize(ref _bits, n);
        Array.Resize(ref _clip, n);
        Array.Resize(ref _overflow, n);
        Array.Resize(ref _fade, n);
    }

    internal void Set(int element, Text role, Tone textTone, Tone fillTone, bool large, bool blocker,
                      UiRect clip = default, bool hidden = false, bool scroll = false, float fade = 1f)
    {
        _fade[element] = fade;
        Ensure(element);
        _role[element] = role;
        _textTone[element] = textTone;
        _fillTone[element] = fillTone;
        _bits[element] = (byte)((large ? Large : 0) | (blocker ? Blocker : 0) | (hidden ? Hidden : 0) | (scroll ? ScrollBit : 0));
        _clip[element] = blocker ? new UiRect(float.MinValue / 4, float.MinValue / 4, float.MaxValue / 2, float.MaxValue / 2) : clip;
        // An overflow recorded during emit (before Set) is kept.
    }

    internal void Overflow(int element, float px)
    {
        Ensure(element);
        _overflow[element] = px;
    }

    internal void ResetOverflow(int element)
    {
        Ensure(element);
        _overflow[element] = 0f;
    }

    internal void AddCollision(string screen, uint id, int a, int b, UiContext ctx)
        => _collisions.Add(new UiIdCollision(screen, id, ctx.Describe(a), ctx.Describe(b)));

    /// <summary>The type role of a text element; 255 for any other kind.</summary>
    public Text RoleOf(int element) => _role[element];

    public Tone TextToneOf(int element) => _textTone[element];

    public Tone FillToneOf(int element) => _fillTone[element];

    /// <summary>Text at 24 du or more: the 3:1 contrast rule applies instead of 4.5:1.</summary>
    public bool IsLargeText(int element) => (_bits[element] & Large) != 0;

    public bool IsBlocker(int element) => (_bits[element] & Blocker) != 0;

    /// <summary>Drawn nothing: wholly outside its clip or faded out.</summary>
    public bool IsHidden(int element) => (_bits[element] & Hidden) != 0;

    public bool IsScroll(int element) => (_bits[element] & ScrollBit) != 0;

    /// <summary>A scrollbar thumb: dragged, never a focus stop.</summary>
    public bool IsThumb(int element) => (_bits[element] & ThumbBit) != 0;

    /// <summary>A window's frame: placed by the player (kept reachable by the window's clamp, not by the lint).</summary>
    public bool IsMovable(int element) => (_bits[element] & MovableBit) != 0;

    internal void MarkMovable(int element)
    {
        Ensure(element);
        _bits[element] |= MovableBit;
    }

    internal void MarkThumb(int element)
    {
        Ensure(element);
        _bits[element] |= ThumbBit;
    }

    /// <summary>The clip the element was drawn under, in pixels.</summary>
    public UiRect ClipOf(int element) => _clip[element];

    /// <summary>
    /// The element's opacity from <c>.Opacity()</c> on it and its ancestors: a
    /// fade in progress (a chat line going, a panel arriving), which the lint
    /// judges at full strength, as it will be read.
    /// </summary>
    public float FadeOf(int element) => _fade[element];

    /// <summary>How many pixels an overflowing text ran past its box.</summary>
    public float OverflowOf(int element) => _overflow[element];
}
