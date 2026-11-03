//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEPlayerAnimEvent</c>: a player's animation event, sent to the
/// clients that can see the player so each one's copy of the player's
/// animation state plays it (sdk_player.cpp's <c>TE_PlayerAnimEvent</c>, the
/// multiplayer animation state's event path).
/// </summary>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE( CTEPlayerAnimEvent, DT_TEPlayerAnimEvent )
///     SendPropEHandle( SENDINFO( m_hPlayer ) ),
///     SendPropInt( SENDINFO( m_iEvent ), Q_log2( PLAYERANIMEVENT_COUNT ) + 1, SPROP_UNSIGNED ),
///     SendPropInt( SENDINFO( m_nData ), 32 )
/// END_SEND_TABLE()
/// </code>
/// <para>
/// HL2MP's own players show a shot and a reload as a gesture the server
/// restarts on its networked overlay layers (<c>CHL2MP_Player::SetAnimation</c>);
/// this port animates every player in each client's own copy of the
/// multiplayer state and networks no layers, so the event reaches those
/// copies the way the SDK's player sends it. <c>m_nData</c> carries the
/// weapon's catalog type for the two weapon events, which pick the gesture
/// by the weapon as HL2MP's activity tables do (stock's client read the
/// weapon from the player's networked active weapon, which this port does not
/// network for other players).
/// </para>
[TempEntity]
public class TePlayerAnimEvent : TempEntState
{
    /// <summary>The player, as <c>SendProxy_EHandleToInt</c> packs it.</summary>
    [TeProp(BitsFrom = TeBits.NetworkedEHandle, Flags = TeFlags.Unsigned)]
    public int Player;

    /// <summary><c>Q_log2( PLAYERANIMEVENT_COUNT ) + 1</c> bits: 38 events, six bits.</summary>
    [TeProp(Bits = 6, Flags = TeFlags.Unsigned)]
    public int Event;

    /// <summary>The event's data, 32 bits signed.</summary>
    [TeProp(Bits = 32)]
    public int Data;

    /// <summary>The player's entity index, or -1 for none.</summary>
    public int PlayerEntityIndex => TempEntReader.EntryIndexOf(Player);
}
