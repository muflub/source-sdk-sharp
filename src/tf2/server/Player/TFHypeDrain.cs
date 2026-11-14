//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// <c>CTFPlayerShared::UpdateEnergyDrinkMeter</c>'s hype half
/// (tf_player_shared.cpp:14071-14078): while the Soda Popper's buff runs, the
/// meter pays the frame at the drink's consume rate times stock's 0.75, and a
/// meter at or under zero takes the condition with it. Stock runs the think
/// from <c>ConditionThink</c> (:3117), every frame whatever is held — the
/// per-player component, not the weapon's frames, as the rage drain is. The
/// refill half (:14101-14120) is the energy meter's, the lunchbox behaviour's
/// guest; the hype meter refills by no clock, only by damage.
/// </summary>
public sealed class TFHypeDrain(ITFPlayerData players, ITFPlayerConditions conditions,
                                TFScoutEnergydrinkConsumeRateConVar consume)
    : IPlayerComponent
{
    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private float _last = float.NaN;

    /// <summary>Between the rage drain (42) and the attributes (45): the same
    /// frame's meters, the order the stable sort keeps.</summary>
    public int Order => 43;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _last = float.NaN;
    }

    /// <summary>One frame of the drink: the metered drain and the emptied buff.</summary>
    public void Tick(float now)
    {
        float frametime = float.IsNaN(_last) ? 0f : now - _last;
        _last = now;

        if (frametime <= 0f || players.Predicted(_index) is not { } state)
            return;

        if (state.Class() != TFClass.Scout)                                   // :14059
            return;

        if (!conditions.InCond(_player, TFCond.SodapopperHype))
            return;

        // :14073 — no clamp; the emptied meter's branch owns the bottom.
        state.HypeMeter -= frametime * consume.Value * 0.75f;
        if (state.HypeMeter <= 0f)
            conditions.Remove(_player, TFCond.SodapopperHype);   // :14076
    }

    public void Detach() => _player = EntityHandle.None;
}
