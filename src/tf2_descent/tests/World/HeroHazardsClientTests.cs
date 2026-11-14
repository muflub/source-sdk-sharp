extern alias client;

using TF2.Descent.Rules;
using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using HazardKinds = client::TF2.Descent.Client.Lamp.HazardKinds;
using HeroHazards = client::TF2.Descent.Client.Lamp.HeroHazards;
using IHeroHazardWire = client::TF2.Descent.Client.Lamp.IHeroHazardWire;

namespace TF2.Descent.Tests.World;

/// <summary>The client's "the local hero stands in a hazard" (HE10): his entity's wire number named by the rules' own hazards.json.</summary>
public sealed class HeroHazardsClientTests
{
    private sealed class Wire : IHeroHazardWire
    {
        public Dictionary<int, int> Numbers { get; } = new();
        public int LocalPlayer { get; set; } = 2;
        public int HazardOf(int playerIndex) => Numbers.GetValueOrDefault(playerIndex);
    }

    private static readonly TF2DescentRules Rules = new();

    private readonly Wire _wire = new();
    private readonly HazardKinds _kinds = new(new Catalog());
    private readonly HeroHazards _hazards;

    public HeroHazardsClientTests() => _hazards = new HeroHazards(_wire, _kinds);

    /// <summary>The server's wire number for a kind: what HazardService writes.</summary>
    private static int ServerNumber(string kind) =>
        Rules.Content.Hazards.Kinds.Select((k, i) => (k.Id, i)).Single(p => p.Id == kind).i + 1;

    [Fact]
    public void The_local_hero_in_lava_reads_lava()
    {
        _wire.Numbers[2] = ServerNumber("lava");

        Assert.Equal("lava", _hazards.Local);
    }

    [Fact]
    public void A_hero_in_nothing_reads_nothing()
    {
        _wire.Numbers[3] = ServerNumber("slime");

        Assert.Null(_hazards.Local);
        Assert.Equal("slime", _hazards.Of(3));
    }

    [Fact]
    public void The_client_names_the_kinds_in_the_servers_order()
    {
        Assert.Equal(Rules.Content.Hazards.Kinds.Select(k => k.Id), _kinds.Ids);
        Assert.Null(_kinds.Of(0));
        Assert.Null(_kinds.Of(_kinds.Ids.Count + 1));
    }

    [Fact]
    public void The_wire_number_fits_the_heroes_four_bits()
    {
        Assert.InRange(Rules.Content.Hazards.Kinds.Count, 1, 15);
    }
}
