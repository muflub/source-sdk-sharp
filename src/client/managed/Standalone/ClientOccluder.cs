using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_occluder, and the side the entity actually acts on.
///
/// <para>
/// Occlusion state lives on the client — <c>engine->ActivateOccluder</c> is a
/// client interface, because what an occluder does is stop the renderer drawing
/// what is behind it. So the server half of this entity decides and networks,
/// and this is where the decision lands.
/// </para>
///
/// <para>
/// Every update, not only the changed ones. Stock does the same, from
/// <c>C_FuncOccluder::OnDataChanged</c>, and it is not laziness: the engine's
/// occluder state is per level and is reset when the level loads, so an entity
/// that spoke once at signon and then went quiet would leave the occluder at
/// whatever the reset left there.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncOccluder)]
public sealed class ClientOccluder : ClientStandaloneEntity
{
    public ClientOccluder(IEngineEntityRender render, IEngineVisibility visibility)
        : base(render)
    {
        Visibility = visibility;
    }

    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;


    public override string ClassName => EntityNames.FuncOccluder;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public OccluderNetState State = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
        => Visibility.ActivateOccluder((int)State.Index, State.Active);
}
