//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — baseentity.cpp's tenants that outlive it ===========//
// SS_REASON g  leaf math over mdl/bsp data
//
// THE LINK-ONLY DEFECT CLASS, CLOSED BEFORE THE ARM.
//
// game/server/baseentity.cpp is step 6's removal set: it is deleted and its
// $File dropped from server_base.vpc in the same commit that arms the alias.
// Almost everything in it is a CBaseEntity member, and a member is not a step-6
// hazard -- armed, a caller either dispatches through IGameEntity's vtable and
// emits no symbol at all, or it fails to COMPILE, and the compile survey names
// the file and the line.
//
// A FREE FUNCTION DOES NEITHER. It survives the flip untouched, every
// translation unit that calls it compiles perfectly clean, and the LINK fails.
// No compile survey this port has run can see that class, and a step-6 tree
// cannot be link-tested at all until its compile gate is already green -- so
// the defect would be discovered on the far side of the one irreversible
// commit. docs/typedef-dryrun/step6/README.md §4 is the measurement; this file
// and its client counterpart are the close-out.
//
// It is the same shape T1 already hit once: PhysGetMassCenterOverride had
// external linkage while its only definition sat inside a guard. Every TU
// compiled. The link failed.
//
// WHAT IS HERE, AND WHY EACH ONE. Measured with src/evict_consumers plus
// src/s6r_linkonly.py -- link server.so with baseentity.o removed and read the
// undefined symbols that are NOT CBaseEntity:: members. SEVEN came back across
// the three configurations; four have demands that survive the elision and are
// evacuated here.
//
//   g_pForceAttachEdict            mapentities.cpp:83,89 -- CreateEntityByName
//                                  writes it to force the next entity onto a
//                                  particular edict. mapentities.cpp survives
//                                  step 6 and compiles CLEAN armed, which is
//                                  what makes this invisible.
//   EntityCreate_Spawned           gameinterface.cpp:1108 -- the level-
//                                  transition autosave entity. DELETED with
//                                  EntityCreate_NoSpawn (P5 lane E): no
//                                  compiled TU calls either, server.so held
//                                  neither, and Spawned's DispatchSpawn has no
//                                  definition anywhere.
//   SendProxy_ClientSideAnimation  baseanimating.cpp:184 -- the send proxy on
//                                  DT_ServerAnimationData.
//   GameSound_EmitSentenceByIndex  sound.cpp:104,150,180,211 -- and this one
//                                  appears in NO probe on a normal build. See
//                                  THE SENTENCE EMITTER below.
//
// THREE CONFIGURATIONS, NOT ONE, AND EACH FOUND SOMETHING THE LAST DID NOT.
// Release is what the README's table is taken in. Debug adds the demands that
// live inside Assert(), which compiles to nothing in release -- on the client
// that is two more symbols. And `make debug GC_SECTIONS=0` adds the demands
// that --gc-sections collects the CALLER of before it ever needs the callee --
// on the server that is GameSound_EmitSentenceByIndex, which is why the
// README names that command specifically and why it is not enough to run the
// probe once.
//
// TWO OF THE SIX ARE REFUSED, and the reason is written where anyone looking
// for them will be -- see THE TWO THAT HAVE NO SURVIVING HOME at the bottom.
//
// THE BODIES ARE UNCHANGED. Only the translation unit they are compiled in
// moves, exactly as it did for entity_bases.cpp and precache_model.cpp. Nothing
// here is armed, nothing here is conditional, and a normal green
// `make debug GC_SECTIONS=0 && make release` is the whole verification --
// which is the entire point of doing it now rather than later.
//
// THIS FILE IS NOT STEP-6-READY, AND IS NOT MEANT TO BE. Two of these bodies
// still name CBaseEntity, so armed this TU joins the compile gate as ordinary
// RETYPE work. That is the trade being made on purpose: a compile failure that
// a survey names beats a link failure that nothing can see.
//
// THE DECLARATIONS ARE ALREADY WHERE THEY NEED TO BE and this file adds no
// header surface at all. GameSound_EmitSentenceByIndex is declared in
// baseentity_statics.h; the other two were already declared extern
// at their own demand sites, by the SDK, before this port started --
// mapentities.cpp:76 and baseanimating.cpp:153. A local extern that disagreed
// with the definition below would mangle differently and fail to link, so the
// duplication is self-checking rather than a place to drift.
//
//=============================================================================//

#include "dt_send.h"
#include "edict.h"
#include "irecipientfilter.h"
#include "engine/IEngineSound.h"
#include "baseentity_statics.h"
#include "engine_utlvector.h"    // ss::EngineFilledUtlVector: the sentence origins the engine appends

extern IEngineSound *enginesound;   // shell_engineboundary.cpp; cbase.h declared it before P5


//-----------------------------------------------------------------------------
// This can be set before creating an entity to force it to use a particular
// edict.
//
// WRITTEN by mapentities.cpp's CreateEntityByName when an edict index is forced
// (a save-game restore, which has to put entities back on the indices the save
// recorded), and READ once, by CBaseEntity::PostConstructor, which attaches it
// and clears it. The reader is inside the removal set and goes away at step 6;
// the writer is not and does not, which is exactly why the definition had to
// leave baseentity.cpp.
//
// mapentities.cpp:76 declares it `extern edict_t *g_pForceAttachEdict;` in its
// own file scope and needs no edit for this move.
//-----------------------------------------------------------------------------
edict_t *g_pForceAttachEdict = NULL;

//-----------------------------------------------------------------------------
// The send proxy on DT_ServerAnimationData: suppress m_flAnimTime for an entity
// whose animation the client is running itself.
//
// Never a class member -- a SendProxy has a fixed C signature and could not be
// one. It was defined in baseentity.cpp next to the DT_AnimTimeMustBeFirst
// table that also uses it; that table is inside the removal set and goes away,
// while baseanimating.cpp's DT_BaseAnimating does not.
//
// baseanimating.cpp:153 declares it in its own file scope and needs no edit,
// which matters here: game/server/baseanimating.cpp belongs to another lane and
// this move deliberately does not reach into it.
//
// REGISTER_SEND_PROXY_NON_MODIFIED_POINTER comes with it. It declares a
// file-scope registration object that adds the proxy to the engine's
// non-modified-pointer list at static-init time; which TU that object lives in
// has never mattered, only that exactly one does.
//-----------------------------------------------------------------------------
void* SendProxy_ClientSideAnimation( const SendProp *pProp, const void *pStruct, const void *pVarData, CSendProxyRecipients *pRecipients, int objectID )
{
	// ARMED, NOTHING INVOKES THIS PROXY. Its two tables are
	// DT_AnimTimeMustBeFirst (baseentity.cpp) and DT_ServerAnimationData
	// (baseanimating.cpp), and BOTH files are in the flip's removal set. The
	// registration object below survives because
	// REGISTER_SEND_PROXY_NON_MODIFIED_POINTER is a file-scope object, not
	// because anything sends through it.
	//
	// The unreachable answer is "send it", which is what a client running its
	// own animation is NOT told -- so the neutral is deliberately the arm that
	// keeps the field flowing rather than the arm that silently drops it, in
	// case a table is ever pointed back here before this proxy leaves with
	// them.
	return (void*)pVarData;
}
REGISTER_SEND_PROXY_NON_MODIFIED_POINTER( SendProxy_ClientSideAnimation );

//=============================================================================//
//
// THE TWO THAT HAVE NO SURVIVING HOME, and are refused rather than invented.
//
//     GetScriptInstanceHelper_CBaseEntity()
//     ScriptClassDesc_t *GetScriptDesc<CBaseEntity>( CBaseEntity * )
//
// Both are emitted by ONE construct in baseentity.cpp:
//
//     BEGIN_ENT_SCRIPTDESC_ROOT( CBaseEntity, "Root class of all server-side
//                                              entities" )
//         ... about 180 DEFINE_SCRIPTFUNC lines ...
//     END_SCRIPTDESC();
//
// and both are demanded by ONE construct in game/server/baseanimating.cpp:192,
// which names CBaseEntity as its scriptdesc BASE:
//
//     BEGIN_ENT_SCRIPTDESC( CBaseAnimating, CBaseEntity, "Animating models" )
//
// (game/shared/vscript_shared.h:26-27 is where both macros are; the accessor
// pair is public/vscript/ivscript.h:353-379.)
//
// MOVING THE BLOCK WOULD ACCOMPLISH NOTHING, and that is the finding rather
// than a matter of taste. Every line inside it is a DEFINE_SCRIPTFUNC naming a
// MEMBER of CBaseEntity -- GetHealth, SetModel, ScriptTakeDamage, entindex,
// ConnectOutputToScript and some ninety more. Under `using CBaseEntity =
// IGameEntity;` an alias is not a class scope and IGameEntity has exactly six
// pure virtuals and no base, so not one of those lines compiles no matter which
// translation unit it is written in. This is not a definition that needs a new
// home; it is a description of a class that is being deleted.
//
// IT IS NOT A LINK-ONLY DEFECT EITHER, once looked at. The demand is not a call
// -- it is the base-class argument of a macro, and it disappears the moment
// CBaseAnimating stops naming CBaseEntity as its scriptdesc base. That is
// exactly the UNDERIVE decision wave 1 is making for CBaseAnimating anyway, and
// the whole close-out here is one word in one line of baseanimating.cpp:
// BEGIN_ENT_SCRIPTDESC( CBaseAnimating, CBaseEntity, ... ) becomes
// BEGIN_ENT_SCRIPTDESC_ROOT( CBaseAnimating, ... ).
//
// SO IT IS HANDED OVER RATHER THAN WORKED AROUND. game/server/baseanimating.cpp
// is another lane's file, the edit belongs in that lane's commit next to the
// rest of CBaseAnimating's underive, and doing it from here would be a second
// front on one line. Recorded here so that the next person to run
// s6r_linkonly.py and find six symbols where this file accounts for four knows
// which two are missing and why.
//
//=============================================================================//
