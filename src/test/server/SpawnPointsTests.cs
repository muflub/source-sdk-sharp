using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The spawn choice and its placement (<see cref="SpawnPoints"/>, what
/// <see cref="ServerModule.SelectSpawnPoint"/> hands each spawn to).
///
/// <para>
/// WHAT EACH FACT PINS: a spot an <see cref="ISpawnSelector"/> names (through
/// <see cref="ITeams.SelectSpawn"/>) is placed, once, at its own origin lifted
/// one unit and with its own angles, exactly as the core's walk places its
/// spot; the core's walk is not asked when a selector named one; and no spot
/// at all places nobody. The walk's fact is the positive control: the same
/// placement call over the double, reached by the other branch. The selector
/// fact failed before the fix, when a selector's spot was answered to native
/// (which ignores it) and never placed.
/// </para>
/// </summary>
public class SpawnPointsTests
{
    private static readonly EntityHandle Player = new(0x010001);
    private static readonly EntityHandle TeamSpot = new(0x020042);
    private static readonly EntityHandle WalkSpot = new(0x020043);

    private static readonly Vector TeamSpotAt = new(-1200f, 640f, -96f);
    private static readonly Vector TeamSpotFacing = new(0f, 270f, 0f);
    private static readonly Vector WalkSpotAt = new(300f, -40f, 8f);
    private static readonly Vector WalkSpotFacing = new(0f, 45f, 0f);

    /// <summary>The team service, as far as the spawn ask reads it: a fixed answer.</summary>
    private sealed class SelectingTeams(EntityHandle spot) : ITeams
    {
        public int Asks { get; private set; }
        public int Team(EntityHandle player) => 2;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();

        public EntityHandle SelectSpawn(EntityHandle player)
        {
            Asks++;
            return spot;
        }
    }

    /// <summary>The chain double: the hooks as registered, already in order.</summary>
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>
    /// A placement hook with an answer: the position it was given and the
    /// player and spot it was asked for are recorded, so the fold and the
    /// throw-away can tell what the chain actually passed it.
    /// </summary>
    private sealed class Placer(Vector moved, bool throws = false) : ISpawnPlacement, IOrderedHook
    {
        public int HookOrder { get; init; }
        public Vector? Given { get; private set; }
        public EntityHandle AskedPlayer { get; private set; }
        public EntityHandle AskedSpot { get; private set; }

        public Vector PlaceAt(EntityHandle player, EntityHandle spot, Vector placement)
        {
            Given = placement;
            AskedPlayer = player;
            AskedSpot = spot;
            if (throws)
                throw new InvalidOperationException("the mod is broken");
            return moved;
        }
    }

    /// <summary>
    /// A double answering each spot's own origin and angles by handle, one
    /// info_player_deathmatch on the map (the walk's), and nobody near it.
    /// </summary>
    private static (SpawnPoints Points, RecordingEngine Engine, RecordingLog Log) Rig(
        IHooks<ISpawnPlacement>? placement = null)
    {
        var engine = new RecordingEngine();
        Vector By(string member, Vector team, Vector walk)
            => (EntityHandle)engine.Last(member)!.Value.Args[0]! == TeamSpot ? team : walk;

        engine.ReturnsEachCall("Entities.GetOrigin", () => By("Entities.GetOrigin", TeamSpotAt, WalkSpotAt))
              .ReturnsEachCall("Entities.GetAngles", () => By("Entities.GetAngles", TeamSpotFacing, WalkSpotFacing))
              .ReturnsEachCall("Entities.FindAllByClassname",
                               () => (string)engine.Last("Entities.FindAllByClassname")!.Value.Args[0]! == EntityNames.InfoPlayerDeathmatch
                                   ? new[] { WalkSpot }
                                   : Array.Empty<EntityHandle>())
              .Returns("WorldQuery.EntitiesInSphere", 0)
              .Ignores("Players.PlaceAtSpawn");

        var log = new RecordingLog();
        return (new SpawnPoints(engine, engine, engine, new Random(1), placement, log), engine, log);
    }

    [Fact]
    public void ASelectorsSpotPlacesThePlayerOnItLiftedOneUnitWithItsAngles()
    {
        var (points, engine, _) = Rig();

        EntityHandle placed = points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        Assert.Equal(TeamSpot, placed);
        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(Player, call.Args[0]);
        Assert.Equal(TeamSpotAt with { Z = TeamSpotAt.Z + 1f }, call.Args[1]);
        Assert.Equal(TeamSpotFacing, call.Args[2]);
    }

    [Fact]
    public void ASelectorsSpotIsNotSecondGuessedByTheCoresWalk()
    {
        var (points, engine, _) = Rig();
        var teams = new SelectingTeams(TeamSpot);

        points.SelectAndPlace(Player, teams);

        Assert.Equal(1, teams.Asks);
        Assert.False(engine.Called("Entities.FindAllByClassname"), engine.Describe());
    }

    [Fact]
    public void TheCoresWalkPlacesItsSpotTheSameWay()
    {
        var (points, engine, _) = Rig();

        EntityHandle placed = points.SelectAndPlace(Player, new SelectingTeams(EntityHandle.None));

        Assert.Equal(WalkSpot, placed);
        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(Player, call.Args[0]);
        Assert.Equal(WalkSpotAt with { Z = WalkSpotAt.Z + 1f }, call.Args[1]);
        Assert.Equal(WalkSpotFacing, call.Args[2]);
    }

    [Fact]
    public void NoSpotAnywherePlacesNobody()
    {
        var (points, engine, _) = Rig();
        engine.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());

        EntityHandle placed = points.SelectAndPlace(Player, teams: null);

        Assert.Equal(EntityHandle.None, placed);
        Assert.True(engine.Called("Entities.FindAllByClassname"));
        Assert.False(engine.Called("Players.PlaceAtSpawn"), engine.Describe());
    }

    [Fact]
    public void APlacementHookMovesThePlacementAndTheOneUnitLiftStillFollows()
    {
        // Stock's numbers, the deferred row's example: the ctf_2fort red spawn
        // at z 276 is dropped ~20 units to the floor (z 256.75) by
        // CTFGameRules::GetPlayerSpawnSpot's DropToGround, and the core's
        // one-unit lift still applies on top of the hook's answer.
        var hook = new Placer(TeamSpotAt with { Z = TeamSpotAt.Z - 19.25f });
        var (points, engine, _) = Rig(new ListHooks<ISpawnPlacement>(hook));

        points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(TeamSpotAt with { Z = TeamSpotAt.Z - 18.25f }, call.Args[1]);
        Assert.Equal(TeamSpotFacing, call.Args[2]);
        Assert.Equal(Player, hook.AskedPlayer);
        Assert.Equal(TeamSpot, hook.AskedSpot);
    }

    [Fact]
    public void TheChainFoldsInHookOrderEachHookGettingThePreviousAnswer()
    {
        var first = new Placer(TeamSpotAt with { X = TeamSpotAt.X + 10f }) { HookOrder = 10 };
        var second = new Placer(TeamSpotAt with { X = TeamSpotAt.X + 10f, Y = TeamSpotAt.Y + 20f }) { HookOrder = 20 };
        var (points, engine, _) = Rig(new ListHooks<ISpawnPlacement>(first, second));

        points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        Assert.Equal(TeamSpotAt, first.Given);
        Assert.Equal(TeamSpotAt with { X = TeamSpotAt.X + 10f }, second.Given);
        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(TeamSpotAt with { X = TeamSpotAt.X + 10f, Y = TeamSpotAt.Y + 20f,
                                       Z = TeamSpotAt.Z + 1f }, call.Args[1]);
    }

    /// <summary>
    /// A spot with a player on it among many other things is taken: looking at
    /// only the first 16 near it, the spawn check missed them, and two players
    /// stood in each other at it, a person as well as a bot (live, dm_lockdown's
    /// spawn among lights and decals). The control: the same spot empty is taken up.
    /// </summary>
    [Fact]
    public void ASpotWithAPlayerAmongManyThingsIsTaken()
    {
        EntityHandle standing = new(0x010002);
        EntityHandle[] clutter = [.. Enumerable.Range(0, 40).Select(i => new EntityHandle(0x030000u + (uint)i)), standing];
        EntityHandle Chosen(int seed, EntityHandle[] atWalkSpot)
        {
            var engine = new RecordingEngine();
            engine.ReturnsEachCall("Entities.GetOrigin", () => (EntityHandle)engine.Last("Entities.GetOrigin")!.Value.Args[0]! == TeamSpot ? TeamSpotAt : WalkSpotAt)
                  .ReturnsEachCall("Entities.FindAllByClassname", () => (string)engine.Last("Entities.FindAllByClassname")!.Value.Args[0]! == EntityNames.InfoPlayerDeathmatch ? new[] { WalkSpot, TeamSpot } : Array.Empty<EntityHandle>())
                  .ReturnsEachCall("WorldQuery.EntitiesInSphere.into", () => ((Vector)engine.Last("WorldQuery.EntitiesInSphere")!.Value.Args[0]!).X == WalkSpotAt.X ? atWalkSpot : Array.Empty<EntityHandle>())
                  .ReturnsEachCall("Entities.IsPlayer", () => (EntityHandle)engine.Last("Entities.IsPlayer")!.Value.Args[0]! == standing);
            return new SpawnPoints(engine, engine, engine, new Random(seed)).Choose(Player, null);
        }

        foreach (int seed in Enumerable.Range(1, 8))
            Assert.Equal(TeamSpot, Chosen(seed, clutter));
        Assert.Contains(Enumerable.Range(1, 8), seed => Chosen(seed, []) == WalkSpot);
    }

    [Fact]
    public void AThrowingHookIsNamedAndThePlacementPassesThroughUnmoved()
    {
        var (points, engine, log) = Rig(new ListHooks<ISpawnPlacement>(new Placer(TeamSpotAt, throws: true)));

        points.SelectAndPlace(Player, new SelectingTeams(TeamSpot));

        var call = Assert.Single(engine.AllOf("Players.PlaceAtSpawn"));
        Assert.Equal(TeamSpotAt with { Z = TeamSpotAt.Z + 1f }, call.Args[1]);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("spawn placement hook threw"));
    }
}
