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
                (i, ms, d, m) => _rules.MaxSpeedScale(i, ms, d, m));
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
