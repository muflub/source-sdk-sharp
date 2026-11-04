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
    ICtfRules? ctf = null, ILagCompensation? lagCompensation = null) : IPlayerComponent
{
    /// <summary><c>TF_TIME_TO_DISGUISE</c> (tf_shareddefs.h:674).</summary>
    public const float TimeToDisguise = 2.0f;

    /// <summary><c>TF_TIME_TO_QUICK_DISGUISE</c> (tf_shareddefs.h:675).</summary>
    public const float TimeToQuickDisguise = 0.5f;

    /// <summary>tf_weapon_invis.cpp:243: "must have over 10% cloak to start".</summary>
    public const float CloakStartMeter = 8.0f;

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;

    // False until the first Tick: the attach tick starts the meter, as the
    // old clock difference's first-tick zero did.
    private bool _drained;

    // The disguise in the air (stock's m_nDesiredDisguise*, m_flDisguiseCompleteTime).
    private float _disguiseCompleteTime;
    private TFClass _desiredClass = TFClass.Undefined;
    private TFTeam _desiredTeam = TFTeam.Unassigned;

    // The last disguise, for `lastdisguise` (stock's GetDesiredDisguiseClass,
    // m_bLastDisguisedAsOwnTeam — kept through the disguise's end).
    private bool _wasLastOwnTeam;

    // tf_weapon_invis.cpp's m_flStealthNextChangeTime, the toggle's cooldown.
    private float _stealthNextChangeTime;

    // The swing just made backstabbed, until the next one.
    private bool _backstabbed;

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

        if (state.Conditions().Has(TFCond.Disguising) && now >= _disguiseCompleteTime)
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

            // The classic drain does not clamp (tf_player_shared.cpp:14204-14206):
            // the meter runs past zero and regenerates from the overshoot;
            // only the motion cloak's arm clamps (:14196-14199).
            state.CloakMeter -= frametime * source.ConsumeRate.Value * consumeFactor;
            if (state.CloakMeter <= 0f)
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
        if (ctf?.CarriedBy(_player) is { } flag && flag.Type != TFFlagType.PlayerDestruction)
            return false;

        if (winners.WinningTeam is TFTeam.Red or TFTeam.Blue
            && winners.WinningTeam != (TFTeam)enginePlayers.GetTeam(_player))
            return false;

        return true;
    }

    /// <summary>
    /// <c>ActivateInvisibilityWatch</c> (tf_weapon_invis.cpp:206-263): decloak
    /// when cloaked; otherwise cloak from <see cref="CanGoInvisible"/> and a
    /// meter over <see cref="CloakStartMeter"/> (:243). The Dead Ringer's
    /// feign death and the rate attributes are that lane's.
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

        conditions.Add(_player, TFCond.Disguising, TFConditions.Permanent, _player);

        float time = state.Conditions().Has(TFCond.Disguised) ? TimeToQuickDisguise : TimeToDisguise;
        _disguiseCompleteTime = now + time;
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
        conditions.Remove(_player, TFCond.Disguising);
        _disguiseCompleteTime = 0f;

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

        // The knife's session (tf_weapon_knife.cpp:192, :225): the swing's
        // trace and the behind-and-facing check see the other players where
        // the spy saw them.
        lagCompensation?.Start(_player);
        try
        {
            Vector from = enginePlayers.GetShootOrigin(_player);
            var aim = Angles.Vectors(entities.GetEyeAngles(_player));

            var trace = world.TraceLine(from, from + aim.Forward * range, TraceMask.Shot, _player);
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

                // Razorback's block (stock's CTFPlayer::CheckBlockBackstab, tf_player.cpp:10072-10116,
                // applied at tf_gamerules.cpp:6827): the victim's first item holding
                // set_blockbackstab_once zeroes this one backstab and then hides itself, so the
                // next one lands plain.
                var blockManager = owners.ManagerOf(trace.Entity.EntryIndex);
                float backstab = entities.GetHealth(trace.Entity) * 2f;
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

                return true;
            }

            return false;
        }
        finally
        {
            lagCompensation?.Finish(_player);
        }
    }

    /// <summary>Whether the swing just made backstabbed: its smack lands nothing.</summary>
    public bool Backstabbed => _backstabbed;

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
                          ICtfRules? ctf = null, ILagCompensation? lagCompensation = null)
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
                                        rooms, hooks, owners, sound, disguiseWeapons, ctf, lagCompensation);
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

    public bool Disguise(int playerIndex, TFClass disguiseClass, TFTeam disguiseTeam) =>
        Of(playerIndex)?.Disguise(clock.Now, disguiseClass, disguiseTeam) ?? false;

    public bool LastDisguise(int playerIndex, TFClass desired = TFClass.Undefined) =>
        Of(playerIndex)?.LastDisguise(clock.Now, desired) ?? false;

    // ---- the disguise removal arms (tf_player_shared.cpp:7217-7224, 7267-7268) -----

    /// <summary>Stock's add arms for the disguise conditions are the client's
    /// (the disguising effect particles, :7211-7216); the server stands.</summary>
    public void OnAdded(EntityHandle player, TFCond cond) { }

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
            Of(player.EntryIndex)?.ForgetDesiredTeam();
        else if (cond == TFCond.Disguised && players.Predicted(player.EntryIndex) is { } state)
        {
            state.DisguiseClass = (int)TFClass.Undefined;
            state.DisguiseTeam = (int)TFTeam.Unassigned;
            state.DisguiseTargetIndex = -1;
            state.DisguiseHealth = 0;
        }
    }
}
