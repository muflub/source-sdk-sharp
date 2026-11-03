using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server.Entities;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// <see cref="TFSpawnGrounding"/> (deferred row 62): <c>CTFGameRules::GetPlayerSpawnSpot</c>
/// drops the spawning player to the ground under the spot before the core's
/// one-unit lift — a hull trace straight down 500 units, MASK_SOLID, the
/// player ignored, and the trace's end position answers (shared/tf/tf_gamerules.cpp
/// :9866-9875, :9960-9984). Without it a player spawning on a ctf_2fort spawn
/// (z 276) hangs ~20 units above its floor (z 256.75).
/// </summary>
public sealed class SpawnGroundingTests
{
    private static readonly EntityHandle Player = new(0x010001);
    private static readonly EntityHandle Spot = new(0x020042);

    /// <summary>The ctf_2fort red spawn and its floor: the deferred row's numbers.</summary>
    private static readonly Vector SpotOrigin = new(-1200f, 640f, 276f);
    private static readonly Vector Floor = new(-1200f, 640f, 256.75f);

    private static (TFSpawnGrounding Hook, RecordingEngine Engine) Rig(SurfaceTraceResult drop)
    {
        var engine = new RecordingEngine();
        engine.Returns("WorldQuery.TraceBox", drop);
        return (new TFSpawnGrounding(engine), engine);
    }

    /// <summary>The trace that met the floor: fraction <c>(276 - 256.75) / 500</c>.</summary>
    private static SurfaceTraceResult Met => new(true, (276f - 256.75f) / 500f, Floor, new Vector(0f, 0f, 1f),
                                                 EntityHandle.None, -1, 0);

    [Fact]
    public void TheSpawningPlayerIsDroppedToTheGroundTheTraceMet()
    {
        var (hook, _) = Rig(Met);

        Vector placed = hook.PlaceAt(Player, Spot, SpotOrigin);

        Assert.Equal(Floor, placed);
    }

    [Fact]
    public void TheTraceIsThePlayersHullStraightDown500SolidIgnoringThePlayer()
    {
        var (hook, engine) = Rig(Met);

        hook.DropToGround(Player, SpotOrigin);

        EngineCall trace = engine.Last("WorldQuery.TraceBox")!.Value;
        Assert.Equal(SpotOrigin, trace.ArgAs<Vector>(0));
        Assert.Equal(SpotOrigin + new Vector(0f, 0f, -TFSpawnGrounding.DropRange), trace.ArgAs<Vector>(1));
        Assert.Equal(TFSpawnSelector.HullMin, trace.ArgAs<Vector>(2));
        Assert.Equal(TFSpawnSelector.HullMax, trace.ArgAs<Vector>(3));
        Assert.Equal(TraceMask.Solid, trace.ArgAs<TraceMask>(4));
        Assert.Equal(Player, trace.ArgAs<EntityHandle>(5));
    }

    [Fact]
    public void MeetingNothingStillMovesThePlayerToTheEndOfTheTrace()
    {
        // Stock: a trace that meets nothing reports endpos at the far end of
        // the 500-unit ray, and that is where the player goes — the drop is
        // not only a collision, it is where the trace ended.
        var nowhere = new SurfaceTraceResult(false, 1f, SpotOrigin + new Vector(0f, 0f, -500f), Vector.Zero,
                                             EntityHandle.None, -1, 0);
        var (hook, _) = Rig(nowhere);

        Assert.Equal(SpotOrigin + new Vector(0f, 0f, -500f), hook.DropToGround(Player, SpotOrigin));
    }
}
