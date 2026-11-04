//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Modes;

/// <summary>
/// Stock <c>ETFGameType</c> (tf_shareddefs.h), the prefix dropped: the game
/// type a mode plays. Of these the milestone modes are <see cref="Ctf"/> and
/// <see cref="Escort"/> (Payload).
/// </summary>
public enum TFGameType
{
    Undefined = 0,
    Ctf,
    Cp,
    Escort,
    Arena,
    Mvm,
    Rd,
    Passtime,
    Pd,
    Count,
}

/// <summary>Stock <c>CTFGameRules::Activate</c>'s <c>tf_gamemode_*</c> writes: which game type the map plays.</summary>
public interface ITFGameTypeFlags
{
    void SetGameType(TFGameType type);
}

/// <summary>The flags as the two landed convars.</summary>
[SideService(Side.Server, As = [typeof(ITFGameTypeFlags)])]
public sealed class ConVarGameTypeFlags(TFGameModeCpConVar cp, TFGameModeCtfConVar ctf, TFGameModePayloadConVar payload)
    : ITFGameTypeFlags
{
    public void SetGameType(TFGameType type)
    {
        cp.SetValue(type == TFGameType.Cp);
        ctf.SetValue(type == TFGameType.Ctf);
        payload.SetValue(type == TFGameType.Escort);
    }
}

/// <summary>
/// What every TF2 mode is built from, in one service so a mode's constructor
/// (TF2's, or a further mod's mode over TF2's) takes one parameter: the one
/// round machine every TF2 mode shares, its respawn policy and team scoring,
/// the mode hooks and the game-type flags.
/// </summary>
[SideService(Side.Server)]
public sealed class TFModeServices(TeamRoundFlow rounds, ITeamRespawnWaves waves, ITeamRoundRoster roster,
                                   ITFTeamScores scores, ITFGameTypeFlags flags, ILog log,
                                   IHooks<ITFModeHook>? hooks = null)
{
    public TeamRoundFlow Rounds => rounds;
    public TFRespawnPolicy Respawn { get; } = new(waves, roster);
    public TFTeamScoring Scoring { get; } = new(scores);
    public ITFGameTypeFlags Flags => flags;
    public ILog Log => log;
    public IHooks<ITFModeHook>? Hooks => hooks;

    /// <summary>TF's preserve list for the round's <c>CleanUpMap</c> (stateless, the rules' own).</summary>
    public TFMapCleanupFilter Cleanup { get; } = new();
}

/// <summary>
/// The TF2 concerns every TF2 mode shares (stage2 2.2), composed over the one
/// <see cref="TeamRoundFlow"/>: the team-round machine as the mode's
/// <see cref="IGameMode.Rounds"/>, wave respawns (<see cref="TFRespawnPolicy"/>),
/// the team scores (<see cref="TFTeamScoring"/>), the game-type flags at setup,
/// and the <see cref="ITeamRoundMode"/> answers the machine asks the active
/// mode — <c>CTFGameRules</c>'s overrides, stock's base defaults here, a
/// game type's in its subclass.
///
/// <para>
/// <b>Extended by hooks, not by subclassing (D10).</b> The game types are this
/// class's subclasses; a further mod alters a TF2 mode through the
/// <see cref="ITFModeHook"/> chain, which this class runs at the round's start,
/// on an objective event, at the win check and on the wave length. The spawn
/// points are the registered <see cref="ISpawnSelector"/> chain's (tf2-obj's
/// <c>TFSpawnSelector</c>), so <see cref="Spawns"/> is null unless a game type
/// owns its own.
/// </para>
/// </summary>
public abstract class TFModeBase : IGameMode, ITeamRoundMode, IModeSetup
{
    private readonly TFModeServices _services;

    protected TFModeBase(TFModeServices services)
    {
        _services = services;
    }

    public abstract string Id { get; }

    /// <summary>The game type this mode plays (the <c>tf_gamemode_*</c> flag its setup sets).</summary>
    public abstract TFGameType GameType { get; }

    // ---- IGameMode --------------------------------------------------------------

    public IRoundFlow Rounds => _services.Rounds;

    public IRespawnPolicy? Respawn => _services.Respawn;

    public virtual ISpawnSelector? Spawns => null;

    public IScoring? Scoring => _services.Scoring;

    /// <summary>Stock's team play: a team line reaches the listener's teammates.</summary>
    public bool Teamplay => true;

    public IModeSetup? Setup => this;

    public IMapCleanupFilter? Cleanup => _services.Cleanup;

    /// <summary>Stock <c>CTFGameRules::Activate</c>'s game-type flags.</summary>
    public virtual void SetupRound(float now) => _services.Flags.SetGameType(GameType);

    // ---- ITeamRoundMode -----------------------------------------------------------

    /// <summary>The game type's round start, then every hook's.</summary>
    public void OnRoundStart()
    {
        RoundStarting();
        _services.Hooks?.Each(nameof(ITFModeHook.OnRoundStart), h => h.OnRoundStart(this));
    }

    /// <summary>The game type's part of <c>SetupOnRoundStart</c>.</summary>
    protected virtual void RoundStarting() { }

    public virtual void OnRoundRunning() { }

    public virtual bool TimerMayExpire() => true;

    public virtual bool CanGoToStalemate() => true;

    /// <summary>Stock's <c>CheckCapsPerRound</c>: no captures count outside CTF.</summary>
    public virtual bool CheckCapsPerRound() => false;

    public virtual bool ShouldScorePerRound() => true;

    /// <summary>Stock's <c>IsInKothMode</c> (<c>m_bPlayingKoth</c>): the koth mode's flag.</summary>
    public virtual bool InKothMode => false;

    public virtual bool EvenTeamsJoinAttackers => false;

    public virtual bool PointsMayAlwaysBeBlocked => false;

    /// <summary>Stock's <c>HasMultipleTrains</c> (<c>m_bMultipleTrains</c>): only escort fields more than one train.</summary>
    public virtual bool HasMultipleTrains => false;

    /// <summary>Every hook's say on a win: one veto refuses it; a hook that throws is named and does not veto.</summary>
    public bool AllowWin(TFTeam team, WinReason reason)
    {
        if (_services.Hooks is not { } hooks)
            return true;

        foreach (var hook in hooks.All)
        {
            try
            {
                if (!hook.ModifyWinCheck(this, team, reason))
                {
                    _services.Log.Msg($"[TF2/Modes] {hook.GetType().FullName} vetoed {team}'s win ({reason})\n");
                    return false;
                }
            }
            catch (Exception e)
            {
                _services.Log.Warning($"[TF2/Modes] {hook.GetType().FullName}.ModifyWinCheck threw: {e.Message}\n");
            }
        }

        return true;
    }

    /// <summary>The game type's wave length, then each hook's change to it in order.</summary>
    public float RespawnWaveTime(TFTeam team, float seconds)
    {
        float time = ModifyRespawnWave(team, seconds);

        if (_services.Hooks is not { } hooks)
            return time;

        foreach (var hook in hooks.All)
        {
            try
            {
                time = hook.ModifyRespawnWave(this, team, time);
            }
            catch (Exception e)
            {
                _services.Log.Warning($"[TF2/Modes] {hook.GetType().FullName}.ModifyRespawnWave threw: {e.Message}\n");
            }
        }

        return Math.Max(0f, time);
    }

    /// <summary>The game type's own wave modifier (stock's robot-destruction arm is the only one); unchanged here.</summary>
    protected virtual float ModifyRespawnWave(TFTeam team, float seconds) => seconds;

    /// <summary>An objective moved: the objectives (2.4-2.6) report it, and every hook hears it.</summary>
    public void ReportObjective(TFObjectiveEvent objective)
        => _services.Hooks?.Each(nameof(ITFModeHook.OnObjectiveEvent), h => h.OnObjectiveEvent(this, objective));
}
