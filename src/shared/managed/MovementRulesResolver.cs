//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A mod's answer to the three movement DECISIONS, and optionally its own
/// movement constants (jump height, gravity, the hulls).
///
/// <para>
/// <b>SINGLE OWNER, on purpose.</b> Unlike the chains that take every
/// registered hook (<see cref="IHooks{T}"/>'s <c>Each</c>), movement constants
/// have exactly one owner: two mods cannot both decide how high a jump is, and
/// the last one registered winning is a bug neither author chose. Zero hooks
/// is stock; one is that mod's; two is a startup error that names both. The
/// resolver is built per side over <see cref="IHooks{T}"/>&lt;<see cref="IMovementRules"/>&gt;
/// on both sides, so the chain a player's jump obeys on the server is the same
/// object graph the client's replay obeys — the same rules, resolved the same
/// way, is what keeps prediction from rubber-banding.
/// </para>
///
/// <para>
/// <b>A rule may decide only from what it is handed.</b> Each method runs on
/// the server and again on the client for every unacknowledged command, so
/// anything it reads that the client cannot see — a counter only the server
/// keeps, a random roll — makes the two disagree, and the symptom is the player
/// dragged back to where the server thought they were. The arguments are the
/// whole of what a rule may know.
/// </para>
/// </summary>
public interface IMovementRules
{
    /// <summary>
    /// The mod's movement constants, or null for the core's stock ones.
    /// When present they replace <see cref="Movement.Current"/> at
    /// registration on BOTH sides, so a taller player, a lower gravity and a
    /// different eye height are one mod's statement, made once.
    /// </summary>
    MovementParams? Constants { get; }

    /// <summary>
    /// How much of a jump the player gets: 1 is the ordinary jump, 0 refuses
    /// it, and anything between is a weaker one.
    /// </summary>
    float JumpScale(in JumpRequest request);

    /// <summary>
    /// How slippery the ground is: the friction the engine was about to apply,
    /// scaled. Returning it unchanged is stock. The loop clamps the answer at
    /// zero.
    /// </summary>
    float FrictionScale(int playerIndex, float friction, float speed,
                        bool ducking, int groundMaterial);

    /// <summary>
    /// How fast a player may move right now: the speed the engine arrived at,
    /// scaled, asked LAST so this overrides the answer rather than competing
    /// with its inputs. The loop clamps the answer at zero.
    /// </summary>
    float MaxSpeedScale(int playerIndex, float maxSpeed, bool ducking, int groundMaterial);

    /// <summary>
    /// A jump pressed while the player is in the air, after the engine's own
    /// refusals (dead, swimming, the unduck transition, the button not
    /// released): the upward speed of the dash it grants, or 0 to refuse,
    /// which is the default and stock Half-Life. A positive answer replaces
    /// the player's velocity with the flat wish direction plus that speed
    /// straight up (stock's <c>CTFGameMovement::AirDash</c>).
    ///
    /// <para>
    /// A rule that allows only so many air jumps counts them in its OWN
    /// predicted per-player state: the answer must come out the same on the
    /// server's run and on every client replay, so the count must be state the
    /// client's prediction restores before a replay, and it is reset in
    /// <see cref="Grounded"/>.
    /// </para>
    /// </summary>
    float AirJump(in JumpRequest request) => 0f;

    /// <summary>
    /// The player stands on ground: told on every movement step that sets a
    /// ground entity, on both sides, which is where stock TF2 resets its
    /// air-dash count (<c>CTFGameMovement::SetGroundEntity</c>). Nothing by
    /// default.
    /// </summary>
    void Grounded(int playerIndex) { }

    /// <summary>
    /// The fastest a jump may leave: when the player's whole velocity at the
    /// jump is above this cap the loop crops it to it — stock TF2's
    /// <c>CTFGameMovement::PreventBunnyJumping</c>, which caps the jump at
    /// 1.2 * the max speed (tf_gamemovement.cpp:1086-1112). 0 or below means
    /// no cap, which is the default and stock Half-Life.
    /// </summary>
    float BunnyJumpCap(int playerIndex, float maxSpeed) => 0f;

    /// <summary>
    /// The mod's stock game was built without <c>HL2_DLL</c>: the loop runs
    /// the non-HL2 arms of shareddefs.h and gamemovement.cpp, so the fall
    /// thresholds are 580 and 1024, and a jump is sqrt( 2 g h ) rather than
    /// scaled from HL2's literal 160. False is the default and stock HL2MP.
    /// </summary>
    bool NoHl2Arms => false;

    /// <summary>
    /// The backpedal clamp the walk's closing block obeys: the fraction of the
    /// max speed a player travelling backwards may keep, and the speed under
    /// which nobody is penalised — stock TF2's <c>tf_clamp_back_speed</c> pair
    /// (0.9 / 100, tf_gamemovement.cpp:47-48, applied by WalkMove's closing
    /// block at :1830-1863). Null — no clamp — is the default and stock
    /// Half-Life.
    /// </summary>
    BackpedalLimit? BackpedalLimit(int playerIndex, float maxSpeed) => null;

    /// <summary>
    /// The stun's rewrite of the command about to be walked: the buttons and
    /// the three moves the mod's stun logic leaves behind — stock TF2's
    /// <c>CTFGameMovement::StunMove</c>, which seizes the controls of a
    /// control-stunned player and scales the moves of a movement-stunned one
    /// (tf_gamemovement.cpp:537-627, called by ProcessMovement at :317). Null
    /// — nobody is stunned, the command walks as sent — is the default and
    /// stock Half-Life. A rule that fades its answer over time keeps that fade
    /// state per side and restores nothing across replays — stock's fade trio
    /// (:569-574, :588-609) is an entity local that every replayed StunMove
    /// mutates alike. <c>curTime</c> is this side's clock for this command:
    /// the tick base on a client replay, the level time on the server's run.
    /// </summary>
    MovementStun? MovementStun(int playerIndex, float curTime, int buttons,
                               float forwardMove, float sideMove, float upMove) => null;

    /// <summary>
    /// The taunt's rewrite of the command about to be walked: the max speed,
    /// its client copy and the smoothed forward move the sliding taunt leaves
    /// behind — stock TF2's <c>CTFGameMovement::TauntMove</c>, which hands a
    /// sliding taunt its own speed and along-track forward move
    /// (tf_gamemovement.cpp:633-716, called by ProcessMovement at :320, its
    /// return ignored). Null — no rewrite — is the default and stock
    /// Half-Life: nobody slides, the command walks as sent — exactly stock's
    /// own answer when nobody is taunting or the taunt does not allow moving
    /// (:710-715, which also zeroes the player's current taunt speed). The
    /// per-player current speed of the ramp (:685, :689) is this hook's own,
    /// like the stun's fade trio above: every replayed TauntMove mutates it
    /// alike. <c>frameTime</c> is the scaled tick the loop runs this command
    /// at (the lag-compensated <c>gpGlobals->frametime</c> stock reads).
    /// </summary>
    MovementTaunt? MovementTaunt(int playerIndex, float frameTime, float forwardMove) => null;

    /// <summary>
    /// How high a standing player's eye sits, before the model scale: the
    /// height the loop was about to use (<see cref="Constants"/>' view, or the
    /// stock table's), or the mod's own for this player. Asked wherever the
    /// standing view offset is set: the spawn and every unduck. Stock TF2 asks
    /// the class (<c>CTFGameMovement::GetPlayerViewOffset</c> returns
    /// <c>GetClassEyeHeight()</c>, src/game/shared/tf/tf_gamemovement.cpp).
    /// Unchanged by default.
    /// </summary>
    float EyeHeight(int playerIndex, float standing) => standing;

    /// <summary>
    /// The ground-categorizing overrides, asked once per
    /// <c>CategorizePosition</c>: null — the default and stock Half-Life —
    /// runs the base body untouched. See <c>CategorizePositionRules</c> for
    /// the one-field-per-divergence shape and stock TF's
    /// <c>CTFGameMovement::CategorizePosition</c>
    /// (tf_gamemovement.cpp:2334-2459) for the values.
    /// </summary>
    CategorizePositionRules? CategorizePosition(int playerIndex) => null;

    /// <summary>
    /// The footing answer, asked once a ground trace exists with the command's
    /// velocity and that trace's plane normal: lost footing's takeoff, the two
    /// friction scales, and the condition's own clear. Null — the default and
    /// stock Half-Life — no condition arm, the friction untouched.
    /// </summary>
    FootingAnswer? Footing(int playerIndex, in Vector velocity, in Vector planeNormal) => null;

    /// <summary>
    /// The rise arm's enemy-blast read: with the player's blast latch up, the
    /// rise over the categorize threshold names the enemy-blasted jump
    /// (tf_gamemovement.cpp:2360-2364, the GAME_DLL arm of TF's
    /// <c>CategorizePosition</c>; stock's client build compiles the arm away,
    /// so the loop asks it only when the side is the server's). Nothing by
    /// default.
    /// </summary>
    void EnemyBlasted(int playerIndex) { }

    /// <summary>
    /// The end of one command, server side: the mod drops what it kept for
    /// the command's own rise (tf_gamemovement.cpp:334-336, TF's latch clear
    /// after <c>FinishMove</c>). Nothing by default.
    /// </summary>
    void EndCommand(int playerIndex) { }
}

/// <summary>
/// Decides which <see cref="IMovementRules"/> a side's movement obeys
/// (phase 0.3): zero hooks → <see cref="MovementRules.Stock"/>, one → that
/// hook, two or more → a startup error naming them all.
///
/// <para>
/// <b>Why it resolves through the container rather than
/// <see cref="IHooks{T}"/>.</b> A hook chain's collection rule is ORDER
/// uniqueness — two hooks that never declared an order are a sequencing bug
/// whose fix is to pick one. This hook's rule is OWNERSHIP: a second one is
/// not a sequencing decision waiting to be made, it is two mods claiming the
/// same single-owner decision, and no order makes that coherent. Going through
/// the chain would report the common case (two mods that left the default
/// order) as an order collision, advice that names the wrong fix — give one a
/// different order — and only then refuse it. So the resolver walks the same
/// registrations <see cref="ServiceContainer.ResolveAll{T}"/> gives a chain and
/// applies only the ownership rule, which is what makes the two-hook refusal
/// deterministic and its message the true one.
/// </para>
///
/// <para>
/// Resolved ONCE, at startup, and frozen: the answer is published to both
/// halves of the game (the bindings' <see cref="MovementRules"/> and the
/// module's <c>GetJumpScale</c>/<c>GetFrictionScale</c>/<c>GetMaxSpeedScale</c>
/// ABI slots) and every later reader must see the same one. Resolving it late
/// — per command — would let the two sides disagree about WHO is answering,
/// which is the same divergence as the two sides disagreeing about the answer.
/// The module asks after its mods have registered, like every other driver
/// that collects participants once.
/// </para>
/// </summary>
public sealed class MovementRulesResolver
{
    private readonly IMovementRules? _rules;
    private readonly MovementRules _pack;

    /// <summary>
    /// Counts this side's <see cref="IMovementRules"/> registrations. Built by
    /// the container lazily, after the module has registered its core
    /// services and the mods' scan has run, so the count is taken over the
    /// complete assembly set — and the same service, asked twice, resolves
    /// once.
    /// </summary>
    public MovementRulesResolver(IServiceLocator services)
    {
        if (services is not ServiceContainer container)
            throw new ServiceResolutionException(
                "the movement rules resolver needs the side's ServiceContainer");

        var all = container.ResolveAll<IMovementRules>();

        if (all.Count > 1)
        {
            var names = new StringBuilder();
            foreach (var hook in all)
            {
                if (names.Length != 0)
                    names.Append(" and ");
                names.Append(hook.GetType().FullName)
                     .Append(" (").Append(hook.GetType().Assembly.GetName().Name).Append(')');
            }

            throw new ServiceResolutionException(
                "movement rules are registered by " + all.Count + " mods: " + names +
                ". Movement has ONE owner, because two mods cannot both decide how high" +
                " a jump is or how fast a player runs: the scales would both answer and" +
                " the constants both overwrite, and whichever registered last would" +
                " silently win over the other. Only one assembly may register" +
                " IMovementRules.");
        }

        _rules = all.Count == 1 ? all[0] : null;

        // Built ONCE, here rather than on every ask: converting the hook's
        // methods to the loop's delegates allocates per conversion, and the
        // loop asks per command on both sides. Lambdas rather than method
        // groups, exactly as both bindings' packs do: the hook's methods take
        // the request by `in` and the loop's Func takes it by value.
        _pack = _rules is null
            ? MovementRules.Stock
            : new MovementRules(
                r => _rules.JumpScale(in r),
                (i, f, sp, d, m) => _rules.FrictionScale(i, f, sp, d, m),
                (i, ms, d, m) => _rules.MaxSpeedScale(i, ms, d, m),
                r => _rules.AirJump(in r),
                i => _rules.Grounded(i),
                (i, z) => _rules.EyeHeight(i, z),
                (i, ms) => _rules.BunnyJumpCap(i, ms),
                (i, ms) => _rules.BackpedalLimit(i, ms),
                (i, t, b, f, s, u) => _rules.MovementStun(i, t, b, f, s, u),
                (i, t, f) => _rules.MovementTaunt(i, t, f),
                i => _rules.CategorizePosition(i),
                (i, v, n) => _rules.Footing(i, v, n),
                () => _rules.NoHl2Arms,
                i => _rules.EnemyBlasted(i),
                i => _rules.EndCommand(i));
    }

    /// <summary>Whether any mod owns this side's movement.</summary>
    public bool IsStock => _rules is null;

    /// <summary>
    /// The three decisions as the loop wants them: the hook's, or the identity
    /// when no mod owns movement.
    /// </summary>
    public MovementRules Rules => _pack;

    /// <summary>
    /// The constants to register with the engine: the mod's when it names
    /// them, the core's stock table otherwise.
    /// </summary>
    public MovementParams Constants => _rules?.Constants ?? Movement.Current;
}
