//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>C_LocalTempEntity::Frame</c> — one pooled temp entity's physics, WHOLE
/// (behaviour ledger rows C4 and C5).
///
/// <para>
/// <b>WHY THE WHOLE FUNCTION AND NOT THE COLLISION ARM.</b> Stock's
/// <c>Frame</c> integrates AND collides in one call: it moves the tempent by
/// its velocity, traces from where it WAS to where it now is, and puts it back
/// at the trace's end point if that line was blocked. Porting only the arm
/// would have meant native integrating and managed integrating, one after the
/// other, and every gib would have fallen at twice gravity.
/// </para>
///
/// <para>
/// <b>WHAT MADE THE ARM POSSIBLE</b>, which is what rows C4 and C5 had been
/// waiting on. Three things: a client trace with NAMED FILTER MODES, because
/// the arm needs two of them and <c>CTraceFilterSimple</c> is not
/// <c>CTraceFilterHitAll</c> with one entity removed; the bounce sound (TempEntSounds),
/// without which every bouncing gib is silent and silently so; and a
/// <see cref="TempEntTransform"/> that C# OWNS, because a per-field accessor
/// would have been a dozen ABI crossings per tempent per frame.
/// </para>
///
/// <para>
/// <b>THE STATE THAT STAYS HERE.</b> <c>m_vecPrevLocalOrigin</c>,
/// <c>m_vLastCollisionOrigin</c> and <c>m_iLastCollisionFrame</c> look like
/// shared state and are not — the client's only readers and writers of all
/// three are inside <c>Frame</c> itself. They are this class's working values
/// and deliberately absent from <see cref="TempEntTransform"/>, which
/// <c>TempEntTransformTests</c> asserts.
/// </para>
///
/// <para>
/// <b>WHAT DELIBERATELY DOES NOT HAPPEN HERE.</b> <c>PlaySound</c> stays
/// native (the soundscript, the engine's random stream and its one-bounce-in-six
/// rate limit are all on that side), and so does the drawing. This class decides
/// WHERE a tempent is and WHAT it just hit; native renders what it is told.
/// </para>
/// </summary>
public sealed class ClientTempEntFrame : IClientTempEntSimulation
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineEffects _effects;
    private readonly ITempEntBounceSounds _bounceSounds;
    private readonly IEngineWorldQuery _world;
    private readonly IGameClock _clock;

    /// <summary>
    /// Where <c>CL_AllocDlight</c> lives on this ABI. <c>FTENT_FLICKER</c> is
    /// its one caller here.
    /// </summary>
    private readonly IEngineVisibility _visibility;

    /// <summary>
    /// Who owns a client entity index, for row C4's two questions. Null in a
    /// test that is not about them — and NOT null in the game, where
    /// <c>ClientModule</c> registers the registry before the pool.
    /// </summary>
    private readonly IClientStandalones? _standalones;

    /// <summary>
    /// Stock's <c>cl_fasttempentcollision</c> default, and it is a CONSTANT
    /// here rather than a convar read — measured, not assumed.
    ///
    /// <para>
    /// The convar is declared in <c>c_te_legacytempents.cpp</c> and its only
    /// reader was <c>Frame</c>'s own fast-collision branch, which is gated on
    /// <c>FTENT_USEFASTCOLLISIONS</c>. NOTHING IN THIS BUILD SETS THAT FLAG: its
    /// two setters are <c>tf_projectile_nail.cpp</c> and
    /// <c>tf_projectile_energy_ring.cpp</c>, and TF is not in this build — the
    /// same measurement that deleted the <c>FTENT_COLLISIONGROUP</c> branch,
    /// recorded in stock's own file.
    /// </para>
    ///
    /// <para>
    /// So moving the convar to managed would have been registering a name a
    /// player can set that nothing can read the effect of. It stays native and
    /// settable, this stands in for its default, and
    /// <c>TempEntFrameTests.TheFastCollisionDefaultIsTheOneStockDeclares</c>
    /// reads that declaration off disk so the stand-in cannot drift. If the flag
    /// ever becomes reachable, <see cref="SayFastCollisionUsesStocksDefault"/>
    /// says so once per level at the moment it first matters.
    /// </para>
    /// </summary>
    public const int FastCollisionFramesDefault = 5;

    /// <summary>
    /// Stock's <c>TENT_WIND_ACCEL</c> (<c>c_te_legacytempents.cpp:77</c>) — how
    /// fast a <c>FTENT_WINDBLOWN</c> tempent is dragged towards the wind.
    /// </summary>
    public const float WindAccel = 50f;

    public ClientTempEntFrame(ILog log, IEngineEffects effects, IEngineWorldQuery world,
                              IEngineVisibility visibility, IGameClock clock,
                              ITempEntBounceSounds bounceSounds,
                              IClientStandalones? standalones = null)
    {
        _bounceSounds = bounceSounds;

        _log = log;
        _effects = effects;
        _world = world;
        _visibility = visibility;
        _clock = clock;
        _standalones = standalones;
    }

    //-------------------------------------------------------------------------
    // The per-slot working state.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What this class holds about one slot between frames.
    ///
    /// <para>
    /// <see cref="Transform"/> is here because C# OWNS it — read ONCE when the
    /// pool takes the tempent over and written back once per frame. The other
    /// four are <c>Frame</c>'s own working values, which never cross at all.
    /// </para>
    /// </summary>
    private struct SlotState
    {
        /// <summary>False until the takeover read has happened for this tenant.</summary>
        public bool TakenOver;

        public TempEntTransform Transform;
        public TempEntStatics Statics;

        /// <summary>
        /// <c>m_vecPrevLocalOrigin</c> — where it was before this frame moved
        /// it, which is where the collision trace starts.
        /// </summary>
        public Vector PrevOrigin;

        /// <summary><c>m_vLastCollisionOrigin</c>.</summary>
        public Vector LastCollisionOrigin;

        /// <summary><c>m_iLastCollisionFrame</c>.</summary>
        public int LastCollisionFrame;
    }

    /// <summary>
    /// By slot, grown on demand.
    ///
    /// <para>
    /// AN ARRAY AND NOT A DICTIONARY, because it is indexed once or twice per
    /// live tempent per frame for up to five hundred of them, and because the
    /// slot numbers are dense by construction — the pool hands out 0..capacity-1
    /// and nothing else. It is grown rather than sized from
    /// <c>TempEntPoolCapacity</c> so that this class needs no start-up order
    /// relative to the pool's first question to native.
    /// </para>
    /// </summary>
    private SlotState[] _slots = Array.Empty<SlotState>();

    private ref SlotState Slot(int slot)
    {
        if (slot >= _slots.Length)
        {
            int size = _slots.Length == 0 ? 64 : _slots.Length;

            while (size <= slot)
                size *= 2;

            Array.Resize(ref _slots, size);
        }

        return ref _slots[slot];
    }

    /// <summary>
    /// The owner of the slots something other than this step moves (the
    /// clientside physics props); set by the composition. Asked first each
    /// step and told when a slot is freed.
    /// </summary>
    public ITempEntDriver? Driver { get; set; }

    /// <inheritdoc/>
    public void Forget(int slot)
    {
        if (slot >= 0 && slot < _slots.Length)
            _slots[slot] = default;

        Driver?.Forget(slot);
    }

    /// <inheritdoc/>
    public void LevelInit()
    {
        _saidPropOrDoorIsUnanswerable = false;
        _saidImpactEffectIsRefused = false;
        _saidSmokeTrailIsUnimplemented = false;
        _saidFastCollisionUsesStocksDefault = false;
        _saidRenderGroupChangeIsNotCarried = false;
    }

    /// <summary>
    /// The transform this class currently holds for a slot, for a test to read
    /// back. False when the slot has never been stepped.
    /// </summary>
    public bool TransformOf(int slot, out TempEntTransform transform)
    {
        if (slot >= 0 && slot < _slots.Length && _slots[slot].TakenOver)
        {
            transform = _slots[slot].Transform;
            return true;
        }

        transform = default;
        return false;
    }

    //-------------------------------------------------------------------------
    // The port.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool Step(int slot, float frameTime, int frameNumber, int frameCount, ref TempEntLife life)
    {
        ref SlotState state = ref Slot(slot);

        if (!state.TakenOver)
        {
            // THE TAKEOVER, and it is here rather than at the reservation on
            // purpose: native fills a tempent AFTER the slot is handed out --
            // SpawnTempModel sets the velocity and the angles on the object
            // TempEntAlloc just returned -- so a read taken at Reserve would
            // capture Prepare's defaults and nothing the spawn helper decided.
            // The first Step is the first moment every spawn route has finished.
            if (!_effects.TempEntPoolReadTransform(slot, out state.Transform))
                return false;

            if (!_effects.TempEntPoolReadStatics(slot, out state.Statics))
                return false;

            state.TakenOver = true;
        }

        // A driven slot (a clientside physics prop): its object says where it
        // is, and none of the ballistic step below applies.
        if (Driver is not null && Driver.Drive(slot, ref state.Transform))
        {
            WriteBack(slot, ref state);
            return true;
        }

        float fastFreq = _clock.Now * 5.5f;
        float gravity = -frameTime * _effects.CurrentGravity();
        float gravitySlow = gravity * 0.5f;
        float traceFraction = 1f;

        // Stock's `Assert( !GetMoveParent() )` is not carried, for the reason
        // its own comment in c_te_legacytempents.cpp gives: it guarded the
        // identity that local and abs are the same vector, and that identity is
        // structural now -- a tempent has no parent for one to be set to.
        state.PrevOrigin = state.Transform.Origin;

        state.Transform.Velocity += state.Transform.Acceleration * frameTime;

        int flags = life.Flags;

        if ((flags & (int)TempEntFlags.PlyrAttachment) != 0)
        {
            // A client index that names nothing simply does not move the
            // tempent, which is stock's own `if ( IClientEntity *pClient = ... )`.
            if (_effects.ClientEntityOrigin(life.ClientIndex, out Vector player))
                state.Transform.Origin = player + state.Statics.TentOffset;
        }
        else if ((flags & (int)TempEntFlags.SineWave) != 0)
        {
            state.Transform.SineX += state.Transform.Velocity.X * frameTime;
            state.Transform.SineY += state.Transform.Velocity.Y * frameTime;

            // The Z velocity is used as a PHASE in the first two terms and as a
            // speed in the third. That is stock, not a transcription slip: a
            // bubble's wobble is seeded from how fast it is rising.
            state.Transform.Origin = new Vector(
                state.Transform.SineX
                    + MathF.Sin(state.Transform.Velocity.Z + _clock.Now) * (10f * state.Transform.SpriteScale),
                state.Transform.SineY
                    + MathF.Sin(state.Transform.Velocity.Z + fastFreq + 0.7f) * (8f * state.Transform.SpriteScale),
                state.Transform.Origin.Z + state.Transform.Velocity.Z * frameTime);
        }
        else if ((flags & (int)TempEntFlags.Spiral) != 0)
        {
            // Stock computes SinCos( vel.z + fastFreq ) here and then USES
            // NEITHER result -- the offsets below are sin( curtime * 20 ) and
            // sin( curtime * 30 ). The dead call is not transcribed; deleting it
            // is the only faithful reading, because writing it would be writing
            // two variables nothing reads.
            state.Transform.Origin += new Vector(
                state.Transform.Velocity.X * frameTime + 8f * MathF.Sin(_clock.Now * 20f),
                state.Transform.Velocity.Y * frameTime + 4f * MathF.Sin(_clock.Now * 30f),
                state.Transform.Velocity.Z * frameTime);
        }
        else
        {
            state.Transform.Origin += state.Transform.Velocity * frameTime;
        }

        if ((flags & (int)TempEntFlags.SprAnimate) != 0)
        {
            state.Transform.Frame += frameTime * state.Statics.FrameRate;

            if (state.Transform.Frame >= state.Statics.FrameMax)
            {
                // The fractional part, by C's truncation toward zero.
                state.Transform.Frame -= (int)state.Transform.Frame;

                if ((flags & (int)TempEntFlags.SprAnimateLoop) == 0)
                {
                    // This animating sprite is not set to loop, so it asks to
                    // die. The `die = 0` is stock's and is written back through
                    // the life record the caller holds.
                    life.Die = 0f;
                    life.Flags = flags;
                    WriteBack(slot, ref state);
                    return false;
                }
            }
        }
        else if ((flags & (int)TempEntFlags.SprCycle) != 0)
        {
            state.Transform.Frame += frameTime * 10f;

            if (state.Transform.Frame >= state.Statics.FrameMax)
                state.Transform.Frame -= (int)state.Transform.Frame;
        }

        if ((flags & (int)TempEntFlags.SmokeGrowAndFade) != 0)
        {
            // Stock's alpha ramp beside this is commented out in stock. Only
            // the scale grows.
            state.Transform.SpriteScale += frameTime * 0.5f;
        }

        if ((flags & (int)TempEntFlags.Rotate) != 0)
        {
            state.Transform.Angles = new QAngle(
                state.Transform.Angles.Pitch + state.Transform.AngVelocity.Pitch * frameTime,
                state.Transform.Angles.Yaw + state.Transform.AngVelocity.Yaw * frameTime,
                state.Transform.Angles.Roll + state.Transform.AngVelocity.Roll * frameTime);
        }
        else if ((flags & (int)TempEntFlags.AlignToMotion) != 0)
        {
            if (state.Transform.Velocity.Length > 0f)
                state.Transform.Angles = QAngle.FromVector(Angles.FromVector(state.Transform.Velocity));
        }

        if ((flags & (int)(TempEntFlags.CollideAll | TempEntFlags.CollideWorld | TempEntFlags.CollideProps)) != 0)
        {
            traceFraction = Collide(slot, ref state, ref life, ref flags, gravity, frameCount,
                                    out bool passedThrough);

            if (passedThrough)
            {
                // STOCK RETURNS FROM Frame HERE, not from the collision block:
                // its `return true` for a FTENT_COLLIDEPROPS tempent that hit
                // something which is neither prop, door nor world skips the
                // dlight, the gravity and the wind for this frame as well as
                // the bounce. A port that only skipped the bounce would apply
                // gravity twice over two frames to anything flying through a
                // crowd, which is a difference nothing would ever be traced
                // back to.
                life.Flags = flags;
                WriteBack(slot, ref state);
                return true;
            }
        }

        if ((flags & (int)TempEntFlags.Flicker) != 0 && frameNumber == state.Statics.FlickerFrame)
        {
            // stock: effects->CL_AllocDlight( LIGHT_INDEX_TE_DYNAMIC ), radius
            // 60, colour 255/120/0, die = curtime + 0.01. Every other field of a
            // freshly allocated dlight is zero, which is what the defaults here
            // are -- CL_AllocDlight clears the slot before handing it back.
            _visibility.DynamicLightUpdate(new DynamicLight
            {
                Key = ClientTeDynamicLight.TeDynamicLightKey,
                OriginX = state.Transform.Origin.X,
                OriginY = state.Transform.Origin.Y,
                OriginZ = state.Transform.Origin.Z,
                Radius = 60f,
                Red = 255,
                Green = 120,
                Blue = 0,
                LiveFor = 0.01f,
            });
        }

        if ((flags & (int)TempEntFlags.SmokeTrail) != 0)
        {
            // Stock is `Assert( !"FIXME: Rework smoketrail to be client side" )`
            // and nothing else, so the flag's only effect in this engine is to
            // halt a debug client. The managed pool MASKS the flag off at spawn
            // for exactly that reason and says so once per level, so this arm
            // is unreachable from a managed spawn -- it is here for the native
            // spawn routes, and it says the same thing rather than asserting.
            SaySmokeTrailIsUnimplemented();
        }

        // Gravity, only if nothing was hit this frame.
        if (traceFraction == 1f)
        {
            if ((flags & (int)TempEntFlags.Gravity) != 0)
                state.Transform.Velocity.Z += gravity;
            else if ((flags & (int)TempEntFlags.SlowGravity) != 0)
                state.Transform.Velocity.Z += gravitySlow;
        }

        if ((flags & (int)TempEntFlags.WindBlown) != 0)
        {
            Vector wind = _effects.ClientWindSpeed(_clock.Now);

            // X and Y only -- stock's loop is `i < 2`. Wind does not lift.
            state.Transform.Velocity.X = TowardsWind(state.Transform.Velocity.X, wind.X, frameTime);
            state.Transform.Velocity.Y = TowardsWind(state.Transform.Velocity.Y, wind.Y, frameTime);
        }

        life.Flags = flags;
        WriteBack(slot, ref state);
        return true;
    }

    /// <summary>
    /// One axis of stock's wind loop: accelerate towards the wind at
    /// <see cref="WindAccel"/> and CLAMP rather than overshoot, in whichever
    /// direction is nearer.
    /// </summary>
    private static float TowardsWind(float velocity, float wind, float frameTime)
    {
        if (velocity < wind)
        {
            velocity += frameTime * WindAccel;

            if (velocity > wind)
                velocity = wind;
        }
        else if (velocity > wind)
        {
            velocity -= frameTime * WindAccel;

            if (velocity < wind)
                velocity = wind;
        }

        return velocity;
    }

    /// <summary>Push the transform back into the object native draws.</summary>
    private void WriteBack(int slot, ref SlotState state)
        => _effects.TempEntPoolWriteTransform(slot, in state.Transform);

    //-------------------------------------------------------------------------
    // The collision arm — behaviour ledger rows C4 and C5.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock's <c>if ( flags &amp; (FTENT_COLLIDEALL | FTENT_COLLIDEWORLD |
    /// FTENT_COLLIDEPROPS) )</c> block, whole. Answers the trace fraction,
    /// which is 1 when nothing was hit — that is what decides whether gravity
    /// is applied this frame.
    /// </summary>
    /// <param name="passedThrough">
    /// True when stock's <c>return true</c> fired — a <c>FTENT_COLLIDEPROPS</c>
    /// tempent hit something that is neither a prop, nor a door, nor the world.
    /// The CALLER must then return from the frame, because stock's return is
    /// from <c>Frame</c> and not from this block: it skips the dlight, the
    /// gravity and the wind as well as the bounce.
    /// </param>
    private float Collide(int slot, ref SlotState state, ref TempEntLife life, ref int flags,
                          float gravity, int frameCount, out bool passedThrough)
    {
        passedThrough = false;

        var traceNormal = Vector.Zero;
        float traceFraction = 1f;

        bool hitWorld = false;
        int surfaceFlags = 0;
        var endPos = state.Transform.Origin;

        if ((flags & (int)(TempEntFlags.CollideAll | TempEntFlags.CollideProps)) != 0)
        {
            Vector from = state.PrevOrigin;
            bool shouldCollide = true;

            if (FastCollisionFrames > 0 && (flags & (int)TempEntFlags.UseFastCollisions) != 0)
            {
                SayFastCollisionUsesStocksDefault();

                if (state.LastCollisionFrame + FastCollisionFrames > frameCount)
                {
                    shouldCollide = false;
                }
                else
                {
                    // Stock's test is against vec3_origin, so a tempent whose
                    // last collision genuinely ended at the world origin traces
                    // from its previous position instead. Stock's behaviour and
                    // stock's corner, carried rather than corrected.
                    if (state.LastCollisionOrigin.X != 0f
                     || state.LastCollisionOrigin.Y != 0f
                     || state.LastCollisionOrigin.Z != 0f)
                    {
                        from = state.LastCollisionOrigin;
                    }

                    state.LastCollisionFrame = frameCount;
                    shouldCollide = true;
                }
            }

            if (shouldCollide)
            {
                // CTraceFilterSimple with the owner passed through and the
                // collision group always COLLISION_GROUP_NONE. Stock's own
                // comment says why the group is a constant: nothing in this
                // build ever calls SetCollisionGroup on a tempent.
                ClientTraceResult trace = _world.ClientTraceLine(
                    from, state.Transform.Origin, TraceMask.Solid,
                    TraceFilter.Simple, state.Statics.OwnerEntityIndex);

                if ((flags & (int)TempEntFlags.CollideProps) != 0 && trace.EntityIndex >= 0)
                {
                    // ---------------------------------------------------------
                    // BEHAVIOUR LEDGER ROW C4, and this is the question it is
                    // about. Stock asks dynamic_cast<CDynamicProp *> and
                    // dynamic_cast<CBaseDoor *> of what it hit; in this build
                    // both were hardcoded false, because a managed prop or door
                    // is not a native type to cast to -- so every entity hit
                    // read as neither, and the tempent bounced off ALL of them
                    // rather than passing through the ones stock passes through.
                    //
                    // Asked on this side, of entities this side owns, it is an
                    // ordinary question: the trace answers an INDEX and the
                    // client registry resolves one.
                    // ---------------------------------------------------------
                    if (!IsPropOrDoor(trace.EntityIndex) && !DidHitWorld(trace.EntityIndex))
                    {
                        // And NOT `m_vLastCollisionOrigin = trace.endpos` on the
                        // way out: stock's return is above that line.
                        passedThrough = true;
                        return 1f;
                    }
                }

                // "Make sure it didn't bump into itself... (?!?)" -- stock's own
                // comment and stock's own test.
                if (trace.Fraction != 1f
                 && (DidHitWorld(trace.EntityIndex) || trace.EntityIndex != life.ClientIndex))
                {
                    traceFraction = trace.Fraction;
                    traceNormal = trace.Normal;
                    hitWorld = DidHitWorld(trace.EntityIndex);
                    surfaceFlags = trace.SurfaceFlags;
                    endPos = trace.End;
                }

                state.LastCollisionOrigin = trace.End;
            }
        }
        else if ((flags & (int)TempEntFlags.CollideWorld) != 0)
        {
            ClientTraceResult trace = _world.ClientTraceLine(
                state.PrevOrigin, state.Transform.Origin, TraceMask.Solid, TraceFilter.WorldOnly);

            if (trace.Fraction != 1f)
            {
                traceFraction = trace.Fraction;
                traceNormal = trace.Normal;
                hitWorld = DidHitWorld(trace.EntityIndex);
                surfaceFlags = trace.SurfaceFlags;
                endPos = trace.End;
            }
        }

        if (traceFraction == 1f)
            return 1f;

        // "Decent collision now, and damping works."
        state.Transform.Origin = endPos;

        float damp = state.Statics.BounceFactor;

        if ((flags & (int)(TempEntFlags.Gravity | TempEntFlags.SlowGravity)) != 0)
        {
            damp *= 0.5f;

            if (traceNormal.Z > 0.9f)
            {
                // Hit floor, and slow enough that one more frame of gravity
                // would not lift it: stop dead and stop tracing.
                if (state.Transform.Velocity.Z <= 0f && state.Transform.Velocity.Z >= gravity * 3f)
                {
                    damp = 0f;

                    flags &= ~(int)(TempEntFlags.Rotate | TempEntFlags.Gravity | TempEntFlags.SlowGravity
                                  | TempEntFlags.CollideWorld | TempEntFlags.SmokeTrail);

                    state.Transform.Angles = state.Transform.Angles with { Pitch = 0f, Roll = 0f };
                }
            }
        }

        if ((flags & (int)TempEntFlags.ChangeRenderOnCollide) != 0)
        {
            // stock: m_RenderGroup = RENDER_GROUP_OTHER, which every tempent
            // ALREADY IS -- TempEntAlloc and TempEntAllocHigh both set it on the
            // line before they hand the object back, and the one thing that sets
            // anything else is CSEjectBrass's view-model brass
            // (RENDER_GROUP_VIEW_MODEL_OPAQUE). So the write matters for exactly
            // that case and is the one field of this port with no slot.
            //
            // The FLAG is cleared here as stock clears it, so the arm runs once;
            // the render group itself is behaviour ledger row C4's one measured
            // leftover and is named in the row rather than left silent.
            flags &= ~(int)TempEntFlags.ChangeRenderOnCollide;
            SayRenderGroupChangeIsNotCarried();
        }

        // stock: tempents->PlaySound( this, damp ), which read the tempent's
        // origin (already at the contact point) and its velocity (not yet
        // reflected) at this line.
        if (state.Statics.HitSound != 0)
            _bounceSounds.PlaySound(state.Statics.HitSound, damp, state.Transform.Velocity, state.Transform.Origin);

        if (state.Statics.HasImpactEffect != 0)
            SayImpactEffectIsRefused();

        if ((flags & (int)TempEntFlags.CollideKill) != 0)
        {
            // Die on impact, and do not fade out on the way.
            flags &= ~(int)TempEntFlags.FadeOut;
            life.Die = _clock.Now;
        }
        else if ((flags & (int)TempEntFlags.AttachToTarget) != 0)
        {
            // SURF_SKY is 0x0004, and the skybox is the one surface a tempent
            // must not stick to.
            if (hitWorld && (surfaceFlags & SurfSky) == 0)
            {
                state.Transform.Velocity = Vector.Zero;
                state.Transform.Acceleration = Vector.Zero;

                // Remove movement flags so we don't keep tracing.
                flags &= ~(int)(TempEntFlags.CollideAll | TempEntFlags.CollideWorld);
            }
            else
            {
                // Couldn't attach to this entity. Die.
                flags &= ~(int)TempEntFlags.FadeOut;
                life.Die = _clock.Now;
            }
        }
        else
        {
            if (damp != 0f)
            {
                // Reflect the velocity through the surface.
                float proj = state.Transform.Velocity.Dot(traceNormal);
                state.Transform.Velocity += traceNormal * (-proj * 2f);

                // Reflect rotation (fake).
                state.Transform.Angles = state.Transform.Angles with
                {
                    Yaw = -state.Transform.Angles.Yaw,
                };
            }

            if (damp != 1f)
            {
                state.Transform.Velocity *= damp;

                state.Transform.Angles = new QAngle(
                    state.Transform.Angles.Pitch * 0.9f,
                    state.Transform.Angles.Yaw * 0.9f,
                    state.Transform.Angles.Roll * 0.9f);
            }
        }

        return traceFraction;
    }

    /// <summary>
    /// <c>SURF_SKY</c> (bspflags.h). The one surface bit this arm tests: a
    /// <c>FTENT_ATTACHTOTARGET</c> tempent that hit the skybox has not hit
    /// anything to stick to.
    /// </summary>
    public const int SurfSky = 0x0004;

    /// <summary>
    /// The convar's value, as this class reads it. Overridable so a test can
    /// exercise the fast-collision branch without an engine; see
    /// <see cref="FastCollisionFramesDefault"/> for why it is not a convar read.
    /// </summary>
    public int FastCollisionFrames { get; set; } = FastCollisionFramesDefault;

    /// <summary>
    /// <c>trace.DidHitWorld()</c>. Entity index zero is the world entity, which
    /// is what the engine puts in a trace that stopped on a brush.
    /// </summary>
    private static bool DidHitWorld(int entityIndex) => entityIndex == 0;

    /// <summary>
    /// Row C4's two casts, asked of this side: is the entity at this index a
    /// <c>CDynamicProp</c> or a <c>CBaseDoor</c>?
    ///
    /// <para>
    /// BY CLASSNAME AND NOT BY MANAGED TYPE, because the two hierarchies do not
    /// line up: stock's <c>CBasePropDoor</c> derives from <c>CDynamicProp</c>,
    /// so a <c>prop_door_rotating</c> answers TRUE to the first cast, and
    /// <c>func_water</c> is a <c>CBaseDoor</c> and answers true to the second.
    /// Both are surprises that a managed class hierarchy would have got wrong,
    /// and both are exactly what the names below say.
    /// </para>
    ///
    /// <para>
    /// AN INDEX THIS SIDE DOES NOT OWN IS UNANSWERABLE, and the answer then is
    /// TRUE — which is what this build already does and is right for the two
    /// classes the row is named for. It is said once per level rather than
    /// assumed, because it is the part of row C4 that is still open: closing it
    /// needs a managed client half for <c>prop_dynamic</c> and the doors, which
    /// this build does not have (their logic is entirely server-side, so the
    /// client publishes no type for them and their shells carry no classname).
    /// </para>
    /// </summary>
    private bool IsPropOrDoor(int entityIndex)
    {
        if (DidHitWorld(entityIndex))
            return false;

        ClientStandaloneEntity? struck = _standalones?.GetByIndex(entityIndex);

        if (struck is null)
        {
            SayPropOrDoorIsUnanswerable(entityIndex);
            return true;
        }

        return IsDynamicProp(struck.ClassName) || IsDoor(struck.ClassName);
    }

    /// <summary>
    /// The classnames stock's <c>dynamic_cast&lt;CDynamicProp *&gt;</c> answers
    /// true for, read off <c>props.cpp</c>'s own <c>LINK_ENTITY_TO_CLASS</c>
    /// lines.
    ///
    /// <para>
    /// <c>prop_door_rotating</c> is in the list and is not a mistake:
    /// <c>CPropDoorRotating : CBasePropDoor : CDynamicProp</c>
    /// (<c>BasePropDoor.h:36</c>), so stock's PROP cast catches it before the
    /// door cast ever runs.
    /// </para>
    /// </summary>
    public static bool IsDynamicProp(string className)
        => className == EntityNames.PropDynamic
        || className == EntityNames.PropDynamicOverride
        || className == EntityNames.PropDynamicOrnament
        || className == EntityNames.DynamicProp
        || className == EntityNames.PropDoorRotating;

    /// <summary>
    /// The classnames stock's <c>dynamic_cast&lt;CBaseDoor *&gt;</c> answers
    /// true for, read off <c>doors.cpp</c>'s <c>LINK_ENTITY_TO_CLASS</c> lines.
    ///
    /// <para>
    /// <c>func_water</c> is one of them — <c>doors.cpp:110</c> links it straight
    /// to <c>CBaseDoor</c> — which nothing about the name suggests.
    /// </para>
    /// </summary>
    public static bool IsDoor(string className)
        => className == EntityNames.FuncDoor
        || className == EntityNames.FuncDoorRotating
        || className == EntityNames.FuncWater;

    //-------------------------------------------------------------------------
    // What is said out loud, once per level. See IClientTempEntLevel.
    //-------------------------------------------------------------------------

    private bool _saidPropOrDoorIsUnanswerable;
    private bool _saidImpactEffectIsRefused;
    private bool _saidSmokeTrailIsUnimplemented;
    private bool _saidFastCollisionUsesStocksDefault;

    /// <summary>
    /// Row C4's remaining half: a trace hit something this side owns no managed
    /// half for, so neither of stock's two casts can be asked of it.
    /// </summary>
    private void SayPropOrDoorIsUnanswerable(int entityIndex)
    {
        if (_saidPropOrDoorIsUnanswerable)
            return;

        _saidPropOrDoorIsUnanswerable = true;

        _log.Warning(
            $"[SourceSharp/Client] a FTENT_COLLIDEPROPS tempent hit entity {entityIndex}, which "
          + "this side has no managed half for, so neither dynamic_cast<CDynamicProp*> nor "
          + "dynamic_cast<CBaseDoor*> can be asked of it (behaviour ledger row C4). It is "
          + "treated as a prop, which is what this build already did and is right for "
          + "prop_dynamic and the doors the row is named for -- and wrong for anything else it "
          + "could have hit. Closing this needs a managed CLIENT half for prop_dynamic and the "
          + "doors: their logic is entirely server-side, so the client publishes no type for "
          + "them and their shells carry no classname to ask about. Said once per level.\n");
    }

    /// <summary>Row C5, kept refused and loud rather than fabricated.</summary>
    private void SayImpactEffectIsRefused()
    {
        if (_saidImpactEffectIsRefused)
            return;

        _saidImpactEffectIsRefused = true;

        _log.Warning(
            "[SourceSharp/Client] a pooled tempent collided with m_pszImpactEffect set, and no "
          + "impact effect is dispatched (behaviour ledger row C5). Stock builds a CEffectData "
          + "from the trace and sets m_nDamageType to the TEAM NUMBER of the client entity the "
          + "tempent belongs to, through dynamic_cast<C_BasePlayer*> -- and no client entity in "
          + "this build is a C_BasePlayer to cast to. The effect NAME is deliberately not "
          + "carried across the ABI either: this branch is UNREACHABLE here, because "
          + "m_pszImpactEffect is only set by CTempEnts::ClientProjectile's optional parameter "
          + "and both live callers leave it at its NULL default "
          + "(src/sourcesharp/test/tempent_verdict_test.cpp asserts exactly that), and a slot "
          + "carrying a name nothing sets would be a capability invented for a branch nothing "
          + "takes. If you are reading this line, that measurement has changed. "
          + "Said once per level.\n");
    }

    /// <summary>
    /// <c>FTENT_SMOKETRAIL</c> is unimplemented in stock and asserts. The
    /// managed pool masks it off at spawn; this arm is what a NATIVE spawn route
    /// reaches.
    /// </summary>
    private void SaySmokeTrailIsUnimplemented()
    {
        if (_saidSmokeTrailIsUnimplemented)
            return;

        _saidSmokeTrailIsUnimplemented = true;

        _log.Warning(
            "[SourceSharp/Client] a pooled tempent is carrying FTENT_SMOKETRAIL and no trail is "
          + "drawn. c_te_legacytempents.cpp implements that flag as an Assert naming a FIXME and "
          + "nothing else, so stock's own behaviour here is to halt a debug client; this says so "
          + "instead. Stock's hole, not this port's. Said once per level.\n");
    }

    /// <summary>
    /// The one place the fast-collision stand-in could matter, said at the
    /// moment it first does rather than at load.
    /// </summary>
    private void SayFastCollisionUsesStocksDefault()
    {
        if (_saidFastCollisionUsesStocksDefault)
            return;

        _saidFastCollisionUsesStocksDefault = true;

        _log.Warning(
            $"[SourceSharp/Client] a tempent carrying FTENT_USEFASTCOLLISIONS was stepped, and "
          + $"the managed arm is using stock's declared cl_fasttempentcollision default "
          + $"({FastCollisionFramesDefault}) rather than the convar's current value -- setting "
          + "that convar will not change this. Nothing in this build sets that flag (its two "
          + "setters are tf_projectile_nail.cpp and tf_projectile_energy_ring.cpp, and TF is not "
          + "in this build), which is why the convar was left native and unread. If you are "
          + "reading this line, that measurement has changed and the convar wants moving to "
          + "managed. Said once per level.\n");
    }

    private bool _saidRenderGroupChangeIsNotCarried;

    /// <summary>
    /// <c>FTENT_CHANGERENDERONCOLLIDE</c>'s render-group write, which has no
    /// ABI slot. Row C4's one measured leftover.
    /// </summary>
    private void SayRenderGroupChangeIsNotCarried()
    {
        if (_saidRenderGroupChangeIsNotCarried)
            return;

        _saidRenderGroupChangeIsNotCarried = true;

        _log.Warning(
            "[SourceSharp/Client] a FTENT_CHANGERENDERONCOLLIDE tempent bounced and its render "
          + "group was not moved to RENDER_GROUP_OTHER (behaviour ledger row C4). Every pooled "
          + "tempent is ALREADY in that group -- TempEntAlloc and TempEntAllocHigh both set it "
          + "on the line before they hand the object back -- so this is a no-op for all but one "
          + "case: CSEjectBrass's view-model brass, which is put in "
          + "RENDER_GROUP_VIEW_MODEL_OPAQUE and which stock moves out of it on the first bounce. "
          + "The visible difference is a shell casing from a first-person weapon staying in the "
          + "view-model pass after it has landed. Said once per level.\n");
    }
}
