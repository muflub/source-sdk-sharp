//========= SourceSharp — .NET runtime host for Source SDK 2013 ================//
//
// Public API for the native host library.
//
// This library deliberately knows NOTHING about Source. The game shims
// (client.so / server.so) supply an engine API table. That keeps the host
// testable against a fake engine harness with no Source dependency.
//
//=============================================================================//
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// The host (SOURCESHARP_HOST_BUILD, its CMake target) exports the API; the
// game libraries, which include this header to call it, import it. On the GNU
// platforms one visibility attribute serves both sides.
#ifdef _WIN32
	#ifdef SOURCESHARP_HOST_BUILD
		#define SOURCESHARP_EXPORT __declspec(dllexport)
	#else
		#define SOURCESHARP_EXPORT __declspec(dllimport)
	#endif
#else
	#define SOURCESHARP_EXPORT __attribute__((visibility("default")))
#endif


//-----------------------------------------------------------------------------
// WHICH FACTORY ANSWERS -- plan_native_api.md Part 3b.
//
// One acquisition entry point, with the KIND of factory as its first argument
// and the switch in native code. The engine's own CreateInterfaceFn pointers
// never cross the boundary: native holds them where they already arrive
// (stock's factorylist_t, stored at DLLInit on the server and at Init on the
// client) and dispatches on this enum. Managed never holds an engine function
// pointer at all.
//
// THE TYPE NAMES THE ROLE, NOT THE MODULE, and each side resolves it its own
// way. The client has no fileSystemFactory -- it asks appSystemFactory for
// FILESYSTEM_INTERFACE_VERSION -- so SOURCESHARP_FACTORY_FILESYSTEM routes
// there on the client and to the real third factory on the server. A caller
// names what it wants; where it lives is not its business.
//
// A NEW FACTORY KIND IS AN ENUM VALUE, NOT AN ABI CHANGE. Growth here costs no
// field, which matters in a tree whose slot-append convention has produced
// real collisions.
//
// Passed as a plain 32-bit value: every enumerator is non-negative and fits an
// int, so the register the SysV ABI puts it in holds the same bits whichever
// underlying type the compiler picks. tools/abi-gen maps the spelling to C#
// `int` and SourceSharpFactoryType in Abi.cs is `: int` for the same reason.
//-----------------------------------------------------------------------------
typedef enum
{
	SOURCESHARP_FACTORY_APPSYSTEM  = 0,  // the engine's appSystemFactory
	SOURCESHARP_FACTORY_PHYSICS    = 1,  // physicsFactory
	SOURCESHARP_FACTORY_FILESYSTEM = 2,  // the filesystem, wherever this side gets it
	SOURCESHARP_FACTORY_INTERNAL   = 3,  // the shim layer's own interfaces
} SourceSharpFactoryType;

// How many bytes AcquireInterface's caller must give it for a failure reason,
// mirrored by Vtable.MaxAcquireError. The reasons are a closed set of short
// ASCII literals, so this is room to spare rather than a measured bound -- and
// native truncates rather than refusing if it ever is not.
#define SOURCESHARP_ACQUIRE_ERROR_BYTES 256

//-----------------------------------------------------------------------------
// WHY AN ACQUIRE FAILED -- the CLOSED set, and closed is what makes the ASCII
// guarantee true by construction.
//
// Native reports the REASON and managed composes the MESSAGE. There are four of
// them, none is formatted, and none contains anything the caller passed in --
// so the one string that could put a byte >= 0x80 into the error buffer, the
// caller's own version string, never reaches it. Managed already knows the
// factory type and the name it asked for and adds both itself.
//
// Enumerated rather than left as bare literals so that both sides can WALK the
// set: the managed closed-set fact asserts every one is pure ASCII, and the
// gtest twin asserts no byte >= 0x80 and no '%' in any of them. A sample would
// not be a guarantee; the whole set is.
//-----------------------------------------------------------------------------
typedef enum
{
	// Asked before DLLInit/Init captured the engine's factories.
	// SOURCESHARP_FACTORY_INTERNAL is exempt -- its objects are ours and exist
	// from DLL load.
	SOURCESHARP_ACQUIRE_NOT_YET_AVAILABLE   = 0,

	// The list was captured, but this side has no factory of the kind asked
	// for.
	SOURCESHARP_ACQUIRE_NO_SUCH_FACTORY     = 1,

	// The interesting one: a factory answers only for what it registered, not
	// for everything anyone hoped. Also the answer for a name this side was not
	// built to serve.
	SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE   = 2,

	// A managed/native skew that should be impossible while the handshake
	// version agrees. Refused rather than assumed.
	SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE = 3,
} SourceSharpAcquireReason;

typedef enum
{
	SOURCESHARP_OK                  = 0,
	SOURCESHARP_ERR_HOSTFXR_LOAD    = -1,  // couldn't dlopen libhostfxr
	SOURCESHARP_ERR_HOSTFXR_SYM     = -2,  // missing export in libhostfxr
	SOURCESHARP_ERR_RUNTIME_INIT    = -3,  // hostfxr_initialize_for_runtime_config failed
	SOURCESHARP_ERR_DELEGATE        = -4,  // couldn't get load_assembly_and_get_function_pointer
	SOURCESHARP_ERR_BOOTSTRAP       = -5,  // couldn't resolve/call the managed bootstrap
	SOURCESHARP_ERR_ABI_MISMATCH    = -6,  // managed side rejected our ABI version
	SOURCESHARP_ERR_NOT_INITIALIZED = -7,
} SourceSharpResult;

//-----------------------------------------------------------------------------
// One entity's entry in the control block (plan_nettable_generic.md Part 9).
//
// The whole of what a bound managed state needs in order to stop calling across
// the boundary on every property access. C# maps these directly — one of these
// is a pointer it holds, not a copy — so the layout IS the ABI:
//
//     nSerial   written by NATIVE when a slot is opened and when it is closed
//     nDirty    written by C# on a write, drained by NATIVE once per frame
//
// Mirrored by SourceSharpEntitySlot_t in game/shared/entity_slots.h (which
// includes this header rather than restating it) and by EntitySlot in
// SourceSharp.Abi/NetState.cs. A field added here and not there is read as the
// next entity's serial.
//-----------------------------------------------------------------------------
typedef struct
{
	uint32_t nSerial;
	uint32_t nDirty;
} SourceSharpEntitySlot_t;

// What TraceLine is allowed to hit. Named rather than passed through as an
// engine MASK_* constant, because which contents stop a blast is a game
// decision and the numbers behind it are not something C# should mirror.
//-----------------------------------------------------------------------------
// Weapons.
//
// The state machine that decides when a weapon fires, when it reloads and what
// it sounds like lives in C#. What stays here is everything that IS the engine:
// assembling a bullet and tracing it, playing a view-model animation, reading
// how long that animation lasts.
//-----------------------------------------------------------------------------

// Which animation to play. Named rather than passed as an Activity, because the
// ACT_* enumeration is engine surface with hundreds of entries and a weapon
// needs three of them.
typedef enum
{
	SOURCESHARP_WEAPONANIM_PRIMARY = 0,
	SOURCESHARP_WEAPONANIM_RELOAD  = 1,
	SOURCESHARP_WEAPONANIM_IDLE    = 2,
} SourceSharpWeaponAnim;

// Which of the weapon script's sounds to play.
typedef enum
{
	SOURCESHARP_WEAPONSOUND_SINGLE = 0,
	SOURCESHARP_WEAPONSOUND_EMPTY  = 1,
	SOURCESHARP_WEAPONSOUND_RELOAD = 2,
} SourceSharpWeaponSound;

// Everything the state machine reads, in one call.
//
// One struct rather than a dozen getters because this is read once per weapon
// per frame for every player, and because the fields are only meaningful
// together — a clip count without the max and the reserve does not say whether
// a reload is possible.
//-----------------------------------------------------------------------------
// ONE breakable piece, as the modeller compiled it into the prop.
//
// This is stock's breakmodel_t minus the two fields C# has no use for. The
// list of them lives in the model's COLLISION data -- the "break" blocks in the
// .phy, read through IVPhysicsKeyParser -- which is a different file and a
// different reader from the `prop_data` KeyValues text EntityGetPropData
// already hands over. It is also the ONE fact about a breaking prop that C#
// cannot reach for itself, which is why these two slots exist and why they are
// all that is native about gibs: the file is read here, and every decision
// taken from it is taken in C#.
//
// A struct rather than a dozen out-parameters: the fields are only meaningful
// together, and they
// are read in a loop where an argument out of order is a silent mis-spawn
// rather than a compile error.
//-----------------------------------------------------------------------------
typedef struct
{
	// Where the piece sits in the BREAKING PROP's frame, before that prop's
	// rotation is applied. C# finishes the sum -- rotate by the prop's angles,
	// add the prop's origin -- because that arithmetic is Transform's and is
	// held to the mathlib by its own tests.
	//
	// Already corrected for the two models' "placementOrigin" attachments,
	// which is the half of stock's sum that needs a studio header and so cannot
	// be done anywhere else.
	float    OffsetXYZ[3];

	// The block's own "velocity" key, in the same frame. Almost never written;
	// zero when it is not.
	float    VelocityXYZ[3];

	// How much damage the piece itself takes before breaking again, how long
	// before it fades, and the distance band it is drawn in.
	float    Health;
	float    FadeTime;
	float    FadeMinDist;
	float    FadeMaxDist;

	// How hard the piece is pushed away from the break, if the block asked for
	// something other than the caller's default.
	float    BurstScale;

	// What it collides as, in SOURCESHARP's vocabulary and not the engine's:
	// translated on the way out by the one table that owns the mapping, so that
	// the group a piece is created in and the group C# believes it asked for
	// cannot be two different lookups. Debris unless the block said otherwise.
	int32_t  CollisionGroup;

	// A jointed body rather than a lump, and a piece that is bolted in place.
	int32_t  IsRagdoll;
	int32_t  IsMotionDisabled;

	// mp_break_t: DEFAULT, SERVERSIDE, CLIENTSIDE or BOTH. Reported rather than
	// acted on -- which side of the wire a piece belongs on is a decision, and
	// this mod has only one answer to it since the clientside prop went.
	int32_t  MultiplayerBreakMode;
} SourceSharpBreakPiece;

// The 3D skybox, as sky_camera describes it.
//
// A mirror of the engine's sky3dparams_t rather than a design of its own,
// because the destination is fixed: every field here is copied into
// CPlayerLocalData::m_skybox3d, which is a networked embedded struct the client
// renders the miniature world from. Inventing a nicer shape here would only
// move the translation somewhere less obvious.
//
// Pushed by C# once, rather than asked for per player per frame. There is one
// sky camera in a map -- stock's own comment says "UNDONE: Support multiple sky
// cameras?" -- so the whole thing is a single piece of level data that changes
// when a mapper's entity says it does, and almost never after that.
//
// NAMED, not an anonymous typedef, for SourceSharpFogParams' stated reason and
// the same build error: the CLIENT's declaration block in
// sourcesharp_gamesystem.cpp forward-declares it (the server's SetSkyboxParams
// lives in the `#ifndef CLIENT_DLL` block and is compiled away there), and a
// forward declaration of an anonymous typedef is a CONFLICTING declaration
// rather than the same type. It cost a build in this lane too.
typedef struct SourceSharpSkyboxParams
{
	// Where in the world the miniature is, and how many times smaller. The
	// client subtracts this origin and divides by the scale.
	float    OriginXYZ[3];
	int32_t  Scale;

	// Which visleaf area the camera sits in, so the engine knows what of the
	// miniature world is visible. 255 means "no sky camera", which is the value
	// the native path writes when it finds none.
	int32_t  Area;

	// The skybox's own fog, which is a separate volume from the world's -- a
	// map can have distance fog in the playable space and none in the sky, or
	// the other way round.
	int32_t  FogEnable;
	int32_t  FogBlend;
	int32_t  FogRadial;
	float    FogDirPrimaryXYZ[3];

	// Packed RGBA, one byte each, in the order the engine's color32 stores
	// them. Bytes rather than four floats because that is what the send table
	// carries and rounding twice would show up as a colour that is not the one
	// the mapper typed.
	uint32_t FogColorPrimary;
	uint32_t FogColorSecondary;

	float    FogStart;
	float    FogEnd;
	float    FogMaxDensity;
} SourceSharpSkyboxParams;

// THE WORLD'S FOG, as env_fog_controller describes it (plan_entity.md step
// 8e-render).
//
// A mirror of the engine's fogparams_t, cut down to the fields a mapper can set
// and the renderer reads. It goes the OTHER WAY from SourceSharpSkyboxParams
// above: that one is C# handing the SERVER what sky_camera said, this one is C#
// handing the CLIENT what the master fog controller said, and the destination is
// the pointer CRendering3dView::EnableWorldFog and CSkyboxView ask
// SourceSharp_ViewerFogParams for.
//
// WHY THE PORT NEEDED IT AT ALL. In stock, fog reaches a client through
// DT_FogController -- CFogController's own send table -- plus a handle in the
// player's local data. fogcontroller.cpp is entirely `#ifndef SOURCESHARP` here
// and env_fog_controller is EnvFogController.cs, so server.so declares no
// DT_FogController and there was NO SENDER: the numbers existed on the server
// and world fog rendered off on every frame since the flip. Step 7c put that on
// the not-shipped ledger; this is the wire that retires the row.
//
// THE LERP FIELDS ARE DELIBERATELY ABSENT. fogparams_t carries
// colorPrimaryLerpTo / startLerpTo / endLerpTo / lerptime / duration so the
// CLIENT can interpolate a fog transition. EnvFogController.OnThink already
// runs that lerp on the server and publishes the resolved values, so shipping
// the lerp inputs as well would be two implementations of one transition with
// no way to tell which one a screenshot came from.
// NAMED, not an anonymous typedef: the client's own declaration block in
// sourcesharp_gamesystem.cpp forward-declares it, and a forward declaration of
// an anonymous typedef is a conflicting declaration rather than the same type.
typedef struct SourceSharpFogParams
{
	// Whether there is fog at all, and how it blends. Ints rather than bools
	// for the ABI's fixed-width rule.
	int32_t  Enable;
	int32_t  Blend;
	int32_t  Radial;

	// The direction the secondary colour comes from, for a blended fog.
	float    DirPrimaryXYZ[3];

	// Packed RGBA, one byte each, in the order the engine's color32 stores
	// them -- the same encoding SourceSharpSkyboxParams uses and for the same
	// reason: the send table carries bytes and rounding twice shows up as a
	// colour the mapper did not type.
	uint32_t ColorPrimary;
	uint32_t ColorSecondary;

	// Where the fog begins and ends, how far the far plane is pushed, and the
	// densest it gets. FarZ <= 0 means "the mapper did not set one", which is
	// how stock's env_fog_controller spells it too.
	float    Start;
	float    End;
	float    FarZ;
	float    MaxDensity;
} SourceSharpFogParams;

// A SECOND CAMERA'S VIEW, as managed code decides it (lane camproto, the
// prototype ahead of plan_client_novalve C7). IShimViewClient's
// RenderViewToTarget queues one for this frame's render and
// shell_view_targets.cpp copies it into a CViewSetup field for field: every
// number here is managed's, and the executor adds none of its own.
//
// Ortho extents are in world units about the view's centre, as CViewSetup's
// m_Ortho* are. Width and height are the pixels of the target to draw into,
// from its top left; the executor refuses a size larger than the target.
// NAMED, for SourceSharpFogParams' reason: it is forward-declared.
typedef struct SourceSharpViewTargetSetup
{
	float    OriginXYZ[3];
	float    Angles[3];       // pitch, yaw, roll
	float    Fov;             // horizontal, degrees, at the target's aspect
	float    ZNear;
	float    ZFar;
	float    OrthoLeft;
	float    OrthoTop;
	float    OrthoRight;
	float    OrthoBottom;
	int32_t  Width;
	int32_t  Height;
	int32_t  Flags;           // SOURCESHARP_VIEWTARGET_*
} SourceSharpViewTargetSetup;

#define SOURCESHARP_VIEWTARGET_ORTHO       1   // the Ortho* extents, not Fov
#define SOURCESHARP_VIEWTARGET_DRAW_VIEWER 2   // the local player's body draws in this view

// One dynamic light, as the client asks the engine for it.
//
// A dlight is a light the renderer applies at draw time rather than one baked
// into the lightmap, and the engine keeps a small fixed pool of them. Managed
// code decides everything ABOUT one -- where it is, what colour, how far it
// reaches, whether it is a cone -- and this hands the answer to the pool.
//
// The whole light per call rather than a handle and fifteen setters: it is
// refreshed every frame by whatever owns it, so a partial update has no meaning
// and fifteen crossings per light per frame would.
typedef struct
{
	// Which slot to reuse. The engine's dlights are keyed, and re-asking with
	// the same key returns the same light rather than a new one -- which is how
	// a light MOVES instead of leaving a trail of stale ones behind it.
	//
	// Entity indices are the usual choice and negative keys are conventionally
	// the same entity's second light, which is how a spotlight has one at each
	// end. Zero is not special.
	int32_t  Key;

	// An elight lights MODELS only and never the world, which is much cheaper
	// and is what a light with DLIGHT_NO_WORLD_ILLUMINATION really wants.
	int32_t  IsElight;

	float    OriginXYZ[3];
	float    DirectionXYZ[3];

	float    Radius;

	// Colour, with the engine's shared exponent -- ColorRGBExp32, not a byte
	// triple. The exponent is what lets a dlight be brighter than white.
	int32_t  Red, Green, Blue, Exponent;

	// Which lightstyle animates it, and the engine's own DLIGHT_* bits.
	int32_t  Style;
	int32_t  Flags;

	// A cone rather than a point light, in degrees. Zero outer angle means it
	// is not a cone at all.
	float    InnerAngle;
	float    OuterAngle;

	// How long from now it lasts, in seconds. A light refreshed every frame
	// asks for slightly more than a frame; one that is placed and left asks for
	// a very large number.
	float    LiveFor;

	// How much radius the light sheds each second before it dies -- dlight_t's
	// own `decay`, "drop this each second".
	//
	// A FIELD RATHER THAN A SLOT, and it is here because of ONE caller. Every
	// light this ABI carried before C-Phase 3 was refreshed every frame by the
	// entity that owns it, and a light rewritten sixty times a second cannot
	// decay -- so nothing needed it and SourceSharp_DynamicLightUpdate left
	// dlight_t::decay alone. DT_TEDynamicLight is the first light that is
	// PLACED AND LEFT: c_te_dynamiclight.cpp asks the pool once, sets die and
	// decay together off the wire, and never touches the light again. Without
	// this the temp entity light would hold full brightness for its whole life
	// and then vanish, instead of fading, and nothing anywhere would say so.
	//
	// Zero for every other caller, which is what a freshly allocated dlight
	// already has -- so adding it changed no existing light's behaviour.
	float    Decay;
} SourceSharpDynamicLight;

// One projected-texture flashlight's state, as managed decides it
// (SourceSharp.Client.Flashlight, C7 cluster 12): the fields of
// FlashlightState_t that stock's CFlashlightEffect::UpdateLightNew sets
// (flashlighteffect.cpp). shell_flashlight_client.cpp copies them into a
// default-constructed FlashlightState_t, so every field not named here keeps
// that constructor's value, as stock's does. Every field is one word, so the
// managed mirror (SourceSharp.Abi.FlashlightState) has no padding to agree on;
// AbiParityTests holds the two field for field.
typedef struct
{
	float    OriginXYZ[3];
	// m_quatOrientation: x, y, z, w.
	float    Orientation[4];
	float    NearZ;
	float    FarZ;
	float    HorizontalFovDegrees;
	float    VerticalFovDegrees;
	float    QuadraticAtten;
	float    LinearAtten;
	float    ConstantAtten;
	// m_Color: r, g, b and the ambient term.
	float    Color[4];
	// FlashlightTexture's id for m_pSpotlightTexture; -1 for none.
	int32_t  Texture;
	int32_t  TextureFrame;
	int32_t  EnableShadows;
	float    ShadowMapResolution;
	float    ShadowAtten;
	float    ShadowSlopeScaleDepthBias;
	float    ShadowDepthBias;
} SourceSharpFlashlightState;


// How movement feels, stated once by C# and read by the native movement loop.
//
// Phase 6 of the migration plan says why this is a PARAMETER BLOCK rather than
// managed movement code: movement is predicted, so it runs on the client for
// every unacknowledged command and again on the server. A managed call on that
// path is paid many times per frame per player. A struct read out of memory is
// paid nothing.
//
// It is registered from the SHARED half and by both sides, and that is not
// tidiness. The client predicts movement with its own copy of this loop, so two
// sides that disagree about jump height disagree about where the player is —
// and the symptom is rubber-banding, which names nothing.
typedef struct
{
	// How high a jump reaches, in units. Stock is 21, which is
	// GAMEMOVEMENT_JUMP_HEIGHT — a compile-time constant with no convar behind
	// it, so before this there was no way to change it at all.
	float flJumpHeight;

	// The rest are engine convars, applied once at registration rather than
	// read from here every step. They stay convars deliberately: they are
	// already the server operator's to set, and a mod that overrode them per
	// movement step would be taking that away. What this block gives them is a
	// DEFAULT the mod chooses rather than the one Half-Life chose.
	float flGravity;
	float flFriction;
	float flAccelerate;
	float flAirAccelerate;
	float flStopSpeed;
	float flMaxVelocity;
	float flStepSize;

	// How much a player may steer while airborne, in units per second.
	//
	// This is the deathmatch movement knob. Stock is a hardcoded 30 behind
	// CGameMovement::GetAirSpeedCap — a virtual with no convar and, in this
	// build, nobody overriding it. It is what decides whether air strafing and
	// bunny-hopping work, and every mod that has ever felt different in the air
	// felt different here.
	float flAirSpeedCap;

	// What fraction of their speed a crouching player keeps. Stock is a literal
	// 0.33333333 inside HandleDuckingSpeedCrop, also with nothing behind it.
	float flDuckSpeedScale;

	// How fast a player runs. ONE speed, not three: hl2_walkspeed,
	// hl2_normspeed and hl2_sprintspeed belong to CHL2_Player, and this build's
	// player derives straight from CBasePlayer — so walking and sprinting are
	// states it does not have, and those convars do not exist in it. Setting
	// them was a parameter with nothing behind it, which is exactly the quiet
	// kind of wrong this project keeps finding.
	float flMaxSpeed;

	// THE HULLS — plan_shim.md §5 3e. How big a player is, where their eye is,
	// and what all three become when they crouch.
	//
	// These were a hardcoded `static CViewVectors g_DefaultViewVectors(...)` in
	// shell_gamerules.cpp, nine Vector literals moved verbatim out of stock's
	// gamerules.cpp:1024. They are declared in Shared/Movement.cs now, beside
	// jump height and air speed cap, because they are the same KIND of thing: a
	// number a mod picks once that decides how the game feels. A mod that wants
	// a taller player had to edit C++ and rebuild both DLLs.
	//
	// WHY THEY ARE IN THIS BLOCK AND NOT A BLOCK OF THEIR OWN. They are read on
	// exactly the path this block exists for. Every one of them reaches native
	// code through shareddefs.h's VEC_HULL_MIN / VEC_DUCK_VIEW / ... macros,
	// which expand to `g_pGameRules->GetViewVectors()->m_vHullMin` — inside the
	// movement loop, per command, on both sides. A slot would be a crossing per
	// hull read; a struct read out of memory is paid nothing, which is the whole
	// argument for everything above.
	//
	// AND WHY IT IS A REAL IMPROVEMENT RATHER THAN A RELOCATION. The table was
	// compiled into client.so AND server.so, once each, exactly like the
	// movement arithmetic docs/plan-audit.md E1 moved for the same reason: two
	// copies of numbers that must agree, and a disagreement shows up as a player
	// standing in a hull the other side thinks is somewhere else. One managed
	// declaration pushed to both sides cannot drift.
	//
	// FLATTENED TO FLOATS, three per vector, the way SourceSharpMoveState's
	// vecCv* fields already are: this struct crosses to C# as a blittable
	// StructLayout.Sequential and a Vector-typed field would be blittable by
	// luck rather than by declaration.
	//
	// THE PRE-REGISTRATION VALUES STILL EXIST, and they are stock's. Native
	// readers before the managed side has registered — there are some, the
	// macros are used all over the shared tree — get shell_gamerules.cpp's
	// static, which is initialised to exactly the numbers Shared/Movement.cs
	// declares. gamerules_view_vectors_test.cpp asserts the two agree, so the
	// default can never silently become a second opinion.
	float vecView[3];            // VEC_VIEW
	float vecHullMin[3];         // VEC_HULL_MIN
	float vecHullMax[3];         // VEC_HULL_MAX
	float vecDuckHullMin[3];     // VEC_DUCK_HULL_MIN
	float vecDuckHullMax[3];     // VEC_DUCK_HULL_MAX
	float vecDuckView[3];        // VEC_DUCK_VIEW
	float vecObsHullMin[3];      // VEC_OBS_HULL_MIN
	float vecObsHullMax[3];      // VEC_OBS_HULL_MAX
	float vecDeadViewHeight[3];  // VEC_DEAD_VIEWHEIGHT
} SourceSharpMovementParams;

//-----------------------------------------------------------------------------
// Which kinds of damage behave which way. Five bitmasks of DMG_* flags.
//
// CMultiplayRules answered these by spelling a literal OR inside each method,
// and every one of them is POLICY rather than mechanism: acid keeps hurting
// you, a fall applies no physics force, a crowbar can gib a corpse. A mod that
// wanted burning to stop being time-based had to edit C++ to say so.
//
// PUSHED ONCE, like SourceSharpMovementParams and for the same reason: they are
// constants, they are read from damage paths that run per hit, and a crossing
// per hit to fetch a value that never changes is what section 3 of
// plan_server_all.md says to design away rather than pay.
//
// The native side carries STOCK'S VALUES as its defaults, so a build where
// managed code never registers behaves exactly like Half-Life rather than
// treating no damage as special -- the same guarantee shell_movement.cpp gives
// for jump height.
//-----------------------------------------------------------------------------
typedef struct
{
	// Applied a tick at a time rather than all at once.
	int32_t nTimeBased;

	// Need not supply a physics force and position. INCLUDES nTimeBased in
	// stock, and the managed declaration composes it the same way rather than
	// repeating the flags.
	int32_t nNoPhysicsForce;

	// Violent enough to gib a corpse.
	int32_t nShouldGibCorpse;

	// Draws no blood.
	int32_t nShouldNotBleed;

	// The HUD has directional art for it.
	int32_t nShowOnHud;
} SourceSharpDamageRules;

//-----------------------------------------------------------------------------
// The rules' CONSTANTS, stated once at startup by C#.
//
// A table rather than a call per virtual, and the reasoning is exactly
// SourceSharpDamageRules': every field below is a number a mod picks once and
// never changes, and several of the methods that read them are on paths that
// run per item, per weapon or per kill. A managed call for a value that was
// decided at startup is a boundary crossing to be told the same thing again.
//
// TWO FIELDS, DOWN FROM SEVENTEEN. The other fifteen fed methods that nothing
// in this build called -- the item and weapon respawn constants, the corpse
// drop rules, points per kill, the team id, the charger recharge time. Those
// methods are gone, and a constant whose only reader was a deleted method is
// dead data with a managed declaration behind it. What is left is the two with
// live callers, each named below.
//
// The DECISIONS are not here and must not move here. Anything that depends on
// who is asking -- FlPlayerFallDamage, ClientConnected, PlayerRelationship --
// is a managed CALL, because the answer is different every time and a table
// cannot hold it.
//-----------------------------------------------------------------------------
typedef struct
{
	// Whether this is a multiplayer game. IsMultiplayer, reached from
	// util_shared.cpp:1226.
	int32_t bMultiplayer;

	// bAllowDamage WENT AT plan_shim.md §5 3e, and it is the sixteenth field to
	// go for the reason the block comment above gives. Its one reader was
	// CSourceSharpGameRules::AllowDamage, whose only call site was
	// baseentity.cpp:1750 -- and game/server/baseentity.cpp is in neither vpc.
	// With the method gone the field was declared, pushed across once per level
	// and read by nothing, which is precisely the "dead data with a managed
	// declaration behind it" this struct's own comment refuses.
} SourceSharpGameRulesPolicy;

//-----------------------------------------------------------------------------
// WHICH COLLISION GROUPS COLLIDE, as a matrix rather than a function.
//
// CGameRules::ShouldCollide was sixty-seven lines of ordered ifs over two group
// indices -- pure integer logic, no entity, no engine call, and TOTAL over a
// domain of twenty by twenty. That makes it exactly the shape a table can hold,
// and the table is what removes it: the predicate is asked once per traced
// entity, so it was branching through up to fourteen comparisons on a path that
// runs thousands of times a frame.
//
// ROW g0, BIT g1. The predicate sorts its two arguments before deciding, so the
// relation is symmetric and the matrix is too -- which is asserted rather than
// assumed, on both sides.
//
// Twenty rows because LAST_SHARED_COLLISION_GROUP is twenty. A group index
// outside that range is not indexable and native answers stock's fallthrough
// (true) without touching the table.
//-----------------------------------------------------------------------------
typedef struct
{
	// One row per collision group; bit N of row M is "M collides with N".
	uint32_t nRows[ 20 ];
} SourceSharpCollisionRules;

// Everything a jump decision is given, in one crossing.
//
// One struct rather than a handle to ask questions of, and that shape is the
// whole lesson of measuring the boundary: ONE crossing costs about 7ns, which
// is nothing, but a decision that reached back for six more facts would pay it
// seven times. Weapon state is passed the same way for the same reason.
//
// It runs on BOTH sides, because movement is predicted — so a decision made
// from anything not in here, such as a counter the server alone keeps, will
// disagree with the client and show up as rubber-banding.
typedef struct
{
	// The player, by ENTITY INDEX rather than by handle.
	//
	// Not a preference: a server entity handle carries a serial number the
	// client's entity list knows nothing about, and this question is asked on
	// both sides. The index is what identifies a player across the wire, so it
	// is the only name for one that means the same thing in both answers.
	int32_t  nPlayerIndex;

	// Speed along the ground, which is what a rule about bunny-hopping needs.
	float    flHorizontalSpeed;

	// Straight up, before the jump is added. Negative while falling.
	float    flVerticalSpeed;

	int32_t  bDucking;

	// Ground material, as a surfaceprops game material character. Enough to
	// tell "you cannot jump off this" apart from ordinary ground.
	int32_t  nGroundMaterial;

	float    flCurTime;
} SourceSharpJumpRequest;

// Spectator modes. Source's OBS_MODE_*, with only the ones a deathmatch needs
// named — NONE is "in play", and the rest are ways of watching.
typedef enum
{
	SOURCESHARP_OBS_NONE     = 0,
	SOURCESHARP_OBS_DEATHCAM = 1,
	SOURCESHARP_OBS_FREEZECAM = 2,
	SOURCESHARP_OBS_IN_EYE   = 4,
	SOURCESHARP_OBS_CHASE    = 5,
	SOURCESHARP_OBS_ROAMING  = 7,
} SourceSharpObserverMode;

// How a screen fade behaves. Source's FFADE_*.
typedef enum
{
	SOURCESHARP_FADE_IN       = 0x01,  // fade FROM the colour, revealing the world
	SOURCESHARP_FADE_OUT      = 0x02,  // fade TO the colour
	SOURCESHARP_FADE_MODULATE = 0x04,  // modulate rather than blend
	SOURCESHARP_FADE_STAYOUT  = 0x08,  // hold indefinitely, until another fade arrives
	SOURCESHARP_FADE_PURGE    = 0x10,  // replace whatever fades the client already has
} SourceSharpFadeFlags;

// What a screen shake is being asked to do. Source's ShakeCommand_t.
typedef enum
{
	SOURCESHARP_SHAKE_START          = 0,
	SOURCESHARP_SHAKE_STOP           = 1,
	SOURCESHARP_SHAKE_AMPLITUDE      = 2,
	SOURCESHARP_SHAKE_FREQUENCY      = 3,
	SOURCESHARP_SHAKE_START_RUMBLE   = 4,
	SOURCESHARP_SHAKE_START_NORUMBLE = 5,
} SourceSharpShakeCommand;

// The trace mask vocabulary. Since plan_server_native_v2 T4d only the CLIENT's
// native trace arms read it: the server's trace slots take the engine's own
// MASK_*, which managed TraceMasks chooses from the same members. The "wired on
// BOTH arms" notes below date from when the server mapped it natively; the
// second arm is TraceMasks now (TraceMaskFoldTests).
typedef enum
{
	SOURCESHARP_TRACE_BRUSHES = 0,  // world and brush entities: MASK_SOLID_BRUSHONLY
	SOURCESHARP_TRACE_SHOT    = 1,  // what a bullet hits: MASK_SHOT

	// Brushes AND water, which is what "can this be heard from there" means.
	// Sound does not cross a water surface the way a bullet does, and the
	// soundscape system is where that distinction first mattered: a soundscape
	// on the far side of a pool should not reach a swimmer.
	SOURCESHARP_TRACE_BRUSHES_AND_WATER = 2,

	// EVERYTHING. MASK_ALL, which stops on anything a ray can stop on --
	// brushes, props, players, windows, water, hitboxes and debris alike.
	//
	// Added for the CLIENT trace (ClientTraceLine below), because it is what
	// stock's projected decal traces with (c_te_projecteddecal.cpp:128) and a
	// decal projected with a narrower mask would land on a different surface.
	// Wired on BOTH arms rather than only the one that needed it: this
	// vocabulary is one enumeration serving two implementations, and a value
	// the server's arm did not know would have been read there as the default
	// (brushes only) and answered a plausible wrong number in silence.
	SOURCESHARP_TRACE_ALL = 3,

	// MASK_SHOT_HULL, which is MASK_SHOT with the HITBOX bit taken out and the
	// GRATE bit put in (bspflags.h:128 against :127). Two differences and both
	// matter: a blast line that stopped on a player's hitbox would be blocked
	// by a body it should have thrown, and one that ignored a grating would
	// throw a ragdoll through a floor vent.
	//
	// Added for the ragdoll blast push (behaviour ledger row C1), which is what
	// c_te_explosion.cpp's CRagdollExplosionEnumerator traces with. Wired on
	// BOTH arms rather than only the client one that needed it, for
	// SOURCESHARP_TRACE_ALL's reason: this vocabulary is one enumeration
	// serving two implementations, and a value the server's arm did not know
	// would be read there as the default (brushes only) and answer a plausible
	// wrong number in silence.
	SOURCESHARP_TRACE_SHOT_HULL = 4,
	// MASK_SOLID. Solid brushes, moveables, windows, monsters and grates --
	// what a THING moving through the world runs into, as opposed to what a
	// bullet hits (MASK_SHOT, which adds debris and hitboxes and drops grates).
	//
	// Added for the legacy tempent pool's collision arm, which is the first
	// caller on either side to want it: c_te_legacytempents.cpp traces with
	// MASK_SOLID and nothing else in this ABI did. It is NOT a synonym for any
	// of the four above -- SOURCESHARP_TRACE_SHOT would make a gib stop on
	// debris and fall through a grate, and SOURCESHARP_TRACE_ALL would stop it
	// on water.
	//
	// Wired on BOTH arms, for SOURCESHARP_TRACE_ALL's reason: a value one arm
	// did not know would be read there as the default and answer a plausible
	// wrong number in silence.
	SOURCESHARP_TRACE_SOLID = 5,

	// MASK_BLOCKLOS -- CONTENTS_SOLID | CONTENTS_MOVEABLE | CONTENTS_BLOCKLOS,
	// which is "is the LINE OF SIGHT between these two points broken". It is
	// not a synonym for the brushes-only default: BLOCKLOS drops the window and
	// grate bits (you can see through both) and adds the CONTENTS_BLOCKLOS bit
	// a mapper paints on a brush to break sight without blocking anything else,
	// so a brushes-only trace answers "clear" through a blocklos volume and
	// "blocked" by a pane of glass, and both are the wrong answer to this
	// question.
	//
	// Added for the two debug-draw commands this lane brings back. `drawline`
	// colours its line by whether anything is in the way, and stock's
	// UTIL_AddDebugLine asked that with MASK_BLOCKLOS and nothing else
	// (game/server/ndebugoverlay.cpp, now sourcesharp/game/server/
	// shell_debugoverlays.cpp). The trace moves to managed code -- the colour
	// is the caller's, see AddLineOverlay below -- so the mask it asked with
	// has to be sayable on this boundary or the line would be coloured by a
	// different question, plausibly and silently.
	//
	// Wired on BOTH arms, for SOURCESHARP_TRACE_ALL's reason: a value one arm
	// did not know would be read there as the default and answer a plausible
	// wrong number in silence.
	SOURCESHARP_TRACE_BLOCKLOS = 6,

	// MASK_OPAQUE_AND_NPCS | CONTENTS_WINDOW -- solid, moveable, opaque and
	// monster contents, and glass. What the flashlight's direction and
	// pull-back traces stop on (flashlighteffect.cpp: `MASK_OPAQUE_AND_NPCS`,
	// less CONTENTS_HITBOX, which it does not have, plus CONTENTS_WINDOW). Not
	// SOURCESHARP_TRACE_SOLID: that has the grate bit, so the light would pull
	// back from a grating it shines through. Wired on BOTH arms, for
	// SOURCESHARP_TRACE_ALL's reason.
	SOURCESHARP_TRACE_OPAQUE_AND_NPCS_WINDOW = 7,

	// MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS -- what a glow's sight
	// line stops on (c_pixel_visibility.cpp's GlowSightDistance, C7): solid,
	// moveable and opaque contents, monsters and debris; no glass. Not
	// SOURCESHARP_TRACE_OPAQUE_AND_NPCS_WINDOW: that stops on glass, and a
	// glow behind a window would go out. Wired on BOTH arms, for
	// SOURCESHARP_TRACE_ALL's reason.
	SOURCESHARP_TRACE_GLOW_SIGHT = 8,

	// CONTENTS_SOLID alone -- what a glow overlay's line at the sky stops on
	// when the card cannot answer pixel visibility (glow_overlay.cpp, C7): solid
	// brushes and nothing else, the sky's surface flag read off what it hit.
	// Wired on BOTH arms, for SOURCESHARP_TRACE_ALL's reason.
	SOURCESHARP_TRACE_CONTENTS_SOLID = 9,
} SourceSharpTraceMask;

// WHICH ENTITIES A CLIENT TRACE IS ALLOWED TO HIT. Named modes, each one a
// stock filter class by name, rather than an "ignore this entity" parameter --
// and that distinction is the whole design.
//
// AN IGNORE PARAMETER IS NOT A SUPERSET. It would have to be implemented with
// CTraceFilterSimple, which differs from CTraceFilterHitAll in more than the
// pass entity: it also applies collision groups and
// g_pGameRules->ShouldCollide. So a caller passing "ignore nothing" would NOT
// get the hit-all trace it asked for -- it would get a filtered one that agrees
// with hit-all on most maps and disagrees wherever a collision group or a game
// rule says so, which is the kind of difference that shows up as one gib
// falling through one door.
//
// Each mode below names the stock filter it is, so a reader can go and look.
typedef enum
{
	// CTraceFilterHitAll. Everything, entities included. This is what the slot
	// did before there were modes, and it stays value 0 so that is still what
	// a caller gets by leaving the field alone.
	SOURCESHARP_TRACE_FILTER_HIT_ALL = 0,

	// CTraceFilterSimple( pPassEntity, collisionGroup ), with the pass entity
	// resolved from nPassEntityIndex and the group always COLLISION_GROUP_NONE.
	//
	// THE GROUP IS NOT A PARAMETER, and that is measured rather than assumed:
	// c_te_legacytempents.cpp's collision block passes COLLISION_GROUP_NONE and
	// says why in a comment -- nothing in this build ever calls
	// SetCollisionGroup on a tempent, so the value is whatever
	// C_BaseEntity::Clear left. A parameter with one possible value is a
	// parameter to get wrong.
	SOURCESHARP_TRACE_FILTER_SIMPLE = 1,

	// CTraceFilterWorldOnly. Brushes and the world, no entities at all --
	// what FTENT_COLLIDEWORLD traces with.
	SOURCESHARP_TRACE_FILTER_WORLD_ONLY = 2,

	// CTraceFilterNoNPCsOrPlayer( pPassEntity, COLLISION_GROUP_NONE ): what the
	// chase camera traces with (C_BasePlayer::CalcChaseCamView), passing its
	// target. C7 item 5.
	SOURCESHARP_TRACE_FILTER_NO_NPCS_OR_PLAYER = 3,

	// flashlighteffect.cpp's CTraceFilterSkipPlayerAndViewModel: a CTraceFilter
	// (no pass entity, no game rules) that hits everything but a player, a view
	// model, and an entity in COLLISION_GROUP_DEBRIS or
	// COLLISION_GROUP_INTERACTIVE_DEBRIS; a thing that is no entity (a static
	// prop) is hit. What the flashlight traces with (C7 cluster 12). Wired on
	// both client traces.
	SOURCESHARP_TRACE_FILTER_SKIP_PLAYER_AND_VIEW_MODEL = 4,

	// c_pixel_visibility.cpp's CTraceFilterGlow: CTraceFilterSimple( NULL,
	// COLLISION_GROUP_NONE ) that only an entity whose collideable is
	// SOLID_VPHYSICS or SOLID_BSP may stop. What a glow's sight line traces with
	// (C7 pixel visibility). Wired on both client traces.
	SOURCESHARP_TRACE_FILTER_GLOW = 5,
} SourceSharpTraceFilter;

// Which piece of the client's world render state ClientSetWorldRenderState is
// setting, and how many floats it expects with it.
//
// Every one of these is a stock map entity whose whole job is to hold numbers
// the renderer reads: shadow_control, water_lod_control, env_tonemap_controller.
// The entity is C#'s -- its keys, its inputs, when the numbers change -- and
// this is the one place those numbers cross back into native code.
typedef enum
{
	// dirX, dirY, dirZ, r, g, b, maxDist, disabled  (8)
	SOURCESHARP_WORLDRENDER_SHADOW   = 0,

	// cheapStart, cheapEnd  (2)
	SOURCESHARP_WORLDRENDER_WATERLOD = 1,

	// useExposureMin, useExposureMax, useBloomScale,
	// exposureMin, exposureMax, bloomScale, bloomScaleMinimum  (7)
	SOURCESHARP_WORLDRENDER_TONEMAP  = 2,
} SourceSharpWorldRenderState;

// What an entity is, as one answer. A radius query returns everything in range
// and the caller discards most of it, so these travel together rather than as
// four separate crossings per entity per tick.
typedef enum
{
	SOURCESHARP_ENTSTATE_SOLID       = 1 << 0,
	SOURCESHARP_ENTSTATE_PLAYER      = 1 << 1,
	SOURCESHARP_ENTSTATE_PARENTED    = 1 << 2,  // moves with something else, so pushing it does nothing
	SOURCESHARP_ENTSTATE_TAKESDAMAGE = 1 << 3,
	SOURCESHARP_ENTSTATE_PHYSICS     = 1 << 4,  // has a VPhysics object, whatever its movetype
	SOURCESHARP_ENTSTATE_ONGROUND    = 1 << 5,
} SourceSharpEntityState;

// Source's MOVETYPE_*, as far as anything managed needs to tell them apart.
typedef enum
{
	SOURCESHARP_MOVETYPE_NONE     = 0,
	SOURCESHARP_MOVETYPE_WALK     = 2,
	SOURCESHARP_MOVETYPE_STEP     = 3,
	SOURCESHARP_MOVETYPE_VPHYSICS = 6,
	SOURCESHARP_MOVETYPE_PUSH     = 7,
	SOURCESHARP_MOVETYPE_NOCLIP   = 8,
} SourceSharpMoveType;

//-----------------------------------------------------------------------------
// A user command, flattened to blittable fields.
//
// CUserCmd is a C++ class, so it is copied in and out around the managed call
// rather than shared. That is a small per-tick struct copy, which is acceptable
// here: CreateMove runs once per command, not per entity per prop. (Contrast
// entity state in plan.md Part 4, where copying would be fatal and the design
// shares memory instead.)
//-----------------------------------------------------------------------------
typedef struct
{
	uint32_t struct_size;

	float   viewangles[3];
	float   forwardmove;
	float   sidemove;
	float   upmove;
	int32_t buttons;
	int32_t impulse;
	int32_t weaponselect;
	int32_t command_number;
	int32_t tick_count;
	int32_t mousedx;
	int32_t mousedy;
} SourceSharpUserCmd;

//-----------------------------------------------------------------------------
// ONE COMMAND AS C# DECODED IT OFF THE WIRE -- plan_server_novalve.md P2.2.
// The flat command plus the two fields the native run loop reads that the flat
// one never carried: the weapon subtype and the command's shared random seed
// (MD5_PseudoRandom of its number, computed in C#). Filled by
// PlayerDecodeCommands; native turns each into a CUserCmd and queues it.
//-----------------------------------------------------------------------------
typedef struct
{
	SourceSharpUserCmd cmd;
	int32_t weaponsubtype;
	int32_t random_seed;
} SourceSharpDecodedUserCmd;

//-----------------------------------------------------------------------------
// THE ARGUMENTS OF ONE ENTITY OP -- plan_entity.md §3.4, step 1f.
//
// §3.4 replaces thirteen per-event ABI slots with one, `Dispatch( handle, op,
// args )`. Thirteen events do not have one argument list, so this is the union
// of them, flattened: every op reads the fields it has and ignores the rest.
//
// A STRUCT AND NOT A VARIADIC OR A BYTE BUFFER. Variadic across a
// function-pointer ABI is the one calling convention C# cannot state; a byte
// buffer would put a hand-written decoder on each side of a layout neither
// compiler checks. A fixed struct is checked by both -- COMPILE_TIME_ASSERT
// below and `EntityOpTests.OpArgsLayoutMatchesNative` on the far side.
//
// FIXED-WIDTH THROUGHOUT, per the port's standing rule: the fingerprint is
// computed from the C# declaration, so a C++ `int` that changed width is
// precisely the drift it cannot see. The pointers are the exception no rule
// covers -- they are 8 bytes on the one platform this mod builds for (linux64)
// and C# spells them the same way.
//
// THE ORDER IS CHOSEN SO THERE IS NO INTERIOR PADDING. The two 32-bit words
// after pBlob fill the eight bytes an 8-aligned pointer would otherwise leave,
// and the two trailing 32-bit words fill the tail. Padding is where two
// declarations of one layout disagree without either being wrong, so there is
// none: sizeof is exactly 64 and both sides assert that number rather than
// trusting their compiler and their marshaller to have agreed.
//
// THE BLOB TRAVELS WITH EVERY OP. §3.4: "the bootstrap thunk resolves the
// handle against the directory into a frame-scoped `ref struct EntityRef` that
// exposes the entity object and its blob `Span<byte>`". The shell has the blob
// pointer in hand at the call -- it IS the entity's own allocation -- so
// handing it over costs nothing and saves the managed side an EntityGetBlob
// round trip it would otherwise make per callback. pBlob is NULL and
// nBlobBytes 0 for an entity with no blob, which an EntityRef reports as an
// empty span rather than as a fault.
//-----------------------------------------------------------------------------
typedef struct
{
	// The entity's payload blob, engine block first (see engine_block.h).
	void        *pBlob;
	int32_t      nBlobBytes;

	// The other entity in this op: a toucher, an attacker, a blocker, an
	// activator, a filter's subject. The INVALID handle when the op has none,
	// never a null pointer -- "no entity" has a spelling everywhere else in
	// this API and it is a handle that does not resolve.
	uint32_t     hOther;

	// THE RULE IS: pText0 IS THE CLASSNAME whenever one is carried, and the
	// op's own strings are pText1 and pText2. SPAWN carries the classname
	// alone; KEYVALUE carries the classname, the key and the value; INPUT
	// carries the input name and the value as text. Three and not two because
	// KEYVALUE genuinely needs three: it arrives BEFORE the entity spawns, so
	// the managed instance does not exist yet and the classname is what builds
	// it -- asking the entity for its class instead would be a native round
	// trip per key of a map.
	const char  *pText0;
	const char  *pText1;
	const char  *pText2;

	// DAMAGE: where the blow landed, and which way it was going. POSITION_
	// CHANGED: the previous origin. NULL when the op has none.
	const float *pVec0;
	const float *pVec1;

	// DAMAGE: how much. INPUT: the value converted to a number, which is NOT
	// what a map writes -- a map sends "3" as a string and variant_t::Float()
	// answers zero for one, so the conversion happens on the native side where
	// the variant is.
	float        flFloat0;

	// DAMAGE: the DMG_ word. FILTER_PASSES: the damage type, and hOther is the
	// subject. Zero when the op has none.
	int32_t      nInt0;
} SourceSharpOpArgs;

//-----------------------------------------------------------------------------
// One entry from the engine's OWN console registry — see ConsoleSnapshot.
//
// Strings are copied into this rather than pointed at. The list being walked
// belongs to the engine and its entries go away when the DLL that registered
// them unloads, so a managed listing holding pointers into it would be a list
// of dangling pointers the first time a level change took a DLL with it.
//
// Every buffer is fixed and the copy is truncated to fit. A help text is prose
// meant to be read in a table cell, and the alternative — a second call per
// string to ask how long it is — would double the crossings for a listing that
// already makes thousands.
//-----------------------------------------------------------------------------
typedef struct
{
	char    szName[80];
	char    szValue[128];    // empty for a command: a command holds no value
	char    szDefault[128];  // likewise empty for a command
	char    szHelp[400];
	int32_t nFlags;          // FCVAR_* bits, exactly as it was registered with
	int32_t bIsCommand;      // 1 for a ConCommand, 0 for a ConVar

	// The bounds the convar clamps to, if it has any. Worth carrying because a
	// value that will not go past 100 is a fact about the setting that appears
	// nowhere in its name, its help or its flags -- and a listing that offers an
	// edit without it invites one the engine silently rewrites.
	int32_t bHasMin;
	float   flMin;
	int32_t bHasMax;
	float   flMax;
} SourceSharpConsoleEntry;

//-----------------------------------------------------------------------------
// One entry from CSoundEnt's active list — see SoundEntQuery.
//
// This is NOT a sound anybody hears. It is the world's record of something
// having happened loudly somewhere, which the AI used to read and which
// env_microphone reads instead: a gunshot, a bullet hitting a wall, a body
// landing. So there is no sound NAME here and there never was one — a CSound
// carries a place, a loudness and a kind, and nothing else.
//
// nVolume is CSound's own units (roughly "audible for this many inches"), not a
// gain, which is why the microphone divides a distance by it rather than
// multiplying.
//-----------------------------------------------------------------------------
typedef struct
{
	float    vecOrigin[3];
	int32_t  nType;      // SOUND_* bits, including the context bits
	int32_t  nVolume;
	uint32_t hOwner;     // what made the noise, or an invalid handle
} SourceSharpHeardSound;

//-----------------------------------------------------------------------------
// One titles.txt entry — see HudTextMessageGet.
//
// A copy rather than the engine's own pointer. client_textmessage_t points into
// a buffer the engine reallocates when the level changes, so a managed side
// holding it would be reading freed text at exactly the moment a map wanted to
// say something.
//
// The two colours, the effect and the four times are the same set a HudMsg
// carries, deliberately: once resolved, a titles.txt message IS a game_text and
// the client draws both through the same code.
//-----------------------------------------------------------------------------
typedef struct
{
	int32_t nEffect;
	uint8_t r1, g1, b1, a1;
	uint8_t r2, g2, b2, a2;
	float   flX, flY;
	float   flFadeIn, flFadeOut, flHoldTime, flFxTime;
	char    szMessage[512];

	// The message this one takes down when it arrives, which titles.txt entries
	// use to replace one another rather than stack up. Empty for most.
	char    szClearMessage[64];
} SourceSharpTextMessage;

//=============================================================================//
// TEMP ENTITIES — the tables both sides build, planned in C#.
//
// plan_tmp.md Part 3. A temp entity is a TABLE, not a class: the engine's
// PlaybackTempEntity takes a `const void *pSender` and a SendTable, and every
// prop reads at its own declared offset, so nothing here needs a C++ object. C#
// declares the fields (TeClass), EntityLayout computes the offsets, TePropPlanner
// plans the props, and each side's planner decides every field of every prop.
// What crosses is below; native stores it and decides nothing.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE SERVER'S TEMP ENTITY TABLES, DECIDED IN C# AND STORED HERE
// (plan_server_native_v2 T4b).
//
// The server plans each table in managed code (TePropPlanner, SendPropPlan):
// which props exist, their offsets and names, and every field dt_send.cpp's
// SendProp constructors would have computed. What crosses is one
// length-prefixed block (BitBlock: an int32 bit count, then the payload) per
// table: a SourceSharpTeTableHeader_t, nProps SourceSharpSendPropRow_t, and the
// names, NUL-terminated, at the payload offsets the header and rows give.
// Native stores the rows into SendProps, builds the SendTable and, if asked,
// the ServerClass, and decides nothing. The engine walks both by layout and
// calls the proxies through m_ProxyFn, which is why they are built natively.
//
// The client's tables cross the same way, with RecvProp rows (below).
//-----------------------------------------------------------------------------
typedef enum
{
	SOURCESHARP_SENDPROXY_NONE                    = 0,
	SOURCESHARP_SENDPROXY_INT8_TO_INT32           = 1,
	SOURCESHARP_SENDPROXY_INT16_TO_INT32          = 2,
	SOURCESHARP_SENDPROXY_INT32_TO_INT32          = 3,
	SOURCESHARP_SENDPROXY_UINT8_TO_INT32          = 4,
	SOURCESHARP_SENDPROXY_UINT16_TO_INT32         = 5,
	SOURCESHARP_SENDPROXY_UINT32_TO_INT32         = 6,
	SOURCESHARP_SENDPROXY_FLOAT_TO_FLOAT          = 7,
	SOURCESHARP_SENDPROXY_VECTOR_TO_VECTOR        = 8,
	SOURCESHARP_SENDPROXY_ANGLE_TO_FLOAT          = 9,
	SOURCESHARP_SENDPROXY_QANGLES                 = 10,
	SOURCESHARP_SENDPROXY_STRING_TO_STRING        = 11,
	SOURCESHARP_SENDPROXY_DATATABLE_TO_DATATABLE  = 12,   // a DPT_DataTable's table proxy
	SOURCESHARP_SENDPROXY_EMPTY                   = 13,   // a DPT_Array's (InternalSendPropArray)
	SOURCESHARP_SENDPROXY_COUNT                   = 14,
} SourceSharpSendProxy;

typedef struct
{
	int32_t  nProps;
	int32_t  bRegisterClass;    // 0 = a table other tables nest, with no ServerClass
	int32_t  oClassName;        // payload offsets of the two names
	int32_t  oTableName;
	uint32_t nShapeHash;        // managed's fingerprint, handed back on a re-registration
} SourceSharpTeTableHeader_t;

typedef struct
{
	int32_t nType;              // DPT_* (dt_common.h)
	int32_t nOffset;            // negative for a vector elem; the engine flips it
	int32_t nFlags;             // SPROP_*
	int32_t nBits;
	float   flLowValue;
	float   flHighValue;
	float   flHighLowMul;
	int32_t nElements;
	int32_t nElementStride;
	int32_t nProxy;             // SourceSharpSendProxy
	int32_t hDataTable;         // DPT_DataTable: a table built before this one; else -1
	int32_t oName;              // payload offset of the prop's name
} SourceSharpSendPropRow_t;

// What a build answers besides its handle. A class NAME built by an earlier
// load answers that table, with bExisted set and the hash it was built with:
// the tables are process-lifetime (the engine walked the class list once), so
// a managed reload gets its handles back rather than new tables.
typedef struct
{
	int32_t     bExisted;
	uint32_t    nShapeHash;
	const void *pSendTable;     // for PlaybackTempEntity
	int32_t    *pClassId;       // &ServerClass::m_ClassID, which the engine fills after DLL init; NULL with no class
} SourceSharpTeBuiltTable_t;

//-----------------------------------------------------------------------------
// THE CLIENT'S TEMP ENTITY TABLES, DECIDED IN C# AND STORED HERE, the same way.
//
// The client plans each table from the same TePropPlanner plan the server's
// SendTables come from, and RecvPropPlan decides every field dt_recv.h's
// RecvProp builders would have: the recv name, the type, the positive offset,
// the flags and the standard proxy. The block is the server's shape with a
// SourceSharpRecvPropRow_t per prop: a SourceSharpTeTableHeader_t, the rows, and
// the names. Native stores the rows into RecvProps, builds the RecvTable and, if
// asked, the ClientClass and its receiving shell, and decides nothing. The
// engine walks those by layout and compares a prop's proxy with
// g_StandardRecvProxies, which is why they are built natively.
//-----------------------------------------------------------------------------
typedef enum
{
	SOURCESHARP_RECVPROXY_NONE                = 0,   // a DPT_Array (InternalRecvPropArray, no length proxy)
	SOURCESHARP_RECVPROXY_INT32_TO_INT8       = 1,
	SOURCESHARP_RECVPROXY_INT32_TO_INT16      = 2,
	SOURCESHARP_RECVPROXY_INT32_TO_INT32      = 3,
	SOURCESHARP_RECVPROXY_FLOAT_TO_FLOAT      = 4,
	SOURCESHARP_RECVPROXY_VECTOR_TO_VECTOR    = 5,
	SOURCESHARP_RECVPROXY_STATIC_DATATABLE    = 6,   // a DPT_DataTable's table proxy
	SOURCESHARP_RECVPROXY_COUNT               = 7,
} SourceSharpRecvProxy;

typedef struct
{
	int32_t nType;              // DPT_* (dt_common.h)
	int32_t nOffset;            // into the blob, never negated
	int32_t nFlags;             // SPROP_*
	int32_t nElements;          // m_nElements: 1 unless a DPT_Array
	int32_t nElementStride;     // m_ElementStride: -1 unless a DPT_Array
	int32_t nProxy;             // SourceSharpRecvProxy
	int32_t hDataTable;         // DPT_DataTable: a table built before this one; else -1
	int32_t oName;              // payload offset of the prop's name
} SourceSharpRecvPropRow_t;

// What a client build answers besides its handle: a class NAME built by an
// earlier load answers that table, with bExisted set and the hash it was built
// with, because the engine walked the class list once.
typedef struct
{
	int32_t  bExisted;
	uint32_t nShapeHash;
} SourceSharpTeRecvBuilt_t;

//-----------------------------------------------------------------------------
// ONE POOLED TEMP ENTITY'S LIFETIME, as the managed pool reads and writes it
// (plan_tmp.md Part 5).
//
// c_te_legacytempents.cpp's CTempEnts is a pool of 500 C_LocalTempEntity --
// gibs, shell casings, sprites and debris. Phase C4 moves the POOL to C#: the
// allocation, the free list, the priority classes, the expiry and the update
// loop. What stays here is the OBJECT, because a C_LocalTempEntity is already
// the draw shell -- de-entified onto CShellRenderable, which
// src/sourcesharp/test/tempent_verdict_test.cpp records the verdict for.
//
// SO THIS STRUCT IS A VIEW, NOT A SECOND COPY OF THE STATE. It is filled from
// the live object on every read and written straight back into it on every
// write; nothing here is stored anywhere. The managed pool holds the LIST and
// the POLICY, never the fields -- native Frame() mutates die and flags every
// tick (a gib that lands clears FTENT_ROTATE; a FTENT_COLLIDEKILL tempent sets
// die = curtime), and a mirror would go stale within one frame of a bounce.
//
// EIGHT FIELDS, AND EACH ONE IS READ BY THE EXPIRY DECISION. C_LocalTempEntity
// has thirty-odd members; these are the ones CTempEnts::IsActive,
// CTempEnts::FreeLowPriorityTempEnt and CTempEnts::KillAttachedTents touch.
// Anything else a managed spawn helper needs is a separate slot named for what
// it sets, never an extension of this one -- the moment this struct grows a
// field nothing here reads, it has stopped being a view.
//
// AbiParityTests::TheTempEntLifeStructMatchesTheHeader holds the two halves
// together, for the reason SourceSharpEffectData_t has the same test: every
// field is one word and each has a plausible neighbour of the same width, so a
// one-sided insertion shifts everything after it and compiles on both sides.
// The field names are UNPREFIXED for that test's sake -- it matches header
// names against managed ones directly, and SourceSharpEffectData_t is the
// struct that established the convention.
//-----------------------------------------------------------------------------
typedef struct
{
	float   Die;              // ::die -- the curtime this tempent expires at
	int32_t Flags;            // ::flags -- the FTENT_* set, tempent.h
	float   FadeSpeed;        // ::fadeSpeed -- the FTENT_FADEOUT ramp's rate
	int32_t RenderAmt;        // ::tempent_renderamt -- the alpha the ramp is OF
	int32_t Priority;         // ::priority -- TENTPRIORITY_LOW / _HIGH
	int32_t ClientIndex;      // ::clientIndex -- FTENT_PLYRATTACHMENT's subject
	int32_t RenderMode;       // ::m_nRenderMode -- kRender*, flipped when fading
	int32_t Alpha;            // ::m_clrRender.a -- what the ramp writes
} SourceSharpTempEntLife_t;

//-----------------------------------------------------------------------------
// A POOLED TEMP ENTITY'S TRANSFORM, WHICH C# OWNS (behaviour ledger row C4).
//
// This is the decision the row turned on. C_LocalTempEntity::Frame integrates a
// place, a velocity and a spin every tick, and none of that has ever crossed
// this boundary -- SourceSharpTempEntLife_t carries Die, Flags, FadeSpeed,
// RenderAmt, Priority, ClientIndex, RenderMode and Alpha, and nothing else. So
// porting Frame meant choosing where the transform lives, and it lives in C#.
//
// WHAT THAT RULES OUT, deliberately: a per-field accessor. Frame reads and
// writes the origin, the angles and three vectors several times each, so
// GetLocalOrigin/SetLocalOrigin as slots would be a dozen crossings per tempent
// per frame, for hundreds of tempents. This is ONE struct written ONCE per
// tempent per frame, and native reads it back when it draws.
//
// WHAT IS DELIBERATELY NOT HERE, and this was measured rather than assumed:
// m_vecPrevLocalOrigin, m_vLastCollisionOrigin and m_iLastCollisionFrame. They
// look like shared state and are not -- grepping the client for all three finds
// readers and writers ONLY inside Frame itself. Once C# owns Frame they are
// entirely its own working state, so putting them in this struct would be
// carrying a function's locals across an ABI twice a frame for nothing.
//
// (m_vecPrevLocalOrigin is also private with no accessor, so crossing it would
// have meant editing a stock header to export something nothing outside that
// one function ever reads.)
//-----------------------------------------------------------------------------
typedef struct SourceSharpTempEntTransform_s
{
	float   OriginXYZ[ 3 ];
	float   AnglesPYR[ 3 ];          // QAngle: pitch, yaw, roll

	float   VelocityXYZ[ 3 ];
	float   AccelerationXYZ[ 3 ];
	float   AngVelocityPYR[ 3 ];     // QAngle per second

	// The animating sprite's state, which Frame advances alongside the place.
	float   flSpriteScale;
	float   flFrame;

	// FTENT_SINEWAVE's two accumulators -- stock's `x` and `y`. They are in the
	// TRANSFORM and not only in the spawn record because Frame ADVANCES them
	// every tick: the spawn record is written once and these move with the
	// place. A sine-wave tempent whose accumulators did not cross back would
	// restart its wobble from the spawn value every frame.
	float   flSineX;
	float   flSineY;
} SourceSharpTempEntTransform_t;

//-----------------------------------------------------------------------------
// A POOLED TEMP ENTITY'S WRITE-ONCE STATE, read ONCE when C# takes it over
// (behaviour ledger row C4).
//
// WHY THIS IS A THIRD STRUCT AND NOT A BIGGER TRANSFORM. The transform is the
// thing Frame INTEGRATES -- it changes every tick and crosses back every frame.
// These six never change after the tempent is built: they are what the spawn
// helper decided, and Frame only READS them. Putting them in the transform
// would move twenty-four bytes per tempent per frame that cannot have changed,
// and the transform's own comment above is the argument for why that boundary
// is worth keeping tight.
//
// AND WHY THEY CANNOT COME FROM SourceSharpTempEntSpawn_t INSTEAD, which is the
// obvious alternative and does not work: that record is written by a MANAGED
// spawn helper, and this build still has NATIVE ones. c_stickybolt.cpp calls
// SpawnTempModel, the pool's own debug command calls AttachTentToPlayer, and
// sdk_fx_weaponfx.cpp's EjectBrass_9mm/12Gauge reach CSEjectBrass -- which sets
// bounceFactor and hitSound directly on the object and never touches a spawn
// record. A managed cache of what C# itself wrote would simulate those three
// with Prepare's defaults: a shell casing with no bounce sound, and a player
// attachment at the player's feet instead of forty units up.
//
// So this is asked of the OBJECT, which is the one place all four spawn routes
// agree.
//
// AbiParityTests-style pairing: TempEntFrameTests.TheStaticsStructMatchesTheHeader
// holds the two halves together, for SourceSharpTempEntLife_t's reason.
//-----------------------------------------------------------------------------
typedef struct SourceSharpTempEntStatics_s
{
	// ::bounceFactor -- how much speed survives a bounce. Prepare's default is
	// ONE, not zero, and a zero here is a gib that sticks where it lands.
	float   flBounceFactor;

	// ::hitSound -- a BOUNCE_* sound kind, and zero for silence. Frame's own
	// gate is `if ( hitSound )` before it calls PlaySound.
	int32_t nHitSound;

	// ::m_flFrameMax and ::m_flFrameRate -- what FTENT_SPRANIMATE and
	// FTENT_SPRCYCLE advance ::m_flFrame against. An animating sprite whose
	// FrameMax read zero would run off its last frame on the first tick and
	// (without FTENT_SPRANIMATELOOP) ask to die immediately.
	float   flFrameMax;
	float   flFrameRate;

	// ::tentOffset -- where a FTENT_PLYRATTACHMENT tempent sits relative to the
	// player it is stuck to. AttachTentToPlayer's zoffset lands in [2].
	float   TentOffsetXYZ[ 3 ];

	// ::m_hOwnerEntity as an INDEX -- the entity Frame's CTraceFilterSimple
	// passes through, so a projectile does not immediately hit whoever fired
	// it. -1 for none.
	//
	// TAKEN FROM THE HANDLE'S ENTRY INDEX, which is bit extraction and not a
	// lookup: dereferencing a client handle through CBaseHandle::Get() is
	// behaviour ledger row C8, and this side's table holds managed entities.
	int32_t nOwnerEntityIndex;

	// ::m_nFlickerFrame -- the 0..31 frame a FTENT_FLICKER tempent lights on.
	// Prepare sets it to zero and nothing in this build ever sets it otherwise,
	// which is exactly why it is READ rather than assumed to be zero.
	int32_t nFlickerFrame;

	// WHETHER ::m_pszImpactEffect IS SET, and NOT the name itself. Behaviour
	// ledger row C5, and the shape of it is the point.
	//
	// The row's guarded code is stock's `if ( m_pszImpactEffect )` block, which
	// dispatches an effect carrying the TEAM NUMBER of the client entity the
	// tempent belongs to -- a number this build cannot answer, because no client
	// entity is a C_BasePlayer to dynamic_cast to. The branch is also
	// UNREACHABLE here: m_pszImpactEffect is only ever set by
	// CTempEnts::ClientProjectile's optional pszImpactEffect parameter, and both
	// live callers leave it at its NULL default -- which
	// src/sourcesharp/test/tempent_verdict_test.cpp asserts.
	//
	// So the NAME does not cross and no managed dispatch is written: that would
	// be fabricating a capability for a branch nothing takes, and dead code that
	// looks like a finished port reads as though the row were one line from
	// closing. What crosses is the one bit that keeps the row's call site REAL --
	// if anything ever does set an impact effect, the managed arm says so once
	// per level and names the row, instead of the effect silently not happening.
	int32_t bHasImpactEffect;
} SourceSharpTempEntStatics_t;

//-----------------------------------------------------------------------------
// ONE POOLED TEMP ENTITY'S SPAWN, as a managed spawn helper writes it
// (plan_tmp.md Part 5, C-Phase 5).
//
// WHY THIS IS NOT AN EXTENSION OF SourceSharpTempEntLife_t, which is the first
// thing anyone reading the two together will want to do. That struct is a VIEW:
// eight fields, every one read by the expiry decision, read AND written back
// sixty times a second per live tempent. These are written ONCE, at allocation,
// and most of them are never read across this boundary again -- a sprite's
// frame rate, a gib's angular velocity, the XY origin a sine wave oscillates
// about. Growing the life record to carry them would put thirty-eight words
// across the boundary per tempent per frame to move values nothing on this side
// asks for twice, and it would stop the life record being a view, which is the
// one property that makes it safe to hold no managed copy of it.
//
// THE OVERLAP IS REAL AND IS NOT A MISTAKE. Flags, Die, RenderAmt, RenderMode,
// FadeSpeed and ClientIndex appear in both, because they are set at spawn AND
// read every frame. The alternative was a spawn that wrote the other thirty-two
// fields and then made a SECOND crossing through TempEntPoolWriteLife for those
// six, which is two calls where one does and two places for a helper to forget
// one.
//
// EVERY FIELD IS WRITTEN, unconditionally. There is no mask and no "leave this
// one alone": TempEntPoolCreate has just run C_LocalTempEntity::Prepare, so the
// object is in stock's known post-Prepare state, and C# builds the whole record
// from TempEntSpawn.Prepared -- the managed mirror of that same function, held
// against the real one by TempEntSpawnTests.ThePreparedDefaultsAreStocksPrepare,
// which reads Prepare's body out of c_te_legacytempents.cpp off disk. A partial
// write would be a helper that inherits whatever the previous tenant of a
// recycled slot left behind.
//
// AbiParityTests-style pairing: TempEntSpawnTests.TheSpawnStructMatchesTheHeader
// holds the two halves together, for SourceSharpTempEntLife_t's reason -- every
// field is one word and each has a plausible neighbour of the same width, so a
// one-sided insertion shifts everything after it and compiles on both sides.
// The field names are UNPREFIXED for that test's sake.
//-----------------------------------------------------------------------------
typedef struct
{
	// --- the flag word and the clock -----------------------------------------
	int32_t Flags;             // ::flags -- the FTENT_* set, WHOLE, not OR'd
	float   Die;               // ::die -- the curtime this tempent expires at

	// --- the sprite animation ------------------------------------------------
	float   SpriteScale;       // ::m_flSpriteScale
	float   Frame;             // ::m_flFrame -- the frame it starts on
	float   FrameMax;          // ::m_flFrameMax -- usually frameCount - 1
	float   FrameRate;         // ::m_flFrameRate

	// --- the studio model's look ---------------------------------------------
	int32_t Body;              // ::m_nBody -- BreakModel randomises this for mod_studio
	int32_t Skin;              // ::m_nSkin -- also stock's FindTempEntByID hack

	// --- the render state ----------------------------------------------------
	int32_t RenderMode;        // ::m_nRenderMode, through SetRenderMode
	int32_t RenderFx;          // ::m_nRenderFX
	int32_t RenderAmt;         // ::tempent_renderamt -- what FTENT_FADEOUT ramps FROM
	float   FadeSpeed;         // ::fadeSpeed
	int32_t ColorR;            // ::m_clrRender, through SetRenderColor
	int32_t ColorG;
	int32_t ColorB;
	int32_t ColorA;

	// --- the physics ---------------------------------------------------------
	float   VelocityXYZ[3];         // SetVelocity
	float   AccelerationXYZ[3];     // SetAcceleration
	float   AngularVelocityPYR[3];  // ::m_vecTempEntAngVelocity -- FTENT_ROTATE's rate
	float   AnglesPYR[3];           // SetAbsAngles
	float   BounceFactor;        // ::bounceFactor

	// --- FTENT_SINEWAVE's axis -----------------------------------------------
	//
	// Stock calls them x and y and they are NOT the origin: Frame() oscillates
	// about this point while the tempent rises, so a bubble spawned at its own
	// origin has x,y set to that origin and then wanders around it.
	float   SineX;             // ::x
	float   SineY;             // ::y

	// --- the surface a sprite is lying on -------------------------------------
	float   NormalXYZ[3];         // ::m_vecNormal

	// --- stock's own hack, carried because FindTempEntByID reads it -----------
	//
	// "HACK HACK: We're using skin and hitsounds as a hacky way to store an ID
	// and sub-ID for later identification" -- stock's comment, above
	// FindTempEntByID. BreakModel also uses hitSound for something else
	// entirely: it stores the BREAK_* flags there so PlaySound knows which
	// bounce sound to make.
	int32_t HitSound;          // ::hitSound
	int32_t ClientIndex;       // ::clientIndex

	// --- the two that are not a field write ----------------------------------
	//
	// NAMED FOR WHAT THEY DO rather than folded into a kind enum with a params
	// blob, which is the shape lane CP's slots established and this port holds
	// to. Each is one native action a managed helper cannot perform itself.

	// Join the break-model lighting group: 0 not at all, 1 as the group's
	// master, 2 as a slave. CBreakableHelper is a linked list of break batches
	// so that fifteen gibs share one ModelRenderInfo_t::pLightingOrigin instead
	// of building fifteen light caches; its only reader is that pointer, and a
	// managed list could not supply one. Setting this ALSO installs
	// BreakModelDrawHelper, because in stock the two are one decision made on
	// two consecutive lines and separating them would let a caller ask for the
	// lighting group and not the draw path that reads it.
	int32_t BreakHelper;

	// The entity this tempent belongs to, or -1 for none. Stock's
	// SetTempEntOwner, which stores an EHANDLE that Frame()'s collision arm
	// passes to its trace filter as the entity to ignore -- a projectile must
	// not immediately hit whoever fired it. An INDEX, never a handle: reading a
	// client handle back through CBaseHandle::Get() is behaviour ledger row C8,
	// which segfaults on this build's managed entities.
	int32_t OwnerEntityIndex;

	// --- a tempent that rides a player ---------------------------------------
	//
	// ::tentOffset: where above the player an FTENT_PLYRATTACHMENT tempent sits
	// (AttachTentToPlayer, lane c2-fx). The managed frame reads it back off the
	// statics each frame to follow the player.
	float   TentOffsetXYZ[3];
} SourceSharpTempEntSpawn_t;

// What a model IS, for the two helpers that branch on it. Source's modtype_t
// (model_types.h), with the two values anything in this port distinguishes.
// BreakModel randomises m_flFrame for a sprite and m_nBody for a studio model,
// which is the only place the distinction is read.
typedef enum
{
	SOURCESHARP_MODELTYPE_BAD    = 0,
	SOURCESHARP_MODELTYPE_BRUSH  = 1,
	SOURCESHARP_MODELTYPE_SPRITE = 2,
	SOURCESHARP_MODELTYPE_STUDIO = 3,
} SourceSharpModelType;

//-----------------------------------------------------------------------------
// ONE DISPATCHED EFFECT'S DATA. Mirrors CEffectData (effect_dispatch_data.h),
// which is the payload of DT_EffectData and the busiest thing in this port --
// DispatchEffect("Impact"), "watersplash", "bloodspray" and "GlassImpact" all
// ride it.
//
// WHY A STRUCT RATHER THAN THIRTY ARGUMENTS. The other backends here are four
// or five values and pass them flat; this one is twenty-one fields and would
// be a call nobody could read, with a transposition invisible at both ends. It
// crosses BY POINTER, filled in C# and read field by field in native code --
// the same direction a gib shot went (SourceSharpGibShot, gone with its slot), and the same hazard: a field
// inserted on one side only shifts every field after it and the symptom is an
// effect at plausible but wrong values rather than a crash.
// AbiParityTests::TheEffectDataStructMatchesTheHeader is what holds the two
// halves together.
//
// NO m_iEffectName. That member is an index into the EffectDispatch network
// string table, and this boundary carries the NAME instead: the registry the
// name resolves against (CClientEffectRegistration's list, 52 entries) stays
// native and is matched by string, so an index would be a number C# would have
// to keep in step with a table it does not own.
//
// THE ENTITY IS AN INDEX, not an ehandle. CEffectData's client half holds a
// ClientEntityHandle_t and its server half an int; the wire carries an edict
// index (prop "entindex"), so the index is the thing both sides actually agree
// on. Native turns it back into a handle exactly as
// effect_dispatch_data.cpp:31 does. Negative means "no entity".
//
// SurfaceProp is the RAW surface index, not the +1 the wire carries (plan 2B).
// The +1 belongs to the encoding, and this struct is downstream of the decode.
//-----------------------------------------------------------------------------
typedef struct
{
	float   OriginXYZ[3];
	float   StartXYZ[3];
	float   NormalXYZ[3];
	float   AnglesPYR[3];
	int32_t Flags;
	int32_t EntIndex;
	float   Scale;
	float   Magnitude;
	float   Radius;
	int32_t AttachmentIndex;
	int32_t SurfaceProp;
	int32_t Material;
	int32_t DamageType;
	int32_t HitBox;
	int32_t Color;
	int32_t CustomColors;
	float   Color1XYZ[3];
	float   Color2XYZ[3];
	int32_t ControlPoint1;
	int32_t ControlPointAttachment;
	float   ControlPointOffsetXYZ[3];
} SourceSharpEffectData_t;

//-----------------------------------------------------------------------------
// plan_entity.md step 3e -- THE MOVEMENT LOOP'S ONE CROSSING.
//
// CGameMovement::ProcessMovement runs in C# and this is what crosses: the whole
// of CMoveData plus the IMovementPlayer fields the loop reads, IN and OUT, once
// per command. §3.10's "IGameMovement is a shim vtable copying CMoveData
// across", made literal.
//
// ONE STRUCT AND NOT A HANDLE TO ASK QUESTIONS OF, for the reason
// SourceSharpJumpRequest already gives: a crossing costs about 7ns, which is
// nothing, and a loop that reached back for eighty more facts would pay it
// eighty times. The cost was never the crossing; it was the number of them.
//
// THE CONSTANTS TRAVEL WITH IT -- the tail of this struct is sv_friction and
// friends and the gamerules hull. 46 more words a command, deliberately, because
// every one of them is a convar an operator can move mid-round and a cached copy
// is a staleness class the movement code cannot detect. The cost is recorded in
// the step's handover rather than argued away.
//
// GENERATED, NOT WRITTEN. The field list lives once in tools/3e-gen-movestate,
// and this and SourceSharp.Abi's MoveStateBlob are both printed from it -- two
// hand-written declarations of one layout is the drift the lane brief's second
// question is about. MoveStateBlob.WordCount is checked against this on both
// sides.
//
// int32_t rather than bool on every flag, and float rather than Vector: this
// struct is mirrored byte-for-byte in C#, and a C++ width change is precisely
// the drift a mirror cannot see.
//-----------------------------------------------------------------------------
typedef struct SourceSharpMoveState
{
	int32_t nCommandNumber;
	float   flFrameTime;
	int32_t nMoveType;
	int32_t nFlags;
	int32_t nWaterLevel;
	int32_t nWaterType;
	int32_t bDeadFlag;
	float   vecOrigin[3];
	float   vecVelocity[3];
	float   vecBaseVelocity[3];
	float   angViewAngles[3];
	float   flForwardMove;
	float   flSideMove;
	float   flUpMove;
	int32_t nButtons;
	int32_t nOldButtons;
	float   flMaxSpeed;
	float   flClientMaxSpeed;
	int32_t bDucked;
	int32_t bDucking;
	int32_t bInDuckJump;
	float   flDuckTime;
	float   flDuckJumpTime;
	float   flJumpTime;
	float   flFallVelocity;
	float   flStepSize;
	float   vecViewOffset[3];
	float   angPunchAngle[3];
	float   angPunchAngleVel[3];
	float   flEntityGravity;
	float   flCurrentGravity;
	float   flSurfaceFriction;
	int32_t bHasGround;
	float   vecGroundVelocity[3];
	float   flWaterJumpTime;
	float   vecWaterJumpVel[3];
	float   flSwimSoundTime;
	float   flStepSoundTime;
	int32_t nHealth;
	int32_t bGameCodeMovedPlayer;
	float   flConstraintRadius;
	float   flConstraintWidth;
	float   flConstraintSpeedFactor;
	float   vecConstraintCenter[3];
	float   flOldForwardMove;
	int32_t nImpulseCommand;
	float   flLaggedMovementValue;
	int32_t nSurfaceProps;
	float   vecLadderNormal[3];
	float   flOutStepHeight;
	float   vecOutWishVel[3];
	float   vecOutJumpVel[3];
	int32_t nMoveCollide;
	int32_t nOnLadder;
	int32_t nOldWaterLevel;
	int32_t bIsAlive;
	int32_t nObserverMode;
	int32_t bInAVehicle;
	float   flModelScale;
	int32_t nEntIndex;
	int32_t nStuckLast;
	float   angAngles[3];
	float   angOldAngles[3];
	int32_t bFirstRunOfFunctions;
	float   flCurTime;
	int32_t bSurfaceDataValid;
	int32_t bAllowAutoMovement;
	int32_t bSlowMovement;
	int32_t bIsServer;
	float   flCvFriction;
	float   flCvAccelerate;
	float   flCvAirAccelerate;
	float   flCvStopSpeed;
	float   flCvMaxVelocity;
	float   flCvMaxSpeed;
	float   flCvBounce;
	float   flCvRollAngle;
	float   flCvRollSpeed;
	float   flCvNoClipSpeed;
	float   flCvNoClipAccelerate;
	float   flCvSpecSpeed;
	float   flCvSpecAccelerate;
	float   flCvAirSpeedCap;
	float   flCvDuckSpeedScale;
	int32_t bCvOptimizations;
	int32_t bCvSpecNoClip;
	int32_t nCvMaxClients;
	float   flCvTickInterval;
	float   vecCvView[3];
	float   vecCvHullMin[3];
	float   vecCvHullMax[3];
	float   vecCvDuckHullMin[3];
	float   vecCvDuckHullMax[3];
	float   vecCvDuckView[3];
	float   vecCvObsHullMin[3];
	float   vecCvObsHullMax[3];
	float   vecCvDeadViewHeight[3];
	float   flCvJumpHeight;

	// engine->Time(), read only by CheckStuck's throttle.
	float   flEngineTime;
} SourceSharpMoveState;

//-----------------------------------------------------------------------------
// What a swept player hull found -- the whole of trace_t the movement loop
// reads, counted from gamemovement.cpp rather than guessed.
//
// bHasGround is MovementGroundFromTrace's answer and NOT bHasEntity: a static
// prop is in the same partition and reported in the same field, has no entity
// behind it, and stock has always treated standing on one as standing on
// nothing. Both are here because CategorizePosition tests the raw pointer and
// SetGroundEntity tests the resolved one.
//-----------------------------------------------------------------------------
typedef struct SourceSharpMoveTraceResult
{
	float   flFraction;
	float   vecEndPos[3];
	float   vecPlaneNormal[3];
	float   flPlaneDist;
	int32_t bStartSolid;
	int32_t bAllSolid;
	int32_t nSurfaceProps;
	int32_t nSurfaceFlags;
	int32_t nContents;
	int32_t bHasGround;
	float   vecGroundVelocity[3];
	int32_t bHasEntity;
	int32_t bHitWorld;
} SourceSharpMoveTraceResult;

//-----------------------------------------------------------------------------
// One surfaceprop, as the movement loop reads it: the four `game` fields it
// uses and the physics friction behind them.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// plan_entity.md step 6f -- WHAT NATIVE STILL ASKS ABOUT THE VIEWER.
//
// The managed client player owns its own state; this is the small part of it
// the client DLL cannot go without, published once a frame through
// ClientPlayerPredictionDrive. Fixed-width throughout: the C# side mirrors it
// byte for byte and a plain `int` here is precisely the drift the fingerprint
// cannot see.
//-----------------------------------------------------------------------------
typedef struct SourceSharpClientPlayerView
{
	int32_t struct_size;

	// Where the replay says they are. Written into the block's head only when
	// bDriving; otherwise the interpolated origin stands and this is the
	// decoded one, which is the same number the eye is measured from.
	float   vecOrigin[3];

	// Eyes above feet. Ducking moves it, and the view, the ears and the
	// third-person camera all measure from it.
	float   vecViewOffset[3];

	// The view kick in flight and its decay rate. Read by the view pass, which
	// adds it to the frame's angles in CSourceSharpClientMode::OverrideView.
	// (cs_cl_vieweffects_report printed it beside the displacement the frame
	// actually applied, which was the one reading that could tell "the punch
	// never arrived" from "the punch arrived and nothing applied it". Phase 1c
	// deleted it with the other client instruments.)
	float   angPunch[3];
	float   angPunchVel[3];

	// Whether the origin above is the replay's answer. It is the interpolation
	// pass's filter: a point read from the networked past must not overwrite an
	// origin the replay has already computed for now.
	int32_t bDriving;

	// WHAT THE HUD SHOWS. It is here rather than read out of the blob at a
	// quoted offset, and the difference is a bug this step hit live:
	// standalone_player_state.h restated Health's placement as a #define for
	// "native readers that cannot ask C#", claimed it was pinned from the C#
	// side, and NOTHING PINNED IT -- the named test did not exist. The HUD's
	// first paint read 1544355841 out of it. One declaration and one writer is
	// the whole of the fix.
	int32_t nHealth;
} SourceSharpClientPlayerView;

//-----------------------------------------------------------------------------
// The effects the movement loop triggers. ONE slot with a kind rather than nine
// slots, because they are all "tell the world something happened" and only one
// of them has an answer -- and nine slots would be nine places for the ABI to
// drift.
//-----------------------------------------------------------------------------
typedef enum
{
	SOURCESHARP_MOVE_EFFECT_RESET_TOUCH_LIST = 0,
	SOURCESHARP_MOVE_EFFECT_ADD_TO_TOUCHED,
	SOURCESHARP_MOVE_EFFECT_START_SOUND_SWIM,
	SOURCESHARP_MOVE_EFFECT_SET_ANIMATION,

	// The ONE with an answer: false when the landing killed them.
	SOURCESHARP_MOVE_EFFECT_FALLING_DAMAGE,

	SOURCESHARP_MOVE_EFFECT_PLAY_STEP_SOUND,
	SOURCESHARP_MOVE_EFFECT_UPDATE_STEP_SOUND,
	SOURCESHARP_MOVE_EFFECT_SPLASH,
	SOURCESHARP_MOVE_EFFECT_RUMBLE,
	SOURCESHARP_MOVE_EFFECT_RESET_DUCK_LATCHED,
	SOURCESHARP_MOVE_EFFECT_SET_PLAYER_SURFACE,
} SourceSharpMoveEffect;

//-----------------------------------------------------------------------------
// Native functions handed TO managed. All strings are UTF-8.
// Blittable types only — no marshalling on any of these.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// What EntityParentProbe answers with. A BIT FIELD rather than an enum,
// because two of the three are independent facts about one entity: which
// hierarchy holds it, and whether it has a skeleton.
//
// STANDALONE and BASE partition the world exactly -- an entity is on one
// hierarchy or the other and never on both -- so a probe answering both bits
// would be a bug on the native side and is worth being able to SEE rather than
// having to infer from a collapsed enum.
//-----------------------------------------------------------------------------
#define SOURCESHARP_PARENT_PROBE_STANDALONE 0x1
#define SOURCESHARP_PARENT_PROBE_BASE       0x2
#define SOURCESHARP_PARENT_PROBE_CAN_POSE   0x4

//-----------------------------------------------------------------------------
// THE PLAYER'S PHYSICS SHADOW, ONE COMMAND -- plan_entity.md §3.1 and §3.10,
// the policy-player lane.
//
// Step 3f gave a standalone player a body in the solver and left the STATE
// MACHINE that drives it in C++: which hull is in the solver, how far ahead the
// controller is aimed, whether the shove is softened, when a step is a step and
// when it is a teleport, and when the shadow has fallen so far behind that it
// must be snapped back. Eight numbers and four predicates, none of which the
// engine asks for and all of which decide what the game does. §6 has no row for
// any of it.
//
// It is TWO SLOTS AND NOT ONE, and the split is where the boundary actually is:
//
//   PlayerShadowProbe   native READS -- the engine's clock, the player's origin
//                       and flags, the solver's contact answer, where the
//                       shadow currently is. None of it is a decision.
//   PlayerShadowApply   native WRITES -- SetPosition, SetVelocity,
//                       EnableCollisions, SetObject, StepUp, Jump, Update. None
//                       of it is a decision either.
//
// and everything between the two is managed (PlayerShadow.Plan).
//
// BOTH ARE OUTBOUND, WHICH IS THE WHOLE POINT OF THE SHAPE. The crossing
// counters count native->managed thunk entries (Crossings.cs); these are called
// FROM the managed run loop, at the same point in the sequence stock runs
// PostThinkVPhysics -- the end of one command -- so the move costs ZERO counted
// crossings. It is not free: it is two more managed->native calls per command
// per player, on top of the three PlayerRunMove already makes. That cost is
// recorded rather than hidden.
//
// SERVER ONLY. The shadow is a body in the SERVER's physics environment; the
// client neither simulates it nor is asked about it, and both slots are NULL
// there.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// WHERE THE THIRD-PERSON CAMERA SITS -- plan_entity.md §3.10, the policy-player
// lane.
//
// §3.10: "CInput's remaining entity references ... are the local player's eye
// and the third-person camera, answered from the block and from
// thirdperson_camera.h's policy moved to managed." This is that move.
//
// What was in thirdperson_camera.h: the orbit added to the view angles and its
// +/-89 degree pitch clamp; the displacement's sign convention (back is
// NEGATIVE forward); how much of a wanted displacement survives what a hull
// sweep found; the 30-unit floor under which a pulled-in camera gives up and
// goes back to the eye rather than drawing the inside of a skull; and whether
// the placement is one the player's own model should be drawn for. Every one of
// those is a decision about what the frame shows.
//
// TWO PHASES THROUGH ONE SLOT, because the trace sits between them: managed
// answers where the camera WANTS to be, native sweeps a hull towards it, and
// managed answers how much of that wish survives. Two slots would have been two
// places for the ABI to drift for one rule; SourceSharpMoveEffect makes the
// same argument.
//
// CLIENT ONLY, and entered only while `thirdperson` is on -- which is a
// developer command, not a mode this mod ships in. So the steady-state crossing
// cost is ZERO and the third-person cost is two per rendered frame.
//-----------------------------------------------------------------------------
#define SOURCESHARP_THIRDPERSON_PHASE_PLACE   0
#define SOURCESHARP_THIRDPERSON_PHASE_RESOLVE 1

typedef struct
{
	uint32_t struct_size;
	int32_t  nPhase;

	// --- PLACE: in ---
	// Where the player is looking, and the orbit the cam_ideal* convars ask for.
	float    vecViewAngles[3];
	float    flIdealPitch;
	float    flIdealYaw;
	float    flIdealDist;
	float    flIdealDistRight;
	float    flIdealDistUp;

	// --- PLACE: out ---
	// The camera's own angles, and the displacement from the eye it wants.
	float    vecCameraAngles[3];
	float    vecWish[3];
	float    flWishLength;

	// --- RESOLVE: in ---
	// What the hull sweep reached, 0..1, and the wish length PLACE answered.
	float    flTraceFraction;

	// --- RESOLVE: out ---
	// How much of the wish to keep, and whether a camera placed with that much
	// of it actually sees the player -- which is what decides whether their own
	// model draws.
	float    flSurvivingFraction;
	int32_t  bSeesOwner;

	// --- PLACE: in, appended (lane M1-B) ---
	// cam_collision's value: 1 on, 0 off, -1 when the convar is not there.
	int32_t  nCollision;

	// --- PLACE: out, appended ---
	// The sweep the camera wants towards its wish: a cube of this half extent
	// under nTraceMask (CONTENTS_* bits), or no sweep at all at 0, when the
	// wish is kept whole. Stock's CAM_HULL_OFFSET and mask are managed's.
	float    flHullHalfExtent;
	uint32_t nTraceMask;
} SourceSharpThirdPersonCamera;

// SourceSharpPlayerShadowState and SourceSharpPlayerShadowPlan stood here: the
// probe's answer and the apply's instruction while native made the shadow's
// vphysics calls. Both are managed-only now (PlayerShadowState /
// PlayerShadowPlan, SourceSharp.Abi), plan_physics_bones_native_zero 2.5.

//-----------------------------------------------------------------------------
// WHAT NATIVE KNOWS OF A PLAYER AND THE BODY IT HOLDS FOR THEM
// (plan_physics_bones_native_zero 2.5). The player's shadow is managed's
// (PlayerShadowBodies): it builds the body through vphysics' own tables, reads
// vphysics itself and decides everything. Native holds the three pointers --
// the player's destructor is the body's one teardown owner -- and answers what
// only the player's own state can: where the move left them and what it wrote.
// Stock's m_oldOrigin and m_touchedPhysObject are managed's (PlayerMoveMemory,
// 2026-10-12): the facts carry the move's touch list, not a verdict.
// PlayerShadowFacts in SourceSharp.Abi mirrors it; the asserts beside
// SourceSharp_PlayerShadowFacts pin the offsets.
//-----------------------------------------------------------------------------
#define SOURCESHARP_PLAYER_TOUCH_MAX 16

typedef struct
{
	uint32_t struct_size;
	int32_t  bHasShadow;      // a body (both hulls and the controller) is held
	int32_t  bBodyCurrent;    // ...and the environment that built it is physenv
	int32_t  nState;          // which hull is in the solver (VPHYS_*), -1 none yet

	void    *pStand;
	void    *pCrouch;
	void    *pController;

	float    vecOrigin[3];
	float    vecVelocity[3];
	float    vecOutWishVel[3];   // CMoveData::m_outWishVel
	float    flOutStepHeight;    // CMoveData::m_outStepHeight
	float    flMaxSpeed;
	int32_t  nFlags;
	int32_t  nMoveType;
	float    flFrameTime;        // RAW gpGlobals->frametime
	float    flTickInterval;

	uint32_t hGround;            // the ground entity, or the invalid handle
	int32_t  nGroundMoveType;    // its move type
	void    *pGroundObject;      // its own physics object, or NULL

	// What the last command's move ran into (the move helper's touch list,
	// one entry per entity, kept from the command until the next one starts):
	// managed decides from it whether that was a moveable physics object
	// (stock's CBasePlayer::Touch -> m_touchedPhysObject). nTouched counts
	// every entity; the first SOURCESHARP_PLAYER_TOUCH_MAX are listed, and bit
	// i of fTouchedGround says entry i was the ground when it was touched.
	int32_t  nTouched;
	uint32_t fTouchedGround;
	uint32_t hTouched[SOURCESHARP_PLAYER_TOUCH_MAX];
} SourceSharpPlayerShadowFacts;

//-----------------------------------------------------------------------------
// ONE SERVER COMMAND, AROUND MANAGED'S MOVE (plan_server_native_v2 T4g). The
// managed command loop owns the player's movement state and makes every
// decision; native answers what only the engine's side holds and carries out
// what only it can. PlayerCommandFacts and PlayerCommandResult in
// SourceSharp.Abi mirror these; the asserts beside the slot bodies pin them.
//-----------------------------------------------------------------------------

// IShimMovementServer::PlayerCommandBegin: in, the ground the managed state
// stands on; out, where the player is and what the engine-read CPlayerState
// says. (The command's clock is managed's to set: it writes CGlobalVars.)
typedef struct
{
	uint32_t struct_size;
	uint32_t hGround;              // in: the ground handle, or the invalid one

	float    vecOrigin[3];         // out: GetAbsOrigin()
	float    angVAngle[3];         // out: pl.v_angle
	float    angAngleChange[3];    // out: pl.anglechange
	int32_t  nFixAngle;            // out: pl.fixangle
	int32_t  bDeadFlag;            // out: pl.deadflag
	int32_t  nHealth;              // out: GetHealth()
	float    flModelScale;         // out: the shell's model scale
	int32_t  nEntIndex;            // out
	int32_t  bGroundResolves;      // out: hGround names a live entity
	float    vecGroundVelocity[3]; // out: MovementGroundVelocity( that entity )
} SourceSharpPlayerCommandFacts;

// IShimMovementServer::PlayerCommandFinish: the move's result, carried out.
// The origin and yaw go to the shell (SetAbsOrigin, SetAbsAngles( 0, yaw, 0 ))
// and the triggers it stands in are touched; the rest are the words native
// code still reads off the player between commands (the readers are named in
// docs/todo.md's T4g entry), mirrored until each moves.
typedef struct
{
	uint32_t struct_size;
	int32_t  bRan;                 // the loop ran (the movemath counter)
	float    vecOrigin[3];
	float    flYaw;
	int32_t  nFlags;
	int32_t  nMoveType;
	float    vecViewOffset[3];
	float    vecVelocity[3];
	float    vecBaseVelocity[3];
	uint32_t hGround;
	float    flFallVelocity;
} SourceSharpPlayerCommandResult;

// SourceSharpEngineApi, the struct of engine slots, is gone (plan_client_vtables
// CC-last): every slot is a method of a shim area, the server's (ishim_areas.h)
// or the client's (ishim_areas_client.h, and the shared bases in ishim_areas.h),
// acquired through each side's IShimRoot.

//-----------------------------------------------------------------------------
// ONE MAP LOAD, flattened. Mirrors MapLoadInfo in
// sourcesharp/game/shared/sourcesharp_gamesystem.h, which is what the shell
// game DLL builds from IServerGameDLL::LevelInit's five arguments.
//
// BY POINTER, not by value. Four pointers is small enough to pass either way,
// but a struct passed by value across this boundary has an ABI of its own
// (register vs. memory class) that neither side states, and every other
// aggregate on this table -- SourceSharpCollisionRules, SourceSharpUserCmd --
// already crosses as a pointer.
//
// The strings are the ENGINE's, valid only for the duration of the call.
// pMapEntities is the whole entity lump; managed code parses it inside the
// call and does not keep it (plan_gamedll.md Part 4 makes the managed
// counterpart a ref struct so that is a compile error rather than a review
// note).
//
// pOldLevel and pLandmarkName are NULL on a load that is not a transition.
//-----------------------------------------------------------------------------
typedef struct
{
	const char *pMapName;
	const char *pMapEntities;
	const char *pOldLevel;
	const char *pLandmarkName;
} SourceSharpMapLoad;

//-----------------------------------------------------------------------------
// THE CLIENT'S SIMPLE-PARTICLE SIMS, MANAGED (plan_client_novalve C2, lane
// c2-sims). A sim stock wrote as a CParticleMgr effect (the steam jet first)
// is C#'s, and since C7 particles phase 2.3 so is the binding the manager held
// (ClientParticleManager); shell_managed_particles.cpp's executor is the
// renderable that draws what managed lists.
//
// SourceSharpParticleQuad_t is one particle's quad: what particle_util.h's
// builders take (world position, colour, alpha, size, roll, the heatwave's
// perturbed normal) and which builder. Native transforms the position into
// view space and builds; it decides nothing.
//
// ParticleAbiParityTests holds the structs here against src/client/interop's;
// field names are unprefixed for its sake, as SourceSharpEffectData_t's are.
//-----------------------------------------------------------------------------

typedef struct
{
	int32_t Id;
	int32_t Kind;             // 0 RenderParticle_ColorSizeAngle, 1 the heatwave's ColorSizePerturbNormal, 2 RenderParticle_Color255SizeNormalAngle (lit by managed: Color 0..255, normal zero), 3 RenderParticle_ColorSize, 4 a tracer quad already in view space (Position, Normal, Corner2, Corner3; nothing drawn when Size is 0), 5 a 3D particle's quad in world space (Position the corner, Normal the face, Corner2/Corner3 the width and height edges)
	float   PositionXYZ[3];
	float   ColorXYZ[3];
	float   Alpha;
	float   Size;
	float   Roll;
	float   NormalXYZ[3];
	float   FadeNear;         // GetAlphaDistanceFade( tPos, FadeNear, FadeFar ) when FadeFar > 0
	float   FadeFar;
	int32_t FixedSort;        // 1: GetNext( SortKey ); 2: by view depth truncated to an integer (CSimpleEmitter's); 0: by view depth
	float   SortKey;
	float   Corner2XYZ[3];    // Kind 4: the tracer's third and fourth view-space corners; kind 5: the width and height edges
	float   Corner3XYZ[3];
} SourceSharpParticleQuad_t;

// C7 particles phase 2: the old-style particle manager is managed's
// (SourceSharp.Client.Effects.ClientParticleManager). Each crossing carries ONE
// length-prefixed buffer per direction (ss_bitblock.h: an int32 bit count, then
// the bytes), and a list's count is what the prefix leaves room for.
//
// IManagedParticlesClient::Frame's in: this header, then one
// SourceSharpParticleFrameIn_t per live executor.
typedef struct
{
	float   Dt;               // gpGlobals->frametime, unclamped
	float   CurTime;          // gpGlobals->curtime
	float   FrameTime;        // gpGlobals->frametime (Helper_GetFrameTime)
	float   AabbBuffer;       // cl_particleeffect_aabb_buffer (DetectChanges)
	float   ViewOriginXYZ[3]; // MainViewOrigin()
} SourceSharpParticleFrameHeader_t;

// An executor's local space as FollowSpace read it (CLocalSpaceEmitter's
// attachment): the 3x4 matrix row-major, its origin, and the box it hands
// SetBBox( origin -/+ half the owner's diagonal, true ) when the owner has a
// collideable.
typedef struct
{
	int32_t Followed;         // 0: no follow ran (nothing below is set)
	float   Matrix[12];
	float   OriginXYZ[3];
	int32_t HasBox;
	float   MinsXYZ[3];
	float   MaxsXYZ[3];
} SourceSharpParticleFollow_t;

typedef struct
{
	uint32_t Owner;
	int32_t  Channel;
	float    OriginXYZ[3];    // GetAbsOrigin before this frame's follow (the emit's)
	float    AnglesPYR[3];
	int32_t  MaterialFlags;   // 1: the page material needs the power-of-two frame buffer; 2: the full one
	int32_t  Followed;        // the follow below ran this frame
	float    Matrix[12];
	float    FollowOriginXYZ[3];
	int32_t  HasBox;
	float    MinsXYZ[3];
	float    MaxsXYZ[3];
} SourceSharpParticleFrameIn_t;

// IManagedParticlesClient::Frame's out: one per binding managed updated.
typedef struct
{
	uint32_t Owner;
	int32_t  Channel;
	float    MinXYZ[3];       // the world box GetRenderBounds answers from
	float    MaxXYZ[3];
	int32_t  Flags;           // CParticleEffectBinding's FLAGS_ (ShouldDraw reads FLAGS_DRAW_THRU_LEAF_SYSTEM)
	int32_t  Active;
	int32_t  Changed;         // DetectChanges: tell the leaf system (RenderableChanged)
} SourceSharpParticleCommit_t;

// IManagedParticlesClient::DrawBegin's in: what native read for DrawModel.
typedef struct
{
	uint32_t Owner;
	int32_t  Channel;
	int32_t  Flags;           // DrawModel's studio flags
	int32_t  ClientModeDraws; // g_pClientMode->ShouldDrawParticles()
	int32_t  DrawParticles;   // r_drawparticles
	int32_t  Wireframe;       // ShouldDrawInWireFrameMode()
	int32_t  ShowBBox;        // cl_particle_show_bbox
	int32_t  ShowBBoxCost;    // cl_particle_show_bbox_cost
	int32_t  ViewKey;         // ParticlesViewKey: the view being drawn
	float    FrameTime;       // gpGlobals->frametime
} SourceSharpParticleDrawIn_t;

// DrawBegin's out: this header, then one quad per particle in the binding's
// list order (Kind -1: a particle the sim drew nothing for).
typedef struct
{
	int32_t Result;           // 0 return 0; 1 drawn, nothing to draw (return 1); 2 draw the quads
	float   FrameTime;        // ParticleDraw::Init's frame time
	int32_t RefractUpdate;    // UpdateRefractTexture() before the material
	int32_t ScreenUpdate;     // UpdateScreenEffectTexture() before the material
	int32_t Wireframe;        // a second pass with debug/debugparticlewireframe
	int32_t BBox;             // draw the debug box (cl_particle_show_bbox)
	int32_t BBoxR;
	int32_t BBoxG;
	int32_t BBoxB;
	int32_t Active;
	float   MinXYZ[3];
	float   MaxXYZ[3];
} SourceSharpParticleDrawOut_t;

// DrawEnd's in: the sort key the executor handed each quad, in list order.

//-----------------------------------------------------------------------------
// One CNewParticleEffect the client started, as /client/{slot}/particles reads
// it (plan_client_novalve §4.4, lane c5-particles): IShimParticlesClient::
// ParticleSystemsRead fills one by index and writes the system's name beside
// it. Kind 1 is a managed entity's (Owner its handle, FollowMask the control
// points kept at it), 2 an impact's one-shot (Owner 0xFFFFFFFF). Stopped: the
// owner let it go and it is burning out. Age is curtime since the start;
// Particles the active count summed over the children. ParticleAbiParityTests
// holds it against ParticleSystemInfo.
//-----------------------------------------------------------------------------
typedef struct
{
	int32_t  Kind;
	uint32_t Owner;
	int32_t  FollowMask;
	int32_t  Stopped;
	float    Age;
	int32_t  Particles;
	float    ControlPoint0XYZ[3];
	float    ControlPoint1XYZ[3];
	float    SortOriginXYZ[3];  // what the leaf system sorts and culls it by
} SourceSharpParticleSystemInfo_t;

//-----------------------------------------------------------------------------
// C7 particles phase 3: the new-style effects' manager is managed's
// (SourceSharp.Client.Effects.NewParticleManager over NewParticleEffect); the
// particle library (libss_particles.so) simulates and renders, and native's
// shell (shell_new_particles_client.cpp) is the renderable that carries the
// decisions out. One length-prefixed buffer per direction, as phase 2's.
//
// IManagedParticlesClient::NewEffectsFrame's in: this header, then one
// SourceSharpNewParticleIn_t per live effect in the manager's order (newest
// first: stock's AddToHead). During the call managed steps the listed
// collections through the library (ISSParticleLib::CollectionSimulateBatch).
// NewParticleAbiParityTests holds every struct here against its managed twin.
//-----------------------------------------------------------------------------
typedef struct
{
	float   Dt;               // the frame's dt (UpdateAllEffects', after its 0.1 s clamp)
	float   Timescale;        // r_particle_timescale
	float   CurTime;          // gpGlobals->curtime: the library's last simulation time
	int32_t FrameCount;       // gpGlobals->framecount (the retirement check)
	float   AabbBuffer;       // cl_particleeffect_aabb_buffer (DetectChanges)
	float   RetireCost;       // cl_particle_retire_cost
	int32_t SpikeThresholdMs; // r_particle_sim_spike_threshold_ms (EndSimulateParticles' profile commit)
	int32_t ViewX;            // view->GetPlayerViewSetup(): the retirement pass's view
	int32_t ViewY;
	int32_t ViewWidth;
	int32_t ViewHeight;
	float   ViewFov;
	float   ViewOriginXYZ[3];
	float   WorldToScreen[16]; // render->GetMatricesForView's, row-major
} SourceSharpNewParticleFrameHeader_t;

// A live effect's Flags.
enum
{
	SOURCESHARP_NEW_PARTICLE_RELEASED        = 1 << 0,  // its reference count reached zero since the last frame
	SOURCESHARP_NEW_PARTICLE_ALLOCATED       = 1 << 1,  // Create made it (m_bAllocated)
	SOURCESHARP_NEW_PARTICLE_RENDER_HANDLE   = 1 << 2,  // it is in the leaf system
	SOURCESHARP_NEW_PARTICLE_VIEW_MODEL      = 1 << 3,  // its definition is a view-model effect
	SOURCESHARP_NEW_PARTICLE_HAS_REPLACEMENT = 1 << 4,  // its definition names a cull replacement
	SOURCESHARP_NEW_PARTICLE_ADDED           = 1 << 5,  // started since the last frame
};

typedef struct
{
	uint32_t Id;              // the shell's, stable for the effect's life
	int32_t  Flags;           // SOURCESHARP_NEW_PARTICLE_*
	uint64_t Collection;      // the library's CParticleCollection *, for the batch
	uint64_t Definition;      // its CParticleSystemDefinition *, an identity
	float    NextSleepTime;   // m_flNextSleepTime
	int32_t  Active;          // m_nActiveParticles
	int32_t  HasMoved;        // HasMoved()
	int32_t  Finished;        // IsFinished() (Release's test)
	float    SortOriginXYZ[3];
	float    MinXYZ[3];       // the collection's bounds
	float    MaxXYZ[3];
	float    CullRadius;      // the definition's (EarlyRetireParticleSystems)
	float    CullFillCost;
	float    CullPointXYZ[3]; // GetControlPointAtCurrentTime( the definition's cull control point )
} SourceSharpNewParticleIn_t;

// NewEffectsFrame's out: one per effect managed asks native to act on.
enum
{
	SOURCESHARP_NEW_PARTICLE_CHANGED    = 1 << 0,  // DetectChanges: RenderableChanged
	SOURCESHARP_NEW_PARTICLE_REMOVE     = 1 << 1,  // RemoveEffect: out of the leaf system, deleted if allocated
	SOURCESHARP_NEW_PARTICLE_VIEW_GROUP = 1 << 2,  // AddEffect's RENDER_GROUP_VIEW_MODEL_TRANSLUCENT
	SOURCESHARP_NEW_PARTICLE_REPLACE    = 1 << 3,  // ReplaceWith( the definition's cull replacement )
};

typedef struct
{
	uint32_t Id;
	int32_t  Actions;         // SOURCESHARP_NEW_PARTICLE_CHANGED and the rest
} SourceSharpNewParticleCommit_t;

// IManagedParticlesClient::NewEffectDraw's in: what native read for one
// CNewParticleEffect::DrawModel. Entities are identity tokens (their C_StandaloneEntity
// pointers), 0 for none: the skip-render tests compare them and nothing else.
typedef struct
{
	uint32_t Id;
	int32_t  Flags;           // DrawModel's studio flags
	int32_t  DrawParticles;   // r_drawparticles
	int32_t  ClientModeDraws; // g_pClientMode->ShouldDrawParticles()
	int32_t  RenderSystems;   // ParticleMgr()->ShouldRenderParticleSystems()
	int32_t  ShowBBox;        // cl_particle_show_bbox
	int32_t  ShowBBoxCost;    // cl_particle_show_bbox_cost
	int32_t  Active;          // m_nActiveParticles (the debug box's cost)
	float    CameraXYZ[3];    // the render context's world-space camera
	float    MinXYZ[3];       // the collection's bounds
	float    MaxXYZ[3];
	float    MaxDrawDistance; // the definition's
	float    NoDrawTimeToGoToSleep;
	float    LastSimulationTime; // the library manager's
	float    NextSleepTime;   // m_flNextSleepTime
	int32_t  SkipRenderControlPoint; // the definition's (-1: none)
	int32_t  HighestControlPoint;    // m_nHighestCP
	int32_t  ThirdPerson;     // input->CAM_IsThirdPerson()
	int32_t  ObserverMode;    // the local player's
	int32_t  Pad;
	uint64_t CameraObject;    // the entity at render->GetViewEntity()
	uint64_t SkipObject;      // the skip-render control point's entity
	uint64_t LocalPlayer;
	uint64_t ObserverTarget;  // the local player's
} SourceSharpNewParticleDrawIn_t;

typedef struct
{
	int32_t Action;           // 0 return 0; 1 render now (two-pass when the system is); 2 the library's render cache
	int32_t SetSleep;         // write NextSleepTime (the distance cull keeps the system awake)
	float   NextSleepTime;
	int32_t BBox;             // draw the debug box (cl_particle_show_bbox / _cost)
	int32_t BBoxR;
	int32_t BBoxG;
	int32_t BBoxB;
	int32_t Stats;            // the frame's stats count this draw (cl_particle_stats)
} SourceSharpNewParticleDrawOut_t;

//-----------------------------------------------------------------------------
// point_worldtext's draw (plan_client_novalve C2, lane c2-sims): its layout is
// C#'s (WorldTextLayout); shell_worldtext_render.cpp is the leaf renderer that
// builds what managed lays out.
//
// SourceSharpWorldTextView_t is the draw's in: where the text stands, the
// view's basis (a view-facing text is laid out on it) and the clock (the
// rainbow). SourceSharpWorldTextVertex_t is one built vertex: position,
// texture coordinate and colour bytes (r in the low byte, then g, b, a).
//
// WorldTextAbiParityTests holds both against WorldTextView / WorldTextVertexRaw.
//-----------------------------------------------------------------------------
typedef struct
{
	float   OriginXYZ[3];
	float   AnglesPYR[3];
	float   ForwardXYZ[3];
	float   RightXYZ[3];
	float   UpXYZ[3];
	float   CurTime;
} SourceSharpWorldTextView_t;

typedef struct
{
	float    PositionXYZ[3];
	float    S;
	float    T;
	uint32_t Color;
} SourceSharpWorldTextVertex_t;

//-----------------------------------------------------------------------------
// THE MATERIAL PROXIES (plan_client_novalve §2 cluster 1, lane c5-proxies).
// shell_materialproxies.cpp registers ONE trampoline IMaterialProxy class
// under every proxy name; each Init and OnBind asks managed
// (IManagedMaterialProxyClient), which decides, and native carries out what
// managed answers.
//
// SourceSharpProxyKey_t is one key of the proxy's KeyValues block, RAW:
// KeyValues::GetString rewrites a FLOAT or INT key as a STRING in place, so
// native reads each key only through the accessor that does not convert it
// (String for a STRING key, Float for a FLOAT one, Int for an INT one), and
// for a STRING key adds what sscanf( "%f" ), atof and atoi read from it.
// SourceSharpProxyVar_t is one found var's state before a bind: its type,
// vector size and the three getters (GetIntValue, GetFloatValue,
// GetVecValue( v, 4 )). SourceSharpProxyBindIn_t is the bind's clock, frame and
// raw argument. SourceSharpProxyOp_t is one IMaterialVar setter managed asks
// for, in order (Kind: 1 SetFloatValue, 2 SetIntValue, 3 SetVecValue( v, Arg ),
// 4 SetVecValue( x, y, z ), 5 SetVecComponentValue( v, Arg ), 6 SetMatrixValue,
// 7 SetTextureValue of texture slot Arg; 8, the tools' material record, is
// deleted with SFM, user ruling 2026-10-19, and never reused).
// SourceSharpProxyEntity_t is the bind argument resolved as stock's entity
// proxies resolve it.
//
// MaterialProxyAbiParityTests holds all five against the managed structs.
//-----------------------------------------------------------------------------
typedef struct
{
	const char *Name;
	const char *String;
	int32_t     Type;
	float       Float;
	int32_t     Int;
	int32_t     ScanCount;
	float       ScanValue;
	float       Atof;
	int32_t     Atoi;
	int32_t     Reserved;
} SourceSharpProxyKey_t;

typedef struct
{
	int32_t Type;
	int32_t VecSize;
	int32_t Int;
	float   Float;
	float   V0;
	float   V1;
	float   V2;
	float   V3;
} SourceSharpProxyVar_t;

typedef struct
{
	float   CurTime;          // gpGlobals->curtime
	float   FrameTime;        // gpGlobals->frametime
	int32_t MaxClients;       // gpGlobals->maxClients
	int32_t FrameCount;       // gpGlobals->framecount
	int32_t VarCount;
	int32_t Reserved;
	void   *Arg;              // the bind argument, opaque to managed
} SourceSharpProxyBindIn_t;

typedef struct
{
	int32_t Kind;
	int32_t Slot;
	int32_t Arg;
	int32_t Reserved;
	float   Values[16];
} SourceSharpProxyOp_t;

typedef struct
{
	int32_t HasUnknown;       // the renderable answered an IClientUnknown
	int32_t EntIndex;         // its IClientEntity's index, or -1
	float   OriginXYZ[3];     // that entity's GetAbsOrigin()
	int32_t RenderAlpha;      // the engine block's RenderColorA, or -1
	int32_t IsNpc;            // FL_NPC from the engine block's flags: 1, 0, or -1
	int32_t Reserved;
} SourceSharpProxyEntity_t;

//-----------------------------------------------------------------------------
// THE RENDER PLAN (plan_client_novalve.md §3.3, C7). Once per View_Render the
// executor (shell_view_executor_client.cpp) hands managed the frame
// (IManagedRenderClient::RenderFrame) and managed answers which path draws it
// and whether this frame is recorded (shell_render_trace_client.cpp). A
// recorded frame's text goes back through RenderTraceDone.
//
// SourceSharpRenderFrameIn_t: the rect the engine handed View_Render, the
// frame's clock, and the last frame's path and how long its CViewRender::Render
// took (wall ms, Plat_FloatTime). SourceSharpRenderFrameOut_t: Path (0 stock viewrender, 1 the
// plan's executor), Trace (1: record this frame), TraceTag (handed back with
// the text).
//-----------------------------------------------------------------------------
// (They follow SourceSharpViewSetup_t, which both carry.)

// A CViewSetup, field for field (view_shared.h), as the plan carries it.
typedef struct
{
	int32_t X;
	int32_t Y;
	int32_t Width;
	int32_t Height;
	int32_t UnscaledX;
	int32_t UnscaledY;
	int32_t UnscaledWidth;
	int32_t UnscaledHeight;
	int32_t StereoEye;
	int32_t Ortho;
	float   OrthoLeft;
	float   OrthoTop;
	float   OrthoRight;
	float   OrthoBottom;
	float   Fov;
	float   FovViewmodel;
	float   OriginXYZ[3];
	float   AnglesXYZ[3];
	float   ZNear;
	float   ZFar;
	float   ZNearViewmodel;
	float   ZFarViewmodel;
	int32_t RenderToSubrect;
	float   AspectRatio;
	int32_t OffCenter;
	float   OffCenterTop;
	float   OffCenterBottom;
	float   OffCenterLeft;
	float   OffCenterRight;
	int32_t DoBloomAndToneMapping;
	int32_t CacheFullSceneState;
	int32_t ViewToProjectionOverride;
	float   ViewToProjection[16];
} SourceSharpViewSetup_t;

typedef struct
{
	int32_t X;
	int32_t Y;
	int32_t Width;
	int32_t Height;
	int32_t FrameCount;
	float   CurTime;
	int32_t LastPath;
	float   LastRenderMs;
	// CViewRender::Render's inputs (view.cpp; C7 step 3 phase 2, appended):
	// the mono eye SetUpViews and the client mode's override left, the
	// engine's aspect and window, the convars, the ATI antialiasing case, the
	// client mode's viewmodel switch, cl_leveloverview, the rectangle after
	// AdjustEngineViewport.
	SourceSharpViewSetup_t Eye;
	float   ScreenAspect;
	int32_t Windowed;
	int32_t MaxClients;
	int32_t RestrictAspectFov;
	float   ViewportScale;
	int32_t GlClear;
	int32_t GlClearRandomColor;
	int32_t ClearColorForAntialiasing;
	int32_t ClientModeDrawViewModel;
	float   LevelOverview;
	int32_t ViewportX;
	int32_t ViewportY;
	int32_t ViewportWidth;
	int32_t ViewportHeight;
} SourceSharpRenderFrameIn_t;

typedef struct
{
	int32_t Path;
	int32_t Trace;
	int32_t TraceTag;
	int32_t Reserved;
	// CViewRender::Render's decisions (appended): the eye RenderView draws, its
	// clear and draw flags, gl_clear_randomcolor's colour, the level overview.
	SourceSharpViewSetup_t Eye;
	int32_t ClearFlags;
	int32_t DrawFlags;
	int32_t RandomClear;
	int32_t ClearR;
	int32_t ClearG;
	int32_t ClearB;
	int32_t Overview;
} SourceSharpRenderFrameOut_t;

// SetUpViews (view.cpp), asked before the client mode's override: r_farz,
// r_mapextents, the viewer's fog far Z (0 with none), default_fov and the
// client mode's GetViewModelFOV in; the planes and FOV out. After the override
// native carries out fovViewmodel = fov + ViewModelFovOffset: managed decided
// the offset (stock's GetViewModelFOV() - ( default_fov - fov ), rearranged),
// the override's fov is the one operand only native has, and a second crossing
// after the override would ask managed for one addition.
typedef struct
{
	float FarZ;
	float MapExtents;
	float FogFarZ;
	float DefaultFov;
	float ViewModelFov;
} SourceSharpViewSetUpIn_t;

typedef struct
{
	float ZFar;
	float ZFarViewmodel;
	float ZNear;
	float ZNearViewmodel;
	float Fov;
	float ViewModelFovOffset;
} SourceSharpViewSetUpOut_t;

// A fogparams_t (playernet_vars.h), as the plan reads it. Colours are 0..255.
typedef struct
{
	int32_t Present;
	float   DirPrimaryXYZ[3];
	int32_t ColorPrimary[3];
	int32_t ColorSecondary[3];
	int32_t ColorPrimaryLerpTo[3];
	int32_t ColorSecondaryLerpTo[3];
	float   Start;
	float   End;
	float   MaxDensity;
	float   StartLerpTo;
	float   EndLerpTo;
	float   LerpTime;
	float   Duration;
	int32_t Enable;
	int32_t Blend;
	int32_t Radial;
} SourceSharpFogParams_t;

// SourceSharpRenderInputs_t: everything RenderView's decisions read, gathered
// by the executor at RenderView's entry -- the view and RenderView's two
// arguments, the hardware answers, the convars (Cvars[], indexed by the
// RENDER_CVAR_ list in shell_view_executor_client.cpp and RenderCvar in C#),
// the viewer's fog and 3D sky, the fog volume at the eye with the water
// material's switches, and the post-process inputs. Nothing here is decided.
typedef struct
{
	SourceSharpViewSetup_t View;
	int32_t ClearFlags;
	int32_t WhatToDraw;
	float   CurTime;
	float   FrameTime;
	int32_t FrameCount;
	int32_t MaxClients;
	int32_t HdrType;
	int32_t DxLevel;
	int32_t UseFastClipping;
	int32_t IsHammerRunning;
	float   ScreenAspect;
	int32_t ClientModeDrawFog;
	int32_t FreezeActive;
	int32_t ForceNoVis;
	int32_t PointContentsSolid;
	int32_t SkyboxVisible;
	int32_t SkyPresent;
	int32_t SkyScale;
	float   SkyOriginXYZ[3];
	int32_t SkyArea;
	SourceSharpFogParams_t SkyFog;
	SourceSharpFogParams_t Fog;
	int32_t FogVolume;
	int32_t FogVolumeLeaf;
	int32_t EyeInFogVolume;
	float   DistanceToWater;
	float   WaterHeight;
	int32_t WaterMaterial;
	int32_t WaterTranslucent;
	int32_t WaterForceCheapDefined;
	int32_t WaterForceCheap;
	int32_t WaterForceExpensiveDefined;
	int32_t WaterForceExpensive;
	int32_t WaterReflectTexture;
	int32_t WaterRefractTexture;
	int32_t WaterReflectEntities;
	int32_t WaterUnderwaterOverlay;
	int32_t ViewPlaneIntersectsWater;
	float   CheapWaterStart;
	float   CheapWaterEnd;
	int32_t GlassInView;
	float   GlassPlane[4];
	int32_t ReflectionWidth;
	int32_t ReflectionHeight;
	int32_t RefractionWidth;
	int32_t RefractionHeight;
	int32_t ThirdPersonSeesOwner;
	int32_t ViewEntity;
	int32_t Retired0;   // always 0: was ToolsEnabled(); SFM is dead (2026-10-19). Holds the layout.
	int32_t FadeColor[4];
	int32_t FadeBlend;
	int32_t ScreenOverlay;
	int32_t ScreenOverlayFullFrame;
	int32_t ScreenOverlayPowerOfTwo;
	int32_t FlashlightOn;
	float   Cvars[64];
	// Appended (C7 step 3 phase 1): RenderInputs only grows at its end, so the
	// frozen recordings still read (their tail reads zero).
	int32_t ClientModeDrawDetail;
	// Appended (C7 step 3 phase 2): the freeze frame's capture is due
	// (m_rbTakeFreezeFrame), the flash's end time (g_flFreezeFlash) and
	// engine->IsTakingScreenshot().
	int32_t FreezeTake;
	float   FreezeFlash;
	int32_t TakingScreenshot;
} SourceSharpRenderInputs_t;

// SourceSharpRenderOp_t: one step of the plan, in order. Kind is RENDER_OP_
// (shell_view_executor_client.cpp) / RenderOpKind (C#); what I[] and F[] mean
// is the kind's, and View is the setup the kind pushes or sets up from.
typedef struct
{
	int32_t Kind;
	int32_t I[7];
	float   F[8];
	SourceSharpViewSetup_t View;
} SourceSharpRenderOp_t;

// SourceSharpViewScene_t: what one world view's lists hold, for the decisions
// stock's CRendering3dView made over them (plan_client_novalve C7 step 2):
// which renderables draw in which order with which flags, and which leaves'
// translucent surfaces and detail objects draw between them. Native fills it
// after the view's lists are built; managed answers with the view's draw ops.
// Entries are in render-group order (group 0's first), so an entry's index is
// its group's start (the earlier GroupCounts summed) plus its place in its group.
// Leaves are the world list info's, in its order. TranslucentFlags are the two
// engine draw-flag values managed asked LeafContainsTranslucentSurfaces with
// (the BuildRenderables op's), answered per leaf in Leaf_t::Translucent.
typedef struct
{
	int32_t LeafCount;
	int32_t EntryCount;
	int32_t GroupCounts[14];
	int32_t TranslucentFlags[2];
	int32_t HasRenderables;
	int32_t OpIndex;
} SourceSharpViewScene_t;

typedef struct
{
	int32_t Leaf;
	int32_t ActualLeaf;
	int32_t Detail;
	int32_t Translucent[2];
} SourceSharpViewSceneLeaf_t;

// Flags: 1 a renderable, 2 two-pass, 4 ignores the z buffer, 8 uses the
// power-of-two frame buffer, 16 the full frame buffer, 32 a brush model,
// 64 the studio-stats entity. FxBlend is GetFxBlend (0..255); Color is
// GetColorModulation.
typedef struct
{
	int32_t Group;
	int32_t Flags;
	int32_t Leaf;
	int32_t EntIndex;
	int32_t FxBlend;
	float   Color[3];
} SourceSharpViewSceneEntry_t;

// C7 post-processing: what viewpostprocess.cpp's decisions read where the
// plan's post op runs, gathered by the executor for IManagedRenderClient::
// RenderPostProgram (managed PostProcessInputs, field for field). Kind 0 is
// DoEnginePostProcessing, 1 DoImageSpaceMotionBlur. Readback holds the
// histogram queries the last program asked to read (-1 elsewhere); Cvars is
// indexed by managed's PostCvar (the executor's kPostCvars, in order).
typedef struct
{
	int32_t Kind;
	int32_t X;
	int32_t Y;
	int32_t Width;
	int32_t Height;
	int32_t FlashlightOn;
	int32_t PostVGui;
	int32_t HdrType;
	int32_t EngineDxLevel;
	int32_t HardwareDxLevel;
	int32_t MapHasHdrLighting;
	int32_t CanDoSrgbReadFromRts;
	int32_t FakeSrgbWrite;
	int32_t SupportsPixelShaders20;
	int32_t ColorCorrectionActive;
	int32_t FullFrameWidth;
	int32_t FullFrameHeight;
	int32_t SmallFB1Width;
	int32_t SmallFB1Height;
	int32_t ViewportX;
	int32_t ViewportY;
	int32_t ViewportWidth;
	int32_t ViewportHeight;
	int32_t BackBufferWidth;
	int32_t BackBufferHeight;
	int32_t UseCustomAutoExposureMin;
	int32_t UseCustomAutoExposureMax;
	int32_t UseCustomBloomScale;
	float CustomAutoExposureMin;
	float CustomAutoExposureMax;
	float CustomBloomScale;
	float RealTime;
	float CurTime;
	float FrameTime;
	float ToneScaleLinear;
	float ViewOriginXYZ[ 3 ];
	float ViewAnglesXYZ[ 3 ];
	float Fov;
	float AspectRatio;
	int32_t Readback[ 32 ];
	int32_t TextureSize[ 16 ];
	float Cvars[ 64 ];
} SourceSharpPostInputs_t;

// SourceSharpManagedApi, the struct of managed slots native called, is gone
// (plan_client_vtables CE-last): every one is a method of a managed-built
// area -- the server's (imanaged_areas.h, since plan_engineapi_vtables E-last)
// or the client's (imanaged_areas_client.h) -- acquired through each side's
// managed root.



//-----------------------------------------------------------------------------
// Diagnostics.
//
// The host has no Source dependency, so it cannot call Msg() directly. The
// game shim installs a logger before Acquire so startup tracing goes to the
// Source console; without one, output falls back to stdout.
//-----------------------------------------------------------------------------
typedef void (*SourceSharpLogFn)( const char *pMsg );

SOURCESHARP_EXPORT void SourceSharp_SetLogger( SourceSharpLogFn pfnLog );

// 0 = errors only, 1 = startup tracing (default), 2 = verbose.
SOURCESHARP_EXPORT void SourceSharp_SetVerbosity( int nLevel );

//-----------------------------------------------------------------------------
// Runtime lifetime.
//
// CoreCLR can only be initialized ONCE per process, and on a listen server
// client.so and server.so live in the SAME process. Acquire/Release are
// refcounted so both sides can call them independently and exactly one
// runtime is created. This is why the host must be a SHARED library: a static
// lib would give each caller its own refcount.
//-----------------------------------------------------------------------------
SOURCESHARP_EXPORT int  SourceSharp_Acquire( const char *pModBinDir );
SOURCESHARP_EXPORT void SourceSharp_Release( void );
SOURCESHARP_EXPORT int  SourceSharp_RefCount( void );

// plan_engineapi_vtables.md step A3: the same load, handing managed ONE native
// object -- pShimRoot, an IShimRoot (shared/native/ishim_root.h), called through
// pShimRootTable (plan_forwarders_everywhere.md) -- from which it acquires every shim interface.
// *ppManagedRoot is the
// managed-built root (Phase D4), accepted through its own Validate
// (plan_managed_vtable_reload.md L1), so no slot count travels with it. Every
// call either side makes into managed code is an area acquired through that
// root (the server since plan_engineapi_vtables E-last, the client since
// plan_client_vtables CE-last). One export per side (plan_side_entries SE1):
// no side travels as an argument.
SOURCESHARP_EXPORT int  SourceSharp_LoadServer( void *pShimRoot, const void *pShimRootTable,
                                                void **ppManagedRoot );
SOURCESHARP_EXPORT int  SourceSharp_LoadClient( void *pShimRoot, const void *pShimRootTable,
                                                void **ppManagedRoot );

// plan_managed_vtable_reload.md L5: before a reload saves or unloads anything.
// 0 when the build on disk keeps every slot hash of every managed-built
// contract native holds; otherwise nonzero with the first difference in pWhy,
// and the caller keeps the running build.
SOURCESHARP_EXPORT int  SourceSharp_ReloadCheckServer( char *pWhy, int32_t nWhy );
SOURCESHARP_EXPORT int  SourceSharp_ReloadCheckClient( char *pWhy, int32_t nWhy );

SOURCESHARP_EXPORT void SourceSharp_UnloadServer( void );
SOURCESHARP_EXPORT void SourceSharp_UnloadClient( void );

// Last error string for diagnostics. Never NULL.
SOURCESHARP_EXPORT const char *SourceSharp_LastError( void );

#ifdef __cplusplus
}
#endif
