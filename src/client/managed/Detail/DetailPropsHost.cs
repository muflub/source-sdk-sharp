using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>
/// <see cref="DetailPropSystem"/> as native's client system asks it
/// (<see cref="IClientDetailProps"/>): each answer written into one pinned
/// buffer of int32 words in <see cref="DetailPropsWire"/>'s layouts, which
/// stays valid until the next answer. One crossing per buffer.
///
/// <para>
/// A reload participant: a cs_cl_reload mid-level carries Init's defaults,
/// the last list build's fade and the random stream, and parses the loaded
/// level's lump again through the engine (native already holds the level's
/// renderables and leaf tables, so that parse hands native nothing).
/// </para>
/// </summary>
public sealed class DetailPropsHost(DetailPropSystem system) : IClientDetailProps, IDetailLevelSink, IDetailViewSink, IDetailDrawSink,
                                                              IReloadParticipant
{
    // The level a reload parses again: LevelInitPreEntity's inputs and the
    // random stream as the parse found it.
    private bool _levelActive;
    private int _levelWidth, _levelHeight;
    private bool _levelHdr;
    private uint _levelRandom;

    private int[] _buffer = GC.AllocateArray<int>(4096, pinned: true);
    private int _used;

    // The counts a layout fills in once its entries are written.
    private int _countAt;
    private int _vertexCountAt = -1;

    /// <summary>The service itself, for the facts that read a layout back.</summary>
    public DetailPropSystem System => system;

    private void Reset()
    {
        _used = 1;
        _vertexCountAt = -1;
    }

    private void Put(int word)
    {
        if (_used == _buffer.Length)
        {
            int[] grown = GC.AllocateArray<int>(_buffer.Length * 2, pinned: true);
            Array.Copy(_buffer, grown, _used);
            _buffer = grown;
        }
        _buffer[_used++] = word;
    }

    private void Put(float f) => Put(BitConverter.SingleToInt32Bits(f));

    private void Put(nint pointer)
    {
        long p = pointer;
        Put(unchecked((int)p));
        Put(unchecked((int)(p >> 32)));
    }

    private void Put(Vector v)
    {
        Put(v.X);
        Put(v.Y);
        Put(v.Z);
    }

    private nint Finish()
    {
        _buffer[0] = _used;
        return Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0);
    }

    /// <summary>The last answer's words, for the facts.</summary>
    public ReadOnlySpan<int> Words => _buffer.AsSpan(0, _used);

    // -------------------------------------------------------------------------
    // IClientDetailProps
    // -------------------------------------------------------------------------

    public void Init() => system.Init();

    public nint LevelInitPreEntity(int materialWidth, int materialHeight, bool hdr)
    {
        _levelActive = true;
        _levelWidth = materialWidth;
        _levelHeight = materialHeight;
        _levelHdr = hdr;
        _levelRandom = system.RandomState;
        Reset();
        Put(0);				// flags, below
        _countAt = _used;
        Put(0);				// objects
        _runs.Clear();
        bool precache = system.LevelInitPreEntity(materialWidth, materialHeight, hdr, this);
        _buffer[1] = precache ? DetailPropsWire.LevelPrecache : 0;
        _buffer[_countAt] = system.ObjectCount;
        Put(_runs.Count);
        foreach (var (leaf, first, count, fast) in _runs)
        {
            Put(leaf);
            Put(first);
            Put(count);
            Put(fast ? 1 : 0);
        }
        return Finish();
    }

    private readonly List<(int Leaf, int First, int Count, bool Fast)> _runs = [];

    public string LevelInitPostEntity(string? worldMaterial) => system.LevelInitPostEntity(worldMaterial);

    public void LevelShutdownPreEntity()
    {
        _levelActive = false;
        system.LevelShutdownPreEntity();
    }

    private readonly List<nint> _proxies = [];

    public nint BuildLists(Vector viewOrigin, Vector currentViewOrigin, bool clientModeDraws, int drawDetailProps, int fullbright)
    {
        Reset();
        _proxies.Clear();
        _states.Clear();
        float? radius = system.BuildRenderLists(viewOrigin, currentViewOrigin, clientModeDraws, drawDetailProps, fullbright, this);
        Put(radius.HasValue ? 1 : 0);
        Put(radius ?? 0.0f);
        Put(_proxies.Count);
        foreach (nint p in _proxies)
            Put(p);
        Put(_states.Count);
        foreach (var (alpha, setAngles, angles, color) in _states)
        {
            Put(alpha | (setAngles ? DetailPropsWire.ModelSetAngles : 0));
            Put(angles);
            Put(color);
        }
        return Finish();
    }

    private readonly List<(byte Alpha, bool SetAngles, Vector Angles, Vector Color)> _states = [];

    public void BeginTranslucent() => system.BeginTranslucent();

    public nint Prepare(int kind, in DetailView view, ReadOnlySpan<int> leaves, Vector? closest, DetailSwitches switches)
    {
        Reset();
        Put(DetailPropsWire.MaterialNone);
        _countAt = _used;
        Put(0);				// alphas
        _alphas = 0;
        DetailDrawMaterial material = (DetailDrawKind)kind is DetailDrawKind.FastList or DetailDrawKind.OldList
            ? system.PrepareList((DetailDrawKind)kind, view, leaves, switches, this)
            : system.PrepareInLeaf((DetailDrawKind)kind, view, leaves.Length > 0 ? leaves[0] : -1, closest, switches, this);
        _buffer[1] = (int)material;
        _buffer[_countAt] = _alphas;
        return Finish();
    }

    private int _alphas;
    private bool _emitting;

    public nint Emit(int maxQuads)
    {
        Reset();
        _emitting = true;
        system.Emit(maxQuads, this);
        CloseVertices();
        Put(DetailPropsWire.End);
        _emitting = false;
        return Finish();
    }

    // -------------------------------------------------------------------------
    // The reload
    // -------------------------------------------------------------------------

    void IReloadParticipant.Save(ReloadWriter writer)
    {
        DetailPropSystem.Carried c = system.Carry();
        writer.Write(c.DefaultFadeStart);
        writer.Write(c.DefaultFadeEnd);
        writer.Write(c.CurMaxSqDist);
        writer.Write(c.CurFadeSqDist);
        writer.Write(c.CurFalloffFactor);
        writer.Write(unchecked((int)c.Random));
        writer.Write(_levelActive);
        writer.Write(_levelWidth);
        writer.Write(_levelHeight);
        writer.Write(_levelHdr);
        writer.Write(unchecked((int)_levelRandom));
    }

    void IReloadParticipant.Restore(ReloadReader reader)
    {
        var c = new DetailPropSystem.Carried(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(),
                                             reader.ReadFloat(), unchecked((uint)reader.ReadInt()));
        bool active = reader.ReadBool();
        int width = reader.ReadInt(), height = reader.ReadInt();
        bool hdr = reader.ReadBool();
        uint random = unchecked((uint)reader.ReadInt());

        if (active)
        {
            system.SeedRandom(random);
            system.LevelInitPreEntity(width, height, hdr, new Discard());
            _levelActive = true;
            _levelWidth = width;
            _levelHeight = height;
            _levelHdr = hdr;
            _levelRandom = random;
        }
        system.Restore(c);
    }

    // What a reload's parse hands native: nothing, native holds it already.
    private sealed class Discard : IDetailLevelSink
    {
        public void Object(nint model, Vector origin, Vector angles) { }

        public void LeafRun(int leaf, int first, int count, bool fastSprites) { }
    }

    // -------------------------------------------------------------------------
    // The sinks
    // -------------------------------------------------------------------------

    public void Object(nint model, Vector origin, Vector angles)
    {
        Put(model);
        Put(origin);
        Put(angles);
    }

    public void LeafRun(int leaf, int first, int count, bool fastSprites) => _runs.Add((leaf, first, count, fastSprites));

    public void RecomputeTranslucency(nint model) => _proxies.Add(model);

    public void ModelState(int modelOrdinal, byte alpha, bool setAngles, Vector angles, Vector color)
        => _states.Add((alpha, setAngles, angles, color));

    private void CloseVertices()
    {
        if (_vertexCountAt < 0)
            return;
        _buffer[_vertexCountAt] = (_used - _vertexCountAt - 1) / DetailPropsWire.VertexWords;
        _vertexCountAt = -1;
    }

    public void Begin(int quads)
    {
        CloseVertices();
        Put(DetailPropsWire.Begin);
        Put(quads);
    }

    public void Vertex(float x, float y, float z, byte r, byte g, byte b, byte a, float s, float t)
    {
        if (_vertexCountAt < 0)
        {
            Put(DetailPropsWire.Vertices);
            _vertexCountAt = _used;
            Put(0);
        }
        Put(x);
        Put(y);
        Put(z);
        Put(r | (g << 8) | (b << 16) | (a << 24));
        Put(s);
        Put(t);
    }

    public void EndDraw()
    {
        CloseVertices();
        Put(DetailPropsWire.EndDraw);
    }

    public void ModelAlpha(int objectIndex, byte alpha)
    {
        if (_emitting)
        {
            CloseVertices();
            Put(DetailPropsWire.ModelAlpha);
            Put(objectIndex);
            Put(alpha);
            return;
        }
        // A sort in Prepare (a leaf drawn in pieces): the pairs follow the count.
        Put(objectIndex);
        Put(alpha);
        _alphas++;
    }
}
