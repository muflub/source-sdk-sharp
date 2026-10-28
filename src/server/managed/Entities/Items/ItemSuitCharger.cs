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
/// The wall suit charger, CNewRecharge (func_recharge.cpp:334): held +USE
/// gives a point of armour a tenth of a second from its seventy-five, and an
/// empty one refills thirty seconds later (CMultiplayRules::FlHEVChargerRechargeTime).
/// The citadel charger holds 200, charges to 200 armour two points at a time
/// in HL2MP, tops health up as well and waits twice as long; Kleiner's holds 25.
/// </summary>
[ScriptedEntity(EntityNames.ItemSuitcharger, ScriptedEntityKind.Prop)]
public sealed class ItemSuitCharger : WallCharger
{
    /// <summary>The suit charger's model (func_recharge.cpp's HEALTH_CHARGER_MODEL_NAME).</summary>
    public const string ModelName = "models/props_combine/suit_charger001.mdl";

    /// <summary>sk_suitcharger (mod_hl2mp's cfg/skill.cfg).</summary>
    public const float FullJuice = 75f;

    /// <summary>sk_suitcharger_citadel and sk_suitcharger_citadel_maxarmor.</summary>
    public const float CitadelJuice = 200f;
    public const int CitadelMaxArmour = 200;

    /// <summary>SF_KLEINER_RECHARGER's juice.</summary>
    public const float KleinerJuice = 25f;

    /// <summary>SF_CITADEL_RECHARGER.</summary>
    private const int CitadelFlag = 0x2000;

    /// <summary>SF_KLEINER_RECHARGER.</summary>
    private const int KleinerFlag = 0x4000;

    public ItemSuitCharger(IEngineSound sound, IGameClock clock, IEngineEntityRender render)
        : base(sound, clock, render)
    {
    }

    public readonly Output OutRemainingCharge = new();
    public readonly Output OnHalfEmpty = new();
    public readonly Output OnEmpty = new();
    public readonly Output OnFull = new();

    /// <summary>m_iMaxJuice: the spawn flags' charge, or what SetCharge last said.</summary>
    [SaveField] private float _maxJuice;

    private bool Citadel => (SpawnFlags & CitadelFlag) != 0;

    protected override string Model => ModelName;
    protected override float MaxJuice => _maxJuice;
    protected override float RechargeDelay => Citadel ? 60f : 30f;
    protected override float DisplayChargesPerSecond => Citadel ? 10f / ChargeRate : ChargesPerSecond;
    protected override string DenySound => "SuitRecharge.Deny";
    protected override string StartSound => "SuitRecharge.Start";
    protected override string LoopSound => "SuitRecharge.ChargingLoop";
    protected override string RechargeSound => "SuitRecharge.Start";

    private int MaxArmour => Citadel ? CitadelMaxArmour : SsPlayer.MaxArmour;

    public override void OnSpawn()
    {
        // SetInitialCharge.
        int flags = SpawnFlags;
        _maxJuice = (flags & KleinerFlag) != 0 ? KleinerJuice
                  : (flags & CitadelFlag) != 0 ? CitadelJuice
                  : FullJuice;

        base.OnSpawn();
    }

    /// <summary>
    /// IsSuitEquipped: every HL2MP player wears the suit (CHL2MP_Player::Spawn
    /// equips it), and so does every player here.
    /// </summary>
    protected override bool CanUse(EntityHandle player) => FindByHandle<SsPlayer>(player) is not null;

    /// <summary>Full armour; the citadel charger also wants full health.</summary>
    protected override bool IsFull(EntityHandle player)
    {
        if (FindByHandle<SsPlayer>(player) is not { } p || p.Armour < MaxArmour)
            return false;

        return !Citadel || Entities.GetHealth(player) >= SsPlayer.SpawnHealth;
    }

    protected override void GiveCharge(EntityHandle player)
    {
        if (FindByHandle<SsPlayer>(player) is not { } p)
            return;

        // The citadel charger gives health as well (five a charge).
        if (Citadel && Entities.GetHealth(player) < SsPlayer.SpawnHealth)
            Entities.SetHealth(player, Math.Min(SsPlayer.SpawnHealth, Entities.GetHealth(player) + 5));

        int increment = Citadel ? 2 : 1;
        if (p.Armour < MaxArmour)
        {
            UpdateJuice(Juice - increment);
            p.Armour = Math.Min(p.Armour + increment, MaxArmour);
        }
    }

    protected override void ReportRemaining(EntityHandle player)
        => OutRemainingCharge.Fire((Juice / MaxJuice).ToString(System.Globalization.CultureInfo.InvariantCulture), player);

    protected override void RefillTo(int juice) => UpdateJuice(juice);

    /// <summary>
    /// UpdateJuice: the half-way and empty outputs on the way down, the full
    /// one on the way back up.
    /// </summary>
    private void UpdateJuice(int newJuice)
    {
        if (newJuice < Juice)
        {
            int half = (int)(MaxJuice * 0.5f);
            if (newJuice <= half && Juice > half)
                OnHalfEmpty.Fire();

            if (newJuice <= 0)
                OnEmpty.Fire();
        }
        else if (newJuice != Juice && newJuice == (int)MaxJuice)
        {
            OnFull.Fire();
        }

        Juice = newJuice;
    }

    [ScriptedEvent("Recharge")] private void OnRechargeInput() => Recharge();

    /// <summary>InputSetCharge: full again at a new size.</summary>
    [ScriptedEvent("SetCharge")]
    private void OnSetChargeInput(float charge)
    {
        _maxJuice = (int)charge;
        Juice = (int)charge;
        ShowFull();
    }
}

/// <summary>CNewRecharge::Precache's model and sounds.</summary>
[Precache(PrecacheKind.Model, ItemSuitCharger.ModelName)]
[Precache(PrecacheKind.Sound, "SuitRecharge.Deny")]
[Precache(PrecacheKind.Sound, "SuitRecharge.Start")]
[Precache(PrecacheKind.Sound, "SuitRecharge.ChargingLoop")]
public static class SuitChargerAssets
{
}
