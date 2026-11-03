//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Player;

/// <summary>
/// What a <c>tf_wearable</c> cosmetic carries on the wire (Stage 2.13): the
/// owner it bone-merges to, the item's player display model, and the team skin
/// that colours it. Stock's <c>CTFWearable</c> (<c>DT_TFWearable</c>,
/// tf_item_wearable.cpp:28) networks <c>m_nItemDefIndex</c> (its
/// <c>CEconEntity</c> base) and <c>m_nSkin</c>; the model index and the owner
/// are the entity's own <c>m_hOwnerEntity</c> and model, which this build's
/// net states carry as fields because the client (Stage 3.3) draws the item by
/// them and nothing else on a cosmetic entity is shared.
///
/// <para>
/// Nothing here is <c>[Predicted]</c>: stock predicts no wearable field
/// (<c>DT_TFWearable</c> sends only; the drawing is client render state), and
/// the server writes every field once, when the item is equipped.
/// </para>
/// </summary>
[StandaloneNetState(TFEntityNames.Wearable)]
public partial class TFWearableNetState
{
    /// <summary>Entindex of the player this wearable is worn by; 0 for nobody.</summary>
    public partial int OwnerIndex { get; set; }

    /// <summary>
    /// The precached index of the item's player display model
    /// (<c>CEconWearable::InternalSetPlayerDisplayModel</c>'s
    /// <c>GetPlayerDisplayModel( iClass, iTeam )</c>, econ_wearable.cpp:66-99,
    /// <c>model_player_per_class</c> else <c>model_player</c>), so the client
    /// can draw the item on its owner.
    /// </summary>
    public partial int ModelIndex { get; set; }

    /// <summary>
    /// <c>m_nSkin</c>: the team skin (<c>CEconWearable::GetSkin</c>,
    /// econ_wearable.cpp:156-169 and <c>CTFWearable::Equip</c>,
    /// tf_item_wearable.cpp:741): <c>( iTeamNumber == LAST_SHARED_TEAM+1 ) ? 0
    /// : 1</c> — red is <see cref="TFTeam.Red"/> = 2 = TEAM_SPECTATOR+1, so a
    /// red player's wearable is skin 0 and every other team's is skin 1 (blue).
    /// The per-item <c>model_player_team_{red,blu}</c> skin override is Stage
    /// 3.3's, with the drawing.
    /// </summary>
    public partial int Skin { get; set; }

    /// <summary>
    /// <c>m_nItemDefIndex</c>: the item schema definition the wearable shows,
    /// so the client can resolve the item's own attributes and images.
    /// </summary>
    public partial int ItemDefIndex { get; set; }
}
