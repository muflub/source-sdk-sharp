//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// How this mod moves.
///
/// Shared rather than server-side, and registered by BOTH halves, because
/// movement is predicted: the client runs its own copy of the movement loop for
/// every command the server has not acknowledged yet. Two sides that disagree
/// about jump height disagree about where the player is, and the symptom is
/// rubber-banding — which names nothing and is miserable to trace.
///
/// <para>
/// Every number here is stock Half-Life's, deliberately. This commit is Phase 6
/// step 2, which is about making the parameters REACHABLE rather than about
/// changing how the game plays; a movement change hidden inside a plumbing
/// change would be impossible to review and impossible to bisect. Changing them
/// is now a one-line edit in this file, which is the whole point.
/// </para>
///
/// <para>
/// What is deliberately absent: duck timing. `GAMEMOVEMENT_DUCK_TIME` is used
/// ten times as a compile-time constant, and `m_flDucktime` is predicted state
/// initialised from it — so a value that changed mid-map would disagree with
/// the countdown the client already had.
/// </para>
/// </summary>
public static class Movement
{
    /// <summary>
    /// The parameters, as one value.
    ///
    /// A property rather than a field so the defaults stay in one readable
    /// place and nothing can half-initialise it: a zero jump height is a player
    /// who cannot jump, and it would look like broken input rather than a
    /// missing number.
    /// </summary>
    public static MovementParams Current => new()
    {
        // GAMEMOVEMENT_JUMP_HEIGHT. Before this it was a compile-time constant
        // with no convar behind it, so it is the one number here that was not
        // reachable at all.
        JumpHeight = 21f,

        Gravity       = 600f,
        Friction      = 4f,
        Accelerate    = 10f,
        AirAccelerate = 10f,
        StopSpeed     = 75f,
        MaxVelocity   = 3500f,
        StepSize      = 18f,

        // Air control, and the one number here most worth knowing about: it is
        // what makes air strafing and bunny-hopping work, and stock hides it as
        // a literal 30 in a virtual nobody overrides.
        AirSpeedCap = 30f,

        // A crouch costs you two thirds of your speed.
        DuckSpeedScale = 0.33333333f,

        // sv_maxspeed's own default. One speed, because walking and sprinting
        // are CHL2_Player states this build's player does not have — the
        // hl2_walkspeed / hl2_normspeed / hl2_sprintspeed convars are not even
        // defined in it, which is how a first attempt at this table came to set
        // three numbers that went nowhere.
        MaxSpeed = 320f,

        // THE HULLS. How big a player is, where their eye is, and what all
        // three become when they crouch.
        //
        // These were nine Vector literals in a native
        // `static CViewVectors g_DefaultViewVectors` in shell_gamerules.cpp,
        // moved there verbatim from stock's gamerules.cpp:1024 — which is to
        // say a mod that wanted a taller player edited C++ and rebuilt both
        // DLLs. They belong here for the reason everything else in this table
        // does: they are numbers a mod picks once that decide how the game
        // feels.
        //
        // Every one is stock's, unchanged, for the reason the header comment
        // gives: this is a plumbing commit and a gameplay change hidden inside
        // one is impossible to review and impossible to bisect. Native keeps
        // the same nine as its PRE-REGISTRATION default — the macros are read
        // all over the shared tree and some reads happen before the managed
        // side is up — and gamerules_view_vectors_test.cpp asserts the two
        // tables agree, so the default cannot quietly become a second opinion.
        //
        // WHAT THEY ARE. View is the eye, measured up from the player's origin
        // at their feet. HullMin/HullMax are the standing collision box: 32
        // units across and 72 tall. Duck* are the same three crouched — the box
        // keeps its footprint and halves its height, and the eye drops from 64
        // to 28. ObsHull is the small box a spectator flies around in.
        // DeadViewHeight is where the camera sits after you die.
        View        = new Vector(0f, 0f, 64f),
        HullMin     = new Vector(-16f, -16f, 0f),
        HullMax     = new Vector(16f, 16f, 72f),

        DuckHullMin = new Vector(-16f, -16f, 0f),
        DuckHullMax = new Vector(16f, 16f, 36f),
        DuckView    = new Vector(0f, 0f, 28f),

        ObsHullMin  = new Vector(-10f, -10f, -10f),
        ObsHullMax  = new Vector(10f, 10f, 10f),

        DeadViewHeight = new Vector(0f, 0f, 14f),
    };

    /// <summary>
    /// Hands them over. Called at init on both sides, before any player exists.
    /// </summary>
    public static void Register(string side, IEnginePlayerMovement movement, ILog log)
    {
        var p = Current;

        movement.RegisterParams(p);
        log.Msg($"[SourceSharp/{side}] movement: jump {p.JumpHeight:0}u, " +
                   $"max speed {p.MaxSpeed:0}\n");
    }

    /// <summary>
    /// How much of a jump the player gets: 1 is the ordinary jump, 0 refuses
    /// it, and anything between is a weaker one.
    ///
    /// <para>
    /// The default is 1 for everybody, deliberately. This is the plumbing
    /// commit for Phase 6 step 3, and a gameplay rule smuggled into it would be
    /// impossible to review and impossible to bisect — the same reason
    /// <see cref="Current"/> is still stock's numbers.
    /// </para>
    ///
    /// <para>
    /// A rule written here must decide from the request ALONE. It runs on the
    /// server and again on the client for every unacknowledged command, so
    /// anything it depends on that the client cannot see — a stamina counter
    /// the server keeps to itself, a random roll — makes the two disagree, and
    /// the player is dragged back to where the server thought they were.
    /// </para>
    /// </summary>
    public static float JumpScale(in JumpRequest request) => 1f;

    /// <summary>
    /// How slippery the ground is: the friction the engine was about to apply,
    /// scaled. Returning it unchanged is stock.
    ///
    /// <para>
    /// The second decision to move out of the movement code, and like
    /// <see cref="JumpScale"/> it is deliberately stock by default — a gameplay
    /// rule smuggled into a plumbing commit is impossible to review and
    /// impossible to bisect.
    /// </para>
    ///
    /// <para>
    /// What it is FOR is worth stating, because the shape is not obvious from
    /// the signature: <paramref name="groundMaterial"/> is a surfaceprop game
    /// material, so "ice is slippery" becomes a rule this mod states once rather
    /// than something every map has to arrange for itself. Zero is a legitimate
    /// answer — a skating rink — rather than a refusal.
    /// </para>
    ///
    /// <para>
    /// Same constraint as the jump, and it bites harder here because this runs
    /// every frame rather than once per press: it must decide from its
    /// arguments ALONE. Anything the client cannot also see makes the two sides
    /// disagree, and the symptom is the player sliding back to where the server
    /// thought they were.
    /// </para>
    /// </summary>
    public static float FrictionScale(int playerIndex, float friction, float speed,
                                      bool ducking, int groundMaterial)
        => friction;

    /// <summary>
    /// How fast a player may move right now: the speed the engine arrived at,
    /// scaled. Returning it unchanged is stock.
    ///
    /// <para>
    /// Asked LAST, after the engine has applied the client's own cap, the
    /// surface's speed factor and any constraint — so what arrives is the answer
    /// a rule is overriding rather than one of the inputs to it.
    /// </para>
    ///
    /// <para>
    /// This is where sprinting, being wounded, or carrying something heavy
    /// belongs. Predicted like the rest, so a rule may depend only on what it is
    /// handed: "slower when hurt" works because health is networked, while a
    /// counter the server keeps to itself would make the two sides disagree.
    /// </para>
    /// </summary>
    public static float MaxSpeedScale(int playerIndex, float maxSpeed,
                                      bool ducking, int groundMaterial)
        => maxSpeed;

    /// <summary>
    /// How fast a jump leaves the ground, for a given gravity.
    ///
    /// The engine's own arithmetic — sqrt(2 * g * h) — extracted so it can be
    /// tested and so the relationship is stated somewhere readable: doubling
    /// the height does NOT double the jump velocity, which is the thing people
    /// get wrong when they tune it.
    /// </summary>
    public static float JumpVelocity(float jumpHeight, float gravity)
        => jumpHeight <= 0f || gravity <= 0f
             ? 0f
             : MathF.Sqrt(2f * gravity * jumpHeight);
}
