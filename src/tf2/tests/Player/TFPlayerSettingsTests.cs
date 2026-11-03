using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The per-player settings half of stock's
/// <c>CTeamplayRules::ClientSettingsChanged</c>
/// (tf_gamerules.cpp:10275-10279): the userinfo hook reads
/// <c>fov_desired</c> and writes the clamp onto the player's record — 75
/// floor, <c>MAX_FOV</c>'s 90 ceiling, whatever the convar's own 20 says.
/// </summary>
public sealed class TFPlayerSettingsTests
{
    private static readonly EntityHandle Player = PlayerRig.Player;

    private sealed class Said(string value) : IConnectedClients
    {
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => value;
        public int UserId(int entIndex) => -1;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private static (RecordingEngine Engine, TFPlayerSettings Settings) Rig(string? said)
    {
        var engine = new RecordingEngine();
        engine.Returns("Players.GetHandle", Player);
        return (engine, new TFPlayerSettings(new Said(said ?? ""), engine));
    }

    [Fact]
    public void The_wish_lands_clamped_on_the_players_record()
    {
        var (engine, settings) = Rig("80");
        settings.OnClientSettingsChanged(PlayerRig.Index);

        var written = engine.AllOf("Players.SetDefaultFov").ToArray();
        Assert.Single(written);
        Assert.Equal(Player, written[0].Args[0]);
        Assert.Equal(80, written[0].Args[1]);
    }

    [Fact]
    public void The_clamp_is_the_servers_not_the_convars()
    {
        Assert.Equal(75, TFPlayerSettings.Fov("20"));    // under the convar's own floor
        Assert.Equal(75, TFPlayerSettings.Fov("10"));
        Assert.Equal(90, TFPlayerSettings.Fov("120"));   // past MAX_FOV
        Assert.Equal(83, TFPlayerSettings.Fov("83"));
        Assert.Equal(75, TFPlayerSettings.Fov(null));    // atoi's 0 clamps to the floor
        Assert.Equal(75, TFPlayerSettings.Fov("side"));  // as does junk
    }

    [Fact]
    public void Nobody_or_nowhere_writes_nothing()
    {
        var (engine, settings) = Rig("80");
        engine.Returns("Players.GetHandle", EntityHandle.None);
        settings.OnClientSettingsChanged(PlayerRig.Index);
        Assert.Empty(engine.AllOf("Players.SetDefaultFov"));

        var bare = new TFPlayerSettings(new Said("80"), null);
        bare.OnClientSettingsChanged(PlayerRig.Index);     // no engine side: nothing to throw
    }
}
