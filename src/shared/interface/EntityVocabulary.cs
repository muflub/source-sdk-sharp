namespace SourceSharp.Abi;

/// <summary>
/// What a brush does to something walking into it. EngineSide's
/// EntitySetBrushSolidity turns it into FSOLID bits on a SOLID_BSP shape.
/// </summary>
public enum BrushSolidity
{
    /// <summary>Passable, and says nothing about it.</summary>
    None = 0,

    /// <summary>Solid. A wall.</summary>
    Solid = 1,

    /// <summary>Passable, and reports being entered. A trigger.</summary>
    Trigger = 2,
}

/// <summary>
/// What shape an entity presents to a trace — the engine's own FSOLID_ bits,
/// from src/public/const.h.
///
/// <para>
/// Mirrored rather than renamed, unlike <see cref="CollisionGroup"/> next door,
/// and the difference is the reason: these are eleven stable bit values that
/// every part of the SDK reads directly, not a positional enumeration a new
/// member renumbers. Only the ones this mod asks about are named; the rest are
/// still carried in the value.
/// </para>
/// </summary>
[Flags]
public enum SolidFlags
{
    None = 0,

    /// <summary>Not solid right now, whatever shape it has.</summary>
    NotSolid = 0x0004,

    /// <summary>
    /// Fires touch functions rather than stopping anything — a volume, not an
    /// obstacle. A trigger_multiple is one, and so is a weapon lying on the
    /// floor waiting to notice a foot.
    /// </summary>
    Trigger = 0x0008,

    /// <summary>Nothing may stand on it: a player stepping onto one slides off rather than riding it.</summary>
    NotStandable = 0x0010,

    /// <summary>Volumetric contents, the way water is. Also a volume to fly through.</summary>
    VolumeContents = 0x0020,

    /// <summary>
    /// Touched by triggers through a box bloated past its own (an item's 24
    /// units, a weapon's 36): see <c>IEngineEntities.UseTriggerBounds</c>.
    /// </summary>
    UseTriggerBounds = 0x0080,
}

/// <summary>
/// What SHAPE an entity presents to a trace — the engine's own
/// <c>SolidType_t</c>, from <c>src/public/const.h</c>.
///
/// <para>
/// The other half of <see cref="SolidFlags"/>: the flags qualify a shape, and
/// until this existed C# could read the qualifiers and not the thing they
/// qualified.
/// </para>
///
/// <para>
/// <b>IT IS A POSITIONAL ENUMERATION, which is the property that makes
/// <see cref="CollisionGroup"/> dangerous, and it is safe here for a reason
/// worth writing down.</b> What makes a positional enumeration dangerous is a
/// TRANSLATION TABLE that can go stale against it — a group leaves C# as 2, is
/// looked up in a table in <c>shell_entityapi.cpp</c>, and a member added in
/// the middle of the engine's list turns a vehicle clip into a door blocker
/// with nothing to say so. There is no table here: these ARE the engine's
/// numbers, passed through untranslated in both directions, so what is read
/// back is what was written. <c>CollisionGroupParityTests</c> asserts that
/// against <c>const.h</c>, member for member, rather than leaving it to be kept
/// in step by hand.
/// </para>
///
/// <para>
/// EVERY MEMBER, not just the ones this mod asks about, because a closed set of
/// seven is cheap to state whole and a value that fell outside it would have to
/// be carried as a number nobody could name. That is the opposite trade from
/// <see cref="SolidFlags"/>, where eleven bits combine and only the asked-about
/// ones need names.
/// </para>
/// </summary>
public enum SolidType
{
    /// <summary><c>SOLID_NONE</c> — no solid model.</summary>
    None = 0,

    /// <summary><c>SOLID_BSP</c> — a BSP tree, which is what a brush entity is.</summary>
    Bsp = 1,

    /// <summary><c>SOLID_BBOX</c> — an axis-aligned box.</summary>
    BoundingBox = 2,

    /// <summary><c>SOLID_OBB</c> — an oriented box. Not implemented by the engine.</summary>
    OrientedBox = 3,

    /// <summary><c>SOLID_OBB_YAW</c> — an oriented box that may only yaw.</summary>
    OrientedBoxYaw = 4,

    /// <summary><c>SOLID_CUSTOM</c> — the entity is asked about every test.</summary>
    Custom = 5,

    /// <summary><c>SOLID_VPHYSICS</c> — the model's vcollide, in the solver.</summary>
    VPhysics = 6,
}

/// <summary>
/// Which collision group an entity belongs to — the half of the pair
/// CGameRules::ShouldCollide is asked about. Must match the switch in
/// SourceSharp_EntitySetCollisionGroup.
///
/// <para>
/// These are NOT the engine's numbers, deliberately. Collision_Group_t is thirty
/// entries deep, mostly names this mod has nothing for, and it is positional —
/// a member added in the middle of it renumbers everything after. Naming only
/// what is asked for and translating in one native switch means a change to the
/// engine's table cannot silently turn a vehicle clip into a door blocker.
/// </para>
/// </summary>
public enum CollisionGroup
{
    /// <summary>The default. Collides with whatever the solidity says.</summary>
    None = 0,

    /// <summary>
    /// A brush that restricts vehicles and nothing else.
    ///
    /// The rule is not here and does not need to be: ShouldCollide already
    /// answers false for this group against anything that is not a vehicle,
    /// which is what makes the brush invisible to a player walking through it.
    /// </summary>
    VehicleClip = 1,

    /// <summary>
    /// A weapon lying where it was dropped.
    ///
    /// Read rather than set: nothing here puts an entity IN this group, but a
    /// projectile has to be able to tell one apart from a trigger volume. Stock
    /// says "don't touch triggers (but DO hit weapons)" in CMissile::MissileTouch
    /// and means exactly this — a dropped weapon is a trigger, because that is
    /// how it notices someone standing on it, and a rocket should still blow it
    /// off the ledge.
    /// </summary>
    Weapon = 2,

    /// <summary>
    /// A piece of something that broke.
    ///
    /// <para>
    /// The group that stops debris colliding with players and with other
    /// debris. It is what every gib a prop throws is put in, and it is not
    /// cosmetic: without it the pieces of a smashed crate shove the player who
    /// smashed it and can wedge a door.
    /// </para>
    /// </summary>
    Debris = 3,

    /// <summary>
    /// The neutral group for something that must still be collided with.
    ///
    /// <para>
    /// Half of one rule with <see cref="Debris"/>, and only meaningful next to
    /// it. Stock's break path refuses to hand a piece
    /// <see cref="None"/> — "don't automatically make anything
    /// COLLISION_GROUP_NONE or it will collide with debris being ejected by
    /// breaking" — so a prop in the neutral group gives its pieces this one
    /// instead.
    /// </para>
    /// </summary>
    Interactive = 4,

    /// <summary>
    /// Something fired: it hits everything but debris, weapons and other
    /// projectiles (CGameRules::ShouldCollide). The SMG1's grenade_ar2.
    /// </summary>
    Projectile = 5,

    /// <summary>
    /// A sheet of breakable glass (func_breakable_surf, CBreakableSurface::
    /// Spawn): it does not collide with other glass (CGameRules::ShouldCollide),
    /// and a crossbow bolt flies on through it after the damage.
    /// </summary>
    BreakableGlass = 6,
}

/// <summary>
/// Which distance-fade rule an entity on the parallel hierarchy follows. Must
/// match the switch in the client's StandaloneFade.Blend.
///
/// <para>
/// There are two because stock has two, and they are not variations on one
/// another: they ramp on different quantities and toward opposite ends. Averaging
/// them into a single rule would make both fades slightly wrong and neither
/// recognisably the entity a mapper placed.
/// </para>
/// </summary>
public enum FadeStyle
{
    /// <summary>Always fully drawn. What every entity that never asks does.</summary>
    None = 0,

    /// <summary>
    /// func_lod's rule: ramps on SQUARED distance to the entity's centre, from
    /// fully drawn down to nothing. The squared ramp is why a func_lod holds its
    /// opacity for most of the band and then goes quickly.
    /// </summary>
    Lod = 1,

    /// <summary>
    /// func_areaportalwindow's rule: ramps linearly on distance to the nearest
    /// FACE, and the other way round — most transparent up close, solid once the
    /// viewer has backed off past the far distance. The floor is what the map
    /// calls TranslucencyLimit.
    /// </summary>
    PortalWindow = 2,
}

/// <summary>
/// What kind of thing a map entity physically is. Must match
/// SourceSharpScriptedKind in scripted_kinds.h.
///
/// <para>
/// The numbers cross the ABI, so they are fixed forever and gaps stay gaps.
/// <b>4 is a gap.</b> It was <c>Surface</c>, for a pane grid that shatters a
/// piece at a time, and nothing ever declared it: <c>func_breakable_surf</c>
/// declares <see cref="Brush"/>, and the native side installed the two
/// identically, so the kind was harmless and invisible at once. It is retired
/// rather than reused — an old managed build sending 4 gets the native
/// fallback, which is <see cref="Logical"/>, not somebody else's kind.
/// <c>ScriptedEntityKindTests</c> keeps the rest honest: every value that
/// remains has to be declared by at least one entity in the mod.
/// </para>
/// </summary>
public enum ScriptedEntityKind
{
    /// <summary>No model, no collision, not networked — timers, counters, branches.</summary>
    Logical = 0,

    /// <summary>A model in the world the player walks into — health, ammo, armour.</summary>
    Item = 1,

    /// <summary>A volume carved out of the map — triggers, doors, breakables.</summary>
    Brush = 2,

    /// <summary>A model the physics engine moves — crates, barrels, debris.</summary>
    Prop = 3,

    // 4 is retired. See the remark on the enum.

    /// <summary>
    /// Something in flight — a bolt, a rocket.
    ///
    /// An <see cref="Item"/> that moves: the model, the precache ordering and
    /// the touch report are identical, and only the movement differs. (There
    /// were once two C++ shells in that relationship, the projectile deriving
    /// from the item; both are gone, and the difference is now four values in
    /// one table.)
    ///
    /// <para>
    /// Deliberately NOT a <see cref="Prop"/>. A prop is moved by the physics
    /// solver, which is right for a grenade — bouncing off a wall and rolling
    /// under a table is behaviour that comes free. It is wrong for a bolt or a
    /// rocket, which travel in a line at a fixed speed until they hit
    /// something; simulating those buys tumbling and drag a bolt must not have,
    /// and costs a physics object per shot.
    /// </para>
    ///
    /// <para>
    /// Gravity is a per-entity scale rather than on or off, so one shell serves
    /// both: a rocket sets it to zero and flies flat, a bolt leaves a little on
    /// and drops over distance.
    /// </para>
    /// </summary>
    Projectile = 5,

    /// <summary>
    /// Something the client DRAWS and nothing touches: no model, no collision,
    /// networked purely so the far side has state to draw from.
    ///
    /// <para>
    /// The gap the other five leave. <see cref="Logical"/> is server-only and
    /// never reaches a client at all; every other kind is networked because it
    /// has a MODEL, and carries the collision that comes with having one. A beam
    /// has neither — it is two endpoints and an appearance — so before this
    /// there was no kind that could describe one.
    /// </para>
    /// </summary>
    Effect = 6,

    /// <summary>
    /// A connecting client's avatar (Phase 9a of plan_baseentity.md).
    ///
    /// <para>
    /// The one kind whose instances are never made by the map or by C# — the
    /// ENGINE causes one by handing ClientPutInServer an edict — and the one
    /// kind the native side instantiates as its player shell, which carries
    /// the engine-facing state blocks and answers the movement loop. Exactly
    /// one class in the mod declares it.
    /// </para>
    /// </summary>
    Player = 7,
}
