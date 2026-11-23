//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The class grunt a dying player hears: <c>CTFPlayer::DeathSound</c>
/// (<c>tf_player.cpp:15228-15295</c>), run on the core's death path — stock's
/// <c>CBasePlayer::Event_Killed</c> calls it through <c>Event_Dying</c>
/// (<c>player.cpp:1757</c>), TF's feign through <c>FeignDeath</c>
/// (<c>tf_player.cpp:15726</c>). The grunt is the victim's class script's own
/// (<see cref="TFClassData.DeathSound(TFDeathSound)"/>); which one the script
/// names is the killing blow's damage bits (<c>m_LastDamageType</c>, carried
/// here on the hit the damage layer recorded,
/// <see cref="TFDamageRecords.LastHit"/>): fall, blast, crit, melee, else the
/// generic grunt, checked in stock's order.
///
/// <para>
/// Arms: an undefined class is silent (:15232-15233, a player still choosing a
/// class makes no sound); the feign-death arm (<c>m_bGoingFeignDeath</c>,
/// :15239-15249) wears the disguise's script when the spy is disguised as a
/// teammate; a killer whose active weapon carries <c>set_silent_killer</c>
/// leaves no sound at all (:15251-15257, Your Eternal Reward — the walk of
/// <c>CTFWeaponBase::IsSilentKiller</c>, <c>tf_weaponbase.cpp:6184-6192</c>);
/// the crit grunt is followed by the pain the receiver hears
/// (<c>PlayCritReceivedSound</c>, :15118-15130). The MvM arms (:15261-15271,
/// :15298) are not ported: this mod has no Mann vs. Machine mode.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFDeathSounds(ITFPlayerData players, TFClassTable classes, IEngineSound sound,
                                  TFDamageRecords records, IEnginePlayers enginePlayers, IEngineEntities entities,
                                  AttributeHooks attributes, ITFWeaponOwners owners)
    : IPlayerDeathHook, IOrderedHook
{
    public int HookOrder => TFPlayerOrders.DeathSounds;

    public void OnPlayerDied(PlayerDeath death)
    {
        if (players.Predicted(death.EntIndex) is not { } state)
            return;                                                    // :15231, no carrier to ask

        TFClass playerClass = state.Class();                           // GetPlayerClass(), :15235
        if (playerClass == TFClass.Undefined)
            return;                                                    // :15232 choosing a class is silent

        TFClassData data = classes[playerClass];                       // GetData(), :15235
        if (!data.IsParsed)
            return;                                                    // :15236 !pData

        if (state.Conditions().Has(TFCond.FeignDeath))                 // m_bGoingFeignDeath, :15239
        {
            // :15241 the disguise counts only when it is a teammate's own team
            if (state.Conditions().Has(TFCond.Disguised)
                && state.DisguiseTeam == enginePlayers.GetTeam(death.Player))
            {
                // :15245 the corpse wears the disguise class's grunt
                data = classes[state.DisguiseAs()];
                if (!data.IsParsed)
                    return;                                            // :15246-15247
            }
        }

        // The silent killer (:15251-15257): the attacker's ACTIVE weapon, the
        // item's set_silent_killer == 1 — Your Eternal Reward's walk
        // (tf_weaponbase.cpp:6184-6192) — leaves the death without a grunt.
        EntityHandle attacker = death.Attacker;
        if (attacker != EntityHandle.None && entities.IsValid(attacker)
            && attributes.Int(0, "set_silent_killer",
                              owners.ItemOf(attacker.EntryIndex, enginePlayers.GetActiveWeapon(attacker))) == 1)
            return;                                                    // :15255-15256

        DamageType bits = records.LastHit(death.EntIndex)?.Bits ?? DamageType.Generic;   // m_LastDamageType

        if ((bits & DamageType.Fall) != 0)                             // :15273 died in the fall
        {
            sound.EntityEmitSound(death.Player, FallSound);            // :15276 the splat
            return;
        }

        if ((bits & DamageType.Blast) != 0)                            // :15278
        {
            sound.EntityEmitSound(death.Player, data.DeathSound(TFDeathSound.Explosion));
            return;
        }

        if ((bits & TFDamageBits.Critical) != 0)                       // :15282
        {
            sound.EntityEmitSound(death.Player, data.DeathSound(TFDeathSound.Crit));
            // PlayCritReceivedSound (:15118-15127): to the dying player alone,
            // a CSingleUserRecipientFilter, not made reliable.
            sound.EmitSoundToPlayer(death.Player, death.Player, CritPainSound, reliable: false);
            return;
        }

        if ((bits & DamageType.Club) != 0)                             // :15288
        {
            sound.EntityEmitSound(death.Player, data.DeathSound(TFDeathSound.Melee));
            return;
        }

        sound.EntityEmitSound(death.Player, data.DeathSound(TFDeathSound.Generic));   // :15294
    }

    // Stock's own names for the two fixed sounds: the fall's splat (:15276)
    // and the crit's pain (:15125).
    private const string FallSound = "Player.FallGib";
    private const string CritPainSound = "TFPlayer.CritPain";
}
