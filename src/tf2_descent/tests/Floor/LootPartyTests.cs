using SourceSharp.Abi;

using TF2.Descent.Server.Loot;
using TF2.Descent.Tests.Hero;

using Xunit;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// Who shares a kill (R4, <see cref="HeroLootParty"/>): the living heroes
/// with a sheet within the XP's party range of where the robot died, the
/// killer first when he is one of them, at most the rules' seats.
/// </summary>
public sealed class LootPartyTests
{
    private sealed class PartyRig
    {
        public readonly HeroRig Rig = new();
        public readonly Dictionary<EntityHandle, Vector> Origins = new();
        public readonly HashSet<EntityHandle> Dead = [];
        public readonly HeroLootParty Party;

        public PartyRig()
        {
            Rig.Engine.ReturnsEachCall("Entities.GetOrigin", () => Origins.GetValueOrDefault(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
            Rig.Engine.ReturnsEachCall("Players.IsAlive", () => !Dead.Contains(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
            Party = new HeroLootParty(Rig.Heroes, Rig.Keys(), Rig.Engine, Rig.Engine, Rig.Rules);
        }

        public void Hero(int index, Vector at)
        {
            Rig.Players.Steam[index] = HeroRig.SteamId + (ulong)index;
            Assert.NotNull(Rig.Join(index));
            Origins[HeroRig.PlayerOf(index)] = at;
        }

        public double Range => Rig.Rules.Content.Experience.PartyRangeUnits;
    }

    [Fact]
    public void The_heroes_in_range_share_and_one_out_of_range_does_not()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(0, (float)p.Range - 1, 0));
        p.Hero(5, new Vector((float)p.Range + 1, 0, 0));

        var near = p.Party.Near(default, killer: 0);

        Assert.Equal([3, 4], near.Select(m => m.Player));
    }

    [Fact]
    public void The_killer_comes_first()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(20, 0, 0));

        var near = p.Party.Near(default, killer: 4);

        Assert.Equal([4, 3], near.Select(m => m.Player));
        Assert.Equal(p.Rig.Keys().Of(4), near[0].Hero);
    }

    [Fact]
    public void A_dead_hero_gets_no_seat()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(20, 0, 0));
        p.Dead.Add(HeroRig.PlayerOf(4));

        Assert.Equal([3], p.Party.Near(default, killer: 4).Select(m => m.Player));
    }

    [Fact]
    public void No_more_heroes_share_than_the_rules_seats()
    {
        var p = new PartyRig();
        for (int i = 3; i < 3 + LootFloor.Seats + 2; i++)
            p.Hero(i, new Vector(i, 0, 0));

        Assert.Equal(LootFloor.Seats, p.Party.Near(default, killer: 0).Count);
    }
}
