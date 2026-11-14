using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// TF2's navigation profile (plan_bots_teams_tf2 P6 step 2): its numbers are the mod's movement,
/// the Scout alone reaches a double jump, and the team solids are the respawn room visualizers.
/// </summary>
public class TFNavProfileTests
{
    private sealed class Facts(params (int Index, TFClass Class)[] players) : ITFActorFacts
    {
        public bool TryRead(int playerIndex, out TFActorFacts facts)
        {
            foreach ((int index, TFClass cls) in players)
            {
                if (index == playerIndex)
                {
                    facts = new TFActorFacts(cls, TFTeam.Red, TFClass.Civilian, TFTeam.Unassigned, 0, 0f, 0, false, false, false, false, false, false);
                    return true;
                }
            }

            facts = default;
            return false;
        }
    }

    /// <summary>The level's entities, as the live level would answer them.</summary>
    private sealed class Level(params MapEntity[] all) : ITFLevelEntities
    {
        public int Reads { get; private set; }

        public MapEntities Read()
        {
            Reads++;
            return new MapEntities(all, null);
        }
    }

    private static TFNavProfile Profile(Facts facts, Level? level = null) => new(facts, level ?? new Level());

    private static EntityHandle Player(int index) => new((uint)(0x0001_0000 | index));

    private static (MapEntity Room, MapEntity Visualizer) RedSpawnRoom()
    {
        var keys = new[] { new KeyValuePair<string, string>("respawnroomname", "spawn_red") };
        var room = new MapEntity(TFTeamSolids.Room, "spawn_red", default, new AiVector(0, 0, 0), new AiVector(256, 256, 128), Team: 2,
                                 Keys: [new KeyValuePair<string, string>("TeamNum", "2")]);
        var visualizer = new MapEntity(TFTeamSolids.Visualizer, null, default, new AiVector(256, 96, 0), new AiVector(264, 160, 128), Keys: keys);
        return (room, visualizer);
    }

    [Fact]
    public void TheProfilesNumbersAreTheModsMovement() => Assert.Equal(TFNavProfile.ModNumbers, Profile(new Facts()).Numbers(56f));

    [Fact]
    public void TheNumbersDoNotDependOnTheFile()
        => Assert.Equal(Profile(new Facts()).Numbers(56f), Profile(new Facts()).Numbers(80f));

    [Fact]
    public void TheScoutReachesADoubleJump()
        => Assert.Equal(JumpReach.Double, Profile(new Facts((1, TFClass.Scout))).Reach(Player(1)));

    [Fact]
    public void ASoldierDoesNot()
        => Assert.Equal(JumpReach.None, Profile(new Facts((1, TFClass.Soldier))).Reach(Player(1)));

    [Fact]
    public void APlayerWithNoCarrierYetReachesNoFurther()
        => Assert.Equal(JumpReach.None, Profile(new Facts((2, TFClass.Scout))).Reach(Player(1)));

    [Fact]
    public void TheTeamSolidsAreTheVisualizers()
    {
        var (room, visualizer) = RedSpawnRoom();
        var level = new Level();

        Assert.Equal(2, Assert.Single(Profile(new Facts(), level).TeamSolids(new MapEntities([room, visualizer], null))).Team);
        Assert.Equal(0, level.Reads);
    }

    /// <summary>
    /// A map whose lump is compressed (every shipped TF2 map's) has its visualizers read from the
    /// level's own entities: live on ctf_2fort the lump read none, and no visualizer was solid to
    /// a bot, so enemies' routes ran through each team's spawn rooms.
    /// </summary>
    [Fact]
    public void ACompressedLumpsTeamSolidsAreTheLevelsVisualizers()
    {
        var (room, visualizer) = RedSpawnRoom();
        var unread = new MapEntities([], null, "bsp lump 0 is LZMA-compressed");

        Assert.Equal(2, Assert.Single(Profile(new Facts(), new Level(room, visualizer)).TeamSolids(unread)).Team);
    }
}
