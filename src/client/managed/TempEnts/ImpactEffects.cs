namespace SourceSharp.Client.TempEnts;

/// <summary>
/// Which impact effect is asking. Mirrors <c>SourceSharpImpactEffect_t</c> in
/// <c>game/client/impact_effects.h</c>, and
/// <c>ImpactEffectParamsTests.TheManagedIdsAreTheOnesTheHeaderDeclares</c>
/// compares the two off disk rather than trusting them to stay in step.
/// </summary>
public enum ImpactEffect
{
    /// <summary><c>FX_DebrisFlecks</c> — cement, tile and wood.</summary>
    DebrisFlecks = 0,

    /// <summary><c>FX_GlassImpact</c>.</summary>
    Glass = 1,

    /// <summary><c>FX_AntlionImpact</c>.</summary>
    Antlion = 2,

    /// <summary><c>FX_BugBlood</c>.</summary>
    BugBlood = 3,

    /// <summary><c>FX_Blood</c>.</summary>
    Blood = 4,

    /// <summary><c>FX_DustImpact</c> — dirt and sand.</summary>
    Dust = 5,

    /// <summary><c>FX_GaussExplosion</c>.</summary>
    Gauss = 6,
}

/// <summary>What the decision is made FROM. <c>SourceSharpImpactIn_t</c>.</summary>
public enum ImpactIn
{
    /// <summary>The <c>CHAR_TEX_*</c> the trace's surface carries.</summary>
    Material = 0,

    /// <summary>The impact's scale, as the caller passed it.</summary>
    Scale = 1,

    /// <summary>Flecks are suppressed — <c>r_drawflecks</c>, or the caller's own flag.</summary>
    NoFlecks = 2,
}

/// <summary>
/// The block's header — what is true of the whole effect rather than of one
/// burst. <c>SourceSharpImpactHeader_t</c>.
/// </summary>
public enum ImpactHeader
{
    /// <summary>How many burst records follow.</summary>
    Bursts = 0,

    /// <summary><c>CParticleCollision::Setup</c> gravity.</summary>
    Gravity = 1,

    /// <summary><c>CParticleCollision::Setup</c> dampening.</summary>
    Dampen = 2,

    /// <summary><c>CParticleCollision::Setup</c> angular spray.</summary>
    AngularSpray = 3,

    /// <summary>Units along the surface normal to spawn at.</summary>
    SpawnOffset = 4,

    /// <summary><c>CParticleCollision::Setup</c> min speed.</summary>
    CollMin = 5,

    /// <summary><c>CParticleCollision::Setup</c> max speed.</summary>
    CollMax = 6,

    /// <summary>Per-effect; the effect's own native code names it.</summary>
    HeadA = 7,

    /// <summary>Per-effect; the effect's own native code names it.</summary>
    HeadB = 8,

    /// <summary>Per-effect; the effect's own native code names it.</summary>
    HeadC = 9,
}

/// <summary>One burst: a count and the ranges the emitter samples inside.
/// <c>SourceSharpImpactBurst_t</c>.</summary>
public enum ImpactBurst
{
    /// <summary>Fewest particles in the burst.</summary>
    CountMin = 0,

    /// <summary>Most particles in the burst. Equal to <see cref="CountMin"/> for a fixed count.</summary>
    CountMax = 1,

    /// <summary>Shortest life, in seconds.</summary>
    LifeMin = 2,

    /// <summary>Longest life, in seconds.</summary>
    LifeMax = 3,

    /// <summary>Per-axis jitter added to the base direction, low end.</summary>
    SprayMin = 4,

    /// <summary>Per-axis jitter added to the base direction, high end.</summary>
    SprayMax = 5,

    /// <summary>Slowest speed along the sprayed direction.</summary>
    SpeedMin = 6,

    /// <summary>Fastest speed along the sprayed direction.</summary>
    SpeedMax = 7,

    /// <summary>The downward bias applied to velocity z, low end.</summary>
    ZMin = 8,

    /// <summary>The downward bias applied to velocity z, high end.</summary>
    ZMax = 9,

    /// <summary>Smallest start size.</summary>
    SizeMin = 10,

    /// <summary>Largest start size.</summary>
    SizeMax = 11,

    /// <summary>End size is start size times this.</summary>
    SizeEndMul = 12,

    /// <summary>Lowest start alpha.</summary>
    AlphaMin = 13,

    /// <summary>Highest start alpha.</summary>
    AlphaMax = 14,

    /// <summary>Lowest start roll.</summary>
    RollMin = 15,

    /// <summary>Highest start roll.</summary>
    RollMax = 16,

    /// <summary>Slowest roll delta.</summary>
    RollDeltaMin = 17,

    /// <summary>Fastest roll delta.</summary>
    RollDeltaMax = 18,

    /// <summary>The colour ramp the surface colour is multiplied by, low end.</summary>
    RampMin = 19,

    /// <summary>The colour ramp the surface colour is multiplied by, high end.</summary>
    RampMax = 20,

    /// <summary>An <see cref="ImpactMaterial"/> slot, never a handle.</summary>
    Material = 21,

    /// <summary>Per-burst; the effect's own native code names it.</summary>
    ExtraA = 22,

    /// <summary>Per-burst; the effect's own native code names it.</summary>
    ExtraB = 23,

    /// <summary>
    /// The burst's own colour, red. All three zero means "use the colour the
    /// caller or the surface supplies" — glass shards are near-white and the
    /// dust behind them blue-grey, bug blood is yellow, antlion dust is warm,
    /// and each of those is a statement about what was hit rather than a
    /// literal the emitter should hold.
    /// </summary>
    TintR = 24,

    /// <summary>The burst's own colour, green.</summary>
    TintG = 25,

    /// <summary>The burst's own colour, blue.</summary>
    TintB = 26,
}

/// <summary>
/// Which material sheet a burst draws with, as a slot rather than a handle.
/// <c>SourceSharpImpactMaterial_t</c>.
///
/// <para>
/// The <c>switch ( materialType )</c> that chose between the wood and the
/// cement fleck sheets was the file's headline decision and it is here now.
/// What crosses is this number; <c>shell_impact_effects.cpp</c>'s own table
/// turns it into the <c>PMaterialHandle</c> it cached at level load. A handle
/// cannot cross and should not — it is a pointer into the material system.
/// </para>
/// </summary>
public enum ImpactMaterial
{
    /// <summary>The burst names no material; its own loop supplies one.</summary>
    None = 0,

    /// <summary><c>g_Mat_DustPuff[0]</c>.</summary>
    DustPuff = 1,

    /// <summary><c>g_Mat_DustPuff[1]</c>.</summary>
    DustPuff2 = 2,

    /// <summary><c>g_Mat_BloodPuff[0]</c>.</summary>
    BloodPuff = 3,

    /// <summary><c>g_Mat_BloodPuff[1]</c>.</summary>
    BloodPuff2 = 4,

    /// <summary><c>g_DustPuff2</c>, the bullet-hole capper's own sheet.</summary>
    DustPuffCap = 5,

    /// <summary><c>g_Mat_Fleck_Cement</c>, a PAIR the emitter picks between.</summary>
    FleckCement = 6,

    /// <summary><c>g_Mat_Fleck_Wood</c>, a PAIR.</summary>
    FleckWood = 7,

    /// <summary><c>g_Mat_Fleck_Antlion</c>, a PAIR.</summary>
    FleckAntlion = 8,

    /// <summary><c>g_Mat_Fleck_Glass</c>, a PAIR.</summary>
    FleckGlass = 9,
}

/// <summary>
/// WHAT AN IMPACT LOOKS LIKE, DECIDED HERE (plan_entity.md §3.9, SHIM_WAVE
/// Lane 3).
///
/// <para>
/// <c>shell_impact_effects.cpp</c> was 1,380 lines with <b>zero</b>
/// <c>SourceSharp_</c> entry points: a <c>switch ( materialType )</c> choosing
/// fleck counts, speed ranges, gravity, dampening and angular spray, plus 112
/// other chosen constants and 188 draws from the client's random stream. §6's
/// nearest row is <i>"leaf renderers … mesh building and material calls"</i>
/// and it does not reach — that file builds no mesh and calls no material. It
/// chose how many particles of what kind at what velocity and handed them to
/// the emitter that does. So the choices are here and the emitter is still
/// there.
/// </para>
///
/// <para>
/// <b>ONE CALL PER EFFECT, NEVER ONE PER PARTICLE.</b> This answers RANGES; the
/// native loop samples inside them. That is the whole of §3.9's shape and the
/// reason a 32-spark gauss explosion costs one crossing rather than 32.
/// </para>
///
/// <para>
/// <b>THE RANDOMNESS THAT IS STILL NATIVE IS NOT SHARED RANDOMNESS,</b> and
/// that was checked before any of it moved. Every draw in the file goes through
/// <c>random-&gt;</c>, the raw client stream, and not one through
/// <c>SharedRandomFloat</c>; the file is compiled into the CLIENT ONLY
/// (<c>server_csharp.vpc</c> names it zero times); and
/// <c>SharedRandomFloat</c> re-seeds the stream from the command number
/// <i>before every call</i>, so consumption order cannot perturb prediction
/// even for an effect that fires inside a prediction window. Impacts are
/// decoration, drawn on one machine, seen by one player —
/// <see cref="TempEntRandom"/> records the same finding for temp entities and
/// is the precedent.
/// </para>
///
/// <para>
/// <b>A COUNT DECIDED HERE IS A RANGE, NOT A SAMPLE.</b> <c>numFlecks</c> was
/// <c>random-&gt;RandomInt( 4, 16 )</c> scaled by the particle throttle; the
/// throttle is the particle system's own live state and does not cross, so what
/// crosses is 4 and 16 and the native loop applies the throttle and draws. A
/// sample taken here would put the throttle on the wrong side of the boundary.
/// </para>
/// </summary>
public static class ImpactEffectPolicy
{
    /// <summary>Floats in the block's header. <c>SS_IMPACT_HEADER_COUNT</c>.</summary>
    public const int HeaderFloats = 10;

    /// <summary>Floats in one burst record. <c>SS_IMPACT_BURST_FIELDS</c>.</summary>
    public const int BurstFloats = 27;

    /// <summary>The most bursts any effect answers. <c>SS_IMPACT_MAX_BURSTS</c>.</summary>
    public const int MaxBursts = 8;

    /// <summary>The widest block. <c>SS_IMPACT_MAX_FLOATS</c>.</summary>
    public const int MaxFloats = HeaderFloats + MaxBursts * BurstFloats;

    // The CHAR_TEX_* the surface types arrive as (game/shared/decals.h). They
    // are the ASCII letters the surface property system writes, so they are
    // spelled as characters rather than as numbers — a number here would be a
    // second place for 'W' to be written down wrong.
    private const int CharTexConcrete = 'C';
    private const int CharTexTile = 'T';
    private const int CharTexWood = 'W';

    /// <summary>Where burst <paramref name="n"/>'s fields start.</summary>
    public static int BurstAt(int n) => HeaderFloats + n * BurstFloats;

    /// <summary>
    /// Fill <paramref name="block"/> with the effect's numbers and answer how
    /// many floats were written, or −1 for an effect this side does not know
    /// or a block too small to hold the answer.
    ///
    /// <para>
    /// −1 is a REFUSAL and native emits nothing on it, rather than drawing with
    /// whatever the buffer happened to hold. §7: a behaviour that is refused is
    /// declared, never a silent no-op.
    /// </para>
    /// </summary>
    public static int Build(ImpactEffect effect, int material, float scale, bool noFlecks,
                           Span<float> block)
    {
        return effect switch
        {
            ImpactEffect.DebrisFlecks => DebrisFlecks(material, scale, noFlecks, block),
            ImpactEffect.Glass => Glass(block),
            ImpactEffect.Antlion => Antlion(block),
            ImpactEffect.BugBlood => BugBlood(block),
            ImpactEffect.Blood => Blood(block),
            ImpactEffect.Dust => Dust(block),
            ImpactEffect.Gauss => Gauss(block),
            _ => -1,
        };
    }

    // ------------------------------------------------------------------------
    // The writer. A burst is set field by field so that a field nobody sets is
    // visibly zero rather than whatever the last effect left there.
    // ------------------------------------------------------------------------

    private readonly ref struct Writer
    {
        private readonly Span<float> _block;

        public Writer(Span<float> block) => _block = block;

        public bool Fits(int bursts) => _block.Length >= HeaderFloats + bursts * BurstFloats;

        public void Head(ImpactHeader field, float value) => _block[(int)field] = value;

        public void Set(int burst, ImpactBurst field, float value)
            => _block[BurstAt(burst) + (int)field] = value;

        public void Clear(int bursts)
            => _block[..(HeaderFloats + bursts * BurstFloats)].Clear();
    }

    // ------------------------------------------------------------------------
    // FX_DebrisFlecks — cement, tile and wood.
    // ------------------------------------------------------------------------
    private static int DebrisFlecks(int material, float scale, bool noFlecks, Span<float> block)
    {
        const int bursts = 4;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.Gravity, 800.0f);        // FLECK_GRAVITY
        w.Head(ImpactHeader.Dampen, 0.3f);           // FLECK_DAMPEN
        w.Head(ImpactHeader.AngularSpray, 0.6f);     // FLECK_ANGULAR_SPRAY
        w.Head(ImpactHeader.SpawnOffset, 1.0f);      // the fleck emitter's own offset
        w.Head(ImpactHeader.CollMin, 64.0f);         // FLECK_MIN_SPEED
        w.Head(ImpactHeader.CollMax, 128.0f);        // FLECK_MAX_SPEED, before the scale
        // MORE POWER MAKES THE SPRAY MORE CONTROLLED — stock's own comment. The
        // spray narrows by HeadA per unit of scale and never below HeadB.
        w.Head(ImpactHeader.HeadA, 0.2f);
        w.Head(ImpactHeader.HeadB, 0.2f);
        w.Head(ImpactHeader.HeadC, 2.0f);            // the dust bursts' offset along the normal

        // Burst 0 — THE FLECKS THEMSELVES, and the file's headline decision:
        // which sheet the surface gets. Suppressed entirely when the caller or
        // r_drawflecks says so, which is a count of zero rather than a missing
        // burst, so the block's shape does not depend on a ConVar.
        w.Set(0, ImpactBurst.CountMin, noFlecks ? 0 : 4);
        w.Set(0, ImpactBurst.CountMax, noFlecks ? 0 : 16);
        w.Set(0, ImpactBurst.LifeMin, 3.0f);
        w.Set(0, ImpactBurst.LifeMax, 3.0f);
        w.Set(0, ImpactBurst.SpeedMin, 64.0f);
        w.Set(0, ImpactBurst.SpeedMax, 128.0f);
        w.Set(0, ImpactBurst.SizeMin, 1.0f);
        w.Set(0, ImpactBurst.SizeMax, 2.0f);
        w.Set(0, ImpactBurst.RollMin, 0.0f);
        w.Set(0, ImpactBurst.RollMax, 360.0f);
        w.Set(0, ImpactBurst.RollDeltaMin, 0.0f);
        w.Set(0, ImpactBurst.RollDeltaMax, 360.0f);
        w.Set(0, ImpactBurst.RampMin, 0.75f);
        w.Set(0, ImpactBurst.RampMax, 1.25f);
        w.Set(0, ImpactBurst.Material, (float)FleckSheet(material));
        // The velocity's size term: a size-1 fleck flies at twice a size-2 one.
        w.Set(0, ImpactBurst.ExtraA, 3.0f);

        // Burst 1 — the dust trail. Two, and they get FASTER with the index.
        w.Set(1, ImpactBurst.CountMin, 2.0f);
        w.Set(1, ImpactBurst.CountMax, 2.0f);
        w.Set(1, ImpactBurst.LifeMin, 1.0f);
        w.Set(1, ImpactBurst.LifeMax, 1.0f);
        w.Set(1, ImpactBurst.SprayMin, -0.8f);
        w.Set(1, ImpactBurst.SprayMax, 0.8f);
        w.Set(1, ImpactBurst.SpeedMin, 2.0f);
        w.Set(1, ImpactBurst.SpeedMax, 24.0f);
        w.Set(1, ImpactBurst.ZMin, 8.0f);
        w.Set(1, ImpactBurst.ZMax, 32.0f);
        w.Set(1, ImpactBurst.SizeMin, 2.0f);
        w.Set(1, ImpactBurst.SizeMax, 4.0f);
        w.Set(1, ImpactBurst.SizeEndMul, 8.0f);
        w.Set(1, ImpactBurst.AlphaMin, 100.0f);
        w.Set(1, ImpactBurst.AlphaMax, 200.0f);
        w.Set(1, ImpactBurst.RollMin, 0.0f);
        w.Set(1, ImpactBurst.RollMax, 360.0f);
        w.Set(1, ImpactBurst.RollDeltaMin, -1.0f);
        w.Set(1, ImpactBurst.RollDeltaMax, 1.0f);
        w.Set(1, ImpactBurst.RampMin, 0.5f);
        w.Set(1, ImpactBurst.RampMax, 1.25f);
        w.Set(1, ImpactBurst.Material, (float)ImpactMaterial.DustPuff);
        w.Set(1, ImpactBurst.ExtraA, 1.0f);          // speed and z scale by (i+1)

        // Burst 2 — the four grittier puffs.
        w.Set(2, ImpactBurst.CountMin, 4.0f);
        w.Set(2, ImpactBurst.CountMax, 4.0f);
        w.Set(2, ImpactBurst.LifeMin, 0.25f);
        w.Set(2, ImpactBurst.LifeMax, 0.5f);
        w.Set(2, ImpactBurst.SprayMin, -0.8f);
        w.Set(2, ImpactBurst.SprayMax, 0.8f);
        w.Set(2, ImpactBurst.SpeedMin, 8.0f);
        w.Set(2, ImpactBurst.SpeedMax, 32.0f);
        w.Set(2, ImpactBurst.ZMin, 8.0f);
        w.Set(2, ImpactBurst.ZMax, 64.0f);
        w.Set(2, ImpactBurst.SizeMin, 1.0f);
        w.Set(2, ImpactBurst.SizeMax, 4.0f);
        w.Set(2, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(2, ImpactBurst.AlphaMin, 255.0f);
        w.Set(2, ImpactBurst.AlphaMax, 255.0f);
        w.Set(2, ImpactBurst.RollMin, 0.0f);
        w.Set(2, ImpactBurst.RollMax, 360.0f);
        w.Set(2, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(2, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(2, ImpactBurst.RampMin, 0.5f);
        w.Set(2, ImpactBurst.RampMax, 1.25f);
        w.Set(2, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff);

        // Burst 3 — THE BULLET HOLE CAPPER. One, and its z is ASSIGNED rather
        // than subtracted, which ExtraB says.
        w.Set(3, ImpactBurst.CountMin, 1.0f);
        w.Set(3, ImpactBurst.CountMax, 1.0f);
        w.Set(3, ImpactBurst.LifeMin, 1.0f);
        w.Set(3, ImpactBurst.LifeMax, 1.5f);
        w.Set(3, ImpactBurst.SprayMin, -0.8f);
        w.Set(3, ImpactBurst.SprayMax, 0.8f);
        w.Set(3, ImpactBurst.SpeedMin, 2.0f);
        w.Set(3, ImpactBurst.SpeedMax, 24.0f);
        w.Set(3, ImpactBurst.ZMin, -2.0f);
        w.Set(3, ImpactBurst.ZMax, 2.0f);
        w.Set(3, ImpactBurst.SizeMin, 4.0f);
        w.Set(3, ImpactBurst.SizeMax, 8.0f);
        w.Set(3, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(3, ImpactBurst.AlphaMin, 100.0f);
        w.Set(3, ImpactBurst.AlphaMax, 200.0f);
        w.Set(3, ImpactBurst.RollMin, 0.0f);
        w.Set(3, ImpactBurst.RollMax, 360.0f);
        w.Set(3, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(3, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(3, ImpactBurst.RampMin, 0.5f);
        w.Set(3, ImpactBurst.RampMax, 1.25f);
        w.Set(3, ImpactBurst.Material, (float)ImpactMaterial.DustPuff);
        w.Set(3, ImpactBurst.ExtraB, 1.0f);          // z is assigned, not subtracted

        _ = scale;   // the scale is applied by the emitter, which knows the throttle
        return HeaderFloats + bursts * BurstFloats;
    }

    /// <summary>
    /// THE <c>switch ( materialType )</c> ITSELF — the shim's headline impact
    /// decision, and now the only place it is written down.
    ///
    /// <para>
    /// Wood gets the wood sheet; concrete, tile and <b>everything else</b> get
    /// cement. The default arm is stock's, not an invention: a surface type this
    /// build has never seen still throws grey chips rather than nothing.
    /// </para>
    /// </summary>
    public static ImpactMaterial FleckSheet(int material) => material switch
    {
        CharTexWood => ImpactMaterial.FleckWood,
        CharTexConcrete or CharTexTile => ImpactMaterial.FleckCement,
        _ => ImpactMaterial.FleckCement,
    };

    // ------------------------------------------------------------------------
    // FX_GlassImpact.
    // ------------------------------------------------------------------------
    private static int Glass(Span<float> block)
    {
        const int bursts = 3;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.Gravity, 800.0f);        // GLASS_SHARD_GRAVITY
        w.Head(ImpactHeader.Dampen, 0.3f);           // GLASS_SHARD_DAMPING
        w.Head(ImpactHeader.AngularSpray, 0.8f);     // GLASS_SHARD_NOISE
        w.Head(ImpactHeader.SpawnOffset, 2.0f);      // the dust bursts' offset
        w.Head(ImpactHeader.CollMin, 1.0f);          // GLASS_SHARD_MIN_SPEED
        w.Head(ImpactHeader.CollMax, 300.0f);        // GLASS_SHARD_MAX_SPEED
        // The lerp toward white that makes the shards match their materials, and
        // the two colour tints — shards, then dust.
        w.Head(ImpactHeader.HeadA, 0.3f);

        // Burst 0 — the shards. Their velocity is built PER AXIS, so SprayMin
        // and SpeedMin are both read for each of x, y and z.
        w.Set(0, ImpactBurst.CountMin, 2.0f);
        w.Set(0, ImpactBurst.CountMax, 4.0f);
        w.Set(0, ImpactBurst.LifeMin, 2.5f);         // GLASS_SHARD_MIN_LIFE
        w.Set(0, ImpactBurst.LifeMax, 5.0f);         // GLASS_SHARD_MAX_LIFE
        w.Set(0, ImpactBurst.SprayMin, -0.8f);
        w.Set(0, ImpactBurst.SprayMax, 0.8f);
        w.Set(0, ImpactBurst.SpeedMin, 1.0f);
        w.Set(0, ImpactBurst.SpeedMax, 300.0f);
        w.Set(0, ImpactBurst.SizeMin, 2.0f);
        w.Set(0, ImpactBurst.SizeMax, 6.0f);
        w.Set(0, ImpactBurst.RollMin, 0.0f);
        w.Set(0, ImpactBurst.RollMax, 360.0f);
        w.Set(0, ImpactBurst.RollDeltaMin, -800.0f);
        w.Set(0, ImpactBurst.RollDeltaMax, 800.0f);
        w.Set(0, ImpactBurst.Material, (float)ImpactMaterial.FleckGlass);
        // The shard's size wobbles by half its own size either way.
        w.Set(0, ImpactBurst.ExtraA, 0.5f);
        // NEAR-WHITE SHARDS, and the dust behind them is not: the shards are
        // glass and the dust is what the frame was bedded in.
        w.Set(0, ImpactBurst.TintR, 200.0f);
        w.Set(0, ImpactBurst.TintG, 200.0f);
        w.Set(0, ImpactBurst.TintB, 210.0f);

        // Burst 1 — the dust. Four, faster with the index.
        w.Set(1, ImpactBurst.CountMin, 4.0f);
        w.Set(1, ImpactBurst.CountMax, 4.0f);
        w.Set(1, ImpactBurst.LifeMin, 0.1f);
        w.Set(1, ImpactBurst.LifeMax, 0.25f);
        w.Set(1, ImpactBurst.SprayMin, -0.8f);
        w.Set(1, ImpactBurst.SprayMax, 0.8f);
        w.Set(1, ImpactBurst.SpeedMin, 8.0f);
        w.Set(1, ImpactBurst.SpeedMax, 16.0f);
        w.Set(1, ImpactBurst.ZMin, 16.0f);
        w.Set(1, ImpactBurst.ZMax, 32.0f);
        w.Set(1, ImpactBurst.SizeMin, 1.0f);
        w.Set(1, ImpactBurst.SizeMax, 4.0f);
        w.Set(1, ImpactBurst.SizeEndMul, 8.0f);
        w.Set(1, ImpactBurst.AlphaMin, 128.0f);
        w.Set(1, ImpactBurst.AlphaMax, 255.0f);
        w.Set(1, ImpactBurst.RollMin, 0.0f);
        w.Set(1, ImpactBurst.RollMax, 360.0f);
        w.Set(1, ImpactBurst.RollDeltaMin, -1.0f);
        w.Set(1, ImpactBurst.RollDeltaMax, 1.0f);
        w.Set(1, ImpactBurst.RampMin, 0.5f);
        w.Set(1, ImpactBurst.RampMax, 1.25f);
        w.Set(1, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff);
        w.Set(1, ImpactBurst.ExtraA, 1.0f);          // speed and z scale by (i+1)
        w.Set(1, ImpactBurst.TintR, 64.0f);
        w.Set(1, ImpactBurst.TintG, 64.0f);
        w.Set(1, ImpactBurst.TintB, 92.0f);

        // Burst 2 — the capper. Its z is assigned.
        w.Set(2, ImpactBurst.CountMin, 1.0f);
        w.Set(2, ImpactBurst.CountMax, 1.0f);
        w.Set(2, ImpactBurst.LifeMin, 1.0f);
        w.Set(2, ImpactBurst.LifeMax, 1.5f);
        w.Set(2, ImpactBurst.SprayMin, -0.8f);
        w.Set(2, ImpactBurst.SprayMax, 0.8f);
        w.Set(2, ImpactBurst.SpeedMin, 2.0f);
        w.Set(2, ImpactBurst.SpeedMax, 8.0f);
        w.Set(2, ImpactBurst.ZMin, -2.0f);
        w.Set(2, ImpactBurst.ZMax, 2.0f);
        w.Set(2, ImpactBurst.SizeMin, 4.0f);
        w.Set(2, ImpactBurst.SizeMax, 8.0f);
        w.Set(2, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(2, ImpactBurst.AlphaMin, 32.0f);
        w.Set(2, ImpactBurst.AlphaMax, 64.0f);
        w.Set(2, ImpactBurst.RollMin, 0.0f);
        w.Set(2, ImpactBurst.RollMax, 360.0f);
        w.Set(2, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(2, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(2, ImpactBurst.RampMin, 0.5f);
        w.Set(2, ImpactBurst.RampMax, 1.25f);
        w.Set(2, ImpactBurst.Material, (float)ImpactMaterial.DustPuff);
        w.Set(2, ImpactBurst.ExtraB, 1.0f);          // z is assigned, not subtracted
        w.Set(2, ImpactBurst.TintR, 64.0f);
        w.Set(2, ImpactBurst.TintG, 64.0f);
        w.Set(2, ImpactBurst.TintB, 92.0f);

        return HeaderFloats + bursts * BurstFloats;
    }

    // ------------------------------------------------------------------------
    // FX_AntlionImpact.
    // ------------------------------------------------------------------------
    private static int Antlion(Span<float> block)
    {
        const int bursts = 2;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.Gravity, 800.0f);        // FLECK_GRAVITY
        w.Head(ImpactHeader.Dampen, 0.3f);           // FLECK_DAMPEN
        w.Head(ImpactHeader.AngularSpray, 1.0f);
        w.Head(ImpactHeader.SpawnOffset, 2.0f);      // along the SHOT direction, not the normal
        w.Head(ImpactHeader.CollMin, 8.0f);
        w.Head(ImpactHeader.CollMax, 256.0f);
        w.Head(ImpactHeader.HeadA, 32.0f);           // the emitter's bounding half-extent
        w.Head(ImpactHeader.HeadB, 4.0f);            // the dust burst's own offset along the shot

        // Burst 0 — the chitin flecks.
        w.Set(0, ImpactBurst.CountMin, 8.0f);
        w.Set(0, ImpactBurst.CountMax, 16.0f);
        w.Set(0, ImpactBurst.LifeMin, 3.0f);
        w.Set(0, ImpactBurst.LifeMax, 3.0f);
        w.Set(0, ImpactBurst.SprayMin, -1.0f);
        w.Set(0, ImpactBurst.SprayMax, 1.0f);
        w.Set(0, ImpactBurst.SpeedMin, 64.0f);       // FLECK_MIN_SPEED
        w.Set(0, ImpactBurst.SpeedMax, 256.0f);
        w.Set(0, ImpactBurst.SizeMin, 1.0f);
        w.Set(0, ImpactBurst.SizeMax, 6.0f);
        w.Set(0, ImpactBurst.RollMin, 0.0f);
        w.Set(0, ImpactBurst.RollMax, 360.0f);
        w.Set(0, ImpactBurst.RollDeltaMin, -800.0f);
        w.Set(0, ImpactBurst.RollDeltaMax, 800.0f);
        w.Set(0, ImpactBurst.Material, (float)ImpactMaterial.FleckAntlion);
        w.Set(0, ImpactBurst.ExtraA, 255.0f);        // front face
        w.Set(0, ImpactBurst.ExtraB, 128.0f);        // back face

        // Burst 1 — the dust trail. Antlion dust is warm: 1, 0.9, 0.75.
        w.Set(1, ImpactBurst.CountMin, 4.0f);
        w.Set(1, ImpactBurst.CountMax, 4.0f);
        w.Set(1, ImpactBurst.LifeMin, 1.0f);
        w.Set(1, ImpactBurst.LifeMax, 1.0f);
        w.Set(1, ImpactBurst.SprayMin, -0.8f);
        w.Set(1, ImpactBurst.SprayMax, 0.8f);
        w.Set(1, ImpactBurst.SpeedMin, 4.0f);
        w.Set(1, ImpactBurst.SpeedMax, 64.0f);
        w.Set(1, ImpactBurst.SizeMin, 8.0f);
        w.Set(1, ImpactBurst.SizeMax, 16.0f);
        w.Set(1, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(1, ImpactBurst.AlphaMin, 32.0f);
        w.Set(1, ImpactBurst.AlphaMax, 64.0f);
        w.Set(1, ImpactBurst.RollMin, 0.0f);
        w.Set(1, ImpactBurst.RollMax, (float)(2.0 * Math.PI));
        w.Set(1, ImpactBurst.RollDeltaMin, -0.5f);
        w.Set(1, ImpactBurst.RollDeltaMax, 0.5f);
        w.Set(1, ImpactBurst.RampMin, 0.5f);
        w.Set(1, ImpactBurst.RampMax, 1.0f);
        w.Set(1, ImpactBurst.Material, (float)ImpactMaterial.DustPuff);
        // ANTLION DUST IS WARM. A 0..1 tint rather than a 0..255 one, because
        // this burst ramps it the way a surface colour is ramped -- which is
        // what antlion chitin stands in for, there being no world surface to
        // ask GetColorForSurface about.
        w.Set(1, ImpactBurst.TintR, 1.0f);
        w.Set(1, ImpactBurst.TintG, 0.9f);
        w.Set(1, ImpactBurst.TintB, 0.75f);

        return HeaderFloats + bursts * BurstFloats;
    }

    // ------------------------------------------------------------------------
    // FX_BugBlood.
    // ------------------------------------------------------------------------
    private static int BugBlood(Span<float> block)
    {
        const int bursts = 3;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.SpawnOffset, 0.0f);
        // The direction the whole spurt is built from wobbles by two units per
        // axis before it is normalised, once for the effect and not per burst.
        w.Head(ImpactHeader.HeadA, 2.0f);
        w.Head(ImpactHeader.HeadB, 32.0f);           // the emitter's bounding half-extent

        // Burst 0 — the spray. THIRTY-TWO, and the XBox arm's sixteen went with
        // the hardware.
        w.Set(0, ImpactBurst.CountMin, 32.0f);
        w.Set(0, ImpactBurst.CountMax, 32.0f);
        w.Set(0, ImpactBurst.LifeMin, 0.25f);
        w.Set(0, ImpactBurst.LifeMax, 0.25f);
        w.Set(0, ImpactBurst.SpeedMin, 32.0f);
        w.Set(0, ImpactBurst.SpeedMax, 150.0f);
        w.Set(0, ImpactBurst.ZMin, 32.0f);
        w.Set(0, ImpactBurst.ZMax, 32.0f);
        w.Set(0, ImpactBurst.SizeMin, 1.0f);
        w.Set(0, ImpactBurst.SizeMax, 2.0f);
        w.Set(0, ImpactBurst.SizeEndMul, 0.0f);      // zero: the end size is a RANGE, ExtraA/B
        w.Set(0, ImpactBurst.AlphaMin, 255.0f);
        w.Set(0, ImpactBurst.AlphaMax, 255.0f);
        w.Set(0, ImpactBurst.RollMin, 0.0f);
        w.Set(0, ImpactBurst.RollMax, 360.0f);
        w.Set(0, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(0, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(0, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff);
        w.Set(0, ImpactBurst.ExtraA, 1.0f);          // end size multiplier, low
        w.Set(0, ImpactBurst.ExtraB, 4.0f);          // end size multiplier, high
        // BUG BLOOD IS YELLOW, and it is not ramped, not lit and not a surface
        // colour -- it is what the creature is.
        w.Set(0, ImpactBurst.TintR, 255.0f);
        w.Set(0, ImpactBurst.TintG, 200.0f);
        w.Set(0, ImpactBurst.TintB, 32.0f);

        // Burst 1 — the finer, longer-lived half.
        w.Set(1, ImpactBurst.CountMin, 16.0f);
        w.Set(1, ImpactBurst.CountMax, 16.0f);
        w.Set(1, ImpactBurst.LifeMin, 0.25f);
        w.Set(1, ImpactBurst.LifeMax, 0.5f);
        w.Set(1, ImpactBurst.SpeedMin, 8.0f);
        w.Set(1, ImpactBurst.SpeedMax, 255.0f);
        w.Set(1, ImpactBurst.ZMin, 16.0f);
        w.Set(1, ImpactBurst.ZMax, 16.0f);
        w.Set(1, ImpactBurst.SizeMin, 1.0f);
        w.Set(1, ImpactBurst.SizeMax, 3.0f);
        w.Set(1, ImpactBurst.AlphaMin, 16.0f);
        w.Set(1, ImpactBurst.AlphaMax, 32.0f);
        w.Set(1, ImpactBurst.RollMin, 0.0f);
        w.Set(1, ImpactBurst.RollMax, 360.0f);
        w.Set(1, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(1, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(1, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff2);
        w.Set(1, ImpactBurst.ExtraA, 1.0f);
        w.Set(1, ImpactBurst.ExtraB, 4.0f);
        w.Set(1, ImpactBurst.TintR, 255.0f);
        w.Set(1, ImpactBurst.TintG, 200.0f);
        w.Set(1, ImpactBurst.TintB, 32.0f);

        // Burst 2 — THE SPLATS, and the one burst whose speed rises smoothly
        // across the burst rather than in whole steps: 75 * ((i/N)+1).
        w.Set(2, ImpactBurst.CountMin, 16.0f);
        w.Set(2, ImpactBurst.CountMax, 16.0f);
        w.Set(2, ImpactBurst.LifeMin, 0.25f);
        w.Set(2, ImpactBurst.LifeMax, 0.5f);
        w.Set(2, ImpactBurst.SprayMin, -2.0f);       // the per-splat position jitter
        w.Set(2, ImpactBurst.SprayMax, 2.0f);
        w.Set(2, ImpactBurst.SpeedMin, 75.0f);
        w.Set(2, ImpactBurst.SpeedMax, 75.0f);
        w.Set(2, ImpactBurst.ZMin, 64.0f);
        w.Set(2, ImpactBurst.ZMax, 64.0f);
        w.Set(2, ImpactBurst.SizeMin, 1.0f);
        w.Set(2, ImpactBurst.SizeMax, 2.0f);
        w.Set(2, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(2, ImpactBurst.AlphaMin, 255.0f);
        w.Set(2, ImpactBurst.AlphaMax, 255.0f);
        w.Set(2, ImpactBurst.RollMin, 0.0f);
        w.Set(2, ImpactBurst.RollMax, 360.0f);
        w.Set(2, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(2, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(2, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff2);
        w.Set(2, ImpactBurst.ExtraA, 16.0f);         // the velocity's own random spread
        w.Set(2, ImpactBurst.TintR, 255.0f);
        w.Set(2, ImpactBurst.TintG, 200.0f);
        w.Set(2, ImpactBurst.TintB, 32.0f);

        return HeaderFloats + bursts * BurstFloats;
    }

    // ------------------------------------------------------------------------
    // FX_Blood.
    // ------------------------------------------------------------------------
    private static int Blood(Span<float> block)
    {
        const int bursts = 2;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.HeadA, 1.0f);            // the direction's per-axis wobble

        // Burst 0 — the cloud. THE FIRST PARTICLE OF EACH BURST DOES NOT MOVE,
        // because stock multiplies the speed by the loop index: i = 0 is a
        // stationary puff at the wound and i = 1 is the one that travels.
        w.Set(0, ImpactBurst.CountMin, 2.0f);
        w.Set(0, ImpactBurst.CountMax, 2.0f);
        w.Set(0, ImpactBurst.LifeMin, 0.25f);
        w.Set(0, ImpactBurst.LifeMax, 0.5f);
        w.Set(0, ImpactBurst.SpeedMin, 2.0f);
        w.Set(0, ImpactBurst.SpeedMax, 8.0f);
        w.Set(0, ImpactBurst.ZMin, -32.0f);
        w.Set(0, ImpactBurst.ZMax, -16.0f);
        w.Set(0, ImpactBurst.SizeMin, 2.0f);
        w.Set(0, ImpactBurst.SizeMax, 2.0f);
        w.Set(0, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(0, ImpactBurst.RollMin, 0.0f);
        w.Set(0, ImpactBurst.RollMax, 360.0f);
        w.Set(0, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(0, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(0, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff);
        w.Set(0, ImpactBurst.ExtraA, 1.0f);          // speed scales by the loop index

        // Burst 1 — the second sheet. Its alpha is fixed at 128 rather than
        // taken from the caller, which is stock's own asymmetry.
        w.Set(1, ImpactBurst.CountMin, 2.0f);
        w.Set(1, ImpactBurst.CountMax, 2.0f);
        w.Set(1, ImpactBurst.LifeMin, 0.5f);
        w.Set(1, ImpactBurst.LifeMax, 0.5f);
        w.Set(1, ImpactBurst.SpeedMin, 4.0f);
        w.Set(1, ImpactBurst.SpeedMax, 16.0f);
        w.Set(1, ImpactBurst.SizeMin, 2.0f);
        w.Set(1, ImpactBurst.SizeMax, 2.0f);
        w.Set(1, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(1, ImpactBurst.AlphaMin, 128.0f);
        w.Set(1, ImpactBurst.AlphaMax, 128.0f);
        w.Set(1, ImpactBurst.RollMin, 0.0f);
        w.Set(1, ImpactBurst.RollMax, 360.0f);
        w.Set(1, ImpactBurst.RollDeltaMin, -4.0f);
        w.Set(1, ImpactBurst.RollDeltaMax, 4.0f);
        w.Set(1, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff2);
        w.Set(1, ImpactBurst.ExtraA, 1.0f);

        return HeaderFloats + bursts * BurstFloats;
    }

    // ------------------------------------------------------------------------
    // FX_DustImpact — dirt and sand.
    // ------------------------------------------------------------------------
    private static int Dust(Span<float> block)
    {
        const int bursts = 3;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.AngularSpray, 0.2f);     // the velocity's own random spread
        // FOUR OF EACH, IDEALLY — stock's comment. The throttle scales it and
        // each burst rounds differently, which is the bias each burst's ExtraA
        // carries: the most visible type survives the throttle longest.
        w.Head(ImpactHeader.HeadA, 4.0f);
        w.Head(ImpactHeader.HeadB, 8.0f);            // the third burst's position jitter

        // Burst 0.
        w.Set(0, ImpactBurst.CountMin, 4.0f);
        w.Set(0, ImpactBurst.CountMax, 4.0f);
        w.Set(0, ImpactBurst.LifeMin, 0.5f);
        w.Set(0, ImpactBurst.LifeMax, 1.0f);
        w.Set(0, ImpactBurst.SprayMin, 1.0f);        // the normal's own contribution
        w.Set(0, ImpactBurst.SprayMax, 6.0f);
        w.Set(0, ImpactBurst.SpeedMin, 250.0f);
        w.Set(0, ImpactBurst.SpeedMax, 500.0f);
        w.Set(0, ImpactBurst.SizeMin, 3.0f);
        w.Set(0, ImpactBurst.SizeMax, 4.0f);
        w.Set(0, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(0, ImpactBurst.AlphaMin, 32.0f);
        w.Set(0, ImpactBurst.AlphaMax, 255.0f);
        w.Set(0, ImpactBurst.RollMin, 0.0f);
        w.Set(0, ImpactBurst.RollMax, 360.0f);
        w.Set(0, ImpactBurst.RollDeltaMin, -8.0f);
        w.Set(0, ImpactBurst.RollDeltaMax, 8.0f);
        w.Set(0, ImpactBurst.RampMin, 0.75f);
        w.Set(0, ImpactBurst.RampMax, 1.25f);
        w.Set(0, ImpactBurst.Material, (float)ImpactMaterial.DustPuff);
        w.Set(0, ImpactBurst.ExtraA, 0.50f);         // the throttle's rounding bias
        // AND THIS BURST ALONE SCALES ITS START SIZE BY THE IMPACT. Stock does
        // it here and not in the burst below, which is why the two are separate
        // bursts rather than one burst of eight.
        w.Set(0, ImpactBurst.ExtraB, 1.0f);

        // Burst 1 — THE MOST VISIBLE PARTICLE TYPE, which is why its rounding
        // bias is the highest of the three: it is the last to be thrown away.
        w.Set(1, ImpactBurst.CountMin, 4.0f);
        w.Set(1, ImpactBurst.CountMax, 4.0f);
        w.Set(1, ImpactBurst.LifeMin, 0.25f);
        w.Set(1, ImpactBurst.LifeMax, 0.75f);
        w.Set(1, ImpactBurst.SprayMin, 1.0f);
        w.Set(1, ImpactBurst.SprayMax, 6.0f);
        w.Set(1, ImpactBurst.SpeedMin, 250.0f);
        w.Set(1, ImpactBurst.SpeedMax, 500.0f);
        w.Set(1, ImpactBurst.SizeMin, 2.0f);
        w.Set(1, ImpactBurst.SizeMax, 4.0f);
        w.Set(1, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(1, ImpactBurst.AlphaMin, 255.0f);
        w.Set(1, ImpactBurst.AlphaMax, 255.0f);
        w.Set(1, ImpactBurst.RollMin, 0.0f);
        w.Set(1, ImpactBurst.RollMax, 360.0f);
        w.Set(1, ImpactBurst.RollDeltaMin, -2.0f);
        w.Set(1, ImpactBurst.RollDeltaMax, 2.0f);
        w.Set(1, ImpactBurst.RampMin, 0.75f);
        w.Set(1, ImpactBurst.RampMax, 1.25f);
        w.Set(1, ImpactBurst.Material, (float)ImpactMaterial.BloodPuff);
        w.Set(1, ImpactBurst.ExtraA, 0.83f);

        // Burst 2 — the flat cap over the hole. It does not move.
        w.Set(2, ImpactBurst.CountMin, 4.0f);
        w.Set(2, ImpactBurst.CountMax, 4.0f);
        w.Set(2, ImpactBurst.LifeMin, 0.5f);
        w.Set(2, ImpactBurst.LifeMax, 1.0f);
        w.Set(2, ImpactBurst.SprayMin, 1.0f);
        w.Set(2, ImpactBurst.SprayMax, 1.0f);
        w.Set(2, ImpactBurst.SpeedMin, 0.0f);
        w.Set(2, ImpactBurst.SpeedMax, 50.0f);
        w.Set(2, ImpactBurst.SizeMin, 1.0f);
        w.Set(2, ImpactBurst.SizeMax, 4.0f);
        w.Set(2, ImpactBurst.SizeEndMul, 4.0f);
        w.Set(2, ImpactBurst.AlphaMin, 32.0f);
        w.Set(2, ImpactBurst.AlphaMax, 64.0f);
        w.Set(2, ImpactBurst.RollMin, 0.0f);
        w.Set(2, ImpactBurst.RollMax, 360.0f);
        w.Set(2, ImpactBurst.RollDeltaMin, -16.0f);
        w.Set(2, ImpactBurst.RollDeltaMax, 16.0f);
        w.Set(2, ImpactBurst.RampMin, 0.75f);
        w.Set(2, ImpactBurst.RampMax, 1.25f);
        // g_Mat_DustPuff[0] AND NOT A CAPPER SHEET OF ITS OWN. The XBox arm
        // this file deleted used g_DustPuff2 here; the arm that is compiled
        // does not, and giving it one would be a new effect rather than a move.
        w.Set(2, ImpactBurst.Material, (float)ImpactMaterial.DustPuff);
        w.Set(2, ImpactBurst.ExtraA, 0.17f);

        return HeaderFloats + bursts * BurstFloats;
    }

    // ------------------------------------------------------------------------
    // FX_GaussExplosion.
    // ------------------------------------------------------------------------
    private static int Gauss(Span<float> block)
    {
        const int bursts = 1;
        var w = new Writer(block);
        if (!w.Fits(bursts)) return -1;
        w.Clear(bursts);

        w.Head(ImpactHeader.Bursts, bursts);
        w.Head(ImpactHeader.Gravity, 800.0f);
        w.Head(ImpactHeader.Dampen, 0.3f);
        w.Head(ImpactHeader.AngularSpray, 0.8f);
        w.Head(ImpactHeader.CollMin, 128.0f);
        w.Head(ImpactHeader.CollMax, 512.0f);
        w.Head(ImpactHeader.HeadA, 1.0f);            // the direction's per-axis wobble

        // Burst 0 — the spark ring.
        w.Set(0, ImpactBurst.CountMin, 16.0f);
        w.Set(0, ImpactBurst.CountMax, 32.0f);
        w.Set(0, ImpactBurst.LifeMin, 0.25f);
        w.Set(0, ImpactBurst.LifeMax, 1.0f);
        w.Set(0, ImpactBurst.SprayMin, -0.6f);
        w.Set(0, ImpactBurst.SprayMax, 0.6f);
        w.Set(0, ImpactBurst.SpeedMin, 128.0f);
        w.Set(0, ImpactBurst.SpeedMax, 512.0f);
        w.Set(0, ImpactBurst.SizeMin, 1.0f);         // the spark's WIDTH
        w.Set(0, ImpactBurst.SizeMax, 4.0f);
        w.Set(0, ImpactBurst.AlphaMin, 255.0f);
        w.Set(0, ImpactBurst.AlphaMax, 255.0f);
        w.Set(0, ImpactBurst.Material, (float)ImpactMaterial.None);
        w.Set(0, ImpactBurst.ExtraA, 0.01f);         // the spark's LENGTH, low
        w.Set(0, ImpactBurst.ExtraB, 0.1f);          // the spark's LENGTH, high

        return HeaderFloats + bursts * BurstFloats;
    }
}
