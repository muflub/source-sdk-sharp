using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.OwnerViews;

/// <summary>
/// THE SKILL TREE'S RANKS, TO ITS OWNER (plan_descent_logic UI3): for every
/// skill in the rules' table, the hero's real rank, why the next rank cannot
/// be bought now (the rules' own <see cref="SkillRules.Buy"/> refusal, asked
/// of the live sheet), and the hero level the next rank asks
/// (<see cref="SkillRules.LevelFor"/>), as <see cref="SkillRanks"/>. The
/// client draws the tree from these and the content it embeds; the buy
/// itself is the server's (<c>tf2_descent_learn</c>). Sent when the sheet
/// changes (a rank bought, a level gained, a respec: the stats are derived
/// again each time) and at each spawn; never the same bytes twice in a row.
/// </summary>
[SideService(Side.Server)]
public sealed class OwnerSkillSender(IUserMessageSender sender, Heroes heroes, TF2DescentRules rules)
    : IHeroStatsWatcher, IPlayerSpawnHook, IClientLifecycle, IOrderedHook
{
    /// <summary>In TF2 Descent's owner-views block (UI3).</summary>
    public const int Order = ModStages.First + 1120;

    public int HookOrder => Order;

    public int ClientLifecycleOrder => Order;

    private readonly Dictionary<int, string> _sent = new();
    private uint? _fingerprint;

    private uint Fingerprint => _fingerprint ??= ContentKeys.Fingerprint(rules.Content);

    public void StatsChanged(HeroComponent hero) => Send(hero, always: false);

    public void HeroLeft(int playerIndex) => _sent.Remove(playerIndex);

    public void OnClientDisconnected(int entIndex) => _sent.Remove(entIndex);

    public void OnSpawning(EntityHandle player, float now) { }

    public void OnSpawned(EntityHandle player, float now)
    {
        if (heroes.All.FirstOrDefault(h => h.Player == player) is { } hero)
            Send(hero, always: true);
    }

    /// <summary>Why the next rank of <paramref name="skill"/> cannot be bought on <paramref name="sheet"/> now.</summary>
    public SkillWhy WhyOf(HeroSheet sheet, SkillDef skill) => rules.Skills.Buy(sheet, skill.Id).Refusal switch
    {
        null => SkillWhy.Buyable,
        BuyRefusal.UnknownSkill => SkillWhy.UnknownSkill,
        BuyRefusal.OtherClass => SkillWhy.OtherClass,
        BuyRefusal.BelowLevel => SkillWhy.BelowLevel,
        BuyRefusal.Prerequisite => SkillWhy.Prerequisite,
        BuyRefusal.AtMaxRank => SkillWhy.AtMaxRank,
        _ => SkillWhy.NoPoints,
    };

    /// <summary>The messages of <paramref name="sheet"/>'s ranks, <see cref="SkillRanks.MaxSkills"/> skills each.</summary>
    public IReadOnlyList<SkillRanks> Of(HeroSheet sheet)
    {
        var skills = rules.Content.Skills.Rows;
        var messages = new List<SkillRanks>();
        for (int first = 0; first < skills.Count || first == 0; first += SkillRanks.MaxSkills)
        {
            var part = skills.Skip(first).Take(SkillRanks.MaxSkills).ToList();
            messages.Add(new SkillRanks
            {
                Fingerprint = Fingerprint,
                First = (byte)Math.Min(first, byte.MaxValue),
                Total = (byte)Math.Min(skills.Count, byte.MaxValue),
                Ranks = [.. part.Select(s => (byte)Math.Clamp(SkillRules.RealRank(sheet, s.Id), 0, byte.MaxValue))],
                Why = [.. part.Select(s => (byte)WhyOf(sheet, s))],
                NextLevel = [.. part.Select(s =>
                {
                    int rank = SkillRules.RealRank(sheet, s.Id);
                    return (byte)(rank < s.MaxRank ? Math.Clamp(rules.Skills.LevelFor(s, rank + 1), 0, byte.MaxValue) : 0);
                })],
            });
            if (skills.Count == 0)
                break;
        }
        return messages;
    }

    private void Send(HeroComponent hero, bool always)
    {
        if (hero.PlayerIndex <= 0 || hero.Sheet is not { } sheet)
            return;
        var messages = Of(sheet);
        string key = string.Join("|", messages.Select(m => $"{m.First}:{Convert.ToHexString(m.Ranks)}:{Convert.ToHexString(m.Why)}:{Convert.ToHexString(m.NextLevel)}"));
        if (!always && _sent.TryGetValue(hero.PlayerIndex, out var last) && last == key)
            return;
        _sent[hero.PlayerIndex] = key;
        foreach (var message in messages)
            sender.Send(message, hero.PlayerIndex);
    }
}
