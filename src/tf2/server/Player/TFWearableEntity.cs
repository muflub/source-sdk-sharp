//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The <c>tf_wearable</c> entity (Stage 2.13): stock's <c>CTFWearable</c>
/// (<c>LINK_ENTITY_TO_CLASS( tf_wearable, CTFWearable )</c>,
/// tf_item_wearable.cpp:27) over the <see cref="TFWearableNetState"/> the
/// client draws by. One per equipped wearable item, made and removed by
/// <see cref="TFWearables"/>, never placed by a map.
///
/// <para>
/// Stock's <c>CEconWearable::Spawn</c> (econ_wearable.cpp:105-122) sets the
/// item's player display model, adds <c>EF_BONEMERGE</c> so the item rides its
/// owner's skeleton, and drops collision; the server here carries only the
/// networked fields and the model index — the bone merge and the drawing are
/// the client's, Stage 3.3. Effect kind (no model, no collision on the native
/// side) and ALWAYS TRANSMITTED: a wearable is only ever seen beside its
/// owner, and it sits wherever it was made, so a PVS check on its own spot
/// would drop it — the carrier's reason, the same flag.
/// </para>
/// </summary>
[ScriptedEntity(TFEntityNames.Wearable, ScriptedEntityKind.Effect)]
public sealed class TFWearableEntity : ScriptedEntity
{
    private readonly IEngineEntityRender _render;

    public TFWearableEntity(IEngineEntityRender render)
    {
        _render = render;
    }

    /// <summary>The wearable's networked drawing state.</summary>
    [NetState] public TFWearableNetState State = new();

    /// <summary>The worn item's attributes, as stock's <c>CEconEntity::m_Item</c> on the entity; null until <see cref="TFWearables.Rebuild"/> equips it.</summary>
    public AttributeContainer? Item;

    public override void OnSpawn() => _render.SetTransmitAlways(Handle, true);
}
