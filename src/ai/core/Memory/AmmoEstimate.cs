namespace SourceSharp.Ai;

/// <summary>
/// What a bot believes of one opponent's clip (plan_hl2mp_ai §5.1, ruling
/// 2026-11-01): a range [<see cref="Min"/>, <see cref="Max"/>] and a histogram
/// over 0..clip, moved only by what the bot perceived. Unknown on first
/// sight (the whole range, flat); each perceived shot shifts it down; a
/// perceived reload fills it at the reload's end; a gap in perception widens
/// it by the weapon's cadence and, past a reload's length, back to unknown.
/// It never snaps to the truth: nothing here can read it. Fixed size.
/// </summary>
public sealed class AmmoEstimate
{
    /// <summary>The largest clip the histogram holds (the SMG1's 45 and room to spare).</summary>
    public const int MaxClip = 64;

    private readonly float[] _p = new float[MaxClip + 1];

    /// <summary>The weapon kind the estimate is about, or -1 before any.</summary>
    public int Weapon { get; private set; } = -1;

    public int Clip { get; private set; }

    public int Min { get; private set; }

    public int Max { get; private set; }

    public bool Known => Weapon >= 0 && Clip > 0;

    public bool ReloadInProgress { get; private set; }

    /// <summary>When the perceived reload ends; open-ended (<see cref="int.MaxValue"/>) for a shell-by-shell one until its end is perceived.</summary>
    public Tick ReloadEndTick { get; private set; }

    public Tick ReloadStartTick { get; private set; }

    public Tick LastShotTick { get; private set; }

    /// <summary>The last tick anything moved this estimate (the gap is counted from here).</summary>
    public Tick LastPerceivedTick { get; private set; }

    /// <summary>Rounds perceived since the last perceived reload.</summary>
    public int ShotsSinceReload { get; private set; }

    /// <summary>At the last perceived reload's start: the share of the clip shot since the one before (the reload style, §5.1).</summary>
    public float LastReloadSpent { get; private set; } = float.NaN;

    /// <summary>The probability mass at <paramref name="rounds"/>.</summary>
    public float P(int rounds) => (uint)rounds <= (uint)Clip ? _p[rounds] : 0f;

    /// <summary>P(empty): the mass at zero.</summary>
    public float PEmpty => Known ? _p[0] : 0f;

    public float Width => Max - Min;

    /// <summary>Forgets everything: the opponent respawned, or the bot did.</summary>
    public void Reset()
    {
        Weapon = -1;
        Clip = 0;
        Min = Max = 0;
        ReloadInProgress = false;
        ShotsSinceReload = 0;
        LastReloadSpent = float.NaN;
        _counted = 0;
        Array.Clear(_p);
    }

    /// <summary>
    /// The opponent is seen holding <paramref name="weapon"/>: a weapon the
    /// estimate was not about starts unknown, the whole clip, flat.
    /// </summary>
    public void SeeWeapon(in WeaponFacts weapon, Tick tick)
    {
        if (weapon.Kind == Weapon || !weapon.HasClip)
        {
            if (!weapon.HasClip && weapon.Kind != Weapon)
            {
                Reset();
                Weapon = weapon.Kind;
            }

            return;
        }

        Reset();
        Weapon = weapon.Kind;
        Clip = Math.Min(weapon.ClipSize, MaxClip);
        Min = 0;
        Max = Clip;
        Flat(0, Clip);
        Perceived(tick);
    }

    /// <summary><paramref name="rounds"/> perceived shots (heard or seen): the range and the mass shift down.</summary>
    public void Shot(in WeaponFacts weapon, int rounds, Tick tick)
    {
        if (weapon.Kind != Weapon)
            SeeWeapon(weapon, tick);
        if (!Known || rounds <= 0)
            return;

        // A whole-clip reload perceived under way is over once it fires; and a
        // shot the estimate has no round for says a reload went unperceived.
        if ((ReloadInProgress && !weapon.ShellByShell) || rounds > Max)
            ReloadFinished(tick);

        // A shot or so in ten goes unheard in a fight: the low end keeps room
        // for one and a tenth of those counted since the clip was last known.
        Max = Math.Max(0, Max - rounds);
        _counted += rounds;
        int room = 1 + (int)MathF.Ceiling(MissShare * _counted);
        Min = Math.Max(0, Math.Min(Min - rounds, Max - room));
        Ramp(Min, Max);
        ShotsSinceReload += rounds;
        LastShotTick = tick;
        Perceived(tick);
    }

    private readonly float[] _scratch = new float[MaxClip + 1];
    private int _counted;

    /// <summary>The mass over [lo, hi], rising toward hi: the counted shots are the likeliest, a missed one or two less so.</summary>
    private void Ramp(int lo, int hi)
    {
        Array.Clear(_p);
        float sum = 0f;
        for (int k = lo; k <= hi; k++)
            sum += _p[k] = k - lo + 1;
        for (int k = lo; k <= hi; k++)
            _p[k] /= sum;
    }

    /// <summary>The share of a fight's shots a listener may miss: the range's low end gives that much room.</summary>
    public const float MissShare = 0.1f;

    /// <summary>The mass from 0 up to <paramref name="upTo"/> inclusive (all of it lands on empty).</summary>
    private float SumFrom(int upTo)
    {
        float s = 0f;
        for (int k = 0; k <= upTo; k++)
            s += _p[k];
        return s;
    }

    /// <summary>A perceived dry click: the clip is empty.</summary>
    public void DryFire(in WeaponFacts weapon, Tick tick)
    {
        if (weapon.Kind != Weapon)
            SeeWeapon(weapon, tick);
        if (!Known)
            return;
        Min = Max = 0;
        _counted = 0;
        Array.Clear(_p);
        _p[0] = 1f;
        Perceived(tick);
    }

    /// <summary>
    /// A perceived reload's start: in progress until its end, which for a
    /// whole-clip reload is the weapon's reload length later, and for a
    /// shell-by-shell one is open until its end is perceived.
    /// </summary>
    public void ReloadStarted(in WeaponFacts weapon, Tick tick, float tickSeconds)
    {
        if (weapon.Kind != Weapon)
            SeeWeapon(weapon, tick);
        if (!Known)
            return;
        LastReloadSpent = Clip > 0 ? Math.Clamp(ShotsSinceReload / (float)Clip, 0f, 1f) : float.NaN;
        ReloadInProgress = true;
        ReloadStartTick = tick;
        ReloadEndTick = weapon.ShellByShell ? new Tick(int.MaxValue) : tick + VisionParams.Ticks(weapon.ReloadSeconds, tickSeconds);
        Perceived(tick);
    }

    /// <summary>A perceived shell: one more round in (a shotgun's reload sound is one a shell).</summary>
    public void Shell(in WeaponFacts weapon, int rounds, Tick tick, float tickSeconds)
    {
        if (!ReloadInProgress)
            ReloadStarted(weapon, tick, tickSeconds);
        if (!Known)
            return;
        Min = Math.Min(Clip, Min + rounds);
        Max = Math.Min(Clip, Max + rounds);
        for (int k = Clip; k >= 0; k--)
        {
            int from = k - rounds;
            _scratch[k] = k == Clip ? SumAbove(Math.Max(0, Clip - rounds)) : (from >= 0 ? _p[from] : 0f);
        }

        Array.Copy(_scratch, _p, Clip + 1);
        Normalise();
        Perceived(tick);
    }

    private float SumAbove(int from)
    {
        float s = 0f;
        for (int k = from; k <= Clip; k++)
            s += _p[k];
        return s;
    }

    /// <summary>A perceived reload's end (or its due end): the clip is full.</summary>
    public void ReloadFinished(Tick tick)
    {
        if (!Known)
            return;
        ReloadInProgress = false;
        _counted = 0;
        Min = Max = Clip;
        Array.Clear(_p);
        _p[Clip] = 1f;
        ShotsSinceReload = 0;
        Perceived(tick);
    }

    /// <summary>
    /// Time passes (§5.1): a whole-clip reload in progress ends when due;
    /// unperceived, the range widens: down by the shots the cadence allows in
    /// the gap, and up to a full clip once the gap could hold a reload; the
    /// mass blends toward flat by the same share.
    /// </summary>
    /// <param name="inHearing">
    /// Whether the bot would hear the opponent fire (it heard or saw it lately):
    /// then silence is evidence, so the range does not widen down, and an
    /// audible reload not heard did not happen.
    /// </param>
    public void Advance(in WeaponFacts weapon, Tick now, float tickSeconds, bool inHearing = false)
    {
        if (!Known || weapon.Kind != Weapon)
            return;
        if (ReloadInProgress && now >= ReloadEndTick)
            ReloadFinished(ReloadEndTick);
        if (ReloadInProgress)
            return;

        float gap = (now - LastPerceivedTick) * tickSeconds;
        if (gap <= _widenedGap)
            return;

        int couldFire = inHearing ? 0 : weapon.CadenceSeconds > 0f ? (int)(gap / weapon.CadenceSeconds) : Clip;
        // Time only widens: what an earlier gap opened stays open.
        int lo = Math.Min(Min, Math.Max(0, _perceivedMin - couldFire));
        int hi = Math.Max(Max, gap >= weapon.ReloadSeconds && !(inHearing && weapon.ReloadAudible) ? Clip : _perceivedMax);
        if (lo == Min && hi == Max)
        {
            _widenedGap = gap;
            return;
        }
        float share = Math.Clamp((gap - _widenedGap) / (2f * Math.Max(weapon.ReloadSeconds, 0.1f)), 0f, 1f);
        _widenedGap = gap;

        // Blend toward flat over the widened range by the share of the gap.
        float flat = 1f / (hi - lo + 1);
        for (int k = 0; k <= Clip; k++)
            _p[k] = (1f - share) * _p[k] + (k >= lo && k <= hi ? share * flat : 0f);
        Normalise();
        Min = lo;
        Max = hi;
    }

    private int _perceivedMin, _perceivedMax;
    private float _widenedGap;

    /// <summary>Marks the estimate as just perceived: the gap starts here, from this range.</summary>
    private void Perceived(Tick tick)
    {
        LastPerceivedTick = tick;
        _perceivedMin = Min;
        _perceivedMax = Max;
        _widenedGap = 0f;
    }

    private void Flat(int lo, int hi)
    {
        Array.Clear(_p);
        float f = 1f / (hi - lo + 1);
        for (int k = lo; k <= hi; k++)
            _p[k] = f;
    }

    private void Normalise()
    {
        float s = 0f;
        for (int k = 0; k <= Clip; k++)
            s += _p[k];
        if (s <= 0f)
        {
            Flat(Min, Max);
            return;
        }

        for (int k = 0; k <= Clip; k++)
            _p[k] /= s;
    }
}
