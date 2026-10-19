namespace SourceSharp.Abi;

/// <summary>
/// The constants one replayed command runs under — everything the movement
/// loop reads that is not the player.
/// </summary>
/// <param name="Constants">The convars, the tick interval and the hulls.</param>
/// <param name="JumpHeight">
/// <c>sv_</c>-driven, and separate from <paramref name="Constants"/> because
/// <see cref="GameMovement"/> keeps it as its own property rather than in the
/// struct — mirrored here rather than tidied so that the client's loop is
/// configured by exactly the fields the server's crossing configures.
/// </param>
/// <param name="EngineTime">
/// <c>engine-&gt;Time()</c>, which is WALL time and not the tick clock. Read by
/// exactly one thing (CheckStuck's "too soon?" throttle) and carried per
/// command because it moves per frame.
/// </param>
/// <param name="ModelScale">
/// The player's model scale, which shareddefs.h's hull macros multiply the
/// standing and ducked hulls by. Not a constant of the world but a property of
/// the entity, and it arrives here because the same call is the only place
/// native has the shell in hand — a scaled player traced with an unscaled hull
/// walks through the walls its own model is touching, and every trace still
/// comes back with a plausible fraction.
/// </param>
/// <param name="CurrentGravity">
/// <c>GetCurrentGravity()</c> — sv_gravity times whatever gamerules scales it
/// by. A fact about the world and not about the player, which is why it is
/// filled beside the convars rather than carried in the player's state.
/// </param>
public readonly record struct MoveFrameConstants(MoveConstants Constants,
                                                 float JumpHeight,
                                                 float EngineTime,
                                                 float ModelScale,
                                                 float CurrentGravity);

/// <summary>
/// WHAT THE MANAGED CLIENT REPLAY ASKS NATIVE FOR (plan_entity.md step 6f), and
/// the whole of it.
///
/// <para>
/// The client's prediction is C# — the ring, the basis, the compare, the meter
/// and the movement loop. Three things it cannot answer for itself and they are
/// the three methods here: what the player actually sent (the engine's input
/// ring), what the world is like while one command runs (the movement bracket,
/// which points step 3e's four trace slots at this player), and where native
/// should draw the answer.
/// </para>
///
/// <para>
/// AN INTERFACE, so the replay is testable without a game. The unsafe half is
/// <see cref="ClientReplayEngine"/> and lives in this assembly for the reason
/// <see cref="IInterpolationBlocks"/> gives: the game assemblies are compiled
/// without <c>/unsafe</c> and the prediction code has no business being the
/// exception.
/// </para>
/// </summary>
public interface IClientReplayEngine
{
    /// <summary>
    /// One command out of the engine's input ring, by ABSOLUTE command number.
    /// False when the ring does not hold it, which ends the frame's replay
    /// rather than replaying a command that is not there.
    /// </summary>
    bool TryGetUserCmd(int commandNumber, out UserCmd cmd);

    /// <summary>
    /// Opens one replayed command: the constants come back, and step 3e's four
    /// trace slots are pointed at this player for the duration.
    ///
    /// <para>
    /// OUTSIDE THE BRACKET THEY REFUSE. A trace asked for with no bracket open
    /// answers "blocked at the start", never "the way is clear" — which is why
    /// running the loop when this returns false would be worse than not running
    /// it: a clear trace is what makes a client walk through the floor, which
    /// step 3e measured on exactly this boundary.
    /// </para>
    /// </summary>
    /// <param name="commandNumber">The command about to run. The bracket seeds
    /// the shared random stream from it, which is what makes every
    /// <c>SharedRandom*</c> answer the same number here as on the server.</param>
    bool BeginCommand(EntityHandle player, int commandNumber, out MoveFrameConstants constants);

    /// <summary>
    /// Closes it — ALWAYS, refusal or not, so the trace slots go back to
    /// refusing.
    /// </summary>
    /// <param name="ran">Whether the managed loop ran this command. The only
    /// thing native can count that tells "the managed body ran" from "the
    /// managed body was asked for" (<c>cs_cl_move_managed_report</c>).</param>
    /// <returns>
    /// The entity INDEX of the last standable ground a serviced trace found, or
    /// -1 for none — the one field of the player's state that cannot round-trip
    /// as a value, because the loop carries "is there ground" as a bool and an
    /// entity is not flat.
    /// </returns>
    int EndCommand(bool ran);

    /// <summary>
    /// The local player's view state, into the shell native draws from. Called
    /// every frame there is a local player, driving or not — see
    /// <c>C_StandaloneEntity::PredictionDrive</c> for why the origin is
    /// conditional and the rest of the record is not.
    /// </summary>
    void PublishView(EntityHandle player, in Vector origin, in Vector viewOffset,
                     in QAngle punch, in QAngle punchVel, bool driving, int health);
}
