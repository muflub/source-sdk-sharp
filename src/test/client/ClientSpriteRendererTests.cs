using System.Globalization;
using System.IO.Compression;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The sprite renderer's decisions against stock's, recorded:
/// <c>Fixtures/sprite-stock.tsv.gz</c> (README beside it) holds every
/// <c>C_SpriteRenderer::DrawSprite</c> of a dm_lockdown and ss_sandbox session
/// with what it read, what the engine answered to its glow queries, and what it
/// did (blend, colour, quad), plus a battery of <c>GetSpriteAxes</c> questions.
/// The managed planner, asked the same, must answer the same, bit for bit.
/// </summary>
public class ClientSpriteRendererTests
{
    // One recorded row: its tag and its "key=v v v" groups.
    private sealed class Row
    {
        public string Tag = "";
        public readonly Dictionary<string, string[]> Groups = new();
        public string[] Bare = [];

        public float F(string key, int i = 0) => float.Parse(Groups[key][i], CultureInfo.InvariantCulture);
        public int I(string key, int i = 0) => int.Parse(Groups[key][i], CultureInfo.InvariantCulture);
        public Vector V(string key) => new(F(key, 0), F(key, 1), F(key, 2));
        public bool Has(string key) => Groups.ContainsKey(key);
    }

    private static Row Parse(string line)
    {
        string[] tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var row = new Row { Tag = tok[0] };
        string? key = null;
        var bare = new List<string>();
        var vals = new List<string>();
        for (int i = 1; i < tok.Length; i++)
        {
            int eq = tok[i].IndexOf('=');
            if (eq > 0)
            {
                if (key is not null)
                    row.Groups[key] = vals.ToArray();
                key = tok[i][..eq];
                vals = [tok[i][(eq + 1)..]];
            }
            else if (key is null)
                bare.Add(tok[i]);
            else
                vals.Add(tok[i]);
        }
        if (key is not null)
            row.Groups[key] = vals.ToArray();
        row.Bare = bare.ToArray();
        return row;
    }

    private static List<Row> Recorded()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "sprite-stock.tsv.gz");
        Assert.True(File.Exists(path), "the recorded sprite draws are missing: " + path);
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var rows = new List<Row>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
                rows.Add(Parse(line));
        }
        return rows;
    }

    // One DrawSprite: the rows from its spc (when a managed sprite) to its spr-ret.
    private sealed record Draw(Row? Core, List<Row> Rows);

    private static List<Draw> Draws(List<Row> rows)
    {
        var draws = new List<Draw>();
        Row? core = null;
        List<Row>? open = null;
        foreach (Row r in rows)
        {
            switch (r.Tag)
            {
                case "spc":
                    Assert.True(open is null, "a managed sprite's draw began inside another");
                    core = r;
                    break;
                case "spr-skip":
                    Assert.True(open is null, "a skip inside a draw");
                    draws.Add(new Draw(core, [r]));
                    core = null;
                    break;
                case "spr-in":
                    Assert.True(open is null, "a draw began inside another");
                    open = [r];
                    break;
                case "spr-ret":
                    Assert.True(open is not null, "a draw ended that never began");
                    open!.Add(r);
                    draws.Add(new Draw(core, open));
                    open = null;
                    core = null;
                    break;
                case "spa":
                case "spa-view":
                    break;
                default:
                    Assert.True(open is not null, $"a '{r.Tag}' row outside a draw");
                    open!.Add(r);
                    break;
            }
        }
        Assert.True(open is null, "the recording ends inside a draw");
        return draws;
    }

    // The engine's glow answers, as recorded, and what the planner asked.
    private sealed class RecordedGlow(Row? vis, Row? dist) : IFxSpriteGlow
    {
        public readonly List<string> Wrong = [];
        public int Asked;

        public float FractionVisible(in Vector position, float proxySize, float proxyAspect, ref int queryHandle)
        {
            Asked++;
            if (vis is null)
            {
                Wrong.Add("asked visibility where stock did not");
                return 0;
            }
            if (!Same(position, vis.V("pos")) || proxySize != vis.F("proxy") || proxyAspect != vis.F("aspect"))
                Wrong.Add($"visibility asked at {position} proxy {proxySize} aspect {proxyAspect}, stock {vis.V("pos")} {vis.F("proxy")} {vis.F("aspect")}");
            return vis.F("frac");
        }

        public float SightDistance(in Vector position)
        {
            Asked++;
            if (dist is null)
            {
                Wrong.Add("asked distance where stock did not");
                return 0;
            }
            return float.Parse(dist.Bare[0], CultureInfo.InvariantCulture);
        }
    }

    private sealed class NullLog : ILog
    {
        public readonly List<string> Lines = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add(message);
    }

    private static bool Same(in Vector a, in Vector b) => a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

    private static SpriteDrawRequest RequestOf(Draw d)
    {
        Row inRow = d.Rows[0];
        Row? axes = d.Rows.FirstOrDefault(r => r.Tag == "spr-axes");
        var req = new SpriteDrawRequest
        {
            DrawSprites = inRow.I("draw"),
            WireframeMode = inRow.I("wire") != 0,
            Orientation = inRow.I("orient"),
            Left = inRow.F("ext", 0), Right = inRow.F("ext", 1), Up = inRow.F("ext", 2), Down = inRow.F("ext", 3),
            Width = inRow.F("wh", 0), Height = inRow.F("wh", 1),
            Origin = inRow.V("o"),
            EffectOrigin = axes is not null ? axes.V("eo") : inRow.V("o"),
            Angles = new QAngle(inRow.F("a", 0), inRow.F("a", 1), inRow.F("a", 2)),
            RenderMode = inRow.I("mode"), RenderFx = inRow.I("fx"), Alpha = inRow.I("alpha"),
            R = inRow.I("rgb", 0), G = inRow.I("rgb", 1), B = inRow.I("rgb", 2),
            Scale = inRow.F("scale"),
            HdrColorScale = inRow.F("hdr"),
            Blend = inRow.F("blend"),
            ViewForward = inRow.V("fwd"), ViewRight = inRow.V("right"), ViewUp = inRow.V("up"),
            GlowProxySize = 2.0f,   // PIXELVIS_DEFAULT_PROXY_SIZE: C_SpriteRenderer::GlowBlend's
        };
        if (d.Core is { } core)
        {
            // A managed sprite: the scale as the entity asked for it, and its proxy.
            req.Scale = core.F("scale");
            req.WorldSpaceScale = core.I("ws") != 0;
            req.Width = core.F("wh", 0);
            req.Height = core.F("wh", 1);
            req.GlowProxySize = core.F("proxy");
        }
        return req;
    }

    // What stock did, in the plan's terms; null when the row set says nothing.
    private static string Compare(Draw d, in SpriteDrawPlan plan)
    {
        var bad = new List<string>();
        Row last = d.Rows[^1];
        if (last.Tag == "spr-skip")
        {
            if (plan.Drawn || plan.SetBlend || plan.Quad)
                bad.Add("stock skipped the draw");
            return string.Join("; ", bad);
        }

        bool drawn = last.Bare[0] == "1";
        if (plan.Drawn != drawn)
            bad.Add($"drawn {plan.Drawn}, stock {drawn}");

        Row? blend = d.Rows.FirstOrDefault(r => r.Tag == "spr-blend");
        bool opaqueSet = d.Rows.Any(r => r.Tag == "spb");
        if (blend is not null)
        {
            float want = float.Parse(blend.Bare[0], CultureInfo.InvariantCulture);
            if (!plan.SetBlend || !plan.Blend.Equals(want))
                bad.Add($"blend {plan.SetBlend}/{plan.Blend}, stock {want}");
        }
        else if (opaqueSet)
        {
            if (!plan.SetBlend || plan.Blend != 1.0f)
                bad.Add("stock set the blend to 1");
        }
        else if (plan.SetBlend)
            bad.Add("stock set no blend");

        Row? quad = d.Rows.FirstOrDefault(r => r.Tag == "spq");
        if (quad is null)
        {
            // Without a quad row the draw stopped before the mesh (or there was no material).
            if (plan.Quad && !d.Rows.Any(r => r.Tag == "spq-nomat"))
                bad.Add("stock built no quad");
            return string.Join("; ", bad);
        }
        if (!plan.Quad)
        {
            bad.Add("stock built a quad");
            return string.Join("; ", bad);
        }
        if (plan.Wireframe != (quad.I("wire") != 0))
            bad.Add($"wireframe {plan.Wireframe}, stock {quad.I("wire")}");
        if (plan.R != (byte)quad.I("col", 0) || plan.G != (byte)quad.I("col", 1) || plan.B != (byte)quad.I("col", 2) || plan.A != (byte)quad.I("col", 3))
            bad.Add($"colour {plan.R} {plan.G} {plan.B} {plan.A}, stock {string.Join(' ', quad.Groups["col"])}");
        if (!plan.HdrColorScale.Equals(quad.F("hdr")))
            bad.Add("hdr scale");
        Vector[] corners = [plan.P0, plan.P1, plan.P2, plan.P3];
        for (int i = 0; i < 4; i++)
        {
            if (!Same(corners[i], quad.V("p" + i)))
                bad.Add($"corner {i} {corners[i]}, stock {quad.V("p" + i)}");
        }
        return string.Join("; ", bad);
    }

    [Fact]
    public void TheFixtureIsARealRecording()
    {
        List<Row> rows = Recorded();
        List<Draw> draws = Draws(rows);

        Assert.True(draws.Count == 5633, $"5,633 draws recorded (5,555 entered, 78 skipped), read {draws.Count}");
        Assert.True(rows.Count(r => r.Tag == "spg-vis") == 2252, "2,252 glow queries");
        Assert.True(rows.Count(r => r.Tag == "spq") == 3922, "3,922 quads");
        Assert.True(rows.Count(r => r.Tag == "spa") == 792, "792 axis questions");
        Assert.True(draws.Count(d => d.Core is not null) == 3159, "3,159 draws were managed sprites");
        Assert.True(rows.Count(r => r.Tag == "spg-ret" && r.Bare[0] != "0") > 0, "a glow answered non-zero (the positive arm)");
    }

    [Fact]
    public void EveryRecordedDrawIsPlannedAsStockDrewIt()
    {
        var renderer = new SpriteRenderer(new NullLog());
        var wrong = new List<string>();
        int n = 0, glowAsked = 0;

        foreach (Draw d in Draws(Recorded()))
        {
            n++;
            if (d.Rows[0].Tag == "spr-skip")
            {
                var skip = new SpriteDrawRequest { DrawSprites = d.Rows[0].I("draw") };
                SpriteDrawPlan skipped = renderer.Plan(skip, new RecordedGlow(null, null));
                string s = Compare(d, skipped);
                if (s.Length > 0)
                    wrong.Add($"draw {n}: {s}");
                continue;
            }

            Row? vis = d.Rows.FirstOrDefault(r => r.Tag == "spg-vis");
            Row? dist = d.Rows.FirstOrDefault(r => r.Tag == "spg-dist");
            var glow = new RecordedGlow(vis, dist);
            SpriteDrawPlan plan = renderer.Plan(RequestOf(d), glow);
            glowAsked += glow.Asked;

            int stockAsked = (vis is null ? 0 : 1) + (dist is null ? 0 : 1);
            if (glow.Asked != stockAsked)
                glow.Wrong.Add($"asked the engine {glow.Asked} times, stock {stockAsked}");

            string c = Compare(d, plan);
            if (c.Length > 0 || glow.Wrong.Count > 0)
                wrong.Add($"draw {n}: {c} {string.Join("; ", glow.Wrong)}");
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} draws disagree: " + string.Join(" | ", wrong.Take(8)));
        Assert.True(glowAsked > 0, "the planner asked the engine about a glow (the positive arm)");
    }

    // The request as native's GatherSprite lays it out (SS_SPRITE_IN_*).
    private static float[] InputOf(in SpriteDrawRequest r)
    {
        var f = new float[ClientSpriteHost.InFloats];
        f[ClientSpriteHost.InDrawSprites] = r.DrawSprites;
        f[ClientSpriteHost.InWireframeMode] = r.WireframeMode ? 1 : 0;
        f[ClientSpriteHost.InOrientation] = r.Orientation;
        f[ClientSpriteHost.InLeft] = r.Left; f[ClientSpriteHost.InRight] = r.Right;
        f[ClientSpriteHost.InUp] = r.Up; f[ClientSpriteHost.InDown] = r.Down;
        f[ClientSpriteHost.InWidth] = r.Width; f[ClientSpriteHost.InHeight] = r.Height;
        Put(f, ClientSpriteHost.InOrigin, r.Origin);
        Put(f, ClientSpriteHost.InEffectOrigin, r.EffectOrigin);
        f[ClientSpriteHost.InAngles] = r.Angles.Pitch;
        f[ClientSpriteHost.InAngles + 1] = r.Angles.Yaw;
        f[ClientSpriteHost.InAngles + 2] = r.Angles.Roll;
        f[ClientSpriteHost.InRenderMode] = r.RenderMode; f[ClientSpriteHost.InRenderFx] = r.RenderFx;
        f[ClientSpriteHost.InAlpha] = r.Alpha;
        f[ClientSpriteHost.InR] = r.R; f[ClientSpriteHost.InG] = r.G; f[ClientSpriteHost.InB] = r.B;
        f[ClientSpriteHost.InScale] = r.Scale;
        f[ClientSpriteHost.InWorldSpaceScale] = r.WorldSpaceScale ? 1 : 0;
        f[ClientSpriteHost.InHdrColorScale] = r.HdrColorScale;
        f[ClientSpriteHost.InBlend] = r.Blend;
        Put(f, ClientSpriteHost.InViewForward, r.ViewForward);
        Put(f, ClientSpriteHost.InViewRight, r.ViewRight);
        Put(f, ClientSpriteHost.InViewUp, r.ViewUp);
        f[ClientSpriteHost.InGlowProxySize] = r.GlowProxySize;
        return f;
    }

    private static void Put(float[] f, int at, in Vector v)
    {
        f[at] = v.X;
        f[at + 1] = v.Y;
        f[at + 2] = v.Z;
    }

    // Native's two halves of a direct draw, as a double: Begin hands over the
    // recorded request, End keeps the plan it is given.
    private sealed class RecordedDraw(float[] input) : IFxSpriteDraw
    {
        public float[]? Planned;
        public int Begins;

        public bool Begin(EntityHandle owner, in Vector offset, int modelIndex, float frame, int renderMode,
                          int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale,
                          float hdrColorScale, float glowProxySize, Span<float> into)
        {
            Begins++;
            input.CopyTo(into);
            return true;
        }

        public float[]? Stored { get; private set; }

        public bool Store(ReadOnlySpan<float> output)
        {
            Stored = output.ToArray();
            return output.Length > 0;
        }

        public int End(ReadOnlySpan<float> output)
        {
            Planned = output.ToArray();
            return output.Length > 0 && output[ClientSpriteHost.OutDrawn] != 0 ? 1 : 0;
        }
    }

    /// <summary>
    /// THE DIRECT PATH PLANS WHAT THE CALLBACK PATH PLANS. A managed sprite's
    /// draw is planned between FxSpriteDrawBegin and FxSpriteDrawEnd now rather
    /// than through IManagedFxClient::SpritePlan (the render budget's crossing);
    /// over every managed sprite draw of the stock recording, the plan handed to
    /// native is the same 22 floats, bit for bit, as the callback path's body
    /// answers for the same request and the same glow answers.
    /// </summary>
    [Fact]
    public void TheDirectDrawHandsNativeTheCallbackPathsPlanBitForBit()
    {
        var renderer = new SpriteRenderer(new NullLog());
        var wrong = new List<string>();
        int n = 0, quads = 0;

        foreach (Draw d in Draws(Recorded()))
        {
            if (d.Core is null || d.Rows[0].Tag == "spr-skip")
                continue;

            n++;
            Row? vis = d.Rows.FirstOrDefault(r => r.Tag == "spg-vis");
            Row? dist = d.Rows.FirstOrDefault(r => r.Tag == "spg-dist");
            float[] input = InputOf(RequestOf(d));

            var callback = new float[ClientSpriteHost.OutFloats];
            int planned = new ClientSpriteHost(renderer, new RecordedGlow(vis, dist), new RecordedDraw(input))
                .Plan(input, callback);

            var native = new RecordedDraw(input);
            int drawn = new ClientSpriteHost(renderer, new RecordedGlow(vis, dist), native)
                .DrawSprite(new EntityHandle(1u), 64, 0f, 0, 0, 0, 0, 0, 0, 1f, false, 1f, 2f);

            if (planned != ClientSpriteHost.OutFloats || native.Begins != 1 || native.Planned is null)
            {
                wrong.Add($"draw {n}: planned {planned}, begins {native.Begins}, ended {native.Planned is not null}");
                continue;
            }

            for (int i = 0; i < ClientSpriteHost.OutFloats; i++)
            {
                if (BitConverter.SingleToInt32Bits(native.Planned[i]) != BitConverter.SingleToInt32Bits(callback[i]))
                {
                    wrong.Add($"draw {n}: out[{i}] {native.Planned[i]} direct, {callback[i]} callback");
                    break;
                }
            }

            if (drawn != (callback[ClientSpriteHost.OutDrawn] != 0 ? 1 : 0))
                wrong.Add($"draw {n}: drawn {drawn}");
            if (callback[ClientSpriteHost.OutQuad] != 0)
                quads++;
        }

        Assert.True(n == 3081, $"every managed sprite draw the recording planned (3,159 less its 78 skips), saw {n}");
        Assert.True(quads > 0, "and some of them built a quad (the positive arm)");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} disagree: " + string.Join(" | ", wrong.Take(6)));
    }

    /// <summary>
    /// THE BATCHED PATH STORES WHAT THE DIRECT PATH WOULD DRAW (C7 item 7). A
    /// sprite prepared in its view's batch is planned from the same request and
    /// the plan kept for its DrawModel instead of drawn; over every managed
    /// sprite draw of the stock recording the stored plan is the callback path's
    /// 22 floats, bit for bit, and nothing is drawn at preparation.
    /// </summary>
    [Fact]
    public void ThePreparedSpriteStoresTheCallbackPathsPlanBitForBit()
    {
        var renderer = new SpriteRenderer(new NullLog());
        var wrong = new List<string>();
        int n = 0;

        foreach (Draw d in Draws(Recorded()))
        {
            if (d.Core is null || d.Rows[0].Tag == "spr-skip")
                continue;

            n++;
            Row? vis = d.Rows.FirstOrDefault(r => r.Tag == "spg-vis");
            Row? dist = d.Rows.FirstOrDefault(r => r.Tag == "spg-dist");
            float[] input = InputOf(RequestOf(d));

            var callback = new float[ClientSpriteHost.OutFloats];
            new ClientSpriteHost(renderer, new RecordedGlow(vis, dist), new RecordedDraw(input)).Plan(input, callback);

            var native = new RecordedDraw(input);
            int prepared = new ClientSpriteHost(renderer, new RecordedGlow(vis, dist), native)
                .PrepareSprite(new EntityHandle(1u), 64, 0f, 0, 0, 0, 0, 0, 0, 1f, false, 1f, 2f);

            if (prepared != 1 || native.Stored is null || native.Planned is not null)
            {
                wrong.Add($"draw {n}: prepared {prepared}, stored {native.Stored is not null}, drawn {native.Planned is not null}");
                continue;
            }
            for (int i = 0; i < ClientSpriteHost.OutFloats; i++)
            {
                if (BitConverter.SingleToInt32Bits(native.Stored[i]) != BitConverter.SingleToInt32Bits(callback[i]))
                {
                    wrong.Add($"draw {n}: out[{i}] {native.Stored[i]} stored, {callback[i]} callback");
                    break;
                }
            }
        }

        Assert.True(n == 3081, $"every managed sprite draw the recording planned, saw {n}");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} disagree: " + string.Join(" | ", wrong.Take(6)));
    }

    [NotOnCiFact("a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit")]
    public void EveryBatteryAxisQuestionIsAnsweredAsStockAnswered()
    {
        var log = new NullLog();
        var renderer = new SpriteRenderer(log);
        var wrong = new List<string>();
        Row? view = null;
        int n = 0;

        foreach (Row r in Recorded())
        {
            if (r.Tag == "spa-view")
            {
                view = r;
                continue;
            }
            if (r.Tag != "spa")
                continue;
            Assert.True(view is not null, "a battery row before its view");
            n++;

            var f = new Vector(777, 778, 779);
            var rt = new Vector(787, 788, 789);
            var u = new Vector(797, 798, 799);
            renderer.GetSpriteAxes(r.I("type"), r.V("o"), new QAngle(r.F("a", 0), r.F("a", 1), r.F("a", 2)),
                                   view!.V("fwd"), view.V("right"), view.V("up"), ref f, ref rt, ref u);

            if (!Same(f, r.V("f")) || !Same(rt, r.V("r")) || !Same(u, r.V("u")))
                wrong.Add($"type {r.I("type")} o {r.V("o")}: f {f} r {rt} u {u}, stock {r.V("f")} {r.V("r")} {r.V("u")}");
        }

        Assert.True(n == 792, $"792 battery rows, read {n}");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {n} disagree: " + string.Join(" | ", wrong.Take(6)));
        Assert.True(log.Lines.Count(l => l.Contains("Bad sprite type 5")) == 132, "stock's warning for each of the 132 bad-type questions");
    }

    //-------------------------------------------------------------------------
    // The arms the session did not reach. Each is stock's arithmetic, stated.
    //-------------------------------------------------------------------------

    private sealed class FixedGlow(float frac, float dist) : IFxSpriteGlow
    {
        public float FractionVisible(in Vector position, float proxySize, float proxyAspect, ref int queryHandle) => frac;
        public float SightDistance(in Vector position) => dist;
    }

    private static SpriteDrawRequest Plain(int mode) => new()
    {
        DrawSprites = 1, Orientation = SpriteRenderer.VpParallel, Left = -8, Right = 8, Up = 8, Down = -8,
        Width = 16, Height = 16, RenderMode = mode, Alpha = 200, R = 255, G = 128, B = 64, Scale = 1,
        Blend = 0.5f, ViewForward = new(1, 0, 0), ViewRight = new(0, -1, 0), ViewUp = new(0, 0, 1),
        GlowProxySize = 2,
    };

    [Fact]
    public void AnOpaqueSpriteDrawsAtFullBlendWithoutAskingTheEngine()
    {
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(Plain(0), new FixedGlow(-1, -1));

        Assert.True(plan.Drawn && plan.Quad, "a kRenderNormal sprite draws");
        Assert.True(plan.SetBlend && plan.Blend == 1.0f, "DrawSpriteModel sets the blend to 1 for it");
        Assert.True(plan.R == 255 && plan.G == 128 && plan.B == 64, "and leaves its colour alone");
    }

    [Fact]
    public void AScreenSpaceGlowGrowsWithDistanceFromAZeroScale()
    {
        // kRenderGlow at 2400 units: fade (1200/2400)^2 = 0.25; scale 0 -> 1 -> 2400/200 = 12.
        SpriteDrawRequest req = Plain(3);
        req.Scale = 0;
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(req, new FixedGlow(1, 2400));

        Assert.True(plan.Blend == 0.5f * 0.25f, $"blend 0.125, got {plan.Blend}");
        Assert.True(plan.P0.Y == 8 * 12, $"the quad's half-width is 8 x 12, got {plan.P0.Y}");
    }

    [Fact]
    public void AWorldGlowKeepsItsScale()
    {
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(Plain(9), new FixedGlow(1, 2400));

        Assert.True(plan.P0.Y == 8, $"kRenderWorldGlow is not fixed-size on screen, got {plan.P0.Y}");
    }

    [Fact]
    public void NoDissipationFadesByAlphaNotDistance()
    {
        SpriteDrawRequest req = Plain(3);
        req.RenderFx = 14;
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(req, new FixedGlow(0.5f, 99999));

        Assert.True(plan.Blend == 0.5f * (200f * (1.0f / 255.0f) * 0.5f), $"got {plan.Blend}");
    }

    [Fact]
    public void AGlowThatCannotBeSeenIsNotDrawn()
    {
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(Plain(3), new FixedGlow(0, 100));

        Assert.True(plan.SetBlend && plan.Blend == 0 && !plan.Drawn && !plan.Quad, "a zero blend is set and nothing drawn");
    }

    [Fact]
    public void AWorldSpaceScaleIsDividedByTheSmallerSide()
    {
        SpriteDrawRequest req = Plain(5);
        req.Width = 32;
        req.Height = 16;
        req.Scale = 64;
        req.WorldSpaceScale = true;
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(req, new FixedGlow(0, 0));

        Assert.True(plan.P0.Y == 8 * 4, $"64 units over 16 is a scale of 4, got {plan.P0.Y}");
    }

    [Fact]
    public void DrawSpritesOffDrawsNothing()
    {
        SpriteDrawRequest req = Plain(5);
        req.DrawSprites = 0;
        SpriteDrawPlan plan = new SpriteRenderer(new NullLog()).Plan(req, new FixedGlow(1, 1));

        Assert.True(!plan.Drawn && !plan.SetBlend, "r_drawsprites 0 draws nothing and leaves the blend");
    }

    [Fact]
    public void AnUprightSpriteSeenFromStraightAboveKeepsItsAxes()
    {
        var f = new Vector(1, 2, 3);
        var r = new Vector(4, 5, 6);
        var u = new Vector(7, 8, 9);
        new SpriteRenderer(new NullLog()).GetSpriteAxes(SpriteRenderer.VpParallelUpright, default, QAngle.Zero,
            new Vector(0, 0.0001f, -1f), new Vector(1, 0, 0), new Vector(0, 1, 0), ref f, ref r, ref u);

        Assert.True(f.X == 1 && r.X == 4 && u.X == 7, "within a degree of vertical the axes are left alone");
    }

    [Fact]
    public void AParallelSpriteWithRollTurnsInTheViewPlane()
    {
        var f = default(Vector);
        var r = default(Vector);
        var u = default(Vector);
        new SpriteRenderer(new NullLog()).GetSpriteAxes(SpriteRenderer.VpParallel, default, new QAngle(0, 0, 90),
            new Vector(1, 0, 0), new Vector(0, -1, 0), new Vector(0, 0, 1), ref f, ref r, ref u);

        Assert.True(MathF.Abs(r.Z - 1) < 1e-6f && MathF.Abs(u.Y - 1) < 1e-6f, $"rolled 90: right {r}, up {u}");
    }
}
