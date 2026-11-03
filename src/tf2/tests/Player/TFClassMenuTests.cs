using SourceSharp.Abi;

using TF2.Server.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The class menu's server half: the <c>menuopen</c>/<c>menuclosed</c>
/// commands (<c>CTFPlayer::ClientCommand</c>, tf_player.cpp:7482-7491) and
/// the wave hold and grant they arm (<c>IsReadyToSpawn</c> :3243 through
/// teamplayroundbased_gamerules.cpp:2660).
/// </summary>
public sealed class TFClassMenuTests
{
    private static TFPlayerClass Carried(TFPlayerCarriers carriers, EntityHandle player)
    {
        var cls = new TFPlayerClass(carriers, new TFPlayerItems());
        cls.Attach(player);
        return cls;
    }

    [Fact]
    public void The_menuopen_command_opens_the_menu()
    {
        var carriers = new TFPlayerCarriers();
        var player = new EntityHandle(0x0001_0001u);
        var cls = Carried(carriers, player);
        var commands = new TFClassMenuCommands(carriers);

        Assert.True(commands.Handle(player, "menuopen", ReadOnlySpan<string>.Empty));
        Assert.True(cls.MenuOpen); // SetClassMenuOpen( true ), tf_player.cpp:7484
    }

    [Fact]
    public void The_menuclosed_command_closes_an_open_menu()
    {
        var carriers = new TFPlayerCarriers();
        var player = new EntityHandle(0x0001_0001u);
        var cls = Carried(carriers, player);
        cls.MenuOpen = true;
        var commands = new TFClassMenuCommands(carriers);

        Assert.True(commands.Handle(player, "menuclosed", ReadOnlySpan<string>.Empty));
        Assert.False(cls.MenuOpen); // SetClassMenuOpen( false ), :7489
    }

    [Fact]
    public void A_command_the_menu_does_not_own_passes_on()
    {
        var carriers = new TFPlayerCarriers();
        var player = new EntityHandle(0x0001_0001u);
        var commands = new TFClassMenuCommands(carriers);

        Assert.False(commands.Handle(player, "jointeam", ReadOnlySpan<string>.Empty));
    }

    [Fact]
    public void A_player_at_the_class_menu_waits_for_the_wave()
    {
        var carriers = new TFPlayerCarriers();
        var rig = new TF2.Tests.Rules.RoundRig(carriers: carriers);
        var (red, _) = rig.Running();
        var cls = Carried(carriers, red);
        rig.Roster.Respawns.Clear(); // the round-start mass respawn is not this fact's hold
        rig.Roster.Kill(red);
        float died = rig.Clock.Now;
        cls.MenuOpen = true; // IsReadyToSpawn's menu arm, tf_player.cpp:3243

        rig.RunUntil(() => rig.Clock.Now >= died + 21.6f, limit: 60f);
        Assert.DoesNotContain(rig.Roster.Respawns, r => r.Player == red); // held past the wave's died+16.4 maturity
    }

    [Fact]
    public void The_hold_grants_the_instant_spawn_the_choice_spends()
    {
        var carriers = new TFPlayerCarriers();
        var rig = new TF2.Tests.Rules.RoundRig(carriers: carriers);
        var (red, _) = rig.Running();
        var cls = Carried(carriers, red);
        rig.Roster.Respawns.Clear();
        rig.Roster.Kill(red);
        float died = rig.Clock.Now;
        Assert.False(cls.AllowInstantSpawn);

        cls.MenuOpen = true;
        rig.RunUntil(() => rig.Clock.Now >= died + 21.6f, limit: 60f);
        Assert.True(cls.AllowInstantSpawn); // teamplayroundbased_gamerules.cpp:2661-2666, spent by tf_player.cpp:7032
    }

    [Fact]
    public void A_player_past_the_menu_rides_the_wave()
    {
        var carriers = new TFPlayerCarriers();
        var rig = new TF2.Tests.Rules.RoundRig(carriers: carriers);
        var (red, _) = rig.Running();
        Carried(carriers, red); // the menu closed: MenuOpen false, no hold
        rig.Roster.Respawns.Clear();
        rig.Roster.Kill(red);
        float died = rig.Clock.Now;

        Assert.True(rig.RunUntil(() => rig.Roster.Respawns.Any(r => r.Player == red), limit: 60f));
        Assert.InRange(rig.Roster.Respawns.First(r => r.Player == red).At, died + 16.4f, died + 21.5f);
    }
}
