//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Shared.Anim;

/// <summary>
/// TF2's answer to the machine's main sequence: the four gait handlers of
/// <c>CTFPlayerAnimState</c> (<c>tf_playeranimstate.cpp</c>:
/// <c>HandleSwimming</c> :1265-1290, <c>HandleMoving</c> :1297-1334,
/// <c>HandleDucking</c> :1341-1392, <c>HandleJumping</c> :1427-1538) and the
/// translation <c>TranslateActivity</c> (:124-154) runs on the claim —
/// the condition tables first, then the active weapon's act-table
/// (<see cref="TFWeaponActivityTables"/>; for a disguised spy the enemy's
/// eye picks, the disguise weapon's table — <c>ActivityList</c>'s CLIENT
/// arm, <c>tf_weaponbase.cpp:4212-4222</c>), then the competitive-winner
/// check. Stock's subclass override, re-shaped as a chain layer per ruling
/// D10: it sits at <see cref="LayerOrderValue"/> = 310, under the HL2
/// posture layer (<see cref="Hl2PostureLayer.LayerOrderDefault"/> = 1000),
/// and the machine has already run its own base handlers when it is asked
/// (the same base-then-derived order stock's subclass expressed). Above it,
/// <see cref="ITFAnimHook"/> links are consulted before this layer decides —
/// the points a mod's subclass of stock's class would have overridden (plan
/// D10): the main activity and a gesture's landing, first-answer-wins, and
/// everything unclaimed answers as stock.
///
/// <para>
/// <b>It owns the whole gait claim.</b> The HL2 layer answers always, so
/// the TF layer claiming means the base claim never stands; it refuses
/// (returns false) only where stock had no TF2 player at all: no entity
/// index, no net state behind the seams, or a class that is not a class.
/// Then the HL2 layer's answer passes through untouched.
/// </para>
///
/// <para>
/// <b>Bookkeeping.</b> <c>m_bJumping</c>, <c>m_bFirstJumpFrame</c>,
/// <c>m_bInAirWalk</c>, <c>m_flJumpStartTime</c> and
/// <c>m_flHoldDeployedUntil</c> are stock's per-player anim-state fields,
/// kept here per target. The jump flags and the stamp are seeded by
/// <see cref="TryHandleAnimEvent"/> — the TF <c>DoAnimationEvent</c> switch
/// (:969-1259), which the chain consults BEFORE the core's switch — and read
/// back by the handlers (:1498, :1517, and the hold-deployed reads at
/// :1304, :1323).
/// </para>
///
/// <para>
/// <b>Deferred arms</b>, each named where it stands and re-listed with its
/// grep proof in the commit: the <c>IsLoser</c> exceptions (the duck gate
/// :1344 and the still arm :1351 carry one; stock's loser table :170-184 —
/// no such field on the net state), the building-carried deploy table
/// (:191-221, <c>IsCarryingObject</c>), the fall-stomp arm (:1464-1466,
/// <c>CanFallStomp</c>/<c>m_flFallVelocity</c>), the grappling hook
/// (:1446), and the econ item's activity override (:135-139) and
/// animation-slot override (:4189-4197).
/// </para>
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFAnimLayer : IAnimStateLayer
{
    /// <summary>Plan D10's band for the TF2 anim layer: above the HL2 posture layer, below every hook band.</summary>
    public const int LayerOrderValue = 310;

    /// <summary>Stock's literal at :1446 — the rise speed that makes an airwalk candidate.</summary>
    private const float AirWalkRiseSpeed = 300f;

    /// <summary>Stock's literal at :1517 — when the new jump's START pose becomes its FLOAT pose.</summary>
    private const float JumpFloatDelay = 0.5f;

    private readonly TFClassTable _classes;
    private readonly ITFPlayerData _players;
    private readonly ITFAnimWeapons _weapons;
    private TFAnimHookChain? _hooks;
    private readonly ConditionalWeakTable<IAnimTarget, PerPlayerAnim> _book = new();

    public TFAnimLayer(TFClassTable classes, ITFPlayerData players, ITFAnimWeapons weapons, TFAnimHookChain? hooks = null)
    {
        _classes = classes ?? throw new ArgumentNullException(nameof(classes));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
        _hooks = hooks;   // null until first ask: no composition means the no-hook chain
    }

    /// <summary>The hook chain consulted before every decision; absent composition is the empty chain.</summary>
    private TFAnimHookChain Hooks => _hooks ??= TFAnimHookChain.Of();

    /// <summary>The hook context for this frame's player: the two reads the hook sees are the layer's own.</summary>
    private TFAnimHookContext HookContext(AnimStateContext context, TFClass playerClass) =>
        new(context.Target.EntityIndex, playerClass, _weapons.ActiveWeapon(context.Target.EntityIndex), context.Now);

    /// <summary>Stock's per-<c>CTFPlayerAnimState</c> fields; one entry per target, alive as long as the target.</summary>
    private sealed class PerPlayerAnim
    {
        /// <summary><c>m_bJumping</c>.</summary>
        public bool Jumping;

        /// <summary><c>m_bFirstJumpFrame</c>.</summary>
        public bool FirstJumpFrame;

        /// <summary><c>m_bInAirWalk</c>.</summary>
        public bool InAirWalk;

        /// <summary><c>m_flHoldDeployedUntil</c>; the zoomed fire raises it (:1028), the moving handler clears and reads it (:1304, :1323).</summary>
        public float HoldDeployedUntil;

        /// <summary><c>m_flJumpStartTime</c>: the stock field's two writers — the base's <c>DoAnimationEvent(ACT_JUMP)</c> (multiplayer_animstate.cpp:293) and TF's DOUBLEJUMP arm (:1188) — both seed it here, and <see cref="HandleJumping"/> times its poses against it.</summary>
        public float JumpStartTime;
    }

    public int LayerOrder => LayerOrderValue;

    public bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main)
    {
        if (!TryPlayer(context.Target, out TFPlayerNetState? net, out TFClass playerClass))
            return false;   // stock's CTFPlayerAnimState with no CTFPlayer: not ours to answer

        // Plan D10's mod hook, asked before TF decides: the first hook that
        // answers names the model's activity whole, as the mod's
        // CalcMainActivity override would have with an early return — no
        // handler runs, and the answer is not run through the tables.
        string? hookMain = Hooks.MainOverride(HookContext(context, playerClass));
        if (hookMain is not null)
        {
            main.Activity = hookMain;
            main.Sequence = context.Target.SelectWeightedSequence(hookMain);
            return true;
        }

        PerPlayerAnim book = Book(context.Target);

        // Stock's CalcMainActivity order (same as the core's :336): jump,
        // duck, swim, die — and only if none of them claimed, move.
        string claim = HandleJumping(context, net, playerClass, book)
            ?? HandleDucking(context, net, playerClass, book)
            ?? HandleSwimming(context, net, playerClass, book)
            ?? (context.State.Dying ? MpActivities.DieSimple : null)   // HandleDying's claim
            ?? HandleMoving(context, net, playerClass, book);

        main.Activity = Translate(context, net, playerClass, claim);
        main.Sequence = context.Target.SelectWeightedSequence(main.Activity);
        return true;
    }

    /// <summary>
    /// The live-gesture re-evaluation (stock's per-frame
    /// <c>ComputeGestureSequence</c>, which the chain consults through this
    /// arm) keeps the core's answer; the gesture decisions stock made at
    /// event time — the fire family, the reload trio, the dash — are the
    /// <see cref="TryHandleAnimEvent"/> arms below.
    /// </summary>
    public bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture) => false;

    /// <summary>
    /// The TF <c>DoAnimationEvent</c> switch (:969-1259): the fire family,
    /// the reload trio, the dash, the stun/passtime/CYOA trios. The chain
    /// consults this before the core's switch, so claiming an event is
    /// stock's virtual answering it; anything unclaimed (the flinch family,
    /// swim, die, spawn, the grenade and custom arms) falls through to the
    /// core's switch — stock's <c>default: BaseClass::DoAnimationEvent</c>
    /// arm (:1241-1245).
    /// </summary>
    /// <remarks>
    /// Deferred, each named at its arm: the voice-command gesture
    /// (:1053-1260 — its activity arrives as an <c>nData</c> Activity index
    /// and this build has no managed Activity index space), and the
    /// client-only viewmodel <c>SendWeaponAnim</c> (:1248-1258), which goes
    /// with the viewmodels. Stock's <c>pPlayer == nullptr</c> early returns
    /// are this layer's <see cref="TryPlayer"/> false, after which the
    /// core's switch answers as stock's base does.
    /// </remarks>
    public bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data)
    {
        if (!TryPlayer(context.Target, out TFPlayerNetState? net, out TFClass playerClass))
            return false;   // stock's CTFPlayerAnimState with no CTFPlayer: the core's switch answers

        // The switch's one duck read (:971-975): the FL_DUCKING flag, dropped
        // when the model has no translated crouchwalk to wear.
        bool inDuck = context.Target.Ducked
            && context.Target.SelectWeightedSequence(
                   Translate(context, net, playerClass, MpActivities.CrouchWalk)) >= 0;

        switch (animEvent)
        {
            case PlayerAnimEvent.Jump:
            {
                PerPlayerAnim book = Book(context.Target);
                book.Jumping = true;                // multiplayer_animstate.cpp:285-287
                book.FirstJumpFrame = true;
                book.JumpStartTime = context.Now;   // :293 — the book's stamp, which HandleJumping times against
                return false;                       // the core's arm runs on: stock's base DoAnimationEvent(ACT_JUMP)
            }

            case PlayerAnimEvent.AttackPrimary:
            {
                TFAnimWeapon? weapon = _weapons.ActiveWeapon(context.Target.EntityIndex);
                if (weapon?.Id == TFWeaponId.Minigun)
                {
                    // The minigun (:992-1012): stand, swim or crouch fire,
                    // and stock's guard — the fire gesture plays only when
                    // the slot is not already playing that very gesture
                    // (:1008-1011).
                    string claim = context.State.InSwim
                        ? MpActivities.AttackSwimPrimaryFire           // :1000
                        : inDuck
                            ? TFAnimActivities.AttackCrouchPrimaryFire // :1005
                            : MpActivities.AttackStandPrimaryFire;     // :995

                    if (!context.State.IsGestureSlotPlaying(
                            GestureSlot.AttackAndReload,
                            Translate(context, net, playerClass, claim)))
                        RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload, claim);   // :1010
                }
                else if (weapon is TFAnimWeapon armed && IsSniperRifleOrBow(armed.Id) && net.InCond(TFCond.Zoomed))
                {
                    // The zoomed sniper or bow (:1013-1028): the deployed
                    // fire, and the deployed pose is held for two seconds
                    // (the moving handler's :1304 clears it early).
                    RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload,
                        inDuck ? TFAnimActivities.AttackCrouchPrimaryFireDeployed    // :1018
                               : TFAnimActivities.AttackStandPrimaryFireDeployed);   // :1022

                    Book(context.Target).HoldDeployedUntil = context.Now + 2f;       // :1028
                    // Stock's viewmodel ACT_VM_PRIMARYATTACK (:1025) is deferred.
                }
                else
                {
                    // Everything else (:1030-1036): crouch-or-stand fire —
                    // stock has no swim arm here (the quirk stands) and no
                    // playing-guard.
                    RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload,
                        inDuck ? TFAnimActivities.AttackCrouchPrimaryFire     // :1032
                               : MpActivities.AttackStandPrimaryFire);         // :1032
                }

                return true;
            }

            case PlayerAnimEvent.AttackPrimarySuper:
            {
                // :1041-1051 — swim over duck over stand.
                string claim = context.State.InSwim
                    ? TFAnimActivities.AttackSwimPrimarySuper          // :1046
                    : inDuck
                        ? TFAnimActivities.AttackCrouchPrimarySuper    // :1043
                        : TFAnimActivities.AttackStandPrimarySuper;    // :1043

                RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload, claim);
                return true;
            }

            case PlayerAnimEvent.AttackSecondary:
            {
                // :1061-1071 — duck over stand, and waist-deep takes the
                // swim fire; the swim test is the raw water level, not the
                // swim flag.
                string claim = inDuck
                    ? TFAnimActivities.AttackCrouchSecondaryFire       // :1063
                    : MpActivities.AttackStandSecondaryFire;           // :1063

                if (context.Target.WaterLevel >= MultiplayerAnimState.WaterLevelWaist)
                    claim = MpActivities.AttackSwimSecondaryFire;      // :1066

                RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload, claim);
                // Stock's viewmodel ACT_VM_SECONDARYATTACK (:1070) is deferred.
                return true;
            }

            case PlayerAnimEvent.AttackPre:
            {
                // :1073-1106 — the windup: minigun-in-water, crouch, stand;
                // only the minigun's prefire is auto-killed (:1082-1086).
                bool isMinigun = _weapons.ActiveWeapon(context.Target.EntityIndex)?.Id == TFWeaponId.Minigun;   // :1080

                string claim = context.State.InSwim && isMinigun
                    ? TFAnimActivities.AttackSwimPreFire               // :1091
                    : inDuck
                        ? TFAnimActivities.AttackCrouchPreFire         // :1096
                        : TFAnimActivities.AttackStandPreFire;         // :1101

                RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload, claim, autoKill: isMinigun);   // :1104
                return true;
            }

            case PlayerAnimEvent.AttackPost:
            {
                // :1108-1135 — the winddown, same shape, always restarted.
                bool isMinigun = _weapons.ActiveWeapon(context.Target.EntityIndex)?.Id == TFWeaponId.Minigun;   // :1115

                string claim = context.State.InSwim && isMinigun
                    ? TFAnimActivities.AttackSwimPostFire              // :1120
                    : inDuck
                        ? TFAnimActivities.AttackCrouchPostFire        // :1125
                        : TFAnimActivities.AttackStandPostFire;        // :1130

                RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload, claim);   // :1133
                return true;
            }

            case PlayerAnimEvent.Reload:
            case PlayerAnimEvent.ReloadLoop:
            case PlayerAnimEvent.ReloadEnd:
            {
                // :1138-1176 — in an airwalk the reload trio is the airwalk
                // trio; otherwise stock's base arm picks duck-first, then
                // swim, then stand. The picks go through this layer's
                // Translate because stock's base restart is the virtual
                // CTFPlayerAnimState::RestartGesture (:958-963), which
                // translates — the core's own RestartGesture knows only the
                // base's swim map. (The base arm's rate clamp and re-push,
                // multiplayer_animstate.cpp:1339-1343, are no-ops here: the
                // managed playback rate is always 1 and the restart already
                // set the gesture layer.)
                PerPlayerAnim book = Book(context.Target);

                string claim = animEvent switch
                {
                    PlayerAnimEvent.Reload => book.InAirWalk
                        ? TFAnimActivities.ReloadAirwalk               // :1143
                        : context.Target.Ducked ? MpActivities.ReloadCrouch
                        : context.State.InSwim ? MpActivities.ReloadSwim
                        : MpActivities.ReloadStand,                    // :1147 (BaseClass::DoAnimationEvent)
                    PlayerAnimEvent.ReloadLoop => book.InAirWalk
                        ? TFAnimActivities.ReloadAirwalkLoop           // :1156
                        : context.Target.Ducked ? MpActivities.ReloadCrouchLoop
                        : context.State.InSwim ? MpActivities.ReloadSwimLoop
                        : MpActivities.ReloadStandLoop,                // :1160
                    _ => book.InAirWalk
                        ? TFAnimActivities.ReloadAirwalkEnd            // :1169
                        : context.Target.Ducked ? MpActivities.ReloadCrouchEnd
                        : context.State.InSwim ? MpActivities.ReloadSwimEnd
                        : MpActivities.ReloadStandEnd,                 // :1173
                };

                RestartGesture(context, net, playerClass, GestureSlot.AttackAndReload, claim);
                return true;
            }

            case PlayerAnimEvent.DoubleJump:
            {
                // :1177-1204 — the airdash: the first dash starts the jump
                // on its own stamp, the airwalk is forced off, and the JUMP
                // slot plays the dash gesture. Stock's loser arm
                // (:1196-1198) rides the same CompetitiveLoser-condition
                // modelling the loser tables use.
                PerPlayerAnim book = Book(context.Target);

                if (!book.Jumping)
                {
                    book.Jumping = true;                // :1184-1190
                    book.FirstJumpFrame = true;
                    book.JumpStartTime = context.Now;   // :1188
                    context.State.RestartMainSequence();   // :1189
                }

                book.InAirWalk = false;                 // :1192-1193 — "Force the air walk off."

                RestartGesture(context, net, playerClass, GestureSlot.Jump,
                    net.InCond(TFCond.CompetitiveLoser)
                        ? TFAnimActivities.DoubleJumpLoserState   // :1198
                        : TFAnimActivities.DoubleJump);           // :1202
                return true;
            }

            case PlayerAnimEvent.DoubleJumpCrouch:
                // :1206-1213 — the whole body is commented out upstream: the
                // event is consumed and does nothing.
                return true;

            case PlayerAnimEvent.StunBegin:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.StunBegin, autoKill: false);           // :1215
                return true;
            case PlayerAnimEvent.StunMiddle:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.StunMiddle, autoKill: false);          // :1218
                return true;
            case PlayerAnimEvent.StunEnd:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.StunEnd);                               // :1221
                return true;
            case PlayerAnimEvent.PasstimeThrowBegin:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.PasstimeThrowBegin, autoKill: false);   // :1224
                return true;
            case PlayerAnimEvent.PasstimeThrowMiddle:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.PasstimeThrowMiddle, autoKill: false);  // :1227
                return true;
            case PlayerAnimEvent.PasstimeThrowEnd:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.PasstimeThrowEnd);                     // :1230
                return true;
            case PlayerAnimEvent.CyoaPdaBegin:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.CyoaPdaIntro, autoKill: false);         // :1233
                return true;
            case PlayerAnimEvent.CyoaPdaMiddle:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.CyoaPdaIdle, autoKill: false);          // :1236
                return true;
            case PlayerAnimEvent.CyoaPdaEnd:
                RestartGesture(context, net, playerClass, GestureSlot.Custom, TFAnimActivities.CyoaPdaOutro);                          // :1239
                return true;

            default:
                return false;   // :1241-1245 — stock's default arm: BaseClass::DoAnimationEvent answers
        }
    }

    //-------------------------------------------------------------------------
    // The handlers (tf_playeranimstate.cpp:1265-1538). Each returns its claim
    // as stock's ACT name, or null when the handler does not answer and the
    // next one in the order is asked.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>HandleSwimming</c> (:1265-1290): waist-deep is the swim; the
    /// deployed minigun and the zoomed sniper/bow swim deployed.
    /// </summary>
    private string? HandleSwimming(AnimStateContext context, TFPlayerNetState net, TFClass playerClass, PerPlayerAnim book)
    {
        if (context.Target.WaterLevel < MultiplayerAnimState.WaterLevelWaist)
            return null;   // not in the water: base claims nothing here

        TFAnimWeapon? weapon = _weapons.ActiveWeapon(context.Target.EntityIndex);
        if (weapon is not null)
        {
            if (weapon.Value.Id == TFWeaponId.Minigun && IsAiming(context, net, playerClass))
                return TFAnimActivities.SwimDeployed;      // :1274-1277

            if (IsSniperRifleOrBow(weapon.Value.Id) && net.InCond(TFCond.Zoomed))
                return TFAnimActivities.SwimDeployed;      // :1279-1285
        }

        return MpActivities.Swim;   // :1287 — the base claim
    }

    /// <summary>
    /// <c>HandleMoving</c> (:1297-1334): in TF the player runs (there is no
    /// walk arm here); aiming — or the hold-deployed window step 2 opens —
    /// wears the deployed pose instead.
    /// </summary>
    private string HandleMoving(AnimStateContext context, TFPlayerNetState net, TFClass playerClass, PerPlayerAnim book)
    {
        bool moving = XySpeed(context.Target) > MultiplayerAnimState.MovingMinimumSpeed;   // :1302

        if (moving)
            book.HoldDeployedUntil = 0f;   // :1304 — moving cancels the hold-deployed window

        if (IsAiming(context, net, playerClass))
            return moving ? TFAnimActivities.Deployed : TFAnimActivities.DeployedIdle;   // :1316, :1320

        if (book.HoldDeployedUntil > context.Now)
            return TFAnimActivities.DeployedIdle;   // :1323

        return moving ? MpActivities.Run : MpActivities.StandIdle;   // :1330 — "in TF we run all the time now"
    }

    /// <summary>
    /// <c>HandleDucking</c> (:1341-1392): the duck is honoured only when the
    /// model can wear the crouchwalk; still ducks idle, moving ducks walk,
    /// the airdash double-jumps, aiming deploy-crouchwalks — except the
    /// minigun, which never wears it.
    /// </summary>
    private string? HandleDucking(AnimStateContext context, TFPlayerNetState net, TFClass playerClass, PerPlayerAnim book)
    {
        if (!context.Target.Ducked)
            return null;

        // The duck-cancel gate (:1343-1347): the duck is dropped when the
        // model has no translated crouchwalk to play. (Stock's !IsLoser
        // exception is deferred — no such field on the net state.)
        if (context.Target.SelectWeightedSequence(Translate(context, net, playerClass, MpActivities.CrouchWalk)) < 0)
            return null;   // :1347 — bInDuck = false; the handler then does not answer

        if (XySpeed(context.Target) <= MultiplayerAnimState.MovingMinimumSpeed)
        {
            // :1351-1357 — still (stock's loser arm here is deferred): the
            // crouch idle, deployed-idle when aiming or holding.
            if (IsAiming(context, net, playerClass) || book.HoldDeployedUntil > context.Now)
                return TFAnimActivities.CrouchDeployedIdle;

            return MpActivities.CrouchIdle;
        }

        // :1361-1368 — moving: the airdash double-jump, else the crouchwalk.
        string claim = net.AirDash > 0 ? TFAnimActivities.DoubleJumpCrouch : MpActivities.CrouchWalk;

        // :1370-1384 — and aiming wears CROUCH_DEPLOYED — except the minigun,
        // which cannot fire from a crouch and keeps the crouchwalk.
        TFAnimWeapon? weapon = _weapons.ActiveWeapon(context.Target.EntityIndex);
        if (IsAiming(context, net, playerClass) && weapon?.Id != TFWeaponId.Minigun)
            claim = TFAnimActivities.CrouchDeployed;

        return claim;
    }

    /// <summary>
    /// <c>HandleJumping</c> (:1427-1538). The airwalk supersedes the jump
    /// (:1442): rising past the rise speed it claims <c>ACT_MP_AIRWALK</c>,
    /// and its clear on the ground restarts the main sequence and the JUMP
    /// slot's land gesture (:1449-1454). The jump then times its poses from
    /// the book's stamp — START to 0.5 s, FLOAT after, for the new-jump
    /// classes; plain <c>ACT_MP_JUMP</c> for the old (:1513-1530).
    /// </summary>
    private string? HandleJumping(AnimStateContext context, TFPlayerNetState net, TFClass playerClass, PerPlayerAnim book)
    {
        IAnimTarget target = context.Target;

        // The jump handler's own duck-cancel gate (:1429-1433) — here with no
        // loser exception at all, as stock writes it.
        bool inDuck = target.Ducked;
        if (inDuck && target.SelectWeightedSequence(Translate(context, net, playerClass, MpActivities.CrouchWalk)) < 0)
            inDuck = false;

        // :1438-1440 — "don't allow a firing heavy to jump or air walk": the
        // raw AIMING condition, not IsAiming, and it un-claims the whole
        // handler (the gait handlers answer).
        if (playerClass == TFClass.HeavyWeapons && net.InCond(TFCond.Aiming))
            return null;

        bool validAirWalkClass = !_classes[playerClass].DontDoAirwalk;   // :1443-1444

        // :1446 — stock's third term, the grappling-hook entity, is deferred
        // (the hook has no state in this build).
        if (validAirWalkClass && (target.Velocity.Z > AirWalkRiseSpeed || book.InAirWalk) && !inDuck)
        {
            if (target.OnGround && book.InAirWalk)
            {
                // :1449-1454 — the airwalk ends on the ground: main sequence
                // and land gesture restart, and the handler stands aside (the
                // tail's flags are both down) for the gait to answer.
                book.InAirWalk = false;
                context.State.RestartMainSequence();
                RestartGesture(context, net, playerClass, GestureSlot.Jump, TFAnimActivities.JumpLand);   // :1453
            }
            else if (target.WaterLevel >= MultiplayerAnimState.WaterLevelWaist)
            {
                // :1455-1460 — into the water the airwalk stops.
                book.InAirWalk = false;
                context.State.RestartMainSequence();
            }
            else if (!target.OnGround)
            {
                // :1461-1473 — in an airwalk. Stock's fall-stomp arm
                // (:1464-1466) is deferred: CanFallStomp and
                // m_flFallVelocity have no field on the net state.
                book.InAirWalk = true;
                return TFAnimActivities.Airwalk;   // :1470
            }
        }
        else if (book.Jumping)   // :1476-1478 — the jump itself
        {
            bool newJump = !_classes[playerClass].DontDoNewJump;   // :1481-1482

            if (book.FirstJumpFrame)
            {
                book.FirstJumpFrame = false;
                context.State.RestartMainSequence();   // :1484-1488 — reset the animation
            }

            if (target.WaterLevel >= MultiplayerAnimState.WaterLevelWaist)
            {
                book.Jumping = false;
                context.State.RestartMainSequence();   // :1491-1495
            }
            else if (context.Now - book.JumpStartTime > MultiplayerAnimState.JumpGroundDelay)
            {
                // :1496-1510 — after the grace, the ground ends the jump; the
                // new jump lands with its JUMP-slot gesture.
                if (target.OnGround)
                {
                    book.Jumping = false;
                    context.State.RestartMainSequence();
                    if (newJump)
                        RestartGesture(context, net, playerClass, GestureSlot.Jump, TFAnimActivities.JumpLand);   // :1505-1508
                }
            }

            if (book.Jumping)   // :1512-1530 — still jumping
            {
                return newJump
                    ? context.Now - book.JumpStartTime > JumpFloatDelay
                        ? TFAnimActivities.JumpFloat      // :1517-1519
                        : TFAnimActivities.JumpStart      // :1521-1523
                    : MpActivities.Jump;                  // :1526-1529 — the old jump is plain
            }
        }

        // :1534-1537 — the handler stands while a jump or airwalk runs, on the
        // stand pose stock's idealActivity default carries (:1069's init).
        return book.Jumping || book.InAirWalk ? MpActivities.StandIdle : null;
    }

    //-------------------------------------------------------------------------
    // TranslateActivity (tf_playeranimstate.cpp:124-154) and its tables.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The claim's journey to the model's name: the base's swim map (:128),
    /// the condition tables (<c>ActivityOverride</c> :223-269, first match),
    /// the active weapon's table (:130-140), and the competitive-winner
    /// check on the result (:143-151). The weapon's table is the disguise
    /// weapon's for a disguised spy the enemy's eye picks
    /// (<c>ActivityList</c>'s CLIENT arm, :4212-4222 — see
    /// <see cref="ITFAnimWeapons.DisguiseWeapon"/>). The econ item's
    /// <c>GetActivityOverride</c> (:135-139) is deferred with the rest of
    /// econ.
    /// </summary>
    private string Translate(AnimStateContext context, TFPlayerNetState net, TFClass playerClass, string activity)
    {
        string act = context.State.TranslateActivity(activity);   // :128 — the base's swim map

        if (net.InCond(TFCond.HalloweenKart))
            act = KartTable(act);                                 // :157-168, tried first (:231-235)
        else if (net.InCond(TFCond.CompetitiveLoser) && act == MpActivities.StandIdle)
            act = TFAnimActivities.CompetitiveLoserState;         // :186-189, tried next (:238-242)
            // The IsLoser loser table (:170-184) and the building-carried deploy table
            // (:191-221) are deferred; their conds/fields have no carrier here.

        TFAnimWeapon? weapon = _weapons.ActiveWeapon(context.Target.EntityIndex);

        // :4212-4222 — the disguised spy's table as the enemy's eye picks it:
        // the disguise weapon's table answers for the carried weapon's. The
        // cond check stands here, where stock ran it before GetDisguiseWeapon;
        // the enemy-view and same-weapon gates are the seam side's reads
        // (:4214/:4217), and the server's seam answers them as stock's
        // CLIENT_DLL ifdef did — no swap.
        if (net.InCond(TFCond.Disguised))
            weapon = _weapons.DisguiseWeapon(context.Target.EntityIndex) ?? weapon;

        if (weapon is not null)
            act = TFWeaponActivityTables.Translate(weapon.Value.Role, act);   // :130-140

        // :143-151 — after the weapon table: the translated stand of any class
        // with the PRIMARY gait's name, a spy's melee stand, or a demoman's
        // secondary stand becomes the winner state.
        if (net.InCond(TFCond.CompetitiveWinner)
            && (act == TFAnimActivities.StandPrimary
                || (playerClass == TFClass.Spy && act == TFAnimActivities.StandMelee)
                || (playerClass == TFClass.Demoman && act == TFAnimActivities.StandSecondary)))
            act = TFAnimActivities.CompetitiveWinnerState;

        return act;
    }

    /// <summary>Stock's <c>s_acttableKart</c> (:157-168): the kart pose replaces the gait, the shoot and the jump.</summary>
    private static string KartTable(string act) => act switch
    {
        MpActivities.StandIdle or MpActivities.Run or "ACT_MP_WALK" or TFAnimActivities.Airwalk
            => TFAnimActivities.KartIdle,                                             // :158-161
        MpActivities.AttackStandPrimaryFire or "ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE"
            => TFAnimActivities.KartActionShoot,                                      // :162-163
        TFAnimActivities.JumpStart => TFAnimActivities.KartJumpStart,                 // :164
        TFAnimActivities.JumpFloat => TFAnimActivities.KartJumpFloat,                 // :165
        TFAnimActivities.JumpLand => TFAnimActivities.KartJumpLand,                   // :166
        _ => act,
    };

    /// <summary>
    /// TF's <c>RestartGesture</c> override (:958-963), with plan D10's hook
    /// asked first: a hook naming a gesture for the slot answers whole (the
    /// mod's own <c>RestartGesture</c> override, never calling the base);
    /// otherwise the activity goes through TF's translation first — so the
    /// slot's own sequence lookup (and stock's <c>&lt;= 0</c> refuse,
    /// multiplayer_animstate.cpp:631) sees the weapon table's answer, not
    /// the base name.
    /// </summary>
    private void RestartGesture(AnimStateContext context, TFPlayerNetState net, TFClass playerClass, int gestureSlot, string gestureActivity, bool autoKill = true)
    {
        string act = Hooks.GestureOverride(HookContext(context, playerClass), gestureSlot, gestureActivity)
            ?? Translate(context, net, playerClass, gestureActivity);
        context.State.RestartGesture(gestureSlot, act, autoKill);
    }

    //-------------------------------------------------------------------------
    // The reads the handlers ask.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>CTFPlayerShared::IsAiming</c> (tf_player_shared.cpp:11448-11460):
    /// the AIMING condition, save the soldier's (its rocket-jump aim is not
    /// a deployed aim) — and for the sniper with the classic rifle, the ZOOM
    /// is the aim.
    /// </summary>
    private bool IsAiming(AnimStateContext context, TFPlayerNetState net, TFClass playerClass)
    {
        bool aiming = net.InCond(TFCond.Aiming) && playerClass != TFClass.Soldier;   // :11448-11450

        if (playerClass == TFClass.Sniper)
        {
            TFAnimWeapon? weapon = _weapons.ActiveWeapon(context.Target.EntityIndex);   // GetActiveTFWeapon()
            if (weapon is not null && weapon.Value.Id == TFWeaponId.SniperrifleClassic)
                aiming = net.InCond(TFCond.Zoomed);                                      // :11451-11459
        }

        return aiming;
    }

    /// <summary><c>WeaponID_IsSniperRifleOrBow</c> (tf_weaponbase.cpp / weapon_id_store): the three rifles and the compound bow.</summary>
    private static bool IsSniperRifleOrBow(TFWeaponId id)
        => id is TFWeaponId.Sniperrifle or TFWeaponId.SniperrifleDecap or TFWeaponId.SniperrifleClassic or TFWeaponId.CompoundBow;

    /// <summary>Stock's <c>GetOuterXYSpeed</c>: the planar speed the gait handlers compare against <c>MovingMinimumSpeed</c>.</summary>
    private static float XySpeed(IAnimTarget target)
    {
        Vector v = target.Velocity;
        return MathF.Sqrt(v.X * v.X + v.Y * v.Y);
    }

    /// <summary>
    /// Whether the target is a TF2 player at all, and which class: stock
    /// would have no <c>CTFPlayerAnimState</c> without its <c>CTFPlayer</c>.
    /// The layer's refuse gate — no index, no net state, or a class the
    /// enum does not name means the HL2 layer's answer stands.
    /// </summary>
    private bool TryPlayer(IAnimTarget target, [NotNullWhen(true)] out TFPlayerNetState? net, out TFClass playerClass)
    {
        net = null;
        playerClass = TFClass.Undefined;

        int index = target.EntityIndex;
        if (index < 0)
            return false;

        net = _players.Predicted(index) ?? _players.Networked(index);
        if (net is null)
            return false;

        playerClass = net.Class();
        return playerClass != TFClass.Undefined && Enum.IsDefined(playerClass);
    }

    private PerPlayerAnim Book(IAnimTarget target) => _book.GetValue(target, static _ => new PerPlayerAnim());
}
