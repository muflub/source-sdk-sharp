extern alias client;

using SourceSharp.Abi;

using TF2.Descent.Client.Ui;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientPlayerInfo = client::SourceSharp.Abi.ClientPlayerInfo;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using HeroState = client::TF2.Descent.Client.HeroState;
using IClientStandalones = client::SourceSharp.Client.IClientStandalones;
using IHeroStates = client::TF2.Descent.Client.IHeroStates;
using IPlayerNames = client::SourceSharp.Client.Vgui.IPlayerNames;
using Src = client::TF2.Descent.Client.Ui;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The downed overlay's source over the local hero's networked state (plan_descent_logic UI7,
/// WO6): the bleed-out clock from its ticks at 0.1 s a tick, the reviver and his progress, the
/// nearest member up; and the party frames showing a downed member from the same wire value.
/// </summary>
public sealed class DownedSourceTests
{
    private const int Local = 3;
    private const int Other = 4;

    private sealed class Heroes(params HeroState[] heroes) : IHeroStates
    {
        public HeroState? Of(int playerIndex) => heroes.FirstOrDefault(h => h.PlayerIndex == playerIndex);
        public HeroState? Local() => Of(DownedSourceTests.Local);
        public IReadOnlyList<HeroState> All() => heroes.OrderBy(h => h.PlayerIndex).ToList();
    }

    private sealed class Names(params (int Slot, string Name)[] names) : IPlayerNames
    {
        public bool TryGet(int slot, out ClientPlayerInfo info)
        {
            foreach (var (s, n) in names)
                if (s == slot)
                {
                    info = new ClientPlayerInfo(n, slot, false);
                    return true;
                }
            info = default;
            return false;
        }
    }

    private sealed class Ticks : Src.IClientTicks
    {
        public int Now { get; set; } = 1000;
    }

    private sealed class Interval : Src.IClientTickInterval
    {
        public float Seconds => 0.1f;
    }

    private sealed class Party(params DescentPartyMember[] members) : IDescentParty
    {
        public int MemberCount => members.Length;
        public DescentPartyMember Member(int i) => members[i];
    }

    private sealed class NoStandalones : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => [];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>A hero: down from tick 1000 to 1200 when <paramref name="downed"/>, revived by <paramref name="reviver"/> over ticks 1100 to 1130.</summary>
    private static HeroState Hero(int index, bool downed, int reviver = 0) =>
        new(index, 3, 2, 0, 0, 100, 200, 50, 50, 0, 0, downed, downed ? 1200 : 0, false,
            downed ? 1000 : 0, reviver, reviver > 0 ? 1100 : 0, reviver > 0 ? 1130 : 0);

    private static DescentPartyMember Member(string name, bool local, bool downed, int distance, int life = 150) =>
        new(name, "#Descent_Class_Soldier", 2, life, 200, downed, local, false, distance, false);

    private static (Src.DownedSource Source, Ticks Ticks) Source(HeroState local, params DescentPartyMember[] party)
    {
        var ticks = new Ticks();
        return (new Src.DownedSource(new Heroes(local), new Party(party), new Names((Other, "Merasmus"), (5, "Shortround")),
                                     ticks, new Interval()), ticks);
    }

    [Fact]
    public void A_standing_hero_is_not_down_and_shows_nothing()
    {
        var (s, _) = Source(Hero(Local, downed: false));

        Assert.Equal((false, 0f, 0f, false, "", 0), (s.Downed, s.BleedOutSecondsLeft, s.BleedOutTotal, s.BeingRevived, s.NearestMemberName, s.NearestMemberDistance));
    }

    [Fact]
    public void The_bleed_out_is_full_at_the_down_half_in_the_middle_and_empty_at_the_end()
    {
        var (s, ticks) = Source(Hero(Local, downed: true));

        Assert.Equal((20f, 20f), (s.BleedOutSecondsLeft, s.BleedOutTotal));
        ticks.Now = 1100;
        Assert.Equal(10f, s.BleedOutSecondsLeft, 4);
        ticks.Now = 1250;
        Assert.Equal(0f, s.BleedOutSecondsLeft);
    }

    [Fact]
    public void The_reviver_is_named_with_his_progress()
    {
        var (s, ticks) = Source(Hero(Local, downed: true, reviver: Other));
        ticks.Now = 1115;

        Assert.True(s.BeingRevived);
        Assert.Equal("Merasmus", s.ReviverName);
        Assert.Equal(0.5f, s.ReviveFraction, 4);
    }

    [Fact]
    public void With_nobody_reviving_there_is_no_reviver()
    {
        var (s, _) = Source(Hero(Local, downed: true));

        Assert.Equal((false, "", 0f), (s.BeingRevived, s.ReviverName, s.ReviveFraction));
    }

    [Fact]
    public void The_nearest_member_up_is_named_with_his_distance()
    {
        var (s, _) = Source(Hero(Local, downed: true),
                            Member("Bugha", local: true, downed: true, 0),
                            Member("Merasmus", local: false, downed: false, 620),
                            Member("Shortround", local: false, downed: false, 240),
                            Member("Pyro", local: false, downed: true, 50));

        Assert.Equal(("Shortround", 240), (s.NearestMemberName, s.NearestMemberDistance));
    }

    [Fact]
    public void The_party_frames_show_a_downed_member_from_his_networked_state()
    {
        var hud = new ClientEngine { Strict = false }.Returns("Hud.LocalPlayerIndex", Local).Returns("Hud.LocalPlayerHealth", 120);
        var party = new Src.PartySource(new Heroes(Hero(Local, downed: false), Hero(Other, downed: true)), hud,
                                        new Names((Local, "Bugha"), (Other, "Merasmus")), new NoStandalones(),
                                        new ClientEngine { Strict = false }, ClientParties.Of(Local, (Local, "Bugha"), (Other, "Merasmus")));

        Assert.Equal(2, party.MemberCount);
        Assert.Equal((false, true), (party.Member(0).Downed, party.Member(1).Downed));
    }
}
