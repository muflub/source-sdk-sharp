using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Flashlight;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed flashlight (<see cref="FlashlightEffect"/>) against stock's
/// CFlashlightEffect, run offline from the pristine flashlighteffect.cpp by
/// src/test/oracle/native/flashlight_oracle_test.cpp
/// (<c>Fixtures/flashlight-stock.tsv</c>, README beside it).
///
/// <para>
/// Each recorded frame is driven as the oracle drove it (stock's
/// C_BasePlayer::UpdateFlashlight over the logged eye vectors). Every trace
/// the port asks must be the one stock asked -- kind, endpoints, extents and
/// mask -- and is answered from the recording; every state handed to the
/// shadow manager, every projection and destroy, the texture, the overlays and
/// the old flashlight's dynamic light are compared with stock's, floats by
/// their bits.
/// </para>
///
/// <para>
/// One difference is the platform's: the state's orientation is a quaternion
/// made from the basis through the C runtime's <c>atan2f</c>, <c>sinf</c> and
/// <c>cosf</c> (MatrixAngles, then AngleQuaternion), whose last bit is the
/// recording machine's libm (glibc 2.39's <c>atan2f</c> and the Windows C
/// runtime differ from it, as DetailPropsOracleTests found). The four
/// quaternion components are held to <see cref="LibmUlps"/> units in the last
/// place; every other column exactly.
/// </para>
/// </summary>
public class FlashlightOracleTests
{
    private static float H(string hex) => BitConverter.Int32BitsToSingle(unchecked((int)uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static Vector V(string[] r, int at) => new(H(r[at]), H(r[at + 1]), H(r[at + 2]));

    private static uint Bits(float f) => unchecked((uint)BitConverter.SingleToInt32Bits(f));

    private static void Same(string expectedHex, float actual, string what)
        => Assert.True(uint.Parse(expectedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) == Bits(actual),
                       $"{what}: stock {H(expectedHex).ToString("R", CultureInfo.InvariantCulture)} ({expectedHex}), managed {actual.ToString("R", CultureInfo.InvariantCulture)} ({Bits(actual):x8})");

    /// <summary>
    /// libm's last bit, through two calls: CI's glibc 2.39 put the quaternion
    /// 1 ulp from the recording (atan2f), and 2 with atan2 rounded correctly
    /// (sinf/cosf).
    /// </summary>
    internal const int LibmUlps = 4;

    /// <summary>
    /// Within <paramref name="ulps"/> units in the last place of stock's float:
    /// the same sign, and at most that many representable floats apart.
    /// </summary>
    internal static bool WithinUlps(uint expected, float actual, int ulps)
    {
        uint got = Bits(actual);
        if ((expected ^ got) >> 31 != 0)
            return expected << 1 == 0 && got << 1 == 0;   // +0 and -0 only
        long apart = Math.Abs((long)(expected & 0x7FFFFFFF) - (got & 0x7FFFFFFF));
        return apart <= ulps;
    }

    private static void Near(string expectedHex, float actual, string what)
        => Assert.True(WithinUlps(uint.Parse(expectedHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture), actual, LibmUlps),
                       $"{what}: stock {H(expectedHex).ToString("R", CultureInfo.InvariantCulture)} ({expectedHex}), managed {actual.ToString("R", CultureInfo.InvariantCulture)} ({Bits(actual):x8}), more than {LibmUlps} ulps apart");

    [Fact]
    public void TheLibmBoundAdmitsItsUlpsAndNoMore()
    {
        uint stock = 0xbc5a6524;                                     // C quat.y, -0.013329778
        Assert.True(WithinUlps(stock, BitConverter.Int32BitsToSingle(unchecked((int)0xbc5a6525)), LibmUlps));   // CI's glibc 2.39
        Assert.True(WithinUlps(stock, BitConverter.Int32BitsToSingle(unchecked((int)(stock + LibmUlps))), LibmUlps));
        Assert.False(WithinUlps(stock, BitConverter.Int32BitsToSingle(unchecked((int)(stock + LibmUlps + 1))), LibmUlps));
        Assert.False(WithinUlps(stock, -BitConverter.Int32BitsToSingle(unchecked((int)stock)), LibmUlps));      // the sign is exact
        Assert.True(WithinUlps(0x00000000, -0f, LibmUlps));
    }

    private static void SameV(string[] r, int at, Vector v, string what)
    {
        Same(r[at], v.X, what + ".x");
        Same(r[at + 1], v.Y, what + ".y");
        Same(r[at + 2], v.Z, what + ".z");
    }

    /// <summary>The stock mask a managed <see cref="TraceMask"/> asks for, as the executor maps it.</summary>
    private static uint StockMask(TraceMask mask) => mask switch
    {
        TraceMask.Solid => 0x0200400bu,                  // MASK_SOLID
        TraceMask.OpaqueAndNpcsWindow => 0x02004083u,    // MASK_OPAQUE_AND_NPCS | CONTENTS_WINDOW
        TraceMask.All => 0xffffffffu,                    // MASK_ALL
        _ => throw new InvalidOperationException("the flashlight asked an unexpected mask: " + mask),
    };

    /// <summary>The ConVars, by name, as the recording's CV rows left them.</summary>
    private sealed class Settings : IFlashlightSettings
    {
        public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);

        private float F(string n) => VariantParse.Atof(Values[n]);
        private int Int(string n) => (int)F(n);
        private bool B(string n) => Int(n) != 0;

        public bool NewFlashlight => B("r_newflashlight");
        public void DisableNewFlashlight() => Values["r_newflashlight"] = "0";
        public bool SwingFlashlight => B("r_swingflashlight");
        public bool LockPosition => B("r_flashlightlockposition");
        public float Fov => F("r_flashlightfov");
        public float OffsetY => F("r_flashlightoffsety");
        public float Near => F("r_flashlightnear");
        public float Far => F("r_flashlightfar");
        public float ConstantAtten => F("r_flashlightconstant");
        public float LinearAtten => F("r_flashlightlinear");
        public float QuadraticAtten => F("r_flashlightquadratic");
        public bool VisualizeTrace => B("r_flashlightvisualizetrace");
        public float Ambient => F("r_flashlightambient");
        public float ShadowAtten => F("r_flashlightshadowatten");
        public float LadderDist => F("r_flashlightladderdist");
        public float SlopeScaleDepthBias => F("mat_slopescaledepthbias_shadowmap");
        public float DepthBias => F("mat_depthbias_shadowmap");
        public bool DepthTexture => B("r_flashlightdepthtexture");
        public int DepthResolution => Int("r_flashlightdepthres");
    }

    /// <summary>The recording's world: each call the port makes is held to the next row stock wrote.</summary>
    private sealed class Replay(Queue<string[]> rows) : IFlashlightWorld
    {
        public int DX = 95;
        public bool Border = true;
        public float CurTime;
        public int NextHandle;
        public readonly List<string> Textures = [];
        public readonly Dictionary<int, float> Die = new();
        public int Checked;

        private string[] Next(string tag)
        {
            // The rows the port has no call for: the filter's consultations,
            // stock's dynamic-light allocations (the port rewrites the keyed
            // light each frame) and the harness's world edits.
            while (rows.Count > 0 && rows.Peek()[0] is "F" or "A" or "WB" or "WC")
                rows.Dequeue();
            Assert.True(rows.Count > 0, $"the port made a {tag} call after the recording ended");
            string[] r = rows.Dequeue();
            Assert.True(r[0] == tag, $"the port made a {tag} call where stock wrote: {string.Join(' ', r)}");
            Checked++;
            return r;
        }

        public int? DXSupportLevel() => DX;

        public bool SupportsBorderColor() => Border;

        public int Texture(string name)
        {
            string[] r = Next("X");
            Assert.Equal(r[1], name);
            Assert.Equal("Other textures", r[2]);
            int at = Textures.IndexOf(name);
            if (at >= 0)
                return at;
            Textures.Add(name);
            return Textures.Count - 1;
        }

        private void State(string tag, string[] r, int handle, in FlashlightState s)
        {
            Assert.Equal(I(r[1]), handle);
            Same(r[2], s.OriginX, tag + " origin.x");
            Same(r[3], s.OriginY, tag + " origin.y");
            Same(r[4], s.OriginZ, tag + " origin.z");
            Near(r[5], s.OrientationX, tag + " quat.x");
            Near(r[6], s.OrientationY, tag + " quat.y");
            Near(r[7], s.OrientationZ, tag + " quat.z");
            Near(r[8], s.OrientationW, tag + " quat.w");
            Same(r[9], s.NearZ, tag + " near");
            Same(r[10], s.FarZ, tag + " far");
            Same(r[11], s.HorizontalFovDegrees, tag + " hfov");
            Same(r[12], s.VerticalFovDegrees, tag + " vfov");
            Same(r[13], s.QuadraticAtten, tag + " quadratic");
            Same(r[14], s.LinearAtten, tag + " linear");
            Same(r[15], s.ConstantAtten, tag + " constant");
            Same(r[16], s.ColorR, tag + " r");
            Same(r[17], s.ColorG, tag + " g");
            Same(r[18], s.ColorB, tag + " b");
            Same(r[19], s.ColorA, tag + " ambient");
            Assert.True(s.Texture >= 0 && s.Texture < Textures.Count, tag + ": the state names no texture");
            Assert.Equal(r[20], Textures[s.Texture]);
            Assert.Equal(I(r[21]), s.TextureFrame);
            Assert.Equal(I(r[22]), s.EnableShadows);
            Same(r[24], s.ShadowMapResolution, tag + " shadow map resolution");
            Same(r[26], s.ShadowSlopeScaleDepthBias, tag + " slope-scale depth bias");
            Same(r[27], s.ShadowDepthBias, tag + " depth bias");
            Same(r[29], s.ShadowAtten, tag + " shadow atten");

            // What the wire does not carry: the executor's default-constructed
            // FlashlightState_t keeps them, and stock's are those defaults.
            Assert.Equal("0", r[23]);                  // m_bDrawShadowFrustum
            Same(r[25], 3.0f, tag + " filter size");   // m_flShadowFilterSize
            Same(r[28], 0.0f, tag + " jitter seed");   // m_flShadowJitterSeed
            Assert.Equal("0", r[30]);                  // m_nShadowQuality
        }

        public int Create(in FlashlightState state)
        {
            string[] r = Next("C");
            int handle = NextHandle++;
            State("C", r, handle, state);
            return handle;
        }

        public void Update(int handle, in FlashlightState state) => State("W", Next("W"), handle, state);

        public void Project(int handle)
        {
            string[] r = Next("P");
            Assert.Equal(I(r[1]), handle);
            Assert.Equal("1", r[2]);
        }

        public void Destroy(int handle) => Assert.Equal(I(Next("D")[1]), handle);

        private FlashlightTrace Trace(string kind, Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask)
        {
            string[] r = Next("T");
            Assert.Equal(kind, r[1]);
            SameV(r, 2, start, kind + " start");
            SameV(r, 5, end, kind + " end");
            SameV(r, 8, mins, kind + " mins");
            SameV(r, 11, maxs, kind + " maxs");
            Assert.Equal(uint.Parse(r[14], NumberStyles.HexNumber, CultureInfo.InvariantCulture), StockMask(mask));
            float fraction = H(r[15]);
            bool hit = fraction < 1.0f || r[19] == "1" || r[20] == "1";
            return new FlashlightTrace(hit, fraction, V(r, 16));
        }

        public FlashlightTrace TraceHull(Vector start, Vector end, Vector mins, Vector maxs, TraceMask mask)
            => Trace("hull", start, end, mins, maxs, mask);

        public FlashlightTrace TraceLine(Vector start, Vector end, TraceMask mask)
            => Trace("line", start, end, Vector.Zero, Vector.Zero, mask);

        public void BoxOverlay(Vector origin, Vector mins, Vector maxs, QAngle orientation, int r, int g, int b, int a,
                               float duration)
        {
            string[] row = Next("BX");
            SameV(row, 1, origin, "box origin");
            SameV(row, 4, mins, "box mins");
            SameV(row, 7, maxs, "box maxs");
            SameV(row, 10, orientation.AsVector(), "box angles");
            Assert.Equal((I(row[13]), I(row[14]), I(row[15]), I(row[16])), (r, g, b, a));
            Same(row[17], duration, "box duration");
        }

        public void LineOverlay(Vector origin, Vector dest, int r, int g, int b, bool noDepthTest, float duration)
        {
            string[] row = Next("LN");
            SameV(row, 1, origin, "line origin");
            SameV(row, 4, dest, "line dest");
            Assert.Equal((I(row[7]), I(row[8]), I(row[9]), I(row[10])), (r, g, b, noDepthTest ? 1 : 0));
            Same(row[11], duration, "line duration");
        }

        public void DynamicLightUpdate(in DynamicLight light)
        {
            // Stock's TouchLight row: the light as stock left it, which the
            // engine's pool would hold after the port's keyed rewrite.
            string[] r = Next("L");
            Assert.Equal(I(r[1]), light.Key);
            Assert.Equal(I(r[2]), light.Flags);
            Same(r[3], light.OriginX, "dlight origin.x");
            Same(r[4], light.OriginY, "dlight origin.y");
            Same(r[5], light.OriginZ, "dlight origin.z");
            Same(r[6], light.Radius, "dlight radius");
            Assert.Equal((I(r[7]), I(r[8]), I(r[9]), I(r[10])), (light.Red, light.Green, light.Blue, light.Exponent));
            float die = CurTime + light.LiveFor;
            Same(r[11], die, "dlight die");
            Same(r[12], light.Decay, "dlight decay");
            Assert.Equal("00000000", r[13]);           // minlight, cleared by the pool
            Assert.Equal(I(r[14]), light.Style);
            Same(r[15], light.DirectionX, "dlight dir.x");
            Same(r[16], light.DirectionY, "dlight dir.y");
            Same(r[17], light.DirectionZ, "dlight dir.z");
            Same(r[18], light.InnerAngle, "dlight inner");
            Same(r[19], light.OuterAngle, "dlight outer");
            Assert.Equal(0, light.IsElight);
            Die[light.Key] = die;
        }

        public void DynamicLightRelease(int key) => Die[key] = CurTime;
    }

    private static List<string[]> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "flashlight-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock flashlight is missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).ToList();
        Assert.True(rows.Count(r => r[0] == "U") > 60 && rows.Count(r => r[0] == "T") > 100
                    && rows.Count(r => r[0] == "W") > 30 && rows[^1][0] == "END", "the oracle parsed");
        return rows;
    }

    /// <summary>The whole recording, replayed; returns how many calls were held to a row.</summary>
    private static int ReplayAll()
    {
        var queue = new Queue<string[]>(Stock());
        var settings = new Settings();
        var world = new Replay(queue);
        FlashlightEffect? effect = null;
        int frames = 0, scenarios = 0;

        while (queue.Count > 0)
        {
            string[] r = queue.Dequeue();
            switch (r[0])
            {
                case "S":
                    scenarios++;
                    world.DX = I(r[2]);
                    world.Border = r[3] == "1";
                    break;
                case "V":
                    settings.Values[r[1]] = r[2];
                    break;
                case "CV":
                    settings.Values[r[1]] = r[2];
                    break;
                case "U":
                    frames++;
                    world.CurTime = H(r[2]);
                    bool dim = r[3] == "1";
                    bool ladder = I(r[4]) == 9;   // MOVETYPE_LADDER
                    if (dim)
                    {
                        if (effect is null)
                        {
                            effect = new FlashlightEffect(1, world, settings);
                            Assert.Equal("NEW", queue.Dequeue()[0]);
                            effect.TurnOn();
                        }
                        effect.UpdateLight(V(r, 5), V(r, 8), V(r, 11), V(r, 14), LocalPlayerFlashlight.FlashlightDistance, ladder);
                    }
                    else if (effect is not null)
                    {
                        Assert.Equal("DEL", queue.Dequeue()[0]);
                        effect.Release();
                        effect = null;
                    }
                    break;
                case "OFF":
                    effect!.TurnOff();
                    break;
                case "ON":
                    effect!.TurnOn();
                    break;
                case "Q":
                    // A keyed light in stock's pool: its die is what the port's
                    // keyed writes left (CurTime + 0.2 lit, CurTime put out).
                    int key = I(r[2]);
                    Assert.True(world.Die.TryGetValue(key, out float die), $"stock's pool holds light {key} and the port never lit it");
                    Same(r[3], die, $"light {key} die");
                    world.Checked++;
                    break;
                case "WB": case "WC": case "F": case "A": case "END":
                    break;
                default:
                    Assert.Fail("a row the port did not account for: " + string.Join(' ', r));
                    break;
            }
        }

        Assert.Equal(3, scenarios);   // new, dx6, dx9-noborder
        Assert.True(frames > 60, $"only {frames} frames replayed");
        return world.Checked;
    }

    [Fact]
    public void TheManagedFlashlightMakesStocksEveryCall()
    {
        int held = ReplayAll();

        // Every row of stock's calls was consumed by one of the port's: the
        // traces, states, projections, destroys, textures, lights, overlays
        // and the pool's lights after each frame.
        string[] calls = ["T", "C", "W", "P", "D", "X", "L", "BX", "LN", "Q"];
        int recorded = Stock().Count(r => calls.Contains(r[0]));
        Assert.True(recorded > 200, $"the recording holds only {recorded} calls");
        Assert.Equal(recorded, held);
    }
}
