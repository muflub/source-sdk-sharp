using System;
using System.Collections.Generic;

namespace SourceSharp.Abi;

/// <summary>
/// Two participants declared the same stage value, so the order between them
/// would have been decided by nothing.
///
/// <para>
/// Its own type rather than <c>InvalidOperationException</c> because it is
/// thrown from a driver constructor during startup and the shell has to be able
/// to tell it apart from a participant's own fault: this one is a wiring
/// mistake that no retry fixes, and the correct response is to stop.
/// </para>
/// </summary>
public sealed class StageCollisionException : Exception
{
    public StageCollisionException(string message) : base(message) { }
}

/// <summary>
/// The invariant that makes the sequence deterministic: <b>no two participants
/// share a stage value.</b>
///
/// <para>
/// <b>Why ties are refused rather than resolved.</b> Both drivers used to sort
/// by the declared order and let equal values keep container registration
/// order. <c>OrderBy</c> is a stable sort, so that rule was honestly
/// implemented — but the thing it stabilises against is registration order,
/// and registration order is exactly what nobody decided. It is the order the
/// lines happen to sit in inside <c>ServerModule.Initialize</c>; moving one
/// registration line, or registering a service under a second contract, silently
/// re-orders two participants that a reader would have to run the game to
/// distinguish. That is the same anonymous-ordering failure as a
/// <c>CAutoGameSystem</c> appending itself at DLL load, reached from the managed
/// side (plan_gamedll.md Part 3a).
/// </para>
///
/// <para>
/// <b>So a tie is a startup failure, and it names both sides.</b> A collision
/// is a two-line fix in one file — give one of them its own
/// <see cref="LifecycleStage"/>/<see cref="FrameStage"/> value, which are spaced
/// by 100 precisely so there is always room between any two. Detecting it at
/// startup rather than at the first level change means the fix happens before
/// any behaviour depended on whichever way it happened to fall.
/// </para>
///
/// <para>
/// <b><see cref="LifecycleStage.Unspecified"/> is not exempt.</b> It is the
/// interface default, so it is where a participant that simply forgot to
/// declare a stage lands — which makes it the MOST likely collision, not a
/// harmless one. "My order does not matter" is a claim about a sequence that is
/// observable, and two participants making it at once is still two participants
/// whose relative order nobody chose. At most one participant may leave it
/// unspecified.
/// </para>
/// </summary>
public static class StageUniqueness
{
    /// <summary>
    /// Throws <see cref="StageCollisionException"/> if two participants declare
    /// the same value.
    /// </summary>
    /// <param name="participants">The resolved participants, in any order.</param>
    /// <param name="order">Reads the declared stage value off one.</param>
    /// <param name="property">The declaring property, for the message — e.g. <c>LifecycleOrder</c>.</param>
    /// <param name="stageEnum">The enum a fix should pick a value from — e.g. <c>LifecycleStage</c>.</param>
    public static void Require<T>(
        IReadOnlyList<T> participants,
        Func<T, int> order,
        string property,
        string stageEnum)
        where T : class
    {
        // Seen-first-at, so the message can name the OTHER participant. A bare
        // "duplicate value 400" would leave the reader grepping for whoever
        // else declared it, and the whole point of the check is that the answer
        // is not obvious from any one file.
        var seen = new Dictionary<int, T>();

        foreach (var p in participants)
        {
            int value = order(p);

            if (seen.TryGetValue(value, out var first))
            {
                throw new StageCollisionException(
                    $"{first.GetType().Name} and {p.GetType().Name} both declare "
                    + $"{property} {value}. A stage value is unique per participant on "
                    + "purpose: with two at the same number the order between them is "
                    + "whatever order the container happened to resolve them in, which "
                    + "is not a decision anyone made and changes when a registration "
                    + $"line moves. Give one of them its own {stageEnum} value -- they "
                    + "are spaced by 100 so there is room between any two.");
            }

            seen.Add(value, p);
        }
    }
}
