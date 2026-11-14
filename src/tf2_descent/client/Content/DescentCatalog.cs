using System.Globalization;
using System.Reflection;
using System.Text.Json;

using SourceSharp.Abi;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Content;

//=============================================================================
// WHAT THE CLIENT MAY READ OF THE RULES' CONTENT (plan_descent_logic UI3).
//
// The client may not reference TF2.Descent.Rules: Rules references
// Host.Contracts, which no client assembly may (ReferenceClosureTests), and
// Rules may reference nothing else, because the host loads it alone in its
// own process. So the client embeds the SAME bytes, the rules' own
// Content/*.json linked into this assembly (TF2.Descent.Client.csproj), and
// reads from them only what a screen displays: names, tiers, kinds, max
// ranks, mana costs and cooldowns as written, prerequisites, item bases'
// slots, families and requirements, affix names. Nothing here validates or
// decides: the rules validate the same bytes on the server, and every
// outcome (a buy, a spend, an equip, whether a requirement is met) is the
// server's, sent to the owner. The wire names rows by their index in these
// tables, checked by ContentFingerprint (a fact holds the reading to the
// rules' own, row by row).
//=============================================================================

/// <summary>A skill as the skill tree draws it.</summary>
/// <param name="Prerequisites">The skills a prerequisite names, flattened, in the table's order.</param>
/// <param name="ManaCost">The mana cost as written: one number, or "from-to" across the ranks; "" for none.</param>
/// <param name="Cooldown">The cooldown in seconds as written, the same way; "" for none.</param>
/// <param name="Charges">The skill charges while its key is held (a charge primitive in its effect).</param>
public sealed record CatalogSkill(
    string Id, string Name, string Class, string Tab, int Tier, string Kind, int MaxRank,
    string ManaCost, string Cooldown, IReadOnlyList<string> Prerequisites, bool Charges, string? Scope)
{
    /// <summary>The token of the skill's name (tools/descent-content-tokens writes it from the table).</summary>
    public string NameToken => DescentCatalog.SkillNameToken(Id);
}

/// <summary>A skill tab: a class's tree or the shared Mercenary tab.</summary>
public sealed record CatalogTab(string Id, string Class, string Name)
{
    public string TitleToken => DescentCatalog.SkillTabToken(Id);
}

/// <summary>An attribute requirement of an item base: <paramref name="Attribute"/> at least <paramref name="Min"/>.</summary>
public sealed record CatalogRequirement(string Attribute, int Min);

/// <summary>An item base as the inventory names it.</summary>
public sealed record CatalogBase(
    string Id, string Name, string Kind, string? Family, string Slot, IReadOnlyList<string> Classes, int Level,
    IReadOnlyList<CatalogRequirement> Requires, int Durability, int Stack)
{
    /// <summary>Weapons and armour: the kinds an equipment slot takes.</summary>
    public bool IsGear => Kind is "weapon" or "armour";
}

/// <summary>An affix: its name, the stat it rolls, whether TF2 implements it (a multiplier), whether it is a curse.</summary>
public sealed record CatalogAffix(string Id, string Name, string Stat, bool TF, bool Cursed);

/// <summary>"Each point gives <paramref name="Amount"/> of <paramref name="Stat"/>."</summary>
public sealed record CatalogPerPoint(string Stat, double Amount);

/// <summary>An attribute and what each point of it buys every class.</summary>
public sealed record CatalogAttribute(string Id, string Name, IReadOnlyList<CatalogPerPoint> PerPoint)
{
    /// <summary>The sheet's token for the attribute (<c>#Descent_Attr_Brawn</c>).</summary>
    public string NameToken => "#Descent_Attr_" + Name;
}

/// <summary>A row known by its id and name (rarities, families, damage types, unusual effects, uniques).</summary>
public sealed record CatalogNamed(string Id, string Name);

/// <summary>An equipment slot (inventory.json): its id and the base slot it accepts.</summary>
public sealed record CatalogEquipSlot(string Id, string Accepts);

/// <summary>
/// The display half of the rules' content, read from the embedded tables
/// (see the file's head). Read once, on first use.
/// </summary>
[SideService(Side.Client)]
public sealed class DescentCatalog
{
    /// <summary>The manifest name prefix of a table this assembly embeds (TF2.Descent.Client.csproj).</summary>
    public const string ResourcePrefix = "TF2.Descent.Client.Content.";

    /// <summary>The tables the client embeds: each is the rules' file of that name.</summary>
    public static IReadOnlyList<string> Tables =>
    [
        "skills", "skill_tabs", "skill_settings", "item_bases", "affixes", "attributes", "rarities",
        "families", "damage_types", "unusual_effects", "uniques", "inventory",
        "robots", "champions", "status",
    ];

    /// <summary>The token of a skill's name.</summary>
    public static string SkillNameToken(string id) => "#Descent_SkillName_" + id;

    /// <summary>The token of a skill tab's title.</summary>
    public static string SkillTabToken(string id) => "#Descent_SkillTab_" + id;

    /// <summary>The token of a stat's name (the sheet's hover lines).</summary>
    public static string StatToken(string id) => "#Descent_Stat_" + id;

    /// <summary>The item tooltip's line for an attribute requirement: "Requires Aim 30".</summary>
    public static string RequirementToken(string attribute, int min) =>
        "#Descent_Req_" + attribute + "_" + min.ToString(CultureInfo.InvariantCulture);

    /// <summary>The item tooltip's line for a level requirement: "Requires level 6".</summary>
    public static string LevelRequirementToken(int level) =>
        "#Descent_ReqLevel_" + level.ToString(CultureInfo.InvariantCulture);

    private readonly Assembly _assembly;
    private Loaded? _loaded;

    public DescentCatalog() : this(typeof(DescentCatalog).Assembly) { }

    private DescentCatalog(Assembly assembly) => _assembly = assembly;

    /// <summary>A catalog over the tables another assembly embeds under <see cref="ResourcePrefix"/> (a fact's planted copy).</summary>
    public static DescentCatalog From(Assembly assembly) => new(assembly);

    public IReadOnlyList<CatalogSkill> Skills => Load().Skills;
    public IReadOnlyList<CatalogTab> Tabs => Load().Tabs;
    public IReadOnlyList<int> TierLevels => Load().TierLevels;
    public string SharedClass => Load().SharedClass;
    public IReadOnlyList<CatalogBase> Bases => Load().Bases;
    public IReadOnlyList<CatalogAffix> Affixes => Load().Affixes;
    public IReadOnlyList<CatalogAttribute> Attributes => Load().Attributes;
    public IReadOnlyList<CatalogNamed> Rarities => Load().Rarities;
    public IReadOnlyList<CatalogNamed> Families => Load().Families;
    public IReadOnlyList<CatalogNamed> DamageTypes => Load().DamageTypes;
    public IReadOnlyList<CatalogNamed> UnusualEffects => Load().Effects;
    public IReadOnlyList<CatalogNamed> Uniques => Load().Uniques;
    public IReadOnlyList<CatalogEquipSlot> EquipSlots => Load().EquipSlots;

    /// <summary>The robot templates (robots.json): what a health bar names a robot by its net state's row (UI6).</summary>
    public IReadOnlyList<CatalogNamed> Robots => Load().Robots;

    /// <summary>The champion affixes (champions.json), by the rows a robot view names.</summary>
    public IReadOnlyList<CatalogNamed> Champions => Load().Champions;

    /// <summary>The robot status effects (status.json): bit k of a robot's statuses is row k.</summary>
    public IReadOnlyList<string> Statuses => Load().Statuses;

    /// <summary>
    /// <see cref="RobotContentKeys"/> over robots, champions, damage types and statuses: what the
    /// bestiary's messages are checked against.
    /// </summary>
    public uint RobotFingerprint => Load().RobotFingerprint;

    /// <summary>The backpack's base capacity (inventory.json), before skill ranks raise it.</summary>
    public int BackpackBase => Load().BackpackBase;

    /// <summary><see cref="ContentFingerprint"/> over these tables' ids: what the server's messages are checked against.</summary>
    public uint Fingerprint => Load().Fingerprint;

    /// <summary>The bytes of an embedded table, or null when this assembly does not embed it.</summary>
    public byte[]? Bytes(string table)
    {
        using var stream = _assembly.GetManifestResourceStream(ResourcePrefix + table + ".json");
        if (stream is null)
            return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The skill of an id, or null.</summary>
    public CatalogSkill? Skill(string id) => Load().SkillById.GetValueOrDefault(id);

    /// <summary>The index of a skill in the table, or -1.</summary>
    public int SkillIndex(string id) => Load().SkillIndex.GetValueOrDefault(id, -1);

    /// <summary>The row at <paramref name="index"/> of <paramref name="rows"/>, or null outside it.</summary>
    public static T? At<T>(IReadOnlyList<T> rows, int index) where T : class =>
        index >= 0 && index < rows.Count ? rows[index] : null;

    private Loaded Load() => _loaded ??= Read();

    private sealed record Loaded(
        IReadOnlyList<CatalogSkill> Skills, IReadOnlyDictionary<string, CatalogSkill> SkillById,
        IReadOnlyDictionary<string, int> SkillIndex, IReadOnlyList<CatalogTab> Tabs,
        IReadOnlyList<int> TierLevels, string SharedClass, IReadOnlyList<CatalogBase> Bases,
        IReadOnlyList<CatalogAffix> Affixes, IReadOnlyList<CatalogAttribute> Attributes,
        IReadOnlyList<CatalogNamed> Rarities, IReadOnlyList<CatalogNamed> Families,
        IReadOnlyList<CatalogNamed> DamageTypes, IReadOnlyList<CatalogNamed> Effects,
        IReadOnlyList<CatalogNamed> Uniques, IReadOnlyList<CatalogEquipSlot> EquipSlots, int BackpackBase,
        uint Fingerprint, IReadOnlyList<CatalogNamed> Robots, IReadOnlyList<CatalogNamed> Champions,
        IReadOnlyList<string> Statuses, uint RobotFingerprint);

    private Loaded Read()
    {
        var skills = Rows("skills", ReadSkill);
        var tabs = Rows("skill_tabs", e => new CatalogTab(Str(e, "id"), Str(e, "class"), Str(e, "name")));
        var bases = Rows("item_bases", ReadBase);
        var affixes = Rows("affixes", e => new CatalogAffix(Str(e, "id"), Str(e, "name"), Str(e, "stat"),
                                                           Str(e, "kind") == "tf", Str(e, "pool") == "cursed"));
        var attributes = Rows("attributes", e => new CatalogAttribute(Str(e, "id"), Str(e, "name"),
            [.. Array(e, "perPoint").Select(p => new CatalogPerPoint(Str(p, "stat"), Num(p, "amount")))]));
        var rarities = Rows("rarities", Named);
        var families = Rows("families", Named);
        var damageTypes = Rows("damage_types", Named);
        var effects = Rows("unusual_effects", Named);
        var uniques = Rows("uniques", Named);
        var robots = Rows("robots", Named);
        var champions = Rows("champions", Named);
        var statuses = Rows("status", e => Str(e, "id"));

        using var settings = Document("skill_settings");
        var tierLevels = settings.RootElement.TryGetProperty("tierLevels", out var levels)
            ? [.. levels.EnumerateArray().Select(l => l.GetInt32())]
            : new List<int>();
        string shared = settings.RootElement.TryGetProperty("sharedClass", out var sc) ? sc.GetString() ?? "" : "";

        using var inventory = Document("inventory");
        var slots = inventory.RootElement.TryGetProperty("equipment", out var eq)
            ? [.. eq.EnumerateArray().Select(s => new CatalogEquipSlot(Str(s, "id"), Str(s, "accepts")))]
            : new List<CatalogEquipSlot>();
        int backpack = inventory.RootElement.TryGetProperty("backpack", out var bp) && bp.TryGetProperty("base", out var b)
            ? b.GetInt32() : 0;

        var byId = new Dictionary<string, CatalogSkill>(StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < skills.Count; i++)
        {
            byId[skills[i].Id] = skills[i];
            index[skills[i].Id] = i;
        }

        uint fingerprint = ContentFingerprint.Of(
            bases.Select(x => x.Id), affixes.Select(x => x.Id), skills.Select(x => x.Id),
            effects.Select(x => x.Id), uniques.Select(x => x.Id), attributes.Select(x => x.Id),
            slots.Select(x => x.Id));

        uint robotFingerprint = RobotContentKeys.Fingerprint(
            robots.Select(x => x.Id), champions.Select(x => x.Id), damageTypes.Select(x => x.Id), statuses);

        return new Loaded(skills, byId, index, tabs, tierLevels, shared, bases, affixes, attributes, rarities, families,
                          damageTypes, effects, uniques, slots, backpack, fingerprint, robots, champions, statuses,
                          robotFingerprint);
    }

    private static CatalogNamed Named(JsonElement e) => new(Str(e, "id"), Str(e, "name"));

    private static CatalogSkill ReadSkill(JsonElement e)
    {
        var prerequisites = new List<string>();
        foreach (var p in Array(e, "prerequisites"))
        {
            if (p.TryGetProperty("skill", out var one) && one.GetString() is { } id)
                prerequisites.Add(id);
            foreach (var any in Array(p, "anyOf"))
                if (any.GetString() is { } a)
                    prerequisites.Add(a);
        }
        bool charges = Array(e, "effect").Any(x => Str(x, "primitive") == "charge");
        return new CatalogSkill(Str(e, "id"), Str(e, "name"), Str(e, "class"), Str(e, "tab"),
                                e.TryGetProperty("tier", out var tier) ? tier.GetInt32() : 0,
                                Str(e, "kind"), e.TryGetProperty("maxRank", out var max) ? max.GetInt32() : 1,
                                Curve(e, "manaCost"), Curve(e, "cooldown"), prerequisites, charges,
                                e.TryGetProperty("scope", out var scope) ? scope.GetString() : null);
    }

    private static CatalogBase ReadBase(JsonElement e) => new(
        Str(e, "id"), Str(e, "name"), Str(e, "kind"),
        e.TryGetProperty("family", out var family) ? family.GetString() : null,
        Str(e, "slot"),
        [.. Array(e, "classes").Select(c => c.GetString() ?? "")],
        e.TryGetProperty("level", out var level) ? level.GetInt32() : 0,
        [.. Array(e, "requires").Select(r => new CatalogRequirement(Str(r, "attribute"), r.GetProperty("min").GetInt32()))],
        e.TryGetProperty("durability", out var dur) ? dur.GetInt32() : 0,
        e.TryGetProperty("stack", out var stack) ? stack.GetInt32() : 1);

    /// <summary>A rank curve as written: "4", "10-6", or "" for a zero or missing one.</summary>
    private static string Curve(JsonElement e, string field)
    {
        if (!e.TryGetProperty(field, out var v))
            return "";
        var c = CultureInfo.InvariantCulture;
        if (v.ValueKind == JsonValueKind.Number)
            return v.GetDouble() == 0 ? "" : v.GetDouble().ToString("0.##", c);
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("from", out var from) && v.TryGetProperty("to", out var to))
            return $"{from.GetDouble().ToString("0.##", c)}-{to.GetDouble().ToString("0.##", c)}";
        return "";
    }

    private List<T> Rows<T>(string table, Func<JsonElement, T> read)
    {
        using var doc = Document(table);
        return [.. doc.RootElement.EnumerateArray().Select(read)];
    }

    private JsonDocument Document(string table) =>
        JsonDocument.Parse(Bytes(table) ?? throw new InvalidOperationException($"the client embeds no content table '{table}'"),
                           new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    private static string Str(JsonElement e, string field) =>
        e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string field) =>
        e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static IEnumerable<JsonElement> Array(JsonElement e, string field) =>
        e.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
}
