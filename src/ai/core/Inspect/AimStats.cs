namespace SourceSharp.Ai;

/// <summary>The distance bands hit rates are told in: the same three as <c>ai-replay metrics</c>'.</summary>
public enum AimBand : byte
{
    Near = 0,
    Mid,
    Far,
}

/// <summary>Which band a distance falls in, and its name: under 300, 300-800, over 800 units.</summary>
public static class AimBands
{
    public const float NearBelow = 300f, FarAbove = 800f;

    public static AimBand Of(float distance) => distance < NearBelow ? AimBand.Near : distance > FarAbove ? AimBand.Far : AimBand.Mid;

    public static string Name(AimBand band) => band switch
    {
        AimBand.Near => "under300",
        AimBand.Mid => "300-800",
        _ => "over800",
    };
}

/// <summary>
/// When a shot was aimed at someone (user ruling 2026-11-02: "make sure to only
/// count when i was shooting at bots, not things in the world"): the crosshair
/// within the target's body's angular half-width plus <see cref="MarginDeg"/>.
/// One rule for a recording's metrics and the aim meter, a person's and a bot's,
/// so Medium is compared like for like.
/// </summary>
public static class AimCone
{
    /// <summary>The margin past the body, degrees: room for a person's aim on a target and no more.</summary>
    public const double MarginDeg = 3;

    /// <summary>Half a player's width, units.</summary>
    public const float BodyHalfWidth = 16f;

    /// <summary>The margins the metrics also print a rate at, so the choice can be seen.</summary>
    public static readonly System.Collections.Immutable.ImmutableArray<double> SensitivityMarginsDeg = [3, 6, 10, 30];

    /// <summary>The angle half a player's width takes up at <paramref name="distance"/>.</summary>
    public static double HalfWidthDeg(float distance) => Math.Atan2(BodyHalfWidth, Math.Max(distance, 1f)) * (180.0 / Math.PI);

    /// <summary>The angle from a view at <paramref name="eye"/> to the body from <paramref name="origin"/> up to <paramref name="bodyEye"/>, at the shooter's own height on it.</summary>
    public static double AngleToBody(AiAngles view, AiVector eye, AiVector origin, AiVector bodyEye)
    {
        AiVector axis = bodyEye - origin;
        float len2 = axis.LengthSquared;
        float u = len2 > 0 ? Math.Clamp((eye - origin).Dot(axis) / len2, 0f, 1f) : 0f;
        AiVector to = (origin + axis * u - eye).Normalised();
        return Math.Acos(Math.Clamp(VisionMath.Forward(view).Dot(to), -1f, 1f)) * (180.0 / Math.PI);
    }

    /// <summary>Whether a crosshair <paramref name="angleDeg"/> off a body at <paramref name="distance"/> is aimed at it.</summary>
    public static bool Holds(double angleDeg, float distance) => angleDeg - HalfWidthDeg(distance) <= MarginDeg;
}

/// <summary>What a bot's aim did about one other player on one tick (see <see cref="AimStats"/>).</summary>
/// <param name="Clear">The player is in the bot's field and a sight line is clear, recognised or not.</param>
/// <param name="Sees">The bot holds the player in sight: recognised and clear.</param>
/// <param name="Chosen">The player is the one the bot's brain is about.</param>
/// <param name="ErrorDeg">The angle from the crosshair to the player's chest.</param>
/// <param name="OnBody">The crosshair's ray passes through the player's box.</param>
/// <param name="AttackHeld">The bot's command holds fire.</param>
/// <param name="Leaf">The brain's leaf.</param>
/// <param name="Distance">From the bot's eye to the player's chest.</param>
/// <param name="Aimed">The crosshair is within <see cref="AimCone"/> of the player's body.</param>
public readonly record struct AimSample(bool Clear, bool Sees, bool Chosen, float ErrorDeg, bool OnBody, bool AttackHeld, string Leaf, float Distance, bool Aimed = true);

/// <summary>
/// The aim meter (plan_hl2mp_ai §12.3, the measurement behind the skills'
/// aim curves): for one bot and each other player, how long the player was in
/// sight, what the bot did meanwhile, where its crosshair was, how soon after
/// the player came into view it first fired and how the shots landed. Pure:
/// the binding feeds it what the game did. Fixed size by target slot. A shot is
/// the metrics' (<see cref="AimCone"/>): it goes to the sighted player within the
/// cone of the crosshair when it was fired; with nobody there it goes to slot 0,
/// the "no target" row, in no rate. Which player the brain was about only
/// says which row the in-sight numbers and the first-shot time go to.
/// </summary>
public sealed class AimStats
{
    /// <summary>A player out of view this long ends the engagement: back in view is a new one (the vision's lost-sight time, 0.5 s).</summary>
    public const int EngagementGapTicks = 33;

    /// <summary>Shots in an engagement told apart by their order; later ones share the last.</summary>
    public const int ShotOrders = 8;

    /// <summary>The first-shot and acquire times kept per player.</summary>
    public const int MaxTimes = 512;

    private readonly Row[] _rows;
    private int _current = -1;
    private int _currentAt;
    private int _aimed = -1;
    private int _aimedAt = -1;
    private float _aimedError, _aimedDistance;
    private bool _lastShotAimed;
    private int _lastShotTick = -1;
    private float _lastShotDistance;
    private readonly List<int> _pendingHits = new();
    private int _pendingTick = -1;
    private int _lastWeapon = -1;
    private int _sideSign, _sideAt, _yawSign;

    public AimStats(int slots)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots);
        _rows = new Row[slots];
        for (int i = 0; i < slots; i++)
            _rows[i] = new Row();
    }

    public int Slots => _rows.Length;

    /// <summary>The row of the player in <paramref name="slot"/>; slot 0 holds shots fired at nobody in sight.</summary>
    public Row Of(int slot) => _rows[slot];

    /// <summary>One tick's reading of the player in <paramref name="slot"/>.</summary>
    public void Observe(int slot, int tick, in AimSample s)
    {
        Settle(tick);
        Row r = _rows[slot];
        if (s.Clear)
        {
            r.ClearTicks++;
            if (!r.Engaged)
            {
                r.Engaged = true;
                r.EngagedAt = tick;
                r.Fired = false;
                r.Acquired = false;
                r.ShotOrder = 0;
                r.Engagements++;
            }

            r.GapTicks = 0;
        }
        else if (r.Engaged && ++r.GapTicks >= EngagementGapTicks)
        {
            r.Engaged = false;
        }

        if (s.Sees && s.Aimed && (_aimedAt != tick || s.ErrorDeg < _aimedError))
        {
            _aimed = slot;
            _aimedAt = tick;
            _aimedError = s.ErrorDeg;
            _aimedDistance = s.Distance;
        }

        if (!s.Sees || !s.Chosen)
            return;

        _current = slot;
        _currentAt = tick;
        r.InSightTicks++;
        if (s.AttackHeld)
            r.FiringTicks++;
        r.ErrorSum += s.ErrorDeg;
        r.ErrorSquares += (double)s.ErrorDeg * s.ErrorDeg;
        if (s.OnBody)
        {
            r.OnBodyTicks++;
            if (r.Engaged && !r.Acquired)
            {
                r.Acquired = true;
                if (r.AcquireTicks.Count < MaxTimes)
                    r.AcquireTicks.Add(tick - r.EngagedAt);
            }
        }

        r.Leaves[s.Leaf] = r.Leaves.GetValueOrDefault(s.Leaf) + 1;
    }

    /// <summary>The bot's weapon fired once (a shotgun's seven pellets are one shot).</summary>
    public void Shot(int weaponKind, int tick)
    {
        Settle(tick);
        _lastWeapon = weaponKind;
        // Commands run before the meter's reading: the crosshair's last is a tick old.
        int at = _aimed >= 0 && tick - _aimedAt <= 1 ? _aimed : -1;
        _lastShotAimed = at >= 0;
        _lastShotTick = tick;
        _lastShotDistance = at < 0 ? 0f : _aimedDistance;
        Row r = _rows[at < 0 ? 0 : at];
        r.Shots++;
        r.By((weaponKind, AimBands.Of(at < 0 ? 0f : _aimedDistance))).Shots++;
        r.ShotsByOrder[Math.Min(r.ShotOrder, ShotOrders - 1)]++;
        r.ShotOrder++;

        if (_pendingTick == tick)
            Resolve(_lastShotAimed);

        // A first shot is timed for the player the brain was about, aimed well or not.
        int about = _current >= 0 && tick - _currentAt <= EngagementGapTicks ? _current : -1;
        if (about >= 0 && _rows[about] is { Engaged: true, Fired: false } first)
        {
            first.Fired = true;
            if (first.FirstShotTicks.Count < MaxTimes)
                first.FirstShotTicks.Add(tick - first.EngagedAt);
        }
    }

    /// <summary>The bot's second trigger went, by weapon kind.</summary>
    public IReadOnlyDictionary<int, long> AltFired => _altFired;

    /// <summary>What the bot's blasts did to the bot itself, in all (ruling 2026-11-03: a grenade's cost counts).</summary>
    public long SelfBlastDamage { get; private set; }

    private readonly Dictionary<int, long> _altFired = new();

    /// <summary>The bot's second trigger went: told apart from <see cref="Shot"/>, whose hit rate is the aim's.</summary>
    public void AltShot(int weaponKind) => _altFired[weaponKind] = _altFired.GetValueOrDefault(weaponKind) + 1;

    /// <summary>A blast or a ball took <paramref name="taken"/> off the player in <paramref name="slot"/>: damage, not a hit of an aimed shot.</summary>
    public void AltHit(int slot, int taken) => _rows[slot].AltDamage += taken;

    /// <summary>The bot's own blast took <paramref name="taken"/> off it.</summary>
    public void SelfBlast(int taken) => SelfBlastDamage += taken;

    /// <summary>
    /// The bot's fire took <paramref name="taken"/> off the player in
    /// <paramref name="slot"/>: one shot's pellets all land on one tick and are
    /// one hit. A hit counts through a shot that counted: this tick's, which is
    /// told after it (the core tells Fired once the command has run), or else a
    /// projectile's last, once the tick has gone by with no shot.
    /// </summary>
    public void Hit(int slot, int taken, int tick)
    {
        Settle(tick);
        Row r = _rows[slot];
        r.Damage += taken;
        if (r.LastHitTick == tick)
            return;
        r.LastHitTick = tick;
        if (_lastShotTick == tick)
        {
            if (_lastShotAimed)
                Count(slot);
            return;
        }

        _pendingHits.Add(slot);
        _pendingTick = tick;
    }

    /// <summary>Hits waiting for their tick's shot: past it, they belong to the last shot before (a projectile's).</summary>
    public void Settle(int tick)
    {
        if (_pendingTick >= tick)
            return;
        Resolve(_lastShotAimed);
    }

    private void Resolve(bool aimed)
    {
        if (aimed)
        {
            foreach (int slot in _pendingHits)
                Count(slot);
        }

        _pendingHits.Clear();
        _pendingTick = -1;
    }

    private void Count(int slot)
    {
        Row r = _rows[slot];
        r.Hits++;
        r.By((_lastWeapon, AimBands.Of(_lastShotAimed ? _lastShotDistance : r.Distance))).Hits++;
        r.HitsByOrder[Math.Clamp(r.ShotOrder - 1, 0, ShotOrders - 1)]++;
    }

    /// <summary>A tick the bot saw someone within <see cref="CloseRange"/>: what it was doing, and whether it held fire (§12.1, firing comes first).</summary>
    public void Close(string leaf, bool attackHeld)
    {
        (long Ticks, long Firing) had = CloseByLeaf.GetValueOrDefault(leaf);
        CloseByLeaf[leaf] = (had.Ticks + 1, had.Firing + (attackHeld ? 1 : 0));
    }

    /// <summary>"Close", for the ruling that a bot fires first at close range.</summary>
    public const float CloseRange = 400f;

    /// <summary>Ticks with someone seen within <see cref="CloseRange"/>, and those holding fire, by the brain's leaf.</summary>
    public Dictionary<string, (long Ticks, long Firing)> CloseByLeaf { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// One command's sideways move and the view's yaw step since the last, for
    /// the body and view's jitter, by <c>ai-replay metrics</c>' own definitions:
    /// the ticks between changes of the side move's sign (zeros skipped), and
    /// changes of the yaw step's sign (steps of 0.5° or more).
    /// </summary>
    public void Command(int tick, float side, float yawStep, string leaf = "")
    {
        Commands++;
        int sign = Math.Sign(side);
        if (sign != 0)
        {
            if (_sideSign != 0 && sign != _sideSign)
            {
                if (SideIntervalTicks.Count < MaxTimes)
                    SideIntervalTicks.Add(tick - _sideAt);
                SideFlipsByLeaf[leaf] = SideFlipsByLeaf.GetValueOrDefault(leaf) + 1;
                _sideAt = tick;
            }
            else if (_sideSign == 0)
            {
                _sideAt = tick;
            }

            _sideSign = sign;
        }

        if (MathF.Abs(yawStep) >= 0.5f)
        {
            int yaw = Math.Sign(yawStep);
            if (_yawSign != 0 && yaw != _yawSign)
                YawReversals++;
            _yawSign = yaw;
        }
    }

    /// <summary>Commands counted.</summary>
    public long Commands { get; private set; }

    /// <summary>Changes of the yaw step's sign.</summary>
    public long YawReversals { get; private set; }

    /// <summary>Ticks between changes of the side move's sign.</summary>
    public List<int> SideIntervalTicks { get; } = new();

    /// <summary>Those changes by the brain's leaf at the time.</summary>
    public Dictionary<string, long> SideFlipsByLeaf { get; } = new(StringComparer.Ordinal);

    /// <summary>Starts counting afresh; an engagement under way starts over too.</summary>
    public void Reset()
    {
        for (int i = 0; i < _rows.Length; i++)
            _rows[i] = new Row();
        _current = -1;
        _aimed = -1;
        _aimedAt = -1;
        _lastShotAimed = false;
        _lastShotTick = -1;
        _pendingHits.Clear();
        _pendingTick = -1;
        _lastWeapon = -1;
        CloseByLeaf.Clear();
        Commands = 0;
        YawReversals = 0;
        SideIntervalTicks.Clear();
        SideFlipsByLeaf.Clear();
        _sideSign = 0;
        _yawSign = 0;
    }

    /// <summary>Shots and hits with one weapon in one band.</summary>
    public sealed class Tally
    {
        public long Shots { get; set; }

        public long Hits { get; set; }
    }

    /// <summary>One bot's account of one other player.</summary>
    public sealed class Row
    {
        private readonly Dictionary<(int Weapon, AimBand Band), Tally> _by = new();

        internal bool Engaged { get; set; }

        internal int EngagedAt { get; set; }

        internal int GapTicks { get; set; }

        internal bool Fired { get; set; }

        internal bool Acquired { get; set; }

        internal int ShotOrder { get; set; }

        internal int LastHitTick { get; set; } = -1;

        internal float Distance { get; set; }

        /// <summary>Times the player came into view (out of view for <see cref="EngagementGapTicks"/> first).</summary>
        public long Engagements { get; internal set; }

        /// <summary>Ticks it was in the bot's field with a clear line, recognised or not.</summary>
        public long ClearTicks { get; internal set; }

        /// <summary>Ticks the bot held it in sight and was about it; the rest of these numbers count over them.</summary>
        public long InSightTicks { get; internal set; }

        public long FiringTicks { get; internal set; }

        public long OnBodyTicks { get; internal set; }

        public double ErrorSum { get; internal set; }

        public double ErrorSquares { get; internal set; }

        public long Shots { get; internal set; }

        public long Hits { get; internal set; }

        public long Damage { get; internal set; }

        /// <summary>What its blasts and balls did: in no hit rate, and part of what it costs its victim.</summary>
        public long AltDamage { get; internal set; }

        /// <summary>The mean angle from the crosshair to the chest, in sight.</summary>
        public double ErrorMean => InSightTicks == 0 ? 0 : ErrorSum / InSightTicks;

        /// <summary>That angle's spread.</summary>
        public double ErrorSpread => InSightTicks == 0 ? 0 : Math.Sqrt(Math.Max(0, ErrorSquares / InSightTicks - ErrorMean * ErrorMean));

        /// <summary>Ticks from coming into view to the first shot, one per engagement that fired.</summary>
        public List<int> FirstShotTicks { get; } = new();

        /// <summary>Ticks from coming into view to the crosshair first on its body.</summary>
        public List<int> AcquireTicks { get; } = new();

        /// <summary>In-sight ticks by the brain's leaf.</summary>
        public Dictionary<string, long> Leaves { get; } = new(StringComparer.Ordinal);

        public long[] ShotsByOrder { get; } = new long[ShotOrders];

        public long[] HitsByOrder { get; } = new long[ShotOrders];

        /// <summary>Shots and hits by weapon kind and band.</summary>
        public IReadOnlyDictionary<(int Weapon, AimBand Band), Tally> ByWeapon => _by;

        internal Tally By((int Weapon, AimBand Band) key)
        {
            if (!_by.TryGetValue(key, out Tally? t))
                _by[key] = t = new Tally();
            return t;
        }
    }

    /// <summary>
    /// Whether a ray from <paramref name="eye"/> along <paramref name="forward"/>
    /// passes through the box <paramref name="mins"/>..<paramref name="maxs"/>:
    /// a crosshair on a player's body.
    /// </summary>
    public static bool RayHitsBox(AiVector eye, AiVector forward, AiVector mins, AiVector maxs)
    {
        float near = 0f, far = float.PositiveInfinity;
        return Slab(eye.X, forward.X, mins.X, maxs.X, ref near, ref far)
            && Slab(eye.Y, forward.Y, mins.Y, maxs.Y, ref near, ref far)
            && Slab(eye.Z, forward.Z, mins.Z, maxs.Z, ref near, ref far);
    }

    private static bool Slab(float from, float dir, float lo, float hi, ref float near, ref float far)
    {
        if (MathF.Abs(dir) < 1e-6f)
            return from >= lo && from <= hi;
        float a = (lo - from) / dir, b = (hi - from) / dir;
        near = Math.Max(near, Math.Min(a, b));
        far = Math.Min(far, Math.Max(a, b));
        return near <= far;
    }
}
