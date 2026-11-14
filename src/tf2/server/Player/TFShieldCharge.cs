//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Combat;

using TF2.Shared;

using TF2.Shared.Combat;

using TF2.Shared.Conditions;

using TF2.Shared.Econ;

using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>The demoman shield's charge, as the server decides it.</summary>
public interface ITFShieldCharge
{
    /// <summary>
    /// The shield's <c>DoSpecialAction</c> (tf_wearable_weapons.cpp:60-67):
    /// <c>CanCharge</c> passed, the charge begins. False when it does not —
    /// no shield worn, or a gate held.
    /// </summary>
    bool TryStart(int playerIndex);

    /// <summary>
    /// The shield's <c>EndSpecialAction</c> through the skill's demoman arm
    /// (<c>EndClassSpecialSkill</c>, tf_player_shared.cpp:12401-12404): the
    /// swing's wound-back — the grant, the meter and the removal. Nothing
    /// when the player wears no shield: the wearable's own arm is the
    /// player's, asked only once the shield is the worn one.
    /// </summary>
    void EndSpecialAction(int playerIndex);
}

/// <summary>
/// The demoman shield's charge start (<c>CTFWearableDemoShield</c>'s
/// <c>CanCharge</c> and <c>DoCharge</c>, tf_wearable_weapons.cpp:92-134): the
/// meter full, no gate held, and the condition taken for the charge's time.
/// The server's half of the skill — stock keeps the pair under
/// <c>GAME_DLL</c>, so the client's press mispredicts one command and the
/// condition's arrival is the news, the ride <c>TF_COND_SPEED_BOOST</c>
/// already takes (TFMaxSpeed's note).
/// </summary>
[SideService(Side.Server, As = [typeof(ITFShieldCharge)])]
[Precache(PrecacheKind.Sound, "DemoCharge.HitWorld")]
[Precache(PrecacheKind.Sound, "DemoCharge.HitFlesh")]
[Precache(PrecacheKind.Sound, "DemoCharge.HitFleshRange")]
public sealed class TFShieldCharge(TFPlayerCarriers carriers, IEntityDirectory directory,
                                   ItemSchema schema, IEngineEntities entities, IEnginePlayers players,
                                   IEngineWorldQuery world, IEngineDamage damage, IEngineSound sound,
                                   IScreenEffects screen, ILog log, AttributeHooks? hooks = null,
                                   ITFWeaponOwners? owners = null,
                                   TF2.Shared.Rules.ITFRoundStateView? rounds = null,
                                   TFDamageScopes? scopes = null)
    : ITFShieldCharge
{
    /// <summary>The demoshield's item class (<c>tf_wearable_demoshield</c>), the wearable the charge rides.</summary>
    public const string ShieldItemClass = "tf_wearable_demoshield";

    /// <summary>Stock's base, before the <c>mod_charge_time</c> hook (tf_wearable_weapons.cpp:128).</summary>
    public const float BaseChargeTime = 1.5f;

    /// <summary>The bash's reach along the eye forward, the <c>vecEnd</c> offset (tf_wearable_weapons.cpp:156).</summary>
    public const float BashReach = 48f;

    /// <inheritdoc/>
    public bool TryStart(int playerIndex)
    {
        // The worn shield: GetEquippedDemoShield's walk for the demoshield
        // item class (tf_player_shared.cpp:626-643). No shield, no skill —
        // stock's DoSpecialAction never ran either.
        if (EquippedShield(playerIndex) is null
            || carriers.ConditionsOf(playerIndex) is not { } conditions
            || carriers.Get(playerIndex) is not { } state)
            return false;

        // CanCharge (tf_wearable_weapons.cpp:92-120), in stock's order. The
        // ground gate under it is commented out in stock itself; the Passtime
        // and grapple arms wait for their modes (the deferred rows), neither
        // of which this build runs.
        if (rounds?.State == TF2.Shared.Rules.RoundState.Preround)            // :98-99
            return false;

        bool stunned = state.StunIndex >= 0 && state.InCond(TFCond.Stunned);
        if (stunned && ((TFStunFlags)state.StunFlags & TFStunFlags.LoserState) != 0)   // IsLoser, :101
            return false;

        if (stunned)                                                          // :104-105
            return false;

        if (state.InCond(TFCond.Taunting))                                    // IsTaunting, :107
            return false;

        if (state.ChargeMeter != 100f)                                        // :113-114
            return false;

        // DoCharge (:125-134): the condition for the charge's seconds, the
        // hook on the player as stock's ON_OTHER walk is; this build's player
        // manager carries the weapons (TFLoadoutProvider), so a
        // mod_charge_time worn on the shield is the deferred row's until the
        // wearables feed the manager.
        float chargeTime = hooks?.Float(BaseChargeTime, "mod_charge_time", owners?.ManagerOf(playerIndex))
                           ?? BaseChargeTime;
        conditions.Add(TFCond.ShieldCharge, chargeTime);
        carriers.Part<TFShieldChargeEnd>(playerIndex)?.ResetImpact();         // :132
        return true;
    }

    /// <inheritdoc/>
    public void EndSpecialAction(int playerIndex)
        => carriers.Part<TFShieldChargeEnd>(playerIndex)?.EndSpecialAction();

    /// <summary>
    /// The shield's <c>ShieldBash</c> (tf_wearable_weapons.cpp:139-206): the
    /// once-per-charge impact — a hull of the wearer's eye box over 48 units
    /// of eye forward, the flesh-or-world sound, the player hit's charge
    /// damage at the contact (TF_DMG_CUSTOM_CHARGE_IMPACT on DMG_CLUB, the
    /// force the shield's velocity times the damage), and the screen shake
    /// either way. The bash's own <c>CalcChargeCrit</c> (:200) is the end's
    /// grant, which <see cref="TFShieldChargeEnd.EndCharge"/> makes once at
    /// the same meter — the same call on the same inputs, stock's second
    /// writing over the first.
    /// </summary>
    internal void Bash(int playerIndex, float meter)
    {
        EntityHandle owner = players.GetHandle(playerIndex);                  // :141-143
        if (!entities.IsValid(owner))
            return;

        // The once-per-charge latch (m_bImpactedSomething, :147-150): the
        // end component keeps it, taken and set here.
        if (carriers.Part<TFShieldChargeEnd>(playerIndex) is not { } chargeEnd || chargeEnd.TakeImpact())
            return;

        Vector forward = Angles.Forward(entities.GetEyeAngles(owner));        // :153-154
        Vector start = players.GetShootOrigin(owner);                         // :155
        Vector end = start + forward * BashReach;                             // :156

        var half = new Vector(24f, 24f, 24f);
        SurfaceTraceResult trace = world.TraceBox(start, end, half * -1f, half, TraceMask.Solid, owner);  // :160-161

        if (trace.Hit)                                                        // :165
        {
            // The player test over the hit: the world and everything else
            // standing takes the world sound and no damage (:167-177).
            bool flesh = trace.Entity != EntityHandle.None && entities.IsPlayer(trace.Entity);
            sound.EntityEmitSound(owner, flesh ? "DemoCharge.HitFleshRange" : "DemoCharge.HitWorld");

            if (flesh)                                                        // :181
            {
                float bashDamage = ImpactDamage(playerIndex, meter);          // :183
                Vector force = DamageForce(playerIndex, bashDamage);          // :190
                using (scopes?.Push(TFDamageCustom.ChargeImpact, TFWeaponId.None, ShieldItemClass))
                    damage.ApplyAt(trace.Entity, owner, bashDamage, DamageType.Club, force, trace.End);  // :184-197
            }

            log.Msg($"[TF2/Charge] {owner}'s shield bash meter {meter:0} hit {(flesh ? $"player {trace.Entity}" : "the world")} at {trace.End}\n");
        }

        // Always, hit or not (:203): the center, 25 amplitude, 150 frequency,
        // one second, 750 radius, SHAKE_START.
        screen.Shake(entities.GetWorldSpaceCenter(owner), 25f, 150f, 1f, 750f, ShakeCommand.Start);
    }

    /// <summary>
    /// The impact damage (<c>CalculateChargeDamage</c>,
    /// tf_wearable_weapons.cpp:211-229): the meter remapped 90..40 to 15..50,
    /// up a tenth per decapitation past the first and capped at five, then
    /// the shield's <c>charge_impact_damage</c> — stock's ON_OTHER walk roots
    /// on the wearer, whose worn shield carries the attribute here.
    /// </summary>
    private float ImpactDamage(int playerIndex, float meter)
    {
        float impact = CritBucket.RemapValClamped(meter, 90f, 40f, 15f, 50f);   // :213

        if (carriers.Get(playerIndex) is not { } state)
            return impact;

        int decaps = Math.Min(state.Decapitations, 5);                        // :220
        if (decaps > 0)
            impact *= 1f + decaps * 0.1f;                                     // :222-224

        return hooks?.Float(impact, "charge_impact_damage", EquippedShield(playerIndex)) ?? impact;   // :226
    }

    /// <summary>
    /// The bash's knockback (<c>GetShieldDamageForce</c>,
    /// tf_wearable_weapons.cpp:234-245): the shield's velocity times the
    /// damage. Stock's physics-object arm normalizes inside its own branch
    /// and the none arm never does; the wearables here hold no physics
    /// object, so the velocity rides as the entity's own — unnormalized, as
    /// stock's none arm.
    /// </summary>
    private Vector DamageForce(int playerIndex, float damage)
    {
        EntityHandle shield = ShieldEntity(playerIndex);                      // :236
        Vector velocity = shield == EntityHandle.None ? default : entities.GetVelocity(shield);
        return velocity * damage;                                             // :244
    }

    /// <summary>
    /// The player's worn demoshield item, the attributes the charge's hooks
    /// and the bash's damage read — <c>GetEquippedDemoShield</c>
    /// (tf_player_shared.cpp:626-643) over this build's wearables.
    /// </summary>
    internal IAttributeProvider? EquippedShield(int playerIndex)
    {
        foreach (EntityHandle wearable in WearablesOf(playerIndex, ShieldItemClass))
            return ((TFWearableEntity)directory.Get(wearable)!).Item;

        return null;
    }

    /// <summary>The shield entity behind the player's worn one, the bash's inflictor.</summary>
    internal EntityHandle ShieldEntity(int playerIndex)
    {
        foreach (EntityHandle wearable in WearablesOf(playerIndex, ShieldItemClass))
            return wearable;

        return EntityHandle.None;
    }

    /// <summary>The player's worn items of one item class, in equip order.</summary>
    private IEnumerable<EntityHandle> WearablesOf(int playerIndex, string itemClass)
    {
        if (carriers.Part<TFWearables>(playerIndex) is not { } wearables)
            yield break;

        foreach (EntityHandle wearable in wearables.Wearables)
        {
            if (directory.Get(wearable) is TFWearableEntity entity
                && schema.Item(entity.State.ItemDefIndex)?.ItemClass == itemClass)
                yield return wearable;
        }
    }
}

/// <summary>
/// The charge meter's tick (<c>CTFPlayerShared::UpdateChargeMeter</c>,
/// tf_player_shared.cpp:6017-6065), the server's half of the shared think
/// (<c>ConditionThink</c>, :3118): the drain while the charge stands — the
/// meter empty ends it — and the refill while it does not, capped at full.
///
/// <para>
/// The frame is integrated as the ticks it carried, one <c>IntervalPerTick</c>
/// add each, because the client's replay
/// (<see cref="TF2.Client.Player.TFPlayerPrediction"/>'s charge tick) makes
/// exactly that add once per command: one add of a whole frame's span rounds
/// differently from the tick-sized adds it stands for, and the rounding is the
/// byte the prediction compare counts. A frame's tick count comes from the
/// clock the frame advanced, so the two sides make the same adds in the same
/// order and the chains agree bit for bit.
/// </para>
/// </summary>
public sealed class TFShieldChargeMeter(TFPlayerCarriers carriers, TFPlayerConditions conditions,
                                        TFPlayerItems items, AttributeHooks? hooks,
                                        TFDemomanChargeDrainTimeConVar? drainTime,
                                        TFDemomanChargeRegenRateConVar? regenRate,
                                        IEngineEnvironment? environment = null)
    : IPlayerComponent
{
    private EntityHandle _player = EntityHandle.None;

    private float _last = float.NaN;

    /// <summary>After the condition ticker (20): the drain runs on the words the frame's expiry left.</summary>
    public int Order => 21;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _last = float.NaN;
    }

    public void Tick(float now)
    {
        if (carriers.Get(_player.EntryIndex) is not { } state
            || carriers.ConditionsOf(_player.EntryIndex) is not { } set
            || (TFClass)state.PlayerClass != TFClass.Demoman)                 // :6019-6020
            return;

        float frame = float.IsNaN(_last) ? 0f : now - _last;
        _last = now;
        if (frame <= 0f)
            return;

        // No engine, no tick to slice by: the whole frame is one add, the
        // shape the no-engine caller (a test) stands for.
        float interval = environment?.Globals is { IsAvailable: true } globals
            ? globals.IntervalPerTick
            : 0f;
        int slices = interval > 0f ? Math.Max(1, (int)MathF.Round(frame / interval)) : 1;
        float dt = interval > 0f ? interval : frame;

        for (int slice = 0; slice < slices; slice++)
        {
            if (set.Has(TFCond.ShieldCharge))                                 // :6022
            {
                // The drain: the seconds a full meter lasts, the hook on the
                // player as stock's ON_OTHER walk is (:6025-6026).
                float drain = hooks?.Float(drainTime?.Value ?? 1.5f, "mod_charge_time", items.ManagerOf(_player.EntryIndex))
                              ?? drainTime?.Value ?? 1.5f;
                state.ChargeMeter -= dt * (100f / drain);                     // :6027-6028
                if (state.ChargeMeter <= 0f)                                  // :6029
                {
                    state.ChargeMeter = 0f;                                   // :6031
                    conditions.Remove(_player, TFCond.ShieldCharge);          // :6032
                }
            }
            else if (state.ChargeMeter < 100f)                                // :6037
            {
                // The refill: per second, the hook on the player (:6040-6041),
                // at least a point (:6052), capped at full (:6054-6057). The
                // powerup-mode multipliers (:6042-6050) wait for that mode.
                float regen = hooks?.Float(regenRate?.Value ?? 8.3f, "charge_recharge_rate", items.ManagerOf(_player.EntryIndex))
                              ?? regenRate?.Value ?? 8.3f;
                regen = MathF.Max(regen, 1f);                                 // :6052
                state.ChargeMeter = MathF.Min(100f, state.ChargeMeter + dt * regen);  // :6053-6057
            }
        }
    }

    public void Detach() => _player = EntityHandle.None;
}

/// <summary>
/// The charge's end (<c>CTFPlayerShared::EndCharge</c>,
/// tf_player_shared.cpp:6070-6095) and its crit (<c>CalcChargeCrit</c>,
/// :6125-6146, and <c>CTFPlayer::RemoveMeleeCrit</c>'s :6221-6231), as one
/// per-player component: the walk's two asks land here through the player
/// data's <c>EndCharge</c>, the bash on <see cref="TFShieldCharge"/> runs at
/// the meter's word, and the granted crit expires 0.3 s later on this
/// component's tick — stock's <c>SetContextThink</c>, the player think the
/// grant schedules.
///
/// <para>
/// The duration zero (:6089-6091) is the whole removal story: the condition
/// think removes it next pass and <see cref="TFPlayerConditionEffectArms"/>
/// zeroes the meter on the removal, exactly stock's prediction note. The
/// crit's 0.3 s window is where stock's comment puts the swing — granted at
/// the end, spent by the melee helpers, gone at the smack or here.
/// </para>
/// </summary>
public sealed class TFShieldChargeEnd(TFPlayerCarriers carriers, TFShieldCharge? shields,
                                      AttributeHooks? hooks, IGameClock clock) : IPlayerComponent
{
    private EntityHandle _player = EntityHandle.None;

    private int _index = -1;

    /// <summary>When the granted crit expires; nothing pending while NaN.</summary>
    private float _critExpire = float.NaN;

    /// <summary>After the charge meter (21): the expiry follows the grants the frame's ends made.</summary>
    public int Order => 22;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _critExpire = float.NaN;
        carriers.EnterPart(_index, this);
    }

    /// <summary><c>CTFPlayer::RemoveMeleeCrit</c> at its scheduled time.</summary>
    public void Tick(float now)
    {
        if (_index < 0 || float.IsNaN(_critExpire) || now < _critExpire)
            return;

        if (carriers.Get(_index) is { } state)
            state.NextMeleeCrit = (int)TFMeleeCrit.NoCrit;                    // :6223

        _critExpire = float.NaN;
    }

    /// <summary>
    /// <c>EndCharge</c> (:6070-6095): in the charge and under 90 meter, the
    /// bash, the crit and the duration zero. Over 90 — still charging hard —
    /// stock's gate answers and nothing runs.
    /// </summary>
    public void EndCharge()
    {
        if (carriers.Get(_index) is not { } state || !state.InCond(TFCond.ShieldCharge))
            return;                                                           // :6072-6073

        if (state.ChargeMeter >= 90f)                                         // :6076
            return;

        float meter = state.ChargeMeter;                                      // :6079
        shields?.Bash(_index, meter);                                         // :6081-6085
        GrantCrit(meter);                                                     // :6087
        carriers.ConditionsOf(_index)?.SetDuration(TFCond.ShieldCharge, 0f);  // :6091
    }

    /// <summary>
    /// The shield's <c>EndSpecialAction</c> (tf_wearable_weapons.cpp:72-87),
    /// the swing's end: the charge's own <c>attack_not_cancel_charge</c>
    /// refuses (:74-79, the attribute on the worn shield); in the charge, the
    /// grant, the meter and the removal at once — stock's three lines, the
    /// removal now rather than the duration's zero.
    /// </summary>
    public void EndSpecialAction()
    {
        if (_index < 0 || carriers.Get(_index) is not { } state)
            return;

        if ((hooks?.Int(0, "attack_not_cancel_charge", shields?.EquippedShield(_index)) ?? 0) > 0)
            return;                                                           // :74-79

        if (!state.InCond(TFCond.ShieldCharge))
            return;                                                           // :81

        GrantCrit(state.ChargeMeter);                                         // :83
        state.ChargeMeter = 0f;                                               // :84
        carriers.ConditionsOf(_index)?.Remove(TFCond.ShieldCharge);           // :85
    }

    /// <summary>The once-per-charge impact latch, read and set by the bash.</summary>
    internal bool TakeImpact()
    {
        bool was = _impacted;
        _impacted = true;
        return was;
    }

    /// <summary>The latch reset, as the charge starts (tf_wearable_weapons.cpp:132).</summary>
    internal void ResetImpact() => _impacted = false;

    private bool _impacted;

    /// <summary>
    /// <c>CalcChargeCrit</c> (:6125-6146): the TideTurner's
    /// <c>lose_demo_charge_on_damage_when_charging</c> mini-crits at 75 or
    /// under; else 40 or under crits and 75 or under mini-crits, stock's
    /// ladder with no else — over 75 without the TideTurner grants nothing.
    /// The crit is the player's <c>m_iNextMeleeCrit</c>, and the 0.3 s
    /// think is scheduled with the grant (:6144).
    /// </summary>
    private void GrantCrit(float meter)
    {
        if (carriers.Get(_index) is not { } state)
            return;

        int tideTurner = hooks?.Int(0, "lose_demo_charge_on_damage_when_charging", shields?.EquippedShield(_index)) ?? 0;   // :6129-6130

        if (tideTurner != 0 && meter <= 75f)                                  // :6131
            state.NextMeleeCrit = (int)TFMeleeCrit.MiniCrit;                  // :6133
        else if (meter <= 40f)                                                // :6135
            state.NextMeleeCrit = (int)TFMeleeCrit.Crit;                      // :6137
        else if (meter <= 75f)                                                // :6139-6141
            state.NextMeleeCrit = (int)TFMeleeCrit.MiniCrit;

        _critExpire = clock.Now + 0.3f;                                       // :6144
    }

    public void Detach()
    {
        carriers.LeavePart(_index, this);
        _player = EntityHandle.None;
        _index = -1;
        _critExpire = float.NaN;
    }
}
