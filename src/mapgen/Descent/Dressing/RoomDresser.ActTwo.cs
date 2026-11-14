namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// The dressing Act II adds (brief p4): pieces hung upside down from a tall room's roof (level 7's
/// stalactites), and water standing on a room's floor (level 8's flooded galleries, level 7's cave
/// pools), kept off every door so no water touches a socket's plug.
/// </summary>
public static partial class RoomDresser
{
    /// <summary>The lowest a hanging piece's tip comes over the floor under it: two players' heights.</summary>
    public const int HangingClear = 2 * RoomLint.HullHeight;

    /// <summary>The lowest room a piece hangs from the roof of.</summary>
    public const int HangingRoom = 768;

    private sealed partial class Dresser
    {
        /// <summary>
        /// Pieces hung from the roof of a tall room on a grid (<see cref="PropSet.Hanging"/>), upside down
        /// (rolled half a turn, so each must be symmetric about its origin), their tips
        /// <see cref="HangingClear"/> over the floor at least and over a jump room's rocket air.
        /// </summary>
        private void HangingClutter(PropSet props)
        {
            if (room.Height < HangingRoom || IsHallway(room))
                return;
            Rect b = room.Body;
            float pitch = props.HangingSpacing;
            for (float x = b.X0 + pitch / 2; x < b.X1; x += pitch)
                for (float y = b.Y0 + pitch / 2; y < b.Y1; y += pitch)
                {
                    var (i, j) = FloorPlan.ColumnOf(x, y);
                    if (!plan.IsFloor(i, j) || solids.CeilingAbove(x, y, sill + 1) is not { } ceil)
                        continue;
                    float least = room.Entry.Jump ? sill + JumpTable.RocketClearAir + 16 : sill + HangingClear;
                    foreach (PropModel m in props.Hanging.OrderBy(_ => rng.Int(0, 1000)))
                    {
                        if (Math.Abs(m.Mins.Z + m.Maxs.Z) > 1 || Math.Abs(m.Mins.Y + m.Maxs.Y) > 1 || ceil - m.Height < least)
                            continue;
                        int yaw = 90 * rng.Int(0, 4);
                        var origin = new Point(MathF.Round(x), MathF.Round(y), ceil - m.Maxs.Z);
                        var (mins, maxs) = m.Turned(yaw);
                        if (!Fits(new Box(origin + mins, origin + maxs), solid: false, floorStanding: false, Role.Hanging))
                            continue;
                        AddProp(m, origin, yaw, Role.Hanging, shadows: true, roll: 180);
                        placed.Add((new Box(origin + mins, origin + maxs), Role.Hanging));
                        break;
                    }
                }
        }

        /// <summary>
        /// Water standing over the open floor of the room's body (<see cref="RoomTheme.Standing"/>), cut
        /// back <see cref="StandingWater.FromDoors"/> from every socket's opening; none in a way in or
        /// down, in a room that holds water of its own (a pool, a flooded gallery), or in a family the
        /// theme does not flood.
        /// </summary>
        private IEnumerable<Brush> StandingWater(StandingWater standing)
        {
            if (room.Role != RoomRole.None || room.Water.Count > 0 || room.Entry.Tags.Contains("boss_arena")
                || standing.Families is { } families && !families.Contains(Family(room.Name)))
                return [];
            var holes = doors.Select(d => new Rect(d.Mins.X, d.Mins.Y, d.Maxs.X, d.Maxs.Y).Grow(standing.FromDoors));
            string material = theme.Water ?? Shapes.PoolShape.WaterMaterial;
            Rect area = room.Body.Grow(-standing.Inset);
            if (area.Width < 64 || area.Depth < 64)
                return [];
            return [.. Rects.Subtract(area, holes).Where(r => r.Width >= 64 && r.Depth >= 64)
                .Select(r => Brush.Box(new(r.X0, r.Y0, sill), new(r.X1, r.Y1, sill + standing.Depth), material))];
        }
    }
}
