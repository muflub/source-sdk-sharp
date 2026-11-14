using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Town;

/// <summary>
/// The client's <c>tf2_descent_vendor</c> (plan_descent_logic WO8): a town stall, a vendor in a TF2
/// class's model or a piece of TF2's furniture. The model and place arrive on the entity's own
/// block; this plays the server's idle (<see cref="VendorNetState.Sequence"/>) from the moment it
/// arrives, as the town portal's pad plays its spin.
/// </summary>
[ClientStandaloneEntity(VendorNetState.ClassName)]
public sealed class ClientVendor(IEngineEntityRender render) : ClientStandaloneEntity(render)
{
    public override string ClassName => VendorNetState.ClassName;

    [NetState] public VendorNetState State = new();

    private int _playing = -1;

    /// <summary>The sequence this client plays; -1 before any.</summary>
    public int Playing => _playing;

    public override void OnSpawn() => _playing = -1;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (State.Sequence == _playing || State.Sequence < 0)
            return;
        _playing = State.Sequence;
        Render.PlaySequence(Handle, _playing, 1f, true);
    }
}
