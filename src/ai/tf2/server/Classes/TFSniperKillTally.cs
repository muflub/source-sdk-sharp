using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The kills each player has scored by the victim's class, and how many were headshots, for the Sniper lane's measure
/// (<c>/ai/sniper/kills</c>, round 2's Ready table). Read by the route only; no bot reads it. It is cleared when the scoreboard
/// is (the match reset at the end of waiting for players, and the level's shutdown), so its counts are the scoreboard's kills
/// split by class. It depends on nothing: the round flow raises the reset to every member of its chain, so a member that
/// needed the weapons (as the tally that fills this does) closed a cycle through them and the server did not compose.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSniperKillCounts : IDebugRouteProvider, IOrderedHook, IMatchResetHook, IGameLifecycle
{
    public const string Route = "/ai/sniper/kills";

    /// <summary>Among the routes after /ai/classes.</summary>
    public int HookOrder => ModStages.First + 938;

    private const int Classes = (int)TFClass.Engineer + 1;

    private readonly int[,] _kills = new int[VisionPass.Slots, Classes];
    private readonly int[] _headshots = new int[VisionPass.Slots];

    /// <summary>Players soaked in Jarate (their condition coming on) since the last reset: the Sniper lane's jar measure.</summary>
    public int Soaked { get; private set; }

    /// <summary>A player's Jarate came on.</summary>
    public void NoteSoaked() => Soaked++;

    public IReadOnlyList<DebugRoute> Routes => [new("GET", Route, "TF2 kills by scorer: the victims' classes and the headshots (the Sniper lane's measure)")];

    /// <summary>A kill <paramref name="scorer"/> made on a <paramref name="victim"/>, a headshot or not.</summary>
    public void Note(int scorer, TFClass victim, bool headshot)
    {
        if ((uint)scorer >= (uint)_headshots.Length)
            return;
        if ((uint)victim < Classes)
            _kills[scorer, (int)victim]++;
        if (headshot)
            _headshots[scorer]++;
    }

    /// <summary>The scores were reset (waiting for players ended, a restart): the tally with them.</summary>
    public void OnMatchReset(string why) => Clear();

    /// <summary>Its own stage among the level's participants (a stage is unique per participant): late, a measurement.</summary>
    public int LifecycleOrder => ModStages.First + 939;

    public void OnLevelShutdown() => Clear();

    private void Clear()
    {
        Array.Clear(_kills);
        Array.Clear(_headshots);
        Soaked = 0;
    }

    /// <summary>The kills the player in <paramref name="slot"/> scored on <paramref name="victim"/>.</summary>
    public int Kills(int slot, TFClass victim) => (uint)slot < (uint)_headshots.Length && (uint)victim < Classes ? _kills[slot, (int)victim] : 0;

    /// <summary>The headshot kills the player in <paramref name="slot"/> scored.</summary>
    public int Headshots(int slot) => (uint)slot < (uint)_headshots.Length ? _headshots[slot] : 0;

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        var rows = new List<object>();
        for (int slot = 0; slot < _headshots.Length; slot++)
        {
            var by = new Dictionary<string, int>();
            for (int c = 1; c < Classes; c++)
            {
                if (_kills[slot, c] > 0)
                    by[TFClassNames.Raw((TFClass)c)] = _kills[slot, c];
            }

            if (by.Count > 0 || _headshots[slot] > 0)
                rows.Add(new { slot, kills = by, headshots = _headshots[slot] });
        }

        return new DebugRouteReply(200, AiJson.Write(new { scorers = rows, soaked = Soaked }));
    }
}

/// <summary>
/// Fills <see cref="TFSniperKillCounts"/> from the death notice, after the game's own keys and while the killing hit's record is
/// still held: the victim's class, and whether the killing hit was the scorer's own headshot (<see cref="TFDamageCustom.Headshot"/>
/// or its decapitation, a rifle's or an arrow's).
/// </summary>
[SideService(Side.Server)]
public sealed class TFSniperKillTally(TFDamageRecords records, ITFWeaponOwners owners, TFSniperKillCounts counts) : IDeathNoticeHook, IOrderedHook
{
    /// <summary>Among the death notice's hooks after the game's keys and beside the AI's kill feed (TFKillFeedTap's +906).</summary>
    public int HookOrder => ModStages.First + 938;

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        if (death.Scorer == EntityHandle.None || death.Scorer == death.Victim)
            return;
        bool headshot = records.LastHit(death.Victim.EntryIndex) is { } hit && hit.Attacker == death.Scorer && IsHeadshot(hit.Custom);
        counts.Note(death.Scorer.EntryIndex, owners.ClassOf(death.Victim.EntryIndex), headshot);
    }

    /// <summary>Whether a killing hit's kind is a headshot's (the crit's or the Bazaar Bargain's decapitation).</summary>
    public static bool IsHeadshot(TFDamageCustom custom) => custom is TFDamageCustom.Headshot or TFDamageCustom.HeadshotDecapitation;
}

/// <summary>
/// Counts each player's Jarate coming on (TF_COND_URINE, off to on, from the server's condition carriers) into
/// <see cref="TFSniperKillCounts.Soaked"/>, once a frame after the think: the jar measure's "enemies soaked" (a Sniper's jar is
/// the only Jarate in the lane's runs). A measurement; no bot reads it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSniperSoakTally(ITFPlayerConditions conditions, TFSniperKillCounts counts) : IFrameLifecycle
{
    /// <summary>Its own stage among the frame's participants: late, a measurement.</summary>
    public int FrameOrder => ModStages.First + 939;

    private readonly bool[] _soaked = new bool[VisionPass.Slots];

    public void OnPostThink(float frameTime) => Observe(i => conditions.Of(i)?.Has(TFCond.Urine) ?? false);

    /// <summary>One frame's look at every slot: a soak counted where it came on since the last.</summary>
    public void Observe(Func<int, bool> soaked)
    {
        for (int i = 1; i < _soaked.Length; i++)
        {
            bool now = soaked(i);
            if (now && !_soaked[i])
                counts.NoteSoaked();
            _soaked[i] = now;
        }
    }
}
