//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>C_FuncSmokeVolume</c>'s simulation (c_func_smokevolume.cpp, carried in
/// shell_smokevolume_sim.cpp until lane c2-sims): a grid of smoke puffs filling
/// the brush, each cell's light and wall fade sampled once, neighbours trading
/// places along a cosine, the whole volume's density ramping towards its
/// target, every puff spinning. Replayed bit for bit against
/// <c>Fixtures/smokevolume-stock.tsv</c>.
/// </summary>
public sealed class SmokeVolumeSim
{
    /// <summary>What the world answers about one point: stock's three queries per cell.</summary>
    public interface IWorld
    {
        /// <summary><c>enginetrace-&gt;GetPointContents</c>.</summary>
        int PointContents(Vector position);

        /// <summary><c>engine-&gt;GetLightForPoint(position, true)</c>.</summary>
        Vector Light(Vector position);

        /// <summary>
        /// <c>UTIL_TraceLine</c> against <c>MASK_SOLID_BRUSHONLY</c>: the fraction,
        /// and the hit plane's normal and distance.
        /// </summary>
        (float Fraction, Vector Normal, float PlaneDist) Trace(Vector start, Vector end);
    }

    /// <summary><c>CONTENTS_SOLID</c>.</summary>
    public const int ContentsSolid = 0x1;

    /// <summary><c>SF_EMISSIVE</c>.</summary>
    public const int SpawnFlagEmissive = 0x1;

    /// <summary>Stock's <c>s_FadePlaneDirections</c>: +x, -x, +y, -y, +z, -z (computed, not a static table).</summary>
    private static Vector FadePlaneDirection(int i) => i switch
    {
        0 => new Vector(1, 0, 0),
        1 => new Vector(-1, 0, 0),
        2 => new Vector(0, 1, 0),
        3 => new Vector(0, -1, 0),
        4 => new Vector(0, 0, 1),
        _ => new Vector(0, 0, -1),
    };

    private const int FadePlaneCount = 6;

    /// <summary>One puff: <c>SmokeGrenadeParticle</c> plus the executor's id.</summary>
    public struct Puff
    {
        public int Id;
        public Vector Position;
        public float RotationFactor;
        public float CurRotation;
        public float FadeAlpha;
        public byte ColorInterp;
        public byte R, G, B;
    }

    /// <summary>One cell: <c>SmokeParticleInfo</c>, with the puff it holds as an index into the puffs (-1 none).</summary>
    public struct Cell
    {
        public int Puff;
        public int TradeIndex;
        public float TradeClock;
        public float TradeDuration;
        public float FadeAlpha;
        public byte R, G, B;
    }

    // What the entity networks.
    public byte Red1 = 255, Green1 = 255, Blue1 = 255, Red2 = 255, Green2 = 255, Blue2 = 255;
    public float ParticleDrawWidth;
    public float ParticleSpacingDistance;
    public float DensityRampSpeed;
    public float RotationSpeed;
    public float MovementSpeed;
    public float Density = 1;
    public int SpawnFlags;

    /// <summary>The brush's bounds (stock's OBB mins/maxs: model space, not moved by the origin).</summary>
    public Vector VolumeMins, VolumeMaxs;

    public Vector MinColor = new(1, 1, 1), MaxColor = new(1, 1, 1);
    public float ParticleRadius;
    public float SpacingRadius;
    public float CurrentDensity;
    public int XCount, YCount, ZCount;

    private Cell[] _cells = [];
    private readonly List<Puff> _puffs = new();
    private int _nextId = 1;
    private Vector _lastOrigin;
    private Vector _lastAngles;
    private bool _firstUpdate = true;

    public IReadOnlyList<Puff> Puffs => _puffs;

    public ReadOnlySpan<Cell> Cells => _cells;

    private bool IsEmissive => (SpawnFlags & SpawnFlagEmissive) != 0;

    /// <summary>
    /// Stock's <c>ApplyUpdate</c>: the colours, the reduce-particles scaling
    /// (applied on every update to the freshly networked density and spacing),
    /// the radii; on the first update, the grid and its fill.
    /// </summary>
    public void ApplyUpdate(bool created, bool reduceParticles, IWorld world, IParticleRandom random)
    {
        MinColor = new Vector((1.0f / 255.0f) * Red1, (1.0f / 255.0f) * Green1, (1.0f / 255.0f) * Blue1);
        MaxColor = new Vector((1.0f / 255.0f) * Red2, (1.0f / 255.0f) * Green2, (1.0f / 255.0f) * Blue2);

        if (reduceParticles)
        {
            Density *= 0.5f;
            ParticleSpacingDistance *= 1.5f;
        }

        ParticleRadius = ParticleDrawWidth * 0.5f;
        SpacingRadius = ParticleSpacingDistance * 0.5f;

        if (created)
        {
            Vector size = VolumeMaxs - VolumeMins;
            XCount = (int)(0.5f + (size.X / (SpacingRadius * 2.0f)));
            YCount = (int)(0.5f + (size.Y / (SpacingRadius * 2.0f)));
            ZCount = (int)(0.5f + (size.Z / (SpacingRadius * 2.0f)));
            CurrentDensity = Density;
            _cells = new Cell[Math.Max(0, XCount * YCount * ZCount)];
            _puffs.Clear();
            FillVolume(world, random);
        }
    }

    private int CellIndex(int x, int y, int z) => z * XCount * YCount + y * XCount + x;

    private void CellXyz(int index, out int x, out int y, out int z)
    {
        z = index / (XCount * YCount);
        int zIndex = z * XCount * YCount;
        y = (index - zIndex) / XCount;
        int yIndex = y * XCount;
        x = index - zIndex - yIndex;
    }

    private bool Valid(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < XCount && y < YCount && z < ZCount;

    /// <summary><c>GetSmokeParticlePos</c>: the cell's centre, from the volume's mins.</summary>
    public Vector CellPosition(int x, int y, int z)
        => VolumeMins + new Vector(x * SpacingRadius * 2 + SpacingRadius,
                                   y * SpacingRadius * 2 + SpacingRadius,
                                   z * SpacingRadius * 2 + SpacingRadius);

    private Vector CellPosition(int index)
    {
        CellXyz(index, out int x, out int y, out int z);
        return CellPosition(x, y, z);
    }

    /// <summary>
    /// mathlib's <c>LinearToTexture</c> under client.so's <c>MathLib_Init(2.2,
    /// 2.2, 0, 2)</c>: the 1024-entry table <c>pow(i / 1023.0, 1.0 / texGamma) *
    /// 255</c>, computed per call rather than held.
    /// </summary>
    public static int LinearToTexture(float f)
    {
        int i = (int)(f * 1023);
        if (i < 0)
            i = 0;
        if (i > 1023)
            i = 1023;
        return (int)(Math.Pow(i / 1023.0, 1.0 / 2.2f) * 255);
    }

    /// <summary>
    /// Stock's <c>FillVolume</c>: every cell not in solid gets a puff (colour
    /// interpolant, spin factor, start spin), its light, and a fade for each of
    /// six walls within 100 units closer than the puff's radius.
    /// </summary>
    private void FillVolume(IWorld world, IParticleRandom random)
    {
        for (int x = 0; x < XCount; x++)
        {
            for (int y = 0; y < YCount; y++)
            {
                for (int z = 0; z < ZCount; z++)
                {
                    Vector pos = CellPosition(x, y, z);
                    ref Cell cell = ref _cells[CellIndex(x, y, z)];

                    if ((world.PointContents(pos) & ContentsSolid) != 0)
                    {
                        cell.Puff = -1;
                        continue;
                    }

                    var puff = new Puff { Id = _nextId++, Position = pos };
                    puff.ColorInterp = (byte)((random.Rand() * 255) / ParticleRandomExtensions.ValveRandMax);
                    puff.RotationFactor = random.FRand(-1.0f, 1.0f);
                    puff.CurRotation = random.FRand(-RotationSpeed, RotationSpeed);

                    Vector light = world.Light(pos);
                    cell.R = (byte)LinearToTexture(light.X);
                    cell.G = (byte)LinearToTexture(light.Y);
                    cell.B = (byte)LinearToTexture(light.Z);

                    cell.FadeAlpha = 1;
                    for (int plane = 0; plane < FadePlaneCount; plane++)
                    {
                        var hit = world.Trace(pos, pos + FadePlaneDirection(plane) * 100);
                        if (hit.Fraction < 1.0f)
                        {
                            float dist = hit.Normal.Dot(pos) - hit.PlaneDist;
                            if (dist < 0)
                            {
                                cell.FadeAlpha = 0;
                            }
                            else if (dist < ParticleRadius)
                            {
                                float alphaScale = dist / ParticleRadius;
                                alphaScale *= alphaScale * alphaScale;
                                cell.FadeAlpha *= alphaScale;
                            }
                        }
                    }

                    cell.Puff = _puffs.Count;
                    cell.TradeIndex = -1;
                    _puffs.Add(puff);
                }
            }
        }
    }

    /// <summary>
    /// Stock's <c>Update</c>: the bounds when the volume moved (or on the first
    /// update), the density ramp, and the trades: an idle cell takes its own
    /// light and fade and looks for an idle neighbour (in an order three rand()s
    /// rotate); a trading pair slides along a cosine and swaps when done.
    /// </summary>
    /// <param name="bounds">The bounds stock hands <c>SetBBox</c>, when it does.</param>
    public void Update(float dt, Vector origin, Vector angles, IParticleRandom random, out (Vector Min, Vector Max)? bounds)
    {
        bounds = null;
        if (!ParticleMath.AreEqual(origin, _lastOrigin, 0.1f)
            || MathF.Abs(angles.X - _lastAngles.X) > 0.1
            || MathF.Abs(angles.Y - _lastAngles.Y) > 0.1
            || MathF.Abs(angles.Z - _lastAngles.Z) > 0.1
            || _firstUpdate)
        {
            _firstUpdate = false;
            _lastAngles = angles;
            _lastOrigin = origin;
            var r = new Vector(ParticleRadius, ParticleRadius, ParticleRadius);
            bounds = (VolumeMins + origin - r, VolumeMaxs + origin + r);
        }

        if (CurrentDensity < Density)
        {
            CurrentDensity += DensityRampSpeed * dt;
            if (CurrentDensity > Density)
                CurrentDensity = Density;
        }
        else if (CurrentDensity > Density)
        {
            CurrentDensity -= DensityRampSpeed * dt;
            if (CurrentDensity < Density)
                CurrentDensity = Density;
        }

        if (CurrentDensity == 0.0f)
            return;

        int[] offsetLookup = [-1, 0, 1];
        float tradeDurationMax = ParticleSpacingDistance / (MovementSpeed + 0.1f);
        float tradeDurationMin = tradeDurationMax * 0.5f;
        if (float.IsNaN(tradeDurationMax) || float.IsNaN(tradeDurationMin))
            return;

        for (int i = 0; i < _cells.Length; i++)
        {
            ref Cell info = ref _cells[i];
            if (info.Puff < 0)
                continue;

            if (info.TradeIndex == -1)
            {
                Puff p = _puffs[info.Puff];
                p.FadeAlpha = info.FadeAlpha;
                p.R = info.R; p.G = info.G; p.B = info.B;
                _puffs[info.Puff] = p;

                CellXyz(i, out int x, out int y, out int z);
                int xOff = random.Rand();
                int yOff = random.Rand();
                int zOff = random.Rand();

                bool found = false;
                for (int xc = 0; xc < 3 && !found; xc++)
                {
                    for (int yc = 0; yc < 3 && !found; yc++)
                    {
                        for (int zc = 0; zc < 3; zc++)
                        {
                            int tx = x + offsetLookup[(xc + xOff) % 3];
                            int ty = y + offsetLookup[(yc + yOff) % 3];
                            int tz = z + offsetLookup[(zc + zOff) % 3];
                            if (tx == x && ty == y && tz == z)
                                continue;

                            if (Valid(tx, ty, tz))
                            {
                                int o = CellIndex(tx, ty, tz);
                                ref Cell other = ref _cells[o];
                                if (other.Puff >= 0 && other.TradeIndex == -1)
                                {
                                    info.TradeIndex = o;
                                    other.TradeIndex = i;
                                    info.TradeClock = other.TradeClock = 0;
                                    other.TradeDuration = info.TradeDuration = random.FRand(tradeDurationMin, tradeDurationMax);
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                int o = info.TradeIndex;
                ref Cell other = ref _cells[o];
                if (i < o)
                {
                    other.TradeClock += dt;
                    info.TradeClock = other.TradeClock;

                    Vector myPos = CellPosition(i);
                    Vector otherPos = CellPosition(o);

                    Puff mine = _puffs[info.Puff];
                    Puff theirs = _puffs[other.Puff];

                    if (info.TradeClock >= info.TradeDuration)
                    {
                        info.TradeIndex = other.TradeIndex = -1;
                        mine.Position = otherPos;
                        theirs.Position = myPos;
                        _puffs[info.Puff] = mine;
                        _puffs[other.Puff] = theirs;
                        (info.Puff, other.Puff) = (other.Puff, info.Puff);
                    }
                    else
                    {
                        float percent = MathF.Cos(info.TradeClock * 2 * 1.57079632f / info.TradeDuration);
                        percent = (float)(percent * 0.5 + 0.5);

                        mine.FadeAlpha = info.FadeAlpha + (other.FadeAlpha - info.FadeAlpha) * (1 - percent);
                        theirs.FadeAlpha = info.FadeAlpha + (other.FadeAlpha - info.FadeAlpha) * percent;

                        mine.R = Interp(info.R, other.R, 1 - percent);
                        mine.G = Interp(info.G, other.G, 1 - percent);
                        mine.B = Interp(info.B, other.B, 1 - percent);
                        theirs.R = Interp(info.R, other.R, percent);
                        theirs.G = Interp(info.G, other.G, percent);
                        theirs.B = Interp(info.B, other.B, percent);

                        mine.Position = myPos + (otherPos - myPos) * (1 - percent);
                        theirs.Position = myPos + (otherPos - myPos) * percent;

                        _puffs[info.Puff] = mine;
                        _puffs[other.Puff] = theirs;
                    }
                }
            }
        }
    }

    /// <summary><c>InterpColor</c>'s one channel: <c>(unsigned char)(a + (b - a) * t)</c>.</summary>
    private static byte Interp(byte a, byte b, float t) => (byte)(a + (b - a) * t);

    /// <summary>
    /// Stock's <c>SimulateParticles</c>: every puff spins; the rate is taken in
    /// double (<c>M_PI / 180.0f</c> is a double) and added to the float spin.
    /// </summary>
    public bool Simulate(float dt)
    {
        if (CurrentDensity == 0)
            return false;

        for (int i = 0; i < _puffs.Count; i++)
        {
            Puff p = _puffs[i];
            p.CurRotation = (float)(p.CurRotation + p.RotationFactor * (Math.PI / 180.0f) * RotationSpeed * dt);
            _puffs[i] = p;
        }

        return true;
    }

    /// <summary>
    /// Stock's <c>RenderParticles</c> for one puff, or null when stock skips it
    /// (reducing particles and too faint). The density and the cell's fade make
    /// the alpha; the colour interpolates and is lit (added when emissive,
    /// multiplied when not); the executor applies the 10..30 unit distance fade
    /// and stock's fixed sort key 1.
    /// </summary>
    public ParticleQuad? Draw(in Puff p, bool reduceParticles)
    {
        if (CurrentDensity == 0)
            return null;

        float alpha = CurrentDensity;
        alpha *= p.FadeAlpha;

        Vector color = MinColor + (MaxColor - MinColor) * (p.ColorInterp / 255.1f);
        if (IsEmissive)
        {
            color = new Vector(color.X + p.R / 255.0f, color.Y + p.G / 255.0f, color.Z + p.B / 255.0f);
            color = new Vector(Math.Clamp(color.X, 0.0f, 1.0f), Math.Clamp(color.Y, 0.0f, 1.0f), Math.Clamp(color.Z, 0.0f, 1.0f));
        }
        else
        {
            color = new Vector(color.X * (p.R / 255.0f), color.Y * (p.G / 255.0f), color.Z * (p.B / 255.0f));
        }

        if (!(alpha > 0.05f || !reduceParticles))
            return null;

        return new ParticleQuad
        {
            Position = p.Position, Color = color, Alpha = alpha, Size = ParticleRadius, Roll = p.CurRotation,
            Kind = ParticleQuadKind.ColorSizeAngle, FadeNear = 10, FadeFar = 30, FixedSortKey = true, SortKey = 1,
        };
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Red1); w.Write(Green1); w.Write(Blue1); w.Write(Red2); w.Write(Green2); w.Write(Blue2);
        w.Write(ParticleDrawWidth); w.Write(ParticleSpacingDistance); w.Write(DensityRampSpeed);
        w.Write(RotationSpeed); w.Write(MovementSpeed); w.Write(Density); w.Write(SpawnFlags);
        WriteVector(w, VolumeMins); WriteVector(w, VolumeMaxs); WriteVector(w, MinColor); WriteVector(w, MaxColor);
        w.Write(ParticleRadius); w.Write(SpacingRadius); w.Write(CurrentDensity);
        w.Write(XCount); w.Write(YCount); w.Write(ZCount);
        WriteVector(w, _lastOrigin); WriteVector(w, _lastAngles); w.Write(_firstUpdate); w.Write(_nextId);
        w.Write(_cells.Length);
        foreach (var c in _cells)
        {
            w.Write(c.Puff); w.Write(c.TradeIndex); w.Write(c.TradeClock); w.Write(c.TradeDuration);
            w.Write(c.FadeAlpha); w.Write(c.R); w.Write(c.G); w.Write(c.B);
        }
        w.Write(_puffs.Count);
        foreach (var p in _puffs)
        {
            w.Write(p.Id); WriteVector(w, p.Position); w.Write(p.RotationFactor); w.Write(p.CurRotation);
            w.Write(p.FadeAlpha); w.Write(p.ColorInterp); w.Write(p.R); w.Write(p.G); w.Write(p.B);
        }
    }

    public void Restore(BinaryReader r)
    {
        Red1 = r.ReadByte(); Green1 = r.ReadByte(); Blue1 = r.ReadByte(); Red2 = r.ReadByte(); Green2 = r.ReadByte(); Blue2 = r.ReadByte();
        ParticleDrawWidth = r.ReadSingle(); ParticleSpacingDistance = r.ReadSingle(); DensityRampSpeed = r.ReadSingle();
        RotationSpeed = r.ReadSingle(); MovementSpeed = r.ReadSingle(); Density = r.ReadSingle(); SpawnFlags = r.ReadInt32();
        VolumeMins = ReadVector(r); VolumeMaxs = ReadVector(r); MinColor = ReadVector(r); MaxColor = ReadVector(r);
        ParticleRadius = r.ReadSingle(); SpacingRadius = r.ReadSingle(); CurrentDensity = r.ReadSingle();
        XCount = r.ReadInt32(); YCount = r.ReadInt32(); ZCount = r.ReadInt32();
        _lastOrigin = ReadVector(r); _lastAngles = ReadVector(r); _firstUpdate = r.ReadBoolean(); _nextId = r.ReadInt32();
        _cells = new Cell[r.ReadInt32()];
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new Cell
            {
                Puff = r.ReadInt32(), TradeIndex = r.ReadInt32(), TradeClock = r.ReadSingle(), TradeDuration = r.ReadSingle(),
                FadeAlpha = r.ReadSingle(), R = r.ReadByte(), G = r.ReadByte(), B = r.ReadByte(),
            };
        }
        int count = r.ReadInt32();
        _puffs.Clear();
        for (int i = 0; i < count; i++)
        {
            _puffs.Add(new Puff
            {
                Id = r.ReadInt32(), Position = ReadVector(r), RotationFactor = r.ReadSingle(), CurRotation = r.ReadSingle(),
                FadeAlpha = r.ReadSingle(), ColorInterp = r.ReadByte(), R = r.ReadByte(), G = r.ReadByte(), B = r.ReadByte(),
            });
        }
    }

    private static void WriteVector(BinaryWriter w, Vector v)
    {
        w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
    }

    private static Vector ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
