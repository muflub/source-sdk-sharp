using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_lod.
///
/// <para>
/// It hands one number to the shell and does nothing else, which is the right
/// amount: the fade is read on the render path once per entity per frame, and
/// the distance it fades AT changes only when the server says so. Pushing it is
/// what keeps managed code off that path entirely — compare
/// <see cref="ClientBeam"/>, which genuinely does draw per frame because a beam
/// has no model for the shell to draw for it.
/// </para>
///
/// <para>
/// The transition width is <c>lod_TransitionDist</c>, a client convar, and stock
/// reads it here rather than on the server for the same reason: how sharply
/// something fades is the player's graphics setting and how far away it fades is
/// the mapper's decision.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncLod)]
public sealed class ClientLod : ClientStandaloneEntity
{
    public ClientLod(IEngineEntityRender render)
        : base(render)
    {
    }



    public override string ClassName => EntityNames.FuncLod;

    [NetState] public LodNetState State = new();

    /// <summary>
    /// Stock's <c>lod_TransitionDist</c>, defaulted here rather than declared as
    /// a convar of our own. Reading the real one would be better and is a convar
    /// lookup this side does not have yet; 800 is the value the SDK ships and
    /// nothing in this tree changes it.
    /// </summary>
    private const float TransitionDistance = 800f;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        // Both ends of the band, because the shell wants a band and stock's
        // func_lod expresses it as a point plus a width. Fully solid until
        // DisappearDist, gone at DisappearDist + the transition.
        Render.SetFade(Handle, FadeStyle.Lod,
                                 State.DisappearDist,
                                 State.DisappearDist + TransitionDistance);
    }
}
