//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// CTFGrenadeLauncher's donk list (<c>m_vecDonkVictims</c>,
/// tf_weapon_grenadelauncher.h:127): the victims the Loose Cannon's ball
/// shoved, each held for <c>tf_double_donk_window</c> from its push
/// (AddDonkVictim, tf_weapon_grenadelauncher.cpp:433-448), whose blast then
/// scores the double-donk's mini-crit (IsDoubleDonk, :451-467, asked by
/// tf_gamerules.cpp:6205). Stock keys the list on the launcher entity; the
/// launcher here is whichever cannon the attacker holds, so the list rides
/// the player, culled on the add as stock culls.
/// </summary>
public sealed class TFDonkVictims(TFPlayerCarriers carriers, IGameClock clock,
                                  TFDoubleDonkWindowConVar? donkWindow = null) : IPlayerComponent
{
    /// <summary>After the stun stack (30), the push that feeds it read in the same frame's damage.</summary>
    public int Order => 33;

    private readonly List<(EntityHandle Victim, float Expire)> _victims = [];
    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _victims.Clear();
        carriers.EnterPart(_index, this);
    }

    public void Tick(float now)
    {
    }

    public void Detach()
    {
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
        _player = EntityHandle.None;
        _victims.Clear();
    }

    /// <summary>
    /// <c>AddDonkVictim</c> (:433-448): the expired entries leave as the new
    /// one lands, whose window is <c>curtime + tf_double_donk_window</c>.
    /// </summary>
    public void Donk(EntityHandle victim)
    {
        float now = clock.Now;
        _victims.RemoveAll(donk => donk.Expire <= now);
        _victims.Add((victim, now + (donkWindow?.Value ?? 0.5f)));
    }

    /// <summary><c>IsDoubleDonk</c>'s scan (:458-464): the victim pushed within the window.</summary>
    public bool Donked(EntityHandle victim)
    {
        float now = clock.Now;
        return _victims.Exists(donk => now < donk.Expire && donk.Victim == victim);
    }
}
