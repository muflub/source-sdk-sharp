//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Conditions;

/// <summary>
/// The conditions' names: <c>g_aConditionNames</c>
/// (<c>tf_shareddefs.cpp:295-435</c>, one <c>TF_COND_*</c> string per id in
/// the enum's order) and the two reads beside them —
/// <c>GetTFConditionName</c> and <c>GetTFConditionFromName</c>
/// (:436-458, a case-insensitive walk to <c>TF_COND_INVALID</c> when nothing
/// matches). <c>addcond</c>/<c>removecond</c> take a name whenever their
/// number argument is not one.
/// </summary>
public static class TFConditionNames
{
    /// <summary><c>GetTFConditionName</c>: the id's <c>TF_COND_*</c> string, or null outside 0..<see cref="TFCond.Last"/>.</summary>
    public static string? Of(TFCond cond) => cond switch
    {
        TFCond.Aiming => "TF_COND_AIMING",
        TFCond.Zoomed => "TF_COND_ZOOMED",
        TFCond.Disguising => "TF_COND_DISGUISING",
        TFCond.Disguised => "TF_COND_DISGUISED",
        TFCond.Stealthed => "TF_COND_STEALTHED",
        TFCond.Invulnerable => "TF_COND_INVULNERABLE",
        TFCond.Teleported => "TF_COND_TELEPORTED",
        TFCond.Taunting => "TF_COND_TAUNTING",
        TFCond.InvulnerableWearingoff => "TF_COND_INVULNERABLE_WEARINGOFF",
        TFCond.StealthedBlink => "TF_COND_STEALTHED_BLINK",
        TFCond.SelectedToTeleport => "TF_COND_SELECTED_TO_TELEPORT",
        TFCond.Critboosted => "TF_COND_CRITBOOSTED",
        TFCond.Tmpdamagebonus => "TF_COND_TMPDAMAGEBONUS",
        TFCond.FeignDeath => "TF_COND_FEIGN_DEATH",
        TFCond.Phase => "TF_COND_PHASE",
        TFCond.Stunned => "TF_COND_STUNNED",
        TFCond.Offensebuff => "TF_COND_OFFENSEBUFF",
        TFCond.ShieldCharge => "TF_COND_SHIELD_CHARGE",
        TFCond.DemoBuff => "TF_COND_DEMO_BUFF",
        TFCond.EnergyBuff => "TF_COND_ENERGY_BUFF",
        TFCond.Radiusheal => "TF_COND_RADIUSHEAL",
        TFCond.HealthBuff => "TF_COND_HEALTH_BUFF",
        TFCond.Burning => "TF_COND_BURNING",
        TFCond.HealthOverhealed => "TF_COND_HEALTH_OVERHEALED",
        TFCond.Urine => "TF_COND_URINE",
        TFCond.Bleeding => "TF_COND_BLEEDING",
        TFCond.Defensebuff => "TF_COND_DEFENSEBUFF",
        TFCond.MadMilk => "TF_COND_MAD_MILK",
        TFCond.Megaheal => "TF_COND_MEGAHEAL",
        TFCond.Regenondamagebuff => "TF_COND_REGENONDAMAGEBUFF",
        TFCond.Markedfordeath => "TF_COND_MARKEDFORDEATH",
        TFCond.Nohealingdamagebuff => "TF_COND_NOHEALINGDAMAGEBUFF",
        TFCond.SpeedBoost => "TF_COND_SPEED_BOOST",
        TFCond.CritboostedPumpkin => "TF_COND_CRITBOOSTED_PUMPKIN",
        TFCond.CritboostedUserBuff => "TF_COND_CRITBOOSTED_USER_BUFF",
        TFCond.CritboostedDemoCharge => "TF_COND_CRITBOOSTED_DEMO_CHARGE",
        TFCond.SodapopperHype => "TF_COND_SODAPOPPER_HYPE",
        TFCond.CritboostedFirstBlood => "TF_COND_CRITBOOSTED_FIRST_BLOOD",
        TFCond.CritboostedBonusTime => "TF_COND_CRITBOOSTED_BONUS_TIME",
        TFCond.CritboostedCtfCapture => "TF_COND_CRITBOOSTED_CTF_CAPTURE",
        TFCond.CritboostedOnKill => "TF_COND_CRITBOOSTED_ON_KILL",
        TFCond.CannotSwitchFromMelee => "TF_COND_CANNOT_SWITCH_FROM_MELEE",
        TFCond.DefensebuffNoCritBlock => "TF_COND_DEFENSEBUFF_NO_CRIT_BLOCK",
        TFCond.Reprogrammed => "TF_COND_REPROGRAMMED",
        TFCond.CritboostedRageBuff => "TF_COND_CRITBOOSTED_RAGE_BUFF",
        TFCond.DefensebuffHigh => "TF_COND_DEFENSEBUFF_HIGH",
        TFCond.SniperchargeRageBuff => "TF_COND_SNIPERCHARGE_RAGE_BUFF",
        TFCond.DisguiseWearingoff => "TF_COND_DISGUISE_WEARINGOFF",
        TFCond.MarkedfordeathSilent => "TF_COND_MARKEDFORDEATH_SILENT",
        TFCond.DisguisedAsDispenser => "TF_COND_DISGUISED_AS_DISPENSER",
        TFCond.Sapped => "TF_COND_SAPPED",
        TFCond.InvulnerableHideUnlessDamaged => "TF_COND_INVULNERABLE_HIDE_UNLESS_DAMAGED",
        TFCond.InvulnerableUserBuff => "TF_COND_INVULNERABLE_USER_BUFF",
        TFCond.HalloweenBombHead => "TF_COND_HALLOWEEN_BOMB_HEAD",
        TFCond.HalloweenThriller => "TF_COND_HALLOWEEN_THRILLER",
        TFCond.RadiushealOnDamage => "TF_COND_RADIUSHEAL_ON_DAMAGE",
        TFCond.CritboostedCardEffect => "TF_COND_CRITBOOSTED_CARD_EFFECT",
        TFCond.InvulnerableCardEffect => "TF_COND_INVULNERABLE_CARD_EFFECT",
        TFCond.MedigunUberBulletResist => "TF_COND_MEDIGUN_UBER_BULLET_RESIST",
        TFCond.MedigunUberBlastResist => "TF_COND_MEDIGUN_UBER_BLAST_RESIST",
        TFCond.MedigunUberFireResist => "TF_COND_MEDIGUN_UBER_FIRE_RESIST",
        TFCond.MedigunSmallBulletResist => "TF_COND_MEDIGUN_SMALL_BULLET_RESIST",
        TFCond.MedigunSmallBlastResist => "TF_COND_MEDIGUN_SMALL_BLAST_RESIST",
        TFCond.MedigunSmallFireResist => "TF_COND_MEDIGUN_SMALL_FIRE_RESIST",
        TFCond.StealthedUserBuff => "TF_COND_STEALTHED_USER_BUFF",
        TFCond.MedigunDebuff => "TF_COND_MEDIGUN_DEBUFF",
        TFCond.StealthedUserBuffFading => "TF_COND_STEALTHED_USER_BUFF_FADING",
        TFCond.BulletImmune => "TF_COND_BULLET_IMMUNE",
        TFCond.BlastImmune => "TF_COND_BLAST_IMMUNE",
        TFCond.FireImmune => "TF_COND_FIRE_IMMUNE",
        TFCond.PreventDeath => "TF_COND_PREVENT_DEATH",
        TFCond.MvmBotStunRadiowave => "TF_COND_MVM_BOT_STUN_RADIOWAVE",
        TFCond.HalloweenSpeedBoost => "TF_COND_HALLOWEEN_SPEED_BOOST",
        TFCond.HalloweenQuickHeal => "TF_COND_HALLOWEEN_QUICK_HEAL",
        TFCond.HalloweenGiant => "TF_COND_HALLOWEEN_GIANT",
        TFCond.HalloweenTiny => "TF_COND_HALLOWEEN_TINY",
        TFCond.HalloweenInHell => "TF_COND_HALLOWEEN_IN_HELL",
        TFCond.HalloweenGhostMode => "TF_COND_HALLOWEEN_GHOST_MODE",
        TFCond.MinicritboostedOnKill => "TF_COND_MINICRITBOOSTED_ON_KILL",
        TFCond.ObscuredSmoke => "TF_COND_OBSCURED_SMOKE",
        TFCond.ParachuteActive => "TF_COND_PARACHUTE_ACTIVE",
        TFCond.Blastjumping => "TF_COND_BLASTJUMPING",
        TFCond.HalloweenKart => "TF_COND_HALLOWEEN_KART",
        TFCond.HalloweenKartDash => "TF_COND_HALLOWEEN_KART_DASH",
        TFCond.BalloonHead => "TF_COND_BALLOON_HEAD",
        TFCond.MeleeOnly => "TF_COND_MELEE_ONLY",
        TFCond.SwimmingCurse => "TF_COND_SWIMMING_CURSE",
        TFCond.FreezeInput => "TF_COND_FREEZE_INPUT",
        TFCond.HalloweenKartCage => "TF_COND_HALLOWEEN_KART_CAGE",
        TFCond.Donotuse0 => "TF_COND_DONOTUSE_0",
        TFCond.RuneStrength => "TF_COND_RUNE_STRENGTH",
        TFCond.RuneHaste => "TF_COND_RUNE_HASTE",
        TFCond.RuneRegen => "TF_COND_RUNE_REGEN",
        TFCond.RuneResist => "TF_COND_RUNE_RESIST",
        TFCond.RuneVampire => "TF_COND_RUNE_VAMPIRE",
        TFCond.RuneReflect => "TF_COND_RUNE_REFLECT",
        TFCond.RunePrecision => "TF_COND_RUNE_PRECISION",
        TFCond.RuneAgility => "TF_COND_RUNE_AGILITY",
        TFCond.Grapplinghook => "TF_COND_GRAPPLINGHOOK",
        TFCond.GrapplinghookSafefall => "TF_COND_GRAPPLINGHOOK_SAFEFALL",
        TFCond.GrapplinghookLatched => "TF_COND_GRAPPLINGHOOK_LATCHED",
        TFCond.GrapplinghookBleeding => "TF_COND_GRAPPLINGHOOK_BLEEDING",
        TFCond.AfterburnImmune => "TF_COND_AFTERBURN_IMMUNE",
        TFCond.RuneKnockout => "TF_COND_RUNE_KNOCKOUT",
        TFCond.RuneImbalance => "TF_COND_RUNE_IMBALANCE",
        TFCond.CritboostedRuneTemp => "TF_COND_CRITBOOSTED_RUNE_TEMP",
        TFCond.PasstimeInterception => "TF_COND_PASSTIME_INTERCEPTION",
        TFCond.SwimmingNoEffects => "TF_COND_SWIMMING_NO_EFFECTS",
        TFCond.Purgatory => "TF_COND_PURGATORY",
        TFCond.RuneKing => "TF_COND_RUNE_KING",
        TFCond.RunePlague => "TF_COND_RUNE_PLAGUE",
        TFCond.RuneSupernova => "TF_COND_RUNE_SUPERNOVA",
        TFCond.Plague => "TF_COND_PLAGUE",
        TFCond.KingBuffed => "TF_COND_KING_BUFFED",
        TFCond.TeamGlows => "TF_COND_TEAM_GLOWS",
        TFCond.KnockedIntoAir => "TF_COND_KNOCKED_INTO_AIR",
        TFCond.CompetitiveWinner => "TF_COND_COMPETITIVE_WINNER",
        TFCond.CompetitiveLoser => "TF_COND_COMPETITIVE_LOSER",
        TFCond.HealingDebuff => "TF_COND_HEALING_DEBUFF",
        TFCond.PasstimePenaltyDebuff => "TF_COND_PASSTIME_PENALTY_DEBUFF",
        TFCond.GrappledToPlayer => "TF_COND_GRAPPLED_TO_PLAYER",
        TFCond.GrappledByPlayer => "TF_COND_GRAPPLED_BY_PLAYER",
        TFCond.ParachuteDeployed => "TF_COND_PARACHUTE_DEPLOYED",
        TFCond.Gas => "TF_COND_GAS",
        TFCond.BurningPyro => "TF_COND_BURNING_PYRO",
        TFCond.Rocketpack => "TF_COND_ROCKETPACK",
        TFCond.LostFooting => "TF_COND_LOST_FOOTING",
        TFCond.AirCurrent => "TF_COND_AIR_CURRENT",
        TFCond.HalloweenHellHeal => "TF_COND_HALLOWEEN_HELL_HEAL",
        TFCond.PowerupmodeDominant => "TF_COND_POWERUPMODE_DOMINANT",
        TFCond.ImmuneToPushback => "TF_COND_IMMUNE_TO_PUSHBACK",
        _ => null,
    };

    /// <summary>
    /// <c>GetTFConditionFromName</c>: the id whose name is
    /// <paramref name="name"/> case-insensitively, or
    /// <see cref="TFCond.Invalid"/>.
    /// </summary>
    public static TFCond FromName(string name)
    {
        for (int i = 0; i < TFConditions.Count; i++)
        {
            var cond = (TFCond)i;
            if (string.Equals(Of(cond), name, StringComparison.OrdinalIgnoreCase))
                return cond;
        }

        return TFCond.Invalid;
    }
}
