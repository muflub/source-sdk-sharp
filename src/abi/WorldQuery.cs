namespace SourceSharp.Abi;

/// <summary>
/// Which entities a CLIENT trace is allowed to hit — named modes, each one a
/// stock filter class, rather than an "ignore this entity" parameter.
///
/// <para>
/// AN IGNORE PARAMETER IS NOT A SUPERSET, which is the whole reason this is an
/// enumeration. It would have to be implemented with
/// <c>CTraceFilterSimple</c>, and that differs from <c>CTraceFilterHitAll</c>
/// in more than the pass entity: it also applies collision groups and
/// <c>g_pGameRules-&gt;ShouldCollide</c>. A caller passing "ignore nothing"
/// would not get the hit-all trace it asked for — it would get a filtered one
/// that agrees on most maps and disagrees wherever a collision group or a game
/// rule says otherwise, which shows up as one gib falling through one door.
/// </para>
/// </summary>
public enum TraceFilter
{
    /// <summary>
    /// <c>CTraceFilterHitAll</c> — everything, entities included. What the
    /// trace did before there were modes, and value zero so that is still what
    /// a caller gets by not choosing.
    /// </summary>
    HitAll = 0,

    /// <summary>
    /// <c>CTraceFilterSimple</c>, passing through one entity and applying
    /// collision groups and the game rules. The collision group is always
    /// <c>COLLISION_GROUP_NONE</c> and is deliberately not a parameter: the
    /// legacy tempent block passes that constant and says why — nothing in this
    /// build ever calls <c>SetCollisionGroup</c> on a tempent.
    /// </summary>
    Simple = 1,

    /// <summary>
    /// <c>CTraceFilterWorldOnly</c> — brushes and the world, no entities at
    /// all. What <c>FTENT_COLLIDEWORLD</c> traces with.
    /// </summary>
    WorldOnly = 2,

    /// <summary>
    /// <c>CTraceFilterNoNPCsOrPlayer</c>, passing one entity: what the chase
    /// camera traces with (C7 item 5).
    /// </summary>
    NoNpcsOrPlayer = 3,

    /// <summary>
    /// flashlighteffect.cpp's <c>CTraceFilterSkipPlayerAndViewModel</c>: a plain
    /// <c>CTraceFilter</c> (no pass entity, no game rules) that hits everything
    /// but a player, a view model, and an entity in the two debris collision
    /// groups. What the flashlight traces with (C7 cluster 12).
    /// </summary>
    SkipPlayerAndViewModel = 4,

    /// <summary>
    /// c_pixel_visibility.cpp's <c>CTraceFilterGlow</c>: a
    /// <c>CTraceFilterSimple</c> with no pass entity that only an entity whose
    /// collideable is <c>SOLID_VPHYSICS</c> or <c>SOLID_BSP</c> may stop. What a
    /// glow's sight line traces with (C7 pixel visibility).
    /// </summary>
    Glow = 5,
}

/// <summary>
/// What a trace is allowed to hit.
///
/// Named rather than passed through as an engine MASK_* constant: which
/// contents stop a blast is a game decision, and the numbers behind it are
/// engine detail that managed code has no reason to mirror.
/// </summary>
public enum TraceMask
{
    /// <summary>
    /// World and brush entities only. This is the mask that answers "does a
    /// wall shield you from this" — it deliberately ignores props, so a blast
    /// is not stopped by the crate it is about to throw.
    /// </summary>
    Brushes = 0,

    /// <summary>Everything a bullet would hit, props included.</summary>
    Shot = 1,

    /// <summary>
    /// Brushes and water, which is what "can this be HEARD from there" means.
    /// A bullet crosses a water surface and a soundscape does not, and that is
    /// the difference this exists for: a soundscape on the far side of a pool
    /// should not reach a swimmer.
    /// </summary>
    BrushesAndWater = 2,

    /// <summary>
    /// Everything a ray can stop on — <c>MASK_ALL</c>. Brushes, props, players,
    /// windows, water, hitboxes and debris alike.
    ///
    /// <para>
    /// Added for the client trace, because it is what stock's projected decal
    /// traces with: a decal projected through a narrower mask lands on a
    /// different surface. Mapped on BOTH arms of the ABI, so that the value
    /// means one thing wherever it is passed — a mask the server's arm did not
    /// know would have been read there as <see cref="Brushes"/> and answered a
    /// plausible wrong number in silence.
    /// </para>
    /// </summary>
    All = 3,

    /// <summary>
    /// <c>MASK_SHOT_HULL</c> — what a blast line traces with. It is
    /// <see cref="Shot"/> with the HITBOX bit taken out and the GRATE bit put
    /// in (<c>bspflags.h:128</c> against <c>:127</c>), and both differences
    /// matter: a blast line that stopped on a player's hitbox would be blocked
    /// by a body it should have thrown, and one that ignored a grating would
    /// throw a ragdoll through a floor vent.
    ///
    /// <para>
    /// Added for the ragdoll blast push (behaviour ledger row C1), which is
    /// what <c>CRagdollExplosionEnumerator</c> traces with. Mapped on BOTH arms
    /// of the ABI for <see cref="All"/>'s reason — a mask one arm did not know
    /// would be read there as <see cref="Brushes"/> and answer a plausible
    /// wrong number in silence.
    /// </para>
    /// </summary>
    ShotHull = 4,
    /// <c>MASK_SOLID</c> — solid brushes, moveables, windows, monsters and
    /// grates. What a THING moving through the world runs into, as opposed to
    /// what a bullet hits.
    ///
    /// <para>
    /// Added for the legacy tempent pool's collision arm, the first caller on
    /// either side to want it. It is NOT a synonym for any of the four above:
    /// <see cref="Shot"/> adds debris and hitboxes and drops grates, so a gib
    /// would stop on a scrap of debris and fall through a grating, and
    /// <see cref="All"/> would stop it on water. Mapped on BOTH arms of the
    /// ABI, for <see cref="All"/>'s reason.
    /// </para>
    /// </summary>
    Solid = 5,

    /// <summary>
    /// <c>MASK_BLOCKLOS</c> — "is the LINE OF SIGHT between these two points
    /// broken". <c>CONTENTS_SOLID | CONTENTS_MOVEABLE | CONTENTS_BLOCKLOS</c>.
    ///
    /// <para>
    /// NOT a synonym for <see cref="Brushes"/>, and the two differences point
    /// opposite ways: this drops the window and grate bits, because you can see
    /// through both, and adds the <c>CONTENTS_BLOCKLOS</c> bit a mapper paints
    /// on a brush to break sight without blocking anything else. A brushes-only
    /// trace therefore answers "clear" through a blocklos volume and "blocked"
    /// by a pane of glass.
    /// </para>
    ///
    /// <para>
    /// Added for <c>drawline</c>'s colour: stock's <c>UTIL_AddDebugLine</c>
    /// asked exactly this question with exactly this mask, and the trace is
    /// managed now. Mapped on BOTH arms of the ABI for <see cref="All"/>'s
    /// reason.
    /// </para>
    /// </summary>
    BlockLos = 6,

    /// <summary>
    /// <c>MASK_OPAQUE_AND_NPCS | CONTENTS_WINDOW</c> — solid, moveable, opaque
    /// and monster contents, and glass: what the flashlight's direction and
    /// pull-back traces stop on (flashlighteffect.cpp, C7 cluster 12). Not
    /// <see cref="Solid"/>: that has the grate bit, and the light would pull back
    /// from a grating it shines through. Mapped on BOTH arms of the ABI for
    /// <see cref="All"/>'s reason.
    /// </summary>
    OpaqueAndNpcsWindow = 7,

    /// <summary>
    /// <c>MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS</c> — what a glow's
    /// sight line stops on (c_pixel_visibility.cpp's <c>GlowSightDistance</c>,
    /// C7): no glass, so a glow behind a window stays lit. Mapped on BOTH arms
    /// of the ABI for <see cref="All"/>'s reason.
    /// </summary>
    GlowSight = 8,

    /// <summary>
    /// <c>CONTENTS_SOLID</c> alone — what a glow overlay's line at the sky stops
    /// on when the card cannot answer pixel visibility (glow_overlay.cpp, C7);
    /// the sky's surface flag is read off what it hit. Mapped on BOTH arms of the
    /// ABI for <see cref="All"/>'s reason.
    /// </summary>
    ContentsSolid = 9,

    /// <summary>
    /// <c>MASK_SHOT &amp; ~CONTENTS_WINDOW</c>: a shot that passes through
    /// glass, the RPG laser's (<c>UpdateLaserPosition</c>). Mapped on both arms.
    /// </summary>
    ShotNoWindows = 10,

    /// <summary>
    /// <c>MASK_OPAQUE</c>: solid, moveable and opaque contents, the line a
    /// crossbow bolt traces behind its victim. Mapped on both arms.
    /// </summary>
    Opaque = 11,
}

/// <summary>
/// What is at a point, as the engine's CONTENTS_* mask. From bspflags.h.
///
/// <para>
/// Only the bits a managed caller has asked for are named. The mask carries
/// more, and an unnamed bit still round-trips through the cast — this is a
/// vocabulary for reading the answer, not a claim about what is in it.
/// </para>
/// </summary>
[Flags]
public enum Contents
{
    /// <summary>Nothing there. Open air.</summary>
    Empty = 0,

    /// <summary>Solid brush. CONTENTS_SOLID.</summary>
    Solid = 0x1,

    /// <summary>Slime. CONTENTS_SLIME: water's neighbour, a splash that reads as slime.</summary>
    Slime = 0x10,

    /// <summary>
    /// Water. CONTENTS_WATER.
    ///
    /// <para>
    /// Water ONLY: slime is its own bit, and stock's env_explosion tests this
    /// one alone. Reading "am I submerged" off it therefore answers false in
    /// slime, which is stock's behaviour and is preserved deliberately.
    /// </para>
    /// </summary>
    Water = 0x20,

    /// <summary>CONTENTS_MOVEABLE: a func_door-style mover's volume.</summary>
    Moveable = 0x4000,

    /// <summary>MASK_WATER: water, slime or a mover's volume, what stock means by "in water".</summary>
    MaskWater = Water | Slime | Moveable,
}

/// <summary>
/// What an entity is, as one answer.
///
/// These arrive together rather than as separate calls because a radius query
/// returns everything in range and the caller discards most of it, so the
/// discarding is what decides the cost of the whole query.
/// </summary>
[Flags]
public enum EntityState
{
    None = 0,
    Solid = 1 << 0,
    Player = 1 << 1,

    /// <summary>Moves with something else, so pushing it directly does nothing.</summary>
    Parented = 1 << 2,

    TakesDamage = 1 << 3,

    /// <summary>
    /// Has a physics object. Not the same as <see cref="MoveType.VPhysics"/>: a
    /// brush entity can be simulated while the engine still moves it as a mover.
    /// </summary>
    Physics = 1 << 4,

    OnGround = 1 << 5,
}

/// <summary>Source's MOVETYPE_*, as far as anything here needs to tell them apart.</summary>
public enum MoveType
{
    None = 0,
    Walk = 2,
    Step = 3,
    FlyGravity = 5,
    VPhysics = 6,
    Push = 7,
    NoClip = 8,
    Observer = 10,
}

/// <summary>
/// What a line through the world ran into.
///
/// <see cref="Hit"/> is true when the line was stopped, which includes starting
/// inside something: an emitter buried in a wall has not got a clear line, and
/// reporting otherwise would let it fire through.
/// </summary>
public readonly record struct TraceResult(
    bool Hit,
    float Fraction,
    Vector End,
    Vector Normal,
    EntityHandle Entity);

/// <summary>
/// What a line ran into, AND WHAT THAT THING IS MADE OF.
///
/// <para>
/// <see cref="TraceResult"/> with the two fields a bullet impact cannot be
/// dispatched without. A type of its own rather than a widened
/// <see cref="TraceResult"/>, for the reason the ABI gives at
/// <c>TraceLineSurface</c>: the narrow trace is the hottest slot in this API,
/// at 1.573 crossings per server frame at rest, and every caller that does not
/// want a surface should keep paying for what it asked for.
/// </para>
/// </summary>
/// <param name="SurfaceProps">
/// The surface's <c>surfaceprops</c> index — what
/// <see cref="IEngineWorldQuery.SurfacePropGameMaterial"/> takes, and what
/// <see cref="IEngineEffects.ImpactEffect"/> sends to the client so it can
/// choose a decal and an impact sound.
///
/// <para>
/// <b>-1 FOR A TRACE THAT HIT NOTHING, never 0.</b> Index 0 is a real surface
/// (<c>default</c>), so a zero here would be an answer a caller could act on
/// while meaning "I do not know" — the shape this port has been caught by
/// before.
/// </para>
/// </param>
/// <param name="SurfaceFlags">
/// The surface's <c>SURF_*</c> flags, 0 when nothing was hit. <c>SURF_SKY</c>
/// is the one every stock impact path tests: a shot into the skybox leaves no
/// decal and no flecks.
/// </param>
/// <param name="HitGroup">
/// The engine's <c>trace_t.hitgroup</c>: which group of the studio model's
/// hitboxes the line stopped on, when the mask carries hitboxes
/// (<see cref="TraceMask.Shot"/>). <see cref="Abi.HitGroup.Generic"/> for the
/// world, a brush, a model without hitboxes, or a trace that hit nothing —
/// the same zero stock's <c>trace_t</c> holds there.
/// </param>
public readonly record struct SurfaceTraceResult(
    bool Hit,
    float Fraction,
    Vector End,
    Vector Normal,
    EntityHandle Entity,
    int SurfaceProps,
    int SurfaceFlags,
    HitGroup HitGroup = HitGroup.Generic);

/// <summary>
/// Stock's <c>HITGROUP_*</c> (<c>game/shared/shareddefs.h</c>): the group a
/// studio model's hitbox belongs to, as a trace reports it. The values are the
/// engine's and are pinned against the pristine header by an oracle test.
/// 8 and 9 are unassigned in stock.
/// </summary>
public enum HitGroup
{
    Generic = 0,
    Head = 1,
    Chest = 2,
    Stomach = 3,
    LeftArm = 4,
    RightArm = 5,
    LeftLeg = 6,
    RightLeg = 7,
    Gear = 10,
}

/// <summary>
/// What a line through the CLIENT's world ran into.
///
/// <para>
/// A type of its own rather than a widened <see cref="TraceResult"/>, because
/// the two differ in what they can honestly report. This one carries
/// <see cref="Hitbox"/>, which the server's trace does not answer at all, and it
/// names what it hit by INDEX rather than by <see cref="EntityHandle"/> —
/// reading a client handle back through the engine's handle table is behaviour
/// ledger row C8, an unfiltered reinterpret that segfaulted a live client
/// against a managed entity. <see cref="EntityIndex"/> comes from
/// <c>CGameTrace::GetEntityIndex</c>, which asks
/// <c>IClientNetworkable::entindex()</c> and so answers for both hierarchies.
/// </para>
/// </summary>
/// <param name="Hitbox">
/// The engine's <c>tr.hitbox</c>. NON-ZERO means a static prop, numbered from
/// one so that zero can mean "not a static prop" — subtract one to index it.
/// Zero means the world, a brush model, or an entity with no hitbox.
/// </param>
/// <param name="EntityIndex">The entity hit, or -1 for nothing.</param>
/// <param name="SurfaceProps">
/// The surface's <c>surfaceprops</c> index — what
/// <see cref="IEngineWorldQuery.SurfacePropGameMaterial"/> takes. -1 for a
/// trace that hit nothing.
///
/// <para>
/// NO CALLER IN THE LANE THAT ADDED IT. It is here for behaviour ledger rows C4
/// and C5, whose collision block in <c>c_te_legacytempents.cpp</c> reads exactly
/// this and <see cref="SurfaceFlags"/> off its own <c>trace_t</c> — so the lane
/// that closes them does not have to widen a slot other lanes already call.
/// </para>
/// </param>
/// <param name="SurfaceFlags">
/// The surface's <c>SURF_*</c> flags. <c>SURF_SKY</c> is the one the pool tests:
/// a tempent that hit the skybox must not stick to it, and every stock impact
/// path refuses to decal it.
/// </param>
public readonly record struct ClientTraceResult(
    bool Hit,
    float Fraction,
    Vector End,
    Vector Normal,
    int Hitbox,
    int EntityIndex,
    int SurfaceProps,
    int SurfaceFlags);

/// <summary>
/// What a CLIENT box swept through the world and the entities its caller names
/// stopped on first: whether it stopped, how far it got and where, the index
/// of what it stopped on (the world's is 0, -1 for a miss) and that entity's
/// origin (the world's is the map's origin).
/// </summary>
public readonly record struct ClientBoxTrace(
    bool Hit,
    float Fraction,
    Vector End,
    int EntityIndex,
    Vector EntityOrigin);

/// <summary>
/// What a swept BOX ran into.
///
/// <para>
/// <see cref="StartSolid"/> is separate from <see cref="Blocked"/> rather than
/// folded into it, because the two ways of being blocked read differently at the
/// call site. A hull that STRUCK something names what it struck; a hull that
/// began inside something often names nothing at all, there having been no
/// surface to cross. Code that took "no entity came back" for "clear" would
/// place an object inside a wall.
/// </para>
/// </summary>
public readonly record struct HullResult(
    bool Blocked,
    bool StartSolid,
    EntityHandle Entity);

/// <summary>
/// What a screen shake is being asked to do.
///
/// A shake is not a one-shot: it can be started, stopped, or have its amplitude
/// or frequency changed while it runs, which is what lets a map ramp one up as
/// something gets worse rather than only switching it on.
/// </summary>
public enum ShakeCommand
{
    Start = 0,
    Stop = 1,
    Amplitude = 2,
    Frequency = 3,

    /// <summary>Controller rumble with no screen movement.</summary>
    StartRumbleOnly = 4,

    /// <summary>Screen movement with no controller rumble.</summary>
    StartNoRumble = 5,
}

/// <summary>
/// How a screen fade behaves. Source's FFADE_*.
/// </summary>
[Flags]
public enum FadeFlags
{
    /// <summary>Fade FROM the colour, revealing the world.</summary>
    In = 0x01,

    /// <summary>Fade TO the colour.</summary>
    Out = 0x02,

    Modulate = 0x04,

    /// <summary>Hold indefinitely, ignoring the duration, until another fade arrives.</summary>
    StayOut = 0x08,

    /// <summary>Replace whatever fades the client already has.</summary>
    Purge = 0x10,
}

/// <summary>Which view-model animation to play.</summary>
public enum WeaponAnim
{
    Primary = 0,
    Reload = 1,
    Idle = 2,

    /// <summary>
    /// The deploy — the gun coming up into the hands. Managed-only (Phase 10):
    /// the native WeaponSendAnim mapping this enum used to cross to is gone
    /// with the C++ weapon shell, and the viewmodel's sequence is picked by
    /// name on the managed side now.
    /// </summary>
    Draw = 3,

    /// <summary>ACT_VM_SECONDARYATTACK: an alt-fire with an animation of its own (the shotgun's double).</summary>
    Secondary = 4,

    /// <summary>ACT_VM_DRYFIRE: the trigger pulled on nothing.</summary>
    DryFire = 5,

    /// <summary>ACT_SHOTGUN_PUMP.</summary>
    Pump = 6,

    /// <summary>ACT_SHOTGUN_RELOAD_START: the shotgun turned to take shells.</summary>
    ReloadStart = 7,

    /// <summary>ACT_SHOTGUN_RELOAD_FINISH.</summary>
    ReloadFinish = 8,

    /// <summary>ACT_VM_PULLBACK_HIGH: the grenade's pin pulled for an overhand throw.</summary>
    PullbackHigh = 9,

    /// <summary>ACT_VM_PULLBACK_LOW: the pin pulled for a lob or a roll.</summary>
    PullbackLow = 10,

    /// <summary>ACT_VM_THROW: the overhand throw.</summary>
    Throw = 11,

    /// <summary>ACT_VM_HAULBACK: the grenade's underhand lob.</summary>
    Haulback = 12,

    /// <summary>ACT_VM_FIDGET: the crossbow's idle with no bolt loaded.</summary>
    Fidget = 13,

    /// <summary>ACT_VM_MISSCENTER: a melee swing that met nothing.</summary>
    Miss = 14,
}

/// <summary>Which of the weapon script's sounds to play.</summary>
public enum WeaponSound
{
    Single = 0,
    Empty = 1,
    Reload = 2,
}

/// <summary>Source's in_buttons.h bits, as far as a weapon cares.</summary>
[Flags]
public enum WeaponButtons
{
    None = 0,
    Attack = 1 << 0,
    Attack2 = 1 << 11,
    Reload = 1 << 13,
}

/// <summary>
/// What a player is watching when they are not playing. Source's OBS_MODE_*.
///
/// <see cref="None"/> is "in play" — but a player can also be out of spectating
/// and still dead, which is why leaving observer mode and respawning are
/// separate things.
/// </summary>
public enum ObserverMode
{
    None = 0,
    DeathCam = 1,
    FreezeCam = 2,
    Fixed = 3,
    InEye = 4,
    Chase = 5,
    PointOfInterest = 6,
    Roaming = 7,
}

/// <summary>
/// Whether an entity may be hurt. Source's m_takedamage settings.
///
/// <see cref="EventsOnly"/> is the one that is not a degree of the others: the
/// damage functions run and the health does not move, which is how something
/// reacts to being shot without being destructible.
/// </summary>
public enum TakeDamageMode
{
    No = 0,
    EventsOnly = 1,
    Yes = 2,

    /// <summary>Hurt, and worth autoaiming at.</summary>
    Aim = 3,
}

/// <summary>How a piece of debris is moved once it is thrown.</summary>
public enum GibSimulation
{
    /// <summary>The engine's own mover. Cheap, and it does not collide with much.</summary>
    Point = 0,

    /// <summary>The solver. It tumbles, settles and can be pushed.</summary>
    Physics = 1,

    /// <summary>A whole jointed body rather than a lump.</summary>
    Ragdoll = 2,
}

/// <summary>
/// Source's BLOOD_COLOR_*, which is a COLOUR and not a species.
///
/// <para>
/// <see cref="None"/> is a legitimate value that stock spells DONT_BLEED, and
/// it means the effect does nothing rather than being an absent setting. Only
/// the two an env_blood can be asked for are named: the map's "color" key is a
/// yes/no between red and yellow, and naming the rest would imply a mapper can
/// choose them.
/// </para>
/// </summary>
public enum BloodColor
{
    /// <summary>DONT_BLEED. Asked to bleed, this bleeds nothing.</summary>
    None = -1,

    Red = 0,

    Yellow = 1,
}

/// <summary>
/// Source's FX_BLOODSPRAY_* bits — the newer blood effects, which are in
/// addition to the drips or the stream rather than instead of them.
///
/// <para>
/// A flags enum because an env_blood's spawnflags can ask for any combination,
/// and stock ORs whichever were set into one call.
/// </para>
/// </summary>
[Flags]
public enum BloodSpray
{
    None = 0,

    /// <summary>FX_BLOODSPRAY_DROPS.</summary>
    Drops = 0x01,

    /// <summary>FX_BLOODSPRAY_GORE.</summary>
    Gore = 0x02,

    /// <summary>FX_BLOODSPRAY_CLOUD.</summary>
    Cloud = 0x04,
}

/// <summary>
/// What is underneath a point, as far as water is concerned.
///
/// <para>
/// The three answers arrive together because one trace produces all of them —
/// see WaterSurfaceBelow in sourcesharp.h. <see cref="Submerged"/> and
/// <see cref="Found"/> are separate rather than one tri-state because they read
/// differently at the call site: a splash entity underwater does nothing, and
/// one over dry ground does nothing, and those are not the same "nothing" to
/// anybody debugging a map.
/// </para>
/// </summary>
public readonly record struct WaterBelow(
    bool Submerged,
    bool Found,
    bool Slime,
    Vector Surface);

/// <summary>
/// Source's kRender* — how a thing is blended into what is already drawn.
///
/// <para>
/// Named rather than passed through as a bare int, and only the ones anything
/// here actually asks for. The rest exist in the engine and naming them would
/// imply this side knows what to do with them.
/// </para>
/// </summary>
public enum RenderMode
{
    /// <summary>Opaque.</summary>
    Normal = 0,

    /// <summary>Alpha-blended against what is behind it.</summary>
    TransTexture = 2,

    /// <summary>Added, and ignoring the depth buffer. A light source.</summary>
    Glow = 3,

    /// <summary>
    /// Alpha-blended by the entity's own alpha rather than by its texture.
    ///
    /// env_explosion is the caller: a mapper who sets this on one gets
    /// TE_EXPLFLAG_DRAWALPHA and a fireball drawn translucent instead of
    /// additive. It is the only render mode that entity treats as a request
    /// rather than as a default.
    /// </summary>
    TransAlpha = 4,

    /// <summary>Added to what is behind it. What every beam and sprite effect uses.</summary>
    TransAdd = 5,

    /// <summary>
    /// Added, ignoring depth, and faded by how much of an occlusion proxy the
    /// viewer can see. What a light glow hung in a corridor uses — dm_lockdown's
    /// door indicators are all this.
    /// </summary>
    WorldGlow = 9,

    /// <summary>
    /// Added to what is behind it, and blended BETWEEN two animation frames
    /// rather than snapping from one to the next.
    ///
    /// What every animating sprite effect uses. A plasma flame is the first
    /// caller here: six sprites running at fifteen to twenty frames a second,
    /// which without the interpolation reads as a strobe rather than as fire.
    /// </summary>
    TransAddFrameBlend = 7,

    /// <summary>Not drawn.</summary>
    None = 10,
}
