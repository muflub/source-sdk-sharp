//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — game rules ==========================================//
// SS_REASON a  a vtable the engine calls through
#ifdef CLIENT_DLL
#include "cbase.h"
#endif
#include "shell_gamerules.h"
#include "shell_diagnostics.h"
#include "sourcesharp.h"
#ifdef CLIENT_DLL
	#include "ammodef.h"
	#include "takedamageinfo.h"
	#include "gamestringpool.h"
	#include "blast_blocking.h"
	#include "networkstringtable_clientdll.h"
#else
	// The server half is on public/ headers and server.so's own (P5 lane R):
	// the string table container and the engine's globals are declared here.
	#include "networkstringtabledefs.h"
	#include "edict.h"
	extern INetworkStringTableContainer *networkstringtable;
	extern CGlobalVars *gpGlobals;
#endif

#ifndef CLIENT_DLL
uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );
#endif

#ifndef CLIENT_DLL
	#include "shell_server.h"
	#include "shell_standalone_class.h"

	// physics_shell.h's rule table, which this file wrote the group rows into,
	// is gone (plan_physics_bones_native_zero 2.3): the rules vphysics asks are
	// managed's PhysicsSolverRules.
	#include "ss_shareddefs.h"	// ss::kDmg*, the damage mask table
	#include "ss_takedamageinfo.h"	// ss::g_MultiDamage, ss::ApplyMultiDamage
	#include "standalone_relays.h"	// StandaloneEntity_Name

	// shareddefs.h's damage bits by their stock names, for the mask table
	// below (which gamerules_damage_oracle_test reads by those names): this
	// TU no longer includes shareddefs.h, and ss_shareddefs.h holds the
	// values, checked against upstream by ss_shareddefs_oracle_test.
	#define DMG_ACID                   ss::kDmgAcid
	#define DMG_BLAST                  ss::kDmgBlast
	#define DMG_BURN                   ss::kDmgBurn
	#define DMG_CLUB                   ss::kDmgClub
	#define DMG_CRUSH                  ss::kDmgCrush
	#define DMG_DROWN                  ss::kDmgDrown
	#define DMG_DROWNRECOVER           ss::kDmgDrownrecover
	#define DMG_FALL                   ss::kDmgFall
	#define DMG_NERVEGAS               ss::kDmgNervegas
	#define DMG_PARALYZE               ss::kDmgParalyze
	#define DMG_PHYSGUN                ss::kDmgPhysgun
	#define DMG_PLASMA                 ss::kDmgPlasma
	#define DMG_POISON                 ss::kDmgPoison
	#define DMG_PREVENT_PHYSICS_FORCE  ss::kDmgPreventPhysicsForce
	#define DMG_RADIATION              ss::kDmgRadiation
	#define DMG_SHOCK                  ss::kDmgShock
	#define DMG_SLOWBURN               ss::kDmgSlowburn
	#define DMG_SONIC                  ss::kDmgSonic
#include "imanaged_areas.h"   // plan_engineapi_vtables E10: IManagedClients

	// A #define has no linkage, so this one is copied. gamerules.cpp:425.
	#define ROBUST_RADIUS_PROBE_DIST 16.0f
#endif


//-----------------------------------------------------------------------------
// Copied from gamerules.cpp:162, where it is a file-local static and therefore
// cannot be shared by declaring it. These are the player hull and eye offsets
// the VEC_VIEW / VEC_HULL_* macros resolve to.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// WHAT CAME WITH THE RULES WHEN game/shared/gamerules.cpp LEFT THE SERVER BUILD.
//
// Evicting that file left exactly five undefined symbols on the server, and
// these are them. They are not related to each other beyond having shared a
// translation unit: three convars, the rules global, and one free function the
// radius-damage body calls. Each is defined here rather than left behind
// because nothing else on the server defines it.
//
// SERVER ONLY, AND THE GUARD IS STILL LOAD-BEARING -- but for ONE of the
// reasons it used to give, not both. This block used to say the client "still
// has gamerules.cpp", so the guard was what stopped a duplicate definition of
// g_Language, old_radius_damage and g_pGameRules in the client image. THE
// FIRST HALF OF THAT IS NO LONGER TRUE: gamerules.cpp is in neither vpc --
//     grep -n 'gamerules' src/game/client/client_csharp.vpc
// names shell_gamerules.cpp and nothing else. What still makes the guard
// necessary is c_world.cpp, which defines the CLIENT's g_pGameRules.
//
// AND THE CLIENT NO LONGER GETS THE TWO CONVARS FROM ANYWHERE. g_Language and
// old_radiusdamage are declared on the SERVER only now. Both are
// FCVAR_REPLICATED, so the client receives the server's value at connect and
// reads it by name; neither has a client-side reader in this build
// (util_shared_library.cpp's g_Language read is itself inside #ifndef
// CLIENT_DLL). That is a real change from what this comment described and it
// is written down rather than left to be rediscovered.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL

// gamerules.cpp:191. CSourceSharpGameRules' constructor sets it and its
// destructor clears it; see them below.
IGameRules *g_pGameRules = NULL;

// g_Language (gamerules.cpp:62) IS DECLARED IN C# now,
// SourceSharp.Server.GLanguageConVar, plan_server_novalve.md P2.3b. Its three
// native readers (world_adapter.cpp, server_util_library.cpp and
// util_shared_library.cpp) each hold a CManagedConVarRef to it.

// old_radiusdamage IS DECLARED IN C# NOW -- plan_shim.md §5 3e. It selects the
// pre-Orange-Box blocking rule, which has been BlastSweep's since radius damage
// became managed policy, and managed code already read it BY NAME through
// FindConVarValue once per explosion. The declaration that stood here was the
// last thing tying it to C++.
//
// The reason it used to give for staying -- "it is FCVAR_REPLICATED and the
// client's stock gamerules.cpp declares it too" -- has expired with the file:
// gamerules.cpp is in neither vpc, so there was no second declaration left to
// collide with. SourceSharp.Server.OldRadiusDamageConVar keeps the flag.

// THE GAME'S DIFFICULTY WAS DECLARED HERE, AND IS NOT ANY MORE: THE ENGINE'S
// `skill` IS THE ONE THAT WAS ALWAYS READ. Deleted in plan_server_novalve.md
// P2.3b, the pass the paragraph below left it to. What that pass was owed and
// what follows is the record of why a managed move was the wrong answer.
//
// gamerules.cpp:778. The shim audit named this first among the fifteen gameplay
// ConVars, and rightly -- a difficulty setting declared in C++ is as close to
// "a decision about what the game does" as a static analysis can get. The shim
// wave's policy-rules lane moved it to SourceSharp.Server.SkillConVar, ran the
// live gate, and moved it back.
//
// A MANAGED `skill` CANNOT BE SET FROM THE CONSOLE. Measured on dm_lockdown
// through ./debug-api: with this declaration `skill 3` moves the value 1 -> 3;
// with the managed one it stays at 1, from the console and from ./debug-api convar
// alike, and the managed registry's own row reads 1 as well.
//
// THE NAME IS THE WHOLE OF THE DIFFERENCE, and that was measured rather than
// reasoned about: a second managed convar of the identical shape and a name
// nothing else uses (`ss_skill_probe`), declared beside it and read in the same
// run, moved 1 -> 7, as did cs_sv_teamplay and cs_sv_fraglimit. Managed convars
// are settable; a managed convar called `skill` is not.
//
// WHAT IS IN THE WAY IS THE ENGINE, AND THAT IS A CORRECTION -- plan_shim.md
// §5 3e. The comment that stood here said the blocker was the client still
// compiling stock game/shared/gamerules.cpp and its own ConVar skill, and that
// deleting that file in a later phase would free the name. BOTH HALVES ARE
// WRONG, and each was measured:
//
//   * gamerules.cpp IS ALREADY IN NEITHER VPC.
//         grep -n 'gamerules' src/game/client/client_csharp.vpc
//     names shell_gamerules.cpp and nothing else. The blocker that comment
//     names against a future phase has already been removed and `skill` is
//     still not settable from a managed declaration.
//
//   * THE ENGINE DECLARES IT. Its help text, "Game skill level (1-3).", is
//     in the shipped binary, immediately before the name `skill`:
//         strings .../Source SDK Base 2013 Multiplayer/bin/linux64/engine.so \
//             | grep -c 'Game skill level'   -> 1
//     engine.so is loaded before either game DLL, so the engine's registration
//     is the one that wins the name and this declaration is already a
//     duplicate that changes nothing. A managed one, constructed later still,
//     cannot take a name tier1 has already given out.
//
// SO NO PHASE FREES THIS NAME. `skill` is the engine's convar, with the
// engine's range of 1-3 and the engine's FCVAR_ARCHIVE, and a mod reads it
// rather than declaring it. What should happen to the line below is that it
// GOES -- it is a third declaration of a name the engine already owns -- and
// managed code that wants the value reads it by name through
// IEngineConsole.FindConVarValue, which is how BlastSweep already reads
// old_radiusdamage. That deletion is left to the ConVar ledger pass (Phase 5)
// rather than taken here, because "the shim stops declaring skill" is a change
// to what `cvarlist` shows and belongs with the rest of that accounting.
//
// AND THE COMMENT THAT USED TO STAND HERE WAS WRONG, which the move found. It
// said "GameStats.cpp declares it `extern` and reads it, and that file is still
// compiled". GameStats.cpp is in NEITHER vpc: grepping `skill` over every $File
// in both -- 436 translation units -- returns this declaration and nothing
// else. So it has no reader in this build at all today. Stock's one consumer is
// CTakeDamageInfo::AdjustPlayerDamageInflictedForSkillLevel, which
// CGameRules::RadiusDamage reached only when the victim answered IsNPC(), and
// anyentity.h answers false for every entity on this hierarchy. The knob is
// kept and settable; what would read it is NPCs.

//-----------------------------------------------------------------------------
// IsExplosionTraceBlocked WAS HERE, and it went with its one caller.
//
// gamerules.cpp:375, and the comment that stood here already said "called only
// from RadiusDamage below, which is why it travelled with it". RadiusDamage is
// managed policy now (see the refusal further down), so the function has no
// caller in any compiled TU on either side -- measured over every $File in both
// csharp vpcs, not assumed.
//
// It was four clauses and three of them were ALREADY DEAD, which is why the
// managed rule is one line rather than four. DidHitWorld is the live one. The
// other three asked whether a DOOR was in the way -- MOVETYPE_PUSH plus three
// classnames -- and the comment that stood over them recorded that nothing on
// the parallel hierarchy is one, so the whole door test answered false for
// every entity that could reach it. BlastSweep keeps the live clause and does
// not restate the dead ones.
//
// gamerules_interface_test.cpp counted this as one of the five symbols evicting
// gamerules.cpp left undefined on the server. It is four now, and that test
// says why.
//-----------------------------------------------------------------------------

#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// THE HULLS. WHAT IS HERE IS THE PRE-REGISTRATION DEFAULT — plan_shim.md §5 3e.
//
// NOT guarded: GetViewVectors is an IGameRules member in the SHARED arm, so the
// client needs this table too. It is static, exactly as gamerules.cpp's is, so
// the client having its own copy is two statics and not two definitions.
//
// THE NINE NUMBERS ARE DECLARED IN Shared/Movement.cs and pushed across in
// SourceSharpMovementParams, which SourceSharp_PublishViewVectors below writes
// into this table once per side at managed startup. They were nine literals
// here and nowhere else before, which meant a mod that wanted a taller player
// edited C++ and rebuilt both DLLs -- and meant the same nine numbers were
// compiled TWICE, once per DLL, with nothing checking that the two agreed. That
// is the misprediction shape docs/plan-audit.md E1 removed from the movement
// arithmetic, applied to the box the arithmetic moves.
//
// SO WHY ARE THE LITERALS STILL HERE. Because a read before registration has to
// answer something, and the honest something is stock's hull rather than a
// zero-sized player. The macros are used all over the shared tree and some of
// those reads happen during level init on the way to the managed side coming
// up. gamerules_view_vectors_test.cpp asserts these nine agree with
// Shared/Movement.cs's nine, in both directions, so the default cannot quietly
// become a second opinion -- which is exactly what it would be if somebody
// changed the managed table and left this one.
//-----------------------------------------------------------------------------
static RulesViewVectors_t g_DefaultViewVectors(
	Vector( 0, 0, 64 ),         // VEC_VIEW
	Vector( -16, -16, 0 ),      // VEC_HULL_MIN
	Vector( 16, 16, 72 ),       // VEC_HULL_MAX
	Vector( -16, -16, 0 ),      // VEC_DUCK_HULL_MIN
	Vector( 16, 16, 36 ),       // VEC_DUCK_HULL_MAX
	Vector( 0, 0, 28 ),         // VEC_DUCK_VIEW
	Vector( -10, -10, -10 ),    // VEC_OBS_HULL_MIN
	Vector( 10, 10, 10 ),       // VEC_OBS_HULL_MAX
	Vector( 0, 0, 14 )          // VEC_DEAD_VIEWHEIGHT
);

//-----------------------------------------------------------------------------
// The write. Called once per side from SourceSharp_RegisterMovementParams in
// shell_movement.cpp, which is where the managed parameter block lands.
//
// A COPY AND NOT A STORED POINTER: the caller's struct is its own file-scope
// copy of what managed registered, and a pointer into it would be a second
// reader of something a hot reload re-registers.
//-----------------------------------------------------------------------------
static Vector SourceSharpVec3( const float *p )
{
	return Vector( p[ 0 ], p[ 1 ], p[ 2 ] );
}

//-----------------------------------------------------------------------------
// A DEGENERATE STANDING HULL IS REFUSED AND SAID OUT LOUD.
//
// This is the one thing that can go wrong here and it is invisible: an older
// managed build, or a mod that fills MovementParams field by field and misses
// these, registers a block whose hull fields are zero. Publishing that gives
// every player a collision box of no size -- they fall through the world, the
// traces answer nothing, and nothing anywhere reports an error. It is the exact
// shape of the silent losses this project keeps finding.
//
// So: a standing hull with no height at all is not a hull, and the table keeps
// stock's nine. The other eight are not checked -- an observer hull or a dead
// view height of zero is a strange choice rather than an impossible one, and
// refusing a legal value would be the guard doing more harm than the thing it
// guards against.
//-----------------------------------------------------------------------------
static bool SourceSharpHullIsDegenerate( const SourceSharpMovementParams *p )
{
	return p->vecHullMax[ 2 ] <= p->vecHullMin[ 2 ]
	    || p->vecHullMax[ 0 ] <= p->vecHullMin[ 0 ]
	    || p->vecHullMax[ 1 ] <= p->vecHullMin[ 1 ];
}

void SourceSharp_PublishViewVectors( const SourceSharpMovementParams *pParams )
{
	if ( !pParams )
		return;

	if ( SourceSharpHullIsDegenerate( pParams ) )
	{
		Warning( "[SourceSharp] movement params carry a standing hull with no "
		         "volume (min %.1f %.1f %.1f, max %.1f %.1f %.1f). KEEPING the "
		         "stock hull: publishing that one is a player with no collision "
		         "box, which looks like a broken map rather than a missing "
		         "number. Set Movement.Current's HullMin/HullMax in "
		         "SourceSharp.Shared.\n",
			pParams->vecHullMin[ 0 ], pParams->vecHullMin[ 1 ], pParams->vecHullMin[ 2 ],
			pParams->vecHullMax[ 0 ], pParams->vecHullMax[ 1 ], pParams->vecHullMax[ 2 ] );
		return;
	}

	g_DefaultViewVectors.m_vView           = SourceSharpVec3( pParams->vecView );
	g_DefaultViewVectors.m_vHullMin        = SourceSharpVec3( pParams->vecHullMin );
	g_DefaultViewVectors.m_vHullMax        = SourceSharpVec3( pParams->vecHullMax );
	g_DefaultViewVectors.m_vDuckHullMin    = SourceSharpVec3( pParams->vecDuckHullMin );
	g_DefaultViewVectors.m_vDuckHullMax    = SourceSharpVec3( pParams->vecDuckHullMax );
	g_DefaultViewVectors.m_vDuckView       = SourceSharpVec3( pParams->vecDuckView );
	g_DefaultViewVectors.m_vObsHullMin     = SourceSharpVec3( pParams->vecObsHullMin );
	g_DefaultViewVectors.m_vObsHullMax     = SourceSharpVec3( pParams->vecObsHullMax );
	g_DefaultViewVectors.m_vDeadViewHeight = SourceSharpVec3( pParams->vecDeadViewHeight );
}

//-----------------------------------------------------------------------------
// THE RULES OBJECT IS CREATED DIRECTLY. THERE IS NO REGISTRY.
//
// This was REGISTER_GAMERULES_CLASS( CSourceSharpGameRules ), which pushed a
// factory onto CGameRulesRegister's linked list so CreateGameRulesObject could
// look one up BY NAME. That indirection exists because a stock SDK build can
// hold several rules classes -- CTFGameRules, CHL2MPRules, CHalfLife2Survival
// -- and picks between them at runtime. This mod has exactly one, and every
// other rule it has is managed, so the list had one entry and the lookup could
// only ever return it.
//
// WHAT IS NOT REMOVED IS THE STRING TABLE, and the distinction matters. The
// registry is a name->factory map; the "GameRulesCreation" table is a WIRE
// CONTRACT. Writing the classname into it is how the server tells the client to
// build its own rules object, and the client's callback is the only thing that
// ever constructs C_SourceSharpGameRules. Deleting that with the registry would
// leave g_pGameRules NULL on the client, where cdll_client_int.cpp,
// cam_thirdperson.cpp, voice_status.cpp and c_world.cpp all dereference it.
//
// The string written is still the class name. Nothing reads it any more -- the
// callback below constructs the one class unconditionally -- but it is what
// goes over the wire, it costs nothing, and it is what a packet capture or a
// stringtable dump will show.
//-----------------------------------------------------------------------------
#define GAMERULES_STRINGTABLE_NAME		"GameRulesCreation"

INetworkStringTable *g_StringTableGameRules = NULL;

#ifdef CLIENT_DLL

//-----------------------------------------------------------------------------
// The server has made its rules object; make ours. Was
// OnGameRulesCreationStringChanged in gamerules_register.cpp, minus the lookup.
//-----------------------------------------------------------------------------
static void OnGameRulesCreationStringChanged( void *object, INetworkStringTable *stringTable,
                                              int stringNumber, const char *newString,
                                              void const *newData )
{
	delete g_pGameRules;
	g_pGameRules = NULL;

	// The constructor assigns g_pGameRules, exactly as it does on the server.
	new CSourceSharpGameRules();

	if ( !g_pGameRules )
		Error( "OnGameRulesCreationStringChanged: game rules object not created" );
}

void InstallStringTableCallback_GameRules()
{
	if ( g_StringTableGameRules )
		return;

	g_StringTableGameRules = networkstringtable->FindTable( GAMERULES_STRINGTABLE_NAME );

	if ( g_StringTableGameRules )
		g_StringTableGameRules->SetStringChangedCallback( NULL, OnGameRulesCreationStringChanged );
}

#else

void CreateNetworkStringTables_GameRules()
{
	g_StringTableGameRules = networkstringtable->CreateStringTable( GAMERULES_STRINGTABLE_NAME, 1 );
}

//-----------------------------------------------------------------------------
// Was CreateGameRulesObject( pClassName ). The name argument is gone with the
// registry that consumed it: there is one class, and asking for it by string
// could only fail or return it.
//-----------------------------------------------------------------------------
void SourceSharp_CreateGameRules()
{
	delete g_pGameRules;
	g_pGameRules = NULL;

	// The constructor assigns g_pGameRules.
	new CSourceSharpGameRules();

	if ( !g_pGameRules )
		Error( "SourceSharp_CreateGameRules: game rules object not created" );

	// Tell the client to build its own. Without this line the client never
	// makes one and every g_pGameRules-> on that side is a null dereference.
	Assert( g_StringTableGameRules );
	if ( g_StringTableGameRules )
	{
		const char *pszName = "CSourceSharpGameRules";
		g_StringTableGameRules->AddString( true, "classname", (int)strlen( pszName ) + 1, pszName );
	}

	g_pGameRules->CreateCustomNetworkStringTables();
}

#endif // CLIENT_DLL

#ifdef CLIENT_DLL
BEGIN_NETWORK_TABLE_NOBASE( CSourceSharpGameRules, DT_SourceSharpGameRules )
END_NETWORK_TABLE()
#endif
// The server's empty send table went in plan_server_native_v2 T2: no
// IMPLEMENT_SERVERCLASS and no proxy entity named it.

// The gamerules PROXY ENTITY and its two data-table proxies were here. Deleted
// -- nothing ever spawned one, and it carried an empty table. See the long note
// in shell_gamerules.h for the measurement and the reasoning.

CSourceSharpGameRules::CSourceSharpGameRules()
{
	SOURCESHARP_TRACE();

	// CGameRules' constructor did exactly this (gamerules.cpp:197) and there is
	// no base to do it now. The assert is stock's: two rules objects would
	// leave the first one leaked and unreachable.
	Assert( !g_pGameRules );
	g_pGameRules = this;

	// Also stock's, and it matters: the multi-damage accumulator is global and
	// a level change leaves whatever the last frame put in it.
#ifdef CLIENT_DLL
	ClearMultiDamage();
#else
	ss::ClearMultiDamage();
#endif
}

CSourceSharpGameRules::~CSourceSharpGameRules()
{
	SOURCESHARP_TRACE();

	// CGameRules' destructor put back every convar the rules had changed here
	// (gamerules.cpp:890). The whole mechanism went at plan_shim.md §5 3e: its
	// one writer, SaveConvar, had no caller in either vpc, so the list it
	// reverted was always empty.
	Assert( g_pGameRules == this );
	g_pGameRules = NULL;
}

//=============================================================================//
//
// BOTH SIDES.
//
// CGameRules declares the ten Damage_* answers and IsMultiplayer PURE, so the
// CLIENT's C_SourceSharpGameRules needs them exactly as much as the server's
// does. They lived in the server-only block for one commit and the client would
// not instantiate the class at all.
//
//=============================================================================//

//-----------------------------------------------------------------------------
// Which kinds of damage behave which way.
//
// STOCK'S VALUES AS THE DEFAULTS, so a build where managed code never registers
// behaves exactly like Half-Life rather than treating no damage as special.
// That is the same guarantee shell_movement.cpp gives for jump height and it is
// not decoration: this table is read from damage paths that run before the
// managed runtime is up on a listen server still loading, and a zeroed table
// would mean no damage is time-based, everything applies a physics force, and
// nothing gibs.
//
// Read from game/shared/multiplay_gamerules.cpp lines 167-253 rather than
// remembered. gamerules_damage_test.cpp holds these five against that file.
//-----------------------------------------------------------------------------
// The flag lists are NOT repeated in the comments beside them. Each field is
// the only statement of its own mask, which is the whole complaint against
// stock: CMultiplayRules spelled every one of these twice, in a getter and in a
// predicate, and nothing checked the two agreed.
static SourceSharpDamageRules s_DamageRules =
{
	// nTimeBased
	DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION
		| DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN,

	// nNoPhysicsForce. It INCLUDES the time-based set, which is stock's
	// composition rather than a repeated list.
	DMG_FALL | DMG_BURN | DMG_PLASMA | DMG_DROWN
		| DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION
		| DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN
		| DMG_CRUSH | DMG_PHYSGUN | DMG_PREVENT_PHYSICS_FORCE,

	// nShouldGibCorpse
	DMG_CRUSH | DMG_FALL | DMG_BLAST | DMG_SONIC | DMG_CLUB,

	// nShouldNotBleed
	DMG_POISON | DMG_ACID,

	// nShowOnHud
	DMG_POISON | DMG_ACID | DMG_DROWN | DMG_BURN | DMG_SLOWBURN
		| DMG_NERVEGAS | DMG_RADIATION | DMG_SHOCK,
};

//-----------------------------------------------------------------------------
// C# states them once at startup. See RegisterDamageRules in sourcesharp.h.
//-----------------------------------------------------------------------------
void SourceSharp_RegisterDamageRules( const SourceSharpDamageRules *pRules )
{
	if ( !pRules )
		return;

	s_DamageRules = *pRules;
}

#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// The rules' constants, and the defaults are the literals that used to be
// spelled inside the method bodies below.
//
// Kept as defaults rather than left zeroed for the same reason the dead slots
// answered stock's value rather than zero: these are read on paths that can run
// before C# has registered anything, and a zeroed table is not "unset", it is
// "co-op, no damage, items respawn instantly" -- a worse failure than the one
// being avoided, and a silent one.
//-----------------------------------------------------------------------------
static SourceSharpGameRulesPolicy s_Policy =
{
	true,                        // bMultiplayer
};

//-----------------------------------------------------------------------------
// C# states them once at startup. See RegisterGameRulesPolicy in sourcesharp.h.
//-----------------------------------------------------------------------------
void SourceSharp_RegisterGameRulesPolicy( const SourceSharpGameRulesPolicy *pPolicy )
{
	if ( !pPolicy )
		return;

	s_Policy = *pPolicy;
}
#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// The ten answers, all of them reads of that table.
//
// Each Is/Should/Show is written against its own Get. Stock had them as two
// independent spellings of the same flag list -- Damage_GetTimeBased() returned
// the mask and Damage_IsTimeBased() re-listed the seven flags inline -- which is
// two copies free to drift with nothing to report it if they did.
//-----------------------------------------------------------------------------
int CSourceSharpGameRules::Damage_GetTimeBased( void )       { return s_DamageRules.nTimeBased; }
int CSourceSharpGameRules::Damage_GetNoPhysicsForce( void )  { return s_DamageRules.nNoPhysicsForce; }

//-----------------------------------------------------------------------------
// Yes. Stock's CMultiplayRules::IsMultiplayer is `return true` and so is this;
// there is no single-player arm of this mod to be wrong about.
//
// Eight objects ask it, which is why it is worth having as its own line rather
// than inherited: it is the most-called of the eight.
//-----------------------------------------------------------------------------
bool CSourceSharpGameRules::IsMultiplayer( void )
{
#ifdef CLIENT_DLL
	// The client has no policy table -- it is registered on the server, and
	// this build is multiplayer on both sides by construction.
	return true;
#else
	return s_Policy.bMultiplayer != 0;
#endif
}

#ifndef CLIENT_DLL

//-----------------------------------------------------------------------------
// Connection admission. C# decides; the shell allows by default so a managed
// fault can never lock every player out of the server.
//-----------------------------------------------------------------------------
bool CSourceSharpGameRules::ClientConnected( edict_t *pEntity, const char *pszName,
                                             const char *pszAddress, char *reject, int maxrejectlen )
{
	SOURCESHARP_TRACE_MSG( pszName );

	IManagedClients *pApi = ManagedClients();   // plan_engineapi_vtables E: managed-built
	if ( pApi )
	{
		if ( reject && maxrejectlen > 0 )
			reject[0] = '\0';

		if ( pApi->OnClientConnect( pszName ? pszName : "", pszAddress ? pszAddress : "",
		                            reject, maxrejectlen ) == 0 )
		{
			Msg( "[SourceSharp] C# refused '%s': %s\n", pszName ? pszName : "?",
				( reject && reject[0] ) ? reject : "(no reason given)" );
			return false;
		}
	}

	return true;
}

//-----------------------------------------------------------------------------
// Where a player enters the map.
//
// The DECISION is managed code's: info_player_deathmatch is a C# class, and
// which of them a player gets is a rule a deathmatch mod makes. The PLACEMENT
// is here, and it has to be.
//
// The first attempt put both in C#, teleporting the player from OnPlayerSpawn.
// That runs after CBasePlayer::Spawn has finished and after the shell has made
// the player solid — so a player was made solid at the world origin, where the
// engine's own failed search had left them, and moved out of it afterwards.
// They arrived embedded in whatever was at the destination. It was reported
// from a real playthrough as "the player is spawning in the wall", which is
// exactly what it was.
//
// This runs at the point in CBasePlayer::Spawn where the engine would have
// searched itself: before the hull is sized, before the physics shadow exists,
// before anything is solid. Everything after it is stock's own sequence for
// making a player who is standing where they were just put.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL

// The control block's second drain of the frame. Declared here rather than in a
// header because shell_entityslots.cpp is the whole of its interface and one
// other caller does not earn one.
int SourceSharp_DrainEntitySlotsLate( void );
#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// The end of the game frame, which is the last moment a managed write can still
// reach this tick's snapshot.
//
// The drain in CSourceSharpSystem::FrameUpdatePostEntityThink catches everything
// written by a think, and misses everything written after it — the dev API's
// queue, which runs from Think() above, and ServiceEventQueue(), which is where
// every ent_fire lands. Both of those run later in GameFrame than any game
// system does, so a property they wrote was packed with a clean edict for one
// snapshot and only announced by the NEXT frame's drain.
//
// Measured with sv_debugmanualmode 1: thirteen func_breakable_surf broken from
// the dev API produced 225 "reported ENTITY_CHANGE_NONE but 'pNNN' changed"
// complaints, one per payload word. With this drain in place the same break
// produces none. docs/divergences.md has both runs.
//
// The early drain stays. This one cannot run when there are no game rules —
// between levels g_pGameRules is NULL and EndGameFrame is never called — and a
// frame with no drain at all is the state the pair exists to rule out.
//-----------------------------------------------------------------------------
void CSourceSharpGameRules::EndGameFrame( void )
{
	// Was BaseClass::EndGameFrame (gamerules.cpp:800), inlined with the base
	// gone. It is a diagnostic that only fires on a programming error: damage
	// was accumulated with AddMultiDamage and never applied.
	Assert( ss::g_MultiDamage.IsClear() );
	if ( !ss::g_MultiDamage.IsClear() )
	{
		Warning("Unapplied multidamage left in the system:\nTarget: %s\nInflictor: %s\nAttacker: %s\nDamage: %.2f\n",
			StandaloneEntity_Name( ss::g_MultiDamage.GetTarget() ),
			StandaloneEntity_Name( ss::g_MultiDamage.GetInflictor() ),
			StandaloneEntity_Name( ss::g_MultiDamage.GetAttacker() ),
			ss::g_MultiDamage.GetDamage() );
		ss::ApplyMultiDamage();
	}

	// The LAST of the frame's three managed points. gameinterface.cpp calls
	// this after ServiceEventQueue and UpdateAllClientData, which is what makes
	// it different from FrameUpdatePostEntityThink rather than a second copy of
	// it: an entity input delivered this frame has been acted on by now.
	//
	// BEFORE the late drain, not after. The drain is what turns this frame's
	// managed writes into one NetworkStateChanged per entity that actually
	// changed, and a participant here is entitled to write -- so running it
	// after the drain would hold its writes until the NEXT frame's drain, one
	// tick late, which is the exact failure the late drain was added to
	// prevent. The drain below therefore has to be the last thing in the frame,
	// and this has to be above it.
	// E18: the managed-built IManagedFrame.
	if ( IManagedFrame *pFrame = ManagedFrame() )
		pFrame->FramePostThink( gpGlobals->frametime );

	SourceSharp_DrainEntitySlotsLate();
}

// g_pPlayerResource is gone. Nothing ever assigned it (CreateStandardEntities
// does nothing here) and nothing in either DLL reads it; the globals ledger
// ruled it DEAD.


//=============================================================================//
//
// The last eight CMultiplayRules answers this build could reach.
//
// Every one was read out of game/shared/multiplay_gamerules.cpp rather than
// remembered, and several of them are ALREADY HOLLOW there -- the file carries
// `#ifdef SOURCESHARP` arms that stopped them doing anything when the standalone
// player replaced CBasePlayer. Writing them out here is what makes that visible:
// what reads as 2,004 lines of multiplayer rules is, for this mod, eight short
// answers and forty-one vtable slots nothing calls.
//
//=============================================================================//


//-----------------------------------------------------------------------------
// Level init.
//
// Stock's CMultiplayRules::Init calls InitCustomResponseRulesDicts() and then
// the base. That hook is `virtual void InitCustomResponseRulesDicts() {}` in
// multiplay_gamerules.h and the ONLY override in the tree is CTFGameRules'
// (tf_gamerules.cpp:18601), which is not this mod -- so in this build stock's
// Init is exactly `return BaseClass::Init()` with an empty call in front of it.
//
// CGameRules:: rather than BaseClass::, deliberately. BaseClass is still
// CMultiplayRules for this class, and calling it would put back the very
// implementation this override exists to stop reaching.
//-----------------------------------------------------------------------------
// (inline in the header now -- IGameRules declares it in the shared arm.)

//-----------------------------------------------------------------------------
// A client's keyvalue command.
//
// EMPTY, and stock's is too: its body opens with `#ifdef SOURCESHARP return;`
// already, because everything below dynamic_casts the edict to a
// CBaseMultiplayerPlayer to announce achievements, and none exists.
//-----------------------------------------------------------------------------
// (inline in the header now -- IGameRules declares it in the shared arm.)

#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// MOVED FROM gamerules.cpp:1024, VERBATIM.
//-----------------------------------------------------------------------------
//
// Live. Reached through the VEC_VIEW / VEC_HULL_MIN / VEC_HULL_MAX macros in
// shareddefs.h, which expand to g_pGameRules->GetViewVectors().
//
// The body below is unchanged apart from the class name. This is a
// RELOCATION: the file it came from is on its way out of the build, and a
// behaviour change smuggled into a move like this would be invisible.
//-----------------------------------------------------------------------------
const RulesViewVectors_t* CSourceSharpGameRules::GetViewVectors() const
{
	return &g_DefaultViewVectors;
}

//-----------------------------------------------------------------------------
// WHICH GROUPS COLLIDE -- A TABLE LOOKUP. The sixty-seven lines of ordered ifs
// this replaced were CGameRules::ShouldCollide (gamerules.cpp:915), and they are
// declared in C# now: see SourceSharp.Server.CollisionRules.
//
// The rules were pure integer logic over a twenty-by-twenty domain, so they fit
// in a matrix exactly. That matters here more than it would elsewhere: this is
// asked once per traced entity, from CTraceFilterSimple, so it runs thousands of
// times a frame and used to branch through up to fourteen comparisons to answer.
//
// The defaults below are stock's answers, generated from stock's own predicate
// and pinned to it by collision_rules_test.cpp, which keeps that predicate
// VERBATIM as an oracle and checks all four hundred pairs. They are not
// hand-written hex.
//-----------------------------------------------------------------------------
static SourceSharpCollisionRules s_CollisionRules =
{
	{
	0x000FABFFu,   // COLLISION_GROUP_NONE
	0x00020001u,   // COLLISION_GROUP_DEBRIS
	0x00000001u,   // COLLISION_GROUP_DEBRIS_TRIGGER
	0x000EAAC1u,   // COLLISION_GROUP_INTERACTIVE_DEBRIS
	0x000EABE1u,   // COLLISION_GROUP_INTERACTIVE
	0x000C23F1u,   // COLLISION_GROUP_PLAYER
	0x000EABB9u,   // COLLISION_GROUP_BREAKABLE_GLASS
	0x000EB3F9u,   // COLLISION_GROUP_VEHICLE
	0x000CABF1u,   // COLLISION_GROUP_PLAYER_MOVEMENT
	0x000EE3F9u,   // COLLISION_GROUP_NPC
	0x00000000u,   // COLLISION_GROUP_IN_VEHICLE
	0x000E8959u,   // COLLISION_GROUP_WEAPON
	0x00000080u,   // COLLISION_GROUP_VEHICLE_CLIP
	0x000E83F9u,   // COLLISION_GROUP_PROJECTILE
	0x000E8200u,   // COLLISION_GROUP_DOOR_BLOCKER
	0x000EEBD9u,   // COLLISION_GROUP_PASSABLE_DOOR
	0x00000001u,   // COLLISION_GROUP_DISSOLVING
	0x000EEADBu,   // COLLISION_GROUP_PUSHAWAY
	0x000EEBF9u,   // COLLISION_GROUP_NPC_ACTOR
	0x000EEBF9u,   // COLLISION_GROUP_NPC_SCRIPTED
	}
};

//-----------------------------------------------------------------------------
// C# states them once at startup. See RegisterCollisionRules in sourcesharp.h.
//-----------------------------------------------------------------------------
void SourceSharp_RegisterCollisionRules( const SourceSharpCollisionRules *pRules )
{
	if ( !pRules )
		return;

	s_CollisionRules = *pRules;

	// The physics shell's copy of these rows went with its rule table
	// (plan_physics_bones_native_zero 2.3): managed's PhysicsSolverRules
	// builds the same matrix from CollisionRules for vphysics' questions.
}

bool CSourceSharpGameRules::ShouldCollide( int collisionGroup0, int collisionGroup1 )
{
	// Out of range is not indexable, and stock's answer for everything it did
	// not name is true. A caller passing LAST_SHARED_COLLISION_GROUP or a mod's
	// own group beyond it gets that rather than a read past the table.
	if ( collisionGroup0 < 0 || collisionGroup0 >= LAST_SHARED_COLLISION_GROUP ||
	     collisionGroup1 < 0 || collisionGroup1 >= LAST_SHARED_COLLISION_GROUP )
		return true;

	// The matrix is symmetric, so the swap stock did first is unnecessary --
	// but it is free and it keeps the two sides reading the same way.
	return ( s_CollisionRules.nRows[ collisionGroup0 ] & ( 1u << collisionGroup1 ) ) != 0;
}
