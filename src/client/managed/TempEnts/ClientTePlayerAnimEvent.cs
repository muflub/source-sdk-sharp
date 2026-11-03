//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>C_TEPlayerAnimEvent::PostDataUpdate</c> (c_sdk_player.cpp): the player
/// the event names, when it is here and not dormant, plays it in this
/// client's copy of its animation state. A shot or a reload plays the HL2MP
/// gesture of the weapon the event's data names
/// (<see cref="PlayerAnimGestures"/>); any other event goes to the state as is.
/// </summary>
[ClientTempEnt("CTEPlayerAnimEvent")]
public sealed class ClientTePlayerAnimEvent : ClientTempEnt<TePlayerAnimEvent>
{
    private readonly IClientStandalones _standalones;
    private readonly WeaponCatalog _weapons;

    public ClientTePlayerAnimEvent(IClientStandalones standalones, WeaponCatalog weapons)
    {
        _standalones = standalones;
        _weapons = weapons;
    }

    /// <summary>Events played, for the facts and the probes.</summary>
    public int Played { get; private set; }

    public override void Play(TePlayerAnimEvent te)
    {
        if (_standalones.GetByIndex(te.PlayerEntityIndex) is not ClientSsPlayer player)
            return;

        var e = (PlayerAnimEvent)te.Event;
        bool played = PlayerAnimGestures.Activity(e, _weapons.ByIndex(te.Data)?.Name ?? "") is { } gesture
            ? player.DoAnimationEvent(PlayerAnimEvent.CustomGesture, 0, gesture)
            : player.DoAnimationEvent(e, te.Data);
        if (played)
            Played++;
    }
}
