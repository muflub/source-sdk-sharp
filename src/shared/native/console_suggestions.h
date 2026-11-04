#pragma once

#include <string.h>

#include "engine_heap.h"
#include "engine_utlvector.h"

// A command's suggestions between the engine's CUtlVector<CUtlString> and one buffer of
// newline-separated lines, which is how they cross to and from managed code.
namespace ss
{
const int kConsoleSuggestionBytes = 8192;

// Appends each line to a vector the engine owns, with the engine's allocator. Returns how
// many went in.
inline int AppendSuggestionLines(IEngineHeap* pHeap, EngineUtlVectorLayout<EngineUtlString>& vec, const char* pLines)
{
	int nAdded = 0;
	char line[512];
	while (pLines && *pLines)
	{
		const char* pEnd = strchr(pLines, '\n');
		const size_t nLength = pEnd ? (size_t)(pEnd - pLines) : strlen(pLines);
		if (nLength > 0 && nLength < sizeof(line))
		{
			memcpy(line, pLines, nLength);
			line[nLength] = '\0';
			if (!EngineUtlStringVectorAppend(pHeap, vec, line))
				break;
			nAdded++;
		}
		pLines = pEnd ? pEnd + 1 : NULL;
	}
	return nAdded;
}

// Writes whole lines, as many as fit. Returns how many.
inline int WriteSuggestionLines(const EngineUtlVector<EngineUtlString>& lines, char* pOut, int nOutSize)
{
	if (!pOut || nOutSize <= 0)
		return 0;

	pOut[0] = '\0';
	int nWritten = 0;
	int nUsed = 0;
	for (int i = 0; i < lines.Count(); ++i)
	{
		const char* pLine = lines[i].m_pString;
		if (!pLine)
			continue;

		const int nLength = (int)strlen(pLine);
		if (nUsed + nLength + 2 > nOutSize)
			break;

		memcpy(pOut + nUsed, pLine, nLength);
		nUsed += nLength;
		pOut[nUsed++] = '\n';
		pOut[nUsed] = '\0';
		nWritten++;
	}
	return nWritten;
}

// The command a console line starts with. False when it has none, or one too long for the
// buffer.
inline bool ConsoleLineCommand(const char* pLine, char* pOut, int nOutSize)
{
	if (!pLine || !pOut || nOutSize <= 0)
		return false;

	const char* pSpace = strchr(pLine, ' ');
	const int nLength = pSpace ? (int)(pSpace - pLine) : (int)strlen(pLine);
	if (nLength <= 0 || nLength >= nOutSize)
		return false;

	memcpy(pOut, pLine, nLength);
	pOut[nLength] = '\0';
	return true;
}
} // namespace ss
