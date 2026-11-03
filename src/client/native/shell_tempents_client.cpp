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
#include "shell_tempents_store_client.h"

#include "imanaged_areas_client.h"
#include "shell_te_tables_client.h"

// One singleton per registered class, as stock's IMPLEMENT_CLIENTCLASS_EVENT. The engine
// deltas consecutive events of a class against each other, so the blob must keep the
// previous event's values and is never cleared between events.
class CSourceSharpTempEnt : public IClientUnknown, public IClientNetworkable
{
public:
	CSourceSharpTempEnt(const CManagedAreasClient& areas, const char* pClassName, int hClass, int nBlobBytes)
		: m_Areas(areas)
		, m_pClassName(pClassName)
		, m_hClass(hClass)
		, m_nBlobBytes(nBlobBytes)
	{
		// Zeroed once, so the first event reads zero for anything it does not send.
		m_pBlob = new unsigned char[nBlobBytes > 0 ? nBlobBytes : 1];
		Q_memset(m_pBlob, 0, nBlobBytes > 0 ? nBlobBytes : 1);
	}

	// IClientUnknown: the husk from c_basetempentity.h.
	virtual void SetRefEHandle(const CBaseHandle&) { Assert(false); }

	// An invalid handle rather than stock's null dereference; nothing calls it.
	virtual const CBaseHandle& GetRefEHandle() const { return m_hInvalid; }

	virtual IClientUnknown* GetIClientUnknown() { return this; }

	virtual ICollideable* GetCollideable() { return 0; }

	virtual IClientNetworkable* GetClientNetworkable() { return this; }

	virtual IClientRenderable* GetClientRenderable() { return 0; }

	virtual IClientEntity* GetIClientEntity() { return 0; }

	virtual IGameEntity* GetBaseEntity() { return 0; }

	virtual IClientThinkable* GetClientThinkable() { return 0; }

	// IClientNetworkable. The class is set after construction: the ClientClass's create
	// function returns the shell, so the shell must exist first.
	virtual ClientClass* GetClientClass() { return m_pClientClass; }

	void SetClientClass(ClientClass* pClass) { m_pClientClass = pClass; }

	virtual void Release()
	{
		// The engine only releases what it created; an event singleton is created here once.
		Assert(!"CSourceSharpTempEnt::Release should never be called");
	}

	virtual void NotifyShouldTransmit(ShouldTransmitState_t) {}

	virtual void PreDataUpdate(DataUpdateType_t) {}

	virtual void OnDataUnchangedInPVS() {}

	virtual void OnPreDataChanged(DataUpdateType_t) { Assert(0); }

	virtual void OnDataChanged(DataUpdateType_t) { Assert(0); }

	virtual bool IsDormant()
	{
		Assert(0);
		return false;
	}

	virtual int entindex() const
	{
		Assert(0);
		return 0;
	}

	virtual void ReceiveMessage(int, bf_read&) { Assert(0); }

	virtual void SetDestroyedOnRecreateEntities() { Assert(0); }

	// The recv props read at managed's offsets into the blob.
	virtual void* GetDataTableBasePtr() { return m_pBlob; }

	virtual void PostDataUpdate(DataUpdateType_t)
	{
		IManagedTempEntsClient* pTempEnts = m_Areas.m_pTempEntsClient;

		if (!pTempEnts)
		{
			// Once per class, not per event.
			if (!m_bComplained)
			{
				m_bComplained = true;
				Warning("[SourceSharp] temp entity '%s' arrived and managed code is not "
						"listening; nothing will play it\n",
						m_pClassName);
			}
			return;
		}

		pTempEnts->OnTempEntity(m_hClass, m_pBlob, m_nBlobBytes);
	}

	// For a client playing its own effect through the same managed code.
	unsigned char* Blob() { return m_pBlob; }

	int BlobBytes() const { return m_nBlobBytes; }

private:
	const CManagedAreasClient& m_Areas;
	CBaseHandle m_hInvalid;
	const char* m_pClassName;
	int m_hClass;
	int m_nBlobBytes;
	unsigned char* m_pBlob;
	ClientClass* m_pClientClass = NULL;
	bool m_bComplained = false;
};

IClientNetworkable* CTempEntTablesClient::Built_t::CreateEvent()
{
	return pShell;
}

// Stores each managed-planned table block into RecvProps, a RecvTable and, when asked,
// a ClientClass and its shell.
int32_t CTempEntTablesClient::TempEntBuildClient(const void* pBlock, int32_t nBlockBytes, int32_t nBlobBytes,
												 SourceSharpTeRecvBuilt_t* pOut)
{
	SourceSharpTeTableBlock_t block;

	if (!pOut || nBlobBytes < 0 || !SourceSharpTe_ReadRecvTableBlock(pBlock, nBlockBytes, &block))
		return -1;

	// A class built by an earlier load answers its handle: the engine walks the class list
	// once, at DLL init, so a managed reload cannot build new tables.
	for (int i = 0; i < m_nBuilt; i++)
	{
		if (strcmp(m_pBuilt[i]->pClassName, block.pClassName))
			continue;

		pOut->bExisted = 1;
		pOut->nShapeHash = m_pBuilt[i]->nShapeHash;
		return i;
	}

	// A ClientClass built after the engine's one walk of g_pClientClassHead would never
	// resolve, so registration closes then.
	if (m_bClosed || m_nBuilt >= kMaxTables)
		return -1;

	const int nProps = block.header.nProps;
	RecvProp* pProps = new RecvProp[nProps > 0 ? nProps : 1];

	for (int i = 0; i < nProps; i++)
	{
		const SourceSharpRecvPropRow_t row = SourceSharpTe_RecvBlockRow(block, i);
		const char* pName = SourceSharpTe_BlockName(block, row.oName);

		// A nested table is one built before this one, by handle.
		RecvTable* pNested =
			(row.hDataTable >= 0 && row.hDataTable < m_nBuilt) ? m_pBuilt[row.hDataTable]->pTable : NULL;

		if (!pName || !SourceSharpTe_StoreRecvProp(row, InternName(pName), pNested, &pProps[i]))
		{
			delete[] pProps;
			return -1;
		}
	}

	const int iHandle = m_nBuilt;
	Built_t& built = *new Built_t;
	m_pBuilt[iHandle] = &built;

	built.pProps = pProps;
	built.nProps = nProps;
	built.pTable = new RecvTable(pProps, nProps, InternName(block.pTableName));
	built.pClassName = InternName(block.pClassName);
	built.nShapeHash = block.header.nShapeHash;

	if (block.header.bRegisterClass)
	{
		built.pShell = new CSourceSharpTempEnt(m_Areas, built.pClassName, iHandle, nBlobBytes);

		// A CreateEventFn, not CreateClientClassFn: the engine treats the class as a one-shot with
		// no entity index that is never released.
		built.pClass = new ClientClass(built.pClassName, NULL, built.Create.Get(), built.pTable);
		built.pShell->SetClientClass(built.pClass);
	}

	m_nBuilt = iHandle + 1;

	pOut->bExisted = 0;
	pOut->nShapeHash = built.nShapeHash;
	return iHandle;
}

// Closes registration and reports what got a ClientClass. Called once, after managed has
// pushed its tables.
void CTempEntTablesClient::TempEntClientCloseRegistration()
{
	if (m_bClosed)
		return;

	m_bClosed = true;

	int nClasses = 0;

	for (int i = 0; i < m_nBuilt; i++)
	{
		if (m_pBuilt[i]->pClass)
			nClasses++;
	}

	Msg("[SourceSharp] temp entities: %d table(s), %d ClientClass(es); "
		"registration closed\n",
		m_nBuilt, nClasses);
}

// Prints every prop of every built table the engine holds; `TE|` lines are machine-read.
// A RecvProp carries no width, so widths are checked by the server's cs_sv_te_dump.

static const char* PropTypeName(int nType)
{
	switch (nType)
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

static int CountTablesNamed(const char* pName)
{
	int nFound = 0;

	for (ClientClass* pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext)
	{
		// Only the class's own table: a nested one is reached through several classes.
		if (pClass->m_pRecvTable && pClass->m_pRecvTable->GetName() &&
			!Q_strcmp(pClass->m_pRecvTable->GetName(), pName))
			nFound++;
	}

	return nFound;
}

void CTempEntTablesClient::Dump(const char* pWanted) const
{
	Msg("[SourceSharp] --- temp entities (client): %d table(s), registration %s ---\n", m_nBuilt,
		m_bClosed ? "closed" : "OPEN");

	int nProps = 0;

	for (int i = 0; i < m_nBuilt; i++)
	{
		RecvTable* pTable = m_pBuilt[i]->pTable;

		if (pWanted && Q_stricmp(m_pBuilt[i]->pClassName, pWanted) && Q_stricmp(pTable->GetName(), pWanted))
			continue;

		Msg("%2d %-28s %-28s %s, %d prop(s)\n", i, m_pBuilt[i]->pClassName, pTable->GetName(),
			m_pBuilt[i]->pClass ? "ClientClass" : "table only", pTable->GetNumProps());

		// Two ClientClasses publishing one table name: which the engine picks is link order.
		const int nSameName = CountTablesNamed(pTable->GetName());

		if (nSameName > 1)
		{
			Warning("     *** %d ClientClass(es) publish a table named '%s' ***\n", nSameName, pTable->GetName());
		}

		for (int iProp = 0; iProp < pTable->GetNumProps(); iProp++)
		{
			RecvProp* pProp = pTable->GetProp(iProp);
			RecvTable* pNested = (pProp->m_RecvType == DPT_DataTable) ? pProp->GetDataTable() : NULL;

			nProps++;

			Msg("     %-24s %-9s off %5d flags 0x%05x%s%s\n", pProp->GetName(), PropTypeName(pProp->m_RecvType),
				pProp->GetOffset(), pProp->GetFlags(), pNested ? " -> " : "",
				pNested && pNested->GetName() ? pNested->GetName() : "");

			// SPROP_COLLAPSIBLE is not readable off a RecvProp, so check the condition: a nested table
			// at offset zero that is not the baseclass prop.
			if (pNested && pProp->GetOffset() == 0 && Q_strcmp(pProp->GetName(), "baseclass"))
			{
				Warning("     *** NESTED TABLE AT OFFSET 0: the sender will mark it "
						"COLLAPSIBLE and this side will decode it at the blob base ***\n");
			}

			Msg("TE|%s|%d|%s|%s|%d|0x%x|%s\n", pTable->GetName(), iProp, pProp->GetName(),
				PropTypeName(pProp->m_RecvType), pProp->GetOffset(), pProp->GetFlags(),
				pNested && pNested->GetName() ? pNested->GetName() : "");
		}
	}

	Msg("[SourceSharp] --- %d prop(s) ---\n", nProps);

	if (!m_bClosed)
		Warning("     registration is still OPEN, which it should not be after DLL init\n");
}

CON_COMMAND(cs_cl_te_dump, "Every registered temp entity: its props as the ENGINE holds them")
{
	ShellClientDll_TempEntTables().Dump((args.ArgC() > 1) ? args[1] : NULL);
}
