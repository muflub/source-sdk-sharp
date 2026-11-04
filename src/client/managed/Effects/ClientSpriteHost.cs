using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// Native's sprite plan, unpacked for <see cref="SpriteRenderer"/> and packed
/// back: the float layouts are shell_sprite_leaf.cpp's SS_SPRITE_IN_* and
/// SS_SPRITE_OUT_*, index for index.
/// </summary>
public sealed class ClientSpriteHost(SpriteRenderer renderer, IFxSpriteGlow glow, IFxSpriteDraw draw,
                                      RDrawSpritesConVar drawSprites, WireframeMode wireframe)
    : IClientSpriteHost, IClientSpriteDraw
{
    // SS_SPRITE_IN_*.
    public const int InOrientation = 0, InLeft = 1, InRight = 2, InUp = 3,
        InDown = 4, InWidth = 5, InHeight = 6, InOrigin = 7, InEffectOrigin = 10, InAngles = 13, InRenderMode = 16,
        InRenderFx = 17, InAlpha = 18, InR = 19, InG = 20, InB = 21, InScale = 22, InWorldSpaceScale = 23,
        InHdrColorScale = 24, InBlend = 25, InViewForward = 26, InViewRight = 29, InViewUp = 32, InGlowProxySize = 35,
        InQueryHandle = 36,
        InFloats = 37;

    // SS_SPRITE_OUT_*.
    public const int OutDrawn = 0, OutSetBlend = 1, OutBlend = 2, OutQuad = 3, OutWireframe = 4, OutRgba = 5,
        OutCorners = 9, OutHdrColorScale = 21, OutQueryHandle = 22, OutFloats = 23;

    /// <summary>Sprites planned since the load (cs_cl_fx_report).</summary>
    public long Planned { get; private set; }

    public int Plan(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length < InFloats || output.Length < OutFloats)
            return -1;

        var req = new SpriteDrawRequest
        {
            DrawSprites = drawSprites.Value,
            WireframeMode = wireframe.On,
            Orientation = (int)input[InOrientation],
            Left = input[InLeft], Right = input[InRight], Up = input[InUp], Down = input[InDown],
            Width = input[InWidth], Height = input[InHeight],
            Origin = V(input, InOrigin),
            EffectOrigin = V(input, InEffectOrigin),
            Angles = new QAngle(input[InAngles], input[InAngles + 1], input[InAngles + 2]),
            RenderMode = (int)input[InRenderMode], RenderFx = (int)input[InRenderFx], Alpha = (int)input[InAlpha],
            R = (int)input[InR], G = (int)input[InG], B = (int)input[InB],
            Scale = input[InScale],
            WorldSpaceScale = input[InWorldSpaceScale] != 0,
            HdrColorScale = input[InHdrColorScale],
            Blend = input[InBlend],
            ViewForward = V(input, InViewForward), ViewRight = V(input, InViewRight), ViewUp = V(input, InViewUp),
            GlowProxySize = input[InGlowProxySize],
            QueryHandle = BitConverter.SingleToInt32Bits(input[InQueryHandle]),
        };

        SpriteDrawPlan plan = renderer.Plan(req, glow);
        Planned++;

        output[..OutFloats].Clear();
        output[OutDrawn] = plan.Drawn ? 1 : 0;
        output[OutSetBlend] = plan.SetBlend ? 1 : 0;
        output[OutBlend] = plan.Blend;
        output[OutQuad] = plan.Quad ? 1 : 0;
        output[OutWireframe] = plan.Wireframe ? 1 : 0;
        output[OutRgba] = plan.R;
        output[OutRgba + 1] = plan.G;
        output[OutRgba + 2] = plan.B;
        output[OutRgba + 3] = plan.A;
        Put(output, OutCorners, plan.P0);
        Put(output, OutCorners + 3, plan.P1);
        Put(output, OutCorners + 6, plan.P2);
        Put(output, OutCorners + 9, plan.P3);
        output[OutHdrColorScale] = plan.HdrColorScale;
        output[OutQueryHandle] = BitConverter.Int32BitsToSingle(plan.QueryHandle);
        return OutFloats;
    }

    /// <summary>
    /// A managed sprite's draw: the request from native, the plan made here by
    /// <see cref="Plan"/> -- the same function the callback path runs -- and the
    /// plan handed back to be drawn. No crossing into managed for the plan,
    /// which is the render budget's whole point (live gate `render`).
    /// </summary>
    public int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx,
                          int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                          float glowProxySize)
        => DrawSpriteOffset(owner, Vector.Zero, modelIndex, frame, renderMode, renderFx, r, g, b, a,
                            scale, worldSpaceScale, hdrColorScale, glowProxySize);

    /// <inheritdoc cref="DrawSprite"/>
    public int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode,
                                int renderFx, int r, int g, int b, int a, float scale, bool worldSpaceScale,
                                float hdrColorScale, float glowProxySize)
    {
        Span<float> input = stackalloc float[InFloats];
        if (!draw.Begin(owner, offset, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale,
                        hdrColorScale, glowProxySize, input))
            return 0;

        Span<float> output = stackalloc float[OutFloats];
        if (Plan(input, output) != OutFloats)
            return draw.End(ReadOnlySpan<float>.Empty);

        return draw.End(output);
    }

    /// <inheritdoc cref="IClientSpriteDraw.PrepareSprite"/>
    public int PrepareSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx,
                             int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                             float glowProxySize)
    {
        Span<float> input = stackalloc float[InFloats];
        if (!draw.Begin(owner, Vector.Zero, modelIndex, frame, renderMode, renderFx, r, g, b, a, scale, worldSpaceScale,
                        hdrColorScale, glowProxySize, input))
            return 0;

        Span<float> output = stackalloc float[OutFloats];
        if (Plan(input, output) != OutFloats)
        {
            draw.End(ReadOnlySpan<float>.Empty);
            return 0;
        }
        return draw.Store(output) ? 1 : -1;
    }

    private static Vector V(ReadOnlySpan<float> s, int at) => new(s[at], s[at + 1], s[at + 2]);

    private static void Put(Span<float> s, int at, in Vector v)
    {
        s[at] = v.X;
        s[at + 1] = v.Y;
        s[at + 2] = v.Z;
    }
}

/// <summary>A managed sprite entity's draw, planned on this side.</summary>
public interface IClientSpriteDraw
{
    int DrawSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx,
                   int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize);

    int DrawSpriteOffset(EntityHandle owner, Vector offset, int modelIndex, float frame, int renderMode, int renderFx,
                         int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale,
                         float glowProxySize);

    /// <summary>
    /// The draw batch's half (C7 item 7): the sprite planned now and its plan
    /// stored natively, for its DrawModel to carry out. 1 when stored, 0 when
    /// there is nothing to draw (DrawModel answers 0 without a plan), -1 when it
    /// must draw in place.
    /// </summary>
    int PrepareSprite(EntityHandle owner, int modelIndex, float frame, int renderMode, int renderFx,
                      int r, int g, int b, int a, float scale, bool worldSpaceScale, float hdrColorScale, float glowProxySize);
}
