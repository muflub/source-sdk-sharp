using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Entities;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// GetMaterialIndex: the icon is precached into the Materials string table and
/// indexed from it (team_control_point.cpp:221-262).
/// </summary>
public sealed class PrecachedMaterialsTests
{
    private sealed class FakeTable : INetworkStringTableRef
    {
        public readonly List<string> Strings = new();

        public string Name => "Materials";

        public int AddString(string value, ReadOnlySpan<byte> userData)
        {
            Strings.Add(value);
            return Strings.Count - 1;
        }

        public int FindStringIndex(string value)
        {
            int at = Strings.IndexOf(value);
            return at < 0 ? INetworkStringTableRef.InvalidStringIndex : at;
        }
    }

    private sealed class FakeTables(FakeTable? table) : INetworkStringTableDirectory
    {
        public INetworkStringTableRef? Find(string name) => name == PrecachedMaterials.TableName ? table : null;
    }

    private static (PrecachedMaterials Materials, RecordingEngine Engine) Rig(FakeTable? table)
    {
        var services = TestServices.Services(out var engine);
        return (new PrecachedMaterials(services.Require<IEnginePrecache>(), new FakeTables(table)), engine);
    }

    private static string[] Precached(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Precache.PrecacheMaterial")
                       .Select(c => Assert.IsType<string>(c.Args[0])).ToArray();

    [Fact]
    public void A_named_icon_is_precached_and_indexed_from_the_table()
    {
        var table = new FakeTable { Strings = { "sprites/obj_icons/icon_obj_red", "sprites/obj_icons/icon_obj_blu" } };
        var (materials, engine) = Rig(table);

        Assert.Equal(0, materials.IndexOf("sprites/obj_icons/icon_obj_red"));
        Assert.Equal(1, materials.IndexOf("sprites/obj_icons/icon_obj_blu"));
        Assert.Equal(["sprites/obj_icons/icon_obj_red", "sprites/obj_icons/icon_obj_blu"], Precached(engine));
    }

    [Fact]
    public void A_name_outside_the_table_answers_zero_but_is_still_precached()
    {
        var (materials, engine) = Rig(new FakeTable());

        Assert.Equal(0, materials.IndexOf("sprites/obj_icons/never_made_it"));
        Assert.Equal(["sprites/obj_icons/never_made_it"], Precached(engine));
    }

    [Fact]
    public void An_empty_name_is_neither_precached_nor_indexed()
    {
        var table = new FakeTable();
        var (materials, engine) = Rig(table);

        Assert.Equal(0, materials.IndexOf(string.Empty));
        Assert.Empty(Precached(engine));
        Assert.Empty(table.Strings);
    }

    [Fact]
    public void A_server_with_no_material_table_answers_zero()
    {
        Assert.Equal(0, Rig(null).Materials.IndexOf("sprites/obj_icons/icon_obj_red"));
    }
}
