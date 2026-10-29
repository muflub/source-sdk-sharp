using System.Globalization;
using System.Text.Json;

namespace SourceSharp.StockAb;

/// <summary>One live server entity, as either game reports it.</summary>
public sealed record LiveEntity(string ClassName, double X, double Y, double Z);

/// <summary>One classname's standing between the two games.</summary>
/// <param name="Moved">Entities whose nearest counterpart of the same class is further than the tolerance.</param>
public sealed record ClassCensus(string ClassName, int Stock, int Sharp, int Moved);

/// <summary>
/// Which entities each game's server holds once ss_sandbox has loaded: stock
/// hl2mp's (ab_dump_ents) against SourceSharp's (/entities?class=*).
/// </summary>
public static class EntityCensus
{
    /// <summary>
    /// Stock's ab_dump_ents file: index, classname, targetname, origin, angles,
    /// health, model, effects, tab-separated, then "#end".
    /// </summary>
    public static List<LiveEntity> ReadStock(string text)
    {
        var entities = new List<LiveEntity>();
        bool ended = false;

        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (line.StartsWith("#end", StringComparison.Ordinal))
            {
                ended = true;
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length < 4)
                throw new InvalidDataException($"stock entity line has {fields.Length} fields: {line}");

            string[] origin = fields[3].Split(' ');
            entities.Add(new LiveEntity(fields[1], Number(origin[0]), Number(origin[1]), Number(origin[2])));
        }

        // A dump the game was still writing, or one cut short, is not a census.
        if (!ended)
            throw new InvalidDataException("stock entity dump has no #end line");

        return entities;
    }

    /// <summary>SourceSharp's dev API /entities answer.</summary>
    public static List<LiveEntity> ReadSharp(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("entities", out var list))
            throw new InvalidDataException($"no entities in the dev API answer: {Truncate(json)}");

        var entities = new List<LiveEntity>();
        foreach (var entity in list.EnumerateArray())
        {
            var origin = entity.GetProperty("origin");
            entities.Add(new LiveEntity(
                entity.GetProperty("classname").GetString() ?? "",
                origin[0].GetDouble(), origin[1].GetDouble(), origin[2].GetDouble()));
        }

        return entities;
    }

    /// <summary>
    /// Every classname either game holds, with SourceSharp's classnames first
    /// renamed by <paramref name="renames"/> (the stock map's renames), so an
    /// ss_barrel is counted against the prop_physics stock spawned in its place.
    /// </summary>
    public static List<ClassCensus> Compare(
        IReadOnlyList<LiveEntity> stock, IReadOnlyList<LiveEntity> sharp,
        IReadOnlyDictionary<string, string> renames, double tolerance = 1.0)
    {
        var byStock = stock.ToLookup(e => e.ClassName, StringComparer.OrdinalIgnoreCase);
        var bySharp = sharp
            .Select(e => renames.TryGetValue(e.ClassName, out string? stockName) ? e with { ClassName = stockName } : e)
            .ToLookup(e => e.ClassName, StringComparer.OrdinalIgnoreCase);

        return byStock.Select(g => g.Key).Concat(bySharp.Select(g => g.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new ClassCensus(
                name, byStock[name].Count(), bySharp[name].Count(),
                Moved(byStock[name].ToList(), bySharp[name].ToList(), tolerance)))
            .ToList();
    }

    /// <summary>
    /// How many of the smaller side's entities have no counterpart within
    /// <paramref name="tolerance"/> units, matching each counterpart once,
    /// nearest first.
    /// </summary>
    internal static int Moved(List<LiveEntity> stock, List<LiveEntity> sharp, double tolerance)
    {
        var (few, many) = stock.Count <= sharp.Count ? (stock, sharp) : (sharp, stock);
        var pairs = new List<(double Distance, int Few, int Many)>();
        for (int i = 0; i < few.Count; i++)
        {
            for (int j = 0; j < many.Count; j++)
            {
                double dx = few[i].X - many[j].X, dy = few[i].Y - many[j].Y, dz = few[i].Z - many[j].Z;
                double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (distance <= tolerance)
                    pairs.Add((distance, i, j));
            }
        }

        var usedFew = new bool[few.Count];
        var usedMany = new bool[many.Count];
        int matched = 0;
        foreach (var (_, i, j) in pairs.OrderBy(p => p.Distance))
        {
            if (usedFew[i] || usedMany[j])
                continue;
            usedFew[i] = usedMany[j] = true;
            matched++;
        }

        return few.Count - matched;
    }

    private static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string Truncate(string text) => text.Length > 200 ? text[..200] + "..." : text;
}
