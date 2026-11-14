using System.Collections.Immutable;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Hero;

/// <summary>Why a spend was refused.</summary>
public enum SpendRefusal
{
    NoPoints,
    AtCap,
    UnknownAttribute,
    BadCount,
}

/// <summary>A spend: the sheet after it, the points spent, and the part of the request not spent.</summary>
public sealed record SpendResult(HeroSheet Sheet, int Spent, int Remainder, SpendRefusal? Refusal)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// Experience gained: the sheet after it, levels gained, points granted, and whether life
/// and mana refill to full (ruling L3: they do on any level-up).
/// </summary>
public sealed record LevelUpResult(HeroSheet Sheet, int LevelsGained, int AttributePointsGranted, int SkillPointsGranted,
    bool RefillsLifeAndMana, long XpDiscarded);

/// <summary>A respec: the sheet after it and the Australium it cost.</summary>
public sealed record RespecResult(HeroSheet Sheet, long Price);

/// <summary>An audit of one sheet: Ok, or the first rule it breaks.</summary>
public sealed record AuditResult(bool Ok, string? Rule = null, string? Detail = null)
{
    public static AuditResult Pass { get; } = new(true);
    public static AuditResult Fail(string rule, string detail) => new(false, rule, detail);
}

/// <summary>
/// The hero sheet's rules (plan_tf2_rpg 2a, Appendix C; plan_descent_logic RU2), pure
/// over the content: the XP curve, levelling, spending, respec, the audit identity and
/// the derived stats. Every number is the content's.
/// </summary>
public sealed class HeroRules
{
    private readonly DescentContent _content;
    private readonly long[] _xpAt;   // _xpAt[L] = total experience at which level L is reached

    public HeroRules(DescentContent content)
    {
        _content = content;
        _xpAt = new long[content.Hero.LevelCap + 1];
        for (int level = 2; level <= content.Hero.LevelCap; level++)
            _xpAt[level] = checked(_xpAt[level - 1] + XpToNext(level - 1));
    }

    private HeroSettings S => _content.Hero;

    public int LevelCap => S.LevelCap;

    //-------------------------------------------------------------------------
    // Classes
    //-------------------------------------------------------------------------

    /// <summary>A class by id, ignoring case ("Soldier" is "soldier"); null when there is none.</summary>
    public ClassRow? FindClass(string id) =>
        _content.Classes.Rows.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public ClassRow Class(string id) =>
        FindClass(id) ?? throw new ArgumentException(
            $"'{id}' is not a hero class ({string.Join(", ", _content.Classes.Rows.Select(c => c.Id))})", nameof(id));

    /// <summary>A level 1 hero of the class: every attribute at its class start, nothing to spend.</summary>
    public HeroSheet New(string classId, string name)
    {
        var cls = Class(classId);
        return new HeroSheet
        {
            ClassId = cls.Id,
            Name = name,
            Attributes = Starts(cls),
        };
    }

    /// <summary>Every attribute of the class at its start.</summary>
    public ImmutableSortedDictionary<string, int> Starts(ClassRow cls) =>
        cls.Attributes.ToImmutableSortedDictionary(b => b.Attribute, b => b.Start, StringComparer.Ordinal);

    //-------------------------------------------------------------------------
    // Experience and levels
    //-------------------------------------------------------------------------

    /// <summary>Experience from level L to L + 1: round(scale · L^exponent), 100 · L^2.2 as shipped.</summary>
    public long XpToNext(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        return (long)Math.Round(S.XpScale * Math.Pow(level, S.XpExponent), MidpointRounding.AwayFromZero);
    }

    /// <summary>The total experience at which <paramref name="level"/> is reached (0 for level 1).</summary>
    public long XpAtLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, S.LevelCap);
        return _xpAt[level];
    }

    /// <summary>The attribute points a hero of <paramref name="level"/> has earned: points-per-level · (level − 1).</summary>
    public int AttributePointsFor(int level) => S.AttributePointsPerLevel * (level - 1);

    /// <summary>The skill points a hero of <paramref name="level"/> has earned.</summary>
    public int SkillPointsFor(int level) => S.SkillPointsPerLevel * (level - 1);

    /// <summary>The most experience a hero can hold: what reaching the level cap takes.</summary>
    public long XpCap => _xpAt[S.LevelCap];

    /// <summary>The level a total of <paramref name="xp"/> reaches.</summary>
    public int LevelForXp(long xp)
    {
        int level = 1;
        while (level < S.LevelCap && _xpAt[level + 1] <= xp)
            level++;
        return level;
    }

    /// <summary>
    /// Adds experience. Each level gained grants the content's attribute and skill points
    /// and refills life and mana (L3). Past the cap's threshold experience is discarded.
    /// </summary>
    public LevelUpResult GainXp(HeroSheet sheet, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        long total = Math.Min(XpCap, sheet.Xp + Math.Min(amount, XpCap));
        long discarded = amount - (total - sheet.Xp);
        int level = Math.Max(sheet.Level, LevelForXp(total));
        int gained = level - sheet.Level;
        int attr = gained * S.AttributePointsPerLevel;
        int skill = gained * S.SkillPointsPerLevel;
        var after = sheet with
        {
            Xp = total,
            Level = level,
            AttributePoints = sheet.AttributePoints + attr,
            SkillPoints = sheet.SkillPoints + skill,
        };
        return new LevelUpResult(after, gained, attr, skill, gained > 0 && S.LevelUpRefill, discarded);
    }

    /// <summary>Life and mana after experience: full after a level-up that refills (L3), else unchanged but never above the new maximum.</summary>
    public static Vitals AfterGain(LevelUpResult gain, Vitals current, HeroStats statsAfter) =>
        gain.RefillsLifeAndMana ? statsAfter.Full
        : new Vitals(Math.Min(current.Life, statsAfter.MaxLife), Math.Min(current.Mana, statsAfter.MaxMana));

    //-------------------------------------------------------------------------
    // Spending and respec
    //-------------------------------------------------------------------------

    /// <summary>
    /// Spends up to <paramref name="count"/> points into an attribute (one click is 1, a
    /// shift-click 5). Never overshoots the class cap or the points held; what is not spent
    /// comes back as <see cref="SpendResult.Remainder"/>. Refused when nothing can be spent.
    /// </summary>
    public SpendResult Spend(HeroSheet sheet, string attribute, int count)
    {
        if (count < 1) return new SpendResult(sheet, 0, Math.Max(0, count), SpendRefusal.BadCount);
        var cls = Class(sheet.ClassId);
        var bounds = cls.Attributes.FirstOrDefault(b => b.Attribute == attribute);
        if (bounds is null) return new SpendResult(sheet, 0, count, SpendRefusal.UnknownAttribute);
        int now = sheet.Base(attribute);
        if (sheet.AttributePoints <= 0) return new SpendResult(sheet, 0, count, SpendRefusal.NoPoints);
        if (now >= bounds.Cap) return new SpendResult(sheet, 0, count, SpendRefusal.AtCap);
        int spent = Math.Min(count, Math.Min(sheet.AttributePoints, bounds.Cap - now));
        var after = sheet with
        {
            Attributes = sheet.Attributes.SetItem(attribute, now + spent),
            AttributePoints = sheet.AttributePoints - spent,
        };
        return new SpendResult(after, spent, count - spent, null);
    }

    /// <summary>The Australium the hero's next respec costs: price · growth^respecs, rising each time.</summary>
    public long RespecPrice(HeroSheet sheet)
    {
        double price = S.RespecPrice * Math.Pow(S.RespecGrowth, sheet.RespecCount);
        return price >= long.MaxValue ? long.MaxValue : (long)Math.Round(price, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Every invested point back (C.3): each attribute to its class start, every skill
    /// rank cleared, all points earned by level unspent. The count rises, and so does the
    /// next price.
    /// </summary>
    public RespecResult Respec(HeroSheet sheet)
    {
        var cls = Class(sheet.ClassId);
        var after = sheet with
        {
            Attributes = Starts(cls),
            AttributePoints = AttributePointsFor(sheet.Level),
            SkillRanks = sheet.SkillRanks.Clear(),
            SkillPoints = SkillPointsFor(sheet.Level),
            RespecCount = sheet.RespecCount + 1,
        };
        return new RespecResult(after, RespecPrice(sheet));
    }

    //-------------------------------------------------------------------------
    // The audit (C.3): what the service checks of every sheet
    //-------------------------------------------------------------------------

    /// <summary>
    /// Ok when the sheet could have been reached by play: a known class, a level within
    /// the cap that its experience reaches, every attribute between start and cap,
    /// Σ(base − start) + unspent = points-per-level · (level − 1), and the same for skill
    /// points and ranks. Else the first rule broken, by name.
    /// </summary>
    public AuditResult Audit(HeroSheet sheet)
    {
        if (FindClass(sheet.ClassId) is not { } cls || cls.Id != sheet.ClassId)
            return AuditResult.Fail("unknown_class", $"'{sheet.ClassId}' is not a hero class");
        if (sheet.Level < 1 || sheet.Level > S.LevelCap)
            return AuditResult.Fail("level_out_of_range", $"level {sheet.Level} is outside 1..{S.LevelCap}");
        if (sheet.Xp < 0 || sheet.Xp > XpCap)
            return AuditResult.Fail("xp_out_of_range", $"xp {sheet.Xp} is outside 0..{XpCap}");
        if (LevelForXp(sheet.Xp) != sheet.Level)
            return AuditResult.Fail("xp_level_mismatch", $"xp {sheet.Xp} reaches level {LevelForXp(sheet.Xp)}, not {sheet.Level}");
        if (sheet.AttributePoints < 0 || sheet.SkillPoints < 0)
            return AuditResult.Fail("negative_points", "unspent points below zero");

        if (sheet.Attributes.Keys.FirstOrDefault(k => cls.Attributes.All(b => b.Attribute != k)) is { } unknown)
            return AuditResult.Fail("attribute_unknown", $"'{unknown}' is not an attribute of {cls.Id}");
        int invested = 0;
        foreach (var b in cls.Attributes)
        {
            if (!sheet.Attributes.TryGetValue(b.Attribute, out var value))
                return AuditResult.Fail("attribute_missing", $"no base for '{b.Attribute}'");
            if (value < b.Start)
                return AuditResult.Fail("attribute_below_start", $"{b.Attribute} {value} is below its start {b.Start}");
            if (value > b.Cap)
                return AuditResult.Fail("attribute_over_cap", $"{b.Attribute} {value} is above its cap {b.Cap}");
            invested += value - b.Start;
        }
        long earned = (long)S.AttributePointsPerLevel * (sheet.Level - 1);
        if (invested + sheet.AttributePoints != earned)
            return AuditResult.Fail("attribute_points",
                $"{invested} invested + {sheet.AttributePoints} unspent is not the {earned} earned by level {sheet.Level}");

        if (sheet.SkillRanks.Values.Any(r => r < 0))
            return AuditResult.Fail("negative_rank", "a skill rank below zero");
        long ranks = sheet.SkillRanks.Values.Sum(r => (long)r);
        long skillEarned = (long)S.SkillPointsPerLevel * (sheet.Level - 1);
        if (ranks + sheet.SkillPoints != skillEarned)
            return AuditResult.Fail("skill_points",
                $"{ranks} ranks + {sheet.SkillPoints} unspent is not the {skillEarned} earned by level {sheet.Level}");
        return AuditResult.Pass;
    }

    //-------------------------------------------------------------------------
    // Derived stats
    //-------------------------------------------------------------------------

    /// <summary>
    /// The hero's numbers given what contributes to Now. A contribution counts only when
    /// its source is active (<see cref="ContributionSource.Counts"/>): an inactive or
    /// broken item's count for nothing, through this one test.
    /// </summary>
    public HeroStats Stats(HeroSheet sheet, IReadOnlyList<Contribution>? contributions = null)
    {
        var cls = Class(sheet.ClassId);
        var counting = (contributions ?? []).Where(c => c.Source.Counts).ToList();
        var attributes = _content.Attributes;

        var baseValues = new Dictionary<string, int>(StringComparer.Ordinal);
        var now = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in attributes.Rows)
        {
            baseValues[a.Id] = sheet.Base(a.Id);
            now[a.Id] = sheet.Base(a.Id);
        }

        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        void Add(string stat, double amount) => totals[stat] = (totals.TryGetValue(stat, out var v) ? v : 0) + amount;

        foreach (var c in counting)
        {
            if (now.ContainsKey(c.Stat)) now[c.Stat] += c.Amount;
            else Add(c.Stat, c.Amount);
        }
        foreach (var s in cls.Base) Add(s.Stat, s.Amount);
        foreach (var s in cls.PerLevel) Add(s.Stat, s.Amount * sheet.Level);
        foreach (var a in attributes.Rows)
            foreach (var p in a.PerPoint)
                Add(p.Stat, p.Amount * now[a.Id]);
        foreach (var p in cls.PerPoint)
            Add(p.Stat, p.Amount * (now.TryGetValue(p.Attribute, out var v) ? v : 0));
        foreach (var (id, value) in now)
            totals[id] = value;   // an attribute's total is its Now

        return new HeroStats(S, baseValues, now, totals);
    }

    /// <summary>
    /// The fraction of a landed hit that armour removes (C.2):
    /// armour / (armour + constant + perLevel · attackerLevel). Diminishing, never total,
    /// worth less against a deeper attacker; no armour (or less) removes nothing.
    /// </summary>
    public double ArmourReduction(double armour, int attackerLevel)
    {
        if (armour <= 0) return 0;
        return armour / (armour + S.ArmourConstant + S.ArmourPerAttackerLevel * Math.Max(0, attackerLevel));
    }
}
