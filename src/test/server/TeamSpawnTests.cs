using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Team deathmatch's spawn points (<see cref="TeamSpawnSelector"/>, plan
/// A.3 item 2): stock <c>CHL2MP_Player::EntSelectSpawnPoint</c> under
/// teamplay walks the spawning player's team's class and falls back to the
/// deathmatch points when the map has none (hl2mp_player.cpp:1397-1414);
/// with every point taken, the spawn frag never kills a teammate and settles
/// on a point no teammate holds (plan A.7, a divergence row).
/// </summary>
public class TeamSpawnTests
{
    private static readonly EntityHandle Spawning = new(0x0001_0001u);
    private static readonly EntityHandle Mate = new(0x0001_0002u);
    private static readonly EntityHandle Enemy = new(0x0001_0003u);

    private static readonly EntityHandle CombinePoint = new(0x0002_0010u);
    private static readonly EntityHandle RebelPoint = new(0x0002_0011u);
    private static readonly EntityHandle DmOne = new(0x0002_0012u);
    private static readonly EntityHandle DmTwo = new(0x0002_0013u);

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = new();
        public readonly Dictionary<string, EntityHandle[]> Classes = new();
        public readonly Dictionary<EntityHandle, int> Teams = new();
        /// <summary>Who stands on each point (inside the frag radius, and so inside the 128 u check).</summary>
        public readonly Dictionary<EntityHandle, EntityHandle[]> Standing = new();
        public readonly TeamSpawnSelector Selector;

        private static Vector At(EntityHandle spot) => new(spot.EntryIndex * 1000f, 0f, 0f);

        public Rig(int spawningTeam)
        {
            Teams[Spawning] = spawningTeam;
            var e = Engine;
            e.ReturnsEachCall("Entities.FindAllByClassname",
                              () => Classes.TryGetValue((string)e.Calls[^1].Args[0]!, out var found) ? found : [])
             .ReturnsEachCall("Entities.GetOrigin", () => At((EntityHandle)e.Calls[^1].Args[0]!))
             .ReturnsEachCall("Entities.GetAngles", () => default(Vector))
             .ReturnsEachCall("WorldQuery.EntitiesInSphere.into", () =>
             {
                 var center = (Vector)e.Calls[^1].Args[0]!;
                 var spot = Standing.Keys.FirstOrDefault(s => At(s).X == center.X);
                 return Standing.TryGetValue(spot, out var who) ? who : [];
             })
             .ReturnsEachCall("Entities.IsPlayer",
                              () => (EntityHandle)e.Calls[^1].Args[0]! is var h && (h == Spawning || h == Mate || h == Enemy))
             .ReturnsEachCall("Players.GetTeam",
                              () => Teams.TryGetValue((EntityHandle)e.Calls[^1].Args[0]!, out int t) ? t : -1)
             .Ignores("Players.PlaceAtSpawn");

            var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
            convars.RegisterTypes([typeof(Hl2mpSpawnFragFallbackRadiusConVar)]);
            var points = new SpawnPoints(e, e, e, e, convars.Declared<Hl2mpSpawnFragFallbackRadiusConVar>(), new Random(1));
            Selector = new TeamSpawnSelector(points, e);
        }

        public EntityHandle[] Killed => [.. Engine.AllOf("Damage.TakeDamage").Select(c => c.ArgAs<EntityHandle>(0))];
    }

    [Fact]
    public void ACombinePlayerSpawnsOnACombinePoint()
    {
        var rig = new Rig(TeamDeathmatchMode.TeamCombine);
        rig.Classes[EntityNames.InfoPlayerCombine] = [CombinePoint];
        rig.Classes[EntityNames.InfoPlayerRebel] = [RebelPoint];
        rig.Classes[EntityNames.InfoPlayerDeathmatch] = [DmOne];

        Assert.Equal(CombinePoint, rig.Selector.SelectSpawn(Spawning));
    }

    [Fact]
    public void ARebelPlayerSpawnsOnARebelPoint()
    {
        var rig = new Rig(TeamDeathmatchMode.TeamRebels);
        rig.Classes[EntityNames.InfoPlayerCombine] = [CombinePoint];
        rig.Classes[EntityNames.InfoPlayerRebel] = [RebelPoint];
        rig.Classes[EntityNames.InfoPlayerDeathmatch] = [DmOne];

        Assert.Equal(RebelPoint, rig.Selector.SelectSpawn(Spawning));
    }

    /// <summary>dm_lockdown's case: no team points, so stock plays on the deathmatch ones.</summary>
    [Fact]
    public void AMapWithNoTeamPointsFallsBackToTheDeathmatchPoints()
    {
        var rig = new Rig(TeamDeathmatchMode.TeamRebels);
        rig.Classes[EntityNames.InfoPlayerDeathmatch] = [DmOne];

        Assert.Equal(DmOne, rig.Selector.SelectSpawn(Spawning));
    }

    [Fact]
    public void ATeammateOnAPointStillMakesItTaken()
    {
        var rig = new Rig(TeamDeathmatchMode.TeamRebels);
        rig.Teams[Mate] = TeamDeathmatchMode.TeamRebels;
        rig.Classes[EntityNames.InfoPlayerDeathmatch] = [DmOne, DmTwo];
        rig.Standing[DmOne] = [Mate];

        Assert.Equal(DmTwo, rig.Selector.SelectSpawn(Spawning));
        Assert.Empty(rig.Killed);
    }

    [Fact]
    public void EveryPointTakenTheFragSettlesWhereNoTeammateStands()
    {
        // Both orders of the random start: the point a teammate holds is
        // never the one settled on, whichever the walk began at.
        foreach (var (mateOn, enemyOn) in new[] { (DmOne, DmTwo), (DmTwo, DmOne) })
        {
            var rig = new Rig(TeamDeathmatchMode.TeamRebels);
            rig.Teams[Mate] = TeamDeathmatchMode.TeamRebels;
            rig.Teams[Enemy] = TeamDeathmatchMode.TeamCombine;
            rig.Classes[EntityNames.InfoPlayerDeathmatch] = [DmOne, DmTwo];
            rig.Standing[mateOn] = [Mate];
            rig.Standing[enemyOn] = [Enemy];

            Assert.Equal(enemyOn, rig.Selector.SelectSpawn(Spawning));
            Assert.Equal([Enemy], rig.Killed);
        }
    }

    [Fact]
    public void TheSpawnFragNeverKillsATeammate()
    {
        var rig = new Rig(TeamDeathmatchMode.TeamCombine);
        rig.Teams[Mate] = TeamDeathmatchMode.TeamCombine;
        rig.Teams[Enemy] = TeamDeathmatchMode.TeamRebels;
        rig.Classes[EntityNames.InfoPlayerCombine] = [CombinePoint];
        rig.Standing[CombinePoint] = [Mate, Enemy, Spawning];

        Assert.Equal(CombinePoint, rig.Selector.SelectSpawn(Spawning));
        Assert.Equal([Enemy], rig.Killed);
    }
}
