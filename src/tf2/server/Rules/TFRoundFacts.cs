using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// What every client's HUD shows about the round, as one read: the round
/// machine's state, the HUD timer and the respawn waves. The AI binding reads
/// this instead of reaching into the round machinery. Every member is a plain
/// read of a service the round itself answers through; nothing is cached.
/// </summary>
public interface ITFRoundFacts
{
    /// <summary>The round machine's state.</summary>
    RoundState State { get; }

    /// <summary>Stock's <c>InSetup</c>: the running round's setup period.</summary>
    bool InSetup { get; }

    /// <summary>Seconds left of the setup period; 0 when there is none.</summary>
    float SetupSecondsLeft { get; }

    /// <summary>The HUD timer's seconds left; -1 when no timer is elected.</summary>
    float RoundSecondsLeft { get; }

    /// <summary>Seconds until the team's next respawn wave; 0 when none is scheduled.</summary>
    float NextRespawnWaveIn(TFTeam team);

    /// <summary>Stock's <c>IsInWaitingForPlayers</c>: the pre-game wait.</summary>
    bool IsWaitingForPlayers { get; }

    /// <summary>The round's winner while the state is TeamWin; Unassigned otherwise.</summary>
    TFTeam WinningTeam { get; }
}

/// <summary>
/// The server's <see cref="ITFRoundFacts"/>, over the round machine
/// (<see cref="ITeamRoundState"/>, <see cref="ITeamRespawnWaves"/>, both the
/// flow's) and the elected HUD timer (<see cref="IRoundTimers"/> — the service
/// under the <c>TeamRoundTimerNetState</c> the HUD timer reads).
/// </summary>
[SideService(Side.Server, As = [typeof(ITFRoundFacts)])]
public sealed class TFRoundFacts(ITeamRoundState round, ITeamRespawnWaves waves, IRoundTimers timers, IGameClock clock)
    : ITFRoundFacts
{
    public RoundState State => round.State;

    public bool InSetup => round.InSetup;

    public float SetupSecondsLeft => round.InSetup && timers.Active is { } setup ? setup.TimeRemaining : 0f;

    public float RoundSecondsLeft => timers.Active?.TimeRemaining ?? -1f;

    public float NextRespawnWaveIn(TFTeam team)
    {
        float next = waves.NextRespawnWave(team);

        // Zero when there is none scheduled (the waves' own answer), else the
        // wait from now.
        return next <= 0f ? 0f : MathF.Max(0f, next - clock.Now);
    }

    public bool IsWaitingForPlayers => round.InWaitingForPlayers;

    public TFTeam WinningTeam => round.State == RoundState.TeamWin ? round.WinningTeam : TFTeam.Unassigned;
}
