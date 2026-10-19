//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — temp entities, received into a description =========//
// SS_REASON b  an interface resolved by name at load
//
// plan_tmp.md Part 3, the client half. The mirror of server/shell_tempents.cpp:
// both sides' tables are planned in C# from one TeClass by one TePropPlanner
// (the client's by ClientTempEntEngine and RecvPropPlan), and this stores the
// client's from the block that crosses (TempEntBuildClient). A schema with one
// declaration and one planner cannot drift between the sides, and a prop one
// bit narrower than the other side expects is a bitstream that stops making
// sense partway through with no log line anywhere.
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
//=============================================================================//

#include <set>
#include <string>

#include "cbase.h"
#include "shell_tempents_store_client.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"   // plan_client_vtables CE6: IManagedTempEntsClient
#include "dt_recv.h"
#include "client_class.h"
#include "iclientnetworkable.h"
#include "iclientunknown.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

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
// sized SOURCESHARP_TE_CLIENT_MAX_TABLES cannot run out, because a handle past that
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
// How many tables this side builds: one create function per handle (below). The
// managed registry refuses past the same number (TePropPlanner.MaxClasses), so a
// handle past it is one nobody issues.
enum { SOURCESHARP_TE_CLIENT_MAX_TABLES = 64 };

struct SourceSharpTeBuiltClient_t
{
	RecvProp            *pProps;
	int                  nProps;
	RecvTable           *pTable;
	ClientClass         *pClass;   // NULL for a table registered without one
	CSourceSharpTempEnt *pShell;   // NULL likewise: no class, nothing to create
	const char          *pClassName;  // interned: what a managed reload asks for again
	uint32_t             nShapeHash;  // managed's fingerprint, handed back on a re-registration
};

static SourceSharpTeBuiltClient_t g_TeBuilt[ SOURCESHARP_TE_CLIENT_MAX_TABLES ];

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
	static CreateEventFn s_Fns[ SOURCESHARP_TE_CLIENT_MAX_TABLES ];
	static bool s_bFilled = false;

	if ( !s_bFilled )
	{
		s_bFilled = true;
		CTeCreateEventTable< SOURCESHARP_TE_CLIENT_MAX_TABLES >::Fill( s_Fns );
	}

	return ( iHandle >= 0 && iHandle < SOURCESHARP_TE_CLIENT_MAX_TABLES ) ? s_Fns[ iHandle ] : NULL;
}

//=============================================================================//
// REGISTRATION: a block in, a table out (the client half of
// plan_server_native_v2 T4b).
//
// ClientTempEntEngine plans every table in C# -- TeSchemaRegistry's refusals and
// event-cap price, TePropPlanner's plan, RecvPropPlan's RecvProp fields -- and
// hands one length-prefixed block per table (sourcesharp.h:
// SourceSharpTeTableHeader_t, SourceSharpRecvPropRow_t rows, the names). This
// stores the rows into RecvProps, builds the RecvTable and, when the block asks,
// the ClientClass and its receiving shell, because the engine walks those by
// layout and calls the shell through its create function. It decides nothing.
//=============================================================================//
static int  g_nTeBuilt  = 0;
static bool g_bTeClosed = false;

//-----------------------------------------------------------------------------
// The names the engine keeps by pointer for the table's lifetime: prop names,
// table names and class names. A std::set node never moves, so c_str() is
// stable for the life of the set; the same text twice is the same pointer.
//-----------------------------------------------------------------------------
static std::set< std::string > g_TeNames;

static const char *InternName( const char *pName )
{
	return g_TeNames.emplace( pName ).first->c_str();
}

int32_t SourceSharp_TempEntBuildClient( const void *pBlock, int32_t nBlockBytes, int32_t nBlobBytes,
                                        SourceSharpTeRecvBuilt_t *pOut )
{
	SourceSharpTeTableBlock_t block;

	if ( !pOut || nBlobBytes < 0 || !SourceSharpTe_ReadRecvTableBlock( pBlock, nBlockBytes, &block ) )
		return -1;

	// A CLASS BUILT BY AN EARLIER LOAD answers its table. The engine walked the
	// class list once, at DLL init, so a managed reload cannot build new tables
	// and must not try; what it needs back is the handle. Whether the class
	// changed shape is managed's to judge, from the hash it gave the first time.
	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( strcmp( g_TeBuilt[ i ].pClassName, block.pClassName ) )
			continue;

		pOut->bExisted   = 1;
		pOut->nShapeHash = g_TeBuilt[ i ].nShapeHash;
		return i;
	}

	// The window. A ClientClass built after the engine's one walk of
	// g_pClientClassHead is not in the list the handshake negotiates, and the
	// first event of that class then resolves to nothing with no line anywhere.
	// The bound is the create functions': there is one per handle.
	if ( g_bTeClosed || g_nTeBuilt >= SOURCESHARP_TE_CLIENT_MAX_TABLES )
		return -1;

	const int nProps = block.header.nProps;
	RecvProp *pProps = new RecvProp[ nProps > 0 ? nProps : 1 ];

	for ( int i = 0; i < nProps; i++ )
	{
		const SourceSharpRecvPropRow_t row = SourceSharpTe_RecvBlockRow( block, i );
		const char *pName = SourceSharpTe_BlockName( block, row.oName );

		// A nested table is one built before this one, by handle.
		RecvTable *pNested = ( row.hDataTable >= 0 && row.hDataTable < g_nTeBuilt )
			? g_TeBuilt[ row.hDataTable ].pTable : NULL;

		if ( !pName || !SourceSharpTe_StoreRecvProp( row, InternName( pName ), pNested, &pProps[ i ] ) )
		{
			delete[] pProps;
			return -1;
		}
	}

	const int iHandle = g_nTeBuilt;
	SourceSharpTeBuiltClient_t &built = g_TeBuilt[ iHandle ];

	built.pProps     = pProps;
	built.nProps     = nProps;
	built.pTable     = new RecvTable( pProps, nProps, InternName( block.pTableName ) );
	built.pClassName = InternName( block.pClassName );
	built.nShapeHash = block.header.nShapeHash;
	built.pClass     = NULL;
	built.pShell     = NULL;

	if ( block.header.bRegisterClass )
	{
		built.pShell = new CSourceSharpTempEnt( built.pClassName, iHandle, nBlobBytes );

		// A CreateEventFn and NOT a CreateClientClassFn. The engine treats a
		// class with the event function set as a one-shot: it never gets an
		// entity index, is never in the entity list, and is not released
		// afterwards -- which is why the shell is a singleton and why
		// Release() asserts.
		built.pClass = new ClientClass( built.pClassName, NULL, TeCreateEventFor( iHandle ), built.pTable );
		built.pShell->SetClientClass( built.pClass );
	}

	g_nTeBuilt = iHandle + 1;

	pOut->bExisted   = 0;
	pOut->nShapeHash = built.nShapeHash;
	return iHandle;
}

//-----------------------------------------------------------------------------
// Shuts the window, and reports what got a ClientClass. Called once, after
// managed has pushed its tables, from SourceSharp_ClientEarlyInit's window.
//-----------------------------------------------------------------------------
void SourceSharp_TempEntClientCloseRegistration( void )
{
	if ( g_bTeClosed )
		return;

	g_bTeClosed = true;

	int nClasses = 0;

	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( g_TeBuilt[i].pClass )
			nClasses++;
	}

	Msg( "[SourceSharp] temp entities: %d table(s), %d ClientClass(es); "
	     "registration closed\n", g_nTeBuilt, nClasses );
}

//=============================================================================//
// A FORWARD, WITH ITS ARGUMENTS AND NOTHING ELSE (tempent_forwards.h).
//
// The client's half, and simpler than the server's because the effect is LOCAL:
// there is no filter and nothing goes on the wire, so managed builds the
// declaration and plays it and no blob is built at any point. Passing a null
// blob buffer is what tells the managed side which of the two this is.
//=============================================================================//
bool SourceSharpTe_Forward( int nId, const float *pFloats, int nFloats,
                            const int *pInts, int nInts )
{
	IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

	if ( !pTempEnts )
		return false;

	return pTempEnts->OnNativeTempEntity( nId, pFloats, nFloats, pInts, nInts,
	                                      NULL, 0, NULL ) >= 0;
}

//=============================================================================//
// THE DUMP -- what the engine holds.
//
// Since the tables are planned in C# (ClientTempEntEngine) and native stored
// them without deciding anything, there is no second native reading to compare
// against: this prints what the ENGINE holds, every prop of every built table,
// and the `TE|` lines are the machine half (`grep '^TE|'`). A RecvProp carries
// no width -- the widths arrive with the server's SendTable at connect -- so a
// width is checked by the server's dump, cs_sv_te_dump, and by the managed
// facts that hold the plan to a recording of the native planner.
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
	"Every registered temp entity: its props as the ENGINE holds them" )
{
	const char *pWanted = ( args.ArgC() > 1 ) ? args[1] : NULL;

	Msg( "[SourceSharp] --- temp entities (client): %d table(s), registration %s ---\n",
		g_nTeBuilt, g_bTeClosed ? "closed" : "OPEN" );

	int nProps = 0;

	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		RecvTable *pTable = g_TeBuilt[i].pTable;

		if ( pWanted && Q_stricmp( g_TeBuilt[i].pClassName, pWanted )
		             && Q_stricmp( pTable->GetName(), pWanted ) )
			continue;

		Msg( "%2d %-28s %-28s %s, %d prop(s)\n",
			i, g_TeBuilt[i].pClassName, pTable->GetName(),
			g_TeBuilt[i].pClass ? "ClientClass" : "table only", pTable->GetNumProps() );

		// Two ClientClasses publishing one table name carry two RecvTables for
		// one class id, and which the engine picks is link order.
		const int nSameName = CountTablesNamed( pTable->GetName() );

		if ( nSameName > 1 )
		{
			Warning( "     *** %d ClientClass(es) publish a table named '%s' ***\n",
				nSameName, pTable->GetName() );
		}

		for ( int iProp = 0; iProp < pTable->GetNumProps(); iProp++ )
		{
			RecvProp *pProp = pTable->GetProp( iProp );
			RecvTable *pNested = ( pProp->m_RecvType == DPT_DataTable ) ? pProp->GetDataTable() : NULL;

			nProps++;

			Msg( "     %-24s %-9s off %5d flags 0x%05x%s%s\n",
				pProp->GetName(), PropTypeName( pProp->m_RecvType ), pProp->GetOffset(), pProp->GetFlags(),
				pNested ? " -> " : "", pNested && pNested->GetName() ? pNested->GetName() : "" );

			// The client cannot read SPROP_COLLAPSIBLE off a RecvProp, so it
			// checks the CONDITION: a nested table at offset zero that is not
			// the baseclass prop. Registration refuses it; a dump of a build
			// whose refusal was removed should still say what is wrong.
			if ( pNested && pProp->GetOffset() == 0 && Q_strcmp( pProp->GetName(), "baseclass" ) )
			{
				Warning( "     *** NESTED TABLE AT OFFSET 0: the sender will mark it "
				         "COLLAPSIBLE and this side will decode it at the blob base ***\n" );
			}

			Msg( "TE|%s|%d|%s|%s|%d|0x%x|%s\n",
				pTable->GetName(), iProp, pProp->GetName(),
				PropTypeName( pProp->m_RecvType ), pProp->GetOffset(), pProp->GetFlags(),
				pNested && pNested->GetName() ? pNested->GetName() : "" );
		}
	}

	Msg( "[SourceSharp] --- %d prop(s) ---\n", nProps );

	if ( !g_bTeClosed )
		Warning( "     registration is still OPEN, which it should not be after DLL init\n" );
}
