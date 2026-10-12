//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — temp entities, received into a description =========//
// SS_REASON b  an interface resolved by name at load
//
// plan_tmp.md Part 3, the client half. The mirror of server/shell_tempents.cpp,
// and it is deliberately its reflection rather than a second design: the two
// files walk the SAME plan, out of the same description, published from one
// TeClass in SourceSharp.Shared. That is the whole point — a schema with one
// declaration physically cannot drift between the sides, and a prop one bit
// narrower than the other side expects is a bitstream that stops making sense
// partway through with no log line anywhere.
//
// WHAT A TEMP ENTITY IS ON THIS SIDE. The engine parses an event, finds the
// ClientClass whose id it carries, calls its CreateEventFn, decodes the props
// into whatever GetDataTableBasePtr() answers, and calls PostDataUpdate. Stock
// spends a C++ class per effect on that — 44 of them, each with its members at
// offsets RECVINFO computed. Here the base pointer is a BYTE BLOB, the props
// read at the offsets C# computed, and PostDataUpdate hands the blob to managed
// code. One shell serves every effect and knows nothing about any of them.
//
// THE SHAPES ARE shell_standalone_client.cpp's and shell_recvtable.cpp's, and
// not a third one. c_basetempentity.cpp is the other half of the model: the
// IClientUnknown/IClientNetworkable husk below is its husk, with the asserts
// kept where they are stock's own refusals.
//
// THE ONE COLLISION THAT HANGS A JOIN, and its asymmetry (plan 2E). Two
// ClientClasses of one name is refused here. But note which way round the
// danger runs: a ServerClass with no receiver makes the client begin
// instantiating and stop with nothing in any log, while a ClientClass with no
// sender is harmless — this build already ships two. So the client is the safer
// side to be wrong on, which is not an excuse to be.
//
//=============================================================================//

#include "cbase.h"
#include "tempent_schema.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE6: IManagedTempEntsClient
#include "dt_recv.h"
#include "client_class.h"
#include "iclientnetworkable.h"
#include "iclientunknown.h"
#include "coordsize.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//=============================================================================//
// THE FLAG VALUES ARE SPROP_*'s OWN, AND THIS IS THE CLIENT'S HALF OF THAT
// CHECK.
//
// The server asserts the same set in shell_tempents.cpp. Asserted on BOTH sides
// rather than once, because the two DLLs are compiled separately and a build
// where only one of them saw a changed dt_common.h is exactly the state where a
// flag means one thing to the sender and another to the receiver. The memory
// this tree keeps writing is "check BOTH headers"; this is the same rule for a
// pair of translation units.
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

COMPILE_TIME_ASSERT( SOURCESHARP_TE_MAX_EDICT_BITS     == MAX_EDICT_BITS );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_MODEL_INDEX_BITS   == SP_MODEL_INDEX_BITS );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_TE_EF_MAX_BITS  == (int)EF_MAX_BITS );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_EHANDLE_BITS       == NUM_NETWORKED_EHANDLE_BITS );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_MAX_PLAYERS        == MAX_PLAYERS );
COMPILE_TIME_ASSERT( SOURCESHARP_TE_COORD_INTEGER_BITS == COORD_INTEGER_BITS );

//=============================================================================//
// THE RECEIVING SHELL.
//
// One per registered class that claims a ClientClass. It is a SINGLETON, which
// is stock's arrangement and not a shortcut: IMPLEMENT_CLIENTCLASS_EVENT hands
// the engine a CreateEventFn returning a file-static instance, and the engine
// deltas consecutive events of one class against each other, so the object has
// to hold the previous event's values for the delta to be applied to.
//
// WHICH IS WHY THE BLOB IS NOT CLEARED between events, and that is worth saying
// out loud because clearing it is the obvious-looking safety measure and would
// silently corrupt every event after the first: an unchanged prop is not sent
// at all, and a cleared blob would read it as zero -- a spark at the world
// origin rather than where it happened.
//
// It is NOT a C_BaseTempEntity. That class links itself into a static list the
// stock precache walk reads, and this shell has nothing to precache -- the
// managed side owns what a ported effect needs (plan 3.6). Its husk is copied
// because the ENGINE demands those methods, not because the list is wanted.
//=============================================================================//
class CSourceSharpTempEnt : public IClientUnknown, public IClientNetworkable
{
public:
	CSourceSharpTempEnt( const char *pClassName, int hClass, int nBlobBytes )
		: m_pClassName( pClassName ), m_hClass( hClass ), m_nBlobBytes( nBlobBytes )
	{
		// Zeroed ONCE, at construction, so the first event of the class reads
		// zeroes for anything it does not send rather than whatever the
		// allocator left. After that the blob is the previous event, on purpose.
		m_pBlob = new unsigned char[ nBlobBytes > 0 ? nBlobBytes : 1 ];
		Q_memset( m_pBlob, 0, nBlobBytes > 0 ? nBlobBytes : 1 );
	}

	// IClientUnknown. The husk, from c_basetempentity.h -- these exist because
	// the interface demands them and a temp entity is none of the things they
	// ask about.
	virtual void SetRefEHandle( const CBaseHandle & )   { Assert( false ); }

	// A static rather than stock's `*((CBaseHandle*)0)`. Stock's line
	// dereferences a null to make a reference and is undefined the moment it
	// runs; nothing calls it, which is why it has survived. Same answer, and it
	// cannot fault if something ever does.
	virtual const CBaseHandle &GetRefEHandle() const
	{
		static CBaseHandle s_Invalid;
		return s_Invalid;
	}

	virtual IClientUnknown     *GetIClientUnknown()     { return this; }
	virtual ICollideable       *GetCollideable()        { return 0; }
	virtual IClientNetworkable *GetClientNetworkable()  { return this; }
	virtual IClientRenderable  *GetClientRenderable()   { return 0; }
	virtual IClientEntity      *GetIClientEntity()      { return 0; }
	virtual IGameEntity       *GetBaseEntity()         { return 0; }
	virtual IClientThinkable   *GetClientThinkable()    { return 0; }

	// IClientNetworkable.
	//
	// GetClientClass() is asked by the engine AFTER the create function
	// answered, so the class is set on the shell rather than passed to its
	// constructor: the ClientClass cannot exist until the shell does, because
	// its create function returns the shell.
	virtual ClientClass *GetClientClass() { return m_pClientClass; }
	void SetClientClass( ClientClass *pClass ) { m_pClientClass = pClass; }

	virtual void Release()
	{
		// Stock asserts here and so does this: the engine only releases things
		// it created, and an event singleton is created once by this file.
		Assert( !"CSourceSharpTempEnt::Release should never be called" );
	}

	virtual void NotifyShouldTransmit( ShouldTransmitState_t ) {}
	virtual void PreDataUpdate( DataUpdateType_t )             {}
	virtual void OnDataUnchangedInPVS()                        {}
	virtual void OnPreDataChanged( DataUpdateType_t )          { Assert( 0 ); }
	virtual void OnDataChanged( DataUpdateType_t )             { Assert( 0 ); }
	virtual bool IsDormant()                                   { Assert( 0 ); return false; }
	virtual int  entindex() const                              { Assert( 0 ); return 0; }
	virtual void ReceiveMessage( int, bf_read & )              { Assert( 0 ); }
	virtual void SetDestroyedOnRecreateEntities()              { Assert( 0 ); }

	// THE ONE LINE THAT MAKES THIS DESIGN WORK. Stock answers `this` and its
	// recv props read at offsets into the C++ object; this answers the BLOB and
	// they read at the offsets C# computed. The engine never asks what it is.
	virtual void *GetDataTableBasePtr() { return m_pBlob; }

	// AND THE OTHER ONE. Everything above is the husk; this is the effect.
	virtual void PostDataUpdate( DataUpdateType_t )
	{
		IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

		if ( !pTempEnts )
		{
			// Once per class, not once per event: a firefight would fill the
			// console, and never saying it would make a whole effect disappear
			// silently -- which is precisely the failure this port is scored
			// against.
			if ( !m_bComplained )
			{
				m_bComplained = true;
				Warning( "[SourceSharp] temp entity '%s' arrived and managed code is not "
				         "listening; nothing will play it\n", m_pClassName );
			}
			return;
		}

		pTempEnts->OnTempEntity( m_hClass, m_pBlob, m_nBlobBytes );
	}

	// For the LOCAL path: a client that makes its own effect (c_te.cpp's
	// CTempEntsSystem, the tool-recording playback) writes this blob and plays
	// it through the same managed code a networked one reaches. One description
	// of what a spark does on this client, rather than two.
	unsigned char *Blob()      { return m_pBlob; }
	int            BlobBytes() const { return m_nBlobBytes; }

private:
	const char    *m_pClassName;
	int            m_hClass;
	int            m_nBlobBytes;
	unsigned char *m_pBlob;
	ClientClass   *m_pClientClass = NULL;
	bool           m_bComplained = false;
};

//=============================================================================//
// THE CREATE FUNCTIONS.
//
// CreateEventFn carries NO CONTEXT -- it is `IClientNetworkable *(*)()` -- so
// the handle has to be baked into the function itself, one function per handle.
//
// WRITTEN OUT AT THE REGISTRY'S OWN BOUND, and that is the whole of what this
// tree learned the hard way. shell_standalone_client.cpp records it: its
// predecessor kept a POOL of trampolines carrying a class index, the pool ran
// out at eight twice, and the join then died naming an innocent class. A table
// sized SOURCESHARP_TE_MAX_CLASSES cannot run out, because a handle past that
// bound is a handle the registry refused to issue.
//
// Generated rather than typed: sixty-four hand-written copies of one line is
// sixty-four chances to paste the wrong index, and the one that matters is
// never the one that gets read.
//=============================================================================//

//-----------------------------------------------------------------------------
// One registered class's built parts. Leaked on purpose, exactly as the
// server's are: a RecvTable is read by the engine for the life of the process
// and there is no point at which these could be freed and nothing would notice.
//-----------------------------------------------------------------------------
struct SourceSharpTeBuiltClient_t
{
	RecvProp            *pProps;
	int                  nProps;
	RecvTable           *pTable;
	ClientClass         *pClass;   // NULL for a table registered without one
	CSourceSharpTempEnt *pShell;   // NULL likewise: no class, nothing to create
};

static CSourceSharpTeRegistry     g_TeRegistry;
static int                        g_nTeShadowed = 0;
static SourceSharpTeBuiltClient_t g_TeBuilt[ SOURCESHARP_TE_MAX_CLASSES ];

template < int N >
static IClientNetworkable *TeCreateEvent()
{
	return g_TeBuilt[ N ].pShell;
}

template < int N >
struct CTeCreateEventTable
{
	static void Fill( CreateEventFn *pOut )
	{
		CTeCreateEventTable< N - 1 >::Fill( pOut );
		pOut[ N - 1 ] = &TeCreateEvent< N - 1 >;
	}
};

template <>
struct CTeCreateEventTable< 0 >
{
	static void Fill( CreateEventFn * ) {}
};

static CreateEventFn TeCreateEventFor( int iHandle )
{
	static CreateEventFn s_Fns[ SOURCESHARP_TE_MAX_CLASSES ];
	static bool s_bFilled = false;

	if ( !s_bFilled )
	{
		s_bFilled = true;
		CTeCreateEventTable< SOURCESHARP_TE_MAX_CLASSES >::Fill( s_Fns );
	}

	return ( iHandle >= 0 && iHandle < SOURCESHARP_TE_MAX_CLASSES ) ? s_Fns[ iHandle ] : NULL;
}

//=============================================================================//
// FINDING A TABLE SOMEBODY ELSE STILL OWNS.
//
// THE HOOK THAT MAKES A STAGED PORT POSSIBLE, and it is the server's argument
// verbatim: a lane lands DT_TESparks while DT_TEParticleSystem is still stock's,
// and for the length of this port that is the ordinary state rather than an
// error.
//
// Walks g_pClientClassHead and recurses into each table's datatable props,
// because an INTERMEDIATE table (DT_TEParticleSystem, DT_BaseBeam) is reachable
// only through the class that nests it.
//
// PROVED ON A LIVE JOIN BY LANE CD, C-Phase 3, and worth recording here because
// this is where the next lane will come to ask. The question was whether a
// MANAGED concrete ClientClass may nest a base table a NATIVE RecvTable still
// owns. It may. DT_TEBSPDecal, DT_TEFootprintDecal and DT_TEPlayerDecal are
// managed with c_te_bspdecal.cpp, c_te_footprint.cpp and c_te_playerdecal.cpp
// evicted, while every OTHER user of DT_BaseTempEntity is still native -- and
// all three of their "baseclass" props point at STOCK'S table, found right
// here.
//
// 2E-bis' refusal is about two RecvTables of ONE NAME, which this is not:
// there is still exactly one DT_BaseTempEntity in the process, with the same
// parents it always had -- three of them are simply managed now. One table
// with many parents is what stock already does; c_basetempentity.cpp's is
// nested by every temp entity in the build.
//
// THE ORDER THAT MAKES IT WORK, since it is not obvious: the managed
// CBaseTempEntity IS in g_TeRegistry, so FindByTableName finds it -- but
// SourceSharp_TempEntRegisterClient refused to BUILD it (FindStockTableNamed
// saw the native table) and returned before assigning pTable. The
// `g_TeBuilt[iOurs].pTable` test below is therefore false, and the walk runs.
// Bases are registered before the classes that nest them, so that refusal has
// always already happened by the time a derived class asks.
//
// SO A BASE'S USERS CAN BE SPLIT ACROSS LANES, which is what this was asked
// to decide: DT_BaseTempEntity's do not have to move in one lane. The base
// itself still moves last, when the last of them has gone.
//=============================================================================//
static RecvTable *FindNativeTableIn( RecvTable *pTable, const char *pName, int nDepth )
{
	if ( !pTable || nDepth > 8 )
		return NULL;

	if ( pTable->GetName() && !Q_strcmp( pTable->GetName(), pName ) )
		return pTable;

	for ( int i = 0; i < pTable->GetNumProps(); i++ )
	{
		RecvProp *pProp = pTable->GetProp( i );

		if ( pProp->m_RecvType != DPT_DataTable )
			continue;

		RecvTable *pFound = FindNativeTableIn( pProp->GetDataTable(), pName, nDepth + 1 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

static RecvTable *FindRegisteredTable( const char *pName )
{
	if ( !pName || !pName[0] )
		return NULL;

	// OURS FIRST, and it is the same reason the server gives: when both sides of
	// a name exist -- which is what a half-ported base table looks like -- the
	// MANAGED one is the one whose offsets address the blob, so nesting the
	// native one would decode a coordinate into whatever the blob has at
	// offsetof( C_TEParticleSystem, m_vecOrigin ).
	const int iOurs = g_TeRegistry.FindByTableName( pName );

	if ( iOurs >= 0 && g_TeBuilt[ iOurs ].pTable )
		return g_TeBuilt[ iOurs ].pTable;

	for ( ClientClass *pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext )
	{
		RecvTable *pFound = FindNativeTableIn( pClass->m_pRecvTable, pName, 0 );

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
// Whether a table is one this file built.
//-----------------------------------------------------------------------------
static bool IsOurTable( const RecvTable *pTable )
{
	for ( int i = 0; i < SOURCESHARP_TE_MAX_CLASSES; i++ )
	{
		if ( g_TeBuilt[i].pTable == pTable )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// A table of this name that STOCK still registers -- never one of ours.
//
// THE SECOND MEASUREMENT, and the client is the only side that has one. Both
// columns of cs_sv_te_dump come, in the end, from the same C# declaration: the
// engine transforms it (it flips a negated offset and sets a flag, and
// SendPropFloat widens an unspecified width to NOSCALE) but it does not
// independently KNOW what DT_TESparks should contain, so a width that is wrong
// and consistent is invisible to it. This is a genuinely different source: a
// prop list Valve typed into c_te_*.cpp, in a file this port has not touched,
// still registered while the eviction is staged.
//
// It cannot see a WIDTH either -- a RecvProp carries none, because widths
// arrive with the server's SendTable at connect. What it CAN see is every other
// way a table goes wrong: a prop renamed, a prop missing, a prop in the wrong
// position, a prop of the wrong type, or a base that resolved to a different
// table than stock's does. Those are what "the two sides pair by string" means,
// and they are exactly what produces a join that stops with nothing in any log.
//-----------------------------------------------------------------------------
static RecvTable *FindStockTableIn( RecvTable *pTable, const char *pName, int nDepth )
{
	if ( !pTable || nDepth > 8 )
		return NULL;

	if ( !IsOurTable( pTable ) && pTable->GetName()
	  && !Q_strcmp( pTable->GetName(), pName ) )
		return pTable;

	for ( int i = 0; i < pTable->GetNumProps(); i++ )
	{
		RecvProp *pProp = pTable->GetProp( i );

		if ( pProp->m_RecvType != DPT_DataTable )
			continue;

		RecvTable *pFound = FindStockTableIn( pProp->GetDataTable(), pName, nDepth + 1 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

static RecvTable *FindStockTableNamed( const char *pName )
{
	if ( !pName || !pName[0] )
		return NULL;

	for ( ClientClass *pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext )
	{
		RecvTable *pFound = FindStockTableIn( pClass->m_pRecvTable, pName, 0 );

		if ( pFound )
			return pFound;
	}

	return NULL;
}

//-----------------------------------------------------------------------------
// Whether a ClientClass of this name is already in the list the engine
// negotiates at connect.
//
// Refused HERE, where there is a console to say so on. Two of a name is the
// collision with no symptom -- and on this side there is a second reason to
// refuse rather than shrug: the two would carry DIFFERENT RecvTables for one
// class id, and which of them the engine picks is link order.
//-----------------------------------------------------------------------------
static bool ClientClassExists( const char *pName )
{
	for ( ClientClass *pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext )
	{
		if ( pClass->GetName() && !Q_strcmp( pClass->GetName(), pName ) )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// The name a prop is declared under, interned for the life of the process --
// a RecvProp keeps the pointer it is handed, exactly as a SendProp does.
//
// The DERIVATION is SourceSharpTe_PropName's, in the shared header, because the
// server derives the same one and the two sides pair props BY STRING. What is
// left here is only the interning, which is per-side.
//-----------------------------------------------------------------------------
static CUtlVector< char * > g_TePropNames;

static const char *InternPropName( const char *pName )
{
	for ( int i = 0; i < g_TePropNames.Count(); i++ )
	{
		if ( !Q_strcmp( g_TePropNames[i], pName ) )
			return g_TePropNames[i];
	}

	char *pCopy = new char[ Q_strlen( pName ) + 1 ];
	Q_strncpy( pCopy, pName, Q_strlen( pName ) + 1 );
	g_TePropNames.AddToTail( pCopy );

	return pCopy;
}

//-----------------------------------------------------------------------------
// One RecvProp, from one entry of the shared plan.
//
// nOffset AND NOT nDeclareOffset, which is the one asymmetry between the two
// builders and the one worth stating: SENDINFO_VECTORELEM negates so
// SendTable_InitTable can detect it and set SPROP_IS_A_VECTOR_ELEM, while
// RECVINFO( m_vecOrigin[0] ) is a plain offsetof and the recv side never
// negates anything (c_te_particlesystem.cpp:72 is the stock table this one
// pairs with). A client declaring with the negated offset would decode into
// memory in front of the blob.
//
// EVERY CUSTOM PROXY IS THE CLIENT'S OWN BUSINESS (plan 2B): a recv proxy never
// appears in the handshake, so the plain forms here pair with stock's
// hand-written tables exactly. The two ANGLE kinds collapse into their float
// and vector forms for the same reason -- SendProxy_AngleToFloat runs on the
// way OUT, and RecvPropQAngles is #defined to RecvPropVector in dt_recv.h:377.
//-----------------------------------------------------------------------------
static bool BuildOneProp( const SourceSharpTePropPlan_t *pPlan, RecvProp *pOut )
{
	// THE RECV NAME, NOT THE PLAN'S. They differ for exactly one prop shape --
	// the element template of an array, which stock declares as
	// RecvPropVector( RECVINFO(m_vecPoints[0]) ) against the server's
	// SendPropVector( SENDINFO_ARRAY(m_vecPoints), ... ). The derivation lives
	// in the shared header beside the flag that drives it, because the two
	// sides pair props by string.
	//
	// GETTING THIS WRONG DOES NOT DEGRADE AN EFFECT, IT REFUSES THE JOIN: with
	// the template also called m_vecPoints, the recv side's by-name lookup for
	// the DPT_Array prop finds the DPT_Vector template first and CreateDecoders
	// fails, taking the connection with it. Lane CN saw exactly that.
	char szRecvName[ SOURCESHARP_TE_PROP_NAME_MAX ];

	SourceSharpTe_RecvPropName( szRecvName, (int)sizeof( szRecvName ), pPlan );

	const char *pName = InternPropName( szRecvName );

	switch ( pPlan->nKind )
	{
	case SOURCESHARP_TE_PROP_INT:
		// The storage width picks the write proxy (dt_recv.cpp:202): 1 selects
		// RecvProxy_Int32ToInt8, 4 selects RecvProxy_Int32ToInt32. A byte field
		// described as four writes three bytes of the next field.
		*pOut = RecvPropInt( pName, pPlan->nOffset, pPlan->nSizeBytes, pPlan->nFlags );
		return true;

	case SOURCESHARP_TE_PROP_FLOAT:
	case SOURCESHARP_TE_PROP_ANGLE:
		*pOut = RecvPropFloat( pName, pPlan->nOffset, pPlan->nSizeBytes, pPlan->nFlags );
		return true;

	case SOURCESHARP_TE_PROP_VECTOR:
	case SOURCESHARP_TE_PROP_QANGLES:
		*pOut = RecvPropVector( pName, pPlan->nOffset, pPlan->nSizeBytes, pPlan->nFlags );
		return true;

	case SOURCESHARP_TE_PROP_DATATABLE:
		{
			RecvTable *pNested = FindRegisteredTable( pPlan->pSubTable );

			if ( !pNested )
				return false;

			*pOut = RecvPropDataTable( pName, pPlan->nOffset, pPlan->nFlags, pNested,
			                           DataTableRecvProxy_StaticDataTable );
			return true;
		}

	// The DPT_Array half of the pair. m_pArrayProp is left NULL on both sides
	// and the engine links it to the preceding prop -- emit both, link neither
	// (plan 3.3).
	case SOURCESHARP_TE_PROP_ARRAY:
		*pOut = InternalRecvPropArray( pPlan->nElems, pPlan->nElemStride, pName, NULL );
		return true;
	}

	return false;
}

//=============================================================================//
// REGISTRATION.
//
// The client's half of the SAME ABI slot the server fills (plan 3.5): one
// TempEntRegister, two implementations, one description. The managed side calls
// it identically on both sides and never learns which one answered.
//=============================================================================//
int32_t SourceSharp_TempEntRegisterClient( const SourceSharpTeClass_t *pDesc )
{
	if ( !pDesc || !pDesc->pClassName || !pDesc->pTableName )
	{
		Warning( "[SourceSharp] temp entity registration with no description\n" );
		return -1;
	}

	// A MANAGED RELOAD ASKS AGAIN, and the answer is the handle it had before.
	// The server's reasoning applies unchanged: the tables and the ClientClasses
	// are process-lifetime by construction -- the engine walked the class list
	// once, at DLL init -- so a reload cannot build new ones and must not try.
	// What the new assembly needs back is the HANDLE.
	const int iExisting = g_TeRegistry.FindByClassName( pDesc->pClassName );

	if ( iExisting >= 0 )
	{
		const SourceSharpTeClass_t *pHad = g_TeRegistry.At( iExisting );

		if ( Q_strcmp( pHad->pTableName, pDesc->pTableName )
		  || pHad->nBlobBytes != pDesc->nBlobBytes
		  || pHad->nFieldCount != pDesc->nFieldCount )
		{
			Warning( "[SourceSharp] temp entity '%s' was RESHAPED across a reload "
			         "(table '%s'->'%s', %d->%d blob bytes, %d->%d field(s)). The table on "
			         "the wire is the one built at DLL init and cannot change without a "
			         "restart; the old shape is what will be decoded.\n",
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
		Warning( "[SourceSharp] temp entity '%s' (%s) NOT registered on the client: %s\n",
			pDesc->pClassName, pDesc->pTableName, SourceSharpTe_RefusalText( nWhy ) );
		return -1;
	}

	const SourceSharpTeClass_t *pClass = g_TeRegistry.At( iHandle );

	// COUNTED RATHER THAN GUESSED, by the same function that builds the plan: a
	// short array here is a buffer overrun into whatever the allocator put next,
	// and every prop past the end still builds.
	const int nWanted = SourceSharpTe_PlanCount( pClass );

	if ( nWanted < 0 )
	{
		Warning( "[SourceSharp] temp entity '%s': its description cannot be counted; "
		         "NOT registered\n", pClass->pClassName );
		return -1;
	}

	SourceSharpTePropPlan_t *pPlan = new SourceSharpTePropPlan_t[ nWanted > 0 ? nWanted : 1 ];
	const int nPlanned = SourceSharpTe_PlanProps( pClass, pPlan, nWanted );

	if ( nPlanned != nWanted )
	{
		Warning( "[SourceSharp] temp entity '%s': planned %d prop(s) where %d were counted; "
		         "NOT registered\n", pClass->pClassName, nPlanned, nWanted );
		delete[] pPlan;
		return -1;
	}

	RecvProp *pProps = new RecvProp[ nWanted > 0 ? nWanted : 1 ];
	int nBuilt = 0;

	for ( int i = 0; i < nPlanned; i++ )
	{
		if ( !BuildOneProp( &pPlan[i], &pProps[ nBuilt ] ) )
		{
			Warning( "[SourceSharp] temp entity '%s': prop '%s' could not be built as a "
			         "RecvProp; NOT registered\n", pClass->pClassName, pPlan[i].szName );
			delete[] pPlan;
			delete[] pProps;
			return -1;
		}

		// THE OFFSET-ZERO NESTED TABLE, REFUSED. This side cannot SEE
		// SPROP_COLLAPSIBLE -- a RecvProp carries only the flags it was handed,
		// and the flag is set on the SEND prop (dt_send.cpp:648) -- but it can
		// see the condition, because both sides build from one description: a
		// nested table at offset zero with the identity proxy is what makes the
		// sender collapse it.
		//
		// AND THE RECEIVER IS THE SIDE THAT DIES. A collapsed datatable is
		// applied at the OBJECT BASE, so the nested table's props write over
		// offset 0 of whatever GetDataTableBasePtr answered. Lane S4 found it
		// with DT_EffectData against a native shell, where offset 0 is the
		// vtable pointer: a clean join and a segfault on the first dispatch.
		// Here the base pointer is a blob, so it would not fault -- it would
		// decode the nested table over the outer one's first field and play a
		// plausible wrong effect, which is worse.
		//
		// THE "baseclass" PROP IS EXEMPT AND MUST BE: it is at offset zero on
		// purpose, stock sets the same flag on its own, and collapsing it is
		// exactly right -- a base's props address the same blob from the same
		// place.
		if ( pPlan[i].nKind == SOURCESHARP_TE_PROP_DATATABLE && pPlan[i].iField >= 0
		  && pPlan[i].nOffset == 0 )
		{
			Warning( "[SourceSharp] temp entity '%s': nested table prop '%s' is at offset 0, "
			         "which makes the SENDER mark it SPROP_COLLAPSIBLE and the receiver apply "
			         "it at the object base. NOT registered -- it would decode the nested "
			         "table over this class's own first field. The nested table needs a "
			         "non-zero offset; see TeClassBuilder's pad.\n",
				pClass->pClassName, pPlan[i].szName );
			delete[] pPlan;
			delete[] pProps;
			return -1;
		}

		nBuilt++;
	}

	delete[] pPlan;

	g_TeBuilt[ iHandle ].pProps = pProps;
	g_TeBuilt[ iHandle ].nProps = nBuilt;
	// THE REFUSAL THAT MAKES THE OTHER FORTY-TWO SAFE.
	//
	// The client resolves a nested data table BY NAME and cannot hold two of
	// one, so a managed table whose name a native RecvTable already carries is
	// NOT registered. See tempent_schema.h for the two live failures the rule is
	// made of -- a segfault in CreateDecoders one way round, and a base table
	// decoding into bytes past the end of the blob the other.
	//
	// A CONSOLE LINE PER CLASS AND ONE SUMMARY, deliberately. This is the
	// ordinary state of a staged port -- forty-two of the forty-three tables are
	// in it while their c_te_*.cpp are still compiled -- so it must be visible
	// without being a wall: an effect that silently never plays is exactly what
	// this port is scored against, and so is a startup nobody reads.
	if ( FindStockTableNamed( pClass->pTableName ) )
	{
		Msg( "[SourceSharp] temp entity '%s': a native RecvTable is still named '%s', so the "
		     "managed one is NOT registered. Evict its c_te_*.cpp to port it.\n",
			pClass->pClassName, pClass->pTableName );

		g_nTeShadowed++;
		delete[] pProps;
		return -1;
	}

	g_TeBuilt[ iHandle ].pTable = new RecvTable( pProps, nBuilt, pClass->pTableName );
	g_TeBuilt[ iHandle ].pClass = NULL;
	g_TeBuilt[ iHandle ].pShell = NULL;

	if ( pClass->nRegisterClass )
	{
		if ( ClientClassExists( pClass->pClassName ) )
		{
			Warning( "[SourceSharp] temp entity '%s' NOT registered as a ClientClass: the "
			         "native build still registers one of that name. Evict its c_te_*.cpp "
			         "from client_csharp.vpc, or register this as a base table. Two classes "
			         "of one name carry two RecvTables for one class id and which one the "
			         "engine picks is link order.\n", pClass->pClassName );
		}
		else
		{
			g_TeBuilt[ iHandle ].pShell =
				new CSourceSharpTempEnt( pClass->pClassName, iHandle, pClass->nBlobBytes );

			// A CreateEventFn and NOT a CreateClientClassFn. The engine treats a
			// class with the event function set as a one-shot: it never gets an
			// entity index, is never in the entity list, and is not released
			// afterwards -- which is why the shell is a singleton and why
			// Release() asserts.
			g_TeBuilt[ iHandle ].pClass = new ClientClass(
				pClass->pClassName, NULL, TeCreateEventFor( iHandle ),
				g_TeBuilt[ iHandle ].pTable );

			g_TeBuilt[ iHandle ].pShell->SetClientClass( g_TeBuilt[ iHandle ].pClass );

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
// Shuts the window, and reports what got a ClientClass.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntClientCloseRegistration( void )
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

	Msg( "[SourceSharp] temp entities: %d description(s), %d ClientClass(es), "
	     "%d still owned by a native table; registration closed\n",
		g_TeRegistry.Count(), nClasses, g_nTeShadowed );

	// THE NUMBER THAT SHOULD FALL TO ZERO, said out loud once so it is a
	// measurement rather than a mood. Every one of these is a table whose
	// c_te_*.cpp is still compiled; porting it is evicting that file, and the
	// count is how far C-Phase 3 has to go. It is not an error -- it is the
	// ordinary state of a staged port and the reason the refusal exists.
	if ( g_nTeShadowed > 0 )
	{
		Msg( "[SourceSharp]     %d of them wait on a c_te_*.cpp eviction; see "
		     "tempent_schema.h for why a base table waits on ALL of its users\n",
			g_nTeShadowed );
	}
}

//-----------------------------------------------------------------------------
// Wired once, from the gamesystem, so the registry knows who else owns a table
// name -- before anything can be refused for the lack of one.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntClientInit( void )
{
	g_TeRegistry.SetExternalBaseResolver( TeBaseExists, NULL );
}

//=============================================================================//
// THE LOCAL PATH — a client making its own effect.
//
// c_te.cpp's CTempEntsSystem calls TE_Sparks() and friends directly: a
// tool-recording playback, and anything client-side that wants an effect
// without a round trip. Those go through the SAME managed play code a networked
// event reaches, by writing this class's blob and handing it over -- so there is
// ONE description of what a spark does on this client rather than two that
// agree until somebody changes one.
//
// The generic writer is the mirror of the server's, name for name, and needs no
// per-TE knowledge: the field list per class is already here.
//
// ONE SCRATCH BLOB, and it is not a limitation: this runs on the client's own
// thread inside one call, between Begin and Play, and nothing yields between.
//=============================================================================//

static unsigned char g_TeScratch[ 512 ];
static int           g_iTeScratchClass = -1;

//-----------------------------------------------------------------------------
// A field by name, in this class or anywhere up its MANAGED base chain.
//
// A base still owned by stock has no description here, so a caller naming one
// of its fields is refused rather than writing at a guessed offset -- the same
// answer the registrar gives, for the same reason.
//-----------------------------------------------------------------------------
static const SourceSharpTeField_t *FindField( int hClass, const char *pName, int *pOutBlobBytes )
{
	int nGuard = 0;

	for ( int i = hClass; i >= 0 && nGuard < SOURCESHARP_TE_MAX_CLASSES; nGuard++ )
	{
		const SourceSharpTeClass_t *pClass = g_TeRegistry.At( i );

		if ( !pClass )
			return NULL;

		if ( i == hClass && pOutBlobBytes )
			*pOutBlobBytes = pClass->nBlobBytes;

		for ( int f = 0; f < pClass->nFieldCount; f++ )
		{
			if ( !Q_strcmp( pClass->pFields[f].pName, pName ) )
				return &pClass->pFields[f];
		}

		i = g_TeRegistry.FindByTableName( pClass->pBaseTable );
	}

	return NULL;
}

int SourceSharpTe_HandleForClass( const char *pClassName )
{
	return g_TeRegistry.FindByClassName( pClassName );
}

bool SourceSharpTe_Begin( int hClass )
{
	const SourceSharpTeClass_t *pClass = g_TeRegistry.At( hClass );

	if ( !pClass || pClass->nBlobBytes > (int)sizeof( g_TeScratch ) )
	{
		g_iTeScratchClass = -1;
		return false;
	}

	// ZEROED, so a field the caller forgets is the default rather than the last
	// effect's value. A stale coordinate is a plausible one. (The RECEIVING
	// blob is deliberately the opposite -- see CSourceSharpTempEnt -- because
	// there the previous event is what the delta is applied to.)
	Q_memset( g_TeScratch, 0, sizeof( g_TeScratch ) );
	g_iTeScratchClass = hClass;

	return true;
}

static unsigned char *ScratchAt( const char *pName, int nWantBytes )
{
	if ( g_iTeScratchClass < 0 )
		return NULL;

	int nBlobBytes = 0;
	const SourceSharpTeField_t *pField = FindField( g_iTeScratchClass, pName, &nBlobBytes );

	if ( !pField )
	{
		Warning( "[SourceSharp] temp entity forward: no field '%s' on this class or any "
		         "base it owns\n", pName );
		return NULL;
	}

	if ( pField->nOffset < 0 || pField->nOffset + nWantBytes > nBlobBytes )
		return NULL;

	return g_TeScratch + pField->nOffset;
}

bool SourceSharpTe_SetInt( const char *pName, int nValue )
{
	if ( g_iTeScratchClass < 0 )
		return false;

	int nBlobBytes = 0;
	const SourceSharpTeField_t *pField = FindField( g_iTeScratchClass, pName, &nBlobBytes );

	if ( !pField || pField->nOffset + pField->nSizeBytes > nBlobBytes )
		return false;

	// Written at the field's DECLARED storage width, because that is the width
	// managed code will read back through the same description.
	unsigned char *pAt = g_TeScratch + pField->nOffset;

	switch ( pField->nSizeBytes )
	{
	case 1: *(unsigned char *)pAt = (unsigned char)nValue; return true;
	case 2: *(short *)pAt         = (short)nValue;         return true;
	case 4: *(int *)pAt           = nValue;                return true;
	}

	return false;
}

bool SourceSharpTe_SetFloat( const char *pName, float flValue )
{
	unsigned char *pAt = ScratchAt( pName, (int)sizeof( float ) );

	if ( !pAt )
		return false;

	*(float *)pAt = flValue;
	return true;
}

bool SourceSharpTe_SetVector( const char *pName, const Vector &vec )
{
	unsigned char *pAt = ScratchAt( pName, 3 * (int)sizeof( float ) );

	if ( !pAt )
		return false;

	((float *)pAt)[0] = vec.x;
	((float *)pAt)[1] = vec.y;
	((float *)pAt)[2] = vec.z;

	return true;
}

//-----------------------------------------------------------------------------
// Plays what was just written, THROUGH MANAGED CODE. The client's counterpart
// of the server's Send, and the difference is the whole shape of the port: the
// server hands a blob to the engine, this hands one to C#.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// A FORWARD, WITH ITS ARGUMENTS AND NOTHING ELSE (tempent_forwards.h).
//
// The client's half, and simpler than the server's because the effect is LOCAL:
// there is no filter and nothing goes on the wire, so managed builds the
// declaration and plays it and no blob is built at any point. Passing a null
// blob buffer is what tells the managed side which of the two this is.
//-----------------------------------------------------------------------------
bool SourceSharpTe_Forward( int nId, const float *pFloats, int nFloats,
                            const int *pInts, int nInts )
{
	IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

	if ( !pTempEnts )
		return false;

	return pTempEnts->OnNativeTempEntity( nId, pFloats, nFloats, pInts, nInts,
	                                      NULL, 0, NULL ) >= 0;
}

bool SourceSharpTe_Play( void )
{
	const int hClass = g_iTeScratchClass;
	g_iTeScratchClass = -1;

	const SourceSharpTeClass_t *pClass = g_TeRegistry.At( hClass );

	if ( !pClass )
		return false;

	IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

	if ( !pTempEnts )
		return false;

	pTempEnts->OnTempEntity( hClass, g_TeScratch, pClass->nBlobBytes );
	return true;
}

//=============================================================================//
// THE DUMP — what the engine holds, beside what the plan says.
//
// cs_sv_payload_agree's rule applies verbatim, and cs_sv_te_dump's does: the
// left-hand column is pProp->GetOffset() AS THE ENGINE WILL USE IT and the
// right-hand one is the plan the table was BUILT from. Rederiving the offset
// here would compare a computation with itself and pass forever.
//
// AND ITS REAL PURPOSE IS THE OTHER DUMP. The `TE|` lines are the machine half:
// every field on them is comparable across the two sides and nothing else is,
// so `grep '^TE|'` on the two console logs is a diff rather than an eyeballing
// exercise. Two dumps agreeing field for field is the client/server schema
// check, and it needs no map.
//
// WHAT THE PAIR OF DUMPS STILL CANNOT SEE, said out loud because a gate nobody
// has priced is a gate somebody over-trusts. A WIDTH is declared exactly once,
// in C#, and both engines derive theirs from it — so a width that is wrong and
// CONSISTENT is invisible to a diff of the two. It is caught by three other
// things and they are named here so a reader can check they are still running:
// the parity half of TempEntSchemaTests, which reads te_sparks.cpp off disk;
// the TESTOCK lines below, which compare against a prop list Valve typed; and
// sv_debugtempentities' encoded bit count at gate 3, where the size is a number
// that can be predicted before it is measured.
//=============================================================================//

static const char *PropTypeName( int nType )
{
	switch ( nType )
	{
	case DPT_Int:       return "int";
	case DPT_Float:     return "float";
	case DPT_Vector:    return "vector";
	case DPT_VectorXY:  return "vectorxy";
	case DPT_String:    return "string";
	case DPT_Array:     return "array";
	case DPT_DataTable: return "datatable";
	}

	return "?";
}

static int CountTablesNamed( const char *pName )
{
	int nFound = 0;

	for ( ClientClass *pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext )
	{
		// Only the class's OWN table, not its nested ones: an intermediate table
		// is reached through several classes and counting it once per class
		// would report every base in the game as a duplicate.
		if ( pClass->m_pRecvTable && pClass->m_pRecvTable->GetName()
		  && !Q_strcmp( pClass->m_pRecvTable->GetName(), pName ) )
			nFound++;
	}

	return nFound;
}

CON_COMMAND( cs_cl_te_dump,
	"Every registered temp entity: its props as the ENGINE holds them, beside the plan they were built from" )
{
	const char *pWanted = ( args.ArgC() > 1 ) ? args[1] : NULL;

	Msg( "[SourceSharp] --- temp entities (client): %d description(s), registration %s ---\n",
		g_TeRegistry.Count(), g_TeRegistry.IsClosed() ? "closed" : "OPEN" );

	int nAgreed  = 0;
	int nChecked = 0;

	for ( int i = 0; i < g_TeRegistry.Count(); i++ )
	{
		const SourceSharpTeClass_t *pClass = g_TeRegistry.At( i );
		RecvTable *pTable = g_TeBuilt[i].pTable;

		if ( pWanted && Q_stricmp( pClass->pClassName, pWanted )
		             && Q_stricmp( pClass->pTableName, pWanted ) )
			continue;

		Msg( "%2d %-28s %-28s %s, %d blob byte(s), base %s\n",
			i, pClass->pClassName, pClass->pTableName,
			g_TeBuilt[i].pClass ? "ClientClass" : "table only",
			pClass->nBlobBytes,
			pClass->pBaseTable ? pClass->pBaseTable : "(none)" );

		if ( !pTable )
		{
			Warning( "     no table was built\n" );
			continue;
		}

		// THE BUILT NAME, which is what the engine holds and what the other side
		// resolves on. Asking about the declared one would count stock's copy as
		// a duplicate of a table that is deliberately no longer called that.
		const int nSameName = CountTablesNamed( pTable->GetName() );

		if ( nSameName > 1 )
		{
			Msg( "     NOTE: %d ClientClass(es) publish a table named '%s'. That is what a "
			     "half-ported base table looks like — stock's copy is still registered — and "
			     "it is only safe because the two are prop-for-prop identical.\n",
				nSameName, pTable->GetName() );
		}

		const int nPlanned = SourceSharpTe_PlanCount( pClass );
		SourceSharpTePropPlan_t *pPlan =
			new SourceSharpTePropPlan_t[ nPlanned > 0 ? nPlanned : 1 ];

		const int nHave = SourceSharpTe_PlanProps( pClass, pPlan, nPlanned );

		for ( int iProp = 0; iProp < nHave && iProp < pTable->GetNumProps(); iProp++ )
		{
			RecvProp *pProp = pTable->GetProp( iProp );

			// The engine's own view. GetOffset() is what the decoder will add to
			// the blob pointer. There is no m_nBits here and that is not an
			// omission: a RecvProp carries no width at all, because the widths
			// arrive with the server's SendTable at connect and the client
			// builds its decoder from those. Which is exactly why a diff of the
			// two dumps is the check and a client-only dump is not.
			const int nEngineOffset = pProp->GetOffset();

			// A DPT_Array PROP HAS NO OFFSET OF ITS OWN and must not be counted
			// as disagreeing about one. InternalRecvPropArray takes an element
			// count, a stride, a name and a length proxy -- no offset at all
			// (dt_recv.h:424) -- so the engine holds zero here while the plan
			// carries the field's real blob offset for the ELEMENT TEMPLATE to
			// use. The one array in all 44 tables printed
			// "*** OFFSET DISAGREES ***" on both sides for that reason, which is
			// worse than useless: a line every reader learns to ignore is a line
			// that hides the next real disagreement.
			const bool bOffsetAgrees =
				( pPlan[ iProp ].nKind == SOURCESHARP_TE_PROP_ARRAY )
				|| ( nEngineOffset == pPlan[ iProp ].nOffset );

			nChecked++;
			if ( bOffsetAgrees )
				nAgreed++;

			Msg( "     %-24s %-9s engine off %5d flags 0x%05x | "
			     "plan off %5d bits %2d flags 0x%05x%s\n",
				pProp->GetName(), PropTypeName( pProp->m_RecvType ),
				nEngineOffset, pProp->GetFlags(),
				pPlan[ iProp ].nOffset, pPlan[ iProp ].nBits, pPlan[ iProp ].nFlags,
				bOffsetAgrees ? "" : "   *** OFFSET DISAGREES ***" );

			// The client cannot read SPROP_COLLAPSIBLE off a RecvProp, so it
			// checks the CONDITION instead -- see the refusal at registration.
			// Printed even though registration refuses it, because a dump of a
			// build whose refusal was removed should still say what is wrong.
			if ( pProp->m_RecvType == DPT_DataTable && pPlan[ iProp ].iField >= 0
			  && pPlan[ iProp ].nOffset == 0 )
			{
				Warning( "     *** NESTED TABLE AT OFFSET 0: the sender will mark it "
				         "COLLAPSIBLE and this side will decode it at the blob base ***\n" );
			}

			Msg( "TE|%s|%d|%s|%s|%d|%d|%d|0x%x|%s\n",
				pTable->GetName(), iProp, pProp->GetName(),
				PropTypeName( pProp->m_RecvType ), nEngineOffset,
				pPlan[ iProp ].nOffset, pPlan[ iProp ].nBits, pPlan[ iProp ].nFlags,
				( pProp->m_RecvType == DPT_DataTable && pProp->GetDataTable()
				  && pProp->GetDataTable()->GetName() )
					? pProp->GetDataTable()->GetName() : "" );
		}

		if ( nHave != pTable->GetNumProps() )
		{
			Warning( "     the plan has %d prop(s) and the built table has %d\n",
				nHave, pTable->GetNumProps() );
		}

		delete[] pPlan;

		// AND AGAINST STOCK'S OWN TABLE, where stock still has one. See
		// FindStockTableNamed for why this is the only genuinely independent
		// reading either side can make: it is a prop list Valve typed, in a file
		// this port has not touched.
		RecvTable *pStock = FindStockTableNamed( pClass->pTableName );

		if ( !pStock )
		{
			Msg( "     no stock table of this name is registered, so there is nothing "
			     "independent to check the prop list against\n" );
			continue;
		}

		int nSame = 0;

		Msg( "     STOCK '%s' has %d prop(s) against this table's %d\n",
			pStock->GetName(), pStock->GetNumProps(), pTable->GetNumProps() );

		for ( int iProp = 0; iProp < pTable->GetNumProps(); iProp++ )
		{
			RecvProp *pMine = pTable->GetProp( iProp );

			if ( iProp >= pStock->GetNumProps() )
			{
				Warning( "     %-24s *** STOCK HAS NO PROP %d ***\n",
					pMine->GetName(), iProp );
				continue;
			}

			RecvProp *pTheirs = pStock->GetProp( iProp );

			const bool bNameAgrees = pMine->GetName() && pTheirs->GetName()
				&& !Q_strcmp( pMine->GetName(), pTheirs->GetName() );
			const bool bTypeAgrees = ( pMine->m_RecvType == pTheirs->m_RecvType );

			if ( bNameAgrees && bTypeAgrees )
			{
				nSame++;
				continue;
			}

			// The offsets are EXPECTED to differ and are not compared: stock's
			// address a C++ object and these address a blob. Everything that has
			// to match does.
			Warning( "     prop %d: this table has '%s' (%s), stock has '%s' (%s)"
			         "   *** DISAGREES ***\n",
				iProp, pMine->GetName(), PropTypeName( pMine->m_RecvType ),
				pTheirs->GetName(), PropTypeName( pTheirs->m_RecvType ) );
		}

		if ( pStock->GetNumProps() > pTable->GetNumProps() )
		{
			for ( int iProp = pTable->GetNumProps(); iProp < pStock->GetNumProps(); iProp++ )
			{
				Warning( "     *** STOCK HAS '%s' AND THIS TABLE DOES NOT ***\n",
					pStock->GetProp( iProp )->GetName() );
			}
		}

		Msg( "TESTOCK|%s|%d|%d|%d\n", pTable->GetName(),
			pTable->GetNumProps(), pStock->GetNumProps(), nSame );
	}

	Msg( "[SourceSharp] --- %d of %d prop offset(s) agree with the plan ---\n",
		nAgreed, nChecked );

	if ( !g_TeRegistry.IsClosed() )
		Warning( "     registration is still OPEN, which it should not be after DLL init\n" );
}
