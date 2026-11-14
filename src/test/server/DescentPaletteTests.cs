using System.Globalization;
using Microsoft.Data.Sqlite;
using SourceSharp.MapGen.Descent;
using SourceSharp.MapGen.Descent.Themes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A fact that needs the asset database (<c>/home/lodle/assetdb</c>, or
/// <c>DESCENT_ASSETDB</c>): it skips, saying so, only when the database is
/// absent from this machine.
/// </summary>
public sealed class AssetDbFactAttribute : FactAttribute
{
    public AssetDbFactAttribute()
    {
        if (!File.Exists(AssetDb.File))
            Skip = $"no asset database at {AssetDb.File} (set DESCENT_ASSETDB to its directory)";
    }
}

/// <inheritdoc cref="AssetDbFactAttribute"/>
public sealed class AssetDbTheoryAttribute : TheoryAttribute
{
    public AssetDbTheoryAttribute()
    {
        if (!File.Exists(AssetDb.File))
            Skip = $"no asset database at {AssetDb.File} (set DESCENT_ASSETDB to its directory)";
    }
}

/// <summary>The asset database, read only, and what a palette asks of it.</summary>
public static class AssetDb
{
    public static string Directory => Environment.GetEnvironmentVariable("DESCENT_ASSETDB") is { Length: > 0 } d ? d : "/home/lodle/assetdb";

    public static string File => Path.Combine(Directory, "assets.db");

    /// <summary>
    /// What is wrong with each asset against the database: a material that is
    /// not a TF2 brush material, or does not tile a whole number of times
    /// along a 1024 cell at its scale; a model TF2 does not have; a static
    /// one the database says cannot be a <c>prop_static</c> (not
    /// <c>static_prop</c>, or with <c>prop_data</c>); a model whose bounds
    /// are not the database's mesh bounds.
    /// </summary>
    public static IReadOnlyList<string> Problems(IEnumerable<ThemeAsset> assets, IReadOnlyDictionary<string, float> scales)
    {
        var problems = new List<string>();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = File, Mode = SqliteOpenMode.ReadOnly }.ToString());
        db.Open();
        foreach (ThemeAsset a in assets)
        {
            if (a.Kind is "material" or "water")
            {
                using var q = db.CreateCommand();
                q.CommandText = "SELECT m.usage, t.width FROM materials m JOIN games g ON g.id = m.game_id LEFT JOIN textures t ON t.id = m.texture_id WHERE g.name = 'tf' AND m.path = $p";
                q.Parameters.AddWithValue("$p", a.Asset);
                using var r = q.ExecuteReader();
                if (!r.Read())
                {
                    problems.Add($"{a.Role} {a.Asset}: no such TF2 material");
                    continue;
                }

                // A water volume's material is a water shader's; every other face's a brush material.
                string usage = a.Kind == "water" ? "water" : "brush";
                if (r.GetString(0) != usage)
                    problems.Add($"{a.Role} {a.Asset}: its usage is {r.GetString(0)}, not {usage}");
                if (scales.TryGetValue(a.Role, out float scale) && !r.IsDBNull(1))
                {
                    float repeat = r.GetInt32(1) * scale;
                    if (1024 % repeat != 0)
                        problems.Add($"{a.Role} {a.Asset}: {r.GetInt32(1)} texels at {scale} repeat every {repeat}, not a whole number of times along a 1024 cell");
                }

                continue;
            }

            using var m = db.CreateCommand();
            m.CommandText = """
                SELECT mo.static_prop, mo.prop_data, mo.mesh_min_x, mo.mesh_min_y, mo.mesh_min_z, mo.mesh_max_x, mo.mesh_max_y, mo.mesh_max_z
                FROM game_models gm JOIN games g ON g.id = gm.game_id JOIN models mo ON mo.id = gm.model_id
                WHERE g.name = 'tf' AND mo.path = $p
                """;
            m.Parameters.AddWithValue("$p", $"models/{a.Asset}.mdl");
            using var mr = m.ExecuteReader();
            if (!mr.Read())
            {
                problems.Add($"{a.Role} {a.Asset}: no such TF2 model");
                continue;
            }

            if (a.Kind == "static" && (mr.GetInt32(0) != 1 || !mr.IsDBNull(1)))
                problems.Add($"{a.Role} {a.Asset}: placed as a prop_static, but static_prop is {mr.GetInt32(0)} and prop_data is {(mr.IsDBNull(1) ? "null" : "set")}");
            if (a.Model is { } model)
            {
                float[] db6 = [.. Enumerable.Range(2, 6).Select(i => (float)mr.GetDouble(i))];
                float[] ours = [model.Mins.X, model.Mins.Y, model.Mins.Z, model.Maxs.X, model.Maxs.Y, model.Maxs.Z];
                if (db6.Zip(ours).Any(p => Math.Abs(p.First - p.Second) > 0.01f))
                    problems.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{a.Role} {a.Asset}: bounds ({string.Join(' ', ours)}) are not the database's ({string.Join(' ', db6.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)))})"));
            }
        }

        return problems;
    }
}

/// <summary>
/// The depths' palettes (plan E7, task B2): every asset a theme names is a
/// row of its depth's palette file and a row of the asset database with the
/// right usage, and a palette names nothing its theme does not.
/// </summary>
public sealed class DescentPaletteTests
{
    public static TheoryData<int> ActOne => [1, 2, 3, 4];

    public static TheoryData<int> Themed => [1, 2, 3, 4];

    private static string PalettePath(int depth) =>
        RepoLayout.Repo("src", "mapgen", "Descent", "Themes", "palettes", ThemeAssets.PaletteFile(depth));

    /// <summary>A palette's rows as (asset, kind, role), its header and notes left out.</summary>
    private static IReadOnlyList<(string Asset, string Kind, string Role)> Palette(int depth)
    {
        string path = PalettePath(depth);
        Assert.True(File.Exists(path), $"{path} is missing");
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Skip(1)
            .Select(l => l.Split('\t')).ToArray();
        Assert.All(rows, r => Assert.True(r.Length == 4 && r[3].Length > 0, $"{path}: '{string.Join('\t', r)}' is not asset, kind, role and why"));
        return [.. rows.Select(r => (r[0], r[1], r[2]))];
    }

    private static IReadOnlyList<(string Asset, string Kind, string Role)> Named(int depth) =>
        [.. ThemeAssets.Of(DescentRooms.ThemeFor(depth)).Select(a => (a.Asset, a.Kind, a.Role))];

    private static IReadOnlyDictionary<string, float> Scales(RoomTheme theme)
    {
        var scales = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (t, prefix) in new[] { (theme, ""), (theme.Arrival, "arrival-") })
        {
            if (t is null)
                continue;
            scales[prefix + "floor"] = t.Floor.Scale;
            scales[prefix + "wall"] = t.Wall.Scale;
            scales[prefix + "ceiling"] = t.Ceiling.Scale;
            scales[prefix + "block"] = t.Block.Scale;
            if (t.Trim is { } trim)
                scales[prefix + "trim"] = trim.Scale;
            if (t.LargeWall is { } large)
                scales[prefix + "large-wall"] = large.Scale;
        }

        return scales;
    }

    [Theory]
    [MemberData(nameof(Themed))]
    public void Every_asset_a_theme_names_is_a_row_of_its_palette(int depth)
    {
        var palette = Palette(depth).ToHashSet();

        Assert.DoesNotContain(Named(depth), a => !palette.Contains(a));
    }

    [Theory]
    [MemberData(nameof(Themed))]
    public void A_palette_names_nothing_its_theme_does_not(int depth)
    {
        var named = Named(depth).ToHashSet();

        Assert.DoesNotContain(Palette(depth), a => !named.Contains(a));
    }

    [AssetDbTheory]
    [MemberData(nameof(Themed))]
    public void Every_asset_a_theme_names_is_in_the_asset_database_with_its_usage(int depth)
    {
        RoomTheme theme = DescentRooms.ThemeFor(depth);

        Assert.Empty(AssetDb.Problems(ThemeAssets.Of(theme), Scales(theme)));
    }

    [AssetDbFact]
    public void A_misspelt_material_is_refused()
    {
        RoomTheme theme = DescentRooms.ThemeFor(1);
        var misspelt = ThemeAssets.Of(theme with { Wall = new("concrete/computerwal003c", 0.5f) });

        Assert.Contains(AssetDb.Problems(misspelt, Scales(theme)), p => p.Contains("computerwal003c: no such TF2 material", StringComparison.Ordinal));
    }

    [AssetDbFact]
    public void A_water_material_named_as_a_brush_material_is_refused()
    {
        var water = new ThemeAsset("swamp/water/water_murky", "material", "wall");

        Assert.Contains(AssetDb.Problems([water], new Dictionary<string, float>()), p => p.Contains("usage is water, not brush", StringComparison.Ordinal));
    }

    [AssetDbFact]
    public void A_brush_material_named_as_water_is_refused()
    {
        var water = new ThemeAsset("nature/rockwall003", "water", "water");

        Assert.Contains(AssetDb.Problems([water], new Dictionary<string, float>()), p => p.Contains("usage is brush, not water", StringComparison.Ordinal));
    }

    [AssetDbFact]
    public void A_model_with_prop_data_is_refused_as_a_static_prop()
    {
        var crate = new PropModel("models/props_2fort/miningcrate001.mdl", new(-20.83f, -36f, 0), new(21.93f, 36.59f, 38.75f));

        Assert.Contains(AssetDb.Problems([new ThemeAsset("props_2fort/miningcrate001", "static", "corner-pile", crate)], new Dictionary<string, float>()),
            p => p.Contains("prop_data is set", StringComparison.Ordinal));
    }

    [AssetDbFact]
    public void A_surface_that_does_not_tile_along_a_cell_is_refused()
    {
        var wall = new ThemeAsset("concrete/computerwall003c", "material", "wall");

        Assert.Contains(AssetDb.Problems([wall], new Dictionary<string, float> { ["wall"] = 0.3f }), p => p.Contains("whole number", StringComparison.Ordinal));
    }

    [AssetDbFact]
    public void A_model_whose_bounds_are_not_the_databases_is_refused()
    {
        PropModel lamp = ActOneLamp() with { Maxs = new(14.41f, 14.87f, 40f) };

        Assert.Contains(AssetDb.Problems([new ThemeAsset("props_mining/industrial_ceiling_lamp", "static", "lamp", lamp)], new Dictionary<string, float>()),
            p => p.Contains("not the database's", StringComparison.Ordinal));
    }

    private static PropModel ActOneLamp() => DescentRooms.ThemeFor(1).Lamps!.Fixture;
}
