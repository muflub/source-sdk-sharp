//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's half of the payload props: the schema-hash gate's carry-out and the recv props.
// SS_REASON f  a bit-level wire codec

#include "tier1/convar.h"
#include "dt_recv.h"

#include "generic_nettable.h"
#include "ss_string.h"
#include "imanaged_areas_client.h"

// Declared rather than included: shell_standalone_client.h is a long header.
int SourceSharp_StandaloneBlobOffset();

// Managed decides; this hands it what the type table holds for the type and carries out
// the answer. With no managed side nothing is decoded.
bool SourceSharpSchema_Accept(unsigned int nTypeId, unsigned int nSchemaHash, int nEntIndex,
							  const char** ppOutClassName)
{
	const SourceSharpNetType_t* pType = SourceSharp_NetTypes().ById(nTypeId);

	if (ppOutClassName)
		*ppOutClassName = pType ? pType->szClass : NULL;

	IManagedStandaloneClient* pStandalone = ManagedStandaloneClient();

	if (!pStandalone)
		return false;

	return pStandalone->SchemaAccept(nTypeId, pType ? 1 : 0, pType ? pType->nSchemaHash : 0u, nSchemaHash, nEntIndex,
									 pType ? pType->szClass : "") != 0;
}

// The engine checks both ends before it puts a prop on the fast int32 list, so the recv
// prop must resolve to Int32ToInt32 too.
int SourceSharpPayload_BuildRecvProps(RecvProp* pProps, int nWords, int nBlobBase)
{
	if (!pProps)
		return -1;

	if (nWords < 0 || nWords > SOURCESHARP_PAYLOAD_MAX_WORDS)
		return -1;

	if (!SourceSharpPayload_OffsetsFit(nBlobBase, nWords))
		return -1;

	for (int i = 0; i < nWords; i++)
	{
		pProps[i] = RecvPropInt(SourceSharpPayload_PropName(i), SourceSharpPayload_WordOffset(nBlobBase, i),
								SOURCESHARP_PAYLOAD_WORD_BYTES);
	}

	return nWords;
}

bool SourceSharpPayload_IsFastRecvProxy(const RecvProp* pProp)
{
	if (!pProp)
		return false;

	return pProp->GetProxyFn() == g_StandardRecvProxies.m_Int32ToInt32;
}

const char* SourceSharpPayload_RecvProxyName(const RecvProp* pProp)
{
	if (!pProp)
		return "(null)";

	const RecvVarProxyFn fn = pProp->GetProxyFn();

	if (fn == g_StandardRecvProxies.m_Int32ToInt32)
		return "m_Int32ToInt32";
	if (fn == g_StandardRecvProxies.m_Int32ToInt16)
		return "m_Int32ToInt16";
	if (fn == g_StandardRecvProxies.m_Int32ToInt8)
		return "m_Int32ToInt8";
	if (fn == g_StandardRecvProxies.m_FloatToFloat)
		return "m_FloatToFloat";
	if (fn == g_StandardRecvProxies.m_VectorToVector)
		return "m_VectorToVector";

	return "custom (SLOW)";
}

// The client's half of cs_sv_dump_payload; on a listen server both ends can be compared.
CON_COMMAND(cs_cl_dump_payload,
			"Build the client's generic-table payload prop set standalone and dump its offsets and proxies")
{
	int nBytes = 32;

	if (args.ArgC() > 1)
		nBytes = ss::AtoI(args[1]);

	const int nWords = SourceSharpPayload_WordCount(nBytes);

	if (nWords < 0)
	{
		Warning("[SourceSharp] payload dump: %d bytes is more than the largest tier (%d)\n", nBytes,
				SOURCESHARP_PAYLOAD_MAX_WORDS * SOURCESHARP_PAYLOAD_WORD_BYTES);
		return;
	}

	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	RecvProp* pProps = new RecvProp[nWords > 0 ? nWords : 1];
	const int nBuilt = SourceSharpPayload_BuildRecvProps(pProps, nWords, nBlobBase);

	if (nBuilt != nWords)
	{
		Warning("[SourceSharp] payload dump: build refused %d word(s) at blob base %d\n", nWords, nBlobBase);
		delete[] pProps;
		return;
	}

	Msg("[SourceSharp] --- client payload prop dump: %d byte(s) -> %d word prop(s), "
		"blob base %d ---\n",
		nBytes, nWords, nBlobBase);

	int nFast = 0;

	for (int i = 0; i < nBuilt; i++)
	{
		const RecvProp* pProp = &pProps[i];
		const bool bFast = SourceSharpPayload_IsFastRecvProxy(pProp);

		if (bFast)
			nFast++;

		Msg("[SourceSharp]     %-20s offset=%-4d proxy=%s%s\n", pProp->GetName(), pProp->GetOffset(),
			SourceSharpPayload_RecvProxyName(pProp), bFast ? "" : "   <-- NOT ON THE FAST LOCAL-TRANSFER LIST");
	}

	if (nFast == nBuilt)
	{
		Msg("[SourceSharp] client payload dump: all %d prop(s) resolved to m_Int32ToInt32\n", nBuilt);
	}
	else
	{
		Warning("[SourceSharp] client payload dump: %d of %d prop(s) did NOT resolve to "
				"m_Int32ToInt32\n",
				nBuilt - nFast, nBuilt);
	}

	delete[] pProps;
}
