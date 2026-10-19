//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>The detail convars, as the system reads and writes them.</summary>
public interface IDetailPropSettings
{
    float DetailDist { get; set; }

    float DetailFade { get; set; }

    int Multiplier { get; }

    int FastSprites { get; }
}

/// <summary>The material a draw binds: stock's m_DetailSpriteMaterial, or the wireframe one.</summary>
public enum DetailDrawMaterial
{
    /// <summary>The draw returns before it touches the render context.</summary>
    None = 0,
    Sprite = 1,
    Wireframe = 2,
}

/// <summary>Which of stock's four translucent draws a request is.</summary>
public enum DetailDrawKind
{
    /// <summary>RenderTranslucentDetailObjects' RenderFastSprites.</summary>
    FastList = 0,
    /// <summary>RenderTranslucentDetailObjects' own sorted sprites.</summary>
    OldList = 1,
    /// <summary>RenderFastTranslucentDetailObjectsInLeaf.</summary>
    FastInLeaf = 2,
    /// <summary>RenderTranslucentDetailObjectsInLeaf's own sorted sprites.</summary>
    OldInLeaf = 3,
}

/// <summary>Where the detail system's output goes: what native carries out.</summary>
public interface IDetailDrawSink
{
    /// <summary><c>CMeshBuilder::Begin( mesh, MATERIAL_QUADS, quads )</c>.</summary>
    void Begin(int quads);

    /// <summary>One vertex: position, colour (r, g, b, a), texture coordinate.</summary>
    void Vertex(float x, float y, float z, byte r, byte g, byte b, byte a, float s, float t);

    /// <summary><c>CMeshBuilder::End()</c> then <c>IMesh::Draw()</c>.</summary>
    void EndDraw();

    /// <summary>A model prop's alpha, set by a sort (CDetailModel::SetAlpha).</summary>
    void ModelAlpha(int objectIndex, byte alpha);
}

/// <summary>What one level's load gives native: its renderables and the leaf system's tables.</summary>
public interface IDetailLevelSink
{
    /// <summary>
    /// One detail object, in index order: a model prop (its model, origin and
    /// lump angles) or a sprite (model 0), which has no renderable.
    /// </summary>
    void Object(nint model, Vector origin, Vector angles);

    /// <summary>
    /// The end of a leaf's run of objects: <c>SetSubSystemDataInLeaf</c> when
    /// it held fast sprites, then <c>SetDetailObjectsInLeaf</c>.
    /// </summary>
    void LeafRun(int leaf, int first, int count, bool fastSprites);
}

/// <summary>What one view's list build gives native.</summary>
public interface IDetailViewSink
{
    /// <summary><c>RecomputeTranslucency( model, 0, 0, NULL )</c>.</summary>
    void RecomputeTranslucency(nint model);

    /// <summary>
    /// The state the k-th model prop takes if its leaf is in the sphere:
    /// its alpha, and its angles when it is in range; and the colour
    /// modulation the renderer reads when it gathers the view's lists
    /// (CDetailModel::GetColorModulation), which no view changes.
    /// </summary>
    void ModelState(int modelOrdinal, byte alpha, bool setAngles, Vector angles, Vector color);
}

/// <summary>
/// game/client/detailobjectsystem.cpp's CDetailObjectSystem and CDetailModel,
/// every decision of them: the detail lump's parse, the per-view fades and
/// screen alignment, the back-to-front sorts and every sprite quad. Native
/// keeps the model props' renderables, the leaf system's tables, the BSP
/// query and the mesh (shell_detail_props.cpp) and carries out what this
/// answers. The calls are stock's, in stock's order, and so is the state
/// they share: the sort buffers, the cursors of a leaf drawn in pieces and the
/// fade a view's list build leaves for the fast sprites.
///
/// <para>
/// Not here, as not in this build: the shaped sprites (USE_DETAIL_SHAPES is
/// DOD and CS:S only), so a cross or tri object is sorted and weighed as stock
/// weighs it and draws nothing, and the player-avoid sway (UpdatePlayerAvoid,
/// under the same guard) is not here either; and the FOV factor, 1 because the local player
/// is not native (docs/absence-client.md row 6).
/// </para>
/// </summary>
public sealed class DetailPropSystem(IDetailPropEngine engine, IDetailPropSettings settings)
{
    private const string DetailSpriteMaterial = "detail/detailsprites";

    // CDetailModel::s_pQuadCount: the quads each type weighs in a batch.
    private static ReadOnlySpan<int> QuadCount => [0, 1, 4, 3];

    private struct DetailObject
    {
        public Vector Origin;
        public Vector Angles;
        public ColorRgbExp32 Color;
        public byte Orientation;		// m_Orientation:2
        public byte Type;				// m_Type:2
        public bool HasLightStyle;
        public bool Flipped;
        public byte Alpha;
        public nint Model;
        public ushort SpriteIndex;
        public ushort ScaleHalf;		// m_flScale, a float16
        public int LightStyle;
        public int LightStyleCount;
        public int ModelOrdinal;
    }

    private sealed class FastLeaf
    {
        public int NumSprites;
        public int NumSimd;
        public int FirstGroup;
        public int NumPending;
        public int StartSpriteIndex;
    }

    // The level.
    private readonly List<nint> _dict = [];
    private readonly List<DetailSpriteDict> _spriteDict = [];
    private readonly List<DetailSpriteDict> _spriteDictFlipped = [];
    private readonly List<DetailLightstyle> _lighting = [];
    private readonly List<DetailObject> _objects = [];
    private int _modelCount;

    // The fast sprites, four lanes a group (FastSpriteX4_t).
    private float[] _posX = [], _posY = [], _posZ = [], _halfWidth = [], _height = [];
    private byte[] _rgb = [];
    private int[] _def = [];		// sprite index, bit 31 set for the flipped dictionary; -1 unset
    private bool _hasFastData;

    // The leaf system's view of the level, as the subject reads it back
    // (GetDetailObjectsInLeaf, GetSubSystemDataInLeaf): the last run wins.
    private readonly Dictionary<int, (int First, int Count)> _leafObjects = [];
    private readonly Dictionary<int, FastLeaf> _leafFast = [];

    // The sort buffers and the build-out, shared between the draws as stock's are.
    private DetailSortInfo[] _sortInfo = [];
    private DetailSortInfo[] _fastSortInfo = [];
    private float[] _bX = [], _bY = [], _bZ = [];	// [group][corner][lane]
    private byte[] _bAlpha = [];					// [group][lane]
    private byte[] _bRgb = [];						// [group][lane][4]
    private int[] _bDef = [];						// [group][lane]

    // Cursors of a leaf drawn in pieces.
    private int _spriteCount, _firstSprite, _sortedLeaf, _sortedFastLeaf;

    // Init's defaults, and the last list build's fade.
    // (Read at construction too, so a client whose Init never reached here
    // still restores the player's values rather than zeros.)
    private float _defaultFadeStart = settings.DetailFade, _defaultFadeEnd = settings.DetailDist;
    private float _curMaxSqDist, _curFadeSqDist, _curFalloffFactor;

    // The C library's rand(), which RandomVector draws for the multiplier's
    // copies: MSVC's generator, the range VALVE_RAND_MAX assumes (the
    // precipitation ruling, docs/client-novalve-rulings.tsv), one stream here.
    private uint _rand = 1;

    // The request a Prepare made and its Emit carries out.
    private DetailDrawKind _kind;
    private DetailView _view;
    private DetailSwitches _switches;
    private int[] _leaves = [];
    private int _leafCount;
    private int _drawLeaf;
    private float _minDistance;
    private int _quadCount;

    /// <summary>The C library's seed (srand): the stream the multiplier's copies draw from.</summary>
    public void SeedRandom(uint seed) => _rand = seed;

    /// <summary>The random stream's state, for a reload to carry.</summary>
    public uint RandomState => _rand;

    /// <summary>What outlives a level and a managed reload: Init's defaults and the last list build's fade.</summary>
    public readonly record struct Carried(float DefaultFadeStart, float DefaultFadeEnd, float CurMaxSqDist, float CurFadeSqDist,
                                          float CurFalloffFactor, uint Random);

    public Carried Carry() => new(_defaultFadeStart, _defaultFadeEnd, _curMaxSqDist, _curFadeSqDist, _curFalloffFactor, _rand);

    public void Restore(in Carried c)
    {
        _defaultFadeStart = c.DefaultFadeStart;
        _defaultFadeEnd = c.DefaultFadeEnd;
        _curMaxSqDist = c.CurMaxSqDist;
        _curFadeSqDist = c.CurFadeSqDist;
        _curFalloffFactor = c.CurFalloffFactor;
        _rand = c.Random;
    }

    /// <summary>The objects the level holds (m_DetailObjects.Count()).</summary>
    public int ObjectCount => _objects.Count;

    /// <summary>The model props among them.</summary>
    public int ModelCount => _modelCount;

    /// <summary>Whether the level has detail at all.</summary>
    public bool HasDetail => _hasFastData || _objects.Count > 0;

    private int Rand()
    {
        _rand = _rand * 214013u + 2531011u;
        return (int)((_rand >> 16) & 0x7fff);
    }

    // mathlib's RandomVector( minVal, maxVal ): Vector::Random, x then y then z.
    private Vector RandomVector(float minVal, float maxVal)
    {
        float x = minVal + ((float)Rand() / 0x7fff) * (maxVal - minVal);
        float y = minVal + ((float)Rand() / 0x7fff) * (maxVal - minVal);
        float z = minVal + ((float)Rand() / 0x7fff) * (maxVal - minVal);
        return new Vector(x, y, z);
    }

    //-------------------------------------------------------------------------
    // Init, level init and shutdown
    //-------------------------------------------------------------------------

    /// <summary><c>Init</c>: the player's fade distances, which every level restores.</summary>
    public void Init()
    {
        _defaultFadeStart = settings.DetailFade;
        _defaultFadeEnd = settings.DetailDist;
    }

    /// <summary>
    /// <c>LevelInitPreEntity</c> after the materials' Init: the lump's parse.
    /// <paramref name="materialWidth"/>/<paramref name="materialHeight"/> are
    /// the sprite material's mapping size, <paramref name="hdr"/> whether the
    /// HDR lighting lump is the one read. Answers whether the sprite material
    /// is precached (the level has detail objects or sprites).
    /// </summary>
    public bool LevelInitPreEntity(int materialWidth, int materialHeight, bool hdr, IDetailLevelSink sink)
    {
        bool precache = false;
        if (engine.GameLumpVersion(DetailGameLumps.DetailProps) < 4)
        {
            engine.Warning("Map uses old detail prop file format.. ignoring detail props\n");
            return false;
        }

        int size = engine.GameLumpSize(DetailGameLumps.DetailProps);
        byte[] memory = new byte[Math.Max(size, 0)];
        if (engine.LoadGameLump(DetailGameLumps.DetailProps, memory))
        {
            DetailLumpReader buf = new(memory);
            UnserializeModelDict(ref buf);
            if (engine.GameLumpVersion(DetailGameLumps.DetailProps) == 4)
            {
                UnserializeDetailSprites(ref buf);
                UnserializeModels(ref buf, sink);
            }
        }

        if (_objects.Count > 0 || _spriteDict.Count > 0)
        {
            precache = true;
            // adjust for non-square textures (cropped)
            float ratio = (float)materialWidth / materialHeight;
            if (ratio > 1.0)
            {
                for (int i = 0; i < _spriteDict.Count; i++)
                {
                    DetailSpriteDict d = _spriteDict[i];
                    d.TexUlY *= ratio;
                    d.TexLrY *= ratio;
                    _spriteDict[i] = d;
                    DetailSpriteDict f = _spriteDictFlipped[i];
                    f.TexUlY *= ratio;
                    f.TexLrY *= ratio;
                    _spriteDictFlipped[i] = f;
                }
            }
        }

        int lightingLump = hdr ? DetailGameLumps.DetailPropLightingHdr : DetailGameLumps.DetailPropLighting;
        size = engine.GameLumpSize(lightingLump);
        memory = new byte[Math.Max(size, 0)];
        if (engine.LoadGameLump(lightingLump, memory))
        {
            DetailLumpReader buf = new(memory);
            UnserializeModelLighting(ref buf);
        }

        return precache;
    }

    /// <summary>
    /// <c>LevelInitPostEntity</c>: the fades back to the player's (this build
    /// has no native detail controller; the managed one clamps them after),
    /// and the material the sprites draw with: the map's, or stock's.
    /// </summary>
    public string LevelInitPostEntity(string? worldMaterial)
    {
        string material = DetailSpriteMaterial;
        if (!string.IsNullOrEmpty(worldMaterial))
            material = worldMaterial;

        settings.DetailFade = _defaultFadeStart;
        settings.DetailDist = _defaultFadeEnd;
        return material;
    }

    /// <summary><c>LevelShutdownPreEntity</c>: the level's data and its buffers.</summary>
    public void LevelShutdownPreEntity()
    {
        _objects.Clear();
        _dict.Clear();
        _spriteDict.Clear();
        _spriteDictFlipped.Clear();
        _lighting.Clear();
        _modelCount = 0;
        _posX = _posY = _posZ = _halfWidth = _height = [];
        _rgb = [];
        _def = [];
        _hasFastData = false;
        // The leaf system forgets the level's leaves at its own shutdown.
        _leafObjects.Clear();
        _leafFast.Clear();
        FreeSortBuffers();
    }

    private void FreeSortBuffers()
    {
        _sortInfo = [];
        _fastSortInfo = [];
        _bX = _bY = _bZ = [];
        _bAlpha = [];
        _bRgb = [];
        _bDef = [];
    }

    //-------------------------------------------------------------------------
    // Unserialization
    //-------------------------------------------------------------------------

    private void UnserializeModelDict(ref DetailLumpReader buf)
    {
        int count = buf.GetInt();
        while (--count >= 0)
        {
            string name = buf.GetModelName();
            nint model = engine.LoadModel(name, true);

            // Don't allow vertex-lit models
            if (engine.IsModelVertexLit(model))
            {
                engine.Warning($"Detail prop model {name} is using vertex-lit materials!\nIt must use unlit materials!\n");
                model = engine.LoadModel("models/error.mdl", false);
            }

            _dict.Add(model);
        }
    }

    private void UnserializeDetailSprites(ref DetailLumpReader buf)
    {
        int count = buf.GetInt();
        while (--count >= 0)
        {
            DetailSpriteDict d = buf.GetSpriteDict();
            _spriteDict.Add(d);
            DetailSpriteDict f = d;
            (f.TexUlX, f.TexLrX) = (f.TexLrX, f.TexUlX);
            _spriteDictFlipped.Add(f);
        }
    }

    private void UnserializeModelLighting(ref DetailLumpReader buf)
    {
        int count = buf.GetInt();
        while (--count >= 0)
            _lighting.Add(buf.GetLightstyle());
    }

    private bool IsFastSprite(in DetailObjectLump lump)
        => settings.FastSprites != 0
           && lump.Type == (byte)DetailPropType.Sprite
           && lump.LightStyleCount == 0
           && lump.Orientation == 2
           && lump.ShapeAngle == 0
           && lump.ShapeSize == 0
           && lump.SwayAmount == 0;

    private void ScanForCounts(ref DetailLumpReader buf, out int numOld, out int numFast, out int maxOldInLeaf, out int maxFastInLeaf)
    {
        int oldpos = buf.Position;
        int count = buf.GetInt();

        int nOld = 0, nFast = 0, detailObjectLeaf = -1;
        int numOldInLeaf = 0, numFastInLeaf = 0, maxOld = 0, maxFast = 0;
        int multiplier = settings.Multiplier;
        while (--count >= 0)
        {
            DetailObjectLump lump = buf.GetObject();
            if (detailObjectLeaf != lump.Leaf)
            {
                nFast += (0 - nFast) & 3;
                maxFast = Math.Max(maxFast, numFastInLeaf);
                maxOld = Math.Max(maxOld, numOldInLeaf);
                numOldInLeaf = 0;
                numFastInLeaf = 0;
                detailObjectLeaf = lump.Leaf;
            }

            if (IsFastSprite(lump))
            {
                nFast += multiplier;
                numFastInLeaf += multiplier;
            }
            else
            {
                nOld += multiplier;
                numOldInLeaf += multiplier;
            }
        }

        nFast += (0 - nFast) & 3;
        maxFast = Math.Max(maxFast, numFastInLeaf);
        maxOld = Math.Max(maxOld, numOldInLeaf);

        buf.Seek(oldpos);
        numFast = nFast;
        numOld = nOld;
        maxFast = (3 + maxFast) & ~3;
        maxOldInLeaf = maxOld;
        maxFastInLeaf = maxFast;
    }

    private void UnserializeModels(ref DetailLumpReader buf, IDetailLevelSink sink)
    {
        int firstDetailObject = 0, detailObjectCount = 0, detailObjectLeaf = -1;

        ScanForCounts(ref buf, out int numOld, out int numFastToAllocate, out int maxOldInLeaf, out int maxFastInLeaf);

        FreeSortBuffers();
        if (maxOldInLeaf != 0)
            _sortInfo = new DetailSortInfo[3 + maxOldInLeaf];
        if (maxFastInLeaf != 0)
        {
            _fastSortInfo = new DetailSortInfo[3 + maxFastInLeaf];
            int groups = 1 + maxFastInLeaf / 4;
            _bX = new float[groups * 16];
            _bY = new float[groups * 16];
            _bZ = new float[groups * 16];
            _bAlpha = new byte[groups * 4];
            _bRgb = new byte[groups * 16];
            _bDef = new int[groups * 4];
        }
        if (numFastToAllocate != 0)
        {
            int groups = numFastToAllocate >> 2;
            _posX = new float[groups * 4];
            _posY = new float[groups * 4];
            _posZ = new float[groups * 4];
            _halfWidth = new float[groups * 4];
            _height = new float[groups * 4];
            _rgb = new byte[groups * 16];
            _def = new int[groups * 4];
            Array.Fill(_def, -1);
            _hasFastData = true;
        }

        _objects.Capacity = Math.Max(_objects.Capacity, numOld);

        int count = buf.GetInt();
        int curFastObject = 0, numFastObjectsInCurLeaf = 0, curFastGroupOut = 0;
        int multiplier = settings.Multiplier;

        bool flipped = true;
        while (--count >= 0)
        {
            flipped = !flipped;
            DetailObjectLump lump = buf.GetObject();

            // We rely on the fact that details objects are sorted by leaf in the bsp file for this
            if (detailObjectLeaf != lump.Leaf)
            {
                if (detailObjectLeaf != -1)
                {
                    bool hasFast = false;
                    if (numFastObjectsInCurLeaf != 0)
                    {
                        FastLeaf fl = new()
                        {
                            NumSprites = numFastObjectsInCurLeaf,
                            NumSimd = (3 + numFastObjectsInCurLeaf) >> 2,
                            FirstGroup = curFastGroupOut,
                        };
                        curFastGroupOut += fl.NumSimd;
                        _leafFast[detailObjectLeaf] = fl;
                        hasFast = true;
                        curFastObject += (0 - curFastObject) & 3;
                        numFastObjectsInCurLeaf = 0;
                    }
                    _leafObjects[detailObjectLeaf] = (firstDetailObject, detailObjectCount);
                    sink.LeafRun(detailObjectLeaf, firstDetailObject, detailObjectCount, hasFast);
                }

                detailObjectLeaf = lump.Leaf;
                firstDetailObject = _objects.Count;
                detailObjectCount = 0;
            }

            if (IsFastSprite(lump))
            {
                for (int i = 0; i < multiplier; i++)
                {
                    int group = curFastObject >> 2;
                    int sub = curFastObject & 3;
                    Vector pos = new(0, 0, 0);
                    if (i != 0)
                    {
                        pos = pos + RandomVector(-50, 50);
                        pos.Z = 0;
                    }
                    UnserializeFastSprite(group, sub, lump, flipped, pos);
                    if (sub == 0)
                        ReplicateFirstEntryToOthers(group);
                    curFastObject++;
                    numFastObjectsInCurLeaf++;
                }
            }
            else
            {
                switch (lump.Type)
                {
                    case (byte)DetailPropType.Model:
                    {
                        nint model = lump.DetailModel < _dict.Count ? _dict[lump.DetailModel] : 0;
                        DetailObject o = InitCommon(lump.Origin, lump.Angles, lump.Lighting, lump.LightStyles, lump.LightStyleCount, lump.Orientation);
                        o.Type = (byte)DetailPropType.Model;
                        o.Model = model;
                        o.ModelOrdinal = _modelCount++;
                        _objects.Add(o);
                        sink.Object(model, o.Origin, o.Angles);
                        ++detailObjectCount;
                        break;
                    }

                    case (byte)DetailPropType.Sprite:
                    case (byte)DetailPropType.ShapeCross:
                    case (byte)DetailPropType.ShapeTri:
                    {
                        for (int i = 0; i < multiplier; i++)
                        {
                            Vector pos = lump.Origin;
                            if (i != 0)
                            {
                                pos = pos + RandomVector(-50, 50);
                                pos.Z = lump.Origin.Z;
                            }
                            DetailObject o = InitCommon(pos, lump.Angles, lump.Lighting, lump.LightStyles, lump.LightStyleCount, lump.Orientation);
                            o.SpriteIndex = lump.DetailModel;
                            o.Type = lump.Type;
                            o.ScaleHalf = DetailMath.FloatTo16Bits(lump.Scale);
                            o.Flipped = flipped;
                            o.ModelOrdinal = -1;
                            _objects.Add(o);
                            sink.Object(0, o.Origin, o.Angles);
                            ++detailObjectCount;
                        }
                        break;
                    }
                }
            }
        }

        if (detailObjectLeaf != -1)
        {
            bool hasFast = false;
            if (numFastObjectsInCurLeaf != 0)
            {
                FastLeaf fl = new()
                {
                    NumSprites = numFastObjectsInCurLeaf,
                    NumSimd = (3 + numFastObjectsInCurLeaf) >> 2,
                    FirstGroup = curFastGroupOut,
                };
                _leafFast[detailObjectLeaf] = fl;
                hasFast = true;
            }
            _leafObjects[detailObjectLeaf] = (firstDetailObject, detailObjectCount);
            sink.LeafRun(detailObjectLeaf, firstDetailObject, detailObjectCount, hasFast);
        }
    }

    // CDetailModel::Init/InitSprite's shared part. A light style past stock's
    // 24 bits or 99 entries is Error()'s, which ends the process; here the
    // object keeps no light style and the warning says why.
    private DetailObject InitCommon(Vector origin, Vector angles, ColorRgbExp32 lighting, uint lightStyle, byte lightStyleCount, byte orientation)
    {
        DetailObject o = default;
        o.Color = lighting;
        if (lightStyleCount > 0)
        {
            if (lightStyle >= 0x1000000 || lightStyleCount >= 100)
            {
                engine.Warning("Light style overflow\n");
            }
            else
            {
                o.HasLightStyle = true;
                o.LightStyle = (int)lightStyle;
                o.LightStyleCount = lightStyleCount;
            }
        }
        o.Orientation = (byte)(orientation & 3);
        o.Origin = origin;
        o.Angles = angles;
        o.Alpha = 255;
        return o;
    }

    private Vector SpriteMiddleBottomPosition(in DetailObjectLump lump)
    {
        DetailSpriteDict dict = lump.DetailModel < _spriteDict.Count ? _spriteDict[lump.DetailModel] : default;

        Vector toward = lump.Origin + new Vector(0, -100, 0);
        Vector dir = toward - lump.Origin;
        dir.Z = 0.0f;
        Vector angles = DetailMath.VectorAngles(dir);

        DetailMath.AngleVectors(angles, out _, out Vector dx, out Vector dy);

        float scale = lump.Scale;
        float ulX = dict.UlX * scale, ulY = dict.UlY * scale;
        float lrX = dict.LrX * scale, lrY = dict.LrY * scale;

        Vector origin = DetailMath.MA(lump.Origin, ulX, dx);
        origin = DetailMath.MA(origin, ulY, dy);
        dx = dx * (lrX - ulX);
        dy = dy * (lrY - ulY);

        return origin + dy + 0.5f * dx;
    }

    private void UnserializeFastSprite(int group, int sub, in DetailObjectLump lump, bool flipped, Vector posOffset)
    {
        Vector pos = SpriteMiddleBottomPosition(lump) + posOffset;
        int lane = group * 4 + sub;
        _posX[lane] = pos.X;
        _posY[lane] = pos.Y;
        _posZ[lane] = pos.Z;

        DetailSpriteDict def = lump.DetailModel < _spriteDict.Count ? _spriteDict[lump.DetailModel] : default;
        _halfWidth[lane] = (float)(0.5 * lump.Scale * (def.LrX - def.UlX));
        _height[lane] = lump.Scale * (def.LrY - def.UlY);
        int which = lump.DetailModel;
        if (!flipped)
            which |= int.MinValue;

        ColorRgbExp32 c = lump.Lighting;
        Vector linear = new(
            DetailMath.TexLightToLinear(c.R, c.Exponent),
            DetailMath.TexLightToLinear(c.G, c.Exponent),
            DetailMath.TexLightToLinear(c.B, c.Exponent));
        Vector gamma = engine.LinearToGamma(linear);
        _rgb[lane * 4 + 0] = DetailMath.ToByte(255.0 * gamma.X);
        _rgb[lane * 4 + 1] = DetailMath.ToByte(255.0 * gamma.Y);
        _rgb[lane * 4 + 2] = DetailMath.ToByte(255.0 * gamma.Z);
        _rgb[lane * 4 + 3] = 255;
        _def[lane] = which;
    }

    // FastSpriteX4_t::ReplicateFirstEntryToOthers: every field but the
    // sprite definitions, which the next sprites fill.
    private void ReplicateFirstEntryToOthers(int group)
    {
        int l0 = group * 4;
        for (int i = 1; i < 4; i++)
        {
            _halfWidth[l0 + i] = _halfWidth[l0];
            _height[l0 + i] = _height[l0];
            for (int j = 0; j < 4; j++)
                _rgb[(l0 + i) * 4 + j] = _rgb[l0 * 4 + j];
            _posX[l0 + i] = _posX[l0];
            _posY[l0 + i] = _posY[l0];
            _posZ[l0 + i] = _posZ[l0];
        }
    }

    //-------------------------------------------------------------------------
    // The leaf system's tables, as the draws read them back
    //-------------------------------------------------------------------------

    private (int First, int Count) ObjectsInLeaf(int leaf)
        => _leafObjects.TryGetValue(leaf, out var r) ? r : (0, 0);

    private FastLeaf? FastInLeaf(int leaf) => _leafFast.TryGetValue(leaf, out FastLeaf? f) ? f : null;

    //-------------------------------------------------------------------------
    // The view's lists
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>BuildDetailObjectRenderLists( viewOrigin )</c>: the fade the view
    /// draws with and every model prop's state. Answers the radius of the
    /// sphere whose leaves native marks and applies the states to, or null
    /// when the view builds nothing.
    /// </summary>
    public float? BuildRenderLists(Vector viewOrigin, Vector currentViewOrigin, bool clientModeDrawsDetail, int drawDetailProps, int fullbright, IDetailViewSink sink)
    {
        if (!clientModeDrawsDetail || drawDetailProps == 0)
            return null;

        // Don't bother doing any of this if the level doesn't have detail props.
        if (!_hasFastData && _objects.Count == 0)
            return null;

        // We need to recompute translucency information for all detail props
        for (int i = _dict.Count; --i >= 0;)
        {
            if (engine.ModelHasMaterialProxy(_dict[i]))
                sink.RecomputeTranslucency(_dict[i]);
        }

        // The FOV factor is 1 (docs/absence-client.md row 6), and this site
        // divides by it where SortSpritesBackToFront multiplies.
        float factor = 1.0f;
        float dist = settings.DetailDist;
        _curMaxSqDist = dist * dist;
        _curFadeSqDist = dist - settings.DetailFade;
        _curMaxSqDist /= factor;
        _curFadeSqDist /= factor;
        if (_curFadeSqDist > 0)
            _curFadeSqDist *= _curFadeSqDist;
        else
            _curFadeSqDist = 0;
        _curFadeSqDist = Math.Min(_curFadeSqDist, _curMaxSqDist - 1);
        _curFalloffFactor = 255.0f / (_curMaxSqDist - _curFadeSqDist);

        // EnumerateLeaf's per-object half, for the model props: the alpha and
        // screen alignment each takes if its leaf is in the sphere.
        foreach (DetailObject o in _objects)
        {
            if (o.Type != (byte)DetailPropType.Model)
                continue;
            Vector v = o.Origin - viewOrigin;
            float sqDist = v.X * v.X + v.Y * v.Y + v.Z * v.Z;
            byte alpha = 255;
            bool setAngles = false;
            Vector angles = o.Angles;
            if (sqDist < _curMaxSqDist)
            {
                alpha = sqDist > _curFadeSqDist ? DetailMath.ToByte(_curFalloffFactor * (_curMaxSqDist - sqDist)) : (byte)255;
                if (o.Orientation is 1 or 2)
                {
                    setAngles = true;
                    angles = ScreenAligned(o.Orientation, o.Origin, currentViewOrigin);
                }
            }
            else
            {
                alpha = 0;
            }
            sink.ModelState(o.ModelOrdinal, alpha, setAngles, angles, ColorModulation(o, fullbright));
        }

        return settings.DetailDist;
    }

    // CDetailModel::ComputeAngles for orientations 1 and 2.
    private static Vector ScreenAligned(byte orientation, Vector origin, Vector currentViewOrigin)
    {
        Vector dir = currentViewOrigin - origin;
        if (orientation == 2)
            dir.Z = 0.0f;
        return DetailMath.VectorAngles(dir);
    }

    private static void ComputeAngles(ref DetailObject o, Vector currentViewOrigin)
    {
        if (o.Orientation is 1 or 2)
            o.Angles = ScreenAligned(o.Orientation, o.Origin, currentViewOrigin);
    }

    /// <summary><c>BeginTranslucentDetailRendering</c>.</summary>
    public void BeginTranslucent()
    {
        _sortedLeaf = -1;
        _sortedFastLeaf = -1;
        _spriteCount = _firstSprite = 0;
    }

    //-------------------------------------------------------------------------
    // The four draws, each split where stock reaches the render context:
    // Prepare is everything before it (and answers which material, or None);
    // Emit is everything after GetMaxToRender.
    //-------------------------------------------------------------------------

    /// <summary>A list draw: RenderTranslucentDetailObjects' half <paramref name="kind"/> over <paramref name="leaves"/>.</summary>
    public DetailDrawMaterial PrepareList(DetailDrawKind kind, in DetailView view, ReadOnlySpan<int> leaves, DetailSwitches switches, IDetailDrawSink sink)
    {
        Remember(kind, view, switches);
        if (_leaves.Length < leaves.Length)
            _leaves = new int[leaves.Length];
        leaves.CopyTo(_leaves);
        _leafCount = leaves.Length;

        if (leaves.Length == 0)
            return DetailDrawMaterial.None;

        if (kind == DetailDrawKind.FastList)
        {
            // RenderFastSprites
            int quadCount = CountFastSpritesInLeafList(leaves);
            if (quadCount == 0)
                return DetailDrawMaterial.None;
            if (switches.DrawDetailProps == 0)
                return DetailDrawMaterial.None;
            _quadCount = quadCount;
            return MaterialFor(switches);
        }

        // Count the total # of detail quads we possibly could render
        int count = CountSpritesInLeafList(leaves);
        if (count == 0)
            return DetailDrawMaterial.None;
        _quadCount = count;
        return MaterialFor(switches);
    }

    /// <summary>A leaf drawn up to an entity: <paramref name="closest"/> is its closest point, or null for the rest.</summary>
    public DetailDrawMaterial PrepareInLeaf(DetailDrawKind kind, in DetailView view, int leaf, Vector? closest, DetailSwitches switches, IDetailDrawSink sink)
    {
        Remember(kind, view, switches);
        _drawLeaf = leaf;

        float minDistance = 0.0f;
        if (closest is Vector c)
        {
            Vector delta = c - view.Origin;
            minDistance = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
        }
        _minDistance = minDistance;

        if (kind == DetailDrawKind.FastInLeaf)
        {
            FastLeaf? data = FastInLeaf(leaf);
            if (data == null)
                return DetailDrawMaterial.None;

            if (_sortedFastLeaf != leaf)
            {
                _sortedFastLeaf = leaf;
                data.NumPending = BuildOutSortedSprites(data, view);
                data.StartSpriteIndex = 0;
            }
            if (data.NumPending == 0)
                return DetailDrawMaterial.None;

            if (_fastSortInfo[data.StartSpriteIndex].Distance < minDistance)
                return DetailDrawMaterial.None;

            if (switches.DrawDetailProps == 0)
                return DetailDrawMaterial.None;
            return MaterialFor(switches);
        }

        // We may have already sorted this leaf. If not, sort the leaf.
        if (_sortedLeaf != leaf)
        {
            _sortedLeaf = leaf;
            _spriteCount = 0;
            _firstSprite = 0;

            // Count the total # of detail sprites we possibly could render
            int spriteCount = CountSpritesInLeafList([leaf]);
            if (spriteCount == 0)
                return DetailDrawMaterial.None;

            // Sort detail sprites in each leaf independently; then render them
            _spriteCount = SortSpritesBackToFront(leaf, view, _sortInfo, sink);
        }

        // No more to draw? Bye!
        if (_spriteCount == _firstSprite)
            return DetailDrawMaterial.None;

        if (_sortInfo[_firstSprite].Distance < minDistance)
            return DetailDrawMaterial.None;

        return MaterialFor(switches);
    }

    private void Remember(DetailDrawKind kind, in DetailView view, DetailSwitches switches)
    {
        _kind = kind;
        _view = view;
        _switches = switches;
    }

    private static DetailDrawMaterial MaterialFor(DetailSwitches switches)
        => switches.Wireframe || switches.DrawDetailProps == 2 ? DetailDrawMaterial.Wireframe : DetailDrawMaterial.Sprite;

    /// <summary>
    /// The prepared draw's mesh, once native has the render context and the
    /// mesh's <paramref name="maxQuads"/> (GetMaxToRender's indices / 6,
    /// capped at its vertices / 4). Native has already returned when that is 0.
    /// </summary>
    public void Emit(int maxQuads, IDetailDrawSink sink)
    {
        switch (_kind)
        {
            case DetailDrawKind.FastList: EmitFastList(maxQuads, sink); break;
            case DetailDrawKind.OldList: EmitOldList(maxQuads, sink); break;
            case DetailDrawKind.FastInLeaf: EmitFastInLeaf(maxQuads, sink); break;
            case DetailDrawKind.OldInLeaf: EmitOldInLeaf(maxQuads, sink); break;
        }
    }

    private int CountSpritesInLeafList(ReadOnlySpan<int> leaves)
    {
        int count = 0;
        foreach (int leaf in leaves)
            count += ObjectsInLeaf(leaf).Count;
        return count;
    }

    private int CountFastSpritesInLeafList(ReadOnlySpan<int> leaves)
    {
        int count = 0;
        foreach (int leaf in leaves)
        {
            if (FastInLeaf(leaf) is FastLeaf data)
                count += data.NumSprites;
        }
        return count;
    }

    //-------------------------------------------------------------------------
    // The fast sprites
    //-------------------------------------------------------------------------

    private int BuildOutSortedSprites(FastLeaf data, in DetailView view)
    {
        int simdSprites = data.NumSimd;
        int group = data.FirstGroup;
        int outIndex = 0;
        int outGroup = 0;
        int curidx = 0;
        int lastBfMask = 0;

        float maxSqDist = _curMaxSqDist;
        float falloffFactor = (float)(1.0 / (_curMaxSqDist - _curFadeSqDist));
        float startFade = _curFadeSqDist;
        Vector o = view.Origin, f = view.Forward;

        Span<float> distanceSq = stackalloc float[4];
        do
        {
            int mask = 0;
            for (int lane = 0; lane < 4; lane++)
            {
                int l = group * 4 + lane;
                float ox = _posX[l] - o.X, oy = _posY[l] - o.Y, oz = _posZ[l] - o.Z;
                float dot = ox * f.X + oy * f.Y + oz * f.Z;
                float d2 = ox * ox + oy * oy + oz * oz;
                distanceSq[lane] = d2;
                // TestSignSIMD( OrSIMD( ofsDotFwd, CmpGtSIMD( d2, maxsqdist ) ) )
                bool culled = BitConverter.SingleToInt32Bits(dot) < 0 || d2 > maxSqDist;
                if (culled)
                    mask |= 1 << lane;
            }
            lastBfMask = mask;
            if (lastBfMask != 0xf)
            {
                for (int lane = 0; lane < 4; lane++)
                {
                    int l = group * 4 + lane;
                    float ox = _posX[l] - o.X, oy = _posY[l] - o.Y;
                    // dx1 = ( -ofs.y, ofs.x, 0 ), VectorNormalizeFast
                    float dxx = -oy, dxy = ox, dxz = 0.0f;
                    float magSq = dxx * dxx + dxy * dxy + dxz * dxz;
                    float r = DetailMath.ReciprocalSqrtEstimate(magSq);
                    dxx *= r;
                    dxy *= r;
                    dxz *= r;

                    float hw = _halfWidth[l], h = _height[l];
                    float vdxX = dxx * hw, vdxY = dxy * hw, vdxZ = dxz * hw;
                    float vdyX = 0.0f * h, vdyY = 0.0f * h, vdyZ = 1.0f * h;

                    float alpha = falloffFactor * (distanceSq[lane] - startFade);
                    alpha = 1.0f - DetailMath.MinLane(DetailMath.MaxLane(alpha, 0.0f), 1.0f);
                    _bAlpha[outGroup * 4 + lane] = DetailMath.MagicByte(alpha);

                    float px = _posX[l], py = _posY[l], pz = _posZ[l];
                    px += vdxX; py += vdxY; pz += vdxZ;
                    SetCorner(outGroup, 0, lane, px, py, pz);
                    px -= vdyX; py -= vdyY; pz -= vdyZ;
                    SetCorner(outGroup, 1, lane, px, py, pz);
                    px -= vdxX; py -= vdxY; pz -= vdxZ;
                    px -= vdxX; py -= vdxY; pz -= vdxZ;
                    SetCorner(outGroup, 2, lane, px, py, pz);
                    px += vdyX; py += vdyY; pz += vdyZ;
                    SetCorner(outGroup, 3, lane, px, py, pz);

                    _bDef[outGroup * 4 + lane] = _def[l];
                    for (int k = 0; k < 4; k++)
                        _bRgb[(outGroup * 4 + lane) * 4 + k] = _rgb[l * 4 + k];

                    _fastSortInfo[outIndex + lane].Index = curidx + lane;
                    _fastSortInfo[outIndex + lane].Distance = distanceSq[lane];
                }
                curidx += 4;
                outIndex += 4;
                outGroup++;
            }
            group++;
        }
        while (--simdSprites != 0);

        // adjust count for tail
        int count = outIndex;
        if (lastBfMask != 0xf)
            count -= (0 - data.NumSprites) & 3;

        if (count != 0)
            DetailHeapSort.Sort(_fastSortInfo.AsSpan(0, count));
        return count;
    }

    private void SetCorner(int group, int corner, int lane, float x, float y, float z)
    {
        int i = (group * 4 + corner) * 4 + lane;
        _bX[i] = x;
        _bY[i] = y;
        _bZ[i] = z;
    }

    private void DrawFastQuad(int sortIndex, IDetailDrawSink sink)
    {
        int index = _fastSortInfo[sortIndex].Index;
        int group = index >> 2, lane = index & 3;
        int bl = group * 4 + lane;
        byte r = _bRgb[bl * 4], g = _bRgb[bl * 4 + 1], b = _bRgb[bl * 4 + 2], a = _bAlpha[bl];
        int def = _bDef[bl];
        DetailSpriteDict d = (def & int.MinValue) != 0 ? _spriteDictFlipped[def & int.MaxValue] : _spriteDict[def];

        int c0 = (group * 4 + 0) * 4 + lane, c1 = (group * 4 + 1) * 4 + lane;
        int c2 = (group * 4 + 2) * 4 + lane, c3 = (group * 4 + 3) * 4 + lane;
        sink.Vertex(_bX[c0], _bY[c0], _bZ[c0], r, g, b, a, d.TexLrX, d.TexLrY);
        sink.Vertex(_bX[c1], _bY[c1], _bZ[c1], r, g, b, a, d.TexLrX, d.TexUlY);
        sink.Vertex(_bX[c2], _bY[c2], _bZ[c2], r, g, b, a, d.TexUlX, d.TexUlY);
        sink.Vertex(_bX[c3], _bY[c3], _bZ[c3], r, g, b, a, d.TexUlX, d.TexLrY);
    }

    // RenderFastSprites, after GetMaxToRender.
    private void EmitFastList(int maxQuads, IDetailDrawSink sink)
    {
        int quadsToDraw = Math.Min(_quadCount, maxQuads);
        int quadsRemaining = quadsToDraw;
        sink.Begin(quadsToDraw);

        for (int i = 0; i < _leafCount; ++i)
        {
            FastLeaf? data = FastInLeaf(_leaves[i]);
            if (data == null)
                continue;

            int count = BuildOutSortedSprites(data, _view);
            int draw = 0;
            while (count != 0)
            {
                if (quadsRemaining == 0)
                {
                    sink.EndDraw();
                    quadsRemaining = quadsToDraw;
                    sink.Begin(quadsToDraw);
                }
                int toDraw = Math.Min(count, quadsRemaining);
                count -= toDraw;
                quadsRemaining -= toDraw;
                while (toDraw-- != 0)
                    DrawFastQuad(draw++, sink);
            }
        }
        sink.EndDraw();
    }

    // RenderFastTranslucentDetailObjectsInLeaf, after GetMaxToRender.
    private void EmitFastInLeaf(int maxQuads, IDetailDrawSink sink)
    {
        FastLeaf data = FastInLeaf(_drawLeaf)!;
        int count = data.NumPending;
        int quadsToDraw = Math.Min(count, maxQuads);
        int quadsRemaining = quadsToDraw;
        sink.Begin(quadsToDraw);

        int draw = data.StartSpriteIndex;
        while (count != 0 && _fastSortInfo[draw].Distance >= _minDistance)
        {
            if (quadsRemaining == 0)
            {
                sink.EndDraw();
                quadsRemaining = quadsToDraw;
                sink.Begin(quadsToDraw);
            }
            int toDraw = Math.Min(count, quadsRemaining);
            count -= toDraw;
            quadsRemaining -= toDraw;
            while (toDraw-- != 0)
                DrawFastQuad(draw++, sink);
        }
        data.NumPending = count;
        data.StartSpriteIndex = draw;

        sink.EndDraw();
    }

    //-------------------------------------------------------------------------
    // The sorted sprites
    //-------------------------------------------------------------------------

    private int SortSpritesBackToFront(int leaf, in DetailView view, DetailSortInfo[] sortInfo, IDetailDrawSink sink)
    {
        (int firstDetailObject, int detailObjectCount) = ObjectsInLeaf(leaf);

        // The FOV factor is 1 (docs/absence-client.md row 6). Stock multiplies
        // by it here and divides at BuildRenderLists: a restoration that spells
        // both sites the same way doubles the error instead of cancelling it.
        float factor = 1.0f;
        float detailDist = settings.DetailDist;
        float maxSqDist = detailDist * detailDist;
        float fadeSqDist = detailDist - settings.DetailFade;
        maxSqDist *= factor;
        fadeSqDist *= factor;
        if (fadeSqDist > 0)
            fadeSqDist *= fadeSqDist;
        else
            fadeSqDist = 0;
        float falloffFactor = 255.0f / (maxSqDist - fadeSqDist);

        int count = 0;
        int end = detailObjectCount + firstDetailObject;
        for (int j = firstDetailObject; j < end; ++j)
        {
            DetailObject model = _objects[j];
            Vector delta = model.Origin - view.Origin;
            float sqDist = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
            if (sqDist >= maxSqDist)
                continue;

            if (fadeSqDist > 0 && sqDist > fadeSqDist)
                model.Alpha = DetailMath.ToByte(falloffFactor * (maxSqDist - sqDist));
            else
                model.Alpha = 255;

            if (model.Type == (byte)DetailPropType.Model)
            {
                _objects[j] = model;
                sink.ModelAlpha(j, model.Alpha);
                continue;
            }
            if (model.Alpha == 0)
            {
                _objects[j] = model;
                continue;
            }

            // Perform screen alignment if necessary.
            ComputeAngles(ref model, view.CurrentViewOrigin);
            _objects[j] = model;

            sortInfo[count].Index = j;
            sortInfo[count].Distance = sqDist;
            ++count;
        }

        if (count != 0)
            DetailHeapSort.Sort(sortInfo.AsSpan(0, count));
        return count;
    }

    // CDetailModel::GetColorModulation.
    private Vector ColorModulation(in DetailObject model, int fullbright)
    {
        if (fullbright == 1)
            return new Vector(1.0f, 1.0f, 1.0f);

        Vector tmp = engine.ComputeDynamicLighting(model.Origin, new Vector(1, 0, 0));
        float val = engine.LightStyleValue(0);
        ColorRgbExp32 c = model.Color;
        float r = tmp.X + val * DetailMath.TexLightToLinear(c.R, c.Exponent);
        float g = tmp.Y + val * DetailMath.TexLightToLinear(c.G, c.Exponent);
        float b = tmp.Z + val * DetailMath.TexLightToLinear(c.B, c.Exponent);

        if (model.HasLightStyle)
        {
            for (int i = 0; i < model.LightStyleCount; ++i)
            {
                int at = model.LightStyle + i;
                if (at < 0 || at >= _lighting.Count)
                    continue;
                DetailLightstyle lighting = _lighting[at];
                val = engine.LightStyleValue(lighting.Style);
                if (val != 0)
                {
                    ColorRgbExp32 l = lighting.Lighting;
                    r += val * DetailMath.TexLightToLinear(l.R, l.Exponent);
                    g += val * DetailMath.TexLightToLinear(l.G, l.Exponent);
                    b += val * DetailMath.TexLightToLinear(l.B, l.Exponent);
                }
            }
        }

        return engine.LinearToGamma(new Vector(r, g, b));
    }

    // CDetailModel::DrawSprite: only the plain sprite draws in this build.
    private void DrawSprite(in DetailObject model, int fullbright, IDetailDrawSink sink)
    {
        if (model.Type != (byte)DetailPropType.Sprite)
            return;

        Vector color = ColorModulation(model, fullbright);
        byte cr = DetailMath.ToByte(color.X * 255.0f);
        byte cg = DetailMath.ToByte(color.Y * 255.0f);
        byte cb = DetailMath.ToByte(color.Z * 255.0f);
        byte ca = model.Alpha;

        DetailSpriteDict dict = model.SpriteIndex < _spriteDict.Count ? _spriteDict[model.SpriteIndex] : default;

        DetailMath.AngleVectors(model.Angles, out _, out Vector dx, out Vector dy);

        float scale = DetailMath.Float16To32(model.ScaleHalf);
        float ulX = dict.UlX * scale, ulY = dict.UlY * scale;
        float lrX = dict.LrX * scale, lrY = dict.LrY * scale;

        Vector origin = DetailMath.MA(model.Origin, ulX, dx);
        origin = DetailMath.MA(origin, ulY, dy);
        dx = dx * (lrX - ulX);
        dy = dy * (lrY - ulY);

        float texulX = dict.TexUlX, texulY = dict.TexUlY;
        float texlrX = dict.TexLrX, texlrY = dict.TexLrY;
        if (!model.Flipped)
        {
            texulX = dict.TexLrX;
            texlrX = dict.TexUlX;
        }

        sink.Vertex(origin.X, origin.Y, origin.Z, cr, cg, cb, ca, texulX, texulY);
        origin = origin + dy;
        sink.Vertex(origin.X, origin.Y, origin.Z, cr, cg, cb, ca, texulX, texlrY);
        origin = origin + dx;
        sink.Vertex(origin.X, origin.Y, origin.Z, cr, cg, cb, ca, texlrX, texlrY);
        origin = origin - dy;
        sink.Vertex(origin.X, origin.Y, origin.Z, cr, cg, cb, ca, texlrX, texulY);
    }

    // RenderTranslucentDetailObjects' own sprites, after GetMaxToRender.
    private void EmitOldList(int maxQuads, IDetailDrawSink sink)
    {
        int quadCount = _quadCount;
        int quadsToDraw = Math.Min(quadCount, maxQuads);
        sink.Begin(quadsToDraw);

        int quadsDrawn = 0;
        for (int i = 0; i < _leafCount; ++i)
        {
            int leaf = _leaves[i];

            // Sort detail sprites in each leaf independently; then render them
            int count = SortSpritesBackToFront(leaf, _view, _sortInfo, sink);

            for (int j = 0; j < count; ++j)
            {
                DetailObject model = _objects[_sortInfo[j].Index];
                int quadsInModel = QuadCount[model.Type];

                // Prevent the batches from getting too large
                if (quadsDrawn + quadsInModel > quadsToDraw)
                {
                    sink.EndDraw();
                    quadCount -= quadsDrawn;
                    quadsToDraw = Math.Min(quadCount, maxQuads);
                    sink.Begin(quadsToDraw);
                    quadsDrawn = 0;
                }

                DrawSprite(model, _switches.Fullbright, sink);
                quadsDrawn += quadsInModel;
            }
        }

        sink.EndDraw();
    }

    // RenderTranslucentDetailObjectsInLeaf's own sprites, after GetMaxToRender.
    private void EmitOldInLeaf(int maxQuads, IDetailDrawSink sink)
    {
        // needs to be * 4 since there are a max of 4 quads per detail object
        int quadCount = (_spriteCount - _firstSprite) * 4;
        int quadsToDraw = Math.Min(quadCount, maxQuads);
        sink.Begin(quadsToDraw);

        int quadsDrawn = 0;
        while (_firstSprite < _spriteCount && _sortInfo[_firstSprite].Distance >= _minDistance)
        {
            DetailObject model = _objects[_sortInfo[_firstSprite].Index];
            int quadsInModel = QuadCount[model.Type];
            if (quadsDrawn + quadsInModel > quadsToDraw)
            {
                sink.EndDraw();
                quadCount = (_spriteCount - _firstSprite) * 4;
                quadsToDraw = Math.Min(quadCount, maxQuads);
                sink.Begin(quadsToDraw);
                quadsDrawn = 0;
            }

            DrawSprite(model, _switches.Fullbright, sink);
            ++_firstSprite;
            quadsDrawn += quadsInModel;
        }

        sink.EndDraw();
    }
}
