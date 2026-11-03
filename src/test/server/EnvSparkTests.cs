using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// env_spark's spark state as the code beside it sees it: the public
/// <c>StartSpark</c>/<c>StopSpark</c>/<c>IsSparking</c> of stock's
/// <c>CEnvSpark</c> (EnvSpark.cpp:149, :175; envspark.h:34), which the
/// team_train_watcher's cart sparks drive through while its map inputs drive
/// the very same pair.
/// </summary>
public class EnvSparkTests
{
    private sealed class Rig
    {
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
        public readonly EnvSpark Spark;

        public Rig(int spawnflags = 0)
        {
            Registry.RegisterAll(typeof(EnvSpark).Assembly);
            var services = Services(out Engine);
            Engine.Returns("Entities.IsValid", true)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.GetSpawnFlags", spawnflags);
            Registry.Services = services;

            var handle = new EntityHandle(0x0100_0001u);
            Registry.KeyValue(handle, EntityNames.EnvSpark, "targetname", "sparks");
            Registry.Spawn(handle, EntityNames.EnvSpark);
            Spark = (EnvSpark)Registry.Get(handle)!;
            Handle = handle;
        }

        public EntityHandle Handle { get; }
    }

    [Fact]
    public void AStartedSparkIsSparkingAndItsThinkSparks()
    {
        var rig = new Rig();

        rig.Spark.StartSpark();

        Assert.True(rig.Spark.IsSparking());
        rig.Spark.OnThink();
        Assert.Equal(1, rig.Engine.CountOf("Effects.SparkEffect"));
    }

    [Fact]
    public void AStoppedSparkIsNotSparkingAndItsThinkSaysNothing()
    {
        var rig = new Rig();
        rig.Spark.StartSpark();

        rig.Spark.StopSpark();

        Assert.False(rig.Spark.IsSparking());
        rig.Spark.OnThink();
        Assert.Equal(0, rig.Engine.CountOf("Effects.SparkEffect"));
    }

    [Fact]
    public void ASparkThatDidNotStartOnIsNotSparking()
    {
        var rig = new Rig();

        Assert.False(rig.Spark.IsSparking());
    }

    [Fact]
    public void ASparkThatStartedOnIsSparking()
    {
        var rig = new Rig(spawnflags: 64);

        Assert.True(rig.Spark.IsSparking());
    }

    [Fact]
    public void TheMapInputsDriveTheSameHandle()
    {
        var rig = new Rig();

        rig.Registry.Input(rig.Handle, "StartSpark", string.Empty, 0f, EntityHandle.None);
        Assert.True(rig.Spark.IsSparking());

        rig.Registry.Input(rig.Handle, "StopSpark", string.Empty, 0f, EntityHandle.None);
        Assert.False(rig.Spark.IsSparking());

        rig.Registry.Input(rig.Handle, "ToggleSpark", string.Empty, 0f, EntityHandle.None);
        Assert.True(rig.Spark.IsSparking());
    }
}
