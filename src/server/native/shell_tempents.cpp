//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — temp entities, built from a description ============//
// SS_REASON a  a vtable the engine calls through
//
// plan_tmp.md Part 3. The whole server half of the temp entity system, in one
// file that knows nothing about any individual effect.
//
// It rests on one fact about the engine: a temp entity is encoded from its
// send table and a sender that is a `const void *`.
//
// The engine never asks what class the sender is — every
// prop reads at its own declared offset — so a temp entity needs a TABLE, not a
// C++ class. Stock has classes only because SENDINFO computes offsets with
// offsetof. C# computes them instead, hands the list over, and this builds
// SendProp[] -> SendTable -> ServerClass at runtime from it.
//
// THE SHAPE IS shell_standalone.cpp's, deliberately, and not a third one. That
// file proved the technique end to end (a runtime table, a runtime ServerClass,
// a client that joins and decodes), and its finding about the WINDOW is the one
// that costs a day to rediscover: a ServerClass links itself into
// g_pServerClassHead when it is constructed and the engine walks that list
// exactly ONCE. One built later is simply not in the list.
//
// WHAT IS NOT HERE: any knowledge of what an effect looks like, any per-TE
// function, and any offset. The arithmetic that decides whether a description
// is buildable is in shared/tempent_schema.h, where a gtest can reach it with no
// running Source.
//
//=============================================================================//

#include "eiface.h"

// The engine (connected in shell_engineboundary.cpp); cbase.h supplied it.
extern IVEngineServer *engine;
#include "ss_recipient_filter.h"
#include "shell_tempents.h"
#include "shell_tempents_internal.h"
#include "ss_te_prediction.h"
#include "tempent_schema.h"
#include "sourcesharp.h"
#include "dt_send.h"
#include "server_class.h"
#include "coordsize.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E13: the server's IManagedTempEnts


//=============================================================================//
// THE FLAG VALUES ARE SPROP_*'s OWN, AND THIS IS WHERE THAT IS CHECKED.
//
// sourcesharp.h cannot include dt_common.h — it is a pure C header the managed
// host also reads — so it restates the bits. Two enumerations of one set that
// agreed when they were written and drifted afterwards is the failure this tree
// keeps writing memories about, and the symptom here would be a prop encoded as
// a coordinate that the client decodes as a normal.
//
// A compile-time check rather than a comment claiming they match. If a value
// ever moves, this file stops building and names the flag.
//=============================================================================//
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_UNSIGNED              == SPROP_UNSIGNED );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_COORD                 == SPROP_COORD );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_NOSCALE               == SPROP_NOSCALE );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_ROUNDDOWN             == SPROP_ROUNDDOWN );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_ROUNDUP               == SPROP_ROUNDUP );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_NORMAL                == SPROP_NORMAL );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_VECTOR_ELEM           == SPROP_IS_A_VECTOR_ELEM );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_COORD_MP              == SPROP_COORD_MP );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_COORD_MP_LOWPRECISION == SPROP_COORD_MP_LOWPRECISION );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_FLAG_COORD_MP_INTEGRAL     == SPROP_COORD_MP_INTEGRAL );

// And the widths the schema restates, against the headers this file DOES see.
// tempent_schema_test.cpp checks the same agreement by reading the headers as
// text, because it has none of them; this is the cheaper half, and it fails the
// build rather than a test run.
COMPILE_TIME_ASSERT( SOURCESHARP_TE_MAX_EDICT_BITS     == MAX_EDICT_BITS );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_MODEL_INDEX_BITS   == SP_MODEL_INDEX_BITS );
// Cast, because both sides are unnamed enums and -Wenum-compare objects to the
// comparison rather than to the values. EF_MAX_BITS is an enumerator in
// const.h's effect flags, not a #define like its neighbours.
COMPILE_TIME_ASSERT( (int)SOURCESHARP_TE_EF_MAX_BITS  == (int)EF_MAX_BITS );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_EHANDLE_BITS       == NUM_NETWORKED_EHANDLE_BITS );
// SOURCESHARP_TE_MAX_PLAYERS == MAX_PLAYERS is asserted by the client's twin of
// this block (shell_tempents_client.cpp): MAX_PLAYERS is shareddefs.h's, which
// this file's R5 include set does not reach, and both DLLs read the one header.
COMPILE_TIME_ASSERT( SOURCESHARP_TE_COORD_INTEGER_BITS == COORD_INTEGER_BITS );

//-----------------------------------------------------------------------------
// One registered class's built parts. The props and the table are leaked on
// purpose: a SendTable is read by the engine for the life of the process, and
// there is no point in the process where these could be freed and nothing
// would notice.
//-----------------------------------------------------------------------------
struct SourceSharpTeBuilt_t
{
	SendProp    *pProps;
	int          nProps;
	SendTable   *pTable;
	ServerClass *pClass;      // NULL for a table registered without one
};

static CSourceSharpTeRegistry g_TeRegistry;
static SourceSharpTeBuilt_t   g_TeBuilt[ SOURCESHARP_TE_MAX_CLASSES ];
static int                    g_nTeBuilt = 0;

//-----------------------------------------------------------------------------
// A SendTable already in the server's class list, by name.
//
// THE HOOK THAT MAKES A STAGED PORT POSSIBLE. A lane lands DT_TESparks while
// DT_TEParticleSystem is still stock's, and for the length of the port that is
// the ordinary state rather than an error. Without this the registrar would
// have to refuse every derived table until its whole base chain had moved,
// which is the whole port in one commit.
//
// Walks g_pServerClassHead and recurses into each table's datatable props,
// because an INTERMEDIATE table (DT_TEParticleSystem, DT_BaseBeam) is reachable
// only through the class that nests it, not as a class in its own right.
//-----------------------------------------------------------------------------
static SendTable *FindNativeTableIn( SendTable *pTable, const char *pName, int nDepth )
{
	if ( !pTable || nDepth > 8 )
		return NULL;

	if ( pTable->GetName() && !strcmp( pTable->GetName(), pName ) )
		return pTable;

	for ( int i = 0; i < pTable->GetNumProps(); i++ )
	{
		SendProp *pProp = pTable->GetProp( i );

		if ( pProp->m_Type != DPT_DataTable )
			continue;

		SendTable *pFound = FindNativeTableIn( pProp->GetDataTable(), pName, nDepth + 1 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

static SendTable *FindRegisteredTable( const char *pName )
{
	if ( !pName || !pName[0] )
		return NULL;

	// Ours first. When both sides of a name exist — which is what a half-ported
	// base table looks like — the MANAGED one is the one whose offsets address
	// the blob C# packed, so nesting the native one would read a coordinate out
	// of whatever the blob has at offsetof( CTEParticleSystem, m_vecOrigin ).
	const int iOurs = g_TeRegistry.FindByTableName( pName );

	if ( iOurs >= 0 && iOurs < g_nTeBuilt && g_TeBuilt[ iOurs ].pTable )
		return g_TeBuilt[ iOurs ].pTable;

	for ( ServerClass *pClass = g_pServerClassHead; pClass; pClass = pClass->m_pNext )
	{
		SendTable *pFound = FindNativeTableIn( pClass->m_pTable, pName, 0 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

static bool TeBaseExists( const char *pTableName, void * )
{
	return FindRegisteredTable( pTableName ) != NULL;
}

//-----------------------------------------------------------------------------
// THE BUILT TABLES, read by the dump (shell_tempents_internal.h). Accessors and
// not the arrays themselves: the registry and g_TeBuilt stay this file's, and
// cs_sv_te_dump in shell_tempents_debug.cpp only reads them.
//-----------------------------------------------------------------------------
const CSourceSharpTeRegistry &SourceSharpTe_Registry()
{
	return g_TeRegistry;
}

SendTable *SourceSharpTe_BuiltTable( int i )
{
	return ( i >= 0 && i < SOURCESHARP_TE_MAX_CLASSES ) ? g_TeBuilt[ i ].pTable : NULL;
}

ServerClass *SourceSharpTe_BuiltClass( int i )
{
	return ( i >= 0 && i < SOURCESHARP_TE_MAX_CLASSES ) ? g_TeBuilt[ i ].pClass : NULL;
}

// IsOurTable and FindStockTableNamed, the dump's independent reference, went
// with it to shell_tempents_debug.cpp: nothing else here asked for them.

//-----------------------------------------------------------------------------
// Whether a ServerClass of this name is already in the list the engine
// negotiates at connect.
//
// The one collision that has no symptom (plan 2E): the client begins
// instantiating and stops, with nothing in any log. So it is refused HERE,
// where there is a console to say so on.
//-----------------------------------------------------------------------------
static bool ServerClassExists( const char *pName )
{
	for ( ServerClass *pClass = g_pServerClassHead; pClass; pClass = pClass->m_pNext )
	{
		if ( pClass->GetName() && !strcmp( pClass->GetName(), pName ) )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// The name a prop is declared under. One field can be several props —
// "m_vecOrigin" becomes "m_vecOrigin[0..2]" — and a SendProp keeps the pointer
// it is handed for the life of the process, so these cannot be built on a
// caller's stack.
//
// The DERIVATION of the name is SourceSharpTe_PropName's, in the shared header,
// because the client derives the same one and the two sides pair props by
// string. What is left here is only the interning, which is per-side.
//-----------------------------------------------------------------------------
static SourceSharpTePropNamePool_t g_TePropNames;

static const char *InternPropName( const char *pName )
{
	return SourceSharpTe_InternPropName( g_TePropNames, pName );
}

//-----------------------------------------------------------------------------
// One SendProp, from one entry of the shared plan.
//
// WHICH PROPS EXIST IS NOT DECIDED HERE any more — SourceSharpTe_PlanProps
// decides it, once, for both sides (tempent_schema.h). What is left is the
// construction, which genuinely differs: this side makes SendProps and the
// client makes RecvProps out of the same entries.
//
// SENDINFO_VECTORELEM's NEGATED OFFSET arrives already negated in
// nDeclareOffset. dt_send.h:281 says why it exists — "SENDPROP_VECTORELEM makes
// this negative to start with so we can detect that and set the
// SPROP_IS_VECTOR_ELEM flag" — and the engine flips it back in
// SendTable_InitTable.
//-----------------------------------------------------------------------------
static bool BuildOneProp( const SourceSharpTePropPlan_t *pPlan, SendProp *pOut )
{
	const char *pName = InternPropName( pPlan->szName );

	switch ( pPlan->nKind )
	{
	case SOURCESHARP_TE_PROP_INT:
		*pOut = SendPropInt( pName, pPlan->nDeclareOffset, pPlan->nSizeBytes,
		                     pPlan->nBits, pPlan->nFlags );
		return true;

	case SOURCESHARP_TE_PROP_FLOAT:
		*pOut = SendPropFloat( pName, pPlan->nDeclareOffset, pPlan->nSizeBytes,
		                       pPlan->nBits, pPlan->nFlags, pPlan->flLow, pPlan->flHigh );
		return true;

	case SOURCESHARP_TE_PROP_VECTOR:
		*pOut = SendPropVector( pName, pPlan->nDeclareOffset, pPlan->nSizeBytes,
		                        pPlan->nBits, pPlan->nFlags, pPlan->flLow, pPlan->flHigh );
		return true;

	// SendPropAngle and SendPropQAngles are NOT the float and vector forms with
	// [0,360] written out: they also install SendProxy_AngleToFloat /
	// SendProxy_QAngles, which run anglemod() on the way out. A negative pitch
	// through a plain float proxy encodes outside its declared range.
	case SOURCESHARP_TE_PROP_ANGLE:
		*pOut = SendPropAngle( pName, pPlan->nDeclareOffset, pPlan->nSizeBytes,
		                       pPlan->nBits, pPlan->nFlags );
		return true;

	case SOURCESHARP_TE_PROP_QANGLES:
		*pOut = SendPropQAngles( pName, pPlan->nDeclareOffset, pPlan->nSizeBytes,
		                         pPlan->nBits, pPlan->nFlags );
		return true;

	case SOURCESHARP_TE_PROP_DATATABLE:
		{
			SendTable *pNested = FindRegisteredTable( pPlan->pSubTable );

			if ( !pNested )
				return false;

			*pOut = SendPropDataTable( pName, pPlan->nDeclareOffset, pNested,
			                           SendProxy_DataTableToDataTable );
			return true;
		}

	// The DPT_Array half of the pair. m_pArrayProp is left NULL and the engine
	// links it to the preceding prop in SendTable_InitTable
	// (public/dt_send.cpp:671) — emit both, link neither.
	case SOURCESHARP_TE_PROP_ARRAY:
		*pOut = InternalSendPropArray( pPlan->nElems, pPlan->nElemStride, pName, NULL );
		return true;
	}

	return false;
}

//=============================================================================//
// REGISTRATION.
//=============================================================================//
int32_t SourceSharp_TempEntRegister( const SourceSharpTeClass_t *pDesc )
{
	if ( !pDesc || !pDesc->pClassName || !pDesc->pTableName )
	{
		Warning( "[SourceSharp] temp entity registration with no description\n" );
		return -1;
	}

	// A MANAGED RELOAD ASKS AGAIN, and the answer is the handle it had before.
	//
	// The tables and the ServerClasses are process-lifetime by construction —
	// the engine walked the class list once, at DLL init, and there is no second
	// walk — so a reload cannot build new ones and must not try. What it needs
	// back is the HANDLE, because the new assembly has none. Answering -1 here
	// would leave every ported effect silently unable to fire after the first
	// reload, which is the kind of loss that gets blamed on the effect.
	const int iExisting = g_TeRegistry.FindByClassName( pDesc->pClassName );

	if ( iExisting >= 0 )
	{
		const SourceSharpTeClass_t *pHad = g_TeRegistry.At( iExisting );

		// A RESHAPED CLASS CANNOT BE HONOURED, and saying so is the whole point.
		// The table on the wire is the one built at DLL init; a reloaded
		// assembly whose blob is a different size would pack fields the props
		// do not read.
		if ( strcmp( pHad->pTableName, pDesc->pTableName )
		  || pHad->nBlobBytes != pDesc->nBlobBytes
		  || pHad->nFieldCount != pDesc->nFieldCount )
		{
			Warning( "[SourceSharp] temp entity '%s' was RESHAPED across a reload "
			         "(table '%s'->'%s', %d->%d blob bytes, %d->%d field(s)). The table on "
			         "the wire is the one built at DLL init and cannot change without a "
			         "restart; the old shape is what will be sent.\n",
				pDesc->pClassName, pHad->pTableName, pDesc->pTableName,
				pHad->nBlobBytes, pDesc->nBlobBytes,
				pHad->nFieldCount, pDesc->nFieldCount );
		}

		return iExisting;
	}

	int nWhy = SOURCESHARP_TE_ACCEPTED;
	const int iHandle = g_TeRegistry.Add( pDesc, &nWhy );

	if ( iHandle < 0 )
	{
		Warning( "[SourceSharp] temp entity '%s' (%s) NOT registered: %s\n",
			pDesc && pDesc->pClassName ? pDesc->pClassName : "?",
			pDesc && pDesc->pTableName ? pDesc->pTableName : "?",
			SourceSharpTe_RefusalText( nWhy ) );
		return -1;
	}

	const SourceSharpTeClass_t *pClass = g_TeRegistry.At( iHandle );

	// COUNTED RATHER THAN GUESSED, by the same function the plan is built with:
	// a short array here is a buffer overrun into whatever the allocator put
	// next, and every prop past the end still builds.
	const int nWanted = SourceSharpTe_PlanCount( pClass );

	if ( nWanted < 0 )
	{
		Warning( "[SourceSharp] temp entity '%s': its description cannot be counted; "
		         "NOT registered\n", pClass->pClassName );
		return -1;
	}

	// THE PLAN, from the shared header, which is what decides that the base goes
	// first, that one field can be three props named "m_vecOrigin[0..2]", and
	// that an array is a pair. The client walks the identical list; that is the
	// point of it being there rather than here.
	SourceSharpTePropPlan_t *pPlan = new SourceSharpTePropPlan_t[ nWanted > 0 ? nWanted : 1 ];
	const int nPlanned = SourceSharpTe_PlanProps( pClass, pPlan, nWanted );

	if ( nPlanned != nWanted )
	{
		Warning( "[SourceSharp] temp entity '%s': planned %d prop(s) where %d were counted; "
		         "NOT registered\n", pClass->pClassName, nPlanned, nWanted );
		delete[] pPlan;
		return -1;
	}

	SendProp *pProps = new SendProp[ nWanted > 0 ? nWanted : 1 ];
	int nBuilt = 0;

	for ( int i = 0; i < nPlanned; i++ )
	{
		if ( !BuildOneProp( &pPlan[i], &pProps[ nBuilt ] ) )
		{
			Warning( "[SourceSharp] temp entity '%s': prop '%s' could not be built as a "
			         "SendProp; NOT registered\n", pClass->pClassName, pPlan[i].szName );
			delete[] pPlan;
			delete[] pProps;
			return -1;
		}

		// SPROP_COLLAPSIBLE, WHICH IS SET BEHIND THIS CODE'S BACK AND CRASHES
		// THE CLIENT. dt_send.cpp:648 sets it on any datatable prop whose proxy
		// is the identity AND whose offset is zero, and a collapsed datatable is
		// applied by the RECEIVER at the object base -- so the nested table's
		// props write over whatever is at offset 0 of the receiving object,
		// which for a native shell is its vtable pointer. Lane S4 found this
		// with DT_EffectData: a clean join, cs_sv_te_dump agreeing 28 of 28,
		// and a segfault on the first dispatch with a NULL vptr.
		//
		// STOCK NEVER REACHES IT because SENDINFO_DT is an offsetof past a
		// vtable, so a nested table is never at zero. A runtime-built table has
		// no vtable in front of it and offset zero is reachable in a way it
		// never was in C++. TeClassBuilder pads nested tables for exactly this
		// reason; this says so out loud if the pad ever stops arriving.
		//
		// THE "baseclass" PROP IS EXEMPT AND MUST BE. It is at offset zero on
		// purpose -- a base's props address the same blob from the same place --
		// and stock sets the same flag on its own. Collapsing it is correct.
		if ( pPlan[i].nKind == SOURCESHARP_TE_PROP_DATATABLE && pPlan[i].iField >= 0
		  && ( pProps[ nBuilt ].GetFlags() & SPROP_COLLAPSIBLE ) )
		{
			Warning( "[SourceSharp] temp entity '%s': nested table prop '%s' came out "
			         "SPROP_COLLAPSIBLE (offset %d). The receiver applies a collapsed "
			         "datatable at the OBJECT BASE, so its props will write over whatever "
			         "is at offset 0 -- a vtable pointer, on any native shell. The nested "
			         "table needs a non-zero offset; see TeClassBuilder's pad.\n",
				pClass->pClassName, pPlan[i].szName, pPlan[i].nOffset );
		}

		nBuilt++;
	}

	delete[] pPlan;

	g_TeBuilt[ iHandle ].pProps = pProps;
	g_TeBuilt[ iHandle ].nProps = nBuilt;
	// STOCK'S NAME, ALWAYS, and this is where a rename was tried and reverted.
	//
	// The server is shared by BOTH client implementations at once -- the managed
	// one for whatever C-Phase 3 has evicted, the stock one for everything else
	// -- and it has only one name per table. DT_BaseBeam under any other name
	// produced "Missing RecvProp for DT_TEBeamEntPoint - .../m_nModelIndex" on
	// the first join, because seven beam client halves are still native and nest
	// it by the name stock gave it. plan 2E is not negotiable here: the set of
	// names, tables, prop lists and nesting must be exactly stock's.
	//
	// The duplicate-name problem is real and is entirely the CLIENT's -- see
	// tempent_schema.h, and shell_tempents_client.cpp for the refusal that
	// enforces it.
	g_TeBuilt[ iHandle ].pTable = new SendTable( pProps, nBuilt, pClass->pTableName );
	g_TeBuilt[ iHandle ].pClass = NULL;
	g_nTeBuilt = iHandle + 1;

	if ( pClass->nRegisterClass )
	{
		// The refusal with no symptom. Two ServerClasses of one name go into the
		// list the engine negotiates at connect and the client begins
		// instantiating and stops — see shell_standalone.cpp:1083. Refused here,
		// where there is a console to say so on, and the effect then does not
		// fire rather than the server becoming unjoinable.
		if ( ServerClassExists( pClass->pClassName ) )
		{
			Warning( "[SourceSharp] temp entity '%s' NOT registered as a ServerClass: the "
			         "native build still registers one of that name. Evict its te_*.cpp "
			         "from server_csharp.vpc, or register this as a base table. Two classes "
			         "of one name make the client begin joining and stop, with nothing in "
			         "any log.\n", pClass->pClassName );
		}
		else
		{
			g_TeBuilt[ iHandle ].pClass =
				new ServerClass( pClass->pClassName, g_TeBuilt[ iHandle ].pTable );
		}
	}

	const int nWorst = g_TeRegistry.WorstCaseBits( iHandle );

	Msg( "[SourceSharp] temp entity %2d '%s' -> '%s': %d prop(s), %d blob byte(s), "
	     "base '%s', worst case %s\n",
		iHandle, pClass->pClassName, pClass->pTableName, nBuilt, pClass->nBlobBytes,
		pClass->pBaseTable ? pClass->pBaseTable : "(none)",
		nWorst < 0 ? "not priced (a table in the chain is still stock's)" : "" );

	if ( nWorst >= 0 )
	{
		Msg( "[SourceSharp]     %d bit(s), %d byte(s) of %d\n",
			nWorst, ( nWorst + 7 ) / 8, SOURCESHARP_TE_MAX_EVENT_BYTES );
	}

	return iHandle;
}

//-----------------------------------------------------------------------------
// Shuts the window. Called once, after managed has pushed its classes, from
// wherever LoadSide returns — see TempEntRegister in sourcesharp.h for what a
// late registration does and does not do.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntCloseRegistration( void )
{
	if ( g_TeRegistry.IsClosed() )
		return;

	g_TeRegistry.Close();

	int nClasses = 0;

	for ( int i = 0; i < g_TeRegistry.Count(); i++ )
	{
		if ( g_TeBuilt[i].pClass )
			nClasses++;
	}

	Msg( "[SourceSharp] temp entities: %d description(s), %d ServerClass(es); "
	     "registration closed\n", g_TeRegistry.Count(), nClasses );
}

//=============================================================================//
// THE PREDICTION FILTER, AND THE TOOLS REFUSAL.
//
// Both were CTempEntsSystem's, in te.cpp, and both outlived it. shell_tempents.h
// is where the reasoning lives; this is the code.
//=============================================================================//

//-----------------------------------------------------------------------------
// The temp entity subsystem's suppress host (ss_te_prediction.h).
//
// It was a bare IPredictionSystem whose constructor linked it into
// g_pPredictionSystems, so that SuppressHostEvents would reach this channel.
// Nothing on the server walks that list (nm: no SuppressEvents,
// SuppressHostEvents or CDisablePredictionFiltering in server.so), and the one
// reader of the host -- takedamageinfo_library.cpp's ApplyMultiDamage -- asks
// this object directly, so it is the host alone now (P5 lane SP). The sixty
// pure virtuals ITempEntsSystem added on top were a dispatch table and nothing
// more; every caller names the TE_*() function directly (shell_tempents.h).
//-----------------------------------------------------------------------------
static ss::TePrediction g_TePrediction;

ss::TePrediction *SourceSharpTe_PredictionSystem( void )
{
	return &g_TePrediction;
}

//-----------------------------------------------------------------------------
// CTempEntsSystem::SuppressTE (te.cpp:122), minus its body — plan_entity.md
// step 4b's §3.16 family 5 site, and it was the LAST cast in this file.
//
// STOCK'S BODY. If a suppress host is set, cull it out of the filter and, if
// that empties the filter, refuse the temp entity outright: the host is the
// client who already predicted it locally, so sending it again would draw it
// twice. The cull needed a CBasePlayer *, so stock cast the host — an
// IPredictionSystem's `CBaseEntity const *` — down to one. That downcast is
// family 5, and there is no player class on this hierarchy for it to land on.
//
// THE HOST IS NULL ON EVERY PATH IN THIS BUILD, so `return false` is what the
// whole function already computed and this is a deletion rather than a
// refusal. Every writer of a suppress host, checked one at a time:
//
//   game/server/physics_main.cpp:2205  inside `#ifndef SOURCESHARP`
//   game/server/player.cpp:3376        the TU is not in server_csharp.vpc
//   game/shared/takedamageinfo.cpp:320 writes back the value it just read,
//                                      which is the NULL the constructor set
//
// That is not left as prose: suppress_host_dead_test.cpp reads the compiled TU
// list and fails if a fourth writer appears, naming this site and
// shell_recipientfilter.cpp's two. A live prediction host is then a red test,
// not a temp entity that quietly reaches one client too many.
//
// g_TePrediction ITSELF STAYS. It is the object the engine's prediction system
// list is walked through, and SourceSharpTe_PredictionSystem() above hands it
// to takedamageinfo.cpp; only the cull it existed to feed is gone.
//-----------------------------------------------------------------------------
bool SourceSharpTe_SuppressFilter( IRecipientFilter & )
{
	return false;
}

//=============================================================================//
// PLAYBACK.
//=============================================================================//

static void SendThroughFilter( IRecipientFilter &filter, SendTable *pTable, int nClassID,
                               const void *pBlob, float flDelay )
{
	engine->PlaybackTempEntity( filter, flDelay, pBlob, pTable, nClassID );
}

//-----------------------------------------------------------------------------
// Sends one, through whichever filter the caller asked for.
//
// The two assertions CBaseTempEntity::Create makes are made here instead, and
// the delay one is enforced rather than asserted: stock's Assert compiles out of
// a release build and sends anyway, and a temp entity queued a minute into the
// future is an effect that never visibly happens. Clamped and said out loud.
//
// The filters are all stack objects and none of them is reliable or an init
// message, which is the other half of what stock asserts.
//-----------------------------------------------------------------------------
static void PlaybackThrough( const SourceSharpTeFilter_t *pFilter, SendTable *pTable,
                             int nClassID, const void *pBlob, float flDelay )
{
	const Vector vecOrigin( pFilter->vecOrigin[0], pFilter->vecOrigin[1], pFilter->vecOrigin[2] );

	switch ( pFilter->nKind )
	{
	case SOURCESHARP_TE_FILTER_PVS:
		{
			ss::CPVSFilter filter( vecOrigin );
			SendThroughFilter( filter, pTable, nClassID, pBlob, flDelay );
			return;
		}

	case SOURCESHARP_TE_FILTER_PAS:
		{
			ss::CPASFilter filter( vecOrigin );
			SendThroughFilter( filter, pTable, nClassID, pBlob, flDelay );
			return;
		}

	case SOURCESHARP_TE_FILTER_PAS_ATTENUATION:
		{
			ss::CPASAttenuationFilter filter( vecOrigin,
				pFilter->flAttenuation > 0.0f ? pFilter->flAttenuation : ATTN_NORM );
			SendThroughFilter( filter, pTable, nClassID, pBlob, flDelay );
			return;
		}

	case SOURCESHARP_TE_FILTER_SINGLE_PLAYER:
		{
			ss::CSingleUserRecipientFilter filter( pFilter->nPlayerIndex );
			SendThroughFilter( filter, pTable, nClassID, pBlob, flDelay );
			return;
		}
	}

	ss::CBroadcastRecipientFilter filter;
	SendThroughFilter( filter, pTable, nClassID, pBlob, flDelay );
}

void SourceSharp_TempEntPlayback( int32_t hClass, const void *pBlob, int32_t nBytes,
                                  const SourceSharpTeFilter_t *pFilter, float flDelay )
{
	const SourceSharpTeClass_t *pClass = g_TeRegistry.At( hClass );

	if ( !pClass || !pBlob || !pFilter )
		return;

	if ( !g_TeBuilt[ hClass ].pTable )
		return;

	// A SHORT BLOB IS REFUSED, not truncated. The engine encodes through the
	// props' own offsets and has no idea how long the buffer is, so a blob one
	// field short reads whatever the caller had after it and sends a plausible
	// number.
	if ( nBytes < pClass->nBlobBytes )
	{
		Warning( "[SourceSharp] temp entity '%s': %d byte(s) offered for a %d byte blob; "
		         "NOT sent\n", pClass->pClassName, nBytes, pClass->nBlobBytes );
		return;
	}

	// A class with no ServerClass has no class id to send under. That is the
	// ordinary state of a base table, and asking one to play is a caller bug
	// rather than a wire problem.
	if ( !g_TeBuilt[ hClass ].pClass )
	{
		Warning( "[SourceSharp] temp entity '%s' has no ServerClass of its own and cannot "
		         "be played back\n", pClass->pClassName );
		return;
	}

	// Stock's own bound (basetempentity.cpp: Assert( delay >= -1 && delay <= 1 )),
	// enforced rather than asserted.
	if ( flDelay < -1.0f || flDelay > 1.0f )
	{
		Warning( "[SourceSharp] temp entity '%s': delay %.2f is outside stock's [-1,1]; "
		         "clamped\n", pClass->pClassName, flDelay );
		flDelay = clamp( flDelay, -1.0f, 1.0f );
	}

	PlaybackThrough( pFilter, g_TeBuilt[ hClass ].pTable,
	                 g_TeBuilt[ hClass ].pClass->m_ClassID, pBlob, flDelay );
}

//=============================================================================//
// THE FORWARDING WRITER — what keeps TE_Sparks() resolving while the class is
// registered from managed.
//
// plan_tmp.md Part 7 leaves te.cpp and its callers alone until the very end, and
// EffectsServer.cpp and util.cpp are both on this build's link line and both
// reach te->. So evicting te_sparks.cpp — which is what porting the class
// REQUIRES, because two ServerClasses of one name make the join hang — would
// leave TE_Sparks() unresolved for te.cpp:450.
//
// This is the generic half of the answer: the field list per class is already
// here, so a writer over it needs no per-TE knowledge at all and each forwarding
// shim is six lines in its own file. The alternative was to give each ported TE
// a hand-written native writer, which is 43 copies of an offset table that C#
// already owns.
//
// ONE SCRATCH BLOB, and it is not a limitation: this runs on the server's own
// thread inside one call, between Begin and Send, and nothing yields in between.
//=============================================================================//

static unsigned char g_TeScratch[ 512 ];

//-----------------------------------------------------------------------------
// A FORWARD, WITH ITS ARGUMENTS AND NOTHING ELSE (tempent_forwards.h).
//
// The shell_te_fwd_*.cpp entry points used to know which class they were, which
// argument became which field, and what the field was called. All three are
// managed now. What is left here is the marshalling and the SEND -- and the
// send stays because it needs the caller's own IRecipientFilter, which cannot
// be rebuilt from the managed side's TeFilter (a kind and an origin, and stock
// passes CPVSFilter, CBroadcastRecipientFilter and friends through a base
// interface that does not say which).
//
// So managed packs the fields into the scratch blob and says which class; this
// sends them exactly as SourceSharpTe_Send does.
//
// A refusal from managed -- an unknown id, a class with no declaration, a blob
// that does not fit -- sends NOTHING, rather than sending a blob it made up.
//-----------------------------------------------------------------------------
bool SourceSharpTe_Forward( int nId, IRecipientFilter &filter, float flDelay,
                            const float *pFloats, int nFloats,
                            const int *pInts, int nInts )
{
	// E13: the server's managed-built area.
	IManagedTempEnts *pTempEnts = ManagedTempEnts();

	if ( !pTempEnts )
		return false;

	int hClass = -1;

	const int nBytes = pTempEnts->OnNativeTempEntity( nId, pFloats, nFloats, pInts, nInts,
	                                                  g_TeScratch, (int)sizeof( g_TeScratch ),
	                                                  &hClass );

	if ( nBytes <= 0 || hClass < 0 || hClass >= g_nTeBuilt )
		return false;

	if ( !g_TeBuilt[ hClass ].pTable || !g_TeBuilt[ hClass ].pClass )
		return false;

	Assert( !filter.IsReliable() && !filter.IsInitMessage() );
	Assert( flDelay >= -1 && flDelay <= 1 );

	SendThroughFilter( filter, g_TeBuilt[ hClass ].pTable,
	                   g_TeBuilt[ hClass ].pClass->m_ClassID, g_TeScratch, flDelay );
	return true;
}

// cs_sv_te_dump, the engine's view of every built table beside what C#
// declared, is a diagnostic: it lives in shell_tempents_debug.cpp, which only a
// debug build compiles.

//-----------------------------------------------------------------------------
// Wired once, from the gamesystem, so the registry knows who else owns a table
// name. Separated from the first registration because the resolver has to be in
// place before anything can be refused for the lack of it.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntInit( void )
{
	g_TeRegistry.SetExternalBaseResolver( TeBaseExists, NULL );
}

//-----------------------------------------------------------------------------
// The other half of SourceSharp_IsManagedServerClass (plan_gamedll.md Part 5's
// meter). Every ServerClass built here exists because a C# [TempEntity]
// declared it, so it belongs in the managed column.
//
// By identity against g_TeBuilt rather than by name: the class name comes from
// the declaration, and a rename would otherwise move an entry from one column
// of the meter to the other with nothing said.
//-----------------------------------------------------------------------------
bool SourceSharp_IsTempEntServerClass( const ServerClass *pClass )
{
	if ( pClass == NULL )
		return false;

	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( g_TeBuilt[ i ].pClass == pClass )
			return true;
	}

	return false;
}
