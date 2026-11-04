//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What one attached model networks (stock's <c>attachedmodel_t</c> riding a
/// <c>CEconEntity</c>, econ_entity.h): whose weapon carries it, and the two
/// skeletons it can hang from — the weapon's in the world, the viewmodel's
/// when the local player's copy draws in the viewmodel pass. The model itself
/// and the skin are the entity's, dressed through the render side like every
/// carried model's.
/// </summary>
[StandaloneNetState(EntityNames.SsAttachedModel)]
public partial class AttachedModelNetState
{
    /// <summary>Entindex of the player carrying it.</summary>
    [Predicted]
    public partial int OwnerIndex { get; set; }

    /// <summary>Entindex of the ss_weapon whose world bones it rides.</summary>
    [Predicted]
    public partial int WeaponIndex { get; set; }

    /// <summary>
    /// Entindex of the viewmodel-pass host: the hands' attachment when the
    /// deploy created one (stock's m_hViewmodelAttachment), else the viewmodel
    /// wearing the row's own model.
    /// </summary>
    [Predicted]
    public partial int HostIndex { get; set; }

    /// <summary><c>model_display_flags</c>, <see cref="AttachedModelDisplayFlags"/>.</summary>
    [Predicted]
    public partial int Flags { get; set; }
}
