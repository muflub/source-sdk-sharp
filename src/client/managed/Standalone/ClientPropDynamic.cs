using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// A prop_dynamic's client half: the sequence the server started, run on this
/// side's clock (<see cref="PropDynamicNetState"/>), as stock's multiplayer
/// CDynamicProp animates client-side. A start is played from wherever the
/// server's clock has got to by now, so a client that joins mid-animation
/// sees it part-way; the skeleton's clock wraps a looping sequence and holds
/// a one-shot at its end.
/// </summary>
[ClientStandaloneEntity(EntityNames.PropDynamic)]
public class ClientPropDynamic : ClientStandaloneEntity
{
    public ClientPropDynamic(IEngineEntityRender render, IGameClock clock)
        : base(render)
    {
        Clock = clock;
    }

    private readonly IGameClock Clock;

    public override string ClassName => EntityNames.PropDynamic;

    [NetState] public PropDynamicNetState State = new();

    /// <summary>The start last played here, so an update that kept it does not restart it.</summary>
    private int _played;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (State.Starts == _played)
            return;

        _played = State.Starts;

        float duration = Render.SequenceDuration(Handle, State.Sequence);
        float elapsed = Clock.Now - State.StartTime;
        float cycle = State.StartCycle;
        if (duration > 0f && elapsed > 0f)
            cycle += elapsed * State.PlaybackRate / duration;

        Render.PlaySequenceAt(Handle, State.Sequence, State.PlaybackRate, cycle);
    }

    public override void OnRemoved() => _played = 0;
}

/// <summary>prop_dynamic_override's client half: the same prop under the name it spawned with.</summary>
[ClientStandaloneEntity(EntityNames.PropDynamicOverride)]
public sealed class ClientPropDynamicOverride : ClientPropDynamic
{
    public ClientPropDynamicOverride(IEngineEntityRender render, IGameClock clock)
        : base(render, clock)
    {
    }

    public override string ClassName => EntityNames.PropDynamicOverride;
}

/// <summary>dynamic_prop's client half: the same prop under the name it spawned with.</summary>
[ClientStandaloneEntity(EntityNames.DynamicProp)]
public sealed class ClientDynamicProp : ClientPropDynamic
{
    public ClientDynamicProp(IEngineEntityRender render, IGameClock clock)
        : base(render, clock)
    {
    }

    public override string ClassName => EntityNames.DynamicProp;
}
