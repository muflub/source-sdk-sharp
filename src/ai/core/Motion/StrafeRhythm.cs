namespace SourceSharp.Ai;

/// <summary>
/// A fight's sidestep (plan_hl2mp_ai §3.4: aperiodic, by Agility), lifted from the HL2MP decider for
/// every game's: a stretch at a time, each drawn when the last ends, after a wait of mean
/// <c>strafeMeanSeconds</c>; with chance <c>strafeWeight</c> the stretch is a sidestep to a side, else
/// none. Drawn by the stretch, not by the decide: on and off ten times a second the body jerks like
/// nobody's does. Per bot; its three draws a stretch (the side, whether it steps, the wait) come from
/// the stream it is given, in that order, so a decider that drew them itself draws the same.
///
/// <para>A <see cref="StrafeStyle"/> shapes the stretches. <see cref="StrafeStyle.Coin"/> is HL2MP's:
/// each side a coin's, any wait of four ticks or more. A person's keys (lane Gfight, 2026-11-15) hold
/// a side at least <see cref="StrafeStyle.LeastSeconds"/> (a reversal of a run takes about a tenth of
/// a second to show at all) and mostly go the other way when they change
/// (<see cref="StrafeStyle.ReverseChance"/>), so the side changes about as often as the stretches end.</para>
/// </summary>
public sealed class StrafeRhythm(AiRandom random)
{
    /// <summary>The shortest stretch, in ticks, whatever the draw: HL2MP's.</summary>
    public const int LeastTicks = 4;

    private readonly AiRandom _random = random;
    private int _side = 1;
    private bool _stepping;
    private Tick _until;

    /// <summary>The side this stretch steps to: 1 the view's left of the way to the target, -1 its right, 0 a stretch with no sidestep.</summary>
    public int Side => _stepping ? _side : 0;

    /// <summary>When this stretch ends.</summary>
    public Tick Until => _until;

    /// <summary>Stretches drawn, and the draws that changed the side stepped to.</summary>
    public int Stretches { get; private set; }

    public int Changes { get; private set; }

    /// <summary>
    /// One decide at <paramref name="now"/>: a new stretch when the last has ended, of
    /// <paramref name="weight"/>'s chance to step and a wait of mean <paramref name="meanSeconds"/>
    /// shaped by <paramref name="style"/>. Answers <see cref="Side"/>.
    /// </summary>
    public int Step(Tick now, float tickSeconds, float weight, float meanSeconds, in StrafeStyle style)
    {
        if (now < _until)
            return Side;
        int was = Side;
        float side = _random.NextFloat();
        _side = float.IsNaN(style.ReverseChance) ? (side < 0.5f ? -1 : 1) : side < style.ReverseChance ? -_side : _side;
        _stepping = _random.NextFloat() < weight;
        float u = Math.Max(_random.NextFloat(), 1e-4f);
        float least = Math.Max(0f, style.LeastSeconds);
        float wait = least + -MathF.Log(u) * Math.Max(0f, meanSeconds - least);
        _until = now + Math.Max(LeastTicks, (int)(wait / tickSeconds));
        Stretches++;
        Changes += Side != was ? 1 : 0;
        return Side;
    }

    /// <summary>
    /// The stretch goes on to the other side, its end unchanged, and counts as a change of side: what a
    /// person does at a wall (lane Gmove). No draw, so the stream is as it was. A stretch with no
    /// sidestep has no side to turn.
    /// </summary>
    public void Turn()
    {
        if (!_stepping)
            return;
        _side = -_side;
        Changes++;
    }

    /// <summary>The stretch ends now: the next decide draws a new one (a life's end, a fight's).</summary>
    public void End() => _until = default;
}

/// <summary>
/// The shape of a <see cref="StrafeRhythm"/>'s stretches: the least a stretch lasts (a wait's floor;
/// its mean stays the curve's), and the chance a new stretch steps the other way from the last
/// (NaN: a fresh coin for the side).
/// </summary>
public readonly record struct StrafeStyle(float LeastSeconds, float ReverseChance)
{
    /// <summary>HL2MP's: a coin for each side, no floor but the rhythm's four ticks.</summary>
    public static StrafeStyle Coin => new(0f, float.NaN);
}
