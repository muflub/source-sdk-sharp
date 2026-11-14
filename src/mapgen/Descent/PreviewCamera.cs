namespace SourceSharp.MapGen.Descent;

/// <summary>
/// Where a preview looks at a room from (the review of 2026-11-10: "at eye
/// height in a corner of the room's body looking across its diagonal toward
/// its main feature"): the corner of <see cref="Room.Body"/>, 112 in from
/// both walls, whose floor a player stands on and which is furthest from the
/// room's <see cref="Room.Focus"/>, at a player's eye above that floor, turned
/// to the focus. A corner a pillar or a crate stands in is walked in toward
/// the focus until its floor is clear. Room-local; <c>library.json</c> carries
/// it and <see cref="DescentLevel.Cameras"/> turns it with the room.
/// </summary>
public static class PreviewCamera
{
    /// <summary>A player's eye above the floor.</summary>
    public const int Eye = 64;

    /// <summary>How far a camera stands from any prop's origin.</summary>
    public const int PropClear = 160;

    /// <summary>How far in from the body's walls a corner camera stands.</summary>
    public const int Inset = 112;

    /// <summary>The camera of <paramref name="room"/>: its point (eye height) and its yaw, degrees counter-clockwise from +x.</summary>
    public static (Point At, float Yaw) Of(Room room)
    {
        // A larger room is one open floor: its body's south-west corner, looking at its middle.
        if (room.Cells > 1)
        {
            Rect r = room.Body;
            var at = new Point(r.X0 + Inset, r.Y0 + Inset, room.Kit.Sill + Eye);
            return (at, MathF.Round((MathF.Atan2(room.Focus.Y - at.Y, room.Focus.X - at.X) * 180 / MathF.PI + 360) % 360));
        }

        var columns = new RoomColumns(room);
        // The dressing's props: a camera keeps clear of them, or a duct bend in the corner fills its view.
        var props = room.Entities.Where(e => e.ClassName is "prop_static" or "prop_dynamic" && e.Origin is not null).Select(e => e.Origin!.Value).ToArray();
        var floor = RoomLint.WalkedFloor(room, columns).ToDictionary(s => (s.I, s.J));
        Rect b = room.Body;
        Point focus = room.Focus;
        var best = ((Point At, float Far)?)null;
        foreach (var (cx, cy) in ((float, float)[])[(b.X0 + Inset, b.Y0 + Inset), (b.X1 - Inset, b.Y0 + Inset), (b.X1 - Inset, b.Y1 - Inset), (b.X0 + Inset, b.Y1 - Inset)])
        {
            // From the corner toward the focus until a player stands on clear floor.
            float dx = focus.X - cx, dy = focus.Y - cy, length = MathF.Sqrt(dx * dx + dy * dy);
            for (float d = 0; d < length - 128; d += 16)
            {
                float x = cx + dx * d / Math.Max(length, 1), y = cy + dy * d / Math.Max(length, 1);
                var key = ((int)MathF.Floor(x / RoomLint.Grid), (int)MathF.Floor(y / RoomLint.Grid));
                if (!floor.TryGetValue(key, out var s) || s.Z - room.Kit.Sill > RoomLint.Step || !columns.HullFits(s) || !SeesFocus(columns, x, y, s.Z + Eye, focus)
                    || props.Any(q => (q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y) < PropClear * PropClear))
                    continue;
                float far = MathF.Sqrt((focus.X - x) * (focus.X - x) + (focus.Y - y) * (focus.Y - y));
                if (best is not { } bb || far > bb.Far + 0.5f)
                    best = (new Point(MathF.Round(x), MathF.Round(y), s.Z + Eye), far);
                break;
            }
        }

        // No corner sees the focus: the room's first door, a player's height in, looking at the focus.
        if (best is null && room.Sockets.FirstOrDefault() is var (face, offset) && room.Doors.Count > 0)
        {
            Point d = room.DoorInside(face, offset, 112);
            float y0 = MathF.Atan2(focus.Y - d.Y, focus.X - d.X) * 180 / MathF.PI;
            return (d with { Z = room.Kit.Sill + Eye }, MathF.Round((y0 + 360) % 360));
        }

        // No corner stands clear: the body's middle, looking along +x.
        if (best is not { } chosen)
            return (new Point(b.CentreX, b.CentreY, room.Kit.Sill + Eye), 0);
        float yaw = MathF.Atan2(focus.Y - chosen.At.Y, focus.X - chosen.At.X) * 180 / MathF.PI;
        return (chosen.At, MathF.Round((yaw + 360) % 360));
    }

    /// <summary>Whether nothing solid stands between an eye at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) and the focus, a hand above its floor, sampled every 16.</summary>
    private static bool SeesFocus(RoomColumns columns, float x, float y, float z, Point focus)
    {
        float tz = focus.Z + Eye, dx = focus.X - x, dy = focus.Y - y, length = MathF.Sqrt(dx * dx + dy * dy);
        for (float d = 16; d < length; d += 16)
        {
            float px = x + dx * d / length, py = y + dy * d / length, pz = z + (tz - z) * d / length;
            if (columns.Solids.Any(b => b.Mins.X < px && b.Maxs.X > px && b.Mins.Y < py && b.Maxs.Y > py && b.Column(px, py) is { } c && c.Low < pz && c.High > pz))
                return false;
        }

        return true;
    }
}
