using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ragdolls;

namespace TF2.Client.Player;

/// <summary>
/// TF2's corpse fade, <c>cl_ragdoll_fade_time</c> past a body's arrival
/// (c_tf_player.cpp:513-516, :863). HL2MP's client has no such fade, its
/// bodies stay until the next death, so the core composes none and this
/// mod registers the shared <see cref="RagdollFades"/> for itself.
/// </summary>
[SideService(Side.Client, As = [typeof(IRagdollFades)])]
public sealed class TFRagdollFades(IEngineEntityRender render, IGameClock clock, RagdollFadeTimeConVar fadeTime)
    : IRagdollFades, IClientFrameThink, IOrderedHook
{
    /// <summary>In TF2's range, after the core's corpse hooks (the dissolve, the ash, the ice).</summary>
    public const int Order = 1_000_000 - 1_000 + 192;

    private readonly RagdollFades _fades = new(render, clock, fadeTime);

    public int HookOrder => Order;

    public void Begin(EntityHandle body) => _fades.Begin(body);

    public void End(EntityHandle body) => _fades.End(body);

    public void ClientThink(float frameTime) => _fades.ClientThink(frameTime);
}
