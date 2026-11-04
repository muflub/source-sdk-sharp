namespace SourceSharp.Client.Vgui;

/// <summary>
/// Shapes waiting to go down as one mesh: vertices, a colour per vertex, and
/// indexed triangles. A rect is the triangles (0,1,2) (0,2,3), a polygon a fan
/// from its first vertex: how the surface splits them, so a shape covers the
/// same pixels from here. Fixed buffers made once: a frame allocates nothing.
/// </summary>
internal sealed class UiMeshBatch
{
    /// <summary>What one mesh holds. A 256-sided polygon, the most a shape has, fits,
    /// and these are well inside what the engine's dynamic mesh takes.</summary>
    public const int MaxVertices = 8192, MaxIndices = 24576;

    private readonly float[] _xyuv = new float[MaxVertices * 4];
    private readonly uint[] _rgba = new uint[MaxVertices];
    private readonly ushort[] _indices = new ushort[MaxIndices];
    private int _vertices, _indexCount;

    public bool IsEmpty => _indexCount == 0;

    public ReadOnlySpan<float> Xyuv => _xyuv.AsSpan(0, _vertices * 4);

    public ReadOnlySpan<uint> Rgba => _rgba.AsSpan(0, _vertices);

    public ReadOnlySpan<ushort> Indices => _indices.AsSpan(0, _indexCount);

    public bool Fits(int vertices, int indices) => _vertices + vertices <= MaxVertices && _indexCount + indices <= MaxIndices;

    public void Clear() => _vertices = _indexCount = 0;

    /// <summary>A rect's corners clockwise from the top left, each in its own colour.</summary>
    public void AddRect(int x0, int y0, int x1, int y1, uint c0, uint c1, uint c2, uint c3)
    {
        int first = _vertices;
        Vertex(x0, y0, 0f, 0f, c0);
        Vertex(x1, y0, 0f, 0f, c1);
        Vertex(x1, y1, 0f, 0f, c2);
        Vertex(x0, y1, 0f, 0f, c3);
        Triangle(first, first + 1, first + 2);
        Triangle(first, first + 2, first + 3);
    }

    /// <summary>A convex polygon of (x, y, u, v) vertices in one colour.</summary>
    public void AddFan(ReadOnlySpan<float> xyuv, uint rgba)
    {
        int first = _vertices, n = xyuv.Length / 4;
        for (int i = 0; i < n; i++)
            Vertex(xyuv[i * 4], xyuv[i * 4 + 1], xyuv[i * 4 + 2], xyuv[i * 4 + 3], rgba);
        for (int i = 1; i < n - 1; i++)
            Triangle(first, first + i, first + i + 1);
    }

    private void Vertex(float x, float y, float u, float v, uint rgba)
    {
        int at = _vertices++;
        _xyuv[at * 4] = x;
        _xyuv[at * 4 + 1] = y;
        _xyuv[at * 4 + 2] = u;
        _xyuv[at * 4 + 3] = v;
        _rgba[at] = rgba;
    }

    private void Triangle(int a, int b, int c)
    {
        _indices[_indexCount++] = (ushort)a;
        _indices[_indexCount++] = (ushort)b;
        _indices[_indexCount++] = (ushort)c;
    }
}
