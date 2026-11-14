//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// One kind of thing players may vote on (stock TF2's <c>CBaseTFIssue</c>
/// subclasses, tf_voteissues.h): the choices it offers now, each naming the
/// server command a passing vote runs, and why a listed choice cannot be called
/// now. A chain (plan_bots_teams_tf2 D.3): the core registers its issues and a
/// mod adds its own, each at its own <see cref="IOrderedHook.HookOrder"/>,
/// which is also the order of the list players get.
/// </summary>
public interface IVoteIssue
{
    /// <summary>The kind on the wire, and the word a call names it by (<see cref="VoteOptionWords"/>).</summary>
    VoteKind Kind { get; }

    /// <summary>What players may call now; empty when the server allows none of this kind.</summary>
    IEnumerable<VoteOption> Choices();

    /// <summary>Why the listed <paramref name="choice"/> cannot be called now; null when it can.</summary>
    string? Refusal(VoteOption choice) => null;
}

/// <summary>The core's issues' places on the chain: a hundred apart, so a mod's can sit between.</summary>
public static class VoteIssueOrders
{
    public const int ChangeLevel = 100, NextLevel = 200, Mode = 300, Setting = 400, Extend = 500, Restart = 600, Scramble = 700;
}

/// <summary>
/// What players may vote on now: every registered <see cref="IVoteIssue"/>'s
/// choices, in chain order, and the issue's word on a listed one.
/// </summary>
public sealed class VoteIssueList(IHooks<IVoteIssue> issues) : IVoteOptions
{
    public IReadOnlyList<VoteOption> Build() => [.. issues.All.SelectMany(i => i.Choices())];

    public string? Refusal(VoteOption choice)
        => issues.All.FirstOrDefault(i => i.Kind == choice.Kind)?.Refusal(choice);
}

/// <summary>Stock's <c>CChangeLevelIssue</c>: change now to a candidate map (installed, of the cycle).</summary>
public sealed class ChangeLevelVoteIssue(MapCandidates candidates, VoteIssueChangeLevelAllowedConVar allowed)
    : IVoteIssue, IOrderedHook
{
    public int HookOrder => VoteIssueOrders.ChangeLevel;

    public VoteKind Kind => VoteKind.Map;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value ? candidates.Votable().Select(c => new VoteOption(VoteKind.Map, c.Map, c.Label, $"changelevel {c.Map}")) : [];
}

/// <summary>A switch to another game mode (stock has none): the modes registered but the one on.</summary>
public sealed class ModeVoteIssue(GameModes modes, GameModeCatalog catalog, VoteIssueModeAllowedConVar allowed) : IVoteIssue, IOrderedHook
{
    /// <summary>
    /// What a passing mode vote runs: the switch, kept for the levels after
    /// (<c>sv_mode_next</c>) where their maps can play it. A bare <c>sv_mode</c>
    /// at the console lasts the level.
    /// </summary>
    public static string Command(string id) => $"sv_mode_next {id}; sv_mode {id}";

    public int HookOrder => VoteIssueOrders.Mode;

    public VoteKind Kind => VoteKind.Mode;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value
            ? catalog.Modes().Where(m => !string.Equals(m.Id, modes.ActiveId, StringComparison.OrdinalIgnoreCase))
                     .Select(m => new VoteOption(VoteKind.Mode, m.Id, m.Title, Command(m.Id)))
            : [];

    /// <summary>A mode the map playing cannot play is refused at the call, with the reason (no flag, no control points).</summary>
    public string? Refusal(VoteOption choice)
        => catalog.Supports(catalog.CurrentMap, choice.Id) is { Supported: false } no ? no.Reason : null;
}

/// <summary>A setting of the playing mode, as its catalog row offers it (stock's <c>CClassLimitsIssue</c> is this shape).</summary>
public sealed class SettingVoteIssue(GameModeCatalog catalog, ModeSettingWrites writes, VoteIssueSettingsAllowedConVar allowed) : IVoteIssue, IOrderedHook
{
    public int HookOrder => VoteIssueOrders.Setting;

    public VoteKind Kind => VoteKind.Setting;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value ? GameVoteOptions.Of([], [], null, false, false, false, GameVoteOptions.Votable(catalog.Active), writes.Current) : [];
}

/// <summary>Stock's <c>CRestartGameIssue</c>: <c>mp_restartgame 1</c>.</summary>
public sealed class RestartVoteIssue(VoteIssueRestartGameAllowedConVar allowed) : IVoteIssue, IOrderedHook
{
    public int HookOrder => VoteIssueOrders.Restart;

    public VoteKind Kind => VoteKind.Restart;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value ? [new VoteOption(VoteKind.Restart, VoteOptionWords.Restart, "", "mp_restartgame 1")] : [];
}

/// <summary>Stock's <c>CScrambleTeams</c>: offered only while a mode that can scramble plays.</summary>
public sealed class ScrambleVoteIssue(GameModes modes, VoteIssueScrambleTeamsAllowedConVar allowed) : IVoteIssue, IOrderedHook
{
    public int HookOrder => VoteIssueOrders.Scramble;

    public VoteKind Kind => VoteKind.Scramble;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value && modes.Active is ITeamScramble
            ? [new VoteOption(VoteKind.Scramble, VoteOptionWords.Scramble, "", TeamScrambleCommand.Name)]
            : [];
}

/// <summary>
/// Stock's <c>CNextLevelIssue</c>: the map after this one, set in <c>nextlevel</c>
/// for the intermission to change to. Each candidate is a Yes/No choice, and
/// with two or more the word alone (<c>nextmap</c>) is a choice among the first
/// five; refused while the next map is already set, as stock refuses it. With
/// <c>sv_vote_issue_nextlevel_choicesmode</c> on, a tenth of the voters is a
/// quorum (stock's <c>GetQuorumRatio</c>: a few with a preference decide).
/// </summary>
public sealed class NextLevelVoteIssue(MapCandidates candidates, NextLevelConVar nextLevel, VoteIssueNextLevelAllowedConVar allowed,
                                       VoteIssueNextLevelChoicesModeConVar choicesMode) : IVoteIssue, IOrderedHook
{
    /// <summary>Stock's quorum for the issue in choices mode.</summary>
    public const float ChoicesQuorum = 0.1f;

    public int HookOrder => VoteIssueOrders.NextLevel;

    public VoteKind Kind => VoteKind.NextMap;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value ? Of(candidates.Votable(), Quorum) : [];

    /// <summary>The quorum this issue's votes are judged by: null for <c>sv_vote_quorum_ratio</c>.</summary>
    public float? Quorum => choicesMode.Value ? ChoicesQuorum : null;

    /// <summary>The choices over <paramref name="maps"/>: one each, then the choice among the first five.</summary>
    public static IEnumerable<VoteOption> Of(IReadOnlyList<MapCandidate> maps, float? quorum = null)
    {
        foreach (var map in maps)
            yield return new VoteOption(VoteKind.NextMap, map.Map, map.Label, $"nextlevel {map.Map}") { Quorum = quorum };
        if (maps.Count >= 2)
            yield return Choice([.. maps.Select(m => m.Map)], quorum);
    }

    /// <summary>A choice among the first five of <paramref name="maps"/>, each setting <c>nextlevel</c> when it wins.</summary>
    public static VoteOption Choice(IReadOnlyList<string> maps, float? quorum = null)
        => new(VoteKind.NextMap, VoteOptionWords.NextMap, "#SS_UI_Vote_NextMapChoose", "",
               [.. maps.Take(VoteWire.MaxOptions).Select(m => new VoteChoice(m, $"nextlevel {m}"))]) { Quorum = quorum };

    public string? Refusal(VoteOption choice)
        => nextLevel.Value is { Length: > 0 } set ? $"the next map is already {set}" : null;
}

/// <summary>
/// Stock's <c>CExtendLevelIssue</c>: twenty minutes more on this map (stock's
/// number), offered only while the map has a time limit to extend.
/// </summary>
public sealed class ExtendVoteIssue(MpTimeLimitConVar timeLimit, VoteIssueExtendLevelAllowedConVar allowed) : IVoteIssue, IOrderedHook
{
    /// <summary>Minutes an extension adds.</summary>
    public const int Minutes = 20;

    public int HookOrder => VoteIssueOrders.Extend;

    public VoteKind Kind => VoteKind.Extend;

    public IEnumerable<VoteOption> Choices()
        => allowed.Value && timeLimit.Value > 0f
            ? [new VoteOption(VoteKind.Extend, VoteOptionWords.Extend, "",
                              string.Create(System.Globalization.CultureInfo.InvariantCulture, $"mp_timelimit {timeLimit.Value + Minutes:0.##}"))]
            : [];
}

/// <summary>
/// The end-of-map vote (stock TF2's <c>ManageServerSideVoteCreation</c>, on with
/// <c>sv_vote_issue_nextlevel_choicesmode</c>; plan_bots_teams_tf2 D.3): when a
/// level's round ends the server calls a choice of the next map among up to five
/// candidates, in cycle order from the map the cycle plays next, the map playing
/// left out, and the intermission waits for it to be decided; its winner sets
/// <c>nextlevel</c>. Not while <c>nextlevel</c> is set already (stock's rule).
/// Stock calls it in the round, two minutes before the time limit or a round
/// from the win limit; here the intermission holds it (docs/divergences.md).
/// </summary>
public sealed class EndOfMapVote(ServerVotes votes, VoteOffice office, MapCandidates candidates, NextLevelConVar nextLevel,
                                 VoteIssueNextLevelAllowedConVar allowed, VoteIssueNextLevelChoicesModeConVar choicesMode, ILog log)
    : IIntermissionHook
{
    private bool _called;

    public void OnIntermission(string currentMap, string nextMap, float now)
    {
        _called = false;
        if (!allowed.Value || !choicesMode.Value || nextLevel.Value.Length > 0)
            return;
        var maps = Ballot(candidates.Votable().Select(c => c.Map).ToList(), currentMap, nextMap);
        if (maps.Count < 2)
        {
            log.Msg($"[SourceSharp/Vote] no end-of-map vote: {maps.Count} map(s) to choose from\n");
            return;
        }
        if (votes.CallAsServer(NextLevelVoteIssue.Choice(maps, NextLevelVoteIssue.ChoicesQuorum)) is { } why)
        {
            log.Msg($"[SourceSharp/Vote] no end-of-map vote: {why}\n");
            return;
        }
        _called = true;
    }

    /// <summary>While its ballot is open, or passed and not yet run, the level waits.</summary>
    public bool HoldsLevel(float now) => _called && office.Current is { IsUnresolved: true };

    /// <summary>
    /// The choices: <paramref name="maps"/> without <paramref name="currentMap"/>,
    /// turned to start at <paramref name="nextMap"/> (a tie goes to the first, so
    /// to the map the cycle would have played), the first five.
    /// </summary>
    public static IReadOnlyList<string> Ballot(IReadOnlyList<string> maps, string currentMap, string nextMap)
    {
        var others = maps.Where(m => !string.Equals(m, currentMap, StringComparison.OrdinalIgnoreCase)).ToList();
        int start = others.FindIndex(m => string.Equals(m, nextMap, StringComparison.OrdinalIgnoreCase));
        if (start > 0)
            others = [.. others.Skip(start), .. others.Take(start)];
        return [.. others.Take(VoteWire.MaxOptions)];
    }
}
