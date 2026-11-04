using System.Globalization;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// How far each bot goes each tick (user ruling 2026-11-03: bots waiting for the
/// budget must still move smoothly, measured): a histogram of the steps of bots
/// moving, and of the runs of ticks a bot stood still between two steps. A bot
/// that waited a tick stands for it and takes two steps' way the next; a client
/// draws that by its simulation time, as it does any client's late packet.
/// </summary>
public sealed class BotStepMeter
{
    /// <summary>One bucket a unit up to this; past it is no step but a teleport or a respawn, and is not counted.</summary>
    public const int MaxStep = 40;

    /// <summary>Runs of still ticks are counted to this; a longer one is a bot standing, not a hiccup.</summary>
    public const int MaxRun = 8;

    private readonly long[] _steps = new long[MaxStep + 1];
    private readonly long[] _runs = new long[MaxRun + 1];
    private readonly AiVector?[] _last = new AiVector?[VisionPass.Slots];
    private readonly int[] _still = new int[VisionPass.Slots];

    /// <summary>A step smaller than this is standing.</summary>
    public const float Still = 0.05f;

    public void Note(ReadOnlySpan<SensedBot> bots, VisionPass vision)
    {
        foreach (ref readonly SensedBot b in bots)
        {
            int slot = b.Id.Slot;
            if (vision.ReadOf(slot) is not { } read)
            {
                _last[slot] = null;
                _still[slot] = 0;
                continue;
            }

            AiVector? was = _last[slot];
            _last[slot] = read.Origin;
            if (was is null)
                continue;
            float step = (read.Origin - was.Value).Length2D;
            if (step < Still)
            {
                _still[slot]++;
                continue;
            }

            if (step > MaxStep)
            {
                _still[slot] = 0;
                continue;
            }

            _steps[(int)step]++;
            int run = _still[slot];
            if (run is > 0 and <= MaxRun)
                _runs[run]++;
            _still[slot] = 0;
        }
    }

    public void Reset()
    {
        Array.Clear(_steps);
        Array.Clear(_runs);
    }

    /// <summary>The steps by whole units, and the still runs of 1 to <see cref="MaxRun"/> ticks.</summary>
    public string Json() => string.Create(CultureInfo.InvariantCulture, $"{{\"steps\":[{string.Join(',', _steps)}],\"still_runs\":[{string.Join(',', _runs)}]}}");
}
