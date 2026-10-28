//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// What a prop was doing at the moment it broke.
///
/// <para>
/// Stock's <c>breakablepropparams_t</c>, and a parameter block for the same
/// reason that one is: the fields are only meaningful together. Where a piece
/// goes is the prop's origin plus a rotated offset; how fast it leaves is the
/// prop's velocity plus a burst away from the prop's centre. Split them up and
/// half of them stop meaning anything.
/// </para>
/// </summary>
/// <param name="Origin">Where the prop was — the point pieces burst away from.</param>
/// <param name="Angles">
/// Which way it was facing, as pitch/yaw/roll. What the piece offsets are
/// rotated by, and what every piece is given as its own facing.
/// </param>
/// <param name="Velocity">What it was doing, inherited by every piece.</param>
/// <param name="AngularVelocity">How it was spinning, in degrees a second.</param>
/// <param name="DefaultBurstScale">
/// How hard a piece is thrown when its own block does not say. Stock's prop path
/// always passes 100 — see <see cref="PropBreakPlan.DefaultBurstScale"/>.
/// </param>
/// <param name="DefaultCollisionGroup">
/// What a piece collides as when its block does not say. Never
/// <see cref="CollisionGroup.None"/> — see <see cref="PropBreakPlan.GroupForPieces"/>.
/// </param>
/// <param name="SpawnFlags">The prop's own flags, which its pieces inherit.</param>
public readonly record struct BreakContext(
    Vector Origin,
    Vector Angles,
    Vector Velocity,
    Vector AngularVelocity,
    float DefaultBurstScale,
    CollisionGroup DefaultCollisionGroup,
    int SpawnFlags);

/// <summary>
/// One piece, worked out and ready to be made.
///
/// <para>
/// The unit between the two halves of breaking: <see cref="PropBreakPlan"/>
/// decides these from the model's data and the prop's state, and
/// <see cref="PropBreak"/> turns each one into an entity. Separating them is
/// what lets the arithmetic be tested without an engine, which matters because
/// every one of these numbers is invisible until it is wrong in a map — debris
/// that erupts from a point a few units off still looks like debris.
/// </para>
/// </summary>
public readonly record struct GibPlacement(
    string Model,
    Vector Origin,
    Vector Angles,
    Vector Velocity,
    Vector AngularVelocity,
    Vector BurstImpulse,
    CollisionGroup CollisionGroup,
    float FadeTime,
    bool MotionDisabled,
    bool IsRagdoll);

/// <summary>
/// Where each piece of a broken prop goes and how hard it leaves.
///
/// <para>
/// The arithmetic half of <c>PropBreakableCreateAll</c>, with no engine in it.
/// </para>
/// </summary>
public static class PropBreakPlan
{
    /// <summary>
    /// How hard a piece is thrown when its own block does not say.
    ///
    /// <para>
    /// Stock's <c>params.defBurstScale = 100</c>, set unconditionally in
    /// <c>CBreakableProp::Break</c> and commented there "no damage/damage force?
    /// set a burst of 100 for some movement". It is not a fallback for a missing
    /// number — it is what makes a broken crate scatter rather than collapse
    /// into a heap where it stood.
    /// </para>
    /// </summary>
    public const float DefaultBurstScale = 100f;

    /// <summary>
    /// The group a prop hands its pieces.
    ///
    /// <para>
    /// Stock, verbatim: "don't automatically make anything COLLISION_GROUP_NONE
    /// or it will collide with debris being ejected by breaking". A prop in the
    /// neutral group would otherwise hand that group to its own debris and be
    /// hit by it on the way out.
    /// </para>
    /// </summary>
    public static CollisionGroup GroupForPieces(CollisionGroup propGroup)
        => propGroup == CollisionGroup.None ? CollisionGroup.Interactive : propGroup;

    /// <summary>
    /// Where one piece goes and what it is doing when it gets there.
    ///
    /// <para>
    /// The offset arrives in the prop's own frame, already corrected for the two
    /// models' <c>placementOrigin</c> attachments by the native reader — that
    /// half needs a studio header. What is left is stock's
    /// <c>VectorTransform( offset, AngleMatrix( angles, origin ) )</c>.
    /// </para>
    /// </summary>
    public static GibPlacement Place(string model, in BreakPiece piece, in BreakContext context)
    {
        var frame = Transform.FromOriginAngles(
            context.Origin,
            new QAngle(context.Angles.X, context.Angles.Y, context.Angles.Z));

        var origin = frame.TransformPoint(piece.Offset);

        return new GibPlacement(
            Model: model,
            Origin: origin,

            // Stock hands every piece the WHOLE prop's facing rather than one of
            // its own. The pieces were modelled in place, so a crate lying on its
            // side comes apart into planks lying on their sides.
            Angles: context.Angles,

            Velocity: context.Velocity,
            AngularVelocity: context.AngularVelocity,
            BurstImpulse: BurstImpulse(origin, context.Origin, piece.BurstScale),

            // Already a SourceSharp group: native translates it through the one
            // table that owns the mapping, so no engine number reaches here.
            CollisionGroup: (CollisionGroup)piece.CollisionGroup,

            FadeTime: piece.FadeTime,

            // FadeMinDist and FadeMaxDist are deliberately NOT carried: nothing
            // on this hierarchy can apply a prop's distance fade, so a field
            // holding them would be a value nobody reads — which is the exact
            // shape of the bug that made a prop's mass scale and its
            // motion-disabled flag both no-ops for as long as they existed. See
            // PropBreak.Create.
            MotionDisabled: piece.IsMotionDisabled != 0,
            IsRagdoll: piece.IsRagdoll != 0);
    }

    /// <summary>
    /// The shove that pushes a piece away from where the prop was.
    ///
    /// <para>
    /// Stock normalises the direction and multiplies by the scale. A piece
    /// sitting exactly ON the break point gets nothing: mathlib's
    /// <c>VectorNormalize</c> of a zero vector is a zero vector, and stock's own
    /// fallback there — the piece's world-space centre — needs an entity that
    /// does not exist yet. So a single-piece model with a zero offset leaves on
    /// the prop's velocity alone, which is what it would have done anyway.
    /// </para>
    /// </summary>
    public static Vector BurstImpulse(Vector pieceOrigin, Vector breakOrigin, float burstScale)
    {
        if (burstScale == 0f)
            return Vector.Zero;

        var away = pieceOrigin - breakOrigin;
        float length = MathF.Sqrt(away.LengthSquared);

        if (length <= 0f)
            return Vector.Zero;

        return away * (burstScale / length);
    }
}

/// <summary>
/// Breaking a prop into the pieces its model was compiled with.
///
/// <para>
/// The port of <c>PropBreakableCreateAll</c>, which is where gibs come from. Two
/// things about that are worth stating, because they explain the shape of
/// everything here.
/// </para>
///
/// <para>
/// The first is that a gib IS a prop. Stock builds each piece with
/// <c>CreateNoSpawn( "prop_physics" )</c> — the same class as the thing that
/// broke — so a piece has mass, falls, can be punted, and can break again into
/// pieces of its own. Nothing here invents a debris entity; it makes more
/// prop_physics, which on this hierarchy means more managed
/// <see cref="PropPhysics"/>.
/// </para>
///
/// <para>
/// The second is that stock, in MULTIPLAYER, mostly does not do this at all. A
/// breakable prop sends a <c>te->PhysicsProp</c> temp entity and lets each
/// client build its own clientside pieces (ClientPhysicsProps), and the server
/// makes only what the caller's <see cref="BreakPieceSide"/> leaves it: the
/// pieces not marked <c>multiplayer_break client</c>, or only those marked
/// <c>server</c>.
/// </para>
/// </summary>
public sealed class PropBreak
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public PropBreak(ILog log, IEngineEntities entities, IEngineEntityPhysics physics,
                     IEnginePrecache precache)
    {
        _log = log;
        _entities = entities;
        _physics = physics;
        _precache = precache;
    }

    private readonly IEngineEntities _entities;
    private readonly IEngineEntityPhysics _physics;
    private readonly IEnginePrecache _precache;

    /// <summary>
    /// Stock's BREATHING_ROOM, commented there as a "stop-gap to prevent this
    /// function from crashing the engine".
    /// </summary>
    private const int BreathingRoom = 64;

    /// <summary>
    /// How deep a piece's own pieces are followed when precaching.
    ///
    /// <para>
    /// Stock recurses without a limit and relies on a model never listing
    /// itself. Model data is data, and a cycle in it should be a warning rather
    /// than a hang — the same reason the native prop_data reader depth-limits
    /// its base chain. Four is well past anything shipped: a crate breaks into
    /// planks and a plank into splinters, and that is two.
    /// </para>
    /// </summary>
    private const int MaxPrecacheDepth = 4;

    /// <summary><c>SF_PHYSPROP_MOTIONDISABLED</c>, from props_shared.h.</summary>
    public const int SpawnFlagMotionDisabled = 0x000008;

    /// <summary><c>SF_PHYSPROP_IS_GIB</c>, from props_shared.h.</summary>
    public const int SpawnFlagIsGib = 0x400000;

    /// <summary>
    /// Everything a model breaks into, and everything THOSE break into,
    /// precached.
    ///
    /// <para>
    /// Called from a prop's spawn, which is inside the level-load window. It has
    /// to be: a piece is created when something breaks, and by then the map is
    /// long since built and a precache is a console line and nothing drawn.
    /// Stock says the same thing with <c>PropBreakablePrecacheAll</c>, called
    /// from <c>CBreakableProp::Precache</c>.
    /// </para>
    ///
    /// <para>
    /// Recursive because stock is: a crate breaks into planks and a plank may
    /// break into splinters, and the splinters are wanted the moment the crate
    /// is hit twice.
    /// </para>
    /// </summary>
    /// <returns>
    /// How many pieces THIS model breaks into — stock's
    /// <c>m_iNumBreakableChunks</c>, which is what decides whether a gib may be
    /// broken again.
    /// </returns>
    public int Precache(string model)
    {
        if (model.Length == 0)
            return 0;

        return Precache(model, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private int Precache(string model, int depth, HashSet<string> seen)
    {
        // BreakPieceCount precaches the model itself, so nothing here has to.
        int count = _precache.BreakPieceCount(model);

        if (count == 0 || depth >= MaxPrecacheDepth)
            return count;

        for (int i = 0; i < count; i++)
        {
            // The defaults do not matter for a precache — only the name is read —
            // so they are the neutral pair rather than numbers invented to look
            // meaningful.
            if (!_precache.BreakPieceAt(model, i, 0f, CollisionGroup.None,
                                        out string pieceModel, out _))
                continue;

            if (pieceModel.Length == 0 || !seen.Add(pieceModel))
                continue;

            Precache(pieceModel, depth + 1, seen);
        }

        return count;
    }

    /// <summary>
    /// Makes every piece a broken prop throws.
    /// </summary>
    /// <param name="model">What broke.</param>
    /// <param name="owner">
    /// The prop itself, handed on as each piece's owner — which is what stops a
    /// piece colliding with the thing it came out of.
    /// </param>
    /// <param name="eachPiece">Handed every piece once it has spawned, for what the owner passes on.</param>
    /// <returns>How many pieces were made.</returns>
    /// <param name="side">Which of the pieces this server makes (PropBreakableCreateAll's multiplayer test).</param>
    public int CreateAll(string model, EntityHandle owner, in BreakContext context, Action<EntityHandle>? eachPiece = null,
                         BreakPieceSide side = BreakPieceSide.All)
    {
        if (model.Length == 0)
            return 0;

        int count = _precache.BreakPieceCount(model);

        if (count == 0)
            return 0;

        // Stock's PropBreakableCapEdictsOnCreateAll. Breaking is the one event in
        // the game that creates entities by the dozen and MAX_EDICTS is a hard
        // stop: refusing to break is a prop that stays whole, running out of
        // edicts is the level ending.
        if (_entities.Count() + count + BreathingRoom >= EngineLimits.MaxEdicts)
        {
            _log.Warning($"[SourceSharp] '{model}' did not break into pieces: {count} more " +
                        $"entities would exceed the edict limit\n");
            return 0;
        }

        int made = 0;

        for (int i = 0; i < count; i++)
        {
            if (!_precache.BreakPieceAt(model, i, context.DefaultBurstScale,
                                        context.DefaultCollisionGroup,
                                        out string pieceModel, out BreakPiece piece))
                continue;

            if (pieceModel.Length == 0 || !side.Makes(piece.MultiplayerBreakMode))
                continue;

            // Compared with None rather than asked IsValid, and the difference
            // is not pedantry: IsValid is a live engine call that resolves the
            // handle's serial, and this handle was made half a line ago. None is
            // what Create answers when the factory refused, which is the only
            // failure there is here.
            EntityHandle gib = Create(PropBreakPlan.Place(pieceModel, piece, context), owner, context);
            if (gib == EntityHandle.None)
                continue;

            made++;
            eachPiece?.Invoke(gib);
        }

        return made;
    }

    /// <summary>
    /// One piece, made.
    ///
    /// <para>
    /// The port of <c>BreakModelCreate_Prop</c> plus the tail of
    /// <c>BreakModelCreateSingle</c>. The ORDER is stock's and it matters: the
    /// keys go on before the spawn because the shell builds the physics object
    /// from the model during it, and everything that needs a physics object goes
    /// on after.
    /// </para>
    /// </summary>
    private EntityHandle Create(in GibPlacement placement, EntityHandle owner, in BreakContext context)
    {
        var gib = _entities.Create(EntityNames.PropPhysics);

        if (gib == EntityHandle.None)
        {
            _log.Warning($"[SourceSharp] could not create a break piece for '{placement.Model}'\n");
            return EntityHandle.None;
        }

        // The owner's flags, minus the one a piece must never inherit: a crate
        // the mapper bolted down breaks into pieces that FALL, not into pieces
        // frozen in the air where the crate was. Stock removes exactly this one
        // and no other.
        int flags = context.SpawnFlags & ~SpawnFlagMotionDisabled;

        // And plus the one that says what it is, which stock adds for a piece
        // with a fade time.
        if (placement.FadeTime != 0f)
            flags |= SpawnFlagIsGib;

        _entities.SetKeyValue(gib, "spawnflags", flags.ToString(CultureInfo.InvariantCulture));
        _entities.SetKeyValue(gib, "model", placement.Model);

        // How long it lasts. A KEY rather than a call afterwards, and that is not
        // a convenience: the piece decides during its own spawn whether it is a
        // gib — which is also what decides whether it can be broken again — and a
        // value handed to it afterwards would arrive after the decision.
        if (placement.FadeTime != 0f)
        {
            _entities.SetKeyValue(gib, PropPhysicsVars.GibFadeTimeKey,
                placement.FadeTime.ToString(CultureInfo.InvariantCulture));
        }

        _entities.SetOrigin(gib, placement.Origin);
        _entities.SetAngles(gib, placement.Angles);
        _entities.SetOwner(gib, owner);
        _entities.Spawn(gib);

        // Everything below wants the physics object, which the shell creates
        // during Spawn from the model set above.
        _physics.SetCollisionGroup(gib, placement.CollisionGroup);
        _entities.SetVelocity(gib, placement.Velocity);
        _entities.SetAngularVelocity(gib, placement.AngularVelocity);

        if (placement.BurstImpulse.LengthSquared > 0f)
            _entities.ApplyVelocityImpulse(gib, placement.BurstImpulse, clearGround: false);

        if (placement.MotionDisabled)
            _physics.EnableMotion(gib, false);

        // NOT the distance fade, and its absence is recorded rather than
        // papered over. Stock's SetFadeDistance writes m_fadeMinDist and
        // m_fadeMaxDist on CBaseAnimating; on this hierarchy the only fade a
        // managed entity has is StandaloneSetFade, which is a CLIENT slot — the
        // server's copy answers nothing, so calling it from here would be a line
        // that reads correctly and does nothing at all. SideAuditTests refuses
        // it for exactly that reason, and refused this.
        //
        // A block's fademindist/fademaxdist are therefore read across the ABI
        // and dropped. It costs a draw-distance optimisation and nothing a
        // player can see: both default to zero, which is "always drawn", and
        // almost no shipped break block writes either. See docs/divergences.md.

        return gib;
    }
}

/// <summary>
/// Which of a model's pieces the server makes: PropBreakableCreateAll's
/// "skip multiplayer pieces that should be spawning on the other dll", when
/// <c>gpGlobals->maxClients &gt; 1 &amp;&amp; breakable_multiplayer</c>.
/// </summary>
public enum BreakPieceSide
{
    /// <summary>One player, or breakable_multiplayer off: every piece.</summary>
    All,

    /// <summary>Multiplayer, at the default location: all but the pieces marked client.</summary>
    DefaultLocation,

    /// <summary>Multiplayer, as CBreakableProp::Break's server half asks (defaultLocation false): only those marked server.</summary>
    ServerMarked,
}

/// <summary>What each <see cref="BreakPieceSide"/> lets through.</summary>
public static class BreakPieceSides
{
    /// <summary>gpGlobals->maxClients &gt; 1 &amp;&amp; breakable_multiplayer: the clients make the pieces.</summary>
    public static bool InMultiplayer(IEngineEnvironment environment, ConVarRegistry? convars)
        => environment.Globals is { IsAvailable: true, MaxClients: > 1 }
        && (convars?.Get<BreakableMultiplayerConVar>()?.Value ?? true);

    /// <summary>Whether a piece with this <c>multiplayer_break</c> mode (stock's mp_break_t as an int) is made here.</summary>
    public static bool Makes(this BreakPieceSide side, int mode) => side switch
    {
        BreakPieceSide.All => true,
        BreakPieceSide.DefaultLocation => mode != (int)MultiplayerBreak.ClientSide,
        _ => mode != (int)MultiplayerBreak.ClientSide && mode != (int)MultiplayerBreak.Default,
    };
}
