//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>c_te_legacytempents.cpp</c>'s spawn helpers, in C#.
///
/// <para>
/// A spawn helper is the second half of a pooled temp entity: the pool answers
/// a slot and builds the object, and the helper decides what KIND of thing it
/// is — a bubble that wobbles upward and pops at the surface, a gib that
/// tumbles and fades, a blood sprite that plays one animation in a quarter of a
/// second and dies. Stock has seventeen; <c>docs/te-legacy-pool-survey.md</c>
/// section 2 tables them against the networked temp entity each one serves, and
/// this file is the ones that had a waiting class.
/// </para>
///
/// <para>
/// <b>WHY THEY ARE NOT ON <see cref="ClientTempEntPool"/>.</b> The pool is the
/// LIST and the POLICY — allocation, eviction, expiry, the update loop — and
/// every line of it is about slots. A spawn helper is about sprites and gibs
/// and knows nothing of slots beyond asking for one. Stock put both on
/// <c>CTempEnts</c> and the file is 4,054 lines; the survey's first finding was
/// that only a third of it is the pool.
/// </para>
///
/// <para>
/// <b>THE MODEL IS ALWAYS CHECKED FIRST, and the check is stock's.</b> Every
/// one of these opens with "if there is no such model, do nothing" — stock
/// spells it as a NULL <c>model_t *</c> and this spells it as
/// <see cref="IEnginePrecache.ModelFrameCount"/> answering a negative, which is
/// what that slot returns for an index naming nothing. Two of them
/// (<see cref="TempSprite"/>, <see cref="ClientProjectile"/>) warn when the
/// index is non-zero and still resolves to nothing, because stock warns there
/// and nowhere else: a zero index means the server did not send a model, and a
/// non-zero one that fails to resolve means the client did not precache it,
/// which are two different bugs.
/// </para>
/// </summary>
public sealed class TempEntSpawns
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientTempEntPool _pool;
    private readonly IEnginePrecache _models;
    private readonly IEngineConsole _console;
    private readonly IGameClock _clock;

    /// <summary>
    /// The effects' own random stream (stock's <c>g_pUniformStream</c>), seeded
    /// from the module's seed source. It was a static shared by the process.
    /// </summary>
    private readonly TempEntRandom _random;

    public TempEntSpawns(ILog log, ClientTempEntPool pool, IEnginePrecache models,
                         IEngineConsole console, IGameClock clock, IRandomSeeds seeds)
    {

        _log = log;
        _random = new TempEntRandom(seeds.Next());
        _pool = pool;
        _models = models;
        _console = console;
        _clock = clock;
    }

    /// <summary>
    /// Stock's <c>SHARD_VOLUME</c>: "on shard ever n^3 units", its own comment
    /// and its own spelling.
    /// </summary>
    private const float ShardVolume = 12f;

    /// <summary>
    /// How many frames the model has, or -1 if there is no such model.
    ///
    /// <para>
    /// ONE PLACE, because ten of the seventeen helpers open with it and the
    /// answer's meaning is not obvious: the slot returns -1 for an index naming
    /// nothing, which stands in for stock's <c>model == NULL</c>, and 0 is a
    /// real answer about a real model rather than a failure.
    /// </para>
    /// </summary>
    private int FrameCountOf(int modelIndex)
        => modelIndex == 0 ? -1 : _models.ModelFrameCount(modelIndex);

    // =====================================================================
    // The order below is docs/te-legacy-pool-survey.md section 2's: most
    // classes unblocked per line ported. TempSprite is first because it
    // unblocks two.
    // =====================================================================

    /// <summary>
    /// <c>CTempEnts::TempSprite</c>. One sprite, at a place, for a while.
    /// The slot, or -1.
    ///
    /// <para>
    /// THE ONE HELPER WHOSE RETURN VALUE STOCK READS. <c>C_TEGlowSprite</c>
    /// takes the tempent back and writes <c>bounceFactor = 0.2</c> on it. Here
    /// that is <paramref name="bounceFactor"/> instead, for the reason given
    /// where it is set; the slot is still answered, because "did anything
    /// spawn" is worth being able to ask and every other helper's caller
    /// ignores it.
    /// </para>
    ///
    /// <para>
    /// <paramref name="life"/> of ZERO IS NOT "no lifetime": stock reads it as
    /// "work one out", <c>frameCount * 0.1 + 1</c> seconds — long enough for a
    /// ten-frames-a-second animation to play once with a second to spare.
    /// <c>C_TESprite</c> passes 0 and depends on it.
    /// </para>
    /// </summary>
    public int TempSprite(Vector pos, Vector dir, float scale, int modelIndex,
                          int renderMode, int renderFx, float alpha, float life,
                          int flags, Vector normal = default, float bounceFactor = 1f)
    {
        if (modelIndex == 0)
            return -1;

        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
        {
            // Stock's own words, at stock's own only warning site in this
            // helper. A non-zero index that resolves to nothing is a model the
            // CLIENT did not precache, which is worth saying out loud.
            _log.Warning($"No model {modelIndex}!\n");
            return -1;
        }

        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.FrameMax = frameCount - 1;
        spawn.FrameRate = 10f;
        spawn.RenderMode = renderMode;
        spawn.RenderFx = renderFx;
        spawn.SpriteScale = scale;

        // `tempent_renderamt = a * 255` and `SetRenderColor( 255, 255, 255,
        // a * 255 )`, both of them a float truncated by C's int conversion.
        // The colour is white regardless of what the sprite is tinted, which is
        // stock's: the brightness rides in the ALPHA.
        int amount = (int)(alpha * 255f);

        spawn.RenderAmt = amount;
        spawn.Normal = normal;
        spawn.ColorR = 255;
        spawn.ColorG = 255;
        spawn.ColorB = 255;
        spawn.ColorA = amount;

        // OR, not assign: stock adds to the FTENT_NONE Prepare left behind.
        spawn.Flags |= flags;

        spawn.Velocity = dir;

        spawn.Die = life != 0f
            ? _clock.Now + life
            : _clock.Now + (frameCount * 0.1f) + 1f;

        spawn.Frame = 0f;

        // NOT STOCK'S SHAPE, AND THE DIFFERENCE IS ONE LINE MOVED. Stock's
        // TempSprite does not take this; C_TEGlowSprite takes the tempent BACK
        // and writes `ent->bounceFactor = 0.2` on it, which is the only place
        // in this file's consumers where a helper's return value is read for
        // anything.
        //
        // Carrying that shape would have needed a way to write one field of a
        // live tempent from managed code, and the two candidates were both
        // worse than a parameter: growing SourceSharpTempEntLife_t, which is a
        // per-frame view of eight fields the expiry decision reads and would
        // stop being one; or a slot of its own for a value that is set once, at
        // spawn, by one caller. A spawn record already carries fields set once
        // at spawn, and 1 is Prepare's own default -- so the three other
        // classes that call this pass nothing and get exactly what stock gives
        // them.
        spawn.BounceFactor = bounceFactor;

        // Stock does SetLocalOrigin( pos ) here as well as the SetAbsOrigin the
        // allocation already did. SetLocalOrigin IS SetAbsOrigin on this class
        // (tempent.h:203), with the same argument, so it is one write twice.
        return _pool.Spawn(pos, modelIndex, TempEntPriority.Low, in spawn);
    }

    /// <summary>
    /// <c>CTempEnts::BloodSprite</c>. A large single blood sprite, high
    /// priority, playing one animation in a quarter of a second.
    ///
    /// <para>
    /// <b>THE DROP MODEL IS NEVER USED, and carrying it is deliberate.</b>
    /// Stock's signature takes <c>modelIndex2</c> and its body never reads it —
    /// <c>model = modelinfo-&gt;GetModel( modelIndex )</c> and nothing else.
    /// The parameter is here so that the shape of the call from
    /// <c>C_TEBloodSprite</c> stays stock's, and so that a reader who goes
    /// looking for the drop sprite finds this paragraph rather than concluding
    /// the port lost it.
    /// </para>
    ///
    /// <para>
    /// HIGH PRIORITY, which matters and is stock's comment: "Large, single
    /// blood sprite is a high-priority tent". It is one of only two things in
    /// the whole file that allocates that way, so it is also the only live
    /// driver the pool's eviction rule has.
    /// </para>
    /// </summary>
    public int BloodSprite(Vector origin, int r, int g, int b, int a,
                           int modelIndex, int dropModelIndex, float size)
    {
        if (modelIndex == 0)
            return -1;

        int frameCount = FrameCountOf(modelIndex);

        // Stock's guard is the model pointer and there is no warning here --
        // blood is dispatched constantly, and a missing sprite would fill the
        // console rather than tell anybody anything new.
        if (frameCount < 0)
            return -1;

        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.RenderMode = (int)RenderMode.TransTexture;
        spawn.RenderFx = RenderFx.ClampMinScale;

        // LOW LARGER THAN HIGH, and it is stock's. See TempEntRandom.Float:
        // the interpolation answers a value between the two either way, and
        // "correcting" it would change how big every bullet wound is.
        spawn.SpriteScale = _random.Float(size / 25f, size / 35f);

        // ASSIGNED, not OR'd -- one of only two helpers that replaces the flag
        // word rather than adding to it. Prepare has just set FTENT_NONE, so
        // the two are the same value here; it is written as stock writes it
        // because a later reader diffing the two files should not have to
        // decide whether a difference is deliberate.
        spawn.Flags = (int)TempEntFlags.SprAnimate;

        // color32 truncates each to a byte, and the render amount is read back
        // OFF the colour rather than from the argument -- so an alpha above 255
        // wraps in both places identically.
        spawn.ColorR = (byte)r;
        spawn.ColorG = (byte)g;
        spawn.ColorB = (byte)b;
        spawn.ColorA = (byte)a;
        spawn.RenderAmt = (byte)a;

        spawn.Velocity = Vector.Zero;

        // "Finish in 0.250 seconds" / "Play the whole thing Once" -- stock's
        // comments. The two lines are one statement: rate is frames per second
        // and die is frames divided by it, so the sprite lasts exactly one
        // cycle whatever the frame count is.
        spawn.FrameRate = frameCount * 4f;
        spawn.Die = _clock.Now + (frameCount / spawn.FrameRate);

        spawn.Frame = 0f;
        spawn.FrameMax = frameCount - 1;
        spawn.BounceFactor = 0f;

        // SetLocalAnglesDim( Z_INDEX, ... ) -- Z_INDEX is 2, which in a QAngle
        // is ROLL. A blood sprite is flat and faces the viewer, so rolling it
        // is what stops every wound looking like the same decal.
        spawn.Angles = new Vector(0f, 0f, _random.Int(0, 360));

        return _pool.Spawn(origin, modelIndex, TempEntPriority.High, in spawn);
    }

    /// <summary>
    /// <c>CTempEnts::Bubbles</c>. A box fills with bubbles that wobble upward
    /// and stop at <paramref name="height"/> above the box's floor.
    ///
    /// <para>
    /// <b>THE BOX IS SAMPLED WITH <c>RandomInt</c>, not <c>RandomFloat</c>,</b>
    /// which is stock's and is visible: bubbles land on integer coordinates.
    /// The floats are truncated toward zero on the way in, so a box from -0.5 to
    /// 0.5 is one column at 0 rather than a spread.
    /// </para>
    /// </summary>
    public void Bubbles(Vector mins, Vector maxs, float height, int modelIndex,
                        int count, float speed)
    {
        if (modelIndex == 0)
            return;

        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
            return;

        for (int i = 0; i < count; i++)
        {
            var origin = new Vector(
                _random.Int((int)mins.X, (int)maxs.X),
                _random.Int((int)mins.Y, (int)maxs.Y),
                _random.Int((int)mins.Z, (int)maxs.Z));

            // SinCos( RandomInt( -3, 3 ), ... ) -- and the argument is RADIANS,
            // so the seven values it can take are spread over a bit more than a
            // full turn rather than over seven degrees. Stock's, and the reason
            // a bubble field drifts in one of seven directions.
            float angle = _random.Int(-3, 3);
            float sine = MathF.Sin(angle);
            float cosine = MathF.Cos(angle);

            float zspeed = _random.Int(80, 140);

            var spawn = TempEntSpawn.Prepared(_clock.Now);

            spawn.Flags |= (int)TempEntFlags.SineWave;

            // The axis the wobble is about, not the origin. See TempEntSpawn.SineX.
            spawn.SineX = origin.X;
            spawn.SineY = origin.Y;

            spawn.Velocity = new Vector(speed * cosine, speed * sine, zspeed);

            // How long to reach the surface: the height it has left, over the
            // speed it is rising at, less a tenth so it pops just under.
            spawn.Die = _clock.Now + ((height - (origin.Z - mins.Z)) / zspeed) - 0.1f;

            spawn.Frame = _random.Int(0, frameCount - 1);
            spawn.SpriteScale = 1f / _random.Float(4f, 16f);
            spawn.RenderMode = (int)RenderMode.TransAlpha;
            spawn.ColorR = 255;
            spawn.ColorG = 255;
            spawn.ColorB = 255;
            spawn.ColorA = 192;

            // Stock RETURNS rather than continuing when the pool is full, and
            // that is carried: the remaining bubbles of a full pool would each
            // cost a reservation attempt and a refusal counter for nothing.
            if (_pool.Spawn(origin, modelIndex, TempEntPriority.Low, in spawn) < 0)
                return;
        }
    }

    /// <summary>
    /// <c>CTempEnts::BubbleTrail</c>. Bubbles scattered along a line, rising to
    /// <paramref name="waterZ"/>.
    ///
    /// <para>
    /// Nearly <see cref="Bubbles"/> and deliberately kept separate, because the
    /// three differences are all live: the placement is a lerp along a segment
    /// rather than a box sample, the lifetime is measured to an ABSOLUTE water
    /// height rather than to a height above the box, and the scale range is 4–8
    /// rather than 4–16. Folding them into one function with three flags would
    /// make each of those a parameter nobody could check against stock.
    /// </para>
    /// </summary>
    public void BubbleTrail(Vector start, Vector end, float waterZ, int modelIndex,
                            int count, float speed)
    {
        if (modelIndex == 0)
            return;

        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
            return;

        for (int i = 0; i < count; i++)
        {
            float dist = _random.Float(0f, 1f);
            var origin = start + (end - start) * dist;

            float angle = _random.Int(-3, 3);
            float zspeed = _random.Int(80, 140);

            var spawn = TempEntSpawn.Prepared(_clock.Now);

            spawn.Flags |= (int)TempEntFlags.SineWave;
            spawn.SineX = origin.X;
            spawn.SineY = origin.Y;

            spawn.Velocity = new Vector(
                speed * MathF.Cos(angle), speed * MathF.Sin(angle), zspeed);

            spawn.Die = _clock.Now + ((waterZ - origin.Z) / zspeed) - 0.1f;

            spawn.Frame = _random.Int(0, frameCount - 1);
            spawn.SpriteScale = 1f / _random.Float(4f, 8f);
            spawn.RenderMode = (int)RenderMode.TransAlpha;
            spawn.ColorR = 255;
            spawn.ColorG = 255;
            spawn.ColorB = 255;
            spawn.ColorA = 192;

            if (_pool.Spawn(origin, modelIndex, TempEntPriority.Low, in spawn) < 0)
                return;
        }
    }

    /// <summary>
    /// <c>CTempEnts::FizzEffect</c>. Bubbles rising off the floor of a
    /// submerged brush, drifting with the current.
    ///
    /// <para>
    /// <b>THIS IS BEHAVIOUR LEDGER ROW C6 CLOSED, and it took a capability
    /// rather than a translation.</b> Stock's helper takes the ENTITY and reads
    /// two things off it — <c>modelinfo-&gt;GetModelBounds( pent-&gt;GetModel(),
    /// … )</c> and <c>pent-&gt;GetLocalAngles()[1]</c> — and this ABI answered
    /// neither for a client entity, so the whole class ended at a warning. The
    /// slot <c>ClientEntityModelBounds</c> answers both, from one
    /// <c>cl_entitylist</c> resolve, and <see cref="ClientTeFizz"/> asks it.
    /// </para>
    ///
    /// <para>
    /// <b>THE BOUNDS ARE THE MODEL'S AND NOT THE COLLISION BOX</b>, which is
    /// the mistake this row predicted and the reason the parameters are a
    /// rectangle rather than an entity. <c>IEngineEntities.GetBounds</c> answers
    /// the collideable's box, and on <c>C_StandaloneEntity</c> that is a
    /// symmetric ±16 cube fixed in the constructor and never networked — so
    /// every fizz in the game would rise out of a 32-unit cube at the volume's
    /// origin and look like a working effect.
    /// </para>
    ///
    /// <para>
    /// <b>THE BOUNDS ARE USED AS WORLD COORDINATES, which is stock's and is not
    /// a bug.</b> <c>GetModelBounds</c> answers in the MODEL's frame; a brush
    /// model's frame IS world space (the mapper drew the slab where it sits),
    /// so stock hands <c>mins</c> straight to <c>TempEntAlloc</c> as an origin
    /// and the bubbles land inside the volume. The entity's own origin is never
    /// added, here or in stock — adding it would double the offset for every
    /// brush whose origin is not the world's.
    /// </para>
    ///
    /// <para>
    /// <b>THE WIDTH AND DEPTH ARE INTEGERS AND THE FLOOR IS NOT.</b> Stock
    /// declares <c>int width, depth</c> and assigns float differences to them,
    /// so the span is truncated toward zero, and then samples
    /// <c>RandomInt(0, width-1)</c> — but adds it to <c>mins[0]</c>, which is
    /// still the float. A 40.5-wide volume is sampled over 40 units starting at
    /// its real left edge. <see cref="Bubbles"/> truncates BOTH ends and is
    /// deliberately different; keeping the two apart is why they are two
    /// functions.
    /// </para>
    ///
    /// <para>
    /// <paramref name="yaw"/> is <c>GetLocalAngles()[1]</c> in DEGREES, and the
    /// conversion to radians is stock's <c>*M_PI/180</c>. <see cref="Bubbles"/>
    /// passes <c>RandomInt(-3,3)</c> to <c>SinCos</c> with no conversion at all
    /// — that one really is radians — so the two look alike and are not.
    /// </para>
    /// </summary>
    public void FizzEffect(Vector mins, Vector maxs, float yaw, int modelIndex,
                           int density, int current)
    {
        if (modelIndex == 0)
            return;

        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
            return;

        // `count = density + 1` -- a density of zero is still ONE bubble, which
        // is what makes an env_bubbles with no key set visible at all.
        //
        // Stock's next line is `density = count * 3 + 6`, and NOTHING READS IT
        // AFTERWARDS. It is not carried, because carrying it would be a dead
        // store the compiler warns about and a reader would hunt for the use
        // of. Said here instead so that a reader comparing this against the
        // .cpp finds the answer rather than a discrepancy.
        int count = density + 1;

        // The whole column, floor to ceiling, and NOT a per-bubble remainder:
        // every bubble starts at mins.z, so the distance each has to travel is
        // the same. Bubbles() computes a remainder because it spawns them
        // throughout the box.
        float maxHeight = maxs.Z - mins.Z;

        // int, from a float difference: stock's own declaration, truncating
        // toward zero.
        int width = (int)(maxs.X - mins.X);
        int depth = (int)(maxs.Y - mins.Y);

        float speed = current;

        // SinCos( radians, &sine, &cosine ) -- so the SINE lands in yspeed and
        // the COSINE in xspeed, which is stock's argument order and the reason
        // a current at yaw 0 pushes along +X.
        float radians = yaw * MathF.PI / 180f;
        float ySpeed = MathF.Sin(radians) * speed;
        float xSpeed = MathF.Cos(radians) * speed;

        for (int i = 0; i < count; i++)
        {
            var origin = new Vector(
                mins.X + _random.Int(0, width - 1),
                mins.Y + _random.Int(0, depth - 1),
                mins.Z);

            var spawn = TempEntSpawn.Prepared(_clock.Now);

            spawn.Flags |= (int)TempEntFlags.SineWave;

            // The axis the wobble is about, not the origin. See TempEntSpawn.SineX.
            spawn.SineX = origin.X;
            spawn.SineY = origin.Y;

            float zspeed = _random.Int(80, 140);

            spawn.Velocity = new Vector(xSpeed, ySpeed, zspeed);

            // How long to reach the top of the volume, less a tenth so it pops
            // just under. Stock's, including the tenth.
            spawn.Die = _clock.Now + (maxHeight / zspeed) - 0.1f;

            spawn.Frame = _random.Int(0, frameCount - 1);

            // 2..5, NOT Bubbles' 4..16: a fizz's bubbles are two to eight times
            // the size of a bubble field's. Stock's numbers, and the difference
            // is visible.
            spawn.SpriteScale = 1f / _random.Float(2f, 5f);

            spawn.RenderMode = (int)RenderMode.TransAlpha;

            // SetRenderColorA( 255 ) and nothing else -- the RGB stays the
            // white Prepared already carries. Bubbles() calls the four-argument
            // SetRenderColor and lands on 192, which is why that one writes all
            // four and this one writes one.
            spawn.ColorA = 255;

            // Stock RETURNS rather than continuing when the pool is full, and
            // that is carried: see Bubbles.
            if (_pool.Spawn(origin, modelIndex, TempEntPriority.Low, in spawn) < 0)
                return;
        }
    }

    /// <summary>
    /// <c>CTempEnts::Sprite_Spray</c>. A cone of sprites thrown along a
    /// direction and fading as they fall.
    ///
    /// <para>
    /// <paramref name="scatter"/> is stock's <c>iRand</c>, in hundredths: the
    /// helper divides it by 100 to get the sideways spread and multiplies THAT
    /// by 1.5, capped at 1, for the upward one — "more vertical displacement",
    /// stock's comment. The vertical noise is one-sided, so the spray always
    /// arcs up relative to its direction and never down.
    /// </para>
    /// </summary>
    public void SpriteSpray(Vector pos, Vector dir, int modelIndex, int count,
                            int speed, int scatter)
    {
        float noise = (float)scatter / 100f;

        float znoise = noise * 1.5f;

        if (znoise > 1f)
            znoise = 1f;

        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
        {
            _log.Warning($"No model {modelIndex}!\n");
            return;
        }

        // MINUS ONE HERE, not at the use. Stock keeps frameCount already
        // decremented in this one helper, and the random frame below is drawn
        // INCLUSIVE of it -- so the spread is the same as everywhere else and
        // only the spelling differs. Written stock's way so a diff is empty.
        frameCount -= 1;

        for (int i = 0; i < count; i++)
        {
            var spawn = TempEntSpawn.Prepared(_clock.Now);

            spawn.RenderMode = (int)RenderMode.TransAlpha;
            spawn.ColorR = 255;
            spawn.ColorG = 255;
            spawn.ColorB = 255;
            spawn.ColorA = 255;
            spawn.RenderAmt = 255;
            spawn.RenderFx = RenderFx.NoDissipation;
            spawn.SpriteScale = 0.5f;
            spawn.Flags |= (int)(TempEntFlags.FadeOut | TempEntFlags.SlowGravity);
            spawn.FadeSpeed = 2f;

            var velocity = new Vector(
                dir.X + _random.Float(-noise, noise),
                dir.Y + _random.Float(-noise, noise),
                dir.Z + _random.Float(0f, znoise));

            velocity *= _random.Float(speed * 0.8f, speed * 1.2f);

            spawn.Velocity = velocity;
            spawn.Die = _clock.Now + 0.35f;
            spawn.Frame = _random.Int(0, frameCount);

            if (_pool.Spawn(pos, modelIndex, TempEntPriority.Low, in spawn) < 0)
                return;
        }
    }

    /// <summary>
    /// <c>CTempEnts::ClientProjectile</c>. One model flying along a velocity,
    /// aligned to its own motion, colliding with everything except its owner.
    /// The slot, or -1.
    ///
    /// <para>
    /// <b>THE ONLY HELPER WHOSE EFFECT DEPENDS ON THE COLLISION ARM</b>, which
    /// is still native and still holds ledger rows C4 and C5 open. It sets
    /// <c>FTENT_COLLIDEALL</c> and <c>FTENT_ATTACHTOTARGET</c>, both of which
    /// are read only inside <c>Frame</c>'s collision block, and
    /// <see cref="TempEntSpawn.OwnerEntityIndex"/> is the entity that block's
    /// trace filter ignores. All three cross correctly and native reads them
    /// exactly as it always has — this helper moving to C# does not move that
    /// block, and does not need to.
    /// </para>
    ///
    /// <para>
    /// <b>THE IMPACT EFFECT AND THE PARTICLE EFFECT ARE BOTH GONE ALREADY,</b>
    /// and not by this lane: stock's last two parameters default to NULL, both
    /// live callers pass six arguments, and
    /// <c>src/sourcesharp/test/tempent_verdict_test.cpp</c> asserts exactly
    /// that. The particle branch was removed from the native body before this
    /// port reached here; <c>m_pszImpactEffect</c> is still a member and is
    /// still NULL, which is what keeps <see cref="ClientTempEntPool"/>'s
    /// re-entrancy note true.
    /// </para>
    /// </summary>
    public int ClientProjectile(Vector origin, Vector velocity, Vector acceleration,
                                int modelIndex, int lifetime, int ownerEntityIndex)
    {
        if (modelIndex == 0)
            return -1;

        if (FrameCountOf(modelIndex) < 0)
        {
            _log.Warning($"ClientProjectile: No model {modelIndex}!\n");
            return -1;
        }

        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.Velocity = velocity;
        spawn.Acceleration = acceleration;

        // VectorAngles of the velocity, so the model points where it is going
        // on the first frame too -- FTENT_ALIGNTOMOTION only takes over once
        // Frame has run.
        spawn.Angles = Angles.FromVector(velocity);

        spawn.Die = _clock.Now + lifetime;

        // ASSIGNED, not OR'd. The second of the two helpers that replaces the
        // flag word; see BloodSprite.
        spawn.Flags = (int)(TempEntFlags.CollideAll
                          | TempEntFlags.AttachToTarget
                          | TempEntFlags.AlignToMotion);

        // ZERO AND NOT -1 FOR NO OWNER, which is stock's and is not the same as
        // Prepare's default. `clientIndex = ( pOwner != NULL ) ? entindex() : 0`
        // -- so a projectile with no owner carries the WORLD's index here,
        // while the handle beside it carries nothing. The two are read by
        // different things (this by the collision arm's ignore test, the handle
        // by SetTempEntOwner's own consumer) and stock does not keep them in
        // step; neither does this.
        spawn.ClientIndex = ownerEntityIndex >= 0 ? ownerEntityIndex : 0;
        spawn.OwnerEntityIndex = ownerEntityIndex;

        return _pool.Spawn(origin, modelIndex, TempEntPriority.Low, in spawn);
    }

    /// <summary>
    /// <c>CTempEnts::BreakModel</c>. A box of gibs, tumbling, fading, colliding
    /// with the world and sharing one lighting origin. The largest of the
    /// helpers and the last to land.
    ///
    /// <para>
    /// <paramref name="count"/> OF ZERO MEANS "work it out from the size", and
    /// the formula is stock's surface-area estimate — <c>(xy + yz + zx) / (3 *
    /// 12 * 12)</c>, in floats, truncated to an int. Stock's own comment says
    /// "assume surface (not volume)".
    /// </para>
    ///
    /// <para>
    /// <b><c>func_break_max_pieces</c> IS ASKED OF THE ENGINE, NOT RESTATED.</b>
    /// The convar is registered natively in <c>c_te_legacytempents.cpp</c> and
    /// is <c>FCVAR_REPLICATED</c>, so its value is the server's. A C# constant
    /// of 15 beside it would be a stand-in that drifts the moment a server
    /// changes it, and the symptom would be a client drawing a different number
    /// of gibs than everybody else — which no counter in this port would show.
    /// </para>
    /// </summary>
    public void BreakModel(Vector pos, Vector angles, Vector size, Vector dir,
                           float randRange, float life, int count, int modelIndex,
                           int flags)
    {
        if (modelIndex == 0)
            return;

        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
            return;

        // "See g_BreakableHelper above for notes" -- stock's. A slave batch
        // lights off the master batch that is already in the pool, which is how
        // a crate broken in two messages still lights as one object.
        bool isSlave = (flags & BreakFlags.Slave) != 0;

        if (count == 0)
        {
            // Float arithmetic truncated to an int, as stock's assignment to
            // `int count` is.
            count = (int)((size.X * size.Y + size.Y * size.Z + size.Z * size.X)
                        / (3f * ShardVolume * ShardVolume));
        }

        int max = MaxPieces();

        if (count > max)
            count = max;

        // VectorTransform( local, AngleMatrix( angles, pos ) ) -- the gibs are
        // scattered in the BOX's frame and then rotated into the world, so a
        // crate lying on its side comes apart along its own axes.
        var frame = Transform.FromOriginAngles(pos, new QAngle(angles.X, angles.Y, angles.Z));

        ModelType kind = _models.ModelTypeOf(modelIndex);

        for (int i = 0; i < count; i++)
        {
            var local = new Vector(
                _random.Float(-0.5f, 0.5f) * size.X,
                _random.Float(-0.5f, 0.5f) * size.Y,
                _random.Float(-0.5f, 0.5f) * size.Z);

            var spot = frame.TransformPoint(local);

            var spawn = TempEntSpawn.Prepared(_clock.Now);

            // "keep track of break_type, so we know how to play sound on
            // collision" -- stock's comment. PlaySound reads this back to
            // decide whether a landing gib sounds like glass, wood or concrete,
            // and PlaySound is still native and still called from inside the
            // collision arm. Carrying it is what keeps that sound right.
            spawn.HitSound = flags;

            // The one reader of the model type in the whole port. A sprite gib
            // picks a random FRAME and a studio gib a random BODY -- get it
            // backwards and fifteen gibs are all body 0, the same chunk of wood
            // fifteen times, which looks like the effect working.
            if (kind == ModelType.Sprite)
                spawn.Frame = _random.Int(0, frameCount - 1);
            else if (kind == ModelType.Studio)
                spawn.Body = _random.Int(0, frameCount - 1);

            spawn.Flags |= (int)(TempEntFlags.CollideWorld
                               | TempEntFlags.FadeOut
                               | TempEntFlags.SlowGravity);

            // Roughly four gibs in five tumble. RandomInt is inclusive of 255,
            // so the test is 200 outcomes out of 256.
            if (_random.Int(0, 255) < 200)
            {
                spawn.Flags |= (int)TempEntFlags.Rotate;
                spawn.AngularVelocity = new Vector(
                    _random.Float(-256f, 255f),
                    _random.Float(-256f, 255f),
                    _random.Float(-256f, 255f));
            }

            if (_random.Int(0, 255) < 100 && (flags & BreakFlags.Smoke) != 0)
                spawn.Flags |= (int)TempEntFlags.SmokeTrail;

            if ((flags & BreakFlags.Glass) != 0 || (flags & BreakFlags.Trans) != 0)
            {
                spawn.RenderMode = (int)RenderMode.TransTexture;
                spawn.ColorA = 128;
                spawn.RenderAmt = 128;
                spawn.BounceFactor = 0.3f;
            }
            else
            {
                spawn.RenderMode = (int)RenderMode.Normal;

                // "Set this for fadeout" -- stock's comment, and it is the
                // point: FTENT_FADEOUT ramps the alpha down FROM this, so a
                // zero here is a gib that is already invisible.
                spawn.RenderAmt = 255;
            }

            spawn.Velocity = new Vector(
                dir.X + _random.Float(-randRange, randRange),
                dir.Y + _random.Float(-randRange, randRange),
                dir.Z + _random.Float(0f, randRange));

            // "Add an extra 0-1 secs of life" -- stock's, and it is what stops
            // a crate's worth of gibs all vanishing on the same frame.
            spawn.Die = _clock.Now + life + _random.Float(0f, 1f);

            // SetDrawHelper( BreakModelDrawHelper ) and
            // g_BreakableHelper.Insert( pTemp, isSlave ), which are one
            // decision on two adjacent lines in stock and one field here.
            spawn.BreakHelper = isSlave ? BreakGroup.Slave : BreakGroup.Master;

            if (_pool.Spawn(spot, modelIndex, TempEntPriority.Low, in spawn) < 0)
                return;
        }
    }

    /// <summary>
    /// <c>func_break_max_pieces</c>, read off the engine. Stock's default of 15
    /// when the convar cannot be read at all.
    /// </summary>
    private int MaxPieces()
    {
        string? held = _console.FindConVarValue("func_break_max_pieces");

        if (int.TryParse(held, System.Globalization.CultureInfo.InvariantCulture, out int max))
            return max;

        // NOT SILENT. The convar is registered by c_te_legacytempents.cpp,
        // which is still compiled, so failing to read it means something about
        // the console is wrong rather than that the setting is absent -- and
        // the symptom would be a gib count that quietly stopped following the
        // server's.
        _log.Warning("[SourceSharp/Client] BreakModel: func_break_max_pieces answered "
                  + $"'{held ?? "(nothing)"}'; falling back to stock's 15\n");
        return 15;
    }

    /// <summary>
    /// <c>CTempEnts::EjectBrass</c>'s decision half — a spent cartridge case,
    /// tumbling, bouncing off the world, fading out after a second or two.
    ///
    /// <para>
    /// <b>THE ONLY HELPER HERE THAT WRITES INTO A SLOT SOMEBODY ELSE
    /// RESERVED</b>, and the reason is <c>m_pShells[]</c>: the three shell
    /// models are <c>engine-&gt;LoadModel</c>'d by the pool's own
    /// <c>LevelInit</c> and carry no precache INDEX, so there is nothing for
    /// <c>TempEntPoolCreate</c> to resolve. That is exactly the asymmetry
    /// <c>sourcesharp.h</c> describes at the reservation callback — <i>"those
    /// keep the native entry point they already have, and it is why the
    /// reservation callback is a bare priority rather than a
    /// create-with-model"</i> — so the native entry point resolves the model and
    /// builds the object, and this decides what it is.
    /// </para>
    ///
    /// <para>
    /// It is the only helper the legacy pool had left with compiled call sites
    /// on a real entry point (<c>docs/te-pool-reach.tsv</c>,
    /// <c>tools/te-pool-reach</c>); nine of the ten the survey listed as
    /// remaining have no caller at all. <b>None of the three is currently
    /// REACHABLE</b> and that is stated rather than glossed —
    /// <c>fx_shelleject.cpp</c>'s callbacks want a
    /// <c>DispatchEffect("ShellEject")</c> whose only senders are out of the
    /// build, and <c>c_baseanimating.cpp</c>'s <c>CL_EVENT_EJECTBRASS1</c> is
    /// the path <c>shell_te_pool_client.cpp</c> measured at zero allocations.
    /// What drives this end to end is <c>cs_cl_te_pool_shell</c>, through the
    /// real entry point, which is how the pool's gate 3 checks the whole chain.
    /// </para>
    ///
    /// <para>
    /// <paramref name="angles"/> is the direction the case is THROWN and
    /// <paramref name="gunAngles"/> is the way it is FACING, and they are two
    /// arguments in stock for a reason a port could quietly lose: a weapon
    /// ejects sideways while the case leaves the chamber nose-first.
    /// </para>
    /// </summary>
    /// <param name="slot">The slot the native entry point already reserved and
    /// built into. NOT reserved here.</param>
    /// <param name="type">Which of <c>m_pShells[]</c>; 2 is the shotgun's.</param>
    public bool EjectBrass(int slot, int type, Vector angles, Vector gunAngles)
    {
        var spawn = TempEntSpawn.Prepared(_clock.Now);

        // "Keep track of shell type" -- stock's comment. hitSound is what
        // CTempEnts::PlaySound switches on when the case lands, so this is the
        // difference between a rifle case's tink and a shotgun shell's thud.
        spawn.HitSound = type == 2 ? BounceSounds.ShotShell : BounceSounds.Shell;

        spawn.Body = 0;

        // OR, not assign: stock adds to the FTENT_NONE Prepare left behind.
        //
        // COLLIDEWORLD AND NOT COLLIDEALL, which is stock's and is worth saying:
        // a case bounces off the map and passes through people, so a firefight
        // does not end with cases resting on everyone's shoulders. And GRAVITY
        // rather than SLOWGRAVITY, unlike every other helper in this file --
        // brass is heavy and stock says so by the flag it picks.
        spawn.Flags |= (int)(TempEntFlags.CollideWorld | TempEntFlags.FadeOut
                           | TempEntFlags.Gravity | TempEntFlags.Rotate);

        spawn.AngularVelocity = new Vector(_random.Float(-1024f, 1024f),
                                           _random.Float(-1024f, 1024f),
                                           _random.Float(-1024f, 1024f));

        // "Face forward" -- stock's comment, and it is the GUN's angles rather
        // than the throw's.
        spawn.Angles = gunAngles;

        // Prepare already left it here; stock assigns it anyway and so does
        // this, because the line is stock's and a reader comparing the two
        // should find every one of them.
        spawn.RenderMode = (int)RenderMode.Normal;

        // "Set this for fadeout" -- stock's comment. FTENT_FADEOUT ramps the
        // alpha down FROM this, so a zero here is a case invisible from the
        // moment it leaves the gun.
        spawn.RenderAmt = 255;

        Vector dir = Angles.Forward(angles) * _random.Float(150f, 200f);

        // THE Z SCATTER IS ASYMMETRIC ON PURPOSE and it is stock's: 0..64 rather
        // than the -64..64 the other two axes get, so a case is never thrown
        // downwards harder than gravity is already taking it.
        spawn.Velocity = new Vector(dir.X + _random.Float(-64f, 64f),
                                    dir.Y + _random.Float(-64f, 64f),
                                    dir.Z + _random.Float(0f, 64f));

        // "Add an extra 0-1 secs of life" -- stock's comment.
        spawn.Die = _clock.Now + 1f + _random.Float(0f, 1f);

        return _pool.WriteSpawnInto(slot, in spawn);
    }

    // =====================================================================
    // THE ARMS NOTHING IN HL2MP CALLS (lane c2-fx, integrator 2026-10-05).
    //
    // c_te_legacytempents.cpp's thunk keeps these as ITempEnts slots that
    // warn once, because no translation unit stock hl2mp compiles calls them
    // (Sprite_Explode/Smoke/Trail, RicochetSprite, DefaultSprite, RocketFlare)
    // or their one caller is a client effect that is C#'s too (SpawnTempModel,
    // through BoltImpact). Their behaviour is here, so a managed caller has
    // stock's helper rather than a new one.
    // =====================================================================

    /// <summary>
    /// <c>CTempEnts::RicochetSprite</c>: a glowing sprite that starts fading at
    /// once, turned to one of eight 45-degree roll steps. The slot, or -1.
    ///
    /// <para>
    /// <paramref name="duration"/> is stock's parameter and stock never reads it:
    /// <c>die = gpGlobals-&gt;curtime</c>, so FTENT_FADEOUT starts at once at a
    /// fade speed of 8. Kept so the call keeps stock's shape.
    /// </para>
    /// </summary>
    public int RicochetSprite(Vector pos, int modelIndex, float duration, float scale)
    {
        _ = duration;

        if (FrameCountOf(modelIndex) < 0)
            return -1;

        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.RenderMode = (int)RenderMode.Glow;
        spawn.RenderFx = RenderFx.NoDissipation;
        spawn.ColorA = 200;
        spawn.RenderAmt = 200;
        spawn.SpriteScale = scale;
        spawn.Flags = (int)TempEntFlags.FadeOut;
        spawn.Velocity = Vector.Zero;
        spawn.FadeSpeed = 8f;
        spawn.Die = _clock.Now;
        spawn.Frame = 0f;

        // SetLocalAnglesDim( Z_INDEX, 45 * RandomInt( 0, 7 ) ): the ROLL.
        spawn.Angles = new Vector(0f, 0f, 45 * _random.Int(0, 7));

        return _pool.Spawn(pos, modelIndex, TempEntPriority.Low, in spawn);
    }

    /// <summary>
    /// <c>CTempEnts::DefaultSprite</c>'s record: one pass of a sprite's
    /// animation at <paramref name="framerate"/> frames a second (0 means 10).
    /// False where stock answers NULL: paused (<paramref name="frameTime"/> 0),
    /// or an index that is not a sprite.
    ///
    /// <para>
    /// A RECORD AND NOT A SLOT, because stock's callers take the tempent back
    /// and finish it with <see cref="SpriteExplode"/> or
    /// <see cref="SpriteSmoke"/> before it draws; here that is done to the
    /// record, and <see cref="Commit"/> builds it.
    /// </para>
    /// </summary>
    public bool DefaultSprite(Vector pos, int spriteIndex, float framerate, float frameTime,
                              out PendingTempEnt pending)
    {
        pending = default;

        // "don't spawn while paused"
        if (frameTime == 0f)
            return false;

        if (spriteIndex == 0 || FrameCountOf(spriteIndex) < 0
            || _models.ModelTypeOf(spriteIndex) != ModelType.Sprite)
        {
            _log.Warning($"No Sprite {spriteIndex}!\n");
            return false;
        }

        int frameCount = FrameCountOf(spriteIndex);
        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.FrameMax = frameCount - 1;
        spawn.SpriteScale = 1f;
        spawn.Flags |= (int)TempEntFlags.SprAnimate;

        if (framerate == 0f)
            framerate = 10f;

        spawn.FrameRate = framerate;
        spawn.Die = _clock.Now + (float)frameCount / framerate;
        spawn.Frame = 0f;

        pending = new PendingTempEnt { Origin = pos, ModelIndex = spriteIndex, Spawn = spawn };
        return true;
    }

    /// <summary>
    /// <c>CTempEnts::Sprite_Explode</c>: an explosion's sprite, by its
    /// <c>TE_EXPLFLAG_*</c> flags -- opaque, alpha or (the default) additive,
    /// rolled at random under ROTATE, lifted 10 units and drifting up at 8.
    /// </summary>
    public void SpriteExplode(ref PendingTempEnt pending, float scale, int flags)
    {
        ref TempEntSpawn spawn = ref pending.Spawn;

        if ((flags & ExplosionFlags.NoAdditive) != 0)
        {
            // solid sprite
            spawn.RenderMode = (int)RenderMode.Normal;
            spawn.ColorA = 255;
        }
        else if ((flags & ExplosionFlags.DrawAlpha) != 0)
        {
            // alpha sprite
            spawn.RenderMode = (int)RenderMode.TransAlpha;
            spawn.ColorA = 180;
        }
        else
        {
            // additive sprite
            spawn.RenderMode = (int)RenderMode.TransAdd;
            spawn.ColorA = 180;
        }

        if ((flags & ExplosionFlags.Rotate) != 0)
            spawn.Angles = spawn.Angles with { Z = _random.Int(0, 360) };

        spawn.RenderFx = 0;
        spawn.Velocity = new Vector(0f, 0f, 8f);
        spawn.ColorR = 255;
        spawn.ColorG = 255;
        spawn.ColorB = 255;
        pending.Origin = pending.Origin with { Z = pending.Origin.Z + 10f };
        spawn.SpriteScale = scale;
    }

    /// <summary>
    /// <c>CTempEnts::Sprite_Smoke</c>: a dark grey alpha sprite (one shade of
    /// 20..35 on all three channels), lifted 20 units, rising at 30 and blown
    /// by the wind -- and nothing else, because stock ASSIGNS the flags.
    /// </summary>
    public void SpriteSmoke(ref PendingTempEnt pending, float scale)
    {
        ref TempEntSpawn spawn = ref pending.Spawn;

        spawn.RenderMode = (int)RenderMode.TransAlpha;
        spawn.RenderFx = 0;
        spawn.Velocity = new Vector(0f, 0f, 30f);

        int color = _random.Int(20, 35);
        spawn.ColorR = color;
        spawn.ColorG = color;
        spawn.ColorB = color;
        spawn.ColorA = 255;

        pending.Origin = pending.Origin with { Z = pending.Origin.Z + 20f };
        spawn.SpriteScale = scale;
        spawn.Flags = (int)TempEntFlags.WindBlown;
    }

    /// <summary>Build a finished <see cref="DefaultSprite"/> record. The slot, or -1.</summary>
    public int Commit(in PendingTempEnt pending)
        => _pool.Spawn(pending.Origin, pending.ModelIndex, TempEntPriority.Low, in pending.Spawn);

    /// <summary>
    /// <c>CTempEnts::Sprite_Trail</c>: <paramref name="count"/> glowing sprites
    /// spaced evenly from start to end, each thrown along the line at
    /// <paramref name="speed"/> with a random kick of up to 128 * amplitude/256
    /// per axis, on a random frame, dying 0..4 seconds after
    /// <paramref name="life"/>.
    /// </summary>
    public void SpriteTrail(Vector start, Vector end, int modelIndex, int count, float life,
                            float size, float amplitude, int renderAmt, float speed)
    {
        int frameCount = FrameCountOf(modelIndex);

        if (frameCount < 0)
        {
            _log.Warning($"No model {modelIndex}!\n");
            return;
        }

        Vector delta = end - start;
        Vector dir = delta;
        Effects.FxMath.Normalize(ref dir);

        // flAmplitude /= 256.0 -- a double division stored back into a float.
        amplitude = (float)(amplitude / 256.0);

        for (int i = 0; i < count; i++)
        {
            // "Be careful of divide by 0 when using 'count' here..." -- stock's
            // comment; the i == 0 arm is VectorMA( start, 0, delta ).
            float t = i == 0 ? 0f : (float)(i / (count - 1.0));
            Vector pos = start + delta * t;

            var spawn = TempEntSpawn.Prepared(_clock.Now);

            spawn.Flags |= (int)(TempEntFlags.CollideWorld | TempEntFlags.SprCycle
                               | TempEntFlags.FadeOut | TempEntFlags.SlowGravity);

            Vector vel = dir * speed;
            float kx = _random.Int(-127, 128) * amplitude;
            float ky = _random.Int(-127, 128) * amplitude;
            float kz = _random.Int(-127, 128) * amplitude;
            spawn.Velocity = new Vector(vel.X + kx, vel.Y + ky, vel.Z + kz);

            spawn.SpriteScale = size;
            spawn.RenderMode = (int)RenderMode.Glow;
            spawn.RenderFx = RenderFx.NoDissipation;
            spawn.RenderAmt = renderAmt;
            spawn.ColorR = 255;
            spawn.ColorG = 255;
            spawn.ColorB = 255;

            spawn.Frame = _random.Int(0, frameCount - 1);
            spawn.FrameMax = frameCount - 1;
            spawn.Die = _clock.Now + life + _random.Float(0f, 4f);

            if (_pool.Spawn(pos, modelIndex, TempEntPriority.Low, in spawn) < 0)
                return;
        }
    }

    /// <summary>
    /// <c>CTempEnts::RocketFlare</c>'s record, into a slot already built of
    /// <c>sprites/animglow01.vmt</c> (<see cref="RocketFlareModel"/>): a
    /// one-hundredth-of-a-second glow on a random frame.
    /// <paramref name="frameCount"/> is the loaded model's.
    /// </summary>
    public bool RocketFlareInto(int slot, int frameCount)
    {
        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.FrameMax = frameCount - 1;
        spawn.RenderMode = (int)RenderMode.Glow;
        spawn.RenderFx = RenderFx.NoDissipation;
        spawn.RenderAmt = 255;
        spawn.FrameRate = 1f;
        spawn.Frame = _random.Int(0, frameCount - 1);
        spawn.SpriteScale = 1f;
        spawn.Die = _clock.Now + 0.01f;

        return _pool.WriteSpawnInto(slot, in spawn);
    }

    /// <summary>The sprite <c>CTempEnts::RocketFlare</c> loads by name.</summary>
    public const string RocketFlareModel = "sprites/animglow01.vmt";

    /// <summary>
    /// <c>CTempEnts::SpawnTempModel</c>'s record, into a slot already built of
    /// the model: placed at <paramref name="angles"/>, body 0, the caller's
    /// flags ADDED, tumbling at up to 255 degrees a second on each axis, opaque,
    /// moving at <paramref name="velocity"/>, for <paramref name="life"/> seconds.
    /// </summary>
    public bool SpawnTempModelInto(int slot, Vector angles, Vector velocity, float life, int flags)
    {
        var spawn = TempEntSpawn.Prepared(_clock.Now);

        spawn.Angles = angles;
        spawn.Body = 0;
        spawn.Flags |= flags;
        spawn.AngularVelocity = new Vector(_random.Float(-255f, 255f),
                                           _random.Float(-255f, 255f),
                                           _random.Float(-255f, 255f));
        spawn.RenderMode = (int)RenderMode.Normal;
        spawn.RenderAmt = 255;
        spawn.Velocity = velocity;
        spawn.Die = _clock.Now + life;

        return _pool.WriteSpawnInto(slot, in spawn);
    }

}

/// <summary>
/// A <see cref="TempEntSpawns.DefaultSprite"/> tempent before it is built: stock
/// hands the caller the tempent and the caller finishes it
/// (<c>Sprite_Explode</c>, <c>Sprite_Smoke</c>); here the caller finishes this.
/// </summary>
public struct PendingTempEnt
{
    public Vector Origin;
    public int ModelIndex;
    public TempEntSpawn Spawn;
}

/// <summary>
/// The <c>TE_EXPLFLAG_*</c> bits <c>CTempEnts::Sprite_Explode</c> reads
/// (<c>game/shared/tempentity.h</c>). Not the whole set.
/// </summary>
public static class ExplosionFlags
{
    /// <summary><c>TE_EXPLFLAG_NOADDITIVE</c>: the sprite is drawn opaque.</summary>
    public const int NoAdditive = 0x1;

    /// <summary><c>TE_EXPLFLAG_DRAWALPHA</c>: the sprite is drawn alpha.</summary>
    public const int DrawAlpha = 0x10;

    /// <summary><c>TE_EXPLFLAG_ROTATE</c>: the sprite is rolled at random.</summary>
    public const int Rotate = 0x20;
}

/// <summary>
/// <c>public/const.h</c>'s <c>BOUNCE_*</c> — what a tempent's <c>hitSound</c>
/// carries, and what <c>CTempEnts::PlaySound</c> switches on when it lands.
///
/// <para>
/// The two here are the two <see cref="TempEntSpawns.EjectBrass"/> chooses
/// between. They are NOT the <c>TE_BOUNCE_*</c> series four lines below them in
/// the same header, which is a different vocabulary with overlapping values —
/// <c>TE_BOUNCE_SHELL</c> is 1 and <c>BOUNCE_SHELL</c> is 0x20 — and picking
/// the wrong series gives a case that lands in silence.
/// </para>
/// </summary>
public static class BounceSounds
{
    /// <summary><c>BOUNCE_SHELL</c>, 0x20.</summary>
    public const int Shell = 0x20;

    /// <summary>
    /// <c>BOUNCE_SHOTSHELL</c>, 0x80 — and it shares its value with
    /// <c>BREAK_SLAVE</c>, which is stock's own overlap and is why a tempent's
    /// hitSound and its break flags must never be read out of one another.
    /// </summary>
    public const int ShotShell = 0x80;
}

/// <summary>
/// <c>shared/shareddefs.h</c>'s <c>BREAK_*</c>, which arrive on
/// <c>DT_TEBreakModel</c>'s <c>m_nFlags</c> and are read by
/// <see cref="TempEntSpawns.BreakModel"/> and, on the far side of a bounce, by
/// <c>CTempEnts::PlaySound</c>.
/// </summary>
public static class BreakFlags
{
    /// <summary>Sounds and looks like glass, and bounces badly.</summary>
    public const int Glass = 0x01;

    public const int Metal = 0x02;
    public const int Flesh = 0x04;
    public const int Wood = 0x08;

    /// <summary>One gib in ten leaves a smoke trail.</summary>
    public const int Smoke = 0x10;

    /// <summary>Translucent even though it is not glass. Same render path.</summary>
    public const int Trans = 0x20;

    public const int Concrete = 0x40;

    /// <summary>
    /// Light off the batch that is already in the pool rather than starting a
    /// new one. See <see cref="BreakGroup"/>.
    /// </summary>
    public const int Slave = 0x80;
}

/// <summary>
/// The <c>kRenderFx*</c> values this port's spawn helpers set. Not the whole
/// enum: these are the two that appear in <c>c_te_legacytempents.cpp</c>'s
/// helpers, and a half-list of a native enum is a second definition of it
/// unless it says so — this one says so.
/// </summary>
public static class RenderFx
{
    /// <summary>
    /// <c>kRenderFxNoDissipation</c>. A sprite that does not lose brightness
    /// with distance, which is what makes a spray read at range.
    /// </summary>
    public const int NoDissipation = 14;

    /// <summary>
    /// <c>kRenderFxClampMinScale</c>. Holds a minimum on-screen size, so a
    /// blood sprite does not vanish to a pixel across a room.
    /// </summary>
    public const int ClampMinScale = 19;
}
