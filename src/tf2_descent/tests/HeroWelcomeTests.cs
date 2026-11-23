using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Mode;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The message of the day greets a hero on his first level of the session
/// only (user 2026-11-15): every later load in the same session lets him
/// arrive with no panel. The session is the engine's user id, which a level
/// change keeps and a new connection replaces; a player who left and came
/// back is greeted again; other modes keep the core's greeting on every
/// level.
/// </summary>
public sealed class HeroWelcomeTests
{
    private sealed class ModeState(bool active) : IDescentModeState
    {
        public bool Active => active;
    }

    /// <summary>An engine whose player in any slot has the user id <see cref="User"/> holds at the ask.</summary>
    private sealed class Rig
    {
        public RecordingEngine Engine { get; } = RecordingEngine.Default();

        public int User { get; set; } = 5;

        public HeroWelcome Welcome { get; }

        public Rig(bool active = true)
        {
            Engine.Returns("Players.GetHandle", new EntityHandle(1u));
            Engine.ReturnsEachCall("Players.GetUserId", () => User);
            Welcome = new HeroWelcome(new ModeState(active), Engine, new RecordingLog());
        }
    }

    [Fact]
    public void A_heros_first_level_of_the_session_shows_the_motd()
    {
        var rig = new Rig();

        Assert.True(rig.Welcome.ShowsMotd(1));
    }

    [Fact]
    public void A_later_level_in_the_same_session_shows_no_motd()
    {
        var rig = new Rig();
        rig.Welcome.ShowsMotd(1); // the town

        Assert.False(rig.Welcome.ShowsMotd(1)); // depth 1, by the cage
        Assert.False(rig.Welcome.ShowsMotd(1)); // back to town by a portal
    }

    [Fact]
    public void Another_player_joining_later_is_greeted_on_his_own_first_level()
    {
        var rig = new Rig();
        rig.Welcome.ShowsMotd(1);

        rig.User = 6;

        Assert.True(rig.Welcome.ShowsMotd(2));
    }

    /// <summary>A reconnect is a new connection: the same slot, a new user id.</summary>
    [Fact]
    public void A_player_who_left_and_rejoined_is_greeted_again()
    {
        var rig = new Rig();
        rig.Welcome.ShowsMotd(1);

        rig.User = 7;

        Assert.True(rig.Welcome.ShowsMotd(1));
    }

    [Fact]
    public void The_greeting_is_asked_of_the_slots_own_player()
    {
        var rig = new Rig();

        rig.Welcome.ShowsMotd(3);

        Assert.Equal(3, rig.Engine.AllOf("Players.GetHandle").Single().Arg(0));
        Assert.Equal(new EntityHandle(1u), rig.Engine.AllOf("Players.GetUserId").Single().Arg(0));
    }

    [Fact]
    public void A_slot_with_no_user_id_is_greeted()
    {
        var rig = new Rig { User = 0 };
        rig.Welcome.ShowsMotd(1);

        Assert.True(rig.Welcome.ShowsMotd(1));
    }

    [Fact]
    public void Under_another_mode_every_level_shows_the_motd()
    {
        var rig = new Rig(active: false);

        Assert.True(rig.Welcome.ShowsMotd(1));
        Assert.True(rig.Welcome.ShowsMotd(1));
    }

    [Fact]
    public void The_registration_puts_the_welcome_in_the_cores_motd_chain()
    {
        var services = new ServiceContainer();
        services.AddInstance<IDescentModeState>(new ModeState(true));
        services.AddInstance<IEnginePlayers>(RecordingEngine.Default());
        services.AddInstance<ILog>(new RecordingLog());
        SideServices.Register(services, Side.Server, [typeof(HeroWelcome)]);

        Assert.IsType<HeroWelcome>(Assert.Single(services.ResolveAll<IMotdGate>()));
    }
}
