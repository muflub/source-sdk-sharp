//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// <c>CTFPlayerShared::UpdateRageBuffsAndRage</c>
/// (<c>tf_player_shared.cpp:13801-13863</c>) as the player's slice of the
/// per-player composite: the dead reset the system (:13806-13810, whose
/// numbers are <c>ResetRageSystem</c> :14045-14051 and <c>ResetRageBuffs</c>
/// :964-972); while draining, the meter loses the frame at the buff's pulse
/// count's share of full (:13830) and empties, clearing the stamp
/// (:13831-13834); once a second while the deploy's pulse count holds, the buff
/// reaches the team again (:13852-13862), the pulse asking the type stamped on
/// the player at the deploy (:13743). Stock runs this from the player's own
/// think (<c>ConditionThink</c>, :3116), so the drain and the pulses ride
/// whatever weapon is held — holtering the banner does not stop them, which is
/// why the arm lives on the per-player tick and not on the item's frames.
/// </summary>
public sealed class TFBuffRageTicker(ITFPlayerData players, ITFBuffEffects buffs, IEnginePlayers enginePlayers)
    : IPlayerComponent
{
    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float _last = float.NaN;

    /// <summary>After the respawn override (40) in the composite's block; unique
    /// among components, so the stable sort's tie-break never decides for it.</summary>
    public int Order => 42;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _last = float.NaN;
    }

    /// <summary>One frame of stock's rage think: the dead reset, the drain and the one-second pulse.</summary>
    public void Tick(float now)
    {
        float frametime = float.IsNaN(_last) ? 0f : now - _last;
        _last = now;

        if (players.Predicted(_index) is not { } state)
            return;

        if (!enginePlayers.IsAlive(_player))
        {
            // :13806-13810: death resets both the rage and the active buffs.
            state.RageMeter = 0f;
            state.RageDraining = false;
            state.RageBuffType = 0;
            state.RageBuffPulsesLeft = 0;
            state.RageBuffNextPulseTime = 0f;
            return;
        }

        if (state.RageDraining)
        {
            // :13830: the meter loses the frame at 100 over the buff's pulse count;
            // every buff type in g_RageBuffTypes wears the same count (:2760-2769).
            state.RageMeter -= frametime * (TFRageMeter.FullMeter / TFRageMeter.BuffPulses);
            if (state.RageMeter <= 0f)
            {
                // :13831-13834: the emptied meter holds at zero and the stamp goes.
                state.RageMeter = 0f;
                state.RageDraining = false;
            }
        }

        if (now > state.RageBuffNextPulseTime && state.RageBuffPulsesLeft > 0)
        {
            state.RageBuffNextPulseTime += TFBuffItemBehaviour.PulseInterval; // :13858
            state.RageBuffPulsesLeft--;                                        // :13859
            buffs.DeployBuff(_index, state.RageBuffType);                      // :13860 with the slot's type (:13743)
        }
    }

    public void Detach()
    {
        _player = EntityHandle.None;
        _index = -1;
    }
}
