using SkiaSharp;
using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Replay;

/// <summary>One frame of the beliefs: at a tick, a bot's summed mass over its unseen opponents, by cell.</summary>
public sealed record HeatFrame(int Tick, int Bot, Dictionary<int, float> Mass);

/// <summary>
/// <c>ai-replay heat</c> (plan_hl2mp_ai §7.1, the belief map offline): a
/// recording replayed with the brains remembering over the map's graph, and
/// every <c>every</c> ticks each bot's beliefs drawn as heat over the graph.
/// </summary>
public static class HeatPng
{
    /// <summary>Replays <paramref name="recording"/> over <paramref name="world"/> and collects a frame per bot every <paramref name="every"/> ticks.</summary>
    public static List<HeatFrame> Collect(Stream recording, MemoryWorld world, int every)
    {
        var frames = new List<HeatFrame>();
        AiReplayer.Replay(recording, new InlineTransport(), world, (session, tick) =>
        {
            if (tick.Value % every != 0)
                return;
            for (int slot = 0; slot < session.Snapshots.Slots; slot++)
            {
                if (session.BrainIn(slot)?.Memory is not { } m)
                    continue;
                var mass = new Dictionary<int, float>();
                for (int s = 0; s < m.Board.Slots; s++)
                {
                    KnownActor a = m.Board.Actor(s);
                    if (!a.Known || a.InSight || a.Where is not { Active: true } w)
                        continue;
                    for (int c = 0; c < world.Graph!.CellCount; c++)
                    {
                        float p = w.P(c);
                        if (p > 0f)
                            mass[c] = mass.GetValueOrDefault(c) + p;
                    }
                }

                frames.Add(new HeatFrame(tick.Value, slot, mass));
            }
        });
        return frames;
    }

    /// <summary>The graph, dimmed, with the frame's mass over it: brighter and redder where the mass is.</summary>
    public static byte[] Render(DerivedNav nav, HeatFrame frame, float pixelsPerUnit)
    {
        using SKBitmap bitmap = NavPng.Draw(nav, pixelsPerUnit);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 150) };
            canvas.DrawRect(0, 0, bitmap.Width, bitmap.Height, dim);
            NavGraph g = nav.Graph;
            float minX = float.MaxValue, maxY = float.MinValue;
            for (int c = 0; c < g.CellCount; c++)
            {
                minX = MathF.Min(minX, g.Cell(c).MinX);
                maxY = MathF.Max(maxY, g.Cell(c).MaxY);
            }

            float top = frame.Mass.Count == 0 ? 1f : frame.Mass.Values.Max();
            using var fill = new SKPaint { IsAntialias = false };
            foreach ((int c, float p) in frame.Mass)
            {
                NavCell cell = g.Cell(c);
                float t = Math.Clamp(p / top, 0f, 1f);
                fill.Color = new SKColor((byte)(80 + 175 * t), (byte)(40 + 60 * t), (byte)(160 * (1 - t)));
                float x = NavPng.Margin + (cell.MinX - minX) * pixelsPerUnit, y = NavPng.Margin + (maxY - cell.MaxY) * pixelsPerUnit;
                canvas.DrawRect(SKRect.Create(x, y, MathF.Max(1f, (cell.MaxX - cell.MinX) * pixelsPerUnit), MathF.Max(1f, (cell.MaxY - cell.MinY) * pixelsPerUnit)), fill);
            }

            canvas.Flush();
        }

        using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
