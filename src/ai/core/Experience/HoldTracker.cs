namespace SourceSharp.Ai;

/// <summary>A place one player held: as what, where (the middle of its samples), the yaw it faced most, for how long, the kills it made there, whether it died there, and whether a bot played it.</summary>
public readonly record struct HeldSpot(ExperienceContext Context, AiVector At, float Yaw, double Seconds, double Kills, double Deaths, bool Bot);

/// <summary>
/// Finds where players hold (plan_bots_teams_tf2_part_g G.3's <c>spot</c> rows of the kind "hold", lane G4b): a run of one
/// living player's samples within <see cref="Radius"/> of the run's first place, as one context, lasting at least
/// <see cref="MinSeconds"/>. A run ends when the player leaves the radius, changes side or class, dies (a death there), is
/// not sampled (dead, gone, off every side) or the round stops being gathered; a run of <see cref="MaxSeconds"/> is handed
/// over and a new one begins where the player stands. Its kills are the player's while it held.
///
/// <para>It is told players by their slot, which it keeps only to join a slot's samples into a run: what it hands over says
/// nothing of who. Pure and deterministic: the same calls in the same order give the same spots. One thread (the server's
/// game thread).</para>
/// </summary>
public sealed class HoldTracker
{
    /// <summary>How far from where a hold began a player may move and still hold it: a sidestep and a shuffle, two bins.</summary>
    public const float Radius = 128f;

    /// <summary>The least a run lasts to be a hold: longer than a pause at a door.</summary>
    public const double MinSeconds = 4d;

    /// <summary>A run this long is handed over and begun again, so one player standing all round is many holds, not one huge one.</summary>
    public const double MaxSeconds = 30d;

    private struct Run
    {
        public bool On, Sampled;
        public AiVector First, Sum;
        public int Samples;
        public double Seconds, Kills;
        public ExperienceContext Context;
        public bool Bot;
        public int[] Yaws;
    }

    private Run[] _runs = [];

    /// <summary>Holds handed over since the tracker was made.</summary>
    public long Held { get; private set; }

    /// <summary>A sampled frame begins: a slot not sampled before <see cref="EndFrame"/> has left its run.</summary>
    public void BeginFrame()
    {
        for (int i = 0; i < _runs.Length; i++)
            _runs[i].Sampled = false;
    }

    /// <summary>
    /// The player in <paramref name="slot"/> is alive at <paramref name="at"/>, facing <paramref name="yaw"/>, as
    /// <paramref name="context"/>, for the <paramref name="seconds"/> since the last frame. A run it leaves is added to
    /// <paramref name="ended"/> when it was a hold.
    /// </summary>
    public void Sample(int slot, AiVector at, float yaw, ExperienceContext context, bool bot, double seconds, List<HeldSpot> ended)
    {
        ArgumentNullException.ThrowIfNull(ended);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (slot >= _runs.Length)
            Array.Resize(ref _runs, Math.Max(slot + 1, _runs.Length * 2));
        ref Run run = ref _runs[slot];
        run.Sampled = true;
        if (run.On && (run.Context != context || run.Bot != bot || (at - run.First).Length2D > Radius || MathF.Abs(at.Z - run.First.Z) > WorldBin.Height))
            End(ref run, deaths: 0d, ended);
        if (!run.On)
        {
            Begin(ref run, at, yaw, context, bot);
            return;
        }

        run.Seconds += Math.Max(0d, seconds);
        run.Sum += at;
        run.Samples++;
        run.Yaws[LookBuckets.Yaw(yaw)]++;
        if (run.Seconds >= MaxSeconds)
        {
            End(ref run, deaths: 0d, ended);
            Begin(ref run, at, yaw, context, bot);
        }
    }

    /// <summary>A sampled frame is over: each slot not sampled in it has left its run.</summary>
    public void EndFrame(List<HeldSpot> ended)
    {
        for (int i = 0; i < _runs.Length; i++)
        {
            if (_runs[i].On && !_runs[i].Sampled)
                End(ref _runs[i], deaths: 0d, ended);
        }
    }

    /// <summary>The player in <paramref name="slot"/> killed someone: one more kill for the place it holds.</summary>
    public void Kill(int slot)
    {
        if ((uint)slot < (uint)_runs.Length && _runs[slot].On)
            _runs[slot].Kills++;
    }

    /// <summary>The player in <paramref name="slot"/> died: its run ends with a death there.</summary>
    public void Died(int slot, List<HeldSpot> ended)
    {
        if ((uint)slot < (uint)_runs.Length && _runs[slot].On)
            End(ref _runs[slot], deaths: 1d, ended);
    }

    /// <summary>The round stops being gathered: every run ends where it is.</summary>
    public void EndAll(List<HeldSpot> ended)
    {
        for (int i = 0; i < _runs.Length; i++)
        {
            if (_runs[i].On)
                End(ref _runs[i], deaths: 0d, ended);
        }
    }

    /// <summary>The level is over: every run is dropped, none handed over.</summary>
    public void Clear() => _runs = [];

    private static void Begin(ref Run run, AiVector at, float yaw, ExperienceContext context, bool bot)
    {
        run.On = true;
        run.First = run.Sum = at;
        run.Samples = 1;
        run.Seconds = run.Kills = 0d;
        run.Context = context;
        run.Bot = bot;
        if (run.Yaws is null)
            run.Yaws = new int[LookBuckets.YawCount];
        else
            Array.Clear(run.Yaws);
        run.Yaws[LookBuckets.Yaw(yaw)]++;
    }

    private void End(ref Run run, double deaths, List<HeldSpot> ended)
    {
        run.On = false;
        if (run.Seconds < MinSeconds)
            return;
        int yaw = 0;
        for (int i = 1; i < run.Yaws.Length; i++)
        {
            if (run.Yaws[i] > run.Yaws[yaw])
                yaw = i;
        }

        ended.Add(new HeldSpot(run.Context, run.Sum * (1f / run.Samples), LookBuckets.YawCentre(yaw), run.Seconds, run.Kills, deaths, run.Bot));
        Held++;
    }
}
