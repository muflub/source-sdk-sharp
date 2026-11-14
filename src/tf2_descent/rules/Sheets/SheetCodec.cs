using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Sheets;

/// <summary>
/// The character sheet's bytes (plan_descent_logic RU13): the whole hero, schema 3.
/// <list type="bullet">
/// <item>Schema 3 is canonical JSON: fields in a fixed order, maps sorted by key, no
/// whitespace, so encoding a decoded sheet gives the same bytes (the host's admin edit
/// checks that round trip). It is schema 2 and the skill bar ("slots").</item>
/// <item>Schema 2 (no bar) still decodes, with every bar slot empty.</item>
/// <item>Schema 1 (seven fields: class, name, level, xp, reached depth, hardcore, fallen)
/// still decodes, into a hero at its class starts with every point its level
/// earned unspent, and its experience raised to at least what its level takes.</item>
/// </list>
/// Hand-written over <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/>: a field
/// nobody reads, a missing one or one of the wrong kind is <see cref="InvalidDataException"/>.
/// </summary>
public sealed class SheetCodec : ISheetCodec
{
    public const int Schema = 3;
    public const int Schema1 = 1;
    public const int Schema2 = 2;

    private readonly HeroRules _heroes;

    public SheetCodec(HeroRules heroes) => _heroes = heroes;

    public int SchemaVersion => Schema;

    public CharacterSheet Write(HeroSheet sheet) => new(Encode(sheet), Schema);

    /// <summary>The hero in a sheet of either schema.</summary>
    public HeroSheet Read(CharacterSheet sheet) => sheet.SchemaVersion switch
    {
        Schema => Decode(sheet.Data, withBar: true),
        Schema2 => Decode(sheet.Data, withBar: false),
        Schema1 => UpgradeV1(sheet.Data),
        _ => throw new InvalidDataException($"sheet schema {sheet.SchemaVersion} is not 1, 2 or 3"),
    };

    public SheetSummary Summarize(CharacterSheet sheet)
    {
        var s = Read(sheet);
        return new SheetSummary(s.ClassId, s.Name, s.Level, s.Xp, s.ReachedDepth, s.Hardcore, s.Fallen);
    }

    /// <summary>The sheet as schema 3 JSON, whatever schema it was written in.</summary>
    public string ToJson(CharacterSheet sheet) => Encoding.UTF8.GetString(Encode(Read(sheet)));

    /// <summary>
    /// A schema 3 sheet from JSON of schema 3, 2 (no "slots" field) or 1 (told apart by its
    /// "ClassName" field).
    /// </summary>
    public CharacterSheet FromJson(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var doc = Parse(bytes);
        bool isObject = doc.RootElement.ValueKind == JsonValueKind.Object;
        if (isObject && doc.RootElement.TryGetProperty("ClassName", out _))
            return Write(UpgradeV1(bytes));
        return Write(Decode(bytes, withBar: !isObject || doc.RootElement.TryGetProperty("slots", out _)));
    }

    //-------------------------------------------------------------------------
    // Schema 3 (schema 2 and the bar)
    //-------------------------------------------------------------------------

    public static byte[] Encode(HeroSheet s)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("class", s.ClassId);
            w.WriteString("name", s.Name);
            w.WriteNumber("level", s.Level);
            w.WriteNumber("xp", s.Xp);
            w.WriteNumber("reachedDepth", s.ReachedDepth);
            w.WriteBoolean("hardcore", s.Hardcore);
            w.WriteBoolean("fallen", s.Fallen);
            WriteMap(w, "attributes", s.Attributes);
            w.WriteNumber("attributePoints", s.AttributePoints);
            w.WriteNumber("skillPoints", s.SkillPoints);
            WriteMap(w, "skills", s.SkillRanks);
            WriteMap(w, "spells", s.SpellRanks);
            WriteMap(w, "bestiary", s.Bestiary);
            w.WriteStartArray("belt");
            foreach (var id in s.Belt) w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteNumber("respecs", s.RespecCount);
            w.WriteStartArray("bossKills");
            foreach (var id in s.BossKills) w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteStartArray("slots");
            foreach (var id in s.SkillSlots) w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteMap(Utf8JsonWriter w, string name, ImmutableSortedDictionary<string, int> map)
    {
        w.WriteStartObject(name);
        foreach (var (k, v) in map) w.WriteNumber(k, v);
        w.WriteEndObject();
    }

    /// <summary>A schema 3 sheet, or with <paramref name="withBar"/> false a schema 2 one, whose bar is empty.</summary>
    private HeroSheet Decode(byte[] data, bool withBar)
    {
        using var doc = Parse(data);
        var errors = new ContentErrors();
        var f = new JsonFields(doc.RootElement, "sheet", null, errors);
        var sheet = new HeroSheet
        {
            ClassId = f.Str("class"),
            Name = f.Str("name"),
            Level = f.Int("level"),
            Xp = f.Long("xp"),
            ReachedDepth = f.Int("reachedDepth"),
            Hardcore = f.Bool("hardcore"),
            Fallen = f.Bool("fallen"),
            Attributes = Map(f, "attributes"),
            AttributePoints = f.Int("attributePoints"),
            SkillPoints = f.Int("skillPoints"),
            SkillRanks = Map(f, "skills"),
            SpellRanks = Map(f, "spells"),
            Bestiary = Map(f, "bestiary"),
            Belt = [.. Strings(f, "belt")],
            RespecCount = f.Int("respecs"),
            BossKills = [.. Strings(f, "bossKills")],
            SkillSlots = withBar ? [.. Strings(f, "slots")] : _heroes.EmptyBar(),
        };
        f.End();
        Throw(errors);
        return sheet;
    }

    private static ImmutableSortedDictionary<string, int> Map(JsonFields f, string field)
    {
        var map = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in f.Map(field))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n))
                f.Errors.Add(f.Table, key, ContentRule.Malformed, $"'{field}.{key}' must be a whole number");
            else if (!map.TryAdd(key, n))
                f.Errors.Add(f.Table, key, ContentRule.DuplicateId, $"'{field}' names '{key}' twice");
        }
        return map.ToImmutable();
    }

    private static IEnumerable<string> Strings(JsonFields f, string field)
    {
        foreach (var e in f.Array(field))
        {
            if (e.ValueKind == JsonValueKind.String) yield return e.GetString()!;
            else f.Errors.Add(f.Table, null, ContentRule.Malformed, $"'{field}' holds a {e.ValueKind}, not a string");
        }
    }

    //-------------------------------------------------------------------------
    // Schema 1
    //-------------------------------------------------------------------------

    /// <summary>
    /// A schema 1 sheet as a schema 3 hero (its bar empty): its class's starts, zero points spent (so all
    /// 5 · (level − 1) unspent, and level − 1 skill points), and its experience raised to
    /// the least its level takes, since schema 1 never kept experience in step with level.
    /// </summary>
    private HeroSheet UpgradeV1(byte[] data)
    {
        using var doc = Parse(data);
        var errors = new ContentErrors();
        var f = new JsonFields(doc.RootElement, "sheet v1", null, errors);
        string className = f.Str("ClassName");
        string name = f.Str("Name");
        int level = f.Int("Level");
        long xp = f.Long("Xp");
        int depth = f.Int("ReachedDepth");
        bool hardcore = f.Bool("Hardcore");
        bool fallen = f.Bool("Fallen");
        f.End();
        Throw(errors);

        var cls = _heroes.FindClass(className)
                  ?? throw new InvalidDataException($"schema 1 sheet of '{className}', which is not a hero class");
        var fresh = _heroes.New(cls.Id, name);
        int capped = Math.Clamp(level, 1, _heroes.LevelCap);
        return fresh with
        {
            Level = level,
            Xp = Math.Max(xp, _heroes.XpAtLevel(capped)),
            ReachedDepth = depth,
            Hardcore = hardcore,
            Fallen = fallen,
            AttributePoints = _heroes.AttributePointsFor(level),
            SkillPoints = _heroes.SkillPointsFor(level),
        };
    }

    //-------------------------------------------------------------------------

    private static JsonDocument Parse(byte[] data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("the sheet is not JSON: " + e.Message, e);
        }
    }

    private static void Throw(ContentErrors errors)
    {
        if (errors.Count > 0)
            throw new InvalidDataException("the sheet does not decode: " + string.Join("; ", errors.All));
    }
}
