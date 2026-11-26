//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Buildings;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Combat;

/// <summary>
/// TF's <c>player_death</c> (<c>CTFGameRules::DeathNotice</c>,
/// <c>shared/tf/tf_gamerules.cpp:12814-13243</c>) as the core's
/// <see cref="IDeathNoticeHook"/>, from the killing hit the damage layer
/// recorded (<see cref="TFDamageRecords"/>):
///
/// <para>
/// — <c>attacker</c>: the death's scorer (<c>GetDeathScorer</c> :12761-12801),
///   which for a suicide is the most recent enemy damager within
///   <c>TF_TIME_SUICIDE_KILL_CREDIT</c> (10 s).
/// </para>
/// <para>
/// — <c>weapon</c> and <c>weaponid</c>: <c>GetKillingWeaponName</c>
///   (:12108-12685): the burning kinds name their weapon whatever the
///   attacker holds now, a scorer that inflicted the kill names its active
///   weapon (not for a suicide), else the inflictor's class; the
///   <c>tf_weapon_grenade_</c>, <c>tf_weapon_</c>, <c>NPC_</c> and
///   <c>func_</c> prefixes stripped (:12540-12551). A scattergun kill is
///   <c>scattergun</c>; a taunt kill its taunt's weapon (:12209-12231), a
///   trigger kill the map's hazard or the <c>world</c> the chain began with
///   (:12429-12439).
/// </para>
/// <para>
/// — <c>assister</c>: <c>GetAssister</c> (:12690-12736), first answer wins:
///   the victim's named assist (their victim-side condition's giver or their
///   control stun's, <c>DetermineAssistForKill</c> :11000-11010), the scorer's
///   first healer when that healer is a medic and the inflictor is no sentry,
///   the giver of the scorer's buff conditions
///   (<c>GetConditionAssistFromAttacker</c>), the second most recent damager
///   within <c>TF_TIME_ASSIST_KILL</c> (3 s) when it is not the scorer, and
///   for a sentry inflictor the gun's last teammate helper; else -1; never
///   for a suicide. The kept assister gets the kill-assist stat
///   (<see cref="TFPlayerStats.Increment"/>, stock's <c>Event_AssistKill</c>
///   :11514-11518).
/// </para>
/// <para>
/// — <c>damagebits</c>, <c>customkill</c> and <c>crit_type</c>: the killing
///   hit's, after the modify rules (a crit's <c>DMG_CRITICAL</c>, a headshot's
///   custom kind); a self-inflicted death with none recorded is the suicide
///   custom kind, stock's CommitSuicide flag; <c>victim_entindex</c>,
///   <c>inflictor_entindex</c>,
///   <c>weapon_logclassname</c> (the same name: no item log names yet),
///   <c>silent_kill</c> false, <c>stun_flags</c> 0 and <c>death_flags</c>:
///   stock's domination and revenge (<see cref="TFDominations"/>, computed
///   before the kill is recorded, tf_gamerules.cpp:11500-11518); and
///   <c>rocket_jump</c>: the victim's blast jump (<see cref="TFBlastJumpState"/>),
///   stock's direct read of the dying player (tf_gamerules.cpp:13116).
/// </para>
///
/// <para>
/// Not yet: the econ item icon and log names, the kill streaks, feign death,
/// first blood, the deflected projectiles and the sentry levels
/// (<c>docs/plans/tf2/deferred.tsv</c>).
/// </para>
/// </summary>
[SideService(Side.Server)]
/// <summary>
/// Every dependency is injected: the side's registrations hold each one (the
/// dominations and the owners through their own <c>SideService</c> rows, the
/// attribute hooks beside them), so a missing one fails the composition by
/// name instead of answering a default.
/// </summary>
public sealed class TFDeathNotice(IEnginePlayers players, IEngineEntities entities, TFDamageRecords records,
                                  IGameClock clock, TFPlayerCarriers carriers, ILevelInfo level,
                                  TFDominations dominations,
                                  ITFWeaponOwners owners, AttributeHooks attributes,
                                  ITFPlayerHealing healing, ITFPlayerConditions conditions,
                                  IEntityDirectory directory, ITFPlayerStats stats)
    : IDeathNoticeHook, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 703;

    /// <summary><c>TF_DEATH_AUSTRALIUM</c> (tf_shareddefs.h:1816): the gilded death's background.</summary>
    public const int Australium = 0x0400;

    public int HookOrder => Order;

    /// <summary>
    /// Whether the notice of <paramref name="death"/> is silent (its <c>silent_kill</c>, the rule <see cref="Fill"/> writes):
    /// a backstab by a scorer whose weapon is a silent killer. The AI's feed asks it (a silent kill is in no feed of the
    /// victim's team).
    /// </summary>
    public bool SilentKill(in DeathNoticeFacts death)
    {
        if (death.Scorer == EntityHandle.None)
            return false;
        EntityHandle attacker = death.Attacker != EntityHandle.None && entities.IsValid(death.Attacker) ? death.Attacker : EntityHandle.None;
        TFHitRecord? last = records.LastHit(death.Victim.EntryIndex);
        TFDamageCustom custom = last is { } h && h.Attacker == attacker ? h.Custom : TFDamageCustom.None;
        return Silent(death.Scorer, custom);
    }

    // A silent killer causes no death notices (tf_gamerules.cpp:12833-12839): the scorer's active weapon holding
    // IsSilentKiller() (the item's set_silent_killer == 1, tf_weaponbase.cpp:6184-6192) with the backstab's custom kind.
    private bool Silent(EntityHandle scorer, TFDamageCustom custom)
        => scorer != EntityHandle.None && entities.IsValid(scorer) && custom == TFDamageCustom.Backstab
           && attributes.Int(0, "set_silent_killer", owners.ItemOf(scorer.EntryIndex, players.GetActiveWeapon(scorer))) == 1;   // :6188

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        int victimIndex = death.Victim.EntryIndex;
        float now = clock.Now;
        TFHitRecord? last = records.LastHit(victimIndex);
        // The record is the killing hit's when it came from the same attacker
        // (the layer records an attacker that is no entity as none).
        EntityHandle attacker = death.Attacker != EntityHandle.None && entities.IsValid(death.Attacker)
            ? death.Attacker : EntityHandle.None;
        TFHitRecord? hit = last is { } h && h.Attacker == attacker ? h : null;

        EntityHandle scorer = death.Scorer;
        if (death.Attacker == death.Victim && death.Inflictor == death.Victim)
        {
            EntityHandle credit = records.RecentDamager(victimIndex, 0, TFCombatDefs.TimeSuicideKillCredit, now);
            if (credit != EntityHandle.None && entities.IsValid(credit))
                scorer = credit;
        }

        var (weapon, weaponId) = KillingWeapon(hit?.Custom ?? TFDamageCustom.None, scorer, death.Inflictor, death.Victim,
                                               hit?.WeaponName ?? "", hit?.Weapon ?? TFWeaponId.None);
        string logName = weapon;
        if (ItemNames(scorer, weaponId, ProjectileShot(death.Inflictor)?.Row ?? hit?.WeaponName ?? "")
            is var (icon, itemLog))
        {
            weapon = icon ?? weapon;
            logName = itemLog ?? logName;
        }

        EntityHandle assister = Assister(death, scorer, now);
        if (assister == scorer || (assister != EntityHandle.None && !entities.IsValid(assister)))
            assister = EntityHandle.None;

        fields.SetInt("victim_entindex", entities.GetIndex(death.Victim));
        fields.SetInt("attacker", scorer != EntityHandle.None ? players.GetUserId(scorer) : 0);
        fields.SetInt("assister", assister != EntityHandle.None ? players.GetUserId(assister) : -1);
        fields.SetString("weapon", weapon);
        fields.SetString("weapon_logclassname", logName);
        fields.SetInt("weaponid", (int)weaponId);
        fields.SetInt("damagebits", (int)(hit?.Bits ?? DamageType.Generic));
        // The self-kill command's death carries no recorded kind of its own:
        // stock's CommitSuicide flags the damage info
        // TF_DMG_CUSTOM_SUICIDE (tf_player.cpp:10218-10224, passed on at
        // player.cpp:5448-5458), and the event's customkill is what the
        // client's info text reads. A fall keeps its own phrase by the
        // client's precedence.
        var custom = hit?.Custom ?? TFDamageCustom.None;
        if (custom == TFDamageCustom.None
            && (death.Attacker == EntityHandle.None || death.Attacker == death.Victim)
            && (death.Inflictor == EntityHandle.None || death.Inflictor == death.Victim))
            custom = TFDamageCustom.Suicide;
        fields.SetInt("customkill", (int)custom);
        fields.SetInt("inflictor_entindex", death.Inflictor != EntityHandle.None ? entities.GetIndex(death.Inflictor) : -1);
        // A silent killer causes no death notices (Silent); everything else keeps silent_kill false.
        bool silentKill = Silent(scorer, custom);
        fields.SetBool("silent_kill", silentKill);
        int deathFlags = dominations.DeathFlags(victimIndex,
            scorer != EntityHandle.None ? entities.GetIndex(scorer) : -1,
            assister != EntityHandle.None ? entities.GetIndex(assister) : -1);

        // The gilded deaths (tf_gamerules.cpp:13092-13108): the killing blow's
        // weapon wearing is_australium_item or set_turn_to_gold — the Golden
        // Wrench's gold among them — gilds the flag the notice's background
        // reads.
        if (hit is { } killing && scorer != EntityHandle.None
            && owners?.ItemOf(scorer.EntryIndex, killing.WeaponName) is { } killer
            && (attributes!.Int(0, "is_australium_item", killer) != 0
                || attributes.Int(0, "set_turn_to_gold", killer) != 0))
            deathFlags |= Australium; // MUTATED

        fields.SetInt("death_flags", deathFlags);
        // The assister's own credit, after the flags (stock's Event_AssistKill
        // over SetDeathFlags, tf_gamerules.cpp:11514-11518): the kill-assist
        // stat rides on the kept assister, and on nobody else.
        if (assister != EntityHandle.None)
            stats.Increment(assister, TFStat.Killassists);
        fields.SetInt("stun_flags", 0);
        // Stock reads the victim's state directly (tf_gamerules.cpp:13116,
        // RocketJumped() == 1): a death in the air carries the word, one after
        // the landing — the state cleared — does not.
        TFBlastJump jumped = carriers.Part<TFBlastJumpState>(victimIndex)?.State ?? TFBlastJump.None;
        fields.SetBool("rocket_jump", (jumped & TFBlastJump.RocketJumped) != 0);
        fields.SetInt("crit_type", (int)(hit?.Crit ?? TFCritType.None));
    }

    /// <summary>
    /// <c>GetKillingWeaponName</c> for the arms this build can reach: the
    /// kill-feed name and the weapon id.
    /// </summary>
    public (string Name, TFWeaponId Id) KillingWeapon(TFDamageCustom custom, EntityHandle scorer, EntityHandle inflictor,
                                                       EntityHandle victim, string hitWeaponName, TFWeaponId hitWeapon)
    {
        string name = "world";
        var id = TFWeaponId.None;

        switch (custom)
        {
            // Burning may land after the attacker switched weapons.
            case TFDamageCustom.Burning:
                (name, id) = ("tf_weapon_flamethrower", TFWeaponId.Flamethrower);
                break;
            case TFDamageCustom.BurningFlare:
                (name, id) = ("tf_weapon_flaregun", TFWeaponId.Flaregun);
                break;
            case TFDamageCustom.TauntatkHadouken:              // :12209-12211
                name = "tf_weapon_taunt_pyro";
                break;
            case TFDamageCustom.TauntatkHighNoon:              // :12213-12215
                name = "tf_weapon_taunt_heavy";
                break;
            case TFDamageCustom.TauntatkGrandSlam:             // :12217-12219
                name = "tf_weapon_taunt_scout";
                break;
            case TFDamageCustom.TauntatkBarbarianSwing:        // :12221-12223
                name = "tf_weapon_taunt_demoman";
                break;
            case TFDamageCustom.TauntatkUberslice:             // :12225-12227
                name = "tf_weapon_taunt_medic";
                break;
            case TFDamageCustom.TauntatkFencing:               // :12229-12231
                name = "tf_weapon_taunt_spy";
                break;
            case TFDamageCustom.TauntatkArrowStab:             // :12233-12235
                name = "tf_weapon_taunt_sniper";
                break;
            // The soldier's taunt (:12237-12246); stock's lumbricus half asks
            // the killer's hat for the Worms Gear item def (tf_player.cpp:17243-17248),
            // which waits on the wearable schema.
            case TFDamageCustom.TauntatkGrenade:
                name = "tf_weapon_taunt_soldier";
                break;
            case TFDamageCustom.TauntatkEngineerGuitarSmash:   // :12248-12250
                name = "tf_weapon_taunt_guitar_kill";
                break;
            case TFDamageCustom.TauntatkAllclassGuitarRiff:    // :12252-12254
                name = "tf_weapon_taunt_guitar_riff_kill";
                break;
            case TFDamageCustom.TauntatkEngineerArmKill:       // :12256-12258
                name = "robot_arm_blender_kill";
                break;
            case TFDamageCustom.TriggerHurt:                   // :12429-12439
                name = MapHazard() ?? name;
                break;
            case TFDamageCustom.Telefrag:
                name = "telefrag";
                break;
            case TFDamageCustom.Bleeding:
                name = "tf_weapon_bleed_kill";
                break;
            case TFDamageCustom.SapperRecorderDeath:       // tf_gamerules.cpp:12333-12336
                name = "recorder";
                break;
            case TFDamageCustom.PlayerSentry:              // :12296-12311
                name = scorer != EntityHandle.None
                       && attributes.Int(0, "is_giger_counter", owners.ActiveItemOf(scorer.EntryIndex)) > 0
                    ? "giger_counter" : "wrangler_kill";
                break;
            // THE FISH KILL'S NOTICE (CTFPlayer::OnTakeDamage's call,
            // tf_player.cpp:9758): fish_notice, fish_notice__arm once the
            // killing item carries fish_damage_override — stock's
            // CALL_ATTRIB_HOOK_INT_ON_OTHER over the weapon that dealt the blow.
            case TFDamageCustom.FishKill:
                name = scorer != EntityHandle.None && entities.IsValid(scorer)
                       && attributes.Int(0, "fish_damage_override",
                                          owners.ItemOf(scorer.EntryIndex, hitWeaponName)) > 0
                    ? "fish_notice__arm" : "fish_notice";
                id = hitWeapon;
                break;
            default:
                if (scorer != EntityHandle.None && inflictor != EntityHandle.None && inflictor == scorer)
                {
                    // Not a suicide: the killer's current weapon did it.
                    if (victim != scorer)
                    {
                        string active = hitWeaponName.Length > 0 ? hitWeaponName : players.GetActiveWeapon(scorer);
                        if (active.Length > 0)
                            (name, id) = (active, hitWeapon);
                    }
                }
                else if (inflictor != EntityHandle.None && entities.IsValid(inflictor))
                {
                    // The inflictor's class, and a CTFBaseRocket's own weapon id
                    // (:12461-12474, pBaseRocket->GetWeaponID()): the id the
                    // notice's item step below names the scorer's weapon by.
                    name = entities.GetClassName(inflictor);
                    if (ProjectileShot(inflictor) is { } shot)
                        id = shot.Id;
                }
                break;
        }

        return (StockName(name), id);
    }

    /// <summary>
    /// The <c>CTFBaseRocket</c> kin this build fires as an inflictor (the rocket,
    /// the flare, the arrow, the energy ball): the weapon id it carries and the
    /// carried row that fired it. Null for anything else.
    /// </summary>
    private (TFWeaponId Id, string Row)? ProjectileShot(EntityHandle inflictor) => directory.Get(inflictor) switch
    {
        TF2.Server.Projectiles.TFRocket rocket => (rocket.Weapon, rocket.WeaponName),
        TF2.Server.Projectiles.TFArrow arrow => (arrow.Weapon, arrow.WeaponName),
        TF2.Server.Projectiles.TFFlare flare => (flare.Weapon, flare.WeaponName),
        TF2.Server.Projectiles.TFProjectileEnergyBall ball => (ball.Weapon, ball.WeaponName),
        _ => null,
    };

    /// <summary>
    /// <c>DeathNotice</c>'s item step (tf_gamerules.cpp:12905-12925): the
    /// scorer's weapon of the killing id (<c>Weapon_OwnsThisID</c>, here the row
    /// that dealt the blow) names the notice by its item's <c>item_iconname</c>
    /// and the log by its <c>item_logname</c>, each when the item carries one
    /// (the Huntsman's <c>huntsman</c>). Null with no scorer, no id or no item.
    /// </summary>
    private (string? Icon, string? Log)? ItemNames(EntityHandle scorer, TFWeaponId id, string row)
    {
        if (scorer == EntityHandle.None || id == TFWeaponId.None || row.Length == 0
            || owners.ItemOf(scorer.EntryIndex, row) is not AttributeContainer { Item: { } item })
            return null;

        static string? Named(string? value) => string.IsNullOrEmpty(value) ? null : value;
        return (Named(item.Keys.GetString("item_iconname", null)),
                Named(item.Keys.GetString("item_logname", null)));
    }

    /// <summary>
    /// The trigger arm's map hazard (:12431-12437), stock's
    /// <c>gpGlobals-&gt;mapname</c> read; null where the map names nothing, the
    /// arm leaving the name the chain began with.
    /// </summary>
    private string? MapHazard() => level.Map switch
    {
        "koth_cachoeira" => "helicopter",
        "koth_megaton" => "megaton",
        _ => null,
    };

    /// <summary>The prefixes stock strips from a killing weapon's name (:12540-12551), and its renames (:12553-12657).</summary>
    public static string StockName(string name)
    {
        // tf_weapon_grenade_ before tf_weapon_: the first prefix that matches is the one stripped.
        if (name.StartsWith("tf_weapon_grenade_", StringComparison.Ordinal))
            name = name["tf_weapon_grenade_".Length..];
        else if (name.StartsWith("tf_weapon_", StringComparison.Ordinal))
            name = name["tf_weapon_".Length..];
        else if (name.StartsWith("NPC_", StringComparison.Ordinal))
            name = name["NPC_".Length..];
        else if (name.StartsWith("func_", StringComparison.Ordinal))
            name = name["func_".Length..];

        return name switch
        {
            "tf_projectile_sentryrocket" => "obj_sentrygun3",
            "tf_projectile_healing_bolt" => "crusaders_crossbow",
            _ => name,
        };
    }

    /// <summary>
    /// <c>CTFGameRules::GetAssister</c> (:12690-12736): first answer wins —
    /// the victim's named assist, the scorer's medic healer (through no
    /// sentry inflictor), the giver of the scorer's buff conditions, the
    /// second recent damager when it is not the scorer, and for a sentry
    /// inflictor the gun's last teammate helper. Nobody for a suicide.
    /// </summary>
    private EntityHandle Assister(in DeathNoticeFacts death, EntityHandle scorer, float now)
    {
        // Nobody else for a suicide (:12697-12698).
        if (scorer == EntityHandle.None || scorer == death.Victim)
            return EntityHandle.None;

        // If an assist has been specified already, use it (:12701-12704).
        EntityHandle named = NamedAssist(death.Victim.EntryIndex);
        if (named != EntityHandle.None)
            return named;

        // The scorer's first healer, when that healer is a medic and the
        // inflictor is no sentry (:12706-12713): stock's Head() of the healer
        // array, so only the first healer answers and the rest of the array
        // waits behind it.
        foreach (int healerIndex in healing.HealerIndexes(scorer.EntryIndex))
        {
            if (owners.ClassOf(healerIndex) == TFClass.Medic
                && !(death.Inflictor != EntityHandle.None && directory.Get(death.Inflictor) is TFSentryGun))
                return players.GetHandle(healerIndex);
            break;
        }

        // The giver of a buff condition on the scorer (:12715-12718,
        // GetConditionAssistFromAttacker tf_player_shared.cpp:1319-1347): the
        // order-sensitive list, a giver who is the scorer themselves skipped.
        if (conditions.Of(scorer.EntryIndex) is { } buffed)
        {
            foreach (TFCond cond in new[] { TFCond.Offensebuff, TFCond.Defensebuff,
                                            TFCond.Regenondamagebuff, TFCond.Nohealingdamagebuff })
            {
                if (!buffed.Has(cond))
                    continue;
                EntityHandle giver = buffed.Provider(cond);
                if (giver != EntityHandle.None && giver != scorer)
                    return giver;
            }
        }

        // The second recent damager, when it is not the scorer (:12720-12724).
        EntityHandle recent = records.RecentDamager(death.Victim.EntryIndex, 1, TFCombatDefs.TimeAssistKill, now);
        if (recent != EntityHandle.None && recent != scorer)
            return recent;

        // The sentry's last teammate helper (:12726-12733), within the window.
        if (death.Inflictor != EntityHandle.None && directory.Get(death.Inflictor) is TFSentryGun gun)
            return gun.AssistingTeammate(TFCombatDefs.TimeAssistKill, now);

        return EntityHandle.None;
    }

    /// <summary>
    /// What <c>DetermineAssistForKill</c> (tf_player.cpp:10992-11021) latches
    /// onto the victim for <c>GetAssister</c>'s first arm: the giver of the
    /// first tracked victim-side condition, overridden by the control stun's
    /// stunner. Nobody for the scorer's own kill (:11012-11020), the gate the
    /// notice's cleanup answers alike.
    /// </summary>
    private EntityHandle NamedAssist(int victimIndex)
    {
        EntityHandle giver = EntityHandle.None;

        // GetConditionAssistFromVictim (tf_player_shared.cpp:1291-1314): the
        // first tracked cond names its giver, even when it names nobody.
        if (conditions.Of(victimIndex) is { } set)
        {
            foreach (TFCond cond in new[] { TFCond.Urine, TFCond.MadMilk, TFCond.Markedfordeath, TFCond.Gas })
            {
                if (!set.Has(cond))
                    continue;
                giver = set.Provider(cond);
                break;
            }
        }

        // The control stun's stunner outranks the conditions (:11006-11010).
        if (carriers.Part<TFStunStack>(victimIndex) is { } stack && stack.IsControlStunned())
            giver = stack.ActiveStun?.Attacker ?? EntityHandle.None;

        return giver;
    }
}

/// <summary>
/// A death's end of the damage layer (<see cref="IPlayerDeathHook"/>, after
/// the notice): the victim's records are cleared, stock's
/// <c>ClearHistories</c> on the next spawn (<c>tf_player.cpp:3720</c>).
/// </summary>
[SideService(Side.Server)]
public sealed class TFCombatDeaths(TFDamageRecords records, TFSentryDamages sentryDamages)
    : IPlayerDeathHook, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 704;

    public int HookOrder => Order;

    public void OnPlayerDied(PlayerDeath death)
    {
        records.Clear(death.Player.EntryIndex);
        sentryDamages.Clear(death.Player.EntryIndex);
    }
}
