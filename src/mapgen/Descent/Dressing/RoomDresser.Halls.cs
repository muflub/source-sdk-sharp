namespace SourceSharp.MapGen.Descent.Dressing;

/// <summary>
/// The dressing of a larger room (M24, the review of 0c79c6fc2: a great hall is the level's set
/// piece): what stands on its focus (an ore heap, a machine, a timber headframe), an overhead crane
/// on two girders, a run of pipe along a tall wall, and track through a yard hall from each socket
/// to its middle, every piece kept only where its socket is joined.
/// </summary>
public static partial class RoomDresser
{
    private sealed partial class Dresser
    {
        /// <summary>The theme's first centrepiece that fits standing on a larger room's focus, turned either way.</summary>
        private void Centrepiece(IReadOnlyList<PropModel> models)
        {
            if (room.Cells < 2)
                return;
            Point f = room.Focus;
            foreach (PropModel m in models)
                foreach (int yaw in (int[])[0, 90])
                    if (TryProp(m, new Point(f.X, f.Y, f.Z - m.Mins.Z), yaw, Role.Centre, floorStanding: true, group: false))
                        return;
        }

        /// <summary>How far a headframe's legs stand from the focus: on a plinth's lower tier, beside its top.</summary>
        private const float HeadframeReach = 232;

        /// <summary>
        /// A headframe of the theme's trim over a larger room's raised focus (a hall's plinth, level 3):
        /// four legs on the plinth's lower tier round its top, a cap frame under the roof and a girt
        /// half way, all clear of the top's own air.
        /// </summary>
        private void HeadframeOverFocus()
        {
            Point f = room.Focus;
            if (room.Cells < 2 || f.Z <= sill + RoomLint.Step)
                return;
            float ceil = room.Height - kit.WallDepth, top = Math.Min(ceil - 48, sill + 448), girt = (f.Z + top) / 2;
            const float leg = 24, beam = 24;
            float r = HeadframeReach;
            var pieces = new List<Box>();
            foreach (float sx in (float[])[-1, 1])
                foreach (float sy in (float[])[-1, 1])
                {
                    float x = f.X + sx * r, y = f.Y + sy * r;
                    if (solids.FloorBelow(x, y, f.Z) is not { } under)
                        return;
                    pieces.Add(new Box(new(x - leg / 2, y - leg / 2, under), new(x + leg / 2, y + leg / 2, top)));
                }

            foreach (float z in (float[])[girt, top])
            {
                pieces.Add(new Box(new(f.X - r - leg / 2, f.Y - r - leg / 2, z), new(f.X + r + leg / 2, f.Y - r + leg / 2, z + beam)));
                pieces.Add(new Box(new(f.X - r - leg / 2, f.Y + r - leg / 2, z), new(f.X + r + leg / 2, f.Y + r + leg / 2, z + beam)));
                pieces.Add(new Box(new(f.X - r - leg / 2, f.Y - r + leg / 2, z), new(f.X - r + leg / 2, f.Y + r - leg / 2, z + beam)));
                pieces.Add(new Box(new(f.X + r - leg / 2, f.Y - r + leg / 2, z), new(f.X + r + leg / 2, f.Y + r - leg / 2, z + beam)));
            }

            // All or nothing: a frame missing a leg reads as broken.
            if (pieces.Any(p => !StructureFits(p, solid: true)))
                return;
            foreach (Box p in pieces)
                AddStructure(p);
            solids = new RoomSolids(room, brushes);
        }

        /// <summary>
        /// An overhead crane (levels 2 and 4) in a larger room tall enough: two girders of the trim along
        /// x wall to wall at the gantry's height, a bridge girder across them over the focus, and the
        /// hook hung from the bridge's middle on a short cable.
        /// </summary>
        private void GantryOverFloor(Gantry g)
        {
            if (room.Cells < 2 || room.Height - kit.WallDepth < sill + g.Height + 2 * g.Girder + 64)
                return;
            Rect b = room.Body;
            float z0 = sill + g.Height, z1 = z0 + g.Girder, d = b.Y1 - b.Y0;
            float[] ys = [MathF.Round(b.Y0 + d * 0.28f), MathF.Round(b.Y1 - d * 0.28f)];
            var rails = new List<Box>();
            foreach (float y in ys)
            {
                var mid = new Point(room.Focus.X, y, (z0 + z1) / 2);
                if (solids.Ray(mid, 1, 0) is not { } r1 || solids.Ray(mid, -1, 0) is not { } r0)
                    continue;
                var box = new Box(new(mid.X - r0, y - 12, z0), new(mid.X + r1, y + 12, z1));
                if (StructureFits(box, solid: true))
                    rails.Add(box);
            }

            if (rails.Count != 2)
                return;
            float x = room.Focus.X;
            var bridge = new Box(new(x - 20, ys[0] - 12, z1), new(x + 20, ys[1] + 12, z1 + g.Girder));
            if (!StructureFits(bridge, solid: true))
                return;
            foreach (Box r in rails)
                AddStructure(r);
            AddStructure(bridge);
            solids = new RoomSolids(room, brushes);
            if (g.Hook is not { } hook || g.Cable is not { } cable)
                return;
            float cy = (ys[0] + ys[1]) / 2, cl = cable.Maxs.Z - cable.Mins.Z;
            var cableAt = new Point(x, cy, z1 - cl - cable.Mins.Z);
            var hookAt = new Point(x, cy, z1 - cl - hook.Maxs.Z);
            var cableBox = new Box(cableAt + cable.Mins, cableAt + cable.Maxs);
            var (hmins, hmaxs) = hook.Turned(0);
            var hookBox = new Box(hookAt + hmins, hookAt + hmaxs);
            if (Fits(cableBox, solid: false, floorStanding: false, Role.Cord, touching: true) && Fits(hookBox, solid: hook.Solid, floorStanding: false, Role.Cord, touching: true))
            {
                AddProp(cable, cableAt, 0, Role.Cord, shadows: false);
                placed.Add((cableBox, Role.Cord));
                AddProp(hook, hookAt, 0, Role.Cord, shadows: true);
                placed.Add((hookBox, Role.Cord));
            }
        }

        /// <summary>
        /// A second run of pipe along the longest flat wall of a tall room, at the set's
        /// <see cref="PropSet.WallRunHeight"/>: something at height between the floor and the roof.
        /// </summary>
        private void WallRun(PropModel run, int height)
        {
            if (room.Height < 768 || IsHallway(room))
                return;
            float length = run.Maxs.X - run.Mins.X, half = Math.Max(Math.Abs(run.Mins.Y), Math.Abs(run.Maxs.Y));
            var stretch = Stretches(height).Where(s => s.Count * RoomLint.Grid >= 2 * length).OrderByDescending(s => s.Count).FirstOrDefault();
            if (stretch is null)
                return;
            var s0 = stretch[0];
            bool alongX = s0.Dy != 0;
            var (start, span) = Extent(stretch);
            int pieces = (int)(span / length);
            float first = start + (span - pieces * length) / 2, z = sill + height - run.Mins.Z;
            for (int p = 0; p < pieces; p++)
            {
                float a0 = first + p * length;
                Point origin = alongX ? new(a0 - run.Mins.X, s0.Face.Y - s0.Dy * (half + 1), z) : new(s0.Face.X - s0.Dx * (half + 1), a0 - run.Mins.X, z);
                TryProp(run, origin, alongX ? 0 : 90, Role.CeilingRun, floorStanding: false);
            }
        }

        /// <summary>
        /// Track through a yard hall (level 2's larger rooms): from each east and west socket a run on its
        /// axis to the room's middle, from each north and south socket a spur, every piece kept only where
        /// its socket is joined. Two half-runs meeting in the middle are one line; a half-run whose other
        /// half is not there ends in a buffer stop kept only then; a blocked run ends in a stop.
        /// </summary>
        private void HallRails(PropModel rail, PropSet props)
        {
            var pieces = new[] { rail }.Concat(props.RailEnds).OrderByDescending(Length).ToArray();
            PropModel shortest = pieces[^1];
            float first = Length(shortest), e = room.Extent, cell = kit.Cell, m = e / 2;
            var sockets = room.Sockets.ToHashSet();
            for (int k = 0; k < room.Cells; k++)
            {
                float a = cell / 2 + k * cell;
                foreach (var (f0, f1, from0, from1, dx, dy) in new[]
                         {
                             (CellFace.West, CellFace.East, new Point(0, a, sill), new Point(e, a, sill), 1, 0),
                             (CellFace.South, CellFace.North, new Point(a, 0, sill), new Point(a, e, sill), 0, 1),
                         })
                {
                    bool spur = f0 == CellFace.South;
                    bool has0 = sockets.Contains((f0, k)), has1 = sockets.Contains((f1, k));
                    string n0 = "joined_" + room.SocketName(f0, k), n1 = "joined_" + room.SocketName(f1, k);
                    float reach = spur ? SpurLength : m;
                    float laid0 = has0 ? HalfRun(pieces, shortest, first, from0, dx, dy, n0, reach) : 0;
                    float laid1 = has1 ? HalfRun(pieces, shortest, first, from1, -dx, -dy, n1, reach) : 0;
                    if (props.RailStop is not { } stop)
                        continue;
                    bool through = !spur && laid0 >= m - 0.5f && laid1 >= m - 0.5f;
                    if (has0)
                        AddStop(stop, Along(from0, dx, dy, laid0), dx, dy, through ? $"{n0},!{n1}" : n0);
                    if (has1)
                        AddStop(stop, Along(from1, -dx, -dy, laid1), -dx, -dy, through ? $"{n1},!{n0}" : n1);
                }
            }
        }

        /// <summary>How far a yard hall's spur runs in from its north or south socket.</summary>
        public const float SpurLength = 384;

        /// <summary>One socket's run into a hall: its doorway piece, its buffer where capped, and its run, all under the socket's condition; returns how far it is laid.</summary>
        private float HalfRun(PropModel[] pieces, PropModel shortest, float first, Point from, int dx, int dy, string needs, float length)
        {
            var mid = new Point(from.X + dx * first / 2, from.Y + dy * first / 2, sill - shortest.Mins.Z);
            AddRail(shortest, mid, dx != 0 ? 90 : 0, needs);
            if (theme.Props.RailStop is { } stop)
                AddStop(stop, Along(from, dx, dy, first), dx, dy, "!" + needs);
            return Lay(pieces, from, dx, dy, first, length, needs);
        }
    }
}
