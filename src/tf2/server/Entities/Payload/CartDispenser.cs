//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Server.Entities;

/// <summary>
/// mapobj_cart_dispenser: the dispenser that rides the payload cart (stock's
/// <c>CMapobjCartDispenser</c> over <c>CObjectDispenser</c>'s dispense pass,
/// tf_obj_dispenser.cpp), pared to what the cart is for — healing and resupply
/// to the cart's team within the dispenser radius. No builder, no model, no
/// upgrade levels: the cart's dispenser is level 1 and its metal store is not
/// tracked, so the metal is stock's DISPENSER_DROP_METAL flat.
/// </summary>
[ScriptedEntity(MapClass)]
public sealed class CartDispenser(
    IGameClock clock,
    IEngineWorldQuery world,
    ITFPlayerResupply resupply,
    ICapturePlayers capturePlayers) : ScriptedEntity
{
    public const string MapClass = "mapobj_cart_dispenser";

    /// <summary>DISPENSE_CONTEXT's cadence.</summary>
    public const float DispenseThinkTime = 0.1f;

    /// <summary>GetDispenserRadius's base, for a level-1 dispenser with no builder.</summary>
    public const float DispenserRadius = 64f;

    /// <summary>g_flDispenserHealRates[1]: the cart's dispenser is level 1.</summary>
    public const float HealRate = 10f;

    /// <summary>g_flDispenserAmmoRates[1].</summary>
    public const float AmmoRate = 0.2f;

    /// <summary>DISPENSER_DROP_METAL.</summary>
    public const int DropMetal = 40;

    /// <summary>The ammo cadence, and the quick one when nobody received.</summary>
    public const float NextAmmoDelay = 1.0f;

    public const float QuickAmmoDelay = 0.1f;

    /// <summary>What a map may set on this entity. See <see cref="CartDispenserVars"/>.</summary>
    [ScriptVars] public CartDispenserVars Vars = new();

    private float _nextAmmoDispense;

    /// <summary>The brush the map named as the serve area, if any (stock's m_hTouchTrigger).</summary>
    private DispenserTouchTrigger? _touchTrigger;

    /// <summary>The team the cart's dispenser serves; stock's constructor arms it blue.</summary>
    public int Team => Vars.TeamNum;

    public override void OnSpawn() => SetNextThink(DispenseThinkTime);

    /// <summary>Stock's OnGoActive: a map-named brush becomes the serve area.</summary>
    public override void OnServerActivate()
    {
        // A trigger the map named but did not create leaves the radius sphere
        // standing in (stock falls through to creating its own brush, :422-439).
        if (!string.IsNullOrEmpty(Vars.TouchTrigger))
            _touchTrigger = FindByName<DispenserTouchTrigger>(Vars.TouchTrigger);
    }

    public override void OnThink() => DispenseThink();

    /// <summary>DispenseThink: heal and resupply every teammate the serve list holds.</summary>
    public void DispenseThink()
    {
        bool playerReceivedAmmo = false;

        void Serve(EntityHandle entity)
        {
            if (!capturePlayers.IsPlayer(entity) || !capturePlayers.IsAlive(entity))
                return;

            if (capturePlayers.Team(entity) != Team)
                return;

            // heal: the level-1 rate, applied for the tenth of a second since
            // the last think
            resupply.TakeHealth(entity, HealRate * DispenseThinkTime);

            // handle ammo
            if (clock.Now >= _nextAmmoDispense)
            {
                if (DispenseAmmo(entity))
                    playerReceivedAmmo = true;
            }
        }

        if (_touchTrigger is not null)
        {
            // the map's brush names who is served; a dead or departed entry
            // just fails the alive check (stock purges m_hTouchingEntities
            // because EndTouch can be flakey)
            foreach (EntityHandle entity in _touchTrigger.TouchingEntities)
                Serve(entity);
        }
        else
        {
            Span<EntityHandle> nearby = stackalloc EntityHandle[64];
            int found = world.EntitiesInSphere(Origin, DispenserRadius, nearby);

            for (int i = 0; i < found; i++)
                Serve(nearby[i]);
        }

        if (clock.Now >= _nextAmmoDispense)
        {
            // Try to dispense more often when no players are around so we
            // give it as soon as possible when a new player shows up
            _nextAmmoDispense = clock.Now + (playerReceivedAmmo ? NextAmmoDelay : QuickAmmoDelay);
        }

        SetNextThink(DispenseThinkTime);
    }

    /// <summary>DispenseAmmo: primary, secondary, then metal (the cart's rate, no attribs).</summary>
    private bool DispenseAmmo(EntityHandle player)
    {
        int totalPickedUp = 0;

        int ammoToAdd = (int)(resupply.MaxAmmo(player, TFAmmo.Primary) * AmmoRate);
        totalPickedUp += resupply.GiveAmmo(player, ammoToAdd, TFAmmo.Primary);

        ammoToAdd = (int)(resupply.MaxAmmo(player, TFAmmo.Secondary) * AmmoRate);
        totalPickedUp += resupply.GiveAmmo(player, ammoToAdd, TFAmmo.Secondary);

        totalPickedUp += resupply.GiveAmmo(player, DropMetal, TFAmmo.Metal);

        // return true if we picked up anything
        return totalPickedUp > 0;
    }

}

/// <summary>What a map may set on mapobj_cart_dispenser.</summary>
[ScriptVars]
public partial class CartDispenserVars
{
    /// <summary>The cart's dispenser serves the cart's team; stock's cart is blue.</summary>
    public int TeamNum { get; set; } = (int)TFTeam.Blue;

    /// <summary>A brush volume naming who is served; none means the radius sphere (m_iszCustomTouchTrigger).</summary>
    [VarName("touch_trigger")]
    public string TouchTrigger { get; set; } = string.Empty;
}
