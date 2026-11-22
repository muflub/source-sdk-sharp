using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Rules.Loot;

/// <summary>
/// One player's roll of a kill (or of a container): nothing, a potion of <see cref="Potion"/>,
/// or an item of rarity tier <see cref="Tier"/> (−1 when no item); and the Australium the kill
/// pays the whole party in range (rolled once: only seat 0's roll carries it), before the
/// split.
/// </summary>
public sealed record KillLoot(LootOutcome Outcome, int Tier, string? Potion, long Australium)
{
    public bool Item => Outcome == LootOutcome.Item;
}

/// <summary>
/// The robot a loot roll is for, as the string <c>IGameRules.KillRoll</c> and <c>BossDrops</c>
/// carry: its template, then each champion affix after a '+', in ordinal order
/// ("giant_soldier+extra_fast+fireproof"). The contract has no affix input (plan_descent_logic
/// X1), so the affixes ride in the template string, where the host replays them as given.
/// </summary>
public sealed record LootKey(string Template, ImmutableArray<string> Affixes)
{
    public const char Separator = '+';

    public bool Champion => !Affixes.IsEmpty;

    /// <summary>The key of a robot: its template and its affixes, sorted.</summary>
    public static LootKey Of(string template, IEnumerable<string>? affixes = null) =>
        new(template, [.. (affixes ?? []).Order(StringComparer.Ordinal)]);

    public static LootKey Parse(string key)
    {
        var parts = key.Split(Separator);
        return Of(parts[0], parts.Skip(1));
    }

    public override string ToString() =>
        Affixes.IsEmpty ? Template : Template + Separator + string.Join(Separator, Affixes);
}

/// <summary>
/// Loot (plan_tf2_rpg 2c; rulings R4, D11, D13; plan_descent_logic RU4 and X1), pure over
/// loot.json, levels.json and the robot tables.
///
/// <para><b>Once per eligible player (R4).</b> <c>KillRoll</c> takes no player, so a kill is
/// rolled once per party seat by advancing the kill sequence: seat s of kill k rolls with
/// <c>killSeq = k · seats + s</c> (<see cref="KillSeq"/>, seats = robot_rules' party
/// maximum). Each seat's roll is its own stream, independent of the others and of the party
/// size (D11: loot rolls are per player, so a party does not change them).</para>
///
/// <para><b>Australium (R4)</b> is rolled once a kill, from a stream of the kill alone, and only
/// seat 0's roll carries it; <see cref="Split"/> shares it among the members in range,
/// conserving the total. The total rises by the table's percent for each member past the
/// first.</para>
///
/// <para><b>Quality.</b> A template pays by the XP it is worth at the middle of its scale range
/// (2f: XP and loot follow life × temperament): the item weight rises by quality^exponent and
/// the Australium by quality. A champion's affixes each raise both and add a tier roll, the best
/// of which stands.</para>
/// </summary>
public sealed class LootRules
{
    private readonly DescentContent _content;
    private readonly RobotRules _robots;
    private readonly ItemRules _items;
    private readonly IReadOnlyDictionary<string, double> _quality;

    public LootRules(DescentContent content, RobotRules robots, ItemRules items)
    {
        _content = content;
        _robots = robots;
        _items = items;
        var quality = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var t in content.Robots.Rows)
            quality[t.Id] = Math.Clamp(robots.Stats(t, (t.ScaleMin + t.ScaleMax) / 2, 1, []).Xp / content.Loot.ReferenceXp,
                content.Loot.QualityMin, content.Loot.QualityMax);
        _quality = quality;
    }

    private LootSettings S => _content.Loot;

    //-------------------------------------------------------------------------
    // Seats
    //-------------------------------------------------------------------------

    /// <summary>The seats a kill is rolled for: the party's most members.</summary>
    public int Seats => _content.RobotRules.Party.MaxMembers;

    /// <summary>The kill sequence to roll seat <paramref name="seat"/> of kill <paramref name="kill"/> with: kill · seats + seat.</summary>
    public uint KillSeq(uint kill, int seat)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seat);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, Seats);
        return checked(kill * (uint)Seats + (uint)seat);
    }

    /// <summary>The kill and the seat a kill sequence rolls for.</summary>
    public (uint Kill, int Seat) Decode(uint killSeq) => (killSeq / (uint)Seats, (int)(killSeq % (uint)Seats));

    //-------------------------------------------------------------------------
    // Kills
    //-------------------------------------------------------------------------

    /// <summary>
    /// A template's quality: the XP it pays at the middle of its scale range (level 1, no
    /// affixes) over the reference, clamped. An unknown template throws, naming it.
    /// </summary>
    public double Quality(string template) =>
        _quality.TryGetValue(template, out var q) ? q : throw new ArgumentException($"'{template}' is not a robot template", nameof(template));

    /// <summary>
    /// One seat's roll of a kill: what <c>IGameRules.KillRoll</c> answers (<see cref="ToContract"/>).
    /// Pure in its inputs and <paramref name="salt"/>.
    /// </summary>
    public KillLoot Roll(ulong instanceSeed, uint killSeq, string robotKey, int depth, int partySize, ulong salt = 0) =>
        Roll(instanceSeed, killSeq, robotKey, depth, partySize, salt, itemDropPct: 0);

    /// <summary>
    /// <b>Not auditable by the host:</b> <see cref="Roll"/> with the player's magic find
    /// (<c>item_drop_pct</c>, Scavenger's +6 % a rank, raising the item weight in proportion).
    /// <c>KillRoll</c> has no input for it (plan_descent_logic X1), so the host replays the roll
    /// without it; with 0 this is exactly <see cref="Roll"/>. Australium find applies to the
    /// player's share (<see cref="FoundShare"/>).
    /// </summary>
    public KillLoot RollWithFind(ulong instanceSeed, uint killSeq, string robotKey, int depth, int partySize, double itemDropPct, ulong salt = 0) =>
        Roll(instanceSeed, killSeq, robotKey, depth, partySize, salt, itemDropPct);

    private KillLoot Roll(ulong instanceSeed, uint killSeq, string robotKey, int depth, int partySize, ulong salt, double itemDropPct)
    {
        var key = Key(robotKey);
        var row = _robots.Depth(depth);
        var table = S.Table(row.LootTable);
        double q = Quality(key.Template);
        int n = key.Affixes.Length;
        var (kill, seat) = Decode(killSeq);

        var r = Seeded.From(instanceSeed, "loot", salt, killSeq, key.ToString(), row.Depth);
        double item = table.Item * Math.Pow(q, S.ItemQualityExponent) * (1 + S.Champion.ItemPctPerAffix * n / 100)
                      * Math.Max(0, 1 + itemDropPct / 100);
        var (outcome, tier, potion) = Outcome(ref r, table, row.Depth, item, 1 + n * S.Champion.TierRollsPerAffix);

        long australium = 0;
        if (seat == 0)
        {
            var a = Seeded.From(instanceSeed, "australium", salt, kill, key.ToString(), row.Depth);
            if (a.Chance(S.Australium.ChancePct))
            {
                int party = Math.Clamp(partySize, 1, Seats);
                australium = Amount(ref a, row.Depth, q * (1 + S.Champion.AustraliumPctPerAffix * n / 100)
                                                       * (1 + S.Australium.PerExtraMemberPct * (party - 1) / 100));
            }
        }
        return new KillLoot(outcome, tier, potion, australium);
    }

    /// <summary>
    /// The TF2 ammo pack kill <paramref name="kill"/> of <paramref name="robotKey"/> drops
    /// (<see cref="AmmoPacks"/>), or null: its template's size class's row of loot.json's
    /// <c>ammo</c>, at its chance. Seeded like the kill's loot (the instance seed, the kill,
    /// the robot's key, the depth, the salt) on a stream of its own, so a replay agrees and
    /// the loot's rolls are untouched. Not loot: no seat, nothing the host books.
    /// </summary>
    public string? Ammo(ulong instanceSeed, uint kill, string robotKey, int depth, ulong salt = 0)
    {
        var key = Key(robotKey);
        if (S.AmmoOf(_content.Robots[key.Template].Size) is not { } row || row.ChancePct <= 0)
            return null;

        var r = Seeded.From(instanceSeed, "ammo", salt, kill, key.ToString(), _robots.Depth(depth).Depth);
        return r.Chance(row.ChancePct) ? row.Pack : null;
    }

    /// <summary>The roll's outcome, its tier (the best of <paramref name="tierRolls"/>) and its potion.</summary>
    private (LootOutcome, int, string?) Outcome(ref Seeded r, LootTableRow table, int depth, double itemWeight, int tierRolls)
    {
        int pick = r.Pick([table.Nothing, table.Potion, itemWeight]);
        int tier = -1;
        string? potion = null;
        if (pick == 2)
            tier = BestTier(ref r, table.Tiers, depth, tierRolls, 0);
        else if (pick == 1)
            potion = table.Potions[r.Pick([.. table.Potions.Select(p => p.Weight)])].Base;
        return (pick switch { 1 => LootOutcome.Potion, 2 => LootOutcome.Item, _ => LootOutcome.Nothing }, tier, potion);
    }

    /// <summary>The best of <paramref name="rolls"/> tier rolls at the depth, at least <paramref name="floor"/>.</summary>
    private int BestTier(ref Seeded r, IReadOnlyList<TierWeight> tiers, int depth, int rolls, int floor)
    {
        var weights = tiers.Select(t => t.At(depth)).ToList();
        int best = floor;
        for (int k = 0; k < Math.Max(1, rolls); k++)
            best = Math.Max(best, Rarity(tiers[r.Pick(weights)].Rarity));
        return best;
    }

    private int Rarity(string id)
    {
        var rows = _content.Rarities.Rows;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Id == id) return i;
        throw new KeyNotFoundException($"'{id}' is not a rarity");
    }

    /// <summary>base · growth^(depth − 1) · <paramref name="scale"/> · a spread, rounded, at least 1.</summary>
    private long Amount(ref Seeded r, int depth, double scale)
    {
        var au = S.Australium;
        double spread = 1 + au.SpreadPct / 100 * (2 * r.NextDouble() - 1);
        double amount = au.Base * Math.Pow(1 + au.PerDepthPct / 100, depth - 1) * scale * spread;
        return Math.Max(1, (long)Math.Round(amount, MidpointRounding.AwayFromZero));
    }

    private LootKey Key(string robotKey)
    {
        var key = LootKey.Parse(robotKey);
        if (!_content.Robots.Contains(key.Template))
            throw new ArgumentException($"'{key.Template}' is not a robot template", nameof(robotKey));
        foreach (var a in key.Affixes.Where(a => !_content.Champions.Contains(a)))
            throw new ArgumentException($"'{a}' is not a champion affix", nameof(robotKey));
        return key;
    }

    /// <summary>The contract's answer: an item drops with its tier; Australium as rolled. A potion is no item to the host.</summary>
    public static KillRoll ToContract(KillLoot loot) => new(loot.Item, loot.Item ? loot.Tier : -1, loot.Australium);

    /// <summary>Every seat's roll of one kill, seat 0 first: each eligible player's own roll (R4).</summary>
    public IReadOnlyList<KillLoot> PerSeat(ulong instanceSeed, uint kill, string robotKey, int depth, int partySize, ulong salt = 0) =>
        [.. Enumerable.Range(0, Math.Clamp(partySize, 1, Seats)).Select(s => Roll(instanceSeed, KillSeq(kill, s), robotKey, depth, partySize, salt))];

    /// <summary>
    /// A kill's Australium shared among <paramref name="members"/> members in range (R4): each
    /// gets total ÷ members, and the remainder goes one apiece to the first members in the order
    /// given (the killer first). The shares add up to the total.
    /// </summary>
    public static IReadOnlyList<long> Split(long total, int members)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfLessThan(members, 1);
        long each = total / members;
        long rest = total % members;
        return [.. Enumerable.Range(0, members).Select(i => each + (i < rest ? 1 : 0))];
    }

    /// <summary>
    /// <b>Not auditable by the host:</b> a member's share raised by its <c>australium_find_pct</c>
    /// (Scavenger's +4 % a rank), rounded down. The raise is new Australium, beyond the split.
    /// </summary>
    public static long FoundShare(long share, double australiumFindPct) =>
        share + Math.Max(0, (long)Math.Floor(share * australiumFindPct / 100));

    /// <summary>The context a kill's item is rolled in: the depth's item level and loot table.</summary>
    public ItemRollContext KillContext(string classId, int heroLevel, int depth, int tier, string source = "kill")
    {
        var row = _robots.Depth(depth);
        return new ItemRollContext(classId, heroLevel, depth, row.ItemLevel, tier, row.LootTable, source);
    }

    //-------------------------------------------------------------------------
    // Bosses and champions (tier −1)
    //-------------------------------------------------------------------------

    /// <summary>
    /// A boss's or a champion's own table for one player (<c>IGameRules.BossDrops</c>): a boss
    /// rolls its items on its tier weights and gives its potions (D13); a champion gives the
    /// table's items for each affix, the best of its tier rolls and at least the minimum rarity;
    /// an ordinary robot has no table of its own and gives nothing.
    /// </summary>
    public IReadOnlyList<RolledItem> BossDrops(ItemRollContext ctx, string robotKey, ulong seed)
    {
        var key = Key(robotKey);
        var list = new List<RolledItem>();
        var r = Seeded.From(seed, "boss_drops", key.ToString());
        if (S.Boss(key.Template) is { } boss)
        {
            var weights = boss.Tiers.Select(t => t.Weight).ToList();
            for (int i = 0; i < boss.Items; i++)
            {
                int tier = Rarity(boss.Tiers[r.Pick(weights)].Rarity);
                list.Add(_items.Roll(ctx with { Tier = tier, Source = "boss:" + key.Template }, r.NextULong()));
            }
            for (int i = 0; i < boss.Potions; i++)
                list.Add(_items.Roll(ctx with { Tier = 0, LootTable = _content.ItemSettings.ForcedBasePrefix + boss.Potion, Source = "boss:" + key.Template },
                    r.NextULong()));
            return list;
        }
        if (!key.Champion) return list;
        var table = S.Table(_robots.Depth(ctx.Depth).LootTable);
        int n = key.Affixes.Length;
        int floor = Rarity(S.Champion.MinRarity);
        for (int i = 0; i < S.Champion.Items * n; i++)
        {
            int tier = BestTier(ref r, table.Tiers, ctx.Depth, 1 + n * S.Champion.TierRollsPerAffix, floor);
            list.Add(_items.Roll(ctx with { Tier = tier, Source = "champion:" + key }, r.NextULong()));
        }
        return list;
    }

    /// <summary>The Australium a boss pays (D13). The contract carries no Australium for tier −1 drops; the game pays it until it does.</summary>
    public long BossAustralium(string template) => S.Boss(template)?.Australium ?? 0;

    //-------------------------------------------------------------------------
    // Containers
    //-------------------------------------------------------------------------

    /// <summary>
    /// A container's roll for one player (2l, R4), the hook <see cref="ContainerRules.Open{TLoot}"/>
    /// calls once per eligible player: the request's table at the depth, at quality 1, and the
    /// player's own Australium (a container is not split: each player opens their own).
    /// </summary>
    public KillLoot Container(LootRequest request, int depth)
    {
        var row = _robots.Depth(depth);
        var table = S.Table(request.LootTable);
        var r = Seeded.From(request.Seed, "container_loot", request.LootTable, row.Depth);
        var (outcome, tier, potion) = Outcome(ref r, table, row.Depth, table.Item, 1);
        long australium = r.Chance(S.Australium.ContainerChancePct) ? Amount(ref r, row.Depth, 1) : 0;
        return new KillLoot(outcome, tier, potion, australium);
    }

    /// <summary>The container hook for a depth, in the shape <see cref="ContainerRules.Open{TLoot}"/> takes.</summary>
    public Func<LootRequest, KillLoot> ContainerHook(int depth) => request => Container(request, depth);
}
