//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// THE DEBUG OVERLAY'S TEXT (stock CDebugOverlay::Paint, vgui_debugoverlaypanel.cpp):
/// each HUD paint, the engine's text overlays as the shell reads them, each at
/// its screen position plus 13 pixels a line offset (stock's line spacing), in
/// its colour; then the dead ones cleared, as stock's Paint ended. A line's
/// string is kept while its bytes are the same, so a steady overlay allocates
/// nothing a frame.
/// </summary>
public sealed class DebugOverlayText(IClientDebugOverlay overlay, SourceSharp.Abi.IGameClock clock) : IDebugOverlaySource
{
    /// <summary>Stock's line spacing, pixels.</summary>
    public const float LineSpacing = 13f;

    /// <summary>The most lines read a frame.</summary>
    public const int MaxLines = 256;

    private readonly DebugOverlayLine[] _lines = new DebugOverlayLine[MaxLines];
    private readonly byte[][] _bytes = new byte[MaxLines][];
    private readonly byte[] _buffer = new byte[512];
    private int _count;

    private float _readAt = float.NaN;

    /// <summary>The lines this frame: the list is read once a game-clock tick, when the HUD first asks.</summary>
    public int Count
    {
        get
        {
            float now = clock.Now;
            if (now != _readAt)
            {
                _readAt = now;
                Refresh();
            }
            return _count;
        }
    }

    public DebugOverlayLine Line(int i) => _lines[i];

    /// <summary>Reads the list for this frame; once a HUD paint.</summary>
    public void Refresh()
    {
        _count = 0;
        for (int i = 0; _count < MaxLines; i++)
        {
            int r = overlay.Text(i, out float x, out float y, out int line, out uint rgba, _buffer);
            if (r < 0)
                break;
            if (r == 0)
                continue;
            int length = Array.IndexOf(_buffer, (byte)0);
            if (length < 0)
                length = _buffer.Length;
            string text = Same(_count, length) ? _lines[_count].Text : Keep(_count, length);
            _lines[_count++] = new DebugOverlayLine(x, y + line * LineSpacing,
                new UiColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba), text);
        }
        overlay.ClearDead();
    }

    private bool Same(int slot, int length)
        => _bytes[slot] is { } b && b.Length == length && _buffer.AsSpan(0, length).SequenceEqual(b);

    private string Keep(int slot, int length)
    {
        _bytes[slot] = _buffer.AsSpan(0, length).ToArray();
        return System.Text.Encoding.UTF8.GetString(_buffer, 0, length);
    }
}
