using SourceSharp.Abi;

using TF2.Descent.Server.Portal;
using Xunit;

namespace TF2.Descent.Tests.Portal;

/// <summary>Where a town portal's entrance stands (<see cref="PortalPlacement"/>) over a fake floor, walls and pits.</summary>
public sealed class PortalPlacementTests
{
    /// <summary>A floor at <see cref="FloorZ"/> everywhere but the pits, and solid boxes.</summary>
    private sealed class Ground : IPortalGround
    {
        public float FloorZ { get; set; }
        public Vector FloorNormal { get; set; } = new(0f, 0f, 1f);
        public List<(Vector Mins, Vector Maxs)> Boxes { get; } = [];
        public List<(float X0, float Y0, float X1, float Y1)> Pits { get; } = [];

        public TraceResult Line(Vector from, Vector to)
        {
            foreach (var (mins, maxs) in Boxes)
                if (Crosses(from, to, mins, maxs, out float f))
                    return new TraceResult(true, f, from + (to - from) * f, default, EntityHandle.None);

            bool overPit = Pits.Any(p => from.X >= p.X0 && from.X <= p.X1 && from.Y >= p.Y0 && from.Y <= p.Y1);
            if (overPit || from.Z < FloorZ == to.Z < FloorZ)
                return new TraceResult(false, 1f, to, default, EntityHandle.None);
            float t = (from.Z - FloorZ) / (from.Z - to.Z);
            return new TraceResult(true, t, from + (to - from) * t, FloorNormal, EntityHandle.None);
        }

        public bool BoxClear(Vector at, Vector mins, Vector maxs) =>
            !Boxes.Any(b => at.X + mins.X < b.Maxs.X && at.X + maxs.X > b.Mins.X
                            && at.Y + mins.Y < b.Maxs.Y && at.Y + maxs.Y > b.Mins.Y
                            && at.Z + mins.Z < b.Maxs.Z && at.Z + maxs.Z > b.Mins.Z);

        /// <summary>The slab test: whether the segment enters the box, and where.</summary>
        private static bool Crosses(Vector a, Vector b, Vector mins, Vector maxs, out float enter)
        {
            enter = 0f;
            float leave = 1f;
            float[] start = [a.X, a.Y, a.Z], delta = [b.X - a.X, b.Y - a.Y, b.Z - a.Z];
            float[] lo = [mins.X, mins.Y, mins.Z], hi = [maxs.X, maxs.Y, maxs.Z];
            for (int i = 0; i < 3; i++)
            {
                if (MathF.Abs(delta[i]) < 1e-6f)
                {
                    if (start[i] < lo[i] || start[i] > hi[i])
                        return false;
                    continue;
                }
                float t0 = (lo[i] - start[i]) / delta[i], t1 = (hi[i] - start[i]) / delta[i];
                if (t0 > t1)
                    (t0, t1) = (t1, t0);
                enter = MathF.Max(enter, t0);
                leave = MathF.Min(leave, t1);
                if (enter > leave)
                    return false;
            }
            return true;
        }
    }

    private static readonly Vector Feet = new(0f, 0f, 0f);

    [Fact]
    public void On_open_floor_the_pad_stands_on_the_floor_its_distance_in_front()
    {
        var place = PortalPlacement.Place(Feet, 0f, new Ground());

        Assert.True(place.Ok, place.Why);
        Assert.Equal(new Vector(PortalPlacement.Distance, 0f, 0f), place.Position);
    }

    [Fact]
    public void The_pad_stands_in_the_direction_the_hero_faces()
    {
        var place = PortalPlacement.Place(Feet, 90f, new Ground());

        Assert.Equal(0f, place.Position.X, 3);
        Assert.Equal(PortalPlacement.Distance, place.Position.Y, 3);
        Assert.Equal(90f, place.Yaw);
    }

    [Fact]
    public void A_floor_a_step_lower_takes_the_pad_down_onto_it()
    {
        var place = PortalPlacement.Place(new Vector(0f, 0f, 10f), 0f, new Ground { FloorZ = 0f });

        Assert.True(place.Ok, place.Why);
        Assert.Equal(0f, place.Position.Z, 3);
    }

    [Fact]
    public void A_wall_between_the_hero_and_the_spot_refuses_for_room()
    {
        var ground = new Ground();
        ground.Boxes.Add((new Vector(30f, -200f, 0f), new Vector(40f, 200f, 200f)));

        Assert.Equal(PortalPlacement.NoRoom, PortalPlacement.Place(Feet, 0f, ground).Why);
    }

    [Fact]
    public void A_low_ceiling_over_the_spot_refuses_for_room()
    {
        var ground = new Ground();
        ground.Boxes.Add((new Vector(40f, -100f, 60f), new Vector(120f, 100f, 80f)));

        Assert.Equal(PortalPlacement.NoRoom, PortalPlacement.Place(Feet, 0f, ground).Why);
    }

    [Fact]
    public void No_floor_under_the_spot_refuses_for_floor()
    {
        var ground = new Ground();
        ground.Pits.Add((30f, -200f, 500f, 200f));

        Assert.Equal(PortalPlacement.NoFloor, PortalPlacement.Place(Feet, 0f, ground).Why);
    }

    [Fact]
    public void A_corner_over_a_ledge_refuses_for_floor()
    {
        var ground = new Ground();
        ground.Pits.Add((PortalPlacement.Distance + 10f, -200f, 500f, 200f));

        Assert.Equal(PortalPlacement.NoFloor, PortalPlacement.Place(Feet, 0f, ground).Why);
    }

    [Fact]
    public void A_steep_floor_is_refused()
    {
        var ground = new Ground { FloorNormal = new Vector(0.8f, 0f, 0.6f) };

        Assert.Equal(PortalPlacement.TooSteep, PortalPlacement.Place(Feet, 0f, ground).Why);
    }
}
