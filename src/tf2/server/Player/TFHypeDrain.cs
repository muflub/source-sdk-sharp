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
/// refill half (:14101-14120) — the boost's drain at the consume rate, both
/// buffs ending with an emptied meter, and the regain toward full at the regen
/// rate while nothing boosts — rides this same frame; the hype meter itself
/// refills by no clock, only by damage.
/// </summary>
public sealed class TFHypeDrain(ITFPlayerData players, ITFPlayerConditions conditions,
                                TFScoutEnergydrinkConsumeRateConVar consume,
                                TFScoutEnergydrinkRegenRateConVar regen)
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

        if (state.Class() != TFClass.Scout)                                   // :14059, the whole think's gate
            return;

        if (conditions.InCond(_player, TFCond.SodapopperHype))
        {
            // :14073 — no clamp; the emptied meter's branch owns the bottom.
            state.HypeMeter -= frametime * consume.Value * 0.75f;
            if (state.HypeMeter <= 0f)
                conditions.Remove(_player, TFCond.SodapopperHype);   // :14076
        }

        // The drink halves (:14080-14095): while the boost stands, the meter
        // pays the frame at the consume rate, and an emptied one ends both
        // names of the pair together.
        if (conditions.InCond(_player, TFCond.Phase) || conditions.InCond(_player, TFCond.EnergyBuff))
        {
            state.EnergyDrinkMeter -= frametime * consume.Value;               // :14083
            if (state.EnergyDrinkMeter <= 0f)                                   // :14086-14089, the names end together
            {
                // No clamp here either — the refill's next frame takes the
                // negative meter back up, as stock's else-if chain does.
                conditions.Remove(_player, TFCond.Phase);
                conditions.Remove(_player, TFCond.EnergyBuff);
            }

            return;
        }

        // The refill half (:14101-14119): nothing boosting, the meter regains
        // toward full at the regen rate, capped at the top.
        if (state.EnergyDrinkMeter < 100f)
            state.EnergyDrinkMeter = MathF.Min(100f, state.EnergyDrinkMeter + frametime * regen.Value);
    }

    public void Detach() => _player = EntityHandle.None;
}
