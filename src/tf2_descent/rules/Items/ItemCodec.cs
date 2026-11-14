using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Items;

/// <summary>
/// The item's bytes (<see cref="RolledItem.Instance"/>), hand-written (plan_descent_logic RU3):
/// <list type="bullet">
/// <item>Schema 2 is canonical JSON of an <see cref="ItemInstance"/>: fields in a fixed order,
/// an absent optional field left out, no whitespace, the seeds as decimal strings. Encoding a
/// decoded instance gives the same bytes.</item>
/// <item>Schema 1 is the bytes the first rules wrote (reflection JSON of seed, base type,
/// rarity, item level, count, identified, tier and context). It still decodes, and replays by
/// the formula that rolled it (<see cref="LegacyItems"/>).</item>
/// </list>
/// A field nobody reads, a missing one or one of the wrong kind is <see cref="InvalidDataException"/>.
/// </summary>
public sealed class ItemCodec : IItemCodec
{
    public const int Schema = 2;
    public const int Schema1 = 1;

    public int SchemaVersion => Schema;

    /// <summary>The <see cref="RolledItem"/> that carries <paramref name="item"/>: its columns and its schema 2 bytes.</summary>
    public RolledItem ToRolled(ItemInstance item) =>
        new(item.Seed, item.BaseId, item.Rarity, item.ItemLevel, item.Count, item.Identified, Encode(item), Schema, item.Tier, item.Context);

    /// <summary>The instance a schema 2 item carries; <see cref="InvalidDataException"/> for any other schema or bad bytes.</summary>
    public ItemInstance Instance(RolledItem item) =>
        item.SchemaVersion == Schema
            ? DecodeInstance(item.Instance)
            : throw new InvalidDataException($"item schema {item.SchemaVersion} carries no instance of schema {Schema}");

    public RolledItem Decode(byte[] instance, int schemaVersion) => schemaVersion switch
    {
        Schema => ToRolled(DecodeInstance(instance)) with { Instance = instance },
        Schema1 => LegacyItems.Decode(instance),
        _ => throw new InvalidDataException($"item schema {schemaVersion} is not 1 or 2"),
    };

    public string ToJson(RolledItem item) => item.SchemaVersion switch
    {
        Schema => Encoding.UTF8.GetString(Encode(DecodeInstance(item.Instance))),
        Schema1 => Encoding.UTF8.GetString(item.Instance),
        _ => throw new InvalidDataException($"item schema {item.SchemaVersion} is not 1 or 2"),
    };

    /// <summary>An item from JSON of schema 2, or of schema 1 (told apart by its "BaseType" field).</summary>
    public RolledItem FromJson(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var doc = Parse(bytes);
        bool v1 = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("BaseType", out _);
        return v1 ? Decode(bytes, Schema1) : ToRolled(DecodeInstance(bytes));
    }

    //-------------------------------------------------------------------------
    // Schema 2
    //-------------------------------------------------------------------------

    private static string U(ulong n) => n.ToString(CultureInfo.InvariantCulture);

    public static byte[] Encode(ItemInstance i)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("seed", U(i.Seed));
            w.WriteStartObject("context");
            w.WriteString("class", i.Context.CharacterClass);
            w.WriteNumber("level", i.Context.CharacterLevel);
            w.WriteNumber("depth", i.Context.Depth);
            w.WriteNumber("itemLevel", i.Context.ItemLevel);
            w.WriteNumber("tier", i.Context.Tier);
            w.WriteString("lootTable", i.Context.LootTable);
            w.WriteString("source", i.Context.Source);
            w.WriteEndObject();
            w.WriteString("base", i.BaseId);
            w.WriteNumber("itemLevel", i.ItemLevel);
            w.WriteNumber("rarity", i.Rarity);
            w.WriteNumber("tier", i.Tier);
            if (i.Unique is { } unique) w.WriteString("unique", unique);
            w.WriteStartArray("affixes");
            foreach (var a in i.Affixes)
            {
                w.WriteStartObject();
                w.WriteString("id", a.Id);
                w.WriteNumber("roll", a.Roll);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (i.UnusualEffect is { } effect) w.WriteString("effect", effect);
            w.WriteBoolean("identified", i.Identified);
            w.WriteNumber("durability", i.Durability);
            w.WriteNumber("maxDurability", i.MaxDurability);
            w.WriteNumber("wear", i.WearUses);
            w.WriteNumber("rerolls", i.RerollCount);
            w.WriteNumber("count", i.Count);
            w.WriteStartArray("history");
            foreach (var e in i.History)
            {
                w.WriteStartObject();
                w.WriteString("op", e.Op);
                w.WriteNumber("index", e.Index);
                w.WriteString("seed", U(e.Seed));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static ItemInstance DecodeInstance(byte[] data)
    {
        using var doc = Parse(data);
        var errors = new ContentErrors();
        var f = new JsonFields(doc.RootElement, "item", null, errors);
        ulong seed = Seed(f, "seed");
        var c = f.Obj("context");
        var context = new ItemRollContext(c.Str("class"), c.Int("level"), c.Int("depth"), c.Int("itemLevel"), c.Int("tier"),
            c.Str("lootTable"), c.Str("source"));
        c.End();
        var item = new ItemInstance
        {
            Seed = seed,
            Context = context,
            BaseId = f.Str("base"),
            ItemLevel = f.Int("itemLevel"),
            Rarity = f.Int("rarity"),
            Tier = f.Int("tier"),
            Unique = f.StrOr("unique", null),
            Affixes = [.. f.Array("affixes").Select(e =>
            {
                var a = new JsonFields(e, f.Table, f.Row, f.Errors);
                var affix = new RolledAffix(a.Str("id"), a.Int("roll"));
                a.End();
                return affix;
            })],
            UnusualEffect = f.StrOr("effect", null),
            Identified = f.Bool("identified"),
            Durability = f.Int("durability"),
            MaxDurability = f.Int("maxDurability"),
            WearUses = f.Int("wear"),
            RerollCount = f.Int("rerolls"),
            Count = f.Int("count"),
            History = [.. f.Array("history").Select(e =>
            {
                var h = new JsonFields(e, f.Table, f.Row, f.Errors);
                var ev = new ItemEvent(h.Str("op"), h.Int("index"), Seed(h, "seed"));
                h.End();
                return ev;
            })],
        };
        f.End();
        if (errors.Count > 0)
            throw new InvalidDataException("the item does not decode: " + string.Join("; ", errors.All));
        return item;
    }

    private static ulong Seed(JsonFields f, string field)
    {
        var text = f.Str(field);
        if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed)) return seed;
        f.Errors.Add(f.Table, null, ContentRule.Malformed, $"'{field}' is '{text}', not a seed");
        return 0;
    }

    internal static JsonDocument Parse(byte[] data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("the item is not JSON: " + e.Message, e);
        }
    }
}

/// <summary>
/// Schema 1 items: what the first rules (a port of the host's fake rules) rolled. Each still
/// decodes and replays by that formula, so a character who holds one passes the host's
/// checkpoint audit. The rules give a schema 1 item no other use: it names no base of
/// item_bases.json.
/// </summary>
public static class LegacyItems
{
    /// <summary>The eight base types the first rules rolled, in their order.</summary>
    public static IReadOnlyList<string> Bases =>
        ["scattergun", "rocket_launcher", "minigun", "wrench", "sniper_rifle", "knife", "medigun", "flamethrower"];

    /// <summary>The first rules' hash: SHA-256 of the parts joined by '|', its first eight bytes.</summary>
    public static ulong Hash(params object[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <summary>The schema 1 item the first rules rolled for (ctx, seed).</summary>
    public static RolledItem Roll(ItemRollContext ctx, ulong seed)
    {
        var h = Hash("item", seed, ctx.CharacterClass, ctx.CharacterLevel, ctx.Depth, ctx.ItemLevel, ctx.Tier, ctx.LootTable);
        var bases = Bases;
        var item = new RolledItem(seed, bases[(int)(h % (ulong)bases.Count)], ctx.Tier, ctx.ItemLevel, 1, ctx.Tier < 2, [], ItemCodec.Schema1, ctx.Tier, ctx);
        return item with { Instance = Encode(item) };
    }

    /// <summary>True when a schema 1 item is what the first rules rolled.</summary>
    public static bool Replays(RolledItem item)
    {
        var again = Roll(item.Context, item.Seed);
        return again.BaseType == item.BaseType && again.Rarity == item.Rarity && again.ItemLevel == item.ItemLevel
               && again.Tier == item.Tier && again.Count == item.Count;
    }

    /// <summary>The first rules' bytes: PascalCase JSON with the context nested.</summary>
    public static byte[] Encode(RolledItem i)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("Seed", i.Seed);
            w.WriteString("BaseType", i.BaseType);
            w.WriteNumber("Rarity", i.Rarity);
            w.WriteNumber("ItemLevel", i.ItemLevel);
            w.WriteNumber("Count", i.Count);
            w.WriteBoolean("Identified", i.Identified);
            w.WriteNumber("Tier", i.Tier);
            w.WriteStartObject("Context");
            w.WriteString("CharacterClass", i.Context.CharacterClass);
            w.WriteNumber("CharacterLevel", i.Context.CharacterLevel);
            w.WriteNumber("Depth", i.Context.Depth);
            w.WriteNumber("ItemLevel", i.Context.ItemLevel);
            w.WriteNumber("Tier", i.Context.Tier);
            w.WriteString("LootTable", i.Context.LootTable);
            w.WriteString("Source", i.Context.Source);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    public static RolledItem Decode(byte[] instance)
    {
        using var doc = ItemCodec.Parse(instance);
        var root = doc.RootElement;
        try
        {
            var c = root.GetProperty("Context");
            var ctx = new ItemRollContext(c.GetProperty("CharacterClass").GetString()!, c.GetProperty("CharacterLevel").GetInt32(),
                c.GetProperty("Depth").GetInt32(), c.GetProperty("ItemLevel").GetInt32(), c.GetProperty("Tier").GetInt32(),
                c.GetProperty("LootTable").GetString()!, c.GetProperty("Source").GetString()!);
            return new RolledItem(root.GetProperty("Seed").GetUInt64(), root.GetProperty("BaseType").GetString()!,
                root.GetProperty("Rarity").GetInt32(), root.GetProperty("ItemLevel").GetInt32(), root.GetProperty("Count").GetInt32(),
                root.GetProperty("Identified").GetBoolean(), instance, ItemCodec.Schema1, root.GetProperty("Tier").GetInt32(), ctx);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("the schema 1 item does not decode: " + e.Message, e);
        }
    }
}
