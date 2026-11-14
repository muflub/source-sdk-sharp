using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

using RulesBestiaryView = TF2.Descent.Rules.Bestiary.BestiaryView;

namespace TF2.Descent.Server.Bestiary;

/// <summary>
/// WHAT EACH HERO MAY KNOW OF THE ROBOTS (plan_descent_logic UI6, ruling L6,
/// default E7), told to him alone. The body's networked state carries what
/// anyone may see (the bar's fraction, template, champion flag, statuses);
/// everything the bestiary gates goes from here, decided by the rules'
/// <see cref="BestiaryRules"/> for that hero's kills of the robot's template:
///
/// <list type="bullet">
/// <item><b>The counts</b> (<see cref="BestiaryCounts"/>): his kills per
/// template, all of them at each spawn (a resync: the client forgets its
/// counts when a level ends), and the changed rows whenever his sheet's
/// bestiary changes (a kill, the dev command).</item>
/// <item><b>The robot views</b> (<see cref="RobotViewWire"/>): for each lent
/// body, the rules' <see cref="BestiaryRules.View"/> of it for his sheet, put
/// on the wire with no bytes at all for a part the view hides. Sent when the
/// body is lent (a new serial), when his reveal of its template changes (a
/// threshold crossed: the newly revealed parts arrive then, and only then),
/// and when a part he may see changes (the life numbers, once revealed);
/// with template 0 when a body he was told of is parked. Never twice the same
/// state in a row.</item>
/// </list>
///
/// Looked at every <see cref="PollFrames"/> frames. The decision is the
/// rules'; this only compares numbers and carries the answer.
/// </summary>
[SideService(Side.Server)]
public sealed class RobotViewSender(IUserMessageSender sender, Heroes heroes, IRobotFactsSource robots, TF2DescentRules rules)
    : IFrameLifecycle, IPlayerSpawnHook, IClientLifecycle, IGameLifecycle, IOrderedHook
{
    /// <summary>TF2 Descent's robot-bar block (UI6), after the owner views (1100s).</summary>
    public const int Order = ModStages.First + 1200;

    /// <summary>How often the lent bodies and the sheets are looked at.</summary>
    public const int PollFrames = 2;

    public int HookOrder => Order;

    public int FrameOrder => Order;

    public int ClientLifecycleOrder => Order;

    public int LifecycleOrder => Order;

    /// <summary>What one hero has been told.</summary>
    private sealed class Told
    {
        /// <summary>The sheet's bestiary the reveals below were read from (the rules' sheet changes by reference).</summary>
        public ImmutableSortedDictionary<string, int>? Bestiary;

        /// <summary>The counts sent, by template id.</summary>
        public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);

        /// <summary>The reveal of each template, from <see cref="Bestiary"/>.</summary>
        public readonly Dictionary<string, RevealBits> Reveals = new(StringComparer.Ordinal);

        /// <summary>The state of each body last told, by entity index.</summary>
        public readonly Dictionary<int, ViewKey> Views = new();

        public readonly HashSet<int> Seen = new();
    }

    /// <summary>What decides whether a body's view is told again: its lend, its template, the reveal, and the revealed numbers that move.</summary>
    private readonly record struct ViewKey(int Serial, int Template, RevealBits Reveal, int Life, int MaxLife);

    private readonly Dictionary<int, Told> _told = new();
    private readonly List<LentRobot> _lent = new();
    private int _frame;
    private uint? _fingerprint;

    private uint Fingerprint => _fingerprint ??= RobotKeys.Fingerprint(rules.Content);

    /// <summary>Views and counts sent since the server started, for the dev print and the facts.</summary>
    public long ViewsSent { get; private set; }

    public long CountsSent { get; private set; }

    public void OnPostThink(float frameTime)
    {
        if (++_frame % PollFrames != 0)
            return;
        Poll();
    }

    /// <summary>One look at every hero and every lent body: what changed is told.</summary>
    public void Poll()
    {
        robots.Lent(_lent);
        foreach (var hero in heroes.All)
        {
            if (hero.PlayerIndex <= 0 || hero.Sheet is not { } sheet)
                continue;
            var told = TellerOf(hero.PlayerIndex);
            if (!ReferenceEquals(told.Bestiary, sheet.Bestiary))
            {
                told.Bestiary = sheet.Bestiary;
                told.Reveals.Clear();
                SendCounts(hero.PlayerIndex, sheet, told, resync: false);
            }
            Views(hero.PlayerIndex, sheet, told);
        }
    }

    public void OnSpawning(EntityHandle player, float now) { }

    /// <summary>A spawn resyncs: every count, and every lent body's view at the next look.</summary>
    public void OnSpawned(EntityHandle player, float now)
    {
        if (heroes.All.FirstOrDefault(h => h.Player == player) is not { PlayerIndex: > 0, Sheet: { } sheet } hero)
            return;
        var told = TellerOf(hero.PlayerIndex);
        told.Bestiary = sheet.Bestiary;
        told.Reveals.Clear();
        told.Views.Clear();
        SendCounts(hero.PlayerIndex, sheet, told, resync: true);
    }

    public void OnClientDisconnected(int entIndex) => _told.Remove(entIndex);

    /// <summary>A level's bodies are its own: nothing told of them carries to the next.</summary>
    public void OnLevelShutdown() => _told.Clear();

    private Told TellerOf(int player)
    {
        if (!_told.TryGetValue(player, out var told))
            _told[player] = told = new Told();
        return told;
    }

    private RevealBits RevealOf(Told told, HeroSheet sheet, string template)
    {
        if (told.Reveals.TryGetValue(template, out var bits))
            return bits;
        var r = rules.Bestiary.Reveal(sheet, template);
        bits = (r.Count ? RevealBits.Count : 0) | (r.Life ? RevealBits.Life : 0)
             | (r.Resistances ? RevealBits.Resistances : 0) | (r.Style ? RevealBits.Style : 0);
        told.Reveals[template] = bits;
        return bits;
    }

    private void Views(int player, HeroSheet sheet, Told told)
    {
        told.Seen.Clear();
        foreach (var robot in _lent)
        {
            told.Seen.Add(robot.Entity);
            var reveal = RevealOf(told, sheet, robot.Template);
            bool life = (reveal & RevealBits.Life) != 0;
            var key = new ViewKey(robot.Serial, robot.TemplateRow, reveal,
                                  life ? LifeNumber(robot.Life) : -1, life ? LifeNumber(robot.MaxLife) : -1);
            if (told.Views.TryGetValue(robot.Entity, out var last) && last == key)
                continue;
            told.Views[robot.Entity] = key;
            Send(player, Wire(sheet, robot));
        }

        // A body he was told of that is no longer lent: parked, or its robot gone from the roster.
        List<int>? gone = null;
        foreach (var entity in told.Views.Keys)
            if (!told.Seen.Contains(entity))
                (gone ??= []).Add(entity);
        if (gone is null)
            return;
        foreach (int entity in gone)
        {
            var last = told.Views[entity];
            told.Views.Remove(entity);
            Send(player, new RobotViewWire { Entity = (short)entity, Serial = last.Serial, Template = 0, Fingerprint = Fingerprint, Style = "" });
        }
    }

    /// <summary>A life as the bar's numbers show it: rounded up, as the body's engine health is.</summary>
    public static int LifeNumber(double life) => life <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(life));

    /// <summary>
    /// The view of <paramref name="robot"/> <paramref name="sheet"/>'s hero may have, on the wire:
    /// the rules' <see cref="BestiaryRules.View"/>, and for every part it hides nothing at all.
    /// </summary>
    public RobotViewWire Wire(HeroSheet sheet, in LentRobot robot)
    {
        var facts = new RobotFacts(robot.Template, LifeNumber(robot.Life), LifeNumber(robot.MaxLife), robot.Resist, robot.Immunity, robot.Style);
        RulesBestiaryView view = rules.Bestiary.View(sheet, facts);
        var content = rules.Content;
        var reveal = (view.Kills is not null ? RevealBits.Count : 0) | (view.Life is not null ? RevealBits.Life : 0)
                   | (view.Resistances is not null ? RevealBits.Resistances : 0) | (view.Style is not null ? RevealBits.Style : 0);

        int[] life = view.Life is { } l && view.MaxLife is { } m ? [l, m] : [];
        short[] resist = [];
        byte[] immune = [];
        byte[] affixes = [];
        if (view.Resistances is { } resists)
        {
            var types = content.DamageTypes.Rows;
            resist = new short[Math.Min(types.Count, RobotViewWire.MaxTypes)];
            for (int t = 0; t < resist.Length; t++)
                resist[t] = SheetNumbers.Short(resists.TryGetValue(types[t].Id, out double pct) ? pct : 0);
            if (view.Immunity is { } type && IndexOf(types.Select(r => r.Id), type) is >= 0 and var at)
                immune = [(byte)at];
            affixes = [.. robot.Affixes.Select(a => IndexOf(content.Champions.Rows.Select(r => r.Id), a))
                                       .Where(i => i is >= 0 and < 255).Take(RobotViewWire.MaxAffixes).Select(i => (byte)i)];
        }

        string style = view.Style ?? "";
        if (style.Length > RobotViewWire.MaxStyle)
            style = style[..RobotViewWire.MaxStyle];
        return new RobotViewWire
        {
            Entity = (short)robot.Entity,
            Serial = robot.Serial,
            Template = (byte)Math.Clamp(robot.TemplateRow + 1, 0, 255),
            Fingerprint = Fingerprint,
            Reveal = (byte)reveal,
            Life = life,
            Resist = resist,
            Immune = immune,
            Affixes = affixes,
            Style = style,
        };
    }

    private static int IndexOf(IEnumerable<string> ids, string id)
    {
        int i = 0;
        foreach (string x in ids)
        {
            if (x == id)
                return i;
            i++;
        }
        return -1;
    }

    private void Send(int player, RobotViewWire wire)
    {
        sender.Send(wire, player);
        ViewsSent++;
    }

    /// <summary>His counts that differ from those sent (all of them on a resync), in messages of <see cref="BestiaryCounts.MaxRows"/>.</summary>
    private void SendCounts(int player, HeroSheet sheet, Told told, bool resync)
    {
        var robotsTable = rules.Content.Robots.Rows;
        var rows = new List<(byte Template, int Kills)>();
        if (resync)
            told.Counts.Clear();
        for (int r = 0; r < robotsTable.Count && r < 255; r++)
        {
            string id = robotsTable[r].Id;
            int kills = BestiaryRules.Kills(sheet, id);
            int sent = told.Counts.GetValueOrDefault(id);
            if (kills == sent && !(resync && kills > 0))
                continue;
            told.Counts[id] = kills;
            rows.Add(((byte)r, kills));
        }
        if (rows.Count == 0 && !resync)
            return;

        int at = 0;
        bool first = true;
        do
        {
            var chunk = rows.Skip(at).Take(BestiaryCounts.MaxRows).ToList();
            at += chunk.Count;
            sender.Send(new BestiaryCounts
            {
                Fingerprint = Fingerprint,
                Phase = (byte)(resync && first ? BestiaryPhase.Resync : BestiaryPhase.Update),
                Templates = [.. chunk.Select(c => c.Template)],
                Kills = [.. chunk.Select(c => c.Kills)],
            }, player);
            CountsSent++;
            first = false;
        }
        while (at < rows.Count);
    }
}
