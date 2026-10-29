//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only.

#include "tier1/convar.h"
#include "dt_send.h"
#include "generic_payload.h"
#include "native_command_body.h"
#include "ss_string.h"
#include "server_services.h"
#include "server_state.h"

// Declared here rather than including the large shell_standalone.h.
int SourceSharp_StandaloneBlobOffset();

// Builds a payload prop set and dumps what it resolved to. The proxy column matters:
// a prop that fell onto the engine's slow local-transfer list behaves the same and
// nothing else reports it.
SS_NATIVE_COMMAND(cs_sv_dump_payload)
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

	// The real blob base, so the offsets are the ones a table would get today.
	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	SendProp* pProps = new SendProp[nWords > 0 ? nWords : 1];
	const int nBuilt =
		SourceSharpPayload_BuildSendProps(pProps, nWords, nBlobBase, services.m_EntityContext.m_NetTables.m_PropNames);

	if (nBuilt != nWords)
	{
		Warning("[SourceSharp] payload dump: build refused %d word(s) at blob base %d\n", nWords, nBlobBase);
		delete[] pProps;
		return;
	}

	Msg("[SourceSharp] --- payload prop dump: %d byte(s) -> %d word prop(s), "
		"blob base %d, last offset %d ---\n",
		nBytes, nWords, nBlobBase, nWords > 0 ? SourceSharpPayload_WordOffset(nBlobBase, nWords - 1) : nBlobBase);

	int nFast = 0;

	for (int i = 0; i < nBuilt; i++)
	{
		const SendProp* pProp = &pProps[i];
		const bool bFast = SourceSharpPayload_IsFastSendProxy(pProp);

		if (bFast)
			nFast++;

		Msg("[SourceSharp]     %-20s offset=%-4d bits=%-3d type=%d proxy=%s%s\n", pProp->GetName(), pProp->GetOffset(),
			pProp->m_nBits, (int)pProp->GetType(), SourceSharpPayload_SendProxyName(pProp),
			bFast ? "" : "   <-- NOT ON THE FAST LOCAL-TRANSFER LIST");
	}

	if (nFast == nBuilt)
	{
		Msg("[SourceSharp] payload dump: all %d prop(s) resolved to m_UInt32ToInt32 "
			"(engine fast int32 local transfer)\n",
			nBuilt);
	}
	else
	{
		Warning("[SourceSharp] payload dump: %d of %d prop(s) did NOT resolve to "
				"m_UInt32ToInt32 — those words would be copied slowly, proxies and "
				"all\n",
				nBuilt - nFast, nBuilt);
	}

	delete[] pProps;
}
