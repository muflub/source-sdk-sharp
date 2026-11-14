//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Weapons;

/// <summary>
/// The server half of the lunchbox family (<c>CTFLunchBox</c>'s GAME_DLL arms,
/// shared/tf/tf_weapon_lunchbox.cpp): the eat's taunt ask, the bite's effects
/// (<c>ApplyBiteEffects</c>, :381-432) and the thrown plate. The behaviour on
/// both sides decides when an eat or throw may run and what it costs; this is
/// the side that touches the player.
///
/// <para>
/// Recorded with the bite's small arms: <c>SetBroken</c> (no consumer for the
/// broken state), the steak's <c>SetBiteEffectWasApplied</c> (read by the
/// achievements this build has not), the Detach attribute removal (:348-362,
/// the duration here is class-switch hygiene enough) and the eat-finish
/// <c>ATE_FOOD</c> line (:18568-18583, the expresser force the speech side
/// owns) — each waits for its consumer.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFLunchboxEffects)])]
public sealed class TFLunchboxWeapon(ITFMapPlayers players, ITFPlayerResupply resupply, TFPlayerCarriers carriers,
                                     ITFWeaponOwners owners, AttributeHooks attributes,
                                     TFTauntCommand taunts, IEngineEntities entities, IEnginePlayers enginePlayers,
                                     IEngineEntityRender render, IEntityDirectory directory,
                                     TFScoutEnergydrinkConsumeRateConVar convar,
                                     Arsenal arsenal) : ITFLunchboxEffects
{
    /// <summary>The Dalokos buff, thirty seconds of it (:387, :392).</summary>
    public const float MaxHealthBuff = 50f;

    /// <summary>The buff's lifespan (:392).</summary>
    public const float MaxHealthBuffSeconds = 30f;

    /// <summary>The steak's run of minicrits (:396).</summary>
    public const float SteakBuffSeconds = 16f;

    /// <summary>The ceiling the chocolate modes heal past, plus the buff (:409's <c>300.f + DALOKOHS_MAXHEALTH_BUFF</c>).</summary>
    public const int OverhealCeiling = 350;

    /// <summary>The plain bite's heal; the chocolate modes' is a quarter of it (:407).</summary>
    public const int BiteHeal = 75;

    /// <summary>
    /// The eat (<c>CTFLunchBox::PrimaryAttack</c>'s <c>pOwner->Taunt()</c>,
    /// :182): the whole taunt pipeline — the speak, the condition, the heavy's
    /// bite clock — the <see cref="TFTauntCommand"/> already runs for the
    /// taunt button, asked with the weapon taunt that binds to.
    /// </summary>
    public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script)
    {
        if (attack.Command.OwnerIndex <= 0)
            return;

        taunts.Handle(new EntityHandle((uint)attack.Command.OwnerIndex), TFTauntCommand.Command, default);
    }

    /// <summary>
    /// One bite (<c>CTFLunchBox::ApplyBiteEffects</c> through the heavy's eat
    /// arm, tf_player.cpp:18555-18584): the mode's buff, then the heal — the
    /// chocolate modes a quarter of it under their ceiling past the buff, the
    /// steak only its conds.
    /// </summary>
    public void Bite(int playerIndex)
    {
        var player = new EntityHandle((uint)playerIndex);
        var item = owners.ItemOf(playerIndex, LunchboxName);

        // GetLunchboxType (tf_weapon_lunchbox.h:63): the held item's mode.
        int mode = (int)attributes.Float(0f, TFLunchboxBehaviour.ModeAttribute, item);

        // The buffed health rides the player's own attribute layer for its
        // thirty seconds (:392) — the same hidden maxhealth the resupply's
        // walk reads. Stock's Detach removal (:348-362) is the class-switch
        // hygiene its duration here already is.
        if (mode is TFLunchboxBehaviour.ModeChocolateBar or TFLunchboxBehaviour.ModeFishcake)
            carriers.Part<TFPlayerAttributes>(playerIndex)?.Add(HiddenMaxHealthAttribute, MaxHealthBuff, MaxHealthBuffSeconds);
        else if (mode == TFLunchboxBehaviour.ModeAddsMinicrits)
        {
            // The steak: minicrits and the melee lock, no heal (:394-404).
            players.AddCond(player, TFCond.EnergyBuff, SteakBuffSeconds);
            players.AddCond(player, TFCond.CannotSwitchFromMelee, SteakBuffSeconds);
            return;
        }

        // The heal (:406-419): the chocolate modes' quarter-heal rides
        // DMG_IGNORE_MAXHEALTH under the 350 ceiling — the bite's own buff
        // included — and the held item's healing scale scales it.
        float heal = mode is TFLunchboxBehaviour.ModeChocolateBar or TFLunchboxBehaviour.ModeFishcake
            ? ChocolateHeal : BiteHeal;
        bool ignoreMaxHealth = false;
        if (mode is TFLunchboxBehaviour.ModeChocolateBar or TFLunchboxBehaviour.ModeFishcake
            && players.Health(player) < OverhealCeiling)
        {
            ignoreMaxHealth = true;
            heal = MathF.Min(heal, OverhealCeiling - players.Health(player));
        }

        heal *= attributes.Float(1f, HealingScaleAttribute, item);
        resupply.TakeHealth(player, heal, ignoreMaxHealth);

        // The ammo mode tops the primary pool by a quarter of its maximum
        // (:426-431), taken whether or not the heal landed.
        if (mode == TFLunchboxBehaviour.ModeAddsAmmo)
            resupply.GiveAmmo(player, (int)(resupply.MaxAmmo(player, TFAmmo.Primary) * AmmoShare), TFAmmo.Primary);
    }

    /// <summary>
    /// The scout's drink (<c>DoTauntAttack</c>'s scout arm, tf_player.cpp:18636-18675):
    /// the stun gate (:18638), then the held drink's mode — Crit-a-Cola's
    /// minicrit buff (:18647-18649), Bonk!'s phase and quick feet
    /// (:18651-18653) — each held for the meter's fall-back dead time
    /// (:18644), then the hand back where it was (:18672). The flag drop that
    /// rides the phase in stock (:18655-18669) has no carrier this build has:
    /// no intelligence to carry (docs/divergences.md).
    /// </summary>
    public void Drink(int playerIndex)
    {
        var player = new EntityHandle((uint)playerIndex);

        // The stun gate (:18638): IsControlStunned reads here as the stunned
        // condition, the way the movement rules and the criteria words read it.
        if (players.InCond(player, TFCond.Stunned))
            return;

        var item = owners.ItemOf(playerIndex, DrinkName);

        // GetLunchboxType (tf_weapon_lunchbox.h:63): the held item's mode.
        int mode = (int)attributes.Float(0f, TFLunchboxBehaviour.ModeAttribute, item);

        // The fall-back dead time (:18644): a hundred of meter over the
        // drain rate, plus the one — normally over in the meter's own seconds.
        float seconds = DrinkSeconds(convar.Value);

        if (mode == TFLunchboxBehaviour.ModeAddsMinicrits)
        {
            // Crit-a-Cola: minicrits, no phase (:18647-18649).
            players.AddCond(player, TFCond.EnergyBuff, seconds);
        }
        else
        {
            // Bonk!: the phase (:18651-18653) — no harm lands while it holds
            // (tf_player.cpp:9330, the arm the damage rules keep).
            players.AddCond(player, TFCond.Phase, seconds);
        }

        arsenal.SwitchLast(enginePlayers.GetHandle(playerIndex));
    }

    /// <summary>Stock's fall-back dead time (:18644): <c>(100 / rate) + 1</c>.</summary>
    public static float DrinkSeconds(float consumeRate) => 100f / consumeRate + 1f;

    /// <summary>The convar's default (<c>tf_scout_energydrink_consume_rate</c>, tf_player_shared.cpp:173).</summary>
    public const float DefaultDrinkSeconds = 12.5f;

    /// <summary>
    /// The throw (<c>CTFLunchBox::SecondaryAttack</c>, tf_weapon_lunchbox.cpp:195-295):
    /// the health pack the sandvich becomes, heaved down the eye's line to
    /// land at the owner's feet — a <see cref="HealthKit"/> shaped and flung
    /// by <see cref="HealthKit.DropSingleInstance"/>, any plate still out
    /// from the last throw replaced.
    /// </summary>
    public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode)
    {
        if (attack.Command.OwnerIndex <= 0)
            return;

        var player = enginePlayers.GetHandle(attack.Command.OwnerIndex);

        // The last plate goes first (:209-212): m_hThrownPowerup removed.
        RemoveThrown(player);

        // Under the eye, a little low (:214-215), aimed a little down (:216).
        Vector origin = entities.GetEyePosition(player) + new Vector(0f, 0f, DropDrop);
        Vector angles = entities.GetEyeAngles(player) + new Vector(DropPitch, 0f, 0f);
        Vector launch = Angles.Vectors(angles).Forward * ThrownSpeed; // :278

        string className = ThrownClass(mode);
        EntityHandle created = entities.Create(className);
        if (created == EntityHandle.None)
            return;

        entities.SetOrigin(created, origin);
        entities.Spawn(created);

        if (directory.Get(created) is not HealthKit kit)
            return;

        // The plate, on the spawned kit — its spawn model is the medkit's
        // (TFPowerup.Model), the thrown shape laid over it (TFDroppedWeapon's
        // precedent); the hull the spawn built stands (recorded with the
        // throw, docs/plans/tf2/deferred.tsv).
        render.SetModel(created, ThrownModel(mode)); // :247-275
        if (HasThrownSkin(mode))
            render.SetSkin(created, players.Team(player) == (int)TFTeam.Red ? 0 : 1); // :266 et al

        kit.DropSingleInstance(launch, player, ThrownOwnerWait); // :284

        entities.SetAngles(created, Vector.Zero); // :281, level
        entities.SetBounds(created, DroppedMins, DroppedMaxs); // :282
    }

    /// <summary>
    /// The thrower's earlier plate, gone: every thrown kit out there that
    /// names them as owner (stock's single <c>m_hThrownPowerup</c> — at most
    /// one throw in flight per player, so the scan is the field).
    /// </summary>
    private void RemoveThrown(EntityHandle player)
    {
        foreach (string className in ThrownClasses)
        {
            foreach (HealthKit kit in directory.OfClass(className).OfType<HealthKit>().ToList())
            {
                if (kit.ThrownSingleInstance && kit.OwnerEntity == player)
                    entities.Remove(kit.Handle);
            }
        }
    }

    /// <summary>The thrown kit's classname (<c>pszEntClass</c>, :218-232): the
    /// small packs for the small sandviches, the ammo kit for the steak's
    /// cousin, the medium otherwise.</summary>
    private static string ThrownClass(int mode) => mode switch
    {
        TFLunchboxBehaviour.ModeChocolateBar or TFLunchboxBehaviour.ModeBanana or TFLunchboxBehaviour.ModeFishcake
            => HealthKitSmall.SmallClass,
        TFLunchboxBehaviour.ModeAddsAmmo => HealthAmmoKit.MapClass,
        _ => HealthKitMedium.MediumClass,
    };

    /// <summary>The plate on the ground (<c>pszModel</c>, :234-247).</summary>
    private static string ThrownModel(int mode) => mode switch
    {
        TFLunchboxBehaviour.ModeChocolateBar => ChocolatePlate,
        TFLunchboxBehaviour.ModeAddsMinicrits => SteakPlate,
        TFLunchboxBehaviour.ModeStandardRobo => RoboPlate,
        TFLunchboxBehaviour.ModeStandardFestive => FestivePlate,
        TFLunchboxBehaviour.ModeBanana => BananaPlate,
        TFLunchboxBehaviour.ModeFishcake => FishcakePlate,
        _ => PlainPlate, // :249's default
    };

    /// <summary>The teams' skins, on the modes whose plate carries one (:266-274).</summary>
    private static bool HasThrownSkin(int mode)
        => mode is TFLunchboxBehaviour.ModeStandardRobo or TFLunchboxBehaviour.ModeStandardFestive
        or TFLunchboxBehaviour.ModeChocolateBar or TFLunchboxBehaviour.ModeFishcake;

    /// <summary><c>tf_weapon_lunchbox</c>, the row the item's attributes hang on.</summary>
    private static string LunchboxName => TFWeaponNames.Name(TFWeaponId.Lunchbox).ToLowerInvariant();

    /// <summary>The drink's own entity class, the row its items' attributes hang on.</summary>
    private static string DrinkName => TFWeaponTypes.Drink;

    private const string HiddenMaxHealthAttribute = "hidden maxhealth non buffed";

    private const string HealingScaleAttribute = "lunchbox_healing_scale";

    /// <summary>The chocolate bite's heal, a quarter of the plain's (:407).</summary>
    private const int ChocolateHeal = 25;

    /// <summary>The ammo mode's share of the primary maximum (:430's 0.25).</summary>
    private const float AmmoShare = 0.25f;

    /// <summary>The throw's speed down its line (:278's 500).</summary>
    private const float ThrownSpeed = 500f;

    /// <summary>How long the thrower waits to take their own plate back (:286's 0.3).</summary>
    private const float ThrownOwnerWait = 0.3f;

    /// <summary>The spawn's dip below the eye (:215's −8).</summary>
    private const float DropDrop = -8f;

    /// <summary>The throw's extra downward pitch (:216's −10).</summary>
    private const float DropPitch = -10f;

    /// <summary>The thrown plate's box (<c>LUNCHBOX_DROPPED_MINS</c>/<c>MAXS</c>, :37-38).</summary>
    private readonly Vector DroppedMins = new(-17f, -17f, -10f);

    private readonly Vector DroppedMaxs = new(17f, 17f, 10f);

    /// <summary>The classnames a throw creates — the scan behind the last-plate removal.</summary>
    private readonly string[] ThrownClasses =
        [HealthKitSmall.SmallClass, HealthKitMedium.MediumClass, HealthAmmoKit.MapClass];

    /// <summary>The plates (<c>LUNCHBOX_DROP_MODEL</c> and cousins, :65-74).</summary>
    private const string PlainPlate = "models/items/plate.mdl";

    private const string SteakPlate = "models/workshop/weapons/c_models/c_buffalo_steak/plate_buffalo_steak.mdl";

    private const string RoboPlate = "models/items/plate_robo_sandwich.mdl";

    private const string FestivePlate = "models/items/plate_sandwich_xmas.mdl";

    private const string ChocolatePlate = "models/workshop/weapons/c_models/c_chocolate/plate_chocolate.mdl";

    private const string BananaPlate = "models/items/banana/plate_banana.mdl";

    private const string FishcakePlate = "models/workshop/weapons/c_models/c_fishcake/plate_fishcake.mdl";
}
