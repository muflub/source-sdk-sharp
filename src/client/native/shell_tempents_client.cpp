//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Temp entities received into a byte blob. Managed plans every table; one shell class
// serves every effect, its recv props reading at the offsets managed computed, and
// PostDataUpdate hands the blob to managed.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include <set>
#include <string>

#include "shell_tempents_store_client.h"

#include "imanaged_areas_client.h"

// One singleton per registered class, as stock's IMPLEMENT_CLIENTCLASS_EVENT. The engine
// deltas consecutive events of a class against each other, so the blob must keep the
// previous event's values and is never cleared between events.
class CSourceSharpTempEnt : public IClientUnknown, public IClientNetworkable
{
public:
	CSourceSharpTempEnt( const char *pClassName, int hClass, int nBlobBytes )
		: m_pClassName( pClassName )
		, m_hClass( hClass )
		, m_nBlobBytes( nBlobBytes )
	{
		// Zeroed once, so the first event reads zero for anything it does not send.
		m_pBlob = new unsigned char[nBlobBytes > 0 ? nBlobBytes : 1];
		Q_memset( m_pBlob, 0, nBlobBytes > 0 ? nBlobBytes : 1 );
	}

	// IClientUnknown: the husk from c_basetempentity.h.
	virtual void SetRefEHandle( const CBaseHandle & ) { Assert( false ); }

	// A static rather than stock's null dereference; nothing calls it.
	virtual const CBaseHandle &GetRefEHandle() const
	{
		static CBaseHandle s_Invalid;
		return s_Invalid;
	}

	virtual IClientUnknown *GetIClientUnknown() { return this; }

	virtual ICollideable *GetCollideable() { return 0; }

	virtual IClientNetworkable *GetClientNetworkable() { return this; }

	virtual IClientRenderable *GetClientRenderable() { return 0; }

	virtual IClientEntity *GetIClientEntity() { return 0; }

	virtual IGameEntity *GetBaseEntity() { return 0; }

	virtual IClientThinkable *GetClientThinkable() { return 0; }

	// IClientNetworkable. The class is set after construction: the ClientClass's create
	// function returns the shell, so the shell must exist first.
	virtual ClientClass *GetClientClass() { return m_pClientClass; }

	void SetClientClass( ClientClass *pClass ) { m_pClientClass = pClass; }

	virtual void Release()
	{
		// The engine only releases what it created; an event singleton is created here once.
		Assert( !"CSourceSharpTempEnt::Release should never be called" );
	}

	virtual void NotifyShouldTransmit( ShouldTransmitState_t ) {}

	virtual void PreDataUpdate( DataUpdateType_t ) {}

	virtual void OnDataUnchangedInPVS() {}

	virtual void OnPreDataChanged( DataUpdateType_t ) { Assert( 0 ); }

	virtual void OnDataChanged( DataUpdateType_t ) { Assert( 0 ); }

	virtual bool IsDormant()
	{
		Assert( 0 );
		return false;
	}

	virtual int entindex() const
	{
		Assert( 0 );
		return 0;
	}

	virtual void ReceiveMessage( int, bf_read & ) { Assert( 0 ); }

	virtual void SetDestroyedOnRecreateEntities() { Assert( 0 ); }

	// The recv props read at managed's offsets into the blob.
	virtual void *GetDataTableBasePtr() { return m_pBlob; }

	virtual void PostDataUpdate( DataUpdateType_t )
	{
		IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

		if ( !pTempEnts )
		{
			// Once per class, not per event.
			if ( !m_bComplained )
			{
				m_bComplained = true;
				Warning( "[SourceSharp] temp entity '%s' arrived and managed code is not "
						 "listening; nothing will play it\n",
						 m_pClassName );
			}
			return;
		}

		pTempEnts->OnTempEntity( m_hClass, m_pBlob, m_nBlobBytes );
	}

	// For a client playing its own effect through the same managed code.
	unsigned char *Blob() { return m_pBlob; }

	int BlobBytes() const { return m_nBlobBytes; }

private:
	const char *m_pClassName;
	int m_hClass;
	int m_nBlobBytes;
	unsigned char *m_pBlob;
	ClientClass *m_pClientClass = NULL;
	bool m_bComplained = false;
};

// One create function per handle, since CreateEventFn carries no context. The managed
// registry refuses past the same bound (TePropPlanner.MaxClasses).
enum
{
	SOURCESHARP_TE_CLIENT_MAX_TABLES = 64
};

struct SourceSharpTeBuiltClient_t
{
	RecvProp *pProps;
	int nProps;
	RecvTable *pTable;
	ClientClass *pClass; // NULL for a table registered without one
	CSourceSharpTempEnt *pShell; // NULL likewise: no class, nothing to create
	const char *pClassName; // interned: what a managed reload asks for again
	uint32_t nShapeHash; // managed's fingerprint, handed back on a re-registration
};

static SourceSharpTeBuiltClient_t g_TeBuilt[SOURCESHARP_TE_CLIENT_MAX_TABLES];

template <int N>
static IClientNetworkable *TeCreateEvent()
{
	return g_TeBuilt[N].pShell;
}

template <int N>
struct CTeCreateEventTable
{
	static void Fill( CreateEventFn *pOut )
	{
		CTeCreateEventTable<N - 1>::Fill( pOut );
		pOut[N - 1] = &TeCreateEvent<N - 1>;
	}
};

template <>
struct CTeCreateEventTable<0>
{
	static void Fill( CreateEventFn * ) {}
};

static CreateEventFn TeCreateEventFor( int iHandle )
{
	static CreateEventFn s_Fns[SOURCESHARP_TE_CLIENT_MAX_TABLES];
	static bool s_bFilled = false;

	if ( !s_bFilled )
	{
		s_bFilled = true;
		CTeCreateEventTable<SOURCESHARP_TE_CLIENT_MAX_TABLES>::Fill( s_Fns );
	}

	return ( iHandle >= 0 && iHandle < SOURCESHARP_TE_CLIENT_MAX_TABLES ) ? s_Fns[iHandle] : NULL;
}

// Stores each managed-planned table block into RecvProps, a RecvTable and, when asked,
// a ClientClass and its shell. Built parts are never freed: the engine reads them for the
// life of the process.
static int g_nTeBuilt = 0;
static bool g_bTeClosed = false;

// Names the engine keeps by pointer. A std::set node never moves, so c_str() stays valid.
static std::set<std::string> g_TeNames;

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

	// A class built by an earlier load answers its handle: the engine walks the class list
	// once, at DLL init, so a managed reload cannot build new tables.
	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		if ( strcmp( g_TeBuilt[i].pClassName, block.pClassName ) )
			continue;

		pOut->bExisted = 1;
		pOut->nShapeHash = g_TeBuilt[i].nShapeHash;
		return i;
	}

	// A ClientClass built after the engine's one walk of g_pClientClassHead would never
	// resolve, so registration closes then.
	if ( g_bTeClosed || g_nTeBuilt >= SOURCESHARP_TE_CLIENT_MAX_TABLES )
		return -1;

	const int nProps = block.header.nProps;
	RecvProp *pProps = new RecvProp[nProps > 0 ? nProps : 1];

	for ( int i = 0; i < nProps; i++ )
	{
		const SourceSharpRecvPropRow_t row = SourceSharpTe_RecvBlockRow( block, i );
		const char *pName = SourceSharpTe_BlockName( block, row.oName );

		// A nested table is one built before this one, by handle.
		RecvTable *pNested =
			( row.hDataTable >= 0 && row.hDataTable < g_nTeBuilt ) ? g_TeBuilt[row.hDataTable].pTable : NULL;

		if ( !pName || !SourceSharpTe_StoreRecvProp( row, InternName( pName ), pNested, &pProps[i] ) )
		{
			delete[] pProps;
			return -1;
		}
	}

	const int iHandle = g_nTeBuilt;
	SourceSharpTeBuiltClient_t &built = g_TeBuilt[iHandle];

	built.pProps = pProps;
	built.nProps = nProps;
	built.pTable = new RecvTable( pProps, nProps, InternName( block.pTableName ) );
	built.pClassName = InternName( block.pClassName );
	built.nShapeHash = block.header.nShapeHash;
	built.pClass = NULL;
	built.pShell = NULL;

	if ( block.header.bRegisterClass )
	{
		built.pShell = new CSourceSharpTempEnt( built.pClassName, iHandle, nBlobBytes );

		// A CreateEventFn, not CreateClientClassFn: the engine treats the class as a one-shot with
		// no entity index that is never released.
		built.pClass = new ClientClass( built.pClassName, NULL, TeCreateEventFor( iHandle ), built.pTable );
		built.pShell->SetClientClass( built.pClass );
	}

	g_nTeBuilt = iHandle + 1;

	pOut->bExisted = 0;
	pOut->nShapeHash = built.nShapeHash;
	return iHandle;
}

// Closes registration and reports what got a ClientClass. Called once, after managed has
// pushed its tables.
void SourceSharp_TempEntClientCloseRegistration()
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
		 "registration closed\n",
		 g_nTeBuilt, nClasses );
}

// The effect is local: managed builds and plays it, and a null blob buffer tells it so.
bool SourceSharpTe_Forward( int nId, const float *pFloats, int nFloats, const int *pInts, int nInts )
{
	IManagedTempEntsClient *pTempEnts = ManagedTempEntsClient();

	if ( !pTempEnts )
		return false;

	return pTempEnts->OnNativeTempEntity( nId, pFloats, nFloats, pInts, nInts, NULL, 0, NULL ) >= 0;
}

// Prints every prop of every built table the engine holds; `TE|` lines are machine-read.
// A RecvProp carries no width, so widths are checked by the server's cs_sv_te_dump.

static const char *PropTypeName( int nType )
{
	switch ( nType )
	{
		case DPT_Int:
			return "int";
		case DPT_Float:
			return "float";
		case DPT_Vector:
			return "vector";
		case DPT_VectorXY:
			return "vectorxy";
		case DPT_String:
			return "string";
		case DPT_Array:
			return "array";
		case DPT_DataTable:
			return "datatable";
	}

	return "?";
}

static int CountTablesNamed( const char *pName )
{
	int nFound = 0;

	for ( ClientClass *pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext )
	{
		// Only the class's own table: a nested one is reached through several classes.
		if ( pClass->m_pRecvTable && pClass->m_pRecvTable->GetName() &&
			 !Q_strcmp( pClass->m_pRecvTable->GetName(), pName ) )
			nFound++;
	}

	return nFound;
}

CON_COMMAND( cs_cl_te_dump, "Every registered temp entity: its props as the ENGINE holds them" )
{
	const char *pWanted = ( args.ArgC() > 1 ) ? args[1] : NULL;

	Msg( "[SourceSharp] --- temp entities (client): %d table(s), registration %s ---\n", g_nTeBuilt,
		 g_bTeClosed ? "closed" : "OPEN" );

	int nProps = 0;

	for ( int i = 0; i < g_nTeBuilt; i++ )
	{
		RecvTable *pTable = g_TeBuilt[i].pTable;

		if ( pWanted && Q_stricmp( g_TeBuilt[i].pClassName, pWanted ) && Q_stricmp( pTable->GetName(), pWanted ) )
			continue;

		Msg( "%2d %-28s %-28s %s, %d prop(s)\n", i, g_TeBuilt[i].pClassName, pTable->GetName(),
			 g_TeBuilt[i].pClass ? "ClientClass" : "table only", pTable->GetNumProps() );

		// Two ClientClasses publishing one table name: which the engine picks is link order.
		const int nSameName = CountTablesNamed( pTable->GetName() );

		if ( nSameName > 1 )
		{
			Warning( "     *** %d ClientClass(es) publish a table named '%s' ***\n", nSameName, pTable->GetName() );
		}

		for ( int iProp = 0; iProp < pTable->GetNumProps(); iProp++ )
		{
			RecvProp *pProp = pTable->GetProp( iProp );
			RecvTable *pNested = ( pProp->m_RecvType == DPT_DataTable ) ? pProp->GetDataTable() : NULL;

			nProps++;

			Msg( "     %-24s %-9s off %5d flags 0x%05x%s%s\n", pProp->GetName(), PropTypeName( pProp->m_RecvType ),
				 pProp->GetOffset(), pProp->GetFlags(), pNested ? " -> " : "",
				 pNested && pNested->GetName() ? pNested->GetName() : "" );

			// SPROP_COLLAPSIBLE is not readable off a RecvProp, so check the condition: a nested table
			// at offset zero that is not the baseclass prop.
			if ( pNested && pProp->GetOffset() == 0 && Q_strcmp( pProp->GetName(), "baseclass" ) )
			{
				Warning( "     *** NESTED TABLE AT OFFSET 0: the sender will mark it "
						 "COLLAPSIBLE and this side will decode it at the blob base ***\n" );
			}

			Msg( "TE|%s|%d|%s|%s|%d|0x%x|%s\n", pTable->GetName(), iProp, pProp->GetName(),
				 PropTypeName( pProp->m_RecvType ), pProp->GetOffset(), pProp->GetFlags(),
				 pNested && pNested->GetName() ? pNested->GetName() : "" );
		}
	}

	Msg( "[SourceSharp] --- %d prop(s) ---\n", nProps );

	if ( !g_bTeClosed )
		Warning( "     registration is still OPEN, which it should not be after DLL init\n" );
}
