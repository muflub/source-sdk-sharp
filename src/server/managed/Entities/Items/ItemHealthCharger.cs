//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The wall health charger, CNewWallHealth (item_healthkit.cpp:418): held
/// +USE gives a point of health a tenth of a second from its fifty, and an
/// empty one refills a minute later (CMultiplayRules::FlHealthChargerRechargeTime).
/// </summary>
[ScriptedEntity(EntityNames.ItemHealthcharger, ScriptedEntityKind.Prop)]
public sealed class ItemHealthCharger : WallCharger
{
    /// <summary>HEALTH_CHARGER_MODEL_NAME.</summary>
    public const string ModelName = "models/props_combine/health_charger001.mdl";

    /// <summary>sk_healthcharger (mod_hl2mp's cfg/skill.cfg).</summary>
    public const float FullJuice = 50f;

    public ItemHealthCharger(IEngineSound sound, IGameClock clock, IEngineEntityRender render)
        : base(sound, clock, render)
    {
    }

    /// <summary>OutRemainingHealth: the juice left, 0 to 1, after each charge.</summary>
    public readonly Output OutRemainingHealth = new();

    [NetState] public HealthChargerNetState State = new();

    protected override IChargerNetState Display => State;

    protected override string Model => ModelName;
    protected override float MaxJuice => FullJuice;
    protected override float RechargeDelay => 60f;
    protected override string DenySound => "WallHealth.Deny";
    protected override string StartSound => "WallHealth.Start";
    protected override string LoopSound => "WallHealth.LoopingContinueCharge";
    protected override string RechargeSound => "WallHealth.Recharge";

    protected override bool IsFull(EntityHandle player) => Entities.GetHealth(player) >= SsPlayer.SpawnHealth;

    /// <summary>TakeHealth( 1, DMG_GENERIC ): a point given, and only a given point spends juice.</summary>
    protected override void GiveCharge(EntityHandle player)
    {
        int health = Entities.GetHealth(player);
        if (health >= SsPlayer.SpawnHealth)
            return;

        Entities.SetHealth(player, health + 1);
        Juice--;
    }

    protected override void ReportRemaining(EntityHandle player)
        => OutRemainingHealth.Fire((Juice / FullJuice).ToString(System.Globalization.CultureInfo.InvariantCulture), player);
}

/// <summary>CNewWallHealth::Precache's model and sounds.</summary>
[Precache(PrecacheKind.Model, ItemHealthCharger.ModelName)]
[Precache(PrecacheKind.Sound, "WallHealth.Deny")]
[Precache(PrecacheKind.Sound, "WallHealth.Start")]
[Precache(PrecacheKind.Sound, "WallHealth.LoopingContinueCharge")]
[Precache(PrecacheKind.Sound, "WallHealth.Recharge")]
public static class HealthChargerAssets
{
}
