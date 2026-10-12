using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.MapGen;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ss_sandbox, the map that demonstrates every entity.
///
/// `make map MAP=ss_sandbox` compiles it, running the Windows compilers under
/// Wine -- vbsp is guarded [$WINDOWS] and the tools come from a Windows depot,
/// so there is no native vbsp to reach for. That works, but it needs the tools
/// fetched, Proton present and the HL2 content installed, so it is not what a
/// change to the generator is checked against. Everything checkable without a
/// compiler is checked here instead. Two of these are worth more than the rest:
///
/// Coverage is measured against the [ScriptedEntity] attributes themselves, so
/// adding an entity to the mod and forgetting to demonstrate it fails the build
/// rather than quietly leaving a gap. That is the whole promise of the map.
///
/// Winding is checked because a brush wound inside out still compiles. vbsp
/// takes a face normal as (p0 - p1) x (p2 - p1) pointing OUT of the solid, so a
/// reversed face produces a room a player falls through, discovered by loading
/// the map rather than by building it.
/// </summary>
public class SandboxTests
{
    /// <summary>
    /// The map, built once for the whole class. Building it is the expensive
    /// half and every fact below wants the same one.
    ///
    /// The bulk [Fact] these replace built it once and then called five private
    /// helpers in written order, so a failure in the first stopped the other
    /// four -- and the coverage check is the first.
    /// </summary>
    private static readonly Lazy<VmfMap> Map = new(Sandbox.Build);

    /// <summary>
    /// Classes a map cannot place, and which the coverage rule therefore does
    /// not apply to.
    ///
    /// <para>
    /// The list itself lives in <see cref="SandboxPass.Exemptions"/>, in
    /// MapGen, because the GATE needs the same one: it asks whether the
    /// compiled .bsp contains an instance of every registered class, and an
    /// exemption that applied to the generator but not to the map — or the
    /// other way round — would make one of the two checks lie. One table, two
    /// readers, each entry with a reason beside it.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> NotPlaceable =
        SandboxPass.Exemptions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every class the mod implements, demonstrated somewhere in the map.</summary>
    [Fact]
    public void Coverage()
    {
        var map = Map.Value;

        var declared = typeof(SourceSharp.Server.ServerModule).Assembly
            .GetTypes()
            .SelectMany(t => t.GetCustomAttributes<ScriptedEntityAttribute>())
            .Select(a => a.ClassName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var placed = map.Entities
            .Select(e => e.ClassName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = declared.Where(c => !placed.Contains(c))
                              .Where(c => !NotPlaceable.Contains(c))
                              .OrderBy(c => c)
                              .ToArray();

        Assert.True(declared.Length > 90, $"the mod declares entities to demonstrate ({declared.Length})");
        Assert.True(missing.Length == 0, $"entities missing from the map: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The promise the map makes: a description and a reset button per station.
    /// </summary>
    [Fact]
    public void EveryStationIsComplete()
    {
        var map = Map.Value;

        var byName = map.Entities
            .Where(e => e.Name.Length > 0)
            .ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        int complete = 0;

        foreach (var station in Sandbox.Stations)
        {
            string slug = station.Slug;

            bool hasText = byName.ContainsKey($"{slug}_title")
                        && byName.ContainsKey($"{slug}_expect")
                        && byName.ContainsKey($"{slug}_hint");

            bool hasRelay = byName.TryGetValue($"{slug}_reset", out var relay)
                         && relay.ClassName == EntityNames.LogicRelay;

            bool hasButton = byName.TryGetValue($"{slug}_button", out var button)
                          && button.ClassName == EntityNames.FuncButton
                          && button.Connections.Any(c => c.Target == $"{slug}_reset"
                                                      && c.Input == "Trigger");

            // The zone is what makes a description appear without being asked
            // for, which is the difference between a sign and a manual.
            bool hasZone = byName.TryGetValue($"{slug}_zone", out var zone)
                        && zone.ClassName == EntityNames.TriggerMultiple
                        && zone.Connections.Count(c => c.Input == "Display") == 3;

            // The relay says one more thing than the zone does -- the line that
            // proves the button was pressed rather than walked past.
            bool confirms = hasRelay
                         && relay!.Connections.Any(c => c.Target == $"{slug}_done"
                                                     && c.Input == "Display");

            if (hasText && hasRelay && hasButton && hasZone && confirms)
                complete++;
            else
                Assert.Fail($"station {slug} is complete");
        }

        Assert.True(complete == Sandbox.Stations.Count, $"every station has a description and a reset button ({complete})");

        // Text with no alpha draws invisibly.
        //
        // CGameText::KeyValue reads "color" as four integers -- r, g, b, a --
        // and UTIL_StringToIntArray leaves the missing one at zero. A map that
        // writes the usual three-component colour therefore produces text that
        // is sent, received, drawn, and completely transparent. Nothing warns.
        var badColour = map.Entities
            .Where(e => e.ClassName == "game_text")
            .Select(e => (e.Name, Colour: e.KeyValues.FirstOrDefault(kv => kv.Key == "color").Value))
            .Where(t => t.Colour is null
                     || t.Colour.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not { Length: 4 } parts
                     || parts[3] == "0")
            .Select(t => t.Name)
            .ToArray();

        Assert.True(badColour.Length == 0, $"game_text with no alpha: {string.Join(", ", badColour)}");

        // A button whose brush was forgotten is a button nobody can press.
        Assert.True(map.Entities.Where(e => e.ClassName == EntityNames.FuncButton)
                        .All(e => e.Solids.Count > 0), "every reset button is a brush");

        // game_end ends the round for everyone. It is in the map for coverage
        // and must never be on the end of a wire.
        string[] enders = map.Entities.Where(e => e.ClassName == EntityNames.GameEnd)
                                      .Select(e => e.Name).ToArray();

        Assert.True(enders.Length > 0
            && !map.Entities.SelectMany(e => e.Connections).Any(c => enders.Contains(c.Target)), "game_end is placed but not wired to anything");
    }

    [Fact]
    public void NamesAndWires()
    {
        var map = Map.Value;

        var duplicates = map.Entities
            .Select(e => e.Name)
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.True(duplicates.Length == 0, $"duplicated names: {string.Join(", ", duplicates)}");

        // Every wire has to land somewhere. A typo in a target is invisible in
        // game -- the output fires and nothing happens.
        var names = map.Entities.Select(e => e.Name)
                                .Where(n => n.Length > 0)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dangling = map.Entities
            .SelectMany(e => e.Connections.Select(c => (From: e.Name, c.Target, c.Input)))
            .Where(w => !names.Contains(w.Target))
            .ToArray();

        Assert.True(dangling.Length == 0, $"wires to nothing: {string.Join(", ", dangling.Select(d => $"{d.From}->{d.Target}.{d.Input}"))}");
    }

    /// <summary>
    /// Brush geometry, checked the way vbsp reads it.
    ///
    /// A box is right when every face's normal points away from the middle. That
    /// single property catches a reversed winding, a transposed axis and a
    /// degenerate face, which are the three ways generated brushwork goes wrong.
    /// </summary>
    [Fact]
    public void Geometry()
    {
        var map = Map.Value;

        var solids = map.WorldSolids
            .Concat(map.Entities.SelectMany(e => e.Solids))
            .ToArray();

        Assert.True(solids.Length > 40, $"the map has brushwork ({solids.Length} brushes)");
        Assert.True(solids.All(s => s.Sides.Count == 6), "every brush is a box of six faces");

        int outward = 0;
        int degenerate = 0;

        foreach (var solid in solids)
        {
            // The centre of a convex solid is inside it, and for a box the mean
            // of the face points is exactly that.
            double cx = solid.Sides.Average(s => (s.P0.X + s.P1.X + s.P2.X) / 3.0);
            double cy = solid.Sides.Average(s => (s.P0.Y + s.P1.Y + s.P2.Y) / 3.0);
            double cz = solid.Sides.Average(s => (s.P0.Z + s.P1.Z + s.P2.Z) / 3.0);

            foreach (var side in solid.Sides)
            {
                double ax = side.P0.X - side.P1.X, ay = side.P0.Y - side.P1.Y, az = side.P0.Z - side.P1.Z;
                double bx = side.P2.X - side.P1.X, by = side.P2.Y - side.P1.Y, bz = side.P2.Z - side.P1.Z;

                double nx = (ay * bz) - (az * by);
                double ny = (az * bx) - (ax * bz);
                double nz = (ax * by) - (ay * bx);

                if (Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz)) < 1e-6)
                {
                    degenerate++;
                    continue;
                }

                // Outward means the centre is on the back of the plane.
                double toCentre = (nx * (cx - side.P1.X))
                                + (ny * (cy - side.P1.Y))
                                + (nz * (cz - side.P1.Z));

                if (toCentre < 0)
                    outward++;
            }
        }

        Assert.True(degenerate == 0, "no brush has a degenerate face");
        Assert.True(outward == solids.Length * 6, "every face points out of its brush");
    }

    /// <summary>The file itself: what a compiler will actually read.</summary>
    [Fact]
    public void TextIsWellFormed()
    {
        var map = Map.Value;

        string text = map.Write();

        Assert.True(text.Contains("\"classname\" \"worldspawn\""), "the map file has a world");
        Assert.True(text.StartsWith("versioninfo"), "the map file has a version header");

        int depth = 0;
        int lowest = 0;

        foreach (char c in text)
        {
            if (c == '{') depth++;
            else if (c == '}') { depth--; lowest = Math.Min(lowest, depth); }
        }

        Assert.True(depth == 0, "every block in the map file is closed");
        Assert.True(lowest == 0, "no block closes before it opens");

        // A decimal comma would be produced by a culture-sensitive format and
        // read by vbsp as the end of a number.
        Assert.True(!text.Split('\n').Any(l => l.Contains("\"plane\"") && l.Contains(',')), "no position was written with a comma");

        // Ids must be unique across the document; Hammer refuses the file
        // otherwise and vbsp behaves unpredictably.
        var ids = System.Text.RegularExpressions.Regex
            .Matches(text, "\"id\" \"(\\d+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.True(ids.Length == ids.Distinct().Count(), $"every id in the file is unique ({ids.Length} of them)");
    }
}
