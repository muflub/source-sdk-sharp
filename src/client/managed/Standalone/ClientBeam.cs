using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of ss_beam.
///
/// <para>
/// Like <see cref="ClientBreakableSurf"/>, this does very little and is not
/// optional for that reason. A class with networked state of its own gets a
/// table on a side only if that side has an entity class to host it — and
/// without one here the server publishes a SendTable that this side cannot
/// match:
/// </para>
/// <code>
/// DataTable warning: No matching RecvTable for SendTable 'DT_ss_beam'.
/// Client missing DT class ss_beam
/// </code>
/// <para>
/// — and then the client begins joining and stops. This file exists because
/// that happened: the server class was added first and the join broke until the
/// pair was complete.
/// </para>
///
/// <para>
/// The drawing itself is <c>shell_beam_render.cpp</c>, which reads the same
/// bytes natively at offsets taken from the same table. Stretching a scrolling
/// sprite between two points is not something to reach across the boundary for
/// per frame, and the engine's own beam renderer already does it.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsBeam)]
public sealed class ClientBeam : ClientStandaloneEntity
{
    public ClientBeam(IEngineEntityRender render, IClientBeams beams)
        : base(render)
    {
        _beams = beams;
    }

    /// <summary>the small effects.</summary>
    private readonly IClientBeams _beams;



    public override string ClassName => EntityNames.SsBeam;

    /// <summary>
    /// The same declaration the server writes through. Decoded by the registry
    /// on every update; the renderer reads the same bytes.
    /// </summary>
    [NetState] public BeamNetState State = new();

    /// <summary>
    /// The bounds are pushed on every update because the endpoints ARE what
    /// changes, and the engine asks for bounds every frame.
    ///
    /// <para>
    /// The box runs from this entity's origin — which the server puts at the
    /// beam's start — to the far end, padded by the width. Without it the leaf
    /// system culls by a point and the beam vanishes whenever its emitter leaves
    /// the view, which looks exactly like a beam that stopped being drawn.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Vector local = State.End - State.Start;
        float pad = MathF.Max(State.Width, State.EndWidth) + 1f;

        var mins = new Vector(MathF.Min(0f, local.X) - pad,
                              MathF.Min(0f, local.Y) - pad,
                              MathF.Min(0f, local.Z) - pad);

        var maxs = new Vector(MathF.Max(0f, local.X) + pad,
                              MathF.Max(0f, local.Y) + pad,
                              MathF.Max(0f, local.Z) + pad);

        Render.SetCustomDraw(Handle, mins, maxs);
    }

    /// <summary>
    /// One beam, this frame.
    ///
    /// <para>
    /// Every value here was decoded from the entity's own networked state, which
    /// is why this class is C# and there is no renderer object behind it: there
    /// is nothing to read out of native memory, because the state already
    /// arrived as properties.
    /// </para>
    /// </summary>
    /// <summary>
    /// In the view's batch (C7 item 7): the beam's next life is made there, as
    /// <see cref="OnDraw"/> would make it at the entity's DrawModel. Nothing is
    /// drawn at either -- the beam is a renderable of its own and draws itself
    /// -- so ahead of time is the same draw.
    /// </summary>
    public override int OnDrawBatched() => OnDraw();

    public override int OnDraw()
    {
        if (!State.Visible || State.SpriteIndex < 0)
            return 0;

        // NOT a life of zero: CreateBeamPoints reads zero as FBEAM_FOREVER and
        // a forever beam a frame leaks until the renderer falls over. 0.01 is
        // retired at the next update and replaced at the next draw.
        _beams.CreateBeamPoints(new BeamInfo
        {
            Type = BeamType.Points,
            Start = State.Start,
            End = State.End,
            ModelIndex = State.SpriteIndex,
            HaloIndex = -1,
            HaloScale = 0f,
            Life = 0.01f,
            Width = State.Width,
            EndWidth = State.EndWidth,
            FadeLength = 0f,
            Amplitude = State.Noise,
            Brightness = State.Brightness,
            Speed = State.ScrollRate,
            StartFrame = 0,
            FrameRate = 0f,
            Red = State.Red,
            Green = State.Green,
            Blue = State.Blue,
            Segments = -1,
            Renderable = true,
            Flags = 0,
        });

        return 1;
    }
}
