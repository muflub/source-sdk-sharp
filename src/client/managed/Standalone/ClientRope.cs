using SourceSharp.Abi;
using SourceSharp.Client.Ropes;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client halves of move_rope and keyframe_rope.
///
/// <para>
/// Each owns one span of cable from its own origin to the networked
/// <see cref="IRopeNetState.EndPos"/>: a verlet ring with wind, gusts and
/// impulses (<see cref="RopeSimulation"/>), stepped once a frame by
/// <see cref="IClientRopes"/> and drawn from this entity's own draw.
/// The near end is this entity's interpolated origin, swept each frame, so a
/// rope whose head moved while its blob did not still hangs from the right
/// place.
/// </para>
/// </summary>
public abstract class ClientRopeBase : ClientStandaloneEntity
{
    public ClientRopeBase(IEngineEntityRender render, IClientRopes ropes)
        : base(render)
    {
        RopeSystem = ropes;
    }

    protected readonly IClientRopes RopeSystem;

    protected abstract IRopeNetState Rope { get; }

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var r = Rope;

        if (!r.HasRope)
            return;

        RopeSystem.Configure(Handle, Index, r.EndPos,
                        r.Slack, r.Width, r.TextureScale,
                        r.Segments, r.Subdiv, r.ScrollSpeed,
                        r.Material);
    }

    public override int OnDraw() => RopeSystem.Draw(Handle) ? 1 : 0;

    public override void OnRemoved() => RopeSystem.Remove(Handle);
}

[ClientStandaloneEntity(EntityNames.MoveRope)]
public sealed class ClientRope : ClientRopeBase
{
    public ClientRope(IEngineEntityRender render, IClientRopes ropes)
        : base(render, ropes)
    {
    }

    public override string ClassName => EntityNames.MoveRope;

    [NetState] public MoveRopeNetState State = new();

    protected override IRopeNetState Rope => State;
}

/// <summary>keyframe_rope: every link after the head.</summary>
[ClientStandaloneEntity(EntityNames.KeyframeRope)]
public sealed class ClientRopeKeyframe : ClientRopeBase
{
    public ClientRopeKeyframe(IEngineEntityRender render, IClientRopes ropes)
        : base(render, ropes)
    {
    }

    public override string ClassName => EntityNames.KeyframeRope;

    [NetState] public KeyframeRopeNetState State = new();

    protected override IRopeNetState Rope => State;
}
