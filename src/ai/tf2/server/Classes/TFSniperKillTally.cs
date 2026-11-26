using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Combat;
using TF2.Shared;
using TF2.Shared.Combat;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// The kills each player has scored by the victim's class, and how many were headshots (the killing hit's custom kind, a rifle's
/// or an arrow's: <see cref="TFDamageCustom.Headshot"/> or its decapitation), for the Sniper lane's measure
/// (<c>/ai/sniper/kills</c>, round 2's Ready table: Medics, Snipers and Heavies count most). Read by the route only; no bot
/// reads it. It hangs on the death notice, after the game's own keys and while the killing hit's record is still held.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSniperKillTally(TFDamageRecords records, ITFWeaponOwners owners) : IDeathNoticeHook, IDebugRouteProvider, IOrderedHook
{
    public const string Route = "/ai/sniper/kills";

    /// <summary>Among the death notice's hooks after the game's keys and beside the AI's kill feed (TFKillFeedTap's +906); among the routes after /ai/classes.</summary>
    public int HookOrder => ModStages.First + 938;

    private const int Classes = (int)TFClass.Engineer + 1;

    private readonly int[,] _kills = new int[VisionPass.Slots, Classes];
    private readonly int[] _headshots = new int[VisionPass.Slots];

    public IReadOnlyList<DebugRoute> Routes => [new("GET", Route, "TF2 kills by scorer: the victims' classes and the headshots (the Sniper lane's measure)")];

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        int scorer = death.Scorer.EntryIndex;
        if (death.Scorer == EntityHandle.None || death.Scorer == death.Victim || (uint)scorer >= (uint)_headshots.Length)
            return;
        int victim = (int)owners.ClassOf(death.Victim.EntryIndex);
        if ((uint)victim < Classes)
            _kills[scorer, victim]++;
        if (records.LastHit(death.Victim.EntryIndex) is { } hit && hit.Attacker == death.Scorer && IsHeadshot(hit.Custom))
            _headshots[scorer]++;
    }

    /// <summary>Whether a killing hit's kind is a headshot's (the crit's or the Bazaar Bargain's decapitation).</summary>
    public static bool IsHeadshot(TFDamageCustom custom) => custom is TFDamageCustom.Headshot or TFDamageCustom.HeadshotDecapitation;

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

        return new DebugRouteReply(200, AiJson.Write(new { scorers = rows }));
    }
}
