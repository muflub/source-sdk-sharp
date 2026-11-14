//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using TF2.Server.Combat;
using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Rooms;
using TF2.Shared.Econ;

namespace TF2.Server.Player;

/// <summary>Stage 2.12's band: every ordered member of the spy lane.</summary>
public static class TFSpyOrders
{
    private const int Band = 1_000_000 - 1_000 + 980;

    /// <summary><see cref="TFSpy"/>: the spy slices in the per-player composite's source chain.</summary>
    public const int Components = Band + 0;
}

/// <summary>
/// The spy as a slice of the per-player composite (Stage 2.12): the cloak
/// meter's drain and refill (<c>CTFPlayerShared::UpdateCloakMeter</c>,
/// tf_player_shared.cpp:14165-14253), the disguise's run and completion
/// (<c>Disguise</c> :8157-8269, <c>CompleteDisguise</c> :8300-8344), and the
/// backstab (<c>CTFKnife::PrimaryAttack</c>, tf_weapon_knife.cpp:180-280,
/// over <see cref="ITFSpyState"/>). The meter is the carrier's
/// <see cref="TFPlayerNetState.CloakMeter"/>; the conditions go through the
/// one writer (<see cref="ITFPlayerConditions"/>); the times stock predicts
/// for the fade visuals (<c>m_flInvisChangeCompleteTime</c>,
/// <c>m_flStealthNextChangeTime</c>) are this slice's own until the client
/// renders invisibility (Stage 3).
/// </summary>
public sealed class TFSpyComponent(
    ITFPlayerData _players, ITFPlayerConditions conditions, IEnginePlayers enginePlayers,
    IEngineEntities entities, IEngineWorldQuery world, IEngineDamage damage, DamageHitGroups hitGroups,
    TFDamageScopes scopes, IUniformRandom random, TFClassTable classes, TFSpy source,
    TFRoundWinners winners, ITFRespawnRooms rooms, AttributeHooks hooks,
    ITFWeaponOwners owners, IEngineSound sound, TFDisguiseWeapons disguiseWeapons,
    ICtfRules ctf, ILagCompensation lagCompensation,
    ITFPlayerResupply resupply, DeathNotice notice, Corpses corpses,
    TFDeathDrops drops, TFFeignRagdoll feignRagdoll) : IPlayerComponent
{
    /// <summary><c>TF_TIME_TO_DISGUISE</c> (tf_shareddefs.h:674).</summary>
    public const float TimeToDisguise = 2.0f;

    /// <summary><c>TF_TIME_TO_QUICK_DISGUISE</c> (tf_shareddefs.h:675).</summary>
    public const float TimeToQuickDisguise = 0.5f;

    /// <summary>tf_weapon_invis.cpp:243: "must have over 10% cloak to start".</summary>
    public const float CloakStartMeter = 8.0f;

    /// <summary><c>INVIS_FEIGN_DEATH</c> (<c>tf_weapon_invis.h:22-27</c>): the
    /// watch's <c>set_weapon_mode</c> the Dead Ringer wears
    /// (<c>HasFeignDeath</c>, :67). The shared enum's home is
    /// <see cref="TFInvisibility.InvisFeignDeathMode"/>.</summary>
    public const int InvisFeignDeathMode = TFInvisibility.InvisFeignDeathMode;

    /// <summary><c>INVIS_MOTION_CLOAK</c> (<c>tf_weapon_invis.h:22-27</c>): the
    /// watch's <c>set_weapon_mode</c> the Cloak and Dagger wears
    /// (<c>HasMotionCloak</c>, :68), the meter's motion arm gated on it
    /// (<c>UpdateCloakMeter</c>, tf_player_shared.cpp:14153). The shared enum's
    /// home is <see cref="TFInvisibility.InvisMotionCloakMode"/>.</summary>
    public const int InvisMotionCloakMode = TFInvisibility.InvisMotionCloakMode;

    /// <summary><c>tf_feign_death_duration</c>'s stock default
    /// (tf_player_shared.cpp:180): the seconds the feign condition rides.</summary>
    public const float FeignDeathSeconds = 3.0f;

    /// <summary><c>tf_feign_death_speed_duration</c>'s stock default
    /// (tf_player_shared.cpp:181): the seconds the feign's buffs ride.</summary>
    public const float FeignDeathSpeedSeconds = 3.0f;

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;

    // False until the first Tick: the attach tick starts the meter, as the
    // old clock difference's first-tick zero did.
    private bool _drained;

    // The disguise in the air (stock's m_nDesiredDisguise*). The deadline and
    // the carrier's copy of the desire live on the carrier itself
    // (DisguiseCompleteTime, DesiredDisguiseClass/Team): the replay completes
    // from them, so one copy serves both sides.
    private TFClass _desiredClass = TFClass.Undefined;
    private TFTeam _desiredTeam = TFTeam.Unassigned;

    // The last disguise, for `lastdisguise` (stock's GetDesiredDisguiseClass,
    // m_bLastDisguisedAsOwnTeam — kept through the disguise's end).
    private bool _wasLastOwnTeam;

    // tf_weapon_invis.cpp's m_flStealthNextChangeTime, the toggle's cooldown.
    private float _stealthNextChangeTime;

    // Stock's m_flLastStealthExposeTime (tf_player_shared.h:970): when the
    // motion cloak was last moving, the second the still refill waits for.
    private float _lastStealthExposeTime;

    // Stock's m_flFeignDeathEnd (tf_player_shared.cpp:7190): when the feign ends,
    // the buffs' window; checked every think (:2997-3002).
    private float _feignDeathEndStamp;

    // The swing just made backstabbed, until the next one; the victim it named
    // (stock's m_hBackstabVictim, read by DisguiseOnKill).
    private bool _backstabbed;
    private EntityHandle _backstabVictim = EntityHandle.None;

    // The Spy-cicle's melt (stock's m_flKnifeMeltTimestamp, m_flKnifeRegenerateDuration :523).
    private float _knifeMeltStamp;
    private float _knifeMeltWindow;

    public int Order => 0;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        source.Enter(_index, this);
    }

    public void Tick(float now)
    {
        // The think's dt is the tick itself (see <see cref="TFSpy.TickInterval"/>)
        // once the drain has run: the frame clock's differences carry
        // accumulation noise the replay's constant does not, and the two meters
        // must drift together bitwise. The attach tick starts the meter.
        float frametime = _drained ? source.TickInterval : 0f;
        _drained = true;

        if (_players.Predicted(_index) is not { } state)
            return;

        // InvisibilityThink's safety net (tf_player_shared.cpp:7977-7988): a
        // non-spy is never stealthed.
        if ((TFClass)state.PlayerClass != TFClass.Spy)
        {
            if (state.Conditions().Has(TFCond.Stealthed))
                conditions.Remove(_player, TFCond.Stealthed);
            return;
        }

        UpdateCloakMeter(now, frametime, state);

        // The feign's own early end (:2997-3002): once the buffs' window closes,
        // the think takes the feign condition off; the stamp rides in from the
        // add (:7190), strict-less than stock's check (:2999).
        if (state.Conditions().Has(TFCond.FeignDeath) && _feignDeathEndStamp < now)
            conditions.Remove(_player, TFCond.FeignDeath);                                 // :3001

        // CheckDisguiseTimer's arm (tf_player_shared.cpp:3305-3314): the
        // disguising ends past its deadline, never on it — stock compares
        // strictly, and the replay's DisguiseTick compares the same way.
        if (state.Conditions().Has(TFCond.Disguising) && state.DisguiseCompleteTime > 0f
            && now > state.DisguiseCompleteTime)
            CompleteDisguise(state);

        // The disguise weapon (DetermineDisguiseWeapon's dress): the tick's
        // tail keeps the carried entity wearing the disguise while it stands.
        disguiseWeapons.Tick(_index, state);
    }

    public void Detach()
    {
        if (_index >= 0)
            source.Leave(_index, this);
        _index = -1;
        _player = EntityHandle.None;
    }

    // ---- the cloak ---------------------------------------------------------

    /// <summary>
    /// <c>UpdateCloakMeter</c> (tf_player_shared.cpp:14165-14253), the classic
    /// cloak: a fixed drain while stealthed — the meter out ends the cloak
    /// (<c>FadeInvis</c> 1.0, :14208-14211) — and a refill to 100 whenever not.
    /// The motion cloak's movement-scaled drain is the Dead Ringer lane's.
    /// </summary>
    private void UpdateCloakMeter(float now, float frametime, TFPlayerNetState state)
    {
        if (state.Conditions().Has(TFCond.Stealthed))
        {
            // SetCloakRates' consume arm (tf_weapon_invis.cpp:325-338): the
            // owner's mult_cloak_meter_consume_rate scales the drain, and the
            // schema writes the value inverse-scaled, so below one it is
            // corrected as stock's comment works out (:329-336).
            float consumeFactor = hooks.Float(1f, "mult_cloak_meter_consume_rate", owners.ManagerOf(_index));
            if (consumeFactor < 1f)
                consumeFactor = 1f / (2f - consumeFactor);

            bool motionCloak = hooks.Int(0, "set_weapon_mode", owners.ManagerOf(_index)) == InvisMotionCloakMode;

            if (motionCloak)
            {
                // The motion cloak's arms (tf_player_shared.cpp:14153-14181):
                // moving drains scaled by the squared speed over the squared
                // class maximum and the extra half (:14169-14181); standing
                // still regenerates, once the second since the last expose has
                // gone (:14158-14168). Both clamp, unlike the classic drain.
                float spdSqr = entities.GetVelocity(_player).LengthSquared;
                if (spdSqr == 0f)
                {
                    if (now - _lastStealthExposeTime > 1f)
                    {
                        // SetCloakRates' regen arm (tf_weapon_invis.cpp:340-343).
                        float regenFactor = hooks.Float(1f, "mult_cloak_meter_regen_rate", owners.ManagerOf(_index));
                        state.CloakMeter += frametime * source.RegenRate.Value * regenFactor;
                        if (state.CloakMeter >= 100f)
                            state.CloakMeter = 100f;
                    }
                }
                else
                {
                    _lastStealthExposeTime = now;
                    float maxSpeed = classes[(TFClass)state.PlayerClass].MaxSpeed;
                    float fFactor = Math.Clamp(spdSqr / (maxSpeed * maxSpeed), 0f, 1f);   // :14171-14175
                    state.CloakMeter -= frametime * source.ConsumeRate.Value * consumeFactor * fFactor * 1.5f;
                    if (state.CloakMeter < 0f)
                        state.CloakMeter = 0f;
                }
            }
            else
            {
                // The classic drain does not clamp (tf_player_shared.cpp:14204-14206):
                // the meter runs past zero and regenerates from the overshoot;
                // only the motion cloak's arm clamps (:14196-14199).
                state.CloakMeter -= frametime * source.ConsumeRate.Value * consumeFactor;
            }

            // A motion-cloaked meter that empties fades nothing: the spy
            // stands half-visible instead (:14189-14192).
            if (state.CloakMeter <= 0f && !motionCloak)
                FadeInvis(1f, now);
        }
        else
        {
            // SetCloakRates' regen arm (tf_weapon_invis.cpp:340-343): the
            // owner's mult_cloak_meter_regen_rate multiplies the refill.
            float regenFactor = hooks.Float(1f, "mult_cloak_meter_regen_rate", owners.ManagerOf(_index));
            state.CloakMeter += frametime * source.RegenRate.Value * regenFactor;
            if (state.CloakMeter >= 100f)
                state.CloakMeter = 100f;
        }
    }

    /// <summary><c>FadeInvis</c> (tf_player_shared.cpp:7924-7972) at
    /// <paramref name="rateScale"/>: the cloak's condition goes, the fade's
    /// clock is armed (<c>m_flInvisChangeCompleteTime</c>, :7971, over
    /// <c>tf_spy_invis_unstealth_time</c> and the decloak-rate scale
    /// :7950-7956) and, but for a fade under :7961's 0.15 (a force respawn
    /// — "they can attack whenever") and the user-buff-fading guard :7965
    /// (the Dead Ringer's expiring condition is never granted yet), the
    /// no-attack window is stamped :7968 over
    /// <c>tf_spy_cloak_no_attack_time</c> and both scales.</summary>
    public void FadeInvis(float rateScale, float now)
    {
        if (_players.Predicted(_index) is not { } state)
            return;
        if (state.Conditions().Has(TFCond.Stealthed))
            conditions.Remove(_player, TFCond.Stealthed);
        float decloak = hooks.Float(1f, "mult_decloak_rate", owners.ManagerOf(_index));
        if (decloak <= 0f)
            decloak = 1f;
        float fade = rateScale * source.UnstealthTime.Value * decloak;
        if (fade >= 0.15f && !state.Conditions().Has(TFCond.StealthedUserBuffFading))
            state.NoAttackExpire = now + source.NoAttackTime.Value * decloak * rateScale;
        state.InvisChangeCompleteTime = now + fade;
    }

    /// <summary>
    /// <c>CTFPlayerShared::CanGoInvisible</c> (tf_player_shared.cpp:12697-12717,
    /// the watch's cloak-start arm at <c>bAllowWhileCarryingFlag = false</c>):
    /// an intelligence of any type but player destruction rides the spy, and
    /// the cloak refuses (:12700-12717 — the passtime ball's arm and the
    /// <c>Hint_Cannot_Cloak_with_flag</c> message are not ported); and once a
    /// team has won, only the winners may cloak (:12713-12716, stock's
    /// <c>GR_STATE_TEAM_WIN</c> arm).
    /// </summary>
    private bool CanGoInvisible()
    {
        if (ctf.CarriedBy(_player) is { } flag && flag.Type != TFFlagType.PlayerDestruction)
            return false;

        if (winners.WinningTeam is TFTeam.Red or TFTeam.Blue
            && winners.WinningTeam != (TFTeam)enginePlayers.GetTeam(_player))
            return false;

        return true;
    }

    /// <summary>
    /// <c>ActivateInvisibilityWatch</c> (tf_weapon_invis.cpp:206-263): decloak
    /// when cloaked; otherwise cloak from <see cref="CanGoInvisible"/> and a
    /// meter over <see cref="CloakStartMeter"/> (:243). A Dead Ringer's watch
    /// (<c>set_weapon_mode</c> one on the held item, :66) answers with the
    /// feign-ready toggle instead of the cloak (:230-242).
    /// </summary>
    public bool ActivateInvisibilityWatch(float now)
    {
        if (_players.Predicted(_index) is not { } state)
            return false;

        // The watch is the spy's own item: only a spy may run it.
        if ((TFClass)state.PlayerClass != TFClass.Spy)
            return false;

        // DoClassSpecialSkill's gate (tf_player_shared.cpp:12357-12359): a
        // taunting spy's special skill does nothing.
        if (state.Conditions().Has(TFCond.Taunting))
            return false;

        if (now < _stealthNextChangeTime)
            return false;

        bool cloaked = false;
        if (state.Conditions().Has(TFCond.Stealthed))
        {
            FadeInvis(1f, now);
        }
        else if (hooks.Int(0, "set_weapon_mode", owners.ManagerOf(_index)) == InvisFeignDeathMode)
        {
            // The HasFeignDeath arm (tf_weapon_invis.cpp:230-242): the Dead
            // Ringer's watch toggles its ready bit instead of cloaking — off
            // whenever armed (:232-236), on only from a full meter (:237-241).
            if (state.FeignDeathReady)
                state.FeignDeathReady = false;
            else if (state.CloakMeter == 100f)
                state.FeignDeathReady = true;
        }
        else if (CanGoInvisible() && state.CloakMeter > CloakStartMeter)
        {
            conditions.Add(_player, TFCond.Stealthed, TFConditions.Permanent, _player);
            // OnAddStealthed's clock (tf_player_shared.cpp:6993-7000): the fade
            // in completes over tf_spy_invis_time (the feign-death and
            // user-buff arms are the Dead Ringer lane's).
            state.InvisChangeCompleteTime = now + source.InvisTime.Value;
            cloaked = true;
        }

        _stealthNextChangeTime = now + (cloaked ? 0.5f : 0.1f);
        return cloaked;
    }

    /// <summary>
    /// The spy arm of <c>CTFPlayer::OnTakeDamage</c> (<c>tf_player.cpp:9673-9688</c>)
    /// over <c>SpyDeadRingerDeath</c> (:15674-15697): telefrag and croc hits
    /// stand outside the arm (:9673), any other hit spends the watch's ready
    /// bit (:9676-9678), and under a taunt nothing else runs (:9679). The
    /// death itself refuses for a dead (:15677), already stealthed (:15681) or
    /// uncloakable (:15685) spy; otherwise the meter halves (:15688), the
    /// burning and the bleed go off (:15719-15720) and the feign condition
    /// rides for <see cref="FeignDeathSeconds"/> (:15695) — its buffs and its
    /// instant cloak land through <see cref="FeignDeathAdded"/> on the add.
    /// Answers whether the spy played dead.
    /// </summary>
    public bool FeignDeathHit(float now, EntityHandle attacker = default)
    {
        if (_players.Predicted(_index) is not { } state)
            return false;

        // A telefrag or a croc hit never answers through the watch (:9673).
        if (scopes.TryCurrent(out TFDamageCustom custom, out _, out _)
            && custom is TFDamageCustom.Telefrag or TFDamageCustom.Croc)
            return false;

        if (!state.FeignDeathReady)
            return false;
        state.FeignDeathReady = false;                                   // :9678

        if (!enginePlayers.IsAlive(_player)                              // :15677
            || state.Conditions().Has(TFCond.Stealthed)                  // :15681
            || !CanGoInvisible() || state.CloakMeter < 100f)             // :15685
            return false;

        if (state.Conditions().Has(TFCond.Taunting))                     // :9679
            return false;

        state.CloakMeter = 50f;                                          // :15688

        // The two answers the corpse itself wears, both read before the strip
        // below takes the burn off him: stock's bDisguised (:15713, the
        // disguise only when it is a teammate's) and bBurning (:15716, the
        // burn that survives unless the disguise was a pyro's).
        bool disguised = state.Conditions().Has(TFCond.Disguised)
                     && state.DisguiseTeam == enginePlayers.GetTeam(_player);
        bool burning = state.Conditions().Has(TFCond.Burning)
                    && (!disguised || (TFClass.Pyro != (TFClass)state.DisguiseClass));   // :15716

        conditions.Remove(_player, TFCond.Burning);                      // :15719
        conditions.Remove(_player, TFCond.Bleeding);                     // :15720

        // The fake death's body, in FeignDeath's own order (:15702-15782):
        // the audio (:15724-15726), the notice (:15733-15737), the empty pack
        // (:15740-15743) and the corpse (:15782). The concept line and the
        // class grunt (:15725-15726) wait with the voice-over lane
        // (docs/plans/tf2/deferred.tsv); the flag, rune, medieval, laugh,
        // calling-card and ghost arms (:15704-15778) wait with their modes.
        sound.EntityEmitSound(_player, "BaseCombatCharacter.StopWeaponSounds");   // :15724
        notice.Fire(_player, attacker);                                            // :15736
        drops.OnFeignDeathDropped(new PlayerDeath(_player, _index, attacker,
                                                  entities.GetOrigin(_player), EntityHandle.None));   // :15742
        EntityHandle corpse = corpses.Make(_player);                               // :15782
        feignRagdoll.Dress(_player, corpse, state, disguised, burning);            // :15816-15817, :15870-15879

        conditions.Add(_player, TFCond.FeignDeath, FeignDeathSeconds);   // :15695
        return true;
    }

    /// <summary>
    /// <c>CTFPlayerShared::OnAddFeignDeath</c> (<c>tf_player_shared.cpp:7170-7191</c>):
    /// the feign puts the cloak on when it is off (:7178-7181), and the speed
    /// boost and the afterburn immunity ride for
    /// <see cref="FeignDeathSpeedSeconds"/> (:7185-7186); the ready bit goes
    /// off with the add (:7188), its end stamp being the buffs' own window.
    /// </summary>
    public void FeignDeathAdded(float now)
    {
        if (_players.Predicted(_index) is not { } state)
            return;

        if (!state.Conditions().Has(TFCond.Stealthed))
            conditions.Add(_player, TFCond.Stealthed, TFConditions.Permanent, _player);   // :7180
        conditions.Add(_player, TFCond.SpeedBoost, FeignDeathSpeedSeconds, _player);       // :7185
        conditions.Add(_player, TFCond.AfterburnImmune, FeignDeathSpeedSeconds, _player);  // :7186
        state.FeignDeathReady = false;                                                     // :7188
        _feignDeathEndStamp = now + FeignDeathSpeedSeconds;                              // :7190
    }

    // ---- the disguise ------------------------------------------------------

    /// <summary><c>Disguise</c> (tf_player_shared.cpp:8157-8269).</summary>
    public bool Disguise(float now, TFClass disguiseClass, TFTeam disguiseTeam)
    {
        if (_players.Predicted(_index) is not { } state)
            return false;

        // CanDisguise's gate (tf_player_shared.cpp:12944-12967): the commands
        // refuse a dead spy. Stock also refuses an incapacitated one; the
        // core has no incap state yet.
        if (!enginePlayers.IsAlive(_player))
            return false;

        int realTeam = enginePlayers.GetTeam(_player);
        var realClass = (TFClass)state.PlayerClass;

        if (realClass != TFClass.Spy)
            return false;

        if (state.Conditions().Has(TFCond.Taunting))
            return false;

        // Not disguising as anything but ourselves: reset everything.
        if (realTeam == (int)disguiseTeam && realClass == disguiseClass)
        {
            RemoveDisguise();
            return true;
        }

        // The disguise already on (stock's m_nDisguiseTeam/Class on the carrier).
        if (disguiseTeam == (TFTeam)state.DisguiseTeam && disguiseClass == (TFClass)state.DisguiseClass)
            return false;

        if (disguiseTeam <= TFTeam.Spectator || disguiseTeam >= TFTeam.Count)
            return false;

        if (disguiseClass <= TFClass.Undefined || disguiseClass >= TFClass.CountAll)
            return false;

        // Already going there (the lastdisguise key pushed twice).
        if (state.Conditions().Has(TFCond.Disguising) && disguiseTeam == _desiredTeam && disguiseClass == _desiredClass)
            return false;

        // The cloak-consuming disguise (tf_player_shared.cpp:8214-8234): the
        // item's hook asks once; an incomplete meter refuses with the
        // use-deny vo, and a full one outside a respawn room pays with itself.
        if (hooks.Int(0, "mod_disguise_consumes_cloak", owners.ManagerOf(_index)) != 0)
        {
            if (state.CloakMeter < 100f)
            {
                sound.EntityEmitSound(_player, "Player.UseDeny");
                return false;
            }
            if (!rooms.PointInRespawnRoom(_player, entities.GetWorldSpaceCenter(_player), false))
                state.CloakMeter = 0f;
        }

        _desiredClass = disguiseClass;
        _desiredTeam = disguiseTeam;
        _wasLastOwnTeam = (int)disguiseTeam == realTeam;

        // The wish goes on the carrier (stock's m_nDesiredDisguiseTeam/Class,
        // :8246-8247): the replay completes the disguise from it.
        state.DesiredDisguiseClass = (int)disguiseClass;
        state.DesiredDisguiseTeam = (int)disguiseTeam;

        conditions.Add(_player, TFCond.Disguising, TFConditions.Permanent, _player);

        float time = state.Conditions().Has(TFCond.Disguised) ? TimeToQuickDisguise : TimeToDisguise;
        state.DisguiseCompleteTime = now + time;
        return true;
    }

    /// <summary><c>CompleteDisguise</c> (tf_player_shared.cpp:8300-8344): the
    /// fields go on the carrier, <c>TF_COND_DISGUISED</c> replaces
    /// <c>TF_COND_DISGUISING</c>, the target is drawn and the fake health
    /// written from it. The disguise weapon (<c>DetermineDisguiseWeapon</c>,
    /// :8394) is the think's tail (<see cref="TFDisguiseWeapons"/>); only the
    /// wearables (<c>DetermineDisguiseWearables</c>) are still narrowed in
    /// docs/plans/tf2/deferred.tsv: the core networks no cosmetics.</summary>
    private void CompleteDisguise(TFPlayerNetState state)
    {
        conditions.Add(_player, TFCond.Disguised, TFConditions.Permanent, _player);
        state.DisguiseClass = (int)_desiredClass;
        state.DisguiseTeam = (int)_desiredTeam;

        // The mask's roll (:8307-8310): a spy disguised as a spy wears one of
        // the nine classes' masks, drawn here and sent — the arrival's word.
        if ((TFClass)state.DisguiseClass == TFClass.Spy)
            state.MaskClass = random.RandomInt(1, 9);

        conditions.Remove(_player, TFCond.Disguising);
        state.DisguiseCompleteTime = 0f;

        // Stock's m_pOuter->UpdateModel() (:8316) re-asserts the SPY's own
        // model: the server never wears the disguise body — that is the
        // client's per-viewer ValidateModelIndex (c_tf_player.cpp:8988-9040),
        // and moving the server's model would move its hitboxes and show
        // team-mates an enemy.

        FindDisguiseTarget(state);
        SetDisguiseHealth(state);
    }

    /// <summary>
    /// <c>TeamFortress_GetDisguiseTarget</c> (tf_player.cpp:8601-8656) over
    /// the carrier rows: the players of the disguise team and class first —
    /// the last target excluded, the list built with <c>AddToHead</c> and
    /// drawn with one roll (:8618-8635) — then anyone on the disguise team,
    /// where stock reuses its (now empty) list and does NOT exclude the last
    /// target (:8639-8646). The search is by the DISGUISE fields
    /// (<c>TeamFortress_GetDisguiseTarget( m_nDisguiseTeam, m_nDisguiseClass )</c>,
    /// tf_player_shared.cpp:8284) — the desired pair is already forgotten by
    /// the removal arm of the condition this completion removes
    /// (<c>ForgetDesiredTeam</c>, :7218). Stock honours a desired target
    /// first (:8277-8281); the tree has no such row yet. No alive check in
    /// either pass, as stock's. A player is a row whose carrier is live,
    /// answered through <see cref="ITFPlayerData.Networked"/>; the handle
    /// reaches the team through <see cref="IEnginePlayers.GetHandle"/>. None
    /// found names -1.
    /// </summary>
    private void FindDisguiseTarget(TFPlayerNetState state)
    {
        int previous = state.DisguiseTargetIndex;
        var candidates = new List<int>();

        for (int i = 1; i < TFPlayerResourceNetState.Slots; i++)
        {
            if (i == previous || _players.Networked(i) is not { } carrier)
                continue;

            var player = enginePlayers.GetHandle(i);
            if (player == EntityHandle.None)
                continue;

            if (enginePlayers.GetTeam(player) == state.DisguiseTeam && carrier.PlayerClass == state.DisguiseClass)
                candidates.Insert(0, i);
        }

        if (candidates.Count == 0)
        {
            for (int i = 1; i < TFPlayerResourceNetState.Slots; i++)
            {
                if (_players.Networked(i) is null)
                    continue;

                var player = enginePlayers.GetHandle(i);
                if (player == EntityHandle.None)
                    continue;

                if (enginePlayers.GetTeam(player) == state.DisguiseTeam)
                    candidates.Insert(0, i);
            }
        }

        state.DisguiseTargetIndex = candidates.Count > 0
            ? candidates[random.RandomInt(0, candidates.Count - 1)]
            : -1;
    }

    /// <summary>
    /// The fake health (CompleteDisguise, tf_player_shared.cpp:8321-8334):
    /// the target's health — the disguise class's max when the target is
    /// dead or healthless (<c>GetDisguiseMaxHealth</c>, :8358-8369) — or, with
    /// no target, a half-to-full roll of the spy's own max.
    /// </summary>
    private void SetDisguiseHealth(TFPlayerNetState state)
    {
        if (state.DisguiseTargetIndex != -1)
        {
            var target = enginePlayers.GetHandle(state.DisguiseTargetIndex);
            int health = entities.GetHealth(target);
            state.DisguiseHealth = health > 0 && enginePlayers.IsAlive(target)
                ? health
                : ClassMaxHealth((TFClass)state.DisguiseClass, state);
            return;
        }

        int max = ClassMaxHealth((TFClass)state.PlayerClass, state);
        state.DisguiseHealth = random.RandomInt(max / 2, max);
    }

    /// <summary><c>GetDisguiseMaxHealth</c>'s fallback (:8362-8368): a class
    /// without data answers the spy's own max.</summary>
    private int ClassMaxHealth(TFClass playerClass, TFPlayerNetState state)
    {
        int max = classes[playerClass].MaxHealth;
        return max > 0 ? max : classes[(TFClass)state.PlayerClass].MaxHealth;
    }

    /// <summary>
    /// <c>RemoveDisguise</c> (tf_player_shared.cpp:8374-8388). The teleporter
    /// check reads the disguise's team first (:8376) — the removal arms are
    /// what empty the fields, and they empty them while the conditions come
    /// off (:8384-8385), so the read stands before the removes.
    /// </summary>
    public void RemoveDisguise()
    {
        if (_players.Predicted(_index) is not { } state)
            return;
        if (!state.Conditions().Has(TFCond.Disguised) && !state.Conditions().Has(TFCond.Disguising))
            return;

        // The teleporter's condition ends with an enemy-team disguise
        // (tf_player_shared.cpp:8376-8382): its team is no longer the disguise's.
        if ((TFTeam)state.DisguiseTeam != (TFTeam)enginePlayers.GetTeam(_player))
            conditions.Remove(_player, TFCond.Teleported);

        // The removal arms (TFSpy.OnRemoved) leave no disguise behind: the
        // carrier fields and the desired team go with the conditions.
        conditions.Remove(_player, TFCond.Disguised);
        conditions.Remove(_player, TFCond.Disguising);

        conditions.Add(_player, TFCond.DisguiseWearingoff, 0.5f, _player);
    }

    /// <summary><c>OnRemoveDisguising</c>'s server half
    /// (tf_player_shared.cpp:7218): the desired team goes undefined; the
    /// desired class stands for the <c>lastdisguise</c> key (:7220-7223).</summary>
    internal void ForgetDesiredTeam() => _desiredTeam = TFTeam.Unassigned;

    /// <summary>
    /// <c>cc_tf_player_lastdisguise</c> (c_tf_player.cpp:9594-9633): the last
    /// disguise's class and team note, or <paramref name="desired"/> when the
    /// command carried a class in range (the argument read at :9611-9615);
    /// with none yet, a random class but the scout and the spy, drawn up to
    /// the civilian (<c>TF_LAST_NORMAL_CLASS</c>, tf_shareddefs.h:199), on
    /// the enemy team.
    /// </summary>
    public bool LastDisguise(float now, TFClass desired = TFClass.Undefined)
    {
        if (_players.Predicted(_index) is not { } state)
            return false;
        if (!enginePlayers.IsAlive(_player) || (TFClass)state.PlayerClass != TFClass.Spy)
            return false;

        int localTeam = enginePlayers.GetTeam(_player);
        int enemyTeam = localTeam == (int)TFTeam.Blue ? (int)TFTeam.Red : (int)TFTeam.Blue;
        int team = _wasLastOwnTeam ? localTeam : enemyTeam;
        var desiredClass = desired != TFClass.Undefined ? desired : _desiredClass;

        if (desiredClass == TFClass.Undefined)
        {
            do
                desiredClass = (TFClass)random.RandomInt((int)TFClass.Scout, (int)TFClass.Civilian);
            while (desiredClass is TFClass.Scout or TFClass.Spy);
            team = enemyTeam;
        }

        return Disguise(now, desiredClass, (TFTeam)team);
    }

    // ---- the backstab ------------------------------------------------------

    /// <summary>The swing's box, as the plain melee's (<c>vecSwingMinsBase</c>).</summary>
    private const float SwingHull = 18f;

    /// <summary>
    /// The swing: stock's trace (<c>CBaseHLBludgeonWeapon</c>'s line at the
    /// melee range, as the core arm's) and, when it names a living enemy
    /// player standing behind the spy and facing them
    /// (<c>IsBehindAndFacingTarget</c>, tf_weapon_knife.cpp:447-482), the
    /// backstab: the victim's current health doubled
    /// (<c>GetMeleeDamage</c>, :391-395), flagged, through the layer as
    /// <see cref="TFDamageCustom.Backstab"/>. Stock applies it at the smack
    /// 0.1 s later; the hit goes out here (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public bool Swing(float range)
    {
        _backstabbed = false;
        _backstabVictim = EntityHandle.None;   // stock's m_hBackstabVictim, rewriten with each swing
        _backstabVictim = EntityHandle.None;

        // The knife's session (tf_weapon_knife.cpp:192, :225): the swing's
        // trace and the behind-and-facing check see the other players where
        // the spy saw them.
        lagCompensation.Start(_player);
        try
        {
            Vector from = enginePlayers.GetShootOrigin(_player);
            var aim = Angles.Vectors(entities.GetEyeAngles(_player));

            // DoSwingTraceInternal (tf_weaponbase_melee.cpp:392-): the line, then the
            // 18-unit box, both at MASK_SOLID. A line at the shot mask finds only
            // hitboxes, and a body standing at arm's length often has none on it.
            Vector to = from + aim.Forward * range;
            TraceResult trace = world.TraceLine(from, to, TraceMask.Solid, _player);
            if (!trace.Hit)
            {
                var half = new Vector(SwingHull, SwingHull, SwingHull);
                SurfaceTraceResult box = world.TraceBox(from, to, half * -1f, half, TraceMask.Solid, _player);
                trace = new TraceResult(box.Hit, box.Fraction, box.End, box.Normal, box.Entity);
            }

            if (!trace.Hit || !entities.IsPlayer(trace.Entity))
                return false;

            // Stock's gate is the trace and the dot test only
            // (tf_weapon_knife.cpp:199-213): no alive check — a corpse is
            // normally non-solid anyway.
            if (enginePlayers.GetTeam(trace.Entity) == enginePlayers.GetTeam(_player))
                return false;

            // IsBehindAndFacingTarget: three dot products over the flat vectors.
            Vector toTarget = entities.GetWorldSpaceCenter(trace.Entity) - entities.GetWorldSpaceCenter(_player);
            toTarget.Z = 0f;
            float length = toTarget.Length;
            if (length < 1f)
                return false;
            toTarget = toTarget * (1f / length);

            Vector victimForward = Flat(Angles.Vectors(entities.GetEyeAngles(trace.Entity)).Forward);
            Vector ownerForward = Flat(aim.Forward);

            if (toTarget.Dot(victimForward) > 0f && toTarget.Dot(ownerForward) > 0.5f
                && victimForward.Dot(ownerForward) > -0.3f)
            {
                _backstabbed = true;
                _backstabVictim = trace.Entity;   // stock's m_hBackstabVictim = hHit (tf_weapon_knife.cpp:210)

                // Razorback's block (stock's CTFPlayer::CheckBlockBackstab, tf_player.cpp:10072-10116,
                // applied at tf_gamerules.cpp:6827): the victim's first item holding
                // set_blockbackstab_once zeroes this one backstab and then hides itself, so the
                // next one lands plain.
                var blockManager = owners.ManagerOf(trace.Entity.EntryIndex);
                int victimHealth = entities.GetHealth(trace.Entity);   // the heal's base, read before the hit (:210)
                float backstab = victimHealth * 2f;
                if (hooks.Int(0, "set_blockbackstab_once", blockManager) != 0)
                {
                    backstab = 0f;
                    if (blockManager is AttributeContainer holder)
                        ConsumeOnce(holder);
                }
                using (hitGroups.Push(HitGroup.Generic, critical: true))
                using (scopes.Push(TFDamageCustom.Backstab, TFWeaponId.Knife, TFKnifeHook.KnifeScript))
                    // The trace's end, as the core's MeleeSwing dispatches
                    // (tf_weaponbase_melee.cpp:820-880, Arsenal's trace.End); the
                    // plain swing trace names no hitbox, so the group is Generic.
                    damage.ApplyAt(trace.Entity, _player, backstab, DamageType.Club,
                                   DamageForceRules.MeleeForce(aim.Forward, backstab),
                                   trace.End);

                // sanguisuge (Conniver's Kunai), stock's CTFKnife::PrimaryAttack
                // heal arm (tf_weapon_knife.cpp:267-289): when the backstab just
                // killed the victim, the spy takes health up to three times the
                // class maximum, the victim's health before the hit as the base
                // (at least 75). The rune bonus and the reflect-rune zero that
                // share stock's arm are deferred (docs/plans/tf2/deferred.tsv).
                if (!enginePlayers.IsAlive(trace.Entity)
                    && hooks.Int(0, "sanguisuge", owners.ItemOf(_index, TFKnifeHook.KnifeScript)) > 0)
                {
                    int spyHealth = entities.GetHealth(_player);
                    int newHealth = Math.Min(spyHealth + Math.Max(victimHealth, 75),
                                             resupply.MaxHealth(_player) * 3);
                    int gained = newHealth - spyHealth;
                    if (gained > 0)
                        resupply.TakeHealth(_player, gained, ignoreMaxHealth: true);
                }

                // The kill arms (stock's CTFPlayer::OnKilledOther_Effects, tf_player.cpp:11509-11572),
                // fired once the victim is down: the spy's cloak-on-kill (:11513-11521) puts the held
                // item's add_cloak_on_kill straight on the cloak meter — forced, so the regen hooks
                // are skipped, clamped to 0..100 as AddToSpyCloakMeter does (tf_player_shared.cpp:9731,
                // :9753-9755) — and the weapon's speed_boost_on_kill rides TF_COND_SPEED_BOOST for
                // the attribute's seconds (:11567-11572). Stock's restore-to-percentage arm
                // (:11527-11543) is the heal lane's and stays deferred (docs/plans/tf2/deferred.tsv).
                if (!enginePlayers.IsAlive(trace.Entity))
                {
                    var killItem = owners.ItemOf(_index, TFKnifeHook.KnifeScript);

                    int cloakOnKill = hooks.Int(0, "add_cloak_on_kill", killItem);       // :11516
                    if (cloakOnKill > 0                                                   // :11517
                        && _players.Predicted(_index) is { } cloak
                        && (TFClass)cloak.PlayerClass == TFClass.Spy)                     // :11513
                    {
                        cloak.CloakMeter = Math.Clamp(cloak.CloakMeter + cloakOnKill, 0f, 100f);   // :9755
                    }

                    int boostSeconds = hooks.Int(0, "speed_boost_on_kill", killItem);     // :11568
                    if (boostSeconds != 0)                                                 // :11569
                        conditions.Add(_player, TFCond.SpeedBoost, boostSeconds, _player); // :11571
                }

                return true;
            }

            return false;
        }
        finally
        {
            lagCompensation.Finish(_player);
        }
    }

    /// <summary>Whether the swing just made backstabbed: its smack lands nothing.</summary>
    public bool Backstabbed => _backstabbed;

    /// <summary><c>CTFKnife::ShouldDisguiseOnBackstab</c>
    /// (<c>tf_weapon_knife.cpp:335-343</c>): the knife's own item answers
    /// <c>set_disguise_on_backstab</c> one, stock's hook over the weapon's item
    /// container (:338-339).</summary>
    public bool ShouldDisguiseOnBackstab() =>
        hooks.Int(0, "set_disguise_on_backstab", owners.ItemOf(_index, TFKnifeHook.KnifeScript)) == 1;

    /// <summary>
    /// <c>CTFKnife::DisguiseOnKill</c> (<c>tf_weapon_knife.cpp:315-330</c>),
    /// the think stock schedules on the successful backstab (:243-259): no
    /// victim answers no disguise (:318-319), and with one the spy wears his
    /// team and class (:325-328, stock's quick-disguise half of :328 — the
    /// managed <see cref="Disguise"/> picks the quick time of its own). The
    /// dead victim is stock's <c>bSuccessfulBackstab</c> (:243), stock's
    /// <c>CanDisguise</c> gate (:322) runs inside <see cref="Disguise"/>, and
    /// stock's flag half (:246, an intelligence but the pass-time ball on the
    /// spy refuses) rides beside the slice's own <see cref="CanGoInvisible"/>
    /// read. The think's 0.2 s delay applies at the press here, as the hit's
    /// does (docs/plans/tf2/deferred.tsv); the bot half of :249-255 waits on
    /// the MvM mode with the rest of its kind.
    /// </summary>
    public bool DisguiseOnKill(float now)
    {
        if (_backstabVictim == EntityHandle.None || enginePlayers.IsAlive(_backstabVictim))
            return false;

        if (ctf.CarriedBy(_player) is { } flag && flag.Type != TFFlagType.PlayerDestruction)
            return false;

        if (_players.Networked(_backstabVictim.EntryIndex) is not { } victim)
            return false;

        return Disguise(now, (TFClass)victim.PlayerClass,
                        (TFTeam)enginePlayers.GetTeam(_backstabVictim));
    }

    /// <summary><c>CTFKnife::CanDeploy</c> (<c>tf_weapon_knife.cpp:521-531</c>): the knife exists once
    /// <c>now - m_flKnifeMeltTimestamp &gt; m_flKnifeRegenerateDuration</c> (:523), and a missing one
    /// refuses the deploy (:525-528); the stamp's zero initial is stock's fresh <c>m_bKnifeExists</c>
    /// (:77), whose formula holds with a zero stamp and window.</summary>
    public bool KnifeReady(float now) => now - _knifeMeltStamp > _knifeMeltWindow;

    /// <summary><c>CTFKnife::ApplyOnInjuredAttributes</c> (<c>tf_weapon_knife.cpp:118-157</c>):
    /// burned or ignited with the knife in hand, the holder's item answering
    /// <c>melts_in_fire</c> melts the knife for its seconds (:129-131) — only while it exists (:126) —
    /// the melt sound with it (:136); stock's forced switch to the sapper (:142-153) waits with the
    /// building lanes.</summary>
    public void MeltKnife(float now, float seconds)
    {
        if (!KnifeReady(now))
            return;

        _knifeMeltStamp = now;
        _knifeMeltWindow = seconds;

        // The wire (tf_weapon_knife.cpp:33-40): the pair rides the carrier, the
        // client's CanDeploy recomputing the same line from it (:523) at the
        // switch back, where no spy slice stands to ask.
        if (_players.Predicted(_index) is { } carrier)
        {
            carrier.KnifeMeltTimestamp = now;
            carrier.KnifeRegenerateDuration = seconds;
        }

        sound.EntityEmitSound(_player, "Icicle.Melt");
    }

    /// <summary>Stock's z-flattened normalize; a zero vector stays zero.</summary>
    private static Vector Flat(Vector v)
    {
        v.Z = 0f;
        float length = v.Length;
        return length > 0f ? v * (1f / length) : v;
    }

    /// <summary>The block is once, as stock's item that answered hides itself
    /// (<c>EF_NODRAW</c>, tf_player.cpp:10072-10116): the first attribute of the
    /// hook class leaves the manager's own list, so the next walk of the same
    /// hook answers nothing and the next backstab lands.</summary>
    private static void ConsumeOnce(AttributeContainer manager)
    {
        int defindex = -1;
        foreach (ref readonly AttributeValue attribute in manager.Attributes)
        {
            if (attribute.Definition.ValueType == AttributeValueType.Default
                && string.Equals(attribute.Definition.AttributeClass, "set_blockbackstab_once",
                                 StringComparison.OrdinalIgnoreCase))
            {
                defindex = attribute.Definition.Defindex;
                break;
            }
        }
        if (defindex >= 0)
            manager.Remove(defindex);
    }
}

/// <summary>
/// The spy slices by player index, their source in the per-player composite,
/// and the <see cref="ITFSpyState"/> the knife hook and the spy commands ask.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFSpyState)])]
public sealed class TFSpy(ITFPlayerData players, ITFPlayerConditions conditions, IEnginePlayers enginePlayers,
                          IEngineEntities entities, IEngineWorldQuery world, IEngineDamage damage,
                          DamageHitGroups hitGroups, TFDamageScopes scopes, IUniformRandom random,
                          TFClassTable classes, IGameClock clock, IEngineEnvironment environment,
                          TFSpyCloakConsumeRateConVar consumeRate,
                          TFSpyCloakRegenRateConVar regenRate, TFSpyInvisTimeConVar invisTime,
                          TFSpyInvisUnstealthTimeConVar unstealthTime,
                          TFSpyCloakNoAttackTimeConVar noAttackTime,
                          TFRoundWinners winners, ITFRespawnRooms rooms, AttributeHooks hooks,
                          ITFWeaponOwners owners, IEngineSound sound, TFDisguiseWeapons disguiseWeapons,
                          ICtfRules ctf, ILagCompensation lagCompensation,
                          ITFPlayerResupply resupply, DeathNotice notice,
                          Corpses corpses, TFDeathDrops drops,
                          TFFeignRagdoll feignRagdoll)
    : IPlayerComponentSource, IOrderedHook, ITFSpyState, ITFConditionHook
{
    private readonly Dictionary<int, TFSpyComponent> _byIndex = new();

    public int HookOrder => TFSpyOrders.Components;

    /// <summary>
    /// The drain's dt, stock's <c>gpGlobals->frametime</c> on the tick-locked
    /// think: one interval per tick, the number the client's replay drains by
    /// too, so the two meters accumulate the same bits.
    /// </summary>
    internal float TickInterval => environment.Globals.IsAvailable ? environment.Globals.IntervalPerTick : 0f;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        yield return new TFSpyComponent(players, conditions, enginePlayers, entities, world, damage,
                                        hitGroups, scopes, random, classes, this, winners,
                                        rooms, hooks, owners, sound, disguiseWeapons, ctf, lagCompensation,
                                        resupply, notice, corpses, drops, feignRagdoll);
    }

    internal TFSpyCloakConsumeRateConVar ConsumeRate => consumeRate;

    internal TFSpyCloakRegenRateConVar RegenRate => regenRate;

    internal TFSpyInvisTimeConVar InvisTime => invisTime;

    internal TFSpyInvisUnstealthTimeConVar UnstealthTime => unstealthTime;

    internal TFSpyCloakNoAttackTimeConVar NoAttackTime => noAttackTime;

    /// <summary>The spy slice of the player at <paramref name="playerIndex"/>, or null.</summary>
    public TFSpyComponent? Of(int playerIndex) => _byIndex.GetValueOrDefault(playerIndex);

    internal void Enter(int playerIndex, TFSpyComponent spy) => _byIndex[playerIndex] = spy;

    internal void Leave(int playerIndex, TFSpyComponent spy)
    {
        if (_byIndex.TryGetValue(playerIndex, out var held) && ReferenceEquals(held, spy))
            _byIndex.Remove(playerIndex);
    }

    public bool Swing(int ownerIndex, float range) => Of(ownerIndex)?.Swing(range) ?? false;

    public bool Smack(int ownerIndex) => Of(ownerIndex)?.Backstabbed ?? false;

    public bool ShouldDisguiseOnBackstab(int ownerIndex) => Of(ownerIndex)?.ShouldDisguiseOnBackstab() ?? false;

    public bool DisguiseOnKill(int ownerIndex) => Of(ownerIndex)?.DisguiseOnKill(clock.Now) ?? false;

    public bool KnifeReady(int ownerIndex, float now) => Of(ownerIndex)?.KnifeReady(now) ?? true;

    public void MeltKnife(int ownerIndex, float now, float seconds) => Of(ownerIndex)?.MeltKnife(now, seconds);

    public void RemoveDisguise(int playerIndex) => Of(playerIndex)?.RemoveDisguise();

    public void RemoveInvisibility(int playerIndex)
    {
        // CTFPlayer::RemoveInvisibility (tf_player.cpp:15988-16005): the cloak
        // fades at half the unstealth time — but only when it was on
        // (:15990-15991); "called when player attacks" returns before
        // FadeInvis otherwise, so a plain swing arms no fade clock. The
        // user-buff fading condition is the Dead Ringer lane's.
        if (players.Predicted(playerIndex) is not { } state
            || !state.Conditions().Has(TFCond.Stealthed))
            return;

        Of(playerIndex)?.FadeInvis(0.5f, clock.Now);
    }

    public bool ActivateInvisibilityWatch(int playerIndex) =>
        Of(playerIndex)?.ActivateInvisibilityWatch(clock.Now) ?? false;

    public bool FeignDeathHit(int victimIndex, EntityHandle attacker = default) =>
        Of(victimIndex)?.FeignDeathHit(clock.Now, attacker) ?? false;

    public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) =>
        Of(playerIndex)?.Disguise(clock.Now, disguiseClass, disguiseTeam) ?? false;

    public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) =>
        Of(playerIndex)?.LastDisguise(clock.Now, desired) ?? false;

    // ---- the disguise removal arms (tf_player_shared.cpp:7217-7224, 7267-7268) -----

    /// <summary>
    /// The server add arms: the disguise conditions' arms are the client's
    /// (the disguising effect particles, :7211-7216), while the feign's own
    /// add (<c>OnAddFeignDeath</c>, tf_player_shared.cpp:7170-7191) lands the
    /// cloak and the buffs through
    /// <see cref="TFSpyComponent.FeignDeathAdded"/>, and the instant cloak of
    /// a feigning or user-buffed spy (<c>OnAddStealthed</c>'s else half,
    /// :6993-7004) stamps its change time at once.
    /// </summary>
    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.FeignDeath)
        {
            Of(player.EntryIndex)?.FeignDeathAdded(clock.Now);
            return;
        }

        if (cond == TFCond.Stealthed && players.Predicted(player.EntryIndex) is { } state)
        {
            // A feigning or user-buffed spy goes stealthed immediately, with
            // no fade to wait for (:6995-7003).
            var conds = state.Conditions();
            if (conds.Has(TFCond.FeignDeath) || conds.Has(TFCond.StealthedUserBuff))
                state.InvisChangeCompleteTime = clock.Now;
        }
    }

    /// <summary>
    /// The server halves of <c>OnRemoveDisguising</c> and <c>OnRemoveDisguised</c>:
    /// with the disguise's run the desired team goes undefined (the desired
    /// class stands — the <c>lastdisguise</c> key reads it, :7220-7223), and
    /// with the disguise the fields naming it go back to empty. Death and
    /// respawn arrive here too: the spawn's <c>RemoveAll</c> runs this chain,
    /// as stock's <c>RemoveAllCond</c> runs <c>OnConditionRemoved</c>. The skin
    /// override, bodygroups, ammo, model, expression, weapon
    /// list and wearables are the disguise-display lane
    /// (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Disguising)
        {
            Of(player.EntryIndex)?.ForgetDesiredTeam();
            if (players.Predicted(player.EntryIndex) is { } disguising)
                disguising.DesiredDisguiseTeam = (int)TFTeam.Unassigned;
        }
        else if (cond == TFCond.Disguised && players.Predicted(player.EntryIndex) is { } state)
        {
            state.DisguiseClass = (int)TFClass.Undefined;
            state.DisguiseTeam = (int)TFTeam.Unassigned;
            state.DisguiseTargetIndex = -1;
            state.DisguiseHealth = 0;
        }
    }
}
