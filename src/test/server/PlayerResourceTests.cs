using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// player_manager, the managed scoreboard singleton (lane U cluster 14).
///
/// <para>
/// What it replaced read each row off a <c>CBasePlayer</c>. There is none left
/// — <c>UTIL_PlayerByIndex</c> has answered NULL for every index since 13c —
/// so the numbers come from the engine's player accessors and from
/// <c>IPlayerDirectory</c>, whose Kills and Deaths are what stock's
/// <c>FragCount()</c> and <c>DeathCount()</c> were. These tests are about that
/// wiring: which source each column comes from, and what an empty slot says.
/// </para>
/// </summary>
public class PlayerResourceTests
{
    /// <summary>
    /// One player in every slot, or none in any — which is as fine a grain as
    /// <see cref="RecordingEngine"/> gives, since it answers per member rather
    /// than per argument. Both cases are the ones that matter: the walk asks
    /// <c>Players.GetHandle</c> and branches on nothing else.
    /// </summary>
    private static PlayerResource Spawn(out RecordingEngine engine,
                                        out Players roster,
                                        bool anyoneConnected,
                                        IClientInfo? clients = null)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PlayerResource).Assembly);

        var services = Services(out engine);

        engine.Returns("Players.GetHandle",
                       anyoneConnected ? new EntityHandle(7) : EntityHandle.None);

        roster = new Players(new RecordingLog(), engine, engine, engine, TestServices.NoButtons);
        services.AddInstance<IPlayerDirectory>(roster);
        if (clients is not null)
            services.AddInstance(clients);

        registry.Services = services;

        var handle = new EntityHandle(90);
        registry.Spawn(handle, EntityNames.PlayerManager);

        return (PlayerResource)registry.Get(handle)!;
    }

    /// <summary>
    /// It registers under stock's own classname, which is what makes the port a
    /// port rather than a second entity beside the old one.
    /// </summary>
    [Fact]
    public void RegistersAsPlayerManager()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PlayerResource).Assembly);

        Assert.True(registry.IsRegistered(EntityNames.PlayerManager));
    }

    /// <summary>
    /// Stock's <c>UpdateTransmitState</c> returned <c>FL_EDICT_ALWAYS</c>, and
    /// this is that. Without it the entity is PVS-checked, which for something
    /// at the origin means most clients never receive it and every scoreboard
    /// row reads zero — with nothing in any log saying why.
    /// </summary>
    [Fact]
    public void IsSentToEveryClientRegardlessOfWhereTheyAre()
    {
        Spawn(out var engine, out _, anyoneConnected: false);

        var call = engine.Last("EntityRender.SetTransmitAlways");

        Assert.True(call.HasValue,
            "player_manager never asked to be always-transmitted; it will be "
            + "PVS-checked and most clients will never see it.\n" + engine.Describe());

        Assert.Equal(true, call!.Value.Args[1]);
    }

    /// <summary>Nothing is claimed about anybody before the first think.</summary>
    [Fact]
    public void StartsWithEveryRowEmpty()
    {
        var resource = Spawn(out _, out _, anyoneConnected: false);

        for (int slot = 0; slot < PlayerResourceNetState.Slots; slot++)
        {
            Assert.False(resource.State.IsConnected(slot));
            Assert.False(resource.State.IsValid(slot));
            Assert.Equal(0, resource.State.Score[slot]);
        }
    }

    /// <summary>
    /// A slot the engine has no player for is cleared whole, so a slot somebody
    /// else later joins into cannot show the previous occupant's numbers.
    /// </summary>
    [Fact]
    public void AnEmptySlotSaysNothingAboutAnybody()
    {
        var resource = Spawn(out _, out _, anyoneConnected: false);

        // Something to overwrite, so that "cleared" is a change rather than the
        // state it started in — a clear that did nothing would pass otherwise.
        resource.State.SetRow(3, 11, 22, 33, 44, 2, true, true, true);

        resource.UpdateSlot(3);

        Assert.False(resource.State.IsConnected(3));
        Assert.False(resource.State.IsValid(3));
        Assert.Equal(0, resource.State.Score[3]);
        Assert.Equal(0, resource.State.Deaths[3]);
        Assert.Equal(0, resource.State.Health[3]);
        Assert.Equal(0, resource.State.Team[3]);
    }

    /// <summary>
    /// Each column comes from the source it is supposed to come from. Every
    /// value here is distinct, so a row wired to the wrong accessor reads back
    /// as somebody else's number rather than as a coincidence.
    /// </summary>
    [Fact]
    public void AConnectedRowIsBuiltFromTheEngineAndTheRoster()
    {
        var resource = Spawn(out var engine, out var roster, anyoneConnected: true);

        engine.Returns("Players.GetUserId", 41)
              .Returns("Players.GetTeam", 3)
              .Returns("Players.IsAlive", true)
              .Returns("Entities.GetHealth", 87);

        // The roster's own counters, which is what stock's FragCount() and
        // DeathCount() were. Distinct values, so a column wired to the wrong one
        // reads back as the other rather than as a coincidence.
        var record = roster[new EntityHandle(7)];
        record.Kills = 9;
        record.Deaths = 2;

        resource.UpdateSlot(6);

        Assert.Equal(9, resource.State.Score[6]);    // the roster's kills
        Assert.Equal(2, resource.State.Deaths[6]);   // the roster's deaths
        Assert.Equal(87, resource.State.Health[6]);  // the entity's
        Assert.Equal(41, resource.State.UserId[6]);  // the player's
        Assert.Equal(3, resource.State.Team[6]);     // the player's
        Assert.True(resource.State.IsConnected(6));
        Assert.True(resource.State.IsAlive(6));
        Assert.True(resource.State.IsValid(6));
    }

    /// <summary>
    /// A dead player is still connected and still has a row. Stock kept the two
    /// apart for the same reason: a scoreboard shows the people waiting to
    /// respawn.
    /// </summary>
    [Fact]
    public void ADeadPlayerIsStillOnTheBoard()
    {
        var resource = Spawn(out var engine, out _, anyoneConnected: true);

        engine.Returns("Players.GetUserId", 41)
              .Returns("Players.GetTeam", 2)
              .Returns("Players.IsAlive", false)
              .Returns("Entities.GetHealth", -30);

        resource.UpdateSlot(6);

        Assert.True(resource.State.IsConnected(6));
        Assert.True(resource.State.IsValid(6));
        Assert.False(resource.State.IsAlive(6));

        // Never negative on the wire — stock clamped for the same reason.
        Assert.Equal(0, resource.State.Health[6]);
    }

    /// <summary>
    /// The walk covers every slot a player's entity index can be, which is what
    /// makes the runs the length they are. Stock looped 1..MAX_PLAYERS and this
    /// is that loop.
    /// </summary>
    [Fact]
    public void TheWalkCoversEverySlotButZero()
    {
        var resource = Spawn(out var engine, out _, anyoneConnected: false);

        engine.Clear();
        resource.OnThink();

        Assert.Equal(PlayerResourceNetState.Slots - 1,
                     engine.CountOf("Players.GetHandle"));
    }

    /// <summary>
    /// It keeps thinking. A scoreboard that updated once would show the round's
    /// opening position for the whole map.
    /// </summary>
    [Fact]
    public void ThinksAgainAfterEachPass()
    {
        Assert.Equal(0.1f, PlayerResource.ThinkInterval);
    }

    /// <summary>Every client 80 ms away at cl_cmdrate 66: stock's sample is 49 ms.</summary>
    private sealed class EightyMs : IClientInfo
    {
        public ulong SteamId64(int entIndex) => 0;

        public string? Address(int entIndex) => null;

        public float? AvgLatency(int entIndex) => 0.080f;

        public string? ClientConVar(int entIndex, string name) => name == "cl_cmdrate" ? "66" : null;
    }

    /// <summary>
    /// The ping moves every twentieth think (2 s), as stock's, and only then:
    /// 19 thinks leave it at 0, the 20th sets it.
    /// </summary>
    [Fact]
    public void The_ping_is_set_on_the_twentieth_think_and_not_before()
    {
        var resource = Spawn(out var engine, out _, anyoneConnected: true, new EightyMs());
        engine.Returns("Players.GetUserId", 41).Returns("Players.GetTeam", 3)
              .Returns("Players.IsAlive", true).Returns("Entities.GetHealth", 87)
              .Returns("Environment.Globals", default(Globals));   // unavailable: stock's default tick
        for (int i = 0; i < 19; i++)
            resource.OnThink();
        Assert.Equal(0, resource.State.Ping[1]);
        resource.OnThink();
        Assert.Equal(9, resource.State.Ping[1]);           // 0.8 x 0 + 0.2 x 49
    }

    /// <summary>Each update smooths the sample into the last ping: 9, 17, 23 towards 49.</summary>
    [Fact]
    public void The_ping_is_smoothed_towards_the_sample()
    {
        var resource = Spawn(out var engine, out _, anyoneConnected: true, new EightyMs());
        engine.Returns("Players.GetUserId", 41).Returns("Players.GetTeam", 3)
              .Returns("Players.IsAlive", true).Returns("Entities.GetHealth", 87)
              .Returns("Environment.Globals", default(Globals));   // unavailable: stock's default tick
        resource.UpdateSlot(1);
        int[] seen = new int[3];
        for (int i = 0; i < 3; i++)
        {
            resource.UpdatePing(1);
            seen[i] = resource.State.Ping[1];
        }
        Assert.Equal([9, 17, 23], seen);
    }
}
