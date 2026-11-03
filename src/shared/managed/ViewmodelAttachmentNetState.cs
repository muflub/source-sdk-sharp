//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the viewmodel attachment networks: the weapon's own model, riding the
/// hands (stock's <c>C_ViewmodelAttachmentModel</c>, econ_entity.cpp:1135).
///
/// <para>
/// Stock's attachment carries no predicted state of its own — its model is
/// the item's player display model, its skin comes from the viewmodel's, and
/// its visibility is "the owner is the local player in first person". The one
/// thing the client's dressing half must know is whose viewmodel it rides,
/// by entindex like every other entity reference on the wire.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsViewmodelAttachment)]
public partial class ViewmodelAttachmentNetState
{
    /// <summary>Entindex of the player whose viewmodel this rides.</summary>
    [Predicted]
    public partial int OwnerIndex { get; set; }

    /// <summary>Entindex of the viewmodel whose skeleton this merges onto.</summary>
    [Predicted]
    public partial int ViewModelIndex { get; set; }
}
