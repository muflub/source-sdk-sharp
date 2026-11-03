using SourceSharp.Abi;
using SourceSharp.Shared;

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
    private readonly TFScoutAirDashCountConVar? _dashCount;
    private readonly TFSpyMaxCloakedSpeedConVar? _cloakedCap;
    private readonly ITFWeaponOwners? _owners;
    private readonly AttributeHooks? _hooks;
    private readonly TFClampBackSpeedConVar? _backSpeed;
    private readonly TFClampBackSpeedMinConVar? _backSpeedMin;
    private readonly Rules.ITFRoundStateView? _rounds;
    private readonly TFPlayerMovementRestartFreezeConVar? _restartFreeze;

    public TFMovementRules(TFClassTable classes, ITFPlayerData players,
                           TFScoutAirDashCountConVar? dashCount = null,
                           TFSpyMaxCloakedSpeedConVar? cloakedCap = null,
                           ITFWeaponOwners? owners = null, AttributeHooks? hooks = null,
                           TFClampBackSpeedConVar? backSpeed = null,
                           TFClampBackSpeedMinConVar? backSpeedMin = null,
                           Rules.ITFRoundStateView? rounds = null,
                           TFPlayerMovementRestartFreezeConVar? restartFreeze = null)
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
    }

    /// <summary><c>tf_scout_air_dash_count</c> (default 1).</summary>
    private int DashCount => _dashCount?.Value ?? 1;

    /// <summary><c>tf_spy_max_cloaked_speed</c> (default 999).</summary>
    private float CloakedCap => _cloakedCap?.Value ?? 999f;

    /// <summary><c>tf_clamp_back_speed</c> (default 0.9, tf_gamemovement.cpp:47).</summary>
    private float BackSpeedFraction => _backSpeed?.Value ?? 0.9f;

    /// <summary><c>tf_clamp_back_speed_min</c> (default 100, tf_gamemovement.cpp:48).</summary>
    private float BackSpeedMin => _backSpeedMin?.Value ?? 100f;

    /// <summary><c>tf_player_movement_restart_freeze</c> (default on).</summary>
    private bool RestartFreeze => _restartFreeze?.Value ?? true;

    public MovementParams? Constants => TFViewVectors.Params;

    /// <summary>
    /// Stock TF2 is built without <c>HL2_DLL</c>: its jump is the literal 289
    /// (tf_gamemovement.cpp:1315), which the constants' height answers only
    /// through the core's sqrt( 2 g h ), and its fall thresholds are
    /// shareddefs.h's non-HL2 ones.
    /// </summary>
    public bool NoHl2Arms => true;

    /// <summary>The ground jump is the constants' (289 at gravity 800); no scale.</summary>
    public float JumpScale(in JumpRequest request) => 1f;

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
        if (_rounds?.State == Rules.RoundState.Preround && RestartFreeze)
            return 1f;

        // The player's own attribute manager roots the speed hooks
        // (CALL_ATTRIB_HOOK_FLOAT on the player): null on a side that holds
        // none, both arms then the value unchanged.
        return TFMaxSpeed.Calculate(_classes, TFMoveInputs.Of(state), CloakedCap,
                                    _hooks, _owners?.ManagerOf(playerIndex),
                                    PeerInputs, playerIndex);
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

        return new TFPeerInput(playerIndex, TFMoveInputs.Of(state),
                               _hooks, _owners?.ManagerOf(playerIndex));
    }

    public float AirJump(in JumpRequest request)
    {
        if (_players.Predicted(request.PlayerIndex) is not { } state)
            return 0f;

        // The no-dash attribute walks the player's own manager, the dash count
        // and the weapon jump height the active weapon's item (the ON_OTHER
        // roots); null on a side that holds none, every hook then unset.
        float dash = TFJump.AirDash(state.Class(), state.Conditions(), state.AirDash, DashCount,
                                    _owners?.ActiveItemOf(request.PlayerIndex),
                                    _hooks, _owners?.ManagerOf(request.PlayerIndex));
        if (dash > 0f)
            state.AirDash++;
        return dash;
    }

    public void Grounded(int playerIndex)
    {
        if (_players.Predicted(playerIndex) is { AirDash: not 0 } state)
            state.AirDash = 0;
    }

    public float EyeHeight(int playerIndex, float standing)
        => _players.Predicted(playerIndex) is { } state
            ? TFViewVectors.ClassEyeHeight(state.Class(), standing)
            : standing;

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
