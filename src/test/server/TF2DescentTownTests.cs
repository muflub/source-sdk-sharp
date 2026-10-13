using System.Text;
using SourceSharp.MapGen;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// tf2_descent_town as the generator writes it (plan_host_sdk §2.10): the way down,
/// the spawns, determinism, and every material and model it names present in
/// the content the tf2_descent mod mounts.
/// </summary>
public sealed class TF2DescentTownTests
{
    private static VmfMap Town() => new TF2DescentTown().Build();

    [Fact]
    public void There_is_one_way_down_and_it_follows_the_entity_contract()
    {
        var transition = Assert.Single(Town().Entities, e => e.ClassName == TF2DescentTown.TransitionClass);

        string Key(string k) => transition.KeyValues.Single(kv => kv.Key == k).Value;
        Assert.Equal(TF2DescentTown.TransitionName, Key("targetname"));
        Assert.Equal("down", Key("direction"));
        Assert.Equal(TF2DescentTown.FirstLevel, Key("map"));
        Assert.Equal("0", Key("StartDisabled"));
    }

    [Theory]
    [InlineData("func_button", "OnPressed")]
    [InlineData("trigger_multiple", "OnStartTouch")]
    public void The_cage_button_and_the_shaft_both_fire_the_transition(string className, string output)
    {
        var source = Assert.Single(Town().Entities, e => e.ClassName == className);

        Assert.Contains(source.Connections, c => c.Output == output && c.Target == TF2DescentTown.TransitionName && c.Input == "Transition");
        Assert.NotEmpty(source.Solids);
    }

    [Fact]
    public void Sixteen_spawns_in_the_square_and_eight_named_for_arrivals()
    {
        var spawns = Town().Entities.Where(e => e.ClassName == "info_player_deathmatch").ToArray();

        Assert.Equal(TF2DescentTown.ArrivalSpawns, spawns.Count(s => s.Name == TF2DescentTown.ArrivalName));
        Assert.Equal(TF2DescentTown.SquareSpawns, spawns.Count(s => s.Name.Length == 0));
    }

    [Fact]
    public void The_worldspawn_says_its_entities_are_the_mod_contracts()
    {
        var world = Town().World.ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal("mod", world["ssmap_entities"]);
        Assert.Equal("1", world["ssmap_entities_version"]);
        Assert.Equal(TownPalette.Tf2Mining.Sky, world["skyname"]);
    }

    [Fact]
    public void Two_builds_write_the_same_bytes()
    {
        Assert.Equal(Town().Write(), Town().Write());
    }

    //-------------------------------------------------------------------------
    // Every palette entry exists in TF2's VPKs, read from the tree's content
    // link (tools/setup: bin/<config>/content -> the local TF2 install).
    //-------------------------------------------------------------------------

    /// <summary>Every file path a VPK v1/v2 directory lists.</summary>
    private static HashSet<string> VpkFiles(string dirVpk)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = new BinaryReader(File.OpenRead(dirVpk));
        r.ReadUInt32();                 // signature
        uint version = r.ReadUInt32();
        r.ReadUInt32();                 // tree size
        if (version == 2)
            r.ReadBytes(16);

        string Z()
        {
            var b = new List<byte>();
            for (byte c; (c = r.ReadByte()) != 0;)
                b.Add(c);
            return Encoding.Latin1.GetString(b.ToArray());
        }

        for (string ext; (ext = Z()).Length > 0;)
        for (string dir; (dir = Z()).Length > 0;)
        for (string name; (name = Z()).Length > 0;)
        {
            r.ReadUInt32();             // crc
            ushort preload = r.ReadUInt16();
            r.ReadBytes(2 + 4 + 4 + 2); // archive, offset, length, terminator
            r.ReadBytes(preload);
            files.Add($"{dir}/{name}.{ext}");
        }

        return files;
    }

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void Every_material_and_model_the_town_names_is_in_the_mounted_tf2_content()
    {
        string tf = Path.Combine(RepoLayout.Repo("bin", "debug", "content"), "tf");
        Assert.True(Directory.Exists(tf), $"no {tf}: make setup links bin/<config>/content to a Team Fortress 2 install (TF2_DIR)");

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vpk in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
            files.UnionWith(VpkFiles(Path.Combine(tf, vpk)));

        Assert.True(files.Count > 10_000, $"the VPKs listed {files.Count} files: the reader saw nothing");
        var palette = TownPalette.Tf2Mining;
        var missing = palette.Materials.Select(m => $"materials/{m}.vmt")
            .Concat(palette.Models)
            .Concat(new[] { "bk", "dn", "ft", "lf", "rt", "up" }.Select(f => $"materials/skybox/{palette.Sky}{f}.vmt"))
            .Where(f => !files.Contains(f))
            .ToArray();

        Assert.Empty(missing);
    }

    [NotOnCiFact("needs a Team Fortress 2 install for its content, which a runner has not")]
    public void The_content_check_sees_a_missing_entry()
    {
        // Fail-first arm: a palette entry that is not in TF2 must be reported
        // by the same lookup.
        string tf = Path.Combine(RepoLayout.Repo("bin", "debug", "content"), "tf");
        var files = VpkFiles(Path.Combine(tf, "tf2_misc_dir.vpk"));

        Assert.DoesNotContain("models/props_mining/no_such_model.mdl", files);
        Assert.Contains(TownPalette.Tf2Mining.WaterTower, files);
    }
}
