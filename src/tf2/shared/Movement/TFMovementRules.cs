using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Shared.Movement;

/// <summary>
/// TF2's movement (stage1 1.8, D8): the mod's one <see cref="IMovementRules"/>
/// owner on both sides, deciding from the player's <c>tf_player_data</c>
/// carrier ONLY (<see cref="ITFPlayerData.Predicted"/>: the server's state, the
/// client's working copy), so the server's run of a command and every client
/// replay of it decide alike.
/// <list type="bullet">
/// <item><see cref="Constants"/>: <c>g_TFViewVectors</c> and TF's movement
/// convar defaults (<see cref="TFViewVectors"/>), replacing the core's HL2 hull.</item>
/// <item><see cref="MaxSpeedScale"/>: <see cref="TFMaxSpeed"/>, the new maximum
/// speed itself.</item>
/// <item><see cref="AirJump"/> / <see cref="Grounded"/>: the scout's dash
/// (<see cref="TFJump"/>), counted in the carrier's <c>AirDash</c> and cleared
/// on ground.</item>
/// <item><see cref="EyeHeight"/>: the class's eye (<c>g_TFClassViewVectors</c>).</item>
/// <item><see cref="BunnyJumpCap"/>: the jump's speed cap
/// (<c>PreventBunnyJumping</c>'s 1.2).</item>
/// <item><see cref="MovementStun"/>: the stun's rewrite of the command,
/// deciding from the carrier's stun mirror.</item>
/// <item><see cref="TFMovementRules.MovementCharge"/>: the shield charge's
/// rewrite of the command, from the carrier's condition word.</item>
/// </list>
///
/// <para>
/// <b>The stun's fade trio is this object's own, per player</b>
/// (<see cref="_stunLerps"/>): stock's <c>m_flStunLerpTarget</c> /
/// <c>m_flLastMovementStunChange</c> / <c>m_bStunNeedsFadeOut</c>
/// (tf_player_shared.h, the members StunMove mutates at :569-574 and
/// :588-609) are entity locals on <c>CTFPlayerShared</c> that sit in NO
/// send, recv or prediction table and survive Spawn — every replayed
/// StunMove mutates them alike, and nothing rolls them back. Restoring them
/// would be the one way to make the two sides disagree about a fade.
/// </para>
///
/// <para>
/// Two arms of StunMove stay deferred (docs/plans/tf2/deferred.tsv row 28):
/// the heavy's minigun exception to the button seizure (:548-552, which asks
/// the active weapon — this build's movement side has no reader for it), and
/// the CYOA-PDA / match-start-countdown no-move block (:614-624, which asks
/// the server's game rules and the view model).
/// </para>
///
/// <para>
/// A player with no carrier (the client before it arrives, any player but the
/// local one there) or no class is left to the core: stock's
/// <c>TeamFortress_CalculateMaxSpeed</c> answers 1 for no class, but stock
/// never has a classless player walking (they are observers until they
/// choose), and the core here spawns one; until Stage 2.3's team and class
/// flow gates the spawn, such a player moves at the core's speed (divergence).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IMovementRules)])]
[SideService(Side.Client, As = [typeof(IMovementRules)])]
public sealed class TFMovementRules : IMovementRules
{
    private readonly TFClassTable _classes;
    private readonly ITFPlayerData _players;
    private readonly TFScoutAirDashCountConVar _dashCount;
    private readonly TFSpyMaxCloakedSpeedConVar _cloakedCap;
    private readonly ITFWeaponOwners _owners;
    private readonly AttributeHooks _hooks;
    private readonly TFClampBackSpeedConVar _backSpeed;
    private readonly TFClampBackSpeedMinConVar _backSpeedMin;
    private readonly Rules.ITFRoundStateView _rounds;
    private readonly TFPlayerMovementRestartFreezeConVar _restartFreeze;
    private readonly TFMovementAircurrentFrictionMultConVar _airCurrentFriction;
    private readonly TFMovementLostFootingRestickConVar _lostFootingRestick;
    private readonly TFMovementLostFootingFrictionConVar _lostFootingFriction;
    private readonly TFMaxChargeSpeedConVar _chargeSpeed;
    private readonly Anim.ITFAnimWeapons _weapons;
    private readonly IEngineEntities _entities;
    private readonly TFParachuteMaxSpeedXYConVar _parachuteXY;
    private readonly TFParachuteMaxSpeedZConVar _parachuteZ;
    private readonly TFParachuteMaxSpeedOnfireZConVar _parachuteOnfireZ;

    /// <summary>
    /// Every dependency is injected: both sides' registrations hold each one
    /// (the convars through the registry's publish, the seams through their
    /// <see cref="SideServiceAttribute"/>), so a missing one fails the
    /// composition by name instead of answering a default.
    /// </summary>
    public TFMovementRules(TFClassTable classes, ITFPlayerData players,
                           TFScoutAirDashCountConVar dashCount,
                           TFSpyMaxCloakedSpeedConVar cloakedCap,
                           ITFWeaponOwners owners, AttributeHooks hooks,
                           TFClampBackSpeedConVar backSpeed,
                           TFClampBackSpeedMinConVar backSpeedMin,
                           Rules.ITFRoundStateView rounds,
                           TFPlayerMovementRestartFreezeConVar restartFreeze,
                           TFMovementAircurrentFrictionMultConVar airCurrentFriction,
                           TFMovementLostFootingRestickConVar lostFootingRestick,
                           TFMovementLostFootingFrictionConVar lostFootingFriction,
                           TFMaxChargeSpeedConVar chargeSpeed,
                           Anim.ITFAnimWeapons weapons,
                           IEngineEntities entities,
                           TFParachuteMaxSpeedXYConVar parachuteXY,
                           TFParachuteMaxSpeedZConVar parachuteZ,
                           TFParachuteMaxSpeedOnfireZConVar parachuteOnfireZ)
    {
        _classes = classes;
        _players = players;
        _dashCount = dashCount;
        _cloakedCap = cloakedCap;
        _owners = owners;
        _hooks = hooks;
        _backSpeed = backSpeed;
        _backSpeedMin = backSpeedMin;
        _rounds = rounds;
        _restartFreeze = restartFreeze;
        _airCurrentFriction = airCurrentFriction;
        _lostFootingRestick = lostFootingRestick;
        _lostFootingFriction = lostFootingFriction;
        _chargeSpeed = chargeSpeed;
        _weapons = weapons;
        _entities = entities;
        _parachuteXY = parachuteXY;
        _parachuteZ = parachuteZ;
        _parachuteOnfireZ = parachuteOnfireZ;
    }

    /// <summary><c>tf_max_charge_speed</c> (tf_gamemovement.cpp:52; the declaration carries the default).</summary>
    private float ChargeSpeed => _chargeSpeed.Value;

    /// <summary><c>tf_parachute_maxspeed_xy</c> (tf_gamemovement.cpp:54).</summary>
    private float ParachuteXY => _parachuteXY.Value;

    /// <summary><c>tf_parachute_maxspeed_z</c> (:55).</summary>
    private float ParachuteZ => _parachuteZ.Value;

    /// <summary><c>tf_parachute_maxspeed_onfire_z</c> (:56).</summary>
    private float ParachuteOnfireZ => _parachuteOnfireZ.Value;

    /// <summary><c>tf_scout_air_dash_count</c>.</summary>
    private int DashCount => _dashCount.Value;

    /// <summary><c>tf_spy_max_cloaked_speed</c>.</summary>
    private float CloakedCap => _cloakedCap.Value;

    /// <summary><c>tf_clamp_back_speed</c> (tf_gamemovement.cpp:47).</summary>
    private float BackSpeedFraction => _backSpeed.Value;

    /// <summary><c>tf_clamp_back_speed_min</c> (tf_gamemovement.cpp:48).</summary>
    private float BackSpeedMin => _backSpeedMin.Value;

    /// <summary><c>tf_player_movement_restart_freeze</c>.</summary>
    private bool RestartFreeze => _restartFreeze.Value;

    public MovementParams? Constants => TFViewVectors.Params;

    /// <summary>
    /// Stock TF2 is built without <c>HL2_DLL</c>: its jump is the literal 289
    /// (tf_gamemovement.cpp:1315), which the constants' height answers only
    /// through the core's sqrt( 2 g h ), and its fall thresholds are
    /// shareddefs.h's non-HL2 ones.
    /// </summary>
    public bool NoHl2Arms => true;

    /// <summary>
    /// The ground jump is the constants' (289 at gravity 800); no scale — but
    /// the weapon's word can refuse it outright, the drawn bow's
    /// <c>OwnerCanJump</c> stock asks at CheckJumpButton's head
    /// (tf_gamemovement.cpp:1210) before anything is pushed.
    /// </summary>
    public float JumpScale(in JumpRequest request)
        => !_owners.OwnerCanJump(request.PlayerIndex) ? 0f : 1f;

    public float FrictionScale(int playerIndex, float friction, float speed, bool ducking, int groundMaterial)
        => friction;

    public float MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial)
    {
        if (_players.Predicted(playerIndex) is not { } state || state.Class() == TFClass.Undefined)
            return maxSpeed;

        // The restart freeze (tf_player_shared.cpp:10819, :10847): while the
        // round restarts and the convar stands, CanPlayerMove answers no and
        // the speed walk answers 1 (:10872-10874, "this can't return 0
        // because other parts of the code interpret that as use default
        // speed"). InRoundRestart is the state check alone
        // (teamplayroundbased_gamerules.h:179); the client reads the same
        // state from the resource's word. Stock's debug, ready-status and
        // competitive re-arms of the flag and the match-countdown,
        // CYOA-PDA and observer arms of CanPlayerMove are not here.
        if (_rounds.State == Rules.RoundState.Preround && RestartFreeze)
            return 1f;

        // The player's own attribute manager roots the speed hooks
        // (CALL_ATTRIB_HOOK_FLOAT on the player). Outside a blast jump the
        // walk's answer is the whole answer; the Mantreads' arm beside it
        // scales the same call (below), so both bindings feed the rules the
        // same call the same way.
        if (!state.InCond(TFCond.Blastjumping))
            return TFMaxSpeed.Calculate(_classes, Inputs(playerIndex, state), CloakedCap,
                                        _hooks, _owners.ManagerOf(playerIndex),
                                        PeerInputs, playerIndex, _owners.ActiveItemOf(playerIndex));

        // The Mantreads' air control (tf_gamemovement.cpp:2081-2094): the blast
        // jump's condition alone gates the scale — stock asks it inside
        // GetAirAcceleration, which runs in the air, and the condition stands
        // only behind a blast until landing (TFBlastJump arms it at :8701,
        // clears it at :8710), so this answer carries the loop's air wishspeed
        // cap exactly while blast-jumping and the ground cap outside it. The
        // scale is stock's CALL_ATTRIB_HOOK_FLOAT_ON_OTHER over the player: the
        // manager is the root, as for the speed hooks above.
        float speed = TFMaxSpeed.Calculate(_classes, Inputs(playerIndex, state), CloakedCap,
                                           _hooks, _owners.ManagerOf(playerIndex),
                                           PeerInputs, playerIndex, _owners.ActiveItemOf(playerIndex));
        return _hooks.Float(speed, "mod_air_control_blast_jump", _owners.ManagerOf(playerIndex));
    }

    /// <summary>
    /// The carrier's inputs with the sword's speed over them
    /// (<c>GetSwordSpeedMod</c>, tf_weapon_sword.cpp:295-309): the demoman's
    /// owned sword, decapitating, at <see cref="TFSwordHook.MaxHeads"/> heads
    /// or fewer — 1 otherwise, the input's default.
    /// </summary>
    private TFMoveInputs Inputs(int playerIndex, TFPlayerNetState state)
    {
        var inputs = TFMoveInputs.Of(state);

        // The worn shield (tf_player_shared.cpp:11005): the shared's
        // IsShieldEquipped, the speed walk's shield-toll gate.
        if (_owners.ShieldEquipped(playerIndex))
            inputs = inputs with { ShieldEquipped = true };

        // THE BFB'S HYPE SPEED (tf_player_shared.cpp:11080-11086): the scout
        // owning the Baby Face's Blaster runs at the meter's scale — stock's
        // Weapon_OwnsThisID(TF_WEAPON_PEP_BRAWLER_BLASTER) gate and class arm,
        // the remap the input carries (1 without the item, the walk's no-op).
        if (inputs.Class == TFClass.Scout && _owners.ItemOf(playerIndex, TFHype.BrawlerScript) is not null)
            inputs = inputs with { HypeSpeedScale = TFHype.SpeedScale(state.HypeMeter) };

        // THE SHOVEL'S HELD SPEED (CTFShovel::GetSpeedMod, tf_weapon_shovel.cpp:123-143):
        // the held SHOVEL_SPEED_BOOST shovel — the Escape Plan's set_weapon_mode 2,
        // which CTFShovel::HasSpeedBoost reads through GetShovelType's hook over the
        // held item (tf_weapon_shovel.h:42-44); the Equalizer's mode is the damage
        // boost and leaves the walk at one. The tiers ride the owner's health ratio
        // (tf_weapon_shovel.cpp:132-142), the held id opens TFMaxSpeed's multiply
        // arm exactly as stock's GetActiveTFWeapon gate does (:10956-10959).
        if (_weapons.ActiveWeapon(playerIndex)?.Id == TFWeaponId.Shovel
            && _owners.ActiveItemOf(playerIndex) is { } held
            && _hooks.Int(0, "set_weapon_mode", held) == ShovelSpeedBoostMode)
        {
            float maxHealth = _classes[state.Class()].MaxHealth;
            float health = _entities.GetHealth(new EntityHandle((uint)playerIndex));
            inputs = inputs with
            {
                ActiveWeapon = TFWeaponId.Shovel,
                WeaponSpeedMod = ShovelSpeedTier(maxHealth > 0f ? health / maxHealth : 1f),
            };
        }

        if (inputs.Class != TFClass.Demoman
            || _owners.ItemOf(playerIndex, TFSwordHook.SwordScript) is not { } sword
            || _hooks.Int(0, "decapitate_type", sword) == 0)
            return inputs;

        return inputs with { SwordSpeedMod = 1f + MathF.Min(TFSwordHook.MaxHeads, state.Decapitations)
                                                       * TFSwordHook.SpeedPerHead };
    }

    /// <summary>
    /// A second player's speed environment, the medic's match's patient
    /// (<c>pHealTarget-&gt;TeamFortress_CalculateMaxSpeed</c>): the same
    /// carrier rule as the caller's own — the predicted state, the networked
    /// copy behind it — and the same hook roots. The patient's arms are
    /// then the caller's arms, on either side.
    /// </summary>
    private TFPeerInput? PeerInputs(int playerIndex)
    {
        if ((_players.Predicted(playerIndex) ?? _players.Networked(playerIndex)) is not { } state)
            return null;

        return new TFPeerInput(playerIndex, Inputs(playerIndex, state),
                               _hooks, _owners.ManagerOf(playerIndex),
                               _owners.ActiveItemOf(playerIndex));
    }

    public float AirJump(in JumpRequest request)
    {
        // The drawn bow holds the dash with the jump: stock's gate is the same
        // CanJump, asked before the air-dash branch (tf_gamemovement.cpp:1210,
        // the dash at :1240 comes after it).
        if (!_owners.OwnerCanJump(request.PlayerIndex))
            return 0f;

        if (_players.Predicted(request.PlayerIndex) is not { } state)
            return 0f;

        // The B.A.S.E. Jumper's toggle (tf_gamemovement.cpp:1132-1154): the airborne
        // jump press puts BOTH conditions on when the player answers parachute_attribute
        // (:1150-1151) — stock's CALL_ATTRIB_HOOK_INT_ON_OTHER over the player (:1135),
        // so the manager is the root, as for the blast-jump scale above; the same root
        // answers the disabled attr (:1146-1147). A second press takes ACTIVE off and
        // keeps DEPLOYED (:1139-1141), and while the deployed record stands a further
        // press does not reopen: tf_parachute_deploy_toggle_allowed ships off (:58), so
        // the record alone gates the reopen (:1148) — it is the landing's remover
        // (tf_player_shared.cpp:3131, held by TFWeaponBehaviour) that clears the pair
        // for the next airborne window. Stock's !bOnGround (:1145) holds by this call's
        // own frame: AirJump answers only an airborne press. In the predicted state's
        // own words (:1143); the dash behind it runs as before.
        var chuteManager = _owners.ManagerOf(request.PlayerIndex);
        if (_hooks.Int(0, "parachute_attribute", chuteManager) != 0)
        {
            // Stock stamps the pair through the player's one condition set
            // (:1150-1151's AddCond, :1139's RemoveCond), which holds a
            // permanent add over the next ConditionGameRulesThink (:1144's
            // PERMANENT_CONDITION). Writing the carrier's words alone does
            // not: that tick sweeps a bit whose tracked duration is absent,
            // so the pair must go onto the player's set itself. Where the
            // side answers no set (a bare chain, a fixture), the state's own
            // words stand in for one, read-and-write alike (:1143).
            var conditions = _owners.ConditionsOf(request.PlayerIndex)
                             ?? new TFConditions(authoritative: true, new TFCarrierCondWords(state));

            if (conditions.Has(TFCond.ParachuteActive))                          // :1139-1141
            {
                conditions.Remove(TFCond.ParachuteActive);
            }
            else if (_hooks.Int(0, "parachute_disabled", chuteManager) == 0      // :1146-1147
                     && !conditions.Has(TFCond.ParachuteDeployed))                // :1148
            {
                conditions.Add(TFCond.ParachuteActive);                          // :1150
                conditions.Add(TFCond.ParachuteDeployed);                        // :1151
            }
        }

        // The no-dash attribute walks the player's own manager, the dash count
        // and the weapon jump height the active weapon's item (the ON_OTHER
        // roots).
        float dash = TFJump.AirDash(state.Class(), state.Conditions(), state.AirDash, DashCount,
                                    _owners.ActiveItemOf(request.PlayerIndex),
                                    _hooks, _owners.ManagerOf(request.PlayerIndex));
        if (dash > 0f)
        {
            state.AirDash++;

            // THE BFB'S DASH TOLL (CTFGameMovement::AirDash's hype loss,
            // tf_gamemovement.cpp:1007-1016): the held item's
            // hype_resets_on_jump is the hype the dash pays, through the
            // setter's refusal and clamp; stock's TeamFortress_SetSpeed is
            // the derived speed's own re-ask.
            int toll = _hooks.Int(0, TFHype.JumpResetAttribute,
                                  _owners.ActiveItemOf(request.PlayerIndex));
            if (toll != 0)
                TFHype.Lose(state, toll);
        }
        return dash;
    }

    public void Grounded(int playerIndex)
    {
        // The landing resets ride the dash's (:2956-2958): the scattergun
        // shove's latch and the gun-knock bit go with it, so the next airtime
        // shoves again.
        if (_players.Predicted(playerIndex) is { } state)
        {
            if (state.AirDash != 0)
                state.AirDash = 0;
            state.ScattergunJump = false;
            state.WeaponKnockback = false;
        }
    }

    /// <summary>
    /// The parachute's fall trim (<c>CTFGameMovement::FullWalkMove</c>,
    /// tf_gamemovement.cpp:2626-2638): while the parachute stands and the
    /// player falls, z clamps against the burning cap under fire else the
    /// plain one (:2628), and the xy is dampened rather than clamped — past
    /// the drag the surplus eases by a third less ten (:2634-2635), each axis
    /// then clamped to that widened bound (:2637-2638).
    /// </summary>
    public Vector ParachuteFallCap(int playerIndex, Vector velocity)
    {
        // The gate: parachute out and falling (:2626).
        if (_players.Predicted(playerIndex) is not { } state
            || !state.Conditions().Has(TFCond.ParachuteActive)
            || velocity.Z >= 0f)
            return velocity;

        // The z's Max against the cap (:2628), the burning one under fire.
        float cap = state.Conditions().Has(TFCond.Burning) ? ParachuteOnfireZ : ParachuteZ;
        float z = MathF.Max(velocity.Z, cap);

        // The xy damp (:2630-2638): the reduction bites only past the drag,
        // and each axis then clamps to its own widened bound.
        float drag = ParachuteXY;
        float speedX = MathF.Abs(velocity.X);
        float speedY = MathF.Abs(velocity.Y);
        float reductionX = speedX > drag ? (speedX - drag) / 3f - 10f : 0f;
        float reductionY = speedY > drag ? (speedY - drag) / 3f - 10f : 0f;

        return new Vector(ClampAxis(velocity.X, drag + reductionX),
                          ClampAxis(velocity.Y, drag + reductionY),
                          z);

        // Valve's Clamp(value, -bound, bound) at :2637-2638.
        static float ClampAxis(float value, float bound) => MathF.Min(MathF.Max(value, -bound), bound);
    }

    public float EyeHeight(int playerIndex, float standing)
        => _players.Predicted(playerIndex) is { } state
            ? TFViewVectors.ClassEyeHeight(state.Class(), standing)
            : standing;

    /// <summary>
    /// <c>CTFGameMovement::CategorizePosition</c>'s shape
    /// (tf_gamemovement.cpp:2334-2459) against the base's
    /// (gamemovement.cpp:3794-3931), as the one record: the observer refusal
    /// first (:2337), any ladder off the ground (:2351), the 250 rise
    /// (:2358) without the base's lift relief (:3836-3850), the step-size
    /// probe and its endpos snap (:2376-2384, :2440-2454), and no player
    /// surface material block (the base's :3889-3915 is not in the override).
    /// Unconditional: every field is a fact about the build, not the player.
    /// </summary>
    public CategorizePositionRules? CategorizePosition(int playerIndex) => new(
        ObserverFirst: true,
        GroundlessLadder: true,
        RiseVelocity: 250f,
        GroundVelocityRelief: false,
        StepSizeProbe: true,
        PlayerSurfaceMaterial: false);

    /// <summary>
    /// The override's two condition arms (tf_gamemovement.cpp:2392-2418), the
    /// footing asked with the ground trace's plane. The air-current friction
    /// (:2392-2395) and lost footing's takeoff, slide friction and restick
    /// (:2397-2418). The restick clears the condition's word in the predicted
    /// state itself — the mask-level half of <c>CTFPlayerShared::RemoveCond</c>
    /// (tf_player_shared.cpp:1181): nothing arms on or hooks LostFooting today
    /// (TFConditionArms's switch has no case, no ITFConditionHook is
    /// registered for it), and a re-add refreshes the duration, so the stale
    /// expiry entry the words-level clear leaves is inert. Null with neither
    /// condition: no arm, the friction untouched — the base's own answer.
    /// </summary>
    public FootingAnswer? Footing(int playerIndex, in Vector velocity, in Vector planeNormal)
    {
        if (_players.Predicted(playerIndex) is not { } state)
            return null;

        bool airCurrent = state.InCond(TFCond.AirCurrent);
        bool lostFooting = state.InCond(TFCond.LostFooting);
        if (!airCurrent && !lostFooting)
            return null;

        float groundFriction = 1f, airFriction = 1f;
        bool inAir = false;

        if (airCurrent)
            airFriction *= _airCurrentFriction.Value;             // :2394

        if (lostFooting)
        {
            // The away-from-ground takeoff, then the slide-or-restick pair
            // (:2400-2416): the velocity along the plane tells standing from
            // leaving, and the across-plane speed against the restick convar
            // tells sliding from stopped.
            float along = velocity.Dot(planeNormal);              // :2400
            if (along > 0f)
            {
                inAir = true;                                     // :2403
            }
            else
            {
                Vector across = velocity - planeNormal * along;   // :2407
                if (across.Length >= _lostFootingRestick.Value)
                    groundFriction *= _lostFootingFriction.Value;            // :2410
                else
                    ClearCond(state, TFCond.LostFooting);         // :2415
            }
        }

        return new FootingAnswer(inAir, groundFriction, airFriction);
    }

    /// <summary>
    /// The rise arm's enemy-blast read (tf_gamemovement.cpp:2360-2364), asked
    /// by the loop only when the side is the server's: the carrier's blast-jump
    /// component answers, through the player data's <c>EnemyBlasted</c>. The
    /// client's player data inherits the interface's nothing and the loop's
    /// side gate skips the ask anyway.
    /// </summary>
    public void EnemyBlasted(int playerIndex) => _players.EnemyBlasted(playerIndex);

    /// <summary>
    /// The end of one command (tf_gamemovement.cpp:334-336): the carrier drops
    /// its blast latch with the command that could claim it.
    /// </summary>
    public void EndCommand(int playerIndex) => _players.EndCommand(playerIndex);

    /// <summary>
    /// The charge end the command's slow walk names (tf_gamemovement.cpp:
    /// 240-245): the loop read the speed against the 300 floor; the end is
    /// the carrier's <c>EndCharge</c>, whose own gates stand behind it.
    /// </summary>
    public void ChargeSlowEnd(int playerIndex) => _players.EndCharge(playerIndex);

    /// <summary>
    /// The charge end the blocked step names (tf_gamemovement.cpp:1925-1929):
    /// <c>ToTFPlayer</c> over the hit first — the world and every non-player
    /// the trace stopped on ends nothing — then the same <c>EndCharge</c>.
    /// </summary>
    public void ChargeBump(int playerIndex, EntityHandle hit)
    {
        if (!_players.IsPlayerEntity(hit))
            return;

        _players.EndCharge(playerIndex);
    }

    /// <summary>The words-level condition clear the restick is.</summary>
    private static void ClearCond(TFPlayerNetState state, TFCond cond)
    {
        var bits = state.Conditions();
        bits.Clear(cond);
        state.SetConditions(in bits);
    }

    /// <summary><c>SHOVEL_SPEED_BOOST</c>, the Escape Plan's shovel mode
    /// (<c>tf_weapon_shovel.h:23</c>); the Equalizer's <c>SHOVEL_DAMAGE_BOOST</c>
    /// is 1 and never reaches the speed tiers.</summary>
    private const int ShovelSpeedBoostMode = 2;

    /// <summary>
    /// <c>CTFShovel::GetSpeedMod</c>'s tiers over the owner's health ratio
    /// (tf_weapon_shovel.cpp:133-142): one above 80%, then 1.1, 1.2, 1.4 and
    /// 1.6 by the tenths below — the slower bands only as the health goes.
    /// </summary>
    private static float ShovelSpeedTier(float healthRatio) => healthRatio switch
    {
        > 0.8f => 1f,
        > 0.6f => 1.1f,
        > 0.4f => 1.2f,
        > 0.2f => 1.4f,
        _ => 1.6f,
    };

    /// <summary>
    /// Stock's <c>BUNNYJUMP_MAX_SPEED_FACTOR</c> (tf_gamemovement.cpp:1087):
    /// <see cref="BunnyJumpCap"/> is this times the max speed, the whole jump
    /// cropped to it as <c>PreventBunnyJumping</c> does (:1092-1112).
    /// </summary>
    private const float BunnyJumpMaxSpeedFactor = 1.2f;

    /// <summary>
    /// The bunny-jump cap is 1.2 times the speed the loop arrived at — stock
    /// reads the player's <c>m_flMaxspeed</c>, already scaled by the
    /// <see cref="MaxSpeedScale"/> answer above and carried in the command, and
    /// asks nothing else, so the crop is decided alike on both sides.
    /// </summary>
    public float BunnyJumpCap(int playerIndex, float maxSpeed) => BunnyJumpMaxSpeedFactor * maxSpeed;

    /// <summary>
    /// The backpedal clamp is stock's <c>tf_clamp_back_speed</c> pair
    /// (0.9 / 100, tf_gamemovement.cpp:47-48), read by its WalkMove's closing
    /// block (:1830-1863) without asking anything of the player — the minimum
    /// exists "to not penalize deployed snipers or heavies" (:1832). The loop
    /// owns the arithmetic; the rule only hands over the two convar values.
    /// </summary>
    public BackpedalLimit? BackpedalLimit(int playerIndex, float maxSpeed)
        => new(BackSpeedFraction, BackSpeedMin);

    /// <summary>
    /// Stock's WalkMove trims the walked xy back to the max every command,
    /// right after Accelerating (tf_gamemovement.cpp:1808-1815), unconditionally
    /// and before the back clamp — HL2 leaves the excess.
    /// </summary>
    public bool ClampXY(int playerIndex) => true;

    /// <summary>
    /// Stock comments both <c>StayOnGround</c> calls of the walk out
    /// (tf_gamemovement.cpp:1906, :1944-1945) — its own
    /// <see cref="CategorizePosition"/> answer covers the ground test — so a
    /// clear walk step skips the two downward probes.
    /// </summary>
    public bool KeepGround(int playerIndex) => false;

    /// <summary>
    /// Stock's duck easing is <c>TIME_TO_DUCK</c> = 0.2 under TF_DLL
    /// (shareddefs.h:105-113; HL2's is 0.4), and <c>TIME_TO_UNDUCK</c> is 0.2
    /// everywhere, so every easing site takes the same fifth-of-a-second. The
    /// duck-JUMP timer pair (the GAME_DLL writes at tf_gamemovement.cpp:3295's
    /// SetDuckTimer, the TF_TIME_TO_DUCK 0.3 cooldown) is not this easing and
    /// stays on the constants.
    /// </summary>
    public float DuckSeconds(int playerIndex, float seconds) => TFTimeToDuck;

    /// <summary>Stock's TIME_TO_DUCK/TIME_TO_UNDUCK pair under TF_DLL.</summary>
    private const float TFTimeToDuck = 0.2f;

    /// <summary>
    /// The fade trio of one player (<c>CTFPlayerShared</c>'s
    /// <c>m_flStunLerpTarget</c> / <c>m_flLastMovementStunChange</c> /
    /// <c>m_bStunNeedsFadeOut</c>, the members StunMove mutates at
    /// tf_gamemovement.cpp:569-574 and :588-609). Not predicted state: stock's
    /// trio is in no send, recv or prediction table, and the prediction never
    /// restores it — every replayed StunMove mutates it alike.
    /// </summary>
    private readonly Dictionary<int, (float Target, float LastChange, bool FadeOut)> _stunLerps = [];

    /// <summary>
    /// <c>CTFGameMovement::StunMove</c> (tf_gamemovement.cpp:537-627), the
    /// command rewrite only — asked by the core's
    /// <see cref="GameMovement.ProcessMovement"/> where stock calls it
    /// (:317, return ignored, before PlayerMove at :329). It decides from the
    /// carrier's stun mirror (the four plain fields the server's
    /// <c>UpdateLegacyStunSystem</c> copies, tf_player_shared.cpp:7441-7465):
    /// the index and the condition say WHETHER there is a stun at all, exactly
    /// as stock's gates stand on them, and the stale value fields behind a
    /// cleared index answer nothing.
    /// <list type="bullet">
    /// <item>The control / loser-state seizure (:539-562): a control-stunned
    /// player gets zero buttons (:546) and zero moves (:554-559); a
    /// loser-state one, buttons alone. Stock writes the zeroed buttons back
    /// to the ENTITY too (:561), which is how a stun stops a held trigger's
    /// fire — this build's weapons read the command's buttons at the source
    /// (Arsenal's RunCommand), so the seizure covers the movement and the
    /// fire-stop is deferred with the heavy arm (row 28). Stock's heavy-
    /// minigun exception (:548-552) is the same deferral.</item>
    /// <item>The movement stun (:565-584): the moves scaled by the amount
    /// — <c>GetAmountStunned</c>'s gate (:9938-9947: the condition, the
    /// <c>TF_STUN_MOVEMENT</c> bit and the expiry, the amount clamped into
    /// 0..1) — and <c>TF_STUN_MOVEMENT_FORWARD_ONLY</c> zeroing the forward
    /// move with it (:578-581). The pair records its target, and the same
    /// target across commands keeps the record's bookkeeping untouched
    /// (:569-574).</item>
    /// <item>The fade-out (:585-612): once the amount gate says zero, a
    /// recorded last-change runs the quarter-second lerp —
    /// <c>RemapValClamped(now - lastChange, 0.2, 0.0, 0.0, 1.0)</c> (:594,
    /// the reversed band: full at the change, none 0.2 s later), the
    /// forward-only zero still applying (:600-603), and the record cleared
    /// when the band runs out (:605-609). A fade noticed on the tick it
    /// starts re-arms the change clock first (:588-592), so that tick still
    /// scales by the full target.</item>
    /// </list>
    /// Null when no arm ran: nobody is stunned and nothing is fading — stock's
    /// <c>return false</c> (:626), which its caller ignores anyway. The CYOA-
    /// PDA / match-countdown block (:614-624) and the heavy's button exception
    /// (:548-552) are the deferred arms (row 28).
    /// </summary>
    public MovementStun? MovementStun(int playerIndex, float curTime, int buttons,
                                      float forwardMove, float sideMove, float upMove)
    {
        if (_players.Predicted(playerIndex) is not { } state)
            return null;

        // Stock's gates all stand on the active entry and the condition:
        // IsControlStunned / IsLoserStateStunned (:9952-9975) want
        // GetActiveStunInfo() AND InCond(TF_COND_STUNNED) AND the flag bit,
        // WITHOUT the expiry — a control stun holds past its expire time
        // until the server's ConditionThink clears it — and the value fields
        // only mean something behind the index.
        bool stunned = state.StunIndex >= 0 && state.InCond(TFCond.Stunned);
        var flags = stunned ? (TFStunFlags)state.StunFlags : TFStunFlags.None;

        int newButtons = buttons;
        float f = forwardMove, s = sideMove, u = upMove;
        bool answered = false;

        // Handle control stun (:539-562).
        if (stunned && (flags & (TFStunFlags.Controls | TFStunFlags.LoserState)) != 0)
        {
            newButtons = 0;                                   // Can't fire or select weapons (:546)
            if ((flags & TFStunFlags.Controls) != 0)          // (:554)
                f = s = u = 0.0f;                             // (:556-558)
            answered = true;
        }

        // Handle movement stuns (:564-612): GetAmountStunned (TF_STUN_MOVEMENT)
        // (:9938-9947), the clamp landing where stock's remap put the 0..255.
        float amount = stunned && (flags & TFStunFlags.Movement) != 0
                       && state.StunExpireTime > curTime
            ? Math.Clamp(state.MovementStunAmount, 0f, 255f) * (1f / 255f)
            : 0f;

        _stunLerps.TryGetValue(playerIndex, out var lerp);

        if (amount != 0f)
        {
            if (lerp.Target != amount)                       // (:569-574)
                lerp = (amount, curTime, FadeOut: true);
            _stunLerps[playerIndex] = lerp;

            f *= 1f - amount;                                // (:576-577)
            s *= 1f - amount;
            if ((flags & TFStunFlags.MovementForwardOnly) != 0)
                f = 0f;                                      // (:578-581)

            return new MovementStun(newButtons, f, s, u);    // return true (:583)
        }
        else if (lerp.LastChange != 0f)                      // (:585)
        {
            // Lerp out to normal speed.
            if (lerp.FadeOut)                                // (:588-592)
                lerp = (lerp.Target, curTime, FadeOut: false);

            // The reversed band: the full target at the change, none at +0.2 (:594).
            float curStun = CritBucket.RemapValClamped(curTime - lerp.LastChange, 0.2f, 0f, 0f, 1f);
            if (curStun != 0f)
            {
                float remap = lerp.Target * curStun;         // (:597-599)
                f *= 1f - remap;
                s *= 1f - remap;
                if ((flags & TFStunFlags.MovementForwardOnly) != 0)
                    f = 0f;                                  // (:600-603)
            }
            else
            {
                lerp = default;                              // (:605-609)
            }

            _stunLerps[playerIndex] = lerp;
            return new MovementStun(newButtons, f, s, u);    // return true (:611)
        }

        return answered ? new MovementStun(newButtons, f, s, u) : null;
    }

    /// <summary>
    /// <c>CTFGameMovement::ChargeMove</c> (tf_gamemovement.cpp:484-532), the
    /// charging demoman's command: the max speed and the forward move both
    /// <c>tf_max_charge_speed</c> (:508, :513), the side and up moves zeroed
    /// (:514-515), and the buttons held to the charge's own attack2 — or
    /// nothing, plus an attack the command's old buttons carry (:510-529),
    /// which is the held swing landing mid-charge. Asked from the carrier's
    /// predicted state, so the server's run and every replay decide alike.
    ///
    /// <para>
    /// The Quick-Fix medic's healing arm (:488-506) waits for the medigun's
    /// heal-target read (the medic lane's row, docs/plans/tf2/deferred.tsv): a
    /// medic riding a patient's charge is not this build's question.
    /// </para>
    /// </summary>
    public MovementCharge? MovementCharge(int playerIndex, int buttons,
                                          float forwardMove, float sideMove, float upMove)
    {
        if (_players.Predicted(playerIndex) is not { } state || !state.InCond(TFCond.ShieldCharge))
            return null;

        int rewritten = (buttons & GameMovement.InAttack2) != 0 ? GameMovement.InAttack2 : 0;   // :516-524
        if ((buttons & GameMovement.InAttack) != 0)                                             // :526-529
            rewritten |= GameMovement.InAttack;

        return new MovementCharge(ChargeSpeed, rewritten, ChargeSpeed, 0f, 0f);
    }

    /// <summary>
    /// Stock's <c>m_flCurrentTauntMoveSpeed</c>: the running speed of the
    /// sliding taunt's accel ramp (:683-690), zeroed whenever the sliding arm
    /// does not run (:712). Entity-local like the stun's fade trio above: no
    /// table carries it, every replayed TauntMove mutates it alike.
    /// </summary>
    private readonly Dictionary<int, float> _tauntSpeeds = [];

    /// <summary>
    /// <c>CTFGameMovement::TauntMove</c> (tf_gamemovement.cpp:633-716): asked
    /// right after the stun's where stock orders them (:320, return ignored).
    /// <list type="bullet">
    /// <item>The kart's arm (:635-638) hands the ride to <c>VehicleMove</c>,
    /// the kart's own deferred row: the kart keeps its command here.</item>
    /// <item>The sliding arm (:639-708) runs when the player is taunting and
    /// <c>CanMoveDuringTaunt</c> says so (tf_player_shared.cpp:13116-13152):
    /// stock's own two answers — the sliding-taunt convar (GAME_DLL-only,
    /// :13133-13138) and the item's <c>m_bAllowMoveDuringTaunt</c> — need the
    /// convar and item-flag readers, so the thriller's replicated condition
    /// carries the arm here until those rows land. The analog input normalized
    /// by the side's speed caps (:651-674, whose 450 defaults are fixed here),
    /// the accel ramp to the item's speed (:683-690, which the item getters
    /// default to zero speed and zero ramp, tf_player.cpp:1041-1044), the
    /// spline on the ratio (:698-702), and the three writes on the max speed,
    /// its client copy and the forward move (:704-706).</item>
    /// <item>Everything else is stock's fall-through (:710-715): zero the
    /// current speed, leave the command as sent.</item>
    /// </list>
    /// </summary>
    public MovementTaunt? MovementTaunt(int playerIndex, float frameTime, float forwardMove)
    {
        if (_players.Predicted(playerIndex) is not { } state)
            return null;

        // The kart's VehicleMove arm (:635-638), deferred with the kart itself.
        if (state.InCond(TFCond.HalloweenKart))
            return null;

        // The sliding gate (:639 + CanMoveDuringTaunt): the thriller is what
        // this build can ask; the convar and item-flag arms wait in the
        // deferred rows. Stock's CanPlayerMove zero (:692-696) rides along
        // with them — its reader is the CYOA-PDA's.
        if (!state.InCond(TFCond.Taunting) || !state.InCond(TFCond.HalloweenThriller))
        {
            _tauntSpeeds[playerIndex] = 0f;                   // (:712)
            return null;
        }

        // The analog input, normalized by the speed caps and clamped to [-1,1]
        // (:651-674). A client that set its caps elsewhere is the documented
        // divergence; both sides replay the same command, so they divide alike.
        float dir = forwardMove > 0f ? forwardMove / 450f
                  : forwardMove < 0f ? forwardMove / 450f
                                     : 0f;
        if (dir > 1f)
            dir = 1f;                                         // (:667-669)
        else if (dir < -1f)
            dir = -1f;                                        // (:671-673)

        float maxSpeed = 0f;                                  // GetTauntMoveSpeed (:644)
        float accel = 0f;                                     // GetTauntMoveAcceleration (:645)
        float current = _tauntSpeeds.GetValueOrDefault(playerIndex);
        float sign = dir != 0f ? 1f : -1f;                    // bMoving/flSign (:681-682)
        current = accel > 0f
            ? Math.Clamp(current + sign * (frameTime / accel) * maxSpeed, 0f, maxSpeed)   // (:685)
            : maxSpeed;                                       // (:689)
        _tauntSpeeds[playerIndex] = current;

        float smooth = maxSpeed > 0f
            ? SimpleSpline(current / maxSpeed) * maxSpeed     // (:698-702)
            : 0f;

        return new MovementTaunt(maxSpeed, dir * smooth, maxSpeed);   // (:704-706)
    }

    /// <summary>Stock's <c>SimpleSpline</c>: <c>t * t * (3 - 2 * t)</c>.</summary>
    private static float SimpleSpline(float t) => t * t * (3f - 2f * t);
}
