using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// env_wind: the parameters travel, the simulation does not.
///
/// <para>
/// The gust generator itself is the stock CEnvWindShared kept alive
/// client-side, so what the server half owns — and what these tests pin — is
/// the ingest: the keys into the state, the facing into a nine-bit yaw, and
/// stock's own literal zero for the seed.
/// </para>
/// </summary>
public class EnvWindTests
{
    private static readonly EntityHandle Ent = new(0x0057_0001u);

    [Fact]
    public void TheKeysAndTheFacingBecomeTheState()
    {
        var engine = Mock();
        engine.Returns("Entities.GetAngles", new Vector(0f, 45f, 0f));
        engine.Returns("Clock.Now", 12.5f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvWind).Assembly);
        registry.Services = Services();
        // The recording double answers the facing read.
        var services = new ServiceContainer();
        foreach (var (contract, _) in EngineServices.Areas(new RecordingLog()))
            services.AddInstance(contract, engine);
        services.AddInstance<IGameClock>(engine);
        registry.Services = services;

        foreach (var (k, v) in new[] {
            ("minwind", "20"), ("maxwind", "50"), ("mingust", "100"),
            ("maxgust", "250"), ("mingustdelay", "10"), ("maxgustdelay", "20"),
            ("gustdirchange", "20"), ("gustduration", "5") })
        {
            registry.KeyValue(Ent, EntityNames.EnvWind, k, v);
        }

        registry.Spawn(Ent, EntityNames.EnvWind);
        var wind = (EnvWind)registry.Get(Ent)!;

        Assert.Equal(20, wind.State.MinWind);
        Assert.Equal(250, wind.State.MaxGust);
        Assert.Equal(20f, wind.State.MaxGustDelay);
        Assert.Equal(45, wind.State.InitialWindDir);

        // Stock passes literally 0 for the seed; a rolled one here would be a
        // different wind from stock's for the same map.
        Assert.Equal(0, wind.State.WindSeed);
    }

    [Fact]
    public void AYawIsBoundToNineUnsignedBits()
    {
        // A mapper's -90 and 270 are one facing; the wire field is unsigned.
        Assert.Equal(270, EnvWind.NormaliseYaw(-90f));
        Assert.Equal(0, EnvWind.NormaliseYaw(360f));
        Assert.Equal(45, EnvWind.NormaliseYaw(45f));
        Assert.Equal(315, EnvWind.NormaliseYaw(-405f));
    }
}
