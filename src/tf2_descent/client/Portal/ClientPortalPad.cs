using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Portal;

/// <summary>
/// The client's <c>tf2_descent_teleporter</c> (plan_descent_logic WO4): a town portal's pad,
/// TF2's teleporter in look. The model, place and team skin arrive on the entity's own block;
/// this spins the arms: it plays the server's <see cref="PortalNetState.Sequence"/> (stock's
/// charged teleporter's <c>ACT_OBJ_RUNNING</c>) from the moment the pad arrives.
/// </summary>
[ClientStandaloneEntity(PortalNetState.ClassName)]
public sealed class ClientPortalPad(IEngineEntityRender render) : ClientStandaloneEntity(render)
{
    public override string ClassName => PortalNetState.ClassName;

    [NetState] public PortalNetState State = new();

    private int _playing = -1;

    /// <summary>The sequence this client plays on the pad; -1 before any.</summary>
    public int Playing => _playing;

    public PortalEnd End => (PortalEnd)State.End;

    public override void OnSpawn() => _playing = -1;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (State.Sequence == _playing || State.Sequence < 0)
            return;
        _playing = State.Sequence;
        Render.PlaySequence(Handle, _playing, 1f, true);
    }
}
