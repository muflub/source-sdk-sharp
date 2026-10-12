//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — game rules ==========================================//
//
// Game rules must be a SHARED, networked class: the server replicates the
// rules class through a proxy entity and the client instantiates its
// counterpart. Registering via REGISTER_GAMERULES_CLASS (rather than a bare
// `new`) is what puts the class in the registry CreateGameRulesObject looks up.
//
// Behaviour that represents an actual game decision routes into C#.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_GAMERULES_H
#define SOURCESHARP_SHELL_GAMERULES_H
#ifdef _WIN32
#pragma once
#endif

#ifdef CLIENT_DLL
#include "gamerules.h"
#else
// The server reaches the rules object through IGameRules alone: game/shared/
// gamerules.h (stock's CGameRules) is no longer in this header's reach there.
#include "igamerules.h"
#include "networkvar.h"     // DECLARE_CLASS_NOBASE
#include "server_class.h"   // DECLARE_SERVERCLASS_NOBASE
#endif

// multiplay_gamerules.h is NOT included and CMultiplayRules is NOT the base any
// more. All 55 of its vtable slots are either answered by this class or reached
// by nothing at all, so the inheritance was carrying 2,004 lines of stock
// deathmatch rules for no answer this build uses.

#ifdef CLIENT_DLL
	#define CSourceSharpGameRules      C_SourceSharpGameRules
#endif

class CSourceSharpGameRules;

//-----------------------------------------------------------------------------
// THE PROXY ENTITY WAS HERE, AND NOTHING IN THIS BUILD EVER MADE ONE.
//
// `class CSourceSharpGameRulesProxy : public CGameRulesProxy` with a
// DECLARE_NETWORKCLASS, registered as `sourcesharp_gamerules`, and a send/recv
// table pair carrying DT_SourceSharpGameRules through a data-table proxy. It is
// the stock mechanism for replicating a rules object, which is not an entity,
// by hanging its table off one that is.
//
// It had no route into existence. CGameRules::CreateStandardEntities() creates
// `player_manager` and nothing else, CSourceSharpGameRules does not override
// it, no CreateEntityByName names this class anywhere in the tree, and
// `sourcesharp_gamerules` does not appear in ss_sandbox.bsp. That is §3b's
// "spawned by nothing" case, measured the way §3b measures it.
//
// AND IT WAS NOT MERELY IDLE, which is why it is deleted rather than left. A
// vtable names every inherited virtual whether or not anything calls one, so
// this class cost the link 178 undefined references on the client and 76 on the
// server FOR EXISTING -- the same accounting that deleted C_QuadraticBeam from
// shell_effects_sim.cpp. Measured with evict_consumers: they were the whole of
// shell_gamerules.o's demand on both sides.
//
// NOTHING WAS REPLICATED THROUGH IT. DT_SourceSharpGameRules is an empty table
// -- BEGIN_NETWORK_TABLE_NOBASE straight to END_NETWORK_TABLE, zero props --
// so even a spawned proxy would have carried no state. The client never had a
// rules object either way: CreateGameRulesObject( "CSourceSharpGameRules" ) is
// called from shell_server.cpp, which is server-only, so the recv proxy's own
// Assert( pRules ) could never have passed. The rules that matter are decided
// on the server and reach the client as ordinary entity state.
//
// The rules class itself and its (empty) network table are untouched below.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// IGameRules ONLY. CGameRules is not the base any more.
//
// What that base supplied, and where each piece went:
//
//   the 50 IGameRules answers  -- 18 were already overridden here; the other 32
//                                 are written out below and in the .cpp, each
//                                 one CGameRules' own body with the line it
//                                 came from named.
//   g_pGameRules ownership     -- the constructor and destructor do it now,
//                                 which is all CGameRules' ever did.
//   CAutoGameSystemPerFrame    -- DROPPED, and it cost nothing. It gave three
//                                 hooks: LevelShutdownPostEntity (empty on the
//                                 server -- its body is `#ifdef CLIENT_DLL`),
//                                 FrameUpdatePostEntityThink -> Think (the
//                                 managed frame moved off it, and what is left
//                                 is SetSkillLevel plus a log timer), and Init
//                                 (still reached, but through IGameRules from
//                                 world_adapter.cpp:378, not through the
//                                 system list).
//
// THE SKILL LEVEL STOPS BEING UPDATED PER FRAME AND THAT IS INERT, measured
// rather than assumed: g_iSkillLevel is written by SetSkillLevel and read by
// GetSkillLevel/IsSkillLevel, GetSkillLevel's only caller is
// CGameRules::RefreshSkillData, and RefreshSkillData has NO caller in the
// compiled set. The whole mechanism is a closed loop inside gamerules.cpp that
// reaches nothing this build runs.
//-----------------------------------------------------------------------------
class CSourceSharpGameRules : public IGameRules
{
public:
	DECLARE_CLASS_NOBASE( CSourceSharpGameRules );
#ifdef CLIENT_DLL
	DECLARE_NETWORKCLASS_NOBASE();
#else
	DECLARE_SERVERCLASS_NOBASE();	// what DECLARE_NETWORKCLASS_NOBASE is on this side
#endif

	CSourceSharpGameRules();
	virtual ~CSourceSharpGameRules();

	//-------------------------------------------------------------------------
	// Whether a connected client may change a FCVAR_NOT_CONNECTED convar.
	//
	// Overridden only to drop an assert. The base implementation asserts
	// !IsMultiplayer() before returning true, which fires on every multiplayer
	// mod that does not override it — this one included, until a debug build
	// said so.
	//-------------------------------------------------------------------------
	virtual bool IsConnectedUserInfoChangeAllowed( CBasePlayer *pPlayer ) OVERRIDE { return true; }

	//-------------------------------------------------------------------------
	// BOTH SIDES. CGameRules declares these eleven PURE, so the client's
	// C_SourceSharpGameRules needs them as much as the server's does -- they
	// were server-only for one commit and the client would not instantiate.
	//-------------------------------------------------------------------------
	//-------------------------------------------------------------------------
	// Which kinds of damage behave which way — eight methods, five masks, and
	// every one of them C#'s answer rather than CMultiplayRules's.
	//
	// These are POLICY. Stock spells each mask as a literal OR of DMG_* flags
	// inside the method body, and every one is a sentence about how this mod's
	// combat feels: acid keeps hurting you, a fall applies no physics force, a
	// crowbar can gib a corpse. A mod that wanted burning to stop being
	// time-based had to edit C++ to say so. DamageRules.cs says it now.
	//
	// THE PAIRS ARE ONE LIST HERE AND WERE TWO IN STOCK. Damage_GetTimeBased()
	// returned the mask and Damage_IsTimeBased(t) re-spelled the same seven
	// flags inline instead of calling it — two copies free to drift, with
	// nothing to report it if they had. Each Is/Should/Show below is written
	// against its own Get, so there is one list per question.
	//
	// Defined inline because each is a mask test, they are called from damage
	// paths that run per hit, and the values are pushed once rather than asked
	// for (see SourceSharpDamageRules).
	//-------------------------------------------------------------------------
	// THREE OF THESE ELEVEN ARE IGameRules MEMBERS AND EIGHT ARE NOT, which is
	// not an oversight. IGameRules lists what calls into the rules THROUGH the
	// rules pointer, and only three are reached that way in this build:
	// Damage_GetNoPhysicsForce, Damage_GetTimeBased and Damage_NoPhysicsForce
	// (takedamageinfo.cpp:355, compiled on both sides). The other eight are the
	// same policy family answered from the same table, but their g_pGameRules->
	// callers are all in files this build does not compile -- player.cpp,
	// ai_basenpc.cpp, physics.cpp. They stay plain virtuals here rather than
	// being added to the interface for symmetry: an interface member with no
	// caller is surface with nothing behind it.
	virtual int  Damage_GetTimeBased( void ) OVERRIDE;
	virtual int  Damage_GetNoPhysicsForce( void ) OVERRIDE;

	virtual bool Damage_NoPhysicsForce( int iDmgType ) OVERRIDE;

	// Yes, and said here rather than inherited. See the .cpp.
	virtual bool IsMultiplayer( void ) OVERRIDE;

	//-------------------------------------------------------------------------
	// THE STOCK ANSWERS, SAID HERE RATHER THAN INHERITED.
	//
	// Every one of these is an IGameRules member this class did not override,
	// so CGameRules answered it. They are written out because inheriting them
	// is the thing being removed: the class has to answer all 50 itself before
	// CGameRules can stop being its base.
	//
	// EACH BODY IS CGameRules' BODY, unchanged, with the header line it came
	// from named. That makes this a RELOCATION and not a decision -- a
	// different answer here would be a change to the game hidden inside a
	// refactor, which is the one thing a move like this must not do.
	//
	// Where one is genuinely policy it should become C#'s, the way the damage
	// masks and GameRulesPolicy already have. None of them is today: the ones
	// with a live caller are listed in the .cpp with the call site.
	//-------------------------------------------------------------------------
	virtual bool AllowThirdPersonCamera( void ) OVERRIDE { return false; }            // gamerules.h:221
	virtual void ClientSpawned( edict_t *pPlayer ) OVERRIDE { return; }               // gamerules.h:465
	virtual void OnFileReceived( const char *fileName, unsigned int transferID ) OVERRIDE { return; }  // gamerules.h:467
	virtual bool ShouldDrawHeadLabels( void ) OVERRIDE { return true; }               // gamerules.h:463

	// IGameRules declares these two in its SHARED arm, so the client needs them
	// as much as the server does -- CGameRules used to supply the client's.
	// Inline because both bodies are one line and neither differs by side.
	//
	// Init was CGameRules' forward to CAutoGameSystemPerFrame::Init. This class
	// is not a game system any more, so there is nothing above to forward to,
	// and true is what that chain returned. Reached from world_adapter.cpp:378.
	virtual bool Init( void ) OVERRIDE { return true; }
	virtual void ClientCommandKeyValues( edict_t *pEntity, KeyValues *pKeyValues ) OVERRIDE {}

	//-------------------------------------------------------------------------
	// The six with real bodies, moved into the .cpp rather than inlined here.
	// Each says at its definition where it came from and what calls it.
	//-------------------------------------------------------------------------
	virtual const RulesViewVectors_t *GetViewVectors( void ) const OVERRIDE;

	// Was CGameRules' (gamerules.h:325) and RadiusDamage asks it. Not an
	// IGameRules member -- nothing calls it through the rules pointer -- so it
	// is a plain virtual, answering what the base answered.
	virtual bool ShouldUseRobustRadiusDamage( IGameEntity *pEntity ) { return false; }
	virtual bool  ShouldCollide( int collisionGroup0, int collisionGroup1 ) OVERRIDE;

	// THE TAGGED-CONVAR MECHANISM IS GONE, all three parts and the vector they
	// shared -- plan_shim.md §5 3e. SaveConvar was the IGameRules member and the
	// only thing that ever WROTE m_SavedConvars; its one caller was
	// gamerules.cpp:1244, and that file is in neither vpc. So the vector could
	// only ever be empty, HasSavedConvar could only ever answer false, and the
	// destructor's RevertSavedConvars() was a loop over nothing. Removing the
	// writer and leaving the reverter would have left a mechanism that reads
	// live and does nothing, which is the shape this phase exists to remove.

#ifndef CLIENT_DLL
	// Server-browser name — answered by C#.

	// Rules C# decides; the shell falls back to stock behaviour when managed
	// code declines (negative damage / -1 flashlight).
	//
	// Server-only: gamerules.h declares these inside its !CLIENT_DLL branch.
	virtual bool  ClientConnected( edict_t *pEntity, const char *pszName,
	                               const char *pszAddress, char *reject, int maxrejectlen ) OVERRIDE;
	virtual int   PlayerRelationship( IGameEntity *pPlayer, IGameEntity *pTarget );

	// Respawn and damage policy. CMultiplayRules answers all of these in C++;
	// each now asks C# first and falls back to the stock rule when managed code
	// declines, so a managed fault degrades to stock behaviour rather than to
	// no behaviour.

	// Where a player enters the map. Asks C#, which owns the spawn point
	// entities and the rule for choosing between them, and does the placement
	// itself — see the definition for why the placement cannot be managed.

	// Team mode, and a Think that no longer drives anything managed -- the
	// managed frame runs from the gamesystem's own FrameUpdatePostEntityThink
	// now. The override survives only because CGameRules declares Think pure.
	// See the .cpp.

	//-------------------------------------------------------------------------
	// The rules' one entity, and it is a MANAGED one now (lane U cluster 14).
	//
	// CGameRules::CreateStandardEntities does exactly one thing:
	//
	//     g_pPlayerResource = (CPlayerResource*)CBaseEntity::Create( "player_manager", ... );
	//     g_pPlayerResource->AddEFlags( EFL_KEEP_ON_RECREATE_ENTITIES );
	//
	// player_manager is a C# class on the parallel hierarchy now, so the factory
	// answers with a CStandaloneEntity — which is not a CBaseEntity, so
	// EntityCreate_NoSpawn's `CreateNoSpawn: only works for CBaseEntities`
	// assert fires, it returns NULL, and the very next line dereferences it.
	// Overriding is what stops that line ever running.
	//
	// EMPTY, not "creates it here". SourceSharp.Server's module creates the
	// singleton at level init, which is the same moment for the same reason and
	// needs no C++ at all. See ServerModule.CreatePlayerResource.
	//
	// NOT CALLING THE BASE IS THE WHOLE OVERRIDE, so there is nothing here to
	// get wrong later: any future standard entity belongs in C# beside the one
	// that is already there.
	//-------------------------------------------------------------------------
	virtual void CreateStandardEntities( void ) OVERRIDE {}

	// The last thing the game DLL does in a frame, and the only hook that runs
	// after both the dev API's queue and the entity I/O queue. The dirty-word
	// drain happens here a second time so that a property written from either is
	// announced in the frame it was written. See SourceSharp_DrainEntitySlotsLate.
	virtual void EndGameFrame( void ) OVERRIDE;


	//-------------------------------------------------------------------------
	// The last eight CMultiplayRules answers this build could still reach.
	//
	// With these overridden, NOTHING in game/shared/multiplay_gamerules.cpp is
	// reachable from this class any more: 27 of its 55 vtable slots were
	// already answered here, 10 more went with the damage masks, and these are
	// the remainder that anything actually calls. The other 41 are pure vtable
	// weight -- slots nothing in this build invokes.
	//
	// They are written out rather than left to inherit because inheriting them
	// is the thing being removed. Each one is small in THIS build and several
	// are already hollow upstream, which is the point: what looked like 2,004
	// lines of multiplayer rules is, for this mod, eight short answers.
	//-------------------------------------------------------------------------
	virtual void ClientDisconnected( edict_t *pClient ) OVERRIDE;
	virtual void GetTaggedConVarList( KeyValues *pCvarTagList ) OVERRIDE;

	//-------------------------------------------------------------------------
	// THE TWENTY-FIVE C++ REQUIRES AND NOTHING CALLS.
	//
	// CGameRules declares each of these pure, so the class cannot be
	// instantiated without a body; CMultiplayRules used to supply them. Not one
	// is reachable in this build, and the reason is the same for almost all of
	// them: their arguments are CBasePlayer, CItem and CBaseCombatWeapon, and
	// this build constructs none of the three. The player is the standalone
	// player (13c), and items and weapons are managed entities.
	//
	// THEY ANSWER STOCK'S VALUE ANYWAY rather than zero. If one ever does become
	// reachable, stock behaviour is the right thing to find there -- a zeroed
	// respawn time is an item that reappears instantly, and that is a worse
	// failure than the one being avoided.
	//
	// Where a managed member already owns the LIVE rule, the .cpp names it, and
	// gamerules_deadslots_test.cpp pins the two together so the dead native
	// constant cannot drift away from the managed one that actually runs.
	//-------------------------------------------------------------------------






	//-------------------------------------------------------------------------
	// The server-only half of the same list. See the comment above.
	//-------------------------------------------------------------------------
	virtual float AdjustPlayerDamageInflicted( float damage ) OVERRIDE { return damage; }  // gamerules.h:359
	virtual void  AdjustPlayerDamageTaken( RulesDamageInfo_t *pInfo ) OVERRIDE {}            // gamerules.h:360
	virtual void  CreateCustomNetworkStringTables( void ) OVERRIDE {}                      // gamerules.h:444
	virtual void  EndMultiplayerGame( void ) OVERRIDE {}                                   // gamerules.h:422
	virtual void  InitDefaultAIRelationships( void ) OVERRIDE { return; }                  // gamerules.h:388
	virtual void  Precache( void ) OVERRIDE { return; }                                    // gamerules.h:267
	virtual void  ResetMapCycleTimeStamp( void ) OVERRIDE { return; }                      // gamerules.h:449
	virtual void  Status( void (*print) (const char *fmt, ...) ) OVERRIDE {}               // gamerules.h:252

	// Server-only, with real bodies in the .cpp.
	virtual void  RadiusDamage( const RulesDamageInfo_t &info, const Vector &vecSrcIn, float flRadius,
	                            int iClassIgnore, IGameEntity *pEntityIgnore,
	                            const IHandleEntity *pTraceIgnore = NULL ) OVERRIDE;
#else
	//-------------------------------------------------------------------------
	// And the two the CLIENT half of IGameRules declares.
	//-------------------------------------------------------------------------
	virtual bool AllowMapVisionFilterShaders( void ) OVERRIDE { return false; }             // gamerules.h:241
	virtual const char *TranslateEffectForVisionFilter( const char *pchEffectType,
	                                                    const char *pchEffectName ) OVERRIDE
	{
		return pchEffectName;                                                              // gamerules.h:242
	}
#endif

};

//-----------------------------------------------------------------------------
// Makes the one rules object and tells the client to make its own. Replaces
// CreateGameRulesObject, whose name argument was a registry lookup.
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
void SourceSharp_CreateGameRules();
#endif

//-----------------------------------------------------------------------------
// THE HULLS ARRIVE IN shell_gamerules.cpp — plan_shim.md §5 3e, and this note
// is here because the rules class is what a reader looking for them will open.
//
// shareddefs.h's VEC_HULL_MIN and its eight neighbours all expand to
// `g_pGameRules->GetViewVectors()->m_v...`, so the rules object is where a hull
// has to be to be readable at all. What DECIDES the nine numbers is
// Shared/Movement.cs, and they arrive inside SourceSharpMovementParams because
// they are read on exactly the path that block exists for — inside the movement
// loop, per command, on both sides.
//
// SourceSharp_PublishViewVectors is the write, defined beside the table in
// shell_gamerules.cpp and declared at the top of shell_movement.cpp, which is
// its one caller. It is NOT declared in this header on purpose: the header is
// included by seven translation units and would have to pull in the whole of
// sourcesharp.h to name the parameter type. A signature that drifted would be a
// link error naming the mangled symbol, which is the check a shared declaration
// would have bought.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
inline CSourceSharpGameRules *SourceSharpGameRules()
{
	return static_cast< CSourceSharpGameRules * >( g_pGameRules );
}

#endif // SOURCESHARP_SHELL_GAMERULES_H
