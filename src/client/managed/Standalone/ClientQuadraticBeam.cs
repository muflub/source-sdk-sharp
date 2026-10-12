using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of env_quadraticbeam.
///
/// <para>
/// <see cref="ClientBeam"/>'s sibling, and it exists for the same
/// non-negotiable reason: a class with networked state gets a table on a side
/// only if that side has an entity class to host it, and a server table with no
/// match here stops the join with a named error rather than a missing beam.
/// </para>
///
/// <para>
/// The whole of the drawing is one call. That is what made this class worth
/// porting when the smoke and steam entities beside it were not — their client
/// halves are particle systems, and this one is the engine's own
/// <c>DrawBeamQuadratic</c> with the arguments already decoded on this side.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvQuadraticbeam)]
public sealed class ClientQuadraticBeam : ClientStandaloneEntity
{
    public ClientQuadraticBeam(IEngineEntityRender render, IGameClock clock, IClientBeams beams)
        : base(render)
    {
        Clock = clock;
        _beams = beams;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>the small effects.</summary>
    private readonly IClientBeams _beams;



    public override string ClassName => EntityNames.EnvQuadraticbeam;

    /// <summary>
    /// The same declaration the server writes through. Decoded by the registry
    /// on every update.
    /// </summary>
    [NetState] public QuadraticBeamNetState State = new();

    /// <summary>
    /// The bounds, pushed on every update because the curve IS what changes.
    ///
    /// <para>
    /// Stock's <c>C_QuadraticBeam::GetRenderBounds</c> takes the box around all
    /// three points and does NOT pad it, which is very slightly wrong — a wide
    /// beam bulges outside its own control hull. The width is added here for
    /// that reason, and it costs nothing: a bounding box that is a little large
    /// draws when it need not, and one that is a little small vanishes at the
    /// edge of the screen, which is the failure that reads as a bug.
    /// </para>
    /// <para>
    /// Relative to the curve's start, which is where the entity is — so the
    /// start contributes the zero vector and only the other two points and the
    /// padding move the box.
    /// </para>
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Vector control = State.Control - State.Start;
        Vector target = State.Target - State.Start;
        float pad = State.Width + 1f;

        var mins = new Vector(MathF.Min(0f, MathF.Min(control.X, target.X)) - pad,
                              MathF.Min(0f, MathF.Min(control.Y, target.Y)) - pad,
                              MathF.Min(0f, MathF.Min(control.Z, target.Z)) - pad);

        var maxs = new Vector(MathF.Max(0f, MathF.Max(control.X, target.X)) + pad,
                              MathF.Max(0f, MathF.Max(control.Y, target.Y)) + pad,
                              MathF.Max(0f, MathF.Max(control.Z, target.Z)) + pad);

        Render.SetCustomDraw(Handle, mins, maxs);
    }

    /// <summary>
    /// One curve, this frame.
    ///
    /// <para>
    /// The scroll arrives as a RATE and is turned into an offset here, against
    /// the client's own clock. Doing it on the server would mean networking a
    /// number that changes every frame to say something that never changes, and
    /// the texture would step once per snapshot instead of running.
    /// </para>
    /// </summary>
    public override int OnDraw()
    {
        if (!State.Visible || State.SpriteIndex <= 0)
            return 0;

        // 0..1 for the builder, 0..255 on the wire.
        var color = new Vector(State.Red * (1.0f / 255.0f), State.Green * (1.0f / 255.0f),
                               State.Blue * (1.0f / 255.0f));

        _beams.DrawQuadratic(State.Start, State.Control, State.Target, State.Width, color,
                            Clock.Now * State.ScrollRate, State.SpriteIndex, (int)RenderMode.TransAdd);

        return 1;
    }
}
