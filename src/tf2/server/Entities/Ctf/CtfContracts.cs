//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Modes;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Server.Entities.Ctf;

/// <summary>
/// The orders CTF takes in the core's unique-order sequences: TF2's thousand
/// below <see cref="ModStages.First"/>, the CTF lane's band +500..+599
/// (<c>TF2LifecycleOrderTests</c> holds the range and the uniqueness).
/// </summary>
public static class CtfOrders
{
    /// <summary>CTF's band.</summary>
    public const int Band = ModStages.First - 1_000 + 500;

    /// <summary><see cref="FlagCarrierDrops"/>: the death hook and the disconnect.</summary>
    public const int CarrierDrops = Band + 1;

    /// <summary><see cref="TFDropItemCommand"/>: the <c>dropitem</c> word.</summary>
    public const int DropItemCommand = Band + 2;

    /// <summary><see cref="FlagTeamChangeDrops"/>: the drop on a forced team change.</summary>
    public const int TeamChangeDrop = Band + 3;

    /// <summary><see cref="FlagRespawnDrops"/>: the drop before a forced respawn.</summary>
    public const int CarrierRespawnDrop = Band + 4;

    /// <summary><see cref="CtfRules"/>' per-level reset (stock's rules ctor, tf_gamerules.cpp:3331).</summary>
    public const int RulesState = Band + 5;

    /// <summary><see cref="FlagCarrierEffects"/>' per-level reset (stock's rules ctor's <c>SetCTFCaptureBonusTime(-1)</c>, tf_gamerules.cpp:3338).</summary>
    public const int CaptureBonusTime = Band + 6;
}

/// <summary>
/// What a flag and a capture zone ask about a player (stock reads these off
/// <c>CTFPlayer</c>): one seam, so a fact can stand players of either team,
/// in any condition, on a flag. The game's is <see cref="EngineCtfPlayers"/>.
/// </summary>
public interface ICtfPlayers
{
    /// <summary><c>IsPlayer()</c>: false for anything else, and for a handle that is gone.</summary>
    bool IsPlayer(EntityHandle entity);

    bool IsAlive(EntityHandle player);

    TFTeam Team(EntityHandle player);

    /// <summary>The player's entity index (<c>entindex()</c>).</summary>
    int Index(EntityHandle player);

    /// <summary><c>GetUserID()</c>.</summary>
    int UserId(EntityHandle player);

    /// <summary><c>WorldSpaceCenter()</c>: where a dropped flag's trace starts.</summary>
    Vector WorldSpaceCenter(EntityHandle player);

    /// <summary><c>ToTFPlayer</c> succeeds: the player has a <c>tf_player_data</c> carrier.</summary>
    bool IsTFPlayer(EntityHandle player);

    TFClass Class(EntityHandle player);

    /// <summary>The player's conditions (<c>m_Shared.InCond</c>); none without a carrier.</summary>
    TFCondMask Conditions(EntityHandle player);

    /// <summary>
    /// <c>CTFPlayer::IsAllowedToPickUpFlag</c>
    /// (<c>shared/tf/tf_player_shared.cpp:11260-11270</c>, gated by
    /// <c>CCaptureFlag::FlagTouch</c> at entity_capture_flag.cpp:1092): the
    /// <c>cannot_pick_up_intelligence</c> attribute hook over the player's
    /// own item manager answers no refusal.
    /// </summary>
    bool AllowedToPickUpFlag(EntityHandle player);

    /// <summary>
    /// <c>CTFPlayerShared::GetPercentInvisible</c>
    /// (<c>shared/tf/tf_player_shared.cpp:8047-8051</c>) through
    /// <see cref="TFInvisibility"/>: how invisible the player is now, 0..1.
    /// </summary>
    float PercentInvisible(EntityHandle player);

    /// <summary><c>LookupAttachment( "flag" )</c> on the player's model: 0 for none.</summary>
    int FlagAttachment(EntityHandle player);

    /// <summary><c>GetAbsVelocity()</c>: what the flag's trail gate reads for running.</summary>
    Vector Velocity(EntityHandle player);

    /// <summary><c>MaxSpeed()</c>: the player's top speed now, through the movement rules.</summary>
    float MaxSpeed(EntityHandle player);
}

/// <summary>The engine's and the carriers' answers for <see cref="ICtfPlayers"/>.</summary>
[SideService(Side.Server, As = [typeof(ICtfPlayers)])]
public sealed class EngineCtfPlayers(IEngineEntities entities, IEnginePlayers players, IEngineEntityRender render,
                                     ITFPlayerData carriers, AttributeHooks attributes, IGameClock clock,
                                     IMovementRules? movement = null, ITFWeaponOwners? owners = null) : ICtfPlayers
{
    public bool IsPlayer(EntityHandle entity) => entities.IsValid(entity) && entities.IsPlayer(entity);

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public TFTeam Team(EntityHandle player) => (TFTeam)players.GetTeam(player);

    public int Index(EntityHandle player) => entities.GetIndex(player);

    public int UserId(EntityHandle player) => players.GetUserId(player);

    public Vector WorldSpaceCenter(EntityHandle player) => entities.GetWorldSpaceCenter(player);

    public bool IsTFPlayer(EntityHandle player) => carriers.Networked(Index(player)) is not null;

    public TFClass Class(EntityHandle player) => carriers.Networked(Index(player))?.Class() ?? TFClass.Undefined;

    public TFCondMask Conditions(EntityHandle player) => carriers.Networked(Index(player))?.Conditions() ?? default;

    public bool AllowedToPickUpFlag(EntityHandle player)
        => attributes.Int(0, "cannot_pick_up_intelligence", owners?.ManagerOf(Index(player))) == 0;

    public float PercentInvisible(EntityHandle player)
        => carriers.Networked(Index(player)) is { } state ? TFInvisibility.Percent(state, clock.Now) : 0f;

    public int FlagAttachment(EntityHandle player) => render.LookupAttachment(player, "flag");

    public Vector Velocity(EntityHandle player) => entities.GetVelocity(player);

    /// <summary>
    /// <c>CTFPlayer::MaxSpeed</c> — the movement rules answer the absolute
    /// speed for a classed player (<see cref="TFMaxSpeed"/>), so the engine's
    /// own answer is asked with nothing to scale.
    /// </summary>
    public float MaxSpeed(EntityHandle player)
        => movement?.MaxSpeedScale(Index(player), 0f, ducking: false, groundMaterial: 0) ?? 0f;
}

/// <summary>The convars the flag reads, as one seam a fact sets.</summary>
public interface ICtfSettings
{
    /// <summary><c>tf_flag_return_on_touch</c>.</summary>
    bool ReturnOnTouch { get; }

    /// <summary><c>tf_flag_return_time_credit_factor</c>.</summary>
    float ReturnTimeCreditFactor { get; }

    /// <summary><c>tf_flag_caps_per_round</c>.</summary>
    int CapsPerRound { get; }
}

/// <summary>The game's <see cref="ICtfSettings"/>: the convars as they stand.</summary>
[SideService(Side.Server, As = [typeof(ICtfSettings)])]
public sealed class CtfConVarSettings(TFFlagReturnOnTouchConVar returnOnTouch,
                                      TFFlagReturnTimeCreditFactorConVar creditFactor,
                                      TFFlagCapsPerRoundConVar capsPerRound) : ICtfSettings
{
    public bool ReturnOnTouch => returnOnTouch.Value;

    public float ReturnTimeCreditFactor => creditFactor.Value;

    public int CapsPerRound => capsPerRound.Value;
}

/// <summary>
/// The CTF game events (resource/modevents.res): <c>teamplay_flag_event</c>,
/// <c>ctf_flag_captured</c> and <c>flagstatus_update</c>, as one seam the facts
/// record. The game's is <see cref="CtfGameEvents"/>.
/// </summary>
public interface ICtfEvents
{
    /// <summary>
    /// <c>teamplay_flag_event</c>. <paramref name="player"/> and
    /// <paramref name="carrier"/> are entity indexes (-1 unset);
    /// <paramref name="home"/> is set only by a pick-up.
    /// </summary>
    void FlagEvent(TFFlagEvent type, int player, TFTeam team, int priority, bool? home = null, int carrier = -1);

    /// <summary><c>ctf_flag_captured</c>.</summary>
    void FlagCaptured(TFTeam cappingTeam, int cappingTeamScore, int capperUserId);

    /// <summary><c>flagstatus_update</c>: the new owner's userid (-1 for none) and the flag's index.</summary>
    void FlagStatusUpdate(int userId, int entIndex);
}

/// <summary>The CTF events through the engine's event bus (create, set, fire).</summary>
[SideService(Side.Server, As = [typeof(ICtfEvents)])]
public sealed class CtfGameEvents(IEngineGameEvents events) : ICtfEvents
{
    public void FlagEvent(TFFlagEvent type, int player, TFTeam team, int priority, bool? home = null, int carrier = -1)
    {
        IntPtr e = events.EventCreate("teamplay_flag_event");
        if (e == IntPtr.Zero)
            return;
        if (player != -1)
            events.EventSetInt(e, "player", player);
        if (carrier != -1)
            events.EventSetInt(e, "carrier", carrier);
        events.EventSetInt(e, "eventtype", (int)type);
        events.EventSetInt(e, "priority", priority);
        if (home is { } h)
            events.EventSetInt(e, "home", h ? 1 : 0);
        events.EventSetInt(e, "team", (int)team);
        events.EventFire(e);
    }

    public void FlagCaptured(TFTeam cappingTeam, int cappingTeamScore, int capperUserId)
    {
        IntPtr e = events.EventCreate("ctf_flag_captured");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "capping_team", (int)cappingTeam);
        events.EventSetInt(e, "capping_team_score", cappingTeamScore);
        events.EventSetInt(e, "capper", capperUserId);
        events.EventSetInt(e, "priority", 9); // HLTV priority
        events.EventFire(e);
    }

    public void FlagStatusUpdate(int userId, int entIndex)
    {
        IntPtr e = events.EventCreate("flagstatus_update");
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "userid", userId);
        events.EventSetInt(e, "entindex", entIndex);
        events.EventFire(e);
    }
}

/// <summary>
/// What a pick-up and a capture do to players beyond the flag —
/// <c>CTFPlayerShared::RemoveDisguise</c> for a spy who takes the flag, and
/// <c>CTFGameRules::HandleCTFCaptureBonus</c>'s
/// <c>TF_COND_CRITBOOSTED_CTF_CAPTURE</c> for the capping team
/// (<c>tf_ctf_bonus_time</c>). The game's is
/// <see cref="FlagCarrierEffects"/>; the flag keeps the ask optional, so a
/// build without the spy slice or the condition drops the arm it cannot answer.
/// </summary>
public interface IFlagCarrierEffects
{
    void RemoveDisguise(EntityHandle player);

    /// <summary>
    /// Stock's <c>CTFGameRules::SetCTFCaptureBonusTime</c>
    /// (tf_gamerules.h:450): the <c>tf_gamerules</c> entity's
    /// <c>SetCTFCaptureBonusTime</c> input (tf_gamerules.cpp:1688-1694)
    /// overriding <c>tf_ctf_bonus_time</c> for this map; negative restores
    /// the convar (<c>GetCTFCaptureBonusTime</c>, tf_gamerules.h:451-460).
    /// </summary>
    void SetCaptureBonusTime(float seconds);

    void CaptureBonus(TFTeam team);
}

/// <summary>
/// The CTFGameRules members the flag, the zones and the CTF mode call
/// (tf_gamerules.cpp): whether flags may be capped now, whether a team may
/// capture, the caps-per-round win, and the flags themselves (stock's
/// <c>ICaptureFlagAutoList</c>). The game's is <see cref="CtfRules"/>.
/// </summary>
public interface ICtfRules
{
    /// <summary>Stock's <c>m_bOvertimeAllowedForCTF</c>
    /// (tf_gamerules.h:220): the <c>tf_gamerules</c> entity's
    /// <c>ctf_overtime</c> key. While it stands, a flag away from home holds
    /// the round timers (the <c>TimerMayExpire</c> arm,
    /// tf_gamerules.cpp:14022).</summary>
    bool OvertimeAllowedForCtf { get; }

    /// <summary>Stock's <c>SetOvertimeAllowedForCTF</c>
    /// (tf_gamerules.h:1006), the proxy's Activate push
    /// (tf_gamerules.cpp:1904).</summary>
    void SetOvertimeAllowedForCtf(bool allowed);

    /// <summary><c>FlagsMayBeCapped</c> (L3659): outside the win and the pre-round.</summary>
    bool FlagsMayBeCapped { get; }

    /// <summary><c>IsInWaitingForPlayers</c>.</summary>
    bool InWaitingForPlayers { get; }

    /// <summary><c>InStalemate</c>.</summary>
    bool InStalemate { get; }

    /// <summary><c>CanFlagBeCaptured</c> (L15740): under <c>tf_flag_return_on_touch</c>, only while the capper's own CTF flag is home.</summary>
    bool CanFlagBeCaptured(EntityHandle player);

    /// <summary>Every flag on the map (<c>ICaptureFlagAutoList</c>).</summary>
    IEnumerable<ItemTeamFlag> Flags { get; }

    /// <summary><c>CTFPlayer::GetItem</c> as a flag: the flag this player carries, or null.</summary>
    ItemTeamFlag? CarriedBy(EntityHandle player);

    /// <summary><c>CheckCapsPerRound</c>/<c>SetCtfWinningTeam</c> (L9298-9375): true when a team's captures won the round.</summary>
    bool CheckCapsPerRound();

    /// <summary><c>PointInRespawnFlagZone</c> (func_respawnflag.cpp).</summary>
    bool PointInRespawnFlagZone(Vector point);

    /// <summary>A flag moved: the active TF2 mode's hooks hear it (<see cref="TFModeBase.ReportObjective"/>).</summary>
    void Report(string kind, TFTeam team, int index);

    /// <summary>The team's flag captures (<c>CTFTeam::GetFlagCaptures</c>).</summary>
    int FlagCaptures(TFTeam team);

    /// <summary>A capture's reward: <c>IncrementFlagCaptures</c>, or <c>AddTeamScore</c> when <paramref name="asCapture"/> is false.</summary>
    void RewardTeam(TFTeam team, bool asCapture, int score);

    /// <summary><c>CTFTeam::SetFlagCaptures</c>.</summary>
    void SetFlagCaptures(TFTeam team, int captures);
}

/// <summary>
/// The game's <see cref="ICtfRules"/>, over the round machine, the team table
/// and the side's entity directory.
/// </summary>
[SideService(Side.Server, As = [typeof(ICtfRules)])]
public sealed class CtfRules(ITeamRoundState round, ITeamRoundControl control, ITFTeamScores scores,
                             ICtfSettings settings, ICtfPlayers players, IEntityDirectory directory,
                             IActiveTeamRoundMode mode)
    : ICtfRules, IGameLifecycle
{
    private bool _overtimeAllowedForCtf = true;

    int IGameLifecycle.LifecycleOrder => CtfOrders.RulesState;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        // The rules ctor's defaults, which stock re-runs at every level's
        // CWorld::Spawn (world.cpp:608-616) — the flags a map's own
        // tf_gamerules then re-set at its Activate.
        _overtimeAllowedForCtf = true;
    }

    public bool OvertimeAllowedForCtf => _overtimeAllowedForCtf;

    public void SetOvertimeAllowedForCtf(bool allowed) => _overtimeAllowedForCtf = allowed;

    public bool FlagsMayBeCapped => round.State is not (RoundState.TeamWin or RoundState.Preround);

    public bool InWaitingForPlayers => round.InWaitingForPlayers;

    public bool InStalemate => round.State == RoundState.Stalemate;

    public IEnumerable<ItemTeamFlag> Flags => directory.OfClass(ItemTeamFlag.MapClass).OfType<ItemTeamFlag>();

    public ItemTeamFlag? CarriedBy(EntityHandle player)
        => player == EntityHandle.None ? null : Flags.FirstOrDefault(f => f.IsStolen && f.Carrier == player);

    public bool CanFlagBeCaptured(EntityHandle player)
    {
        // IsPowerupMode's half is not ported (no powerup mode).
        if (player != EntityHandle.None && settings.ReturnOnTouch)
        {
            TFTeam team = players.Team(player);
            foreach (var flag in Flags)
            {
                if (flag.Type == TFFlagType.Ctf && !flag.IsDisabled && team == flag.Team && !flag.IsHome)
                    return false;
            }
        }

        return true;
    }

    public bool CheckCapsPerRound()
    {
        // IsPasstimeMode's SetPasstimeWinningTeam is not ported (no passtime).
        if (settings.CapsPerRound <= 0)
            return false;

        int maxCaps = -1;
        TFTeam maxTeam = TFTeam.Unassigned;

        // we might have more than one team over the caps limit (if the server op lowered the limit)
        // so loop through to see who has the most among teams over the limit
        for (var team = TFTeam.Red; team < TFTeam.Count; team++)
        {
            int caps = scores.FlagCaptures(team);
            if (caps >= settings.CapsPerRound && caps > maxCaps)
            {
                maxCaps = caps;
                maxTeam = team;
            }
        }

        if (maxCaps == -1 || maxTeam == TFTeam.Unassigned)
            return false;

        control.SetWinningTeam(maxTeam, WinReason.FlagCaptureLimit);
        return true;
    }

    public bool PointInRespawnFlagZone(Vector point)
        => directory.OfClass(FuncRespawnFlag.MapClass).OfType<FuncRespawnFlag>().Any(z => z.Contains(point));

    public void Report(string kind, TFTeam team, int index)
        => (mode.Current as TFModeBase)?.ReportObjective(new TFObjectiveEvent(kind, team, index));

    public int FlagCaptures(TFTeam team) => scores.FlagCaptures(team);

    public void RewardTeam(TFTeam team, bool asCapture, int score)
    {
        if (asCapture)
            scores.AddFlagCapture(team);
        else
            scores.AddScore(team, score);
    }

    public void SetFlagCaptures(TFTeam team, int captures) => scores.SetFlagCaptures(team, captures);
}
