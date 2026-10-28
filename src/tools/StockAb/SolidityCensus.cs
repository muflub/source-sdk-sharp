using System.Globalization;
using System.Text.Json;

namespace SourceSharp.StockAb;

/// <summary>One named entity's solidity, as either game reports it.</summary>
public sealed record NamedSolidity(string ClassName, string Name, int Solid, int Flags, int Physics = -1);

/// <summary>A name both games hold whose solidity differs.</summary>
public sealed record SolidityDifference(string Name, string StockClass, string SharpClass,
                                        int StockSolid, int SharpSolid, int StockFlags, int SharpFlags,
                                        int StockPhysics = -1, int SharpPhysics = -1);

/// <summary>The solidity census: what was paired and what differs.</summary>
public sealed record SolidityReport(int Paired, int StockOnly, int SharpOnly, List<SolidityDifference> Differ);

/// <summary>
/// How each named entity is solid in the two games once ss_sandbox has
/// loaded: stock's ab_movetypes rows against SourceSharp's /entities, paired
/// by targetname. Solid type and every FSOLID_ flag are compared: a
/// prop_dynamic SOLID_BSP here against stock's not-solid OBB was the reason.
/// </summary>
public static class SolidityCensus
{
    /// <summary>
    /// Stock's ab_movetypes file: index, classname, targetname, movetype,
    /// solid, solid flags and (patch 0008) the physics object's kind,
    /// tab-separated, then "#end".
    /// </summary>
    public static List<NamedSolidity> ReadStock(string text)
    {
        var entities = new List<NamedSolidity>();
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
            if (fields.Length is not (6 or 7))
                throw new InvalidDataException($"stock movetype line has {fields.Length} fields: {line}");

            entities.Add(new NamedSolidity(fields[1], fields[2], Integer(fields[4]), Integer(fields[5]),
                                           fields.Length == 7 ? Integer(fields[6]) : -1));
        }

        if (!ended)
            throw new InvalidDataException("stock movetype dump has no #end line");

        return entities;
    }

    /// <summary>SourceSharp's dev API /entities answer, which must carry the name and solidity.</summary>
    public static List<NamedSolidity> ReadSharp(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("entities", out var list))
            throw new InvalidDataException("no entities in the dev API answer");

        var entities = new List<NamedSolidity>();
        foreach (var entity in list.EnumerateArray())
        {
            if (!entity.TryGetProperty("name", out var name) || !entity.TryGetProperty("solid", out var solid)
                || !entity.TryGetProperty("solidflags", out var flags))
            {
                throw new InvalidDataException("the dev API's entities carry no name and solidity: an older build");
            }

            entities.Add(new NamedSolidity(entity.GetProperty("classname").GetString() ?? "", name.GetString() ?? "",
                                           solid.GetInt32(), flags.GetInt32(),
                                           entity.TryGetProperty("physics", out var physics) ? physics.GetInt32() : -1));
        }

        return entities;
    }

    /// <summary>
    /// Every name each game holds exactly once, paired; the pairs whose solid
    /// type or flags differ. A name either game holds twice is neither paired
    /// nor counted as one side's alone.
    /// </summary>
    public static SolidityReport Compare(IReadOnlyList<NamedSolidity> stock, IReadOnlyList<NamedSolidity> sharp)
    {
        var repeated = Repeated(stock).Concat(Repeated(sharp)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stockByName = Unique(stock, repeated);
        var sharpByName = Unique(sharp, repeated);

        var differ = new List<SolidityDifference>();
        int paired = 0;
        foreach (var (name, a) in stockByName.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!sharpByName.TryGetValue(name, out var b))
                continue;

            paired++;
            // The physics object counts only where both games said what it is.
            bool physicsDiffers = a.Physics >= 0 && b.Physics >= 0 && a.Physics != b.Physics;
            if (a.Solid != b.Solid || a.Flags != b.Flags || physicsDiffers)
                differ.Add(new SolidityDifference(name, a.ClassName, b.ClassName, a.Solid, b.Solid, a.Flags, b.Flags,
                                                  a.Physics, b.Physics));
        }

        return new SolidityReport(paired, stockByName.Keys.Count(n => !sharpByName.ContainsKey(n)),
                                  sharpByName.Keys.Count(n => !stockByName.ContainsKey(n)), differ);
    }

    private static IEnumerable<string> Repeated(IReadOnlyList<NamedSolidity> entities)
        => entities.Where(e => e.Name.Length > 0)
                   .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                   .Where(g => g.Count() > 1)
                   .Select(g => g.Key);

    private static Dictionary<string, NamedSolidity> Unique(IReadOnlyList<NamedSolidity> entities, HashSet<string> repeated)
        => entities.Where(e => e.Name.Length > 0 && !repeated.Contains(e.Name))
                   .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

    private static int Integer(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
