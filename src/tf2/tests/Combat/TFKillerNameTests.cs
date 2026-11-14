extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Shared.Combat;

using Xunit;

using ClientNames = client::TF2.Client.Ui.TFKillerNames;
using ClientName = client::TF2.Shared.Combat.TFKillerName;
using Death = client::SourceSharp.Shared.PlayerDeathArgs;

namespace TF2.Tests.Combat;

/// <summary>
/// The killer name of a death no player scored: the server's
/// <see cref="TFKillerNameNotice"/> sends a mod's token ahead of the
/// <c>player_death</c>, and the client's <c>TFKillerNames</c> answers the kill
/// feeds with it, resolved, for that victim's death alone.
/// </summary>
public sealed class TFKillerNameTests
{
    private static readonly EntityHandle Victim = new(0x0001_0003u);
    private static readonly EntityHandle Player = new(0x0001_0004u);
    private static readonly EntityHandle Robot = new(0x0002_0100u);

    private sealed class Sink : IUserMessageSender
    {
        public readonly List<(string Key, byte[] Payload, int Player, bool Reliable)> Sent = [];

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
            => Sent.Add((key, payload.ToArray(), player, reliable));
    }

    private sealed class Namer(string token, int team) : ITFKillerNames
    {
        public int Asked { get; private set; }

        public bool TryToken(in DeathNoticeFacts death, out string t, out int tm)
        {
            Asked++;
            (t, tm) = (token, team);
            return true;
        }
    }

    private sealed class Hooks<T>(params T[] all) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => all;

        public void Each(string what, Action<T> call)
        {
            foreach (var h in all)
                call(h);
        }
    }

    private static (TFKillerNameNotice Notice, Sink Sink) Notice(params ITFKillerNames[] namers)
    {
        var engine = new RecordingEngine();
        engine.Returns("Players.GetUserId", 42);
        var sink = new Sink();
        return (new TFKillerNameNotice(new Hooks<ITFKillerNames>(namers), engine, sink, new RecordingLog()), sink);
    }

    private sealed class NoFields : IDeathNoticeFields
    {
        public void SetInt(string key, int value) { }
        public void SetBool(string key, bool value) { }
        public void SetString(string key, string value) { }
    }

    [Fact]
    public void A_death_by_no_player_sends_the_mods_token_to_everyone_reliably()
    {
        var (notice, sink) = Notice(new Namer("#Descent_Robot_soldier", 3));

        notice.Fill(new DeathNoticeFacts(Victim, Robot, EntityHandle.None, Robot, "tf_projectile_rocket"), new NoFields());

        var (key, payload, player, reliable) = Assert.Single(sink.Sent);
        Assert.Equal(("TF2:killer_name", 0, true), (key, player, reliable));
        var reader = new UserMessageReader(payload);
        var name = TFKillerName.Read(ref reader);
        Assert.Equal((42, (byte)3, "#Descent_Robot_soldier"), (name.Victim, name.Team, name.Token));
    }

    [Fact]
    public void A_death_a_player_scored_sends_no_name()
    {
        var namer = new Namer("#Descent_Robot_soldier", 3);
        var (notice, sink) = Notice(namer);

        notice.Fill(new DeathNoticeFacts(Victim, Player, Player, Player, "scattergun"), new NoFields());

        Assert.Equal((0, 0), (sink.Sent.Count, namer.Asked));
    }

    [Fact]
    public void A_self_kill_sends_no_name()
    {
        var namer = new Namer("#Descent_Robot_soldier", 3);
        var (notice, sink) = Notice(namer);

        notice.Fill(new DeathNoticeFacts(Victim, Victim, Victim, Victim, "rocketlauncher"), new NoFields());

        Assert.Equal((0, 0), (sink.Sent.Count, namer.Asked));
    }

    [Fact]
    public void With_no_mod_answering_a_world_death_sends_nothing()
    {
        var (notice, sink) = Notice();

        notice.Fill(new DeathNoticeFacts(Victim, EntityHandle.None, EntityHandle.None, EntityHandle.None, "world"), new NoFields());

        Assert.Empty(sink.Sent);
    }

    //-------------------------------------------------------------------------
    // The client's namer
    //-------------------------------------------------------------------------

    private sealed class Clock : client::SourceSharp.Abi.IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Language(Dictionary<string, string> table) : client::SourceSharp.Client.Ui.IUiLocalisation
    {
        public string? Find(string token) => table.GetValueOrDefault(token);
    }

    private static (ClientNames Names, Clock Clock) Client()
    {
        var clock = new Clock();
        var names = new ClientNames(clock, new Language(new() { ["#Descent_Robot_soldier"] = "Soldier Robot" }));
        return (names, clock);
    }

    [Fact]
    public void The_client_names_the_victims_killer_by_the_resolved_token_in_its_team()
    {
        var (names, _) = Client();
        names.OnKillerName(new ClientName { Victim = 7, Team = 3, Token = "#Descent_Robot_soldier" });

        Assert.True(names.TryName(new Death(7, 0, "world", false), out string name, out int team));
        Assert.Equal(("Soldier Robot", 3), (name, team));
    }

    [Fact]
    public void A_token_no_table_holds_names_the_killer_by_the_token()
    {
        var (names, _) = Client();
        names.OnKillerName(new ClientName { Victim = 7, Team = 5, Token = "#TF_HALLOWEEN_EYEBALL_BOSS_DEATHCAM_NAME" });

        Assert.True(names.TryName(new Death(7, 0, "world", false), out string name, out _));
        Assert.Equal("#TF_HALLOWEEN_EYEBALL_BOSS_DEATHCAM_NAME", name);
    }

    [Fact]
    public void Another_victims_death_is_not_named()
    {
        var (names, _) = Client();
        names.OnKillerName(new ClientName { Victim = 7, Team = 3, Token = "#Descent_Robot_soldier" });

        Assert.False(names.TryName(new Death(8, 0, "world", false), out _, out _));
    }

    [Fact]
    public void A_name_older_than_its_hold_names_nobody()
    {
        var (names, clock) = Client();
        names.OnKillerName(new ClientName { Victim = 7, Team = 3, Token = "#Descent_Robot_soldier" });
        clock.Now += ClientNames.HoldSeconds + 0.5f;

        Assert.False(names.TryName(new Death(7, 0, "world", false), out _, out _));
    }

    [Fact]
    public void A_name_from_before_the_clock_went_back_names_nobody()
    {
        var (names, clock) = Client();
        names.OnKillerName(new ClientName { Victim = 7, Team = 3, Token = "#Descent_Robot_soldier" });
        clock.Now = 0.2f;   // a new level

        Assert.False(names.TryName(new Death(7, 0, "world", false), out _, out _));
    }
}
