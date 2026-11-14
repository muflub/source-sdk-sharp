//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Shared;

/// <summary>
/// <c>CTFBuffItem</c> as a kind (<c>tf_weapon_buff_item.cpp:116-135</c>,
/// <c>tf_player_shared.cpp:13741-13863</c>): +attack with a full rage meter
/// deploys the held item's buff — the owner's draining stamp goes up
/// (<c>ActivateRageBuff</c>'s <c>m_bRageDraining = true</c>, :13794), the first
/// pulse lands at once (<c>SetupRageBuffTimer</c> calls
/// <c>PulseRageBuff</c> itself, :13747), and everything after the press rides
/// the player's tick (<c>TFBuffRageTicker</c>) on the
/// stock clocks: the meter drains while draining
/// (<c>UpdateRageBuffsAndRage</c>'s <c>m_flRageMeter -=
/// frametime * (100 / nBuffPulses)</c>, :13830) and the buff pulses once a
/// second while the pulse count holds (:13852-13862), whatever weapon is held
/// (:3116's player think). The buff itself — the
/// same-team alive players in reach, each carrying the cond for
/// <see cref="TFRageMeter.BuffDurationSeconds"/> — is the effects seam's
/// (<see cref="ITFWeaponEffects.DeployBuff"/>).
///
/// <para>
/// The press refuses below a full meter (:13756's arm with the sniper-focus
/// exemption not this kind's); the horn the stock item blows at +0.22 s
/// (:134) is the sounds lane's, and its one-second hold stands in here as the
/// refire clock. The deploy's stamp — buff type, pulse count and next-pulse
/// clock (<see cref="TFRageMeter.BuffPulses"/>) — lives on the player's
/// predicted state, stock's slot fields (:13743-13745), so the replay pays it
/// the same and a held item swap does not change whose buff is pulsing.
/// </para>
/// </summary>
public sealed class TFBuffItemBehaviour(TFWeaponParts parts, AttributeHooks attributes)
    : TFWeaponBehaviour(parts, attributes)
{
    /// <summary>The kind's shape for the factory's one-name call: the bare attribute chain.</summary>
    public TFBuffItemBehaviour(TFWeaponParts parts)
        : this(parts, new AttributeHooks())
    {
    }

    /// <summary>The pulse's one-second clock (<c>m_flNextBuffPulseTime +=
    /// 1.0f</c>, tf_player_shared.cpp:13858).</summary>
    public const float PulseInterval = 1.0f;


    /// <summary>
    /// The deploy: full meter or nothing (:125-126), the draining stamp up
    /// (:13794), the pulse clocks stamped (:13744-13745) and the first pulse
    /// asked (:13747). Stock blows its horn at +0.22 s (:134) — its sound is
    /// the sounds lane's; the one-second hold it gives is the refire clock
    /// here, so a held trigger deploys once. Stock's sniper-focus exemption
    /// from the full-meter gate (:13756) belongs to a buff type this kind's
    /// items do not wear.
    /// </summary>
    public override void PrimaryAttack(in WeaponAttackContext attack)
    {
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0 || Parts.PlayerData.Predicted(owner) is not { } state)
            return;

        if (state.RageMeter < TFRageMeter.FullMeter)
            return;

        state.RageDraining = true;

        int buffType = BuffTypeOf(in attack);

        if (attack.EffectsLive)
        {
            SendAnim(in attack, WeaponAnim.Secondary);            // the attack anim (:131-132)
            Parts.Effects.BlowBuffHorn(in attack, buffType);     // the horn (:134's +0.22 stand held by the refire clock)
            Parts.Effects.RaiseBuffBanner(in attack, buffType);   // event, flag sound, battle cry (:243-252)
        }

        state.RageBuffType = buffType;                             // the slot's stamp (:13743)
        state.RageBuffPulsesLeft = TFRageMeter.BuffPulses;        // (:13744)
        state.RageBuffNextPulseTime = attack.Now + PulseInterval; // (:13745)

        attack.Weapon.NextPrimaryAttack = attack.Now + PulseInterval;

        Parts.Effects.DeployBuff(in attack, buffType); // the deploy's own pulse (:13747, under the raise)

        if (attack.EffectsLive && ScriptOf(attack.Type) is { } script)
            Parts.Effects.SelectLast(in attack, script); // the raise's last-item switch (:256)
    }

    /// <summary>The deploy asks its buff type off the held item's
    /// <c>set_buff_type</c> — stock's <c>GetBuffType</c>, read the same way
    /// the meter's gain arm does.</summary>
    private int BuffTypeOf(in WeaponAttackContext attack)
        => _attributes.Int(0, TFRageMeter.BuffTypeAttribute,
                           Parts.Owners.ItemOf(attack.Command.OwnerIndex, attack.Type.Name));

}
