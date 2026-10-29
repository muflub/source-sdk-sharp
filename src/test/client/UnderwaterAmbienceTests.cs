using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Sounds;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The local player's underwater ambience (SetPlayerUnderwater's client arm):
/// "Player.AmbientUnderWater" from the player when its eyes go under, stopped
/// when they come up, once per change.
/// </summary>
public class UnderwaterAmbienceTests
{
    private const int Local = 1;

    private sealed class Sounds : IClientEntitySounds
    {
        public readonly List<string> Calls = [];
        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null) => Calls.Add($"emit {entity.Value:x} {soundName}");
        public void StopFrom(EntityHandle entity, string soundName) => Calls.Add($"stop {entity.Value:x} {soundName}");
    }

    private sealed class One(ClientStandaloneEntity player) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => [player];
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == Local ? player : null;
    }

    private static (UnderwaterAmbience Ambience, ClientSsPlayer Player, Sounds Sounds) Rig()
    {
        var engine = Fake().Returns("Hud.LocalPlayerIndex", Local);
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0001u) };
        player.State.BindLocal();
        var sounds = new Sounds();
        return (new UnderwaterAmbience(new One(player), engine, sounds), player, sounds);
    }

    [Fact]
    public void TheEyesGoingUnderStartItAndComingUpStopsIt()
    {
        var (ambience, player, sounds) = Rig();

        player.State.WaterLevel = 3;
        ambience.Frame();
        ambience.Frame();
        player.State.WaterLevel = 2;
        ambience.Frame();

        Assert.Equal(["emit 10001 Player.AmbientUnderWater", "stop 10001 Player.AmbientUnderWater"], sounds.Calls);
    }

    /// <summary>The client module builds it and asks it once a frame.</summary>
    [Fact]
    public void TheClientAsksItEveryFrame()
    {
        string code = File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"));
        Assert.Matches(@"_underwater\s*=\s*_services\.Create<UnderwaterAmbience>\(\)\s*;", code);
        Assert.Matches(@"_underwater\?\.Frame\(\)\s*;", code);
    }

    [Fact]
    public void FeetInTheWaterAreNotUnder()
    {
        var (ambience, player, sounds) = Rig();

        player.State.WaterLevel = 2;
        ambience.Frame();

        Assert.Empty(sounds.Calls);
        Assert.False(ambience.Underwater);
    }
}
