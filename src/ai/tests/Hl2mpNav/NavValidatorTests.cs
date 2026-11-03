using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The validator (plan_hl2mp_ai §6.3) against a world answered from the
/// course's own solid boxes: the nav agrees on every arm, a world whose floors
/// all moved 32 units (a nav built from drifted geometry) does not, and each
/// arm (ruled 2026-11-02: standing, floor, solid) catches its own drift.
/// </summary>
public sealed class NavValidatorTests
{
    private sealed class BoxWorld(IReadOnlyList<VoxelNavFixture.Box> solids, float shift) : IWorldProbe
    {
        private bool Hits(float x0, float y0, float z0, float x1, float y1, float z1)
            => solids.Any(s => s.Overlaps(x0, y0, z0 - shift, x1, y1, z1 - shift));

        public string LastHit => "a box";

        public bool LastHitWorld => true;

        public bool BoxClear(AiVector feet, float width, float height)
        {
            float r = width * 0.5f;
            return !Hits(feet.X - r, feet.Y - r, feet.Z, feet.X + r, feet.Y + r, feet.Z + height);
        }

        // Outside the course is the void, which a sealed map's world is solid in.
        public bool Meets(AiVector from, AiVector to, AiVector half)
        {
            for (int i = 0; i <= 16; i++)
            {
                AiVector p = from + (to - from) * (i / 16f);
                if (p.Z < 0f || p.X < 0f || p.Y < 0f || p.X > 768f || p.Y > 512f
                    || Hits(p.X - half.X, p.Y - half.Y, p.Z - half.Z, p.X + half.X, p.Y + half.Y, p.Z + half.Z))
                    return true;
            }

            return false;
        }
    }

    private static NavValidation Run(float shift, Func<IEnumerable<VoxelNavFixture.Box>, IEnumerable<VoxelNavFixture.Box>>? edit = null)
    {
        VoxelNavFixture course = NavFixtures.Course();
        Nav3dReader reader = course.Open();
        DerivedNav nav = NavGraphFromNav3d.Derive(new Nav3dNavSource(reader), Hl2mpNavNumbers.Stock(56f), NavEdgeCosts.Default, []);
        List<VoxelNavFixture.Box> world = [.. (edit ?? (b => b))(course.Solids)];
        return NavValidator.Run(nav, new Nav3dNavSource(reader), new Nav3dVoxelSpace(reader), new BoxWorld(world, shift), 400, 7);
    }

    private static string Report(NavValidation v)
        => string.Join("; ", v.Arms.Select(a => $"{a.Name} {a.Bad}/{a.Asked}")) + ": " + string.Join("; ", v.Examples);

    [Fact]
    public void TheCourseAgreesWithItsWorldOnEveryArm()
    {
        NavValidation v = Run(0f);
        Assert.True(v.Passed, Report(v));
        Assert.All(v.Arms, a => Assert.Equal(400, a.Asked));
        Assert.All(v.Arms, a => Assert.Equal(0, a.Bad));
    }

    [Fact]
    public void FloorsThatMovedThirtyTwoUnitsFailIt()
    {
        NavValidation v = Run(-32f);
        Assert.False(v.Passed, Report(v));
    }

    [Fact]
    public void FloorsRaisedThirtyTwoUnitsFailTheStandingArm()
    {
        NavValidation v = Run(32f);
        Assert.True(v.Standing.Rate > NavValidation.StandingCeiling, Report(v));
        Assert.False(v.Standing.Passed);
    }

    // The ladder platform, 184 u up: the nav stands players on it, the world
    // has only the ground floor under it.
    [Fact]
    public void APlatformTheWorldLacksFailsTheFloorArm()
    {
        NavValidation v = Run(0f, w => w.Where(b => b.Z1 != 200f));
        Assert.True(v.Floor.Rate > NavValidation.FloorCeiling, Report(v));
        Assert.False(v.Floor.Passed);
    }

    // The ground slab 16 u under the floor the nav has: the nav is solid in
    // the voxel under its cells, the world open there, yet a player still
    // finds ground within the step height (the floor arm passes).
    [Fact]
    public void AFloorTheWorldHasThinnerFailsTheSolidArm()
    {
        NavValidation v = Run(0f, w => w.Select(b => b.Z0 == 0f && b.Z1 == 16f ? b with { Z0 = -40f, Z1 = -4f } : b));
        Assert.True(v.Floor.Passed, Report(v));
        Assert.True(v.Solid.Rate > NavValidation.SolidCeiling, Report(v));
        Assert.False(v.Solid.Passed);
    }

    [Fact]
    public void TheArmsHaveTheirRuledCeilings()
    {
        NavValidation v = Run(0f);
        Assert.Equal(["standing", "floor", "solid"], v.Arms.Select(a => a.Name));
        Assert.Equal([0.005, 0.005, 0.03], v.Arms.Select(a => a.Ceiling));
    }
}
