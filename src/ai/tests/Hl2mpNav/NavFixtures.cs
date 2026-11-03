using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The §8.2 nav fixture as a real <c>.nav3d</c>: a floor (top at 16) with an
/// 18 u step, a 30 u and a 50 u ledge, a 100 u platform to drop from, a 40 u
/// crawlspace, a wall with a door in it, a 184 u platform reached by a
/// ladder, a water patch and a hurt corner. Three cells by two, 256 u tall.
/// </summary>
internal static class NavFixtures
{
    /// <summary>The course bound to a bsp, or to a linked level's id.</summary>
    public static VoxelNavFixture Course(Nav3dBspBinding? bsp = null, Guid levelId = default)
    {
        VoxelNavFixture f = Course();
        f.Bsp = bsp;
        f.LevelId = levelId;
        return f;
    }

    public static VoxelNavFixture Course() => new VoxelNavFixture(3, 2, 16)
        .Solid(0, 0, 0, 768, 512, 16)
        .Solid(128, 32, 16, 256, 96, 34)        // the step: 18 up
        .Solid(128, 160, 16, 256, 224, 46)      // the 30 u ledge
        .Solid(128, 288, 16, 256, 352, 66)      // the 50 u ledge
        .Solid(384, 32, 16, 512, 96, 116)       // the platform: 100 down to the floor
        .Solid(384, 160, 56, 512, 224, 256)     // the crawlspace's ceiling: 40 over the floor
        .Solid(384, 400, 16, 512, 512, 200)     // the ladder platform: 184 up
        .Ladder(416, 368, 16, 464, 400, 216)
        .Solid(608, 0, 16, 624, 224, 256)       // the wall, with a 64 u door gap at y 224-288
        .Solid(608, 288, 16, 624, 512, 256)
        .Obstacle("door_1", "func_door", Nav3dObstacleKind.Door, 608, 224, 16, 624, 288, 128)
        .Water(32, 400, 16, 96, 464, 48);

}
