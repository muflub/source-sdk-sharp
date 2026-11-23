using SourceSharp.Abi;

using TF2.Descent.Server.Loot;
using TF2.Descent.Server.Party;
using TF2.Descent.Tests.Hero;

using Xunit;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// Who shares a kill (R4, <see cref="HeroLootParty"/>): the living heroes of the killer's party (WO9)
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
        public readonly InMemoryParties Parties;

        public PartyRig()
        {
            Rig.Engine.ReturnsEachCall("Entities.GetOrigin", () => Origins.GetValueOrDefault(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
            Rig.Engine.ReturnsEachCall("Players.IsAlive", () => !Dead.Contains(Rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
            Parties = new InMemoryParties(Rig.Rules, new Party.PartyChanges().Hooks);
            var roster = new PartyRoster(Parties, new EnginePartySlots(Rig.Heroes, Rig.Keys()));
            Party = new HeroLootParty(Rig.Heroes, Rig.Keys(), Rig.Engine, Rig.Engine, Rig.Rules, roster);
        }

        /// <summary>The heroes in these slots form one party (WO9).</summary>
        public void Together(params int[] slots) =>
            Assert.True(Parties.Make([.. slots.Select(s => Rig.Keys().Of(s)!)]).GetAwaiter().GetResult().Ok);

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
        p.Together(4, 3);

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
        p.Together(4, 3);
        p.Dead.Add(HeroRig.PlayerOf(4));

        Assert.Equal([3], p.Party.Near(default, killer: 4).Select(m => m.Player));
    }

    [Fact]
    public void A_stranger_in_range_of_a_heros_kill_gets_no_seat()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(20, 0, 0));
        p.Hero(5, new Vector(30, 0, 0));
        p.Together(4, 3);

        Assert.Equal([4, 3], p.Party.Near(default, killer: 4).Select(m => m.Player));
        Assert.Equal([5], p.Party.Near(default, killer: 5).Select(m => m.Player));   // solo, the stranger's kill is his alone
    }

    [Fact]
    public void A_member_out_of_range_of_his_partys_kill_gets_no_seat()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector((float)p.Range + 1, 0, 0));
        p.Together(3, 4);

        Assert.Equal([3], p.Party.Near(default, killer: 3).Select(m => m.Player));
    }

    [Fact]
    public void A_kill_by_no_hero_pays_every_hero_in_range_party_or_not()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(20, 0, 0));
        p.Hero(5, new Vector(30, 0, 0));
        p.Together(3, 4);

        Assert.Equal([3, 4, 5], p.Party.Near(default, killer: 0).Select(m => m.Player));
    }

    [Fact]
    public void No_more_heroes_share_than_the_rules_seats()
    {
        var p = new PartyRig();
        for (int i = 3; i < 3 + LootFloor.Seats + 2; i++)
            p.Hero(i, new Vector(i, 0, 0));

        Assert.Equal(LootFloor.Seats, p.Party.Near(default, killer: 0).Count);
    }

    [Fact]
    public void A_pile_is_shared_with_the_killers_whole_party_on_the_level_far_or_dead_the_killer_first()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(20, 0, 0));
        p.Hero(5, new Vector((float)p.Range * 3, 0, 0));
        p.Hero(6, new Vector(30, 0, 0));
        p.Together(5, 3, 4);
        p.Dead.Add(HeroRig.PlayerOf(4));
        var key = p.Rig.Keys();

        var members = p.Party.PileMembers(key.Of(4)!, key.Of(3)!);

        Assert.Equal([key.Of(4)!, key.Of(3)!, key.Of(5)!], members);
    }

    [Fact]
    public void A_pile_whose_killer_left_is_shared_with_the_claimers_party()
    {
        var p = new PartyRig();
        p.Hero(3, new Vector(10, 0, 0));
        p.Hero(4, new Vector(20, 0, 0));
        p.Hero(5, new Vector(30, 0, 0));
        p.Together(3, 4);
        var key = p.Rig.Keys();

        Assert.Equal([key.Of(3)!, key.Of(4)!], p.Party.PileMembers("76561190000099999", key.Of(4)!));
    }
}
