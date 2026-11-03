//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Client.Player;

/// <summary>
/// The client's <see cref="ITFWeaponOwners"/>, for the local player's
/// predicted weapons: the class and the crit multiplier from the carrier
/// (the predicted working copy for the local player, the decoded state for
/// anyone else), and the conditions as a <see cref="TFConditions"/> bound to
/// the WORKING COPY's five words — so a weapon's <c>TF_COND_AIMING</c> on a
/// replayed command is in the copy the movement reads and the ring saves,
/// and the next frame's reset to the server's words is what the set reads
/// after it. Only the local player is predicted: anyone else has no
/// conditions to write.
///
/// <para>
/// <see cref="WinsCrits"/> is the round-win arm of
/// <c>CalcIsAttackCritical</c>: stock's team_win block (tf_weaponbase.cpp:1563)
/// crits the winners outright, not only through the 16 s
/// <c>TF_COND_CRITBOOSTED_BONUS_TIME</c> grant, so the prediction reads the
/// resource's <c>RoundState</c> and <c>WinningTeam</c> words the server writes
/// every think — the same read the respawn countdown and the HUD make.
/// </para>
///
/// <para>
/// <see cref="ItemOf"/> answers the item behind a predicted weapon from the
/// definition index the carrier carries for that weapon row — what stock
/// networks on each weapon's own item view (<c>DT_ScriptCreatedItem</c>'s
/// <c>m_iItemDefinitionIndex</c>, econ_item_view.cpp:182), kept here as one
/// array on the TF carrier (see <see cref="TFPlayerNetState.WeaponItemDefIndex"/>):
/// the client looks the defindex up in its own parsed schema
/// (<c>CEconItemView::GetItemDefinition</c>, econ_item_view.h:194, whose
/// <c>GetStaticData</c> looks the index up in the schema at
/// econ_item_view.cpp:435) and hands
/// back the definition's static attributes, so the predicted hooks walk the
/// same table the server's walk does. It reads the DECODED state, as stock
/// reads the server's item view: the defindex is the loadout's word, not a
/// prediction (CritMult's rule). The one attribute answer still deferred is
/// the player's own attribute manager (<see cref="ManagerOf"/>'s walk): stock
/// networks the player's <c>m_AttributeManager</c> (tf_player.h:647, sent at
/// tf_player.cpp:831), and nothing here carries it yet.
/// </para>
///
/// <para>
/// <see cref="ITFWeaponOwners.EyesUnderwater"/> and <see cref="ITFWeaponOwners.OffGround"/>
/// read the standalones' player state — the networked <c>WaterLevel</c> and
/// flags word the local player's prediction
/// moves, the same read the underwater ambience makes. Only the local player
/// is predicted, so the flamethrower's refusals are theirs to read; anyone
/// else's shell runs on the server.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFWeaponOwners)])]
public sealed class TFClientWeaponOwners(ITFPlayerData data, IClientStandalones standalones,
                                         TFClientPlayerResources? resources = null,
                                         ItemSchema? itemSchema = null, WeaponCatalog? catalog = null,
                                         ILocalArsenal? arsenal = null, IEngineHud? hud = null)
    : ITFWeaponOwners
{
    /// <summary>WL_Eyes (<c>GameMovement.WlEyes</c>, which a client class cannot name).</summary>
    private const int WaterLevelEyes = 3;

    /// <summary>FL_ONGROUND (<c>public/const.h</c>, <c>GameMovement.FlOnGround</c>).</summary>
    private const uint FlOnGround = 1u << 0;

    /// <summary>FL_DUCKING (<c>public/const.h</c>, <c>GameMovement.FlDucking</c>).</summary>
    private const uint FlDucking = 1u << 1;

    private TFConditions? _conditions;
    private TFPlayerNetState? _bound;

    public TFClass ClassOf(int playerIndex) => State(playerIndex)?.Class() ?? TFClass.Undefined;

    public TFConditions? ConditionsOf(int playerIndex)
    {
        if (data.Predicted(playerIndex) is not { } working)
            return null;

        if (!ReferenceEquals(working, _bound))
        {
            _bound = working;
            _conditions = new TFConditions(authoritative: false, new TFCarrierCondWords(working));
        }

        return _conditions;
    }

    public float CritMult(int playerIndex) => State(playerIndex)?.CritMultiplier() ?? 1f;

    // Sent, not predicted (the arrival's word): the replay reads the working
    // copy the snapshot decoded, so the refusal matches the server's frame.
    public float NoAttackExpire(int playerIndex) => State(playerIndex)?.NoAttackExpire ?? 0f;

    public bool WinsCrits(int playerIndex)
        => resources?.Live is { } live
           && playerIndex is > 0 and < TFPlayerResourceNetState.Slots
           && live.State.RoundState == (byte)RoundState.TeamWin
           && live.State.WinningTeam == live.State.Team[playerIndex];

    public IAttributeProvider? ItemOf(int playerIndex, string weaponName)
    {
        if (itemSchema is null || catalog is null)
            return null;

        if (data.Networked(playerIndex) is not { } state)
            return null;

        int row = catalog.IndexOf(weaponName);
        if (row < 0 || row >= state.WeaponItemDefIndex.Length)
            return null;

        // The wire keeps defindex + 1 (the array's untouched zero is no item
        // and the schema's own item 0, the bat, stays nameable).
        if (itemSchema.Item(state.WeaponItemDefIndex[row] - 1) is not { } definition)
            return null;

        // Stock's client-side item view (CEconItemView over the server's
        // defindex); Owner stays null — the player's own manager is
        // ManagerOf's deferred story.
        return new AttributeContainer(definition, AttributeProviderType.Weapon);
    }

    public IAttributeProvider? ManagerOf(int playerIndex) => null;

    /// <summary>
    /// The local player's active weapon, as the arsenal holds it; the client
    /// predicts nobody else's hand, so anyone else is stock's no-weapon arm.
    /// </summary>
    public IAttributeProvider? ActiveItemOf(int playerIndex)
    {
        if (hud is null || arsenal is null || playerIndex != hud.LocalPlayerIndex)
            return null;

        return arsenal.TryGetActive(out string name, out _, out _)
            ? ItemOf(playerIndex, name)
            : null;
    }

    public bool OffGround(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player
           && (player.State.Flags & FlOnGround) == 0;

    public bool EyesUnderwater(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player
           && player.State.WaterLevel == WaterLevelEyes;

    public bool DuckingOf(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player
           && (player.State.Flags & FlDucking) != 0;

    private TFPlayerNetState? State(int playerIndex) => data.Predicted(playerIndex) ?? data.Networked(playerIndex);
}
