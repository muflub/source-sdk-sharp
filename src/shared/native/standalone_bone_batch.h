// The frame's skeletons, posed ahead by managed. At the frame's first SetupBones
// ask, IManagedBonesClient::BoneBatch answers this table's address, and
// C_StandaloneEntity::SetupBones copies an entry out instead of crossing. Native
// only checks that an ask matches what the entry was posed for (frame, clock,
// mask, engine-block inputs) and counts answered asks; anything else crosses.
// Managed owns the memory and may move it between frames, so native reads every
// pointer through the header and keeps none. The offsets are an ABI matching
// SourceSharp.Client's ClientBoneBatch.

#pragma once

// An entity's slot is its handle's entry index.
enum StandaloneBoneBatchSize
{
	SS_BONE_BATCH_SLOTS = 4096,
	SS_BONE_BATCH_MATRIX_FLOATS = 12,
};

struct SourceSharpBoneBatchEntry_t
{
	uint32_t hEntity; // the handle the entry was posed for
	int32_t nBoneMask; // the mask it was posed under: an ask must fit inside
	int32_t nBones; // its matrices
	int32_t nFirstMatrix; // where they start in pMatrices
	int32_t nAnswered; // native: asks this entry answered
	int32_t nModelIndex; // the block it was posed from
	int32_t nViewModel;
	float flOrigin[3];
	float flAngles[3];
	float flRenderOrigin[3];
	float flRenderAngles[3];
};

struct SourceSharpBoneBatch_t
{
	int32_t nFrameCount; // the frame the table answers for
	float flCurrentTime; // and the clock
	int32_t nEntries;
	int32_t nPad;
	int16_t* pSlotToEntry; // SS_BONE_BATCH_SLOTS; -1 = no entry
	SourceSharpBoneBatchEntry_t* pEntries;
	const float* pMatrices; // 12 floats a bone
};

enum StandaloneBoneBatchOffset
{
	SS_BONE_BATCH_ENTRY_HENTITY = 0,
	SS_BONE_BATCH_ENTRY_NBONEMASK = 4,
	SS_BONE_BATCH_ENTRY_NBONES = 8,
	SS_BONE_BATCH_ENTRY_NFIRSTMATRIX = 12,
	SS_BONE_BATCH_ENTRY_NANSWERED = 16,
	SS_BONE_BATCH_ENTRY_NMODELINDEX = 20,
	SS_BONE_BATCH_ENTRY_NVIEWMODEL = 24,
	SS_BONE_BATCH_ENTRY_FLORIGIN = 28,
	SS_BONE_BATCH_ENTRY_FLANGLES = 40,
	SS_BONE_BATCH_ENTRY_FLRENDERORIGIN = 52,
	SS_BONE_BATCH_ENTRY_FLRENDERANGLES = 64,
	SS_BONE_BATCH_ENTRY_SIZE = 76,
	SS_BONE_BATCH_HEADER_NFRAMECOUNT = 0,
	SS_BONE_BATCH_HEADER_FLCURRENTTIME = 4,
	SS_BONE_BATCH_HEADER_NENTRIES = 8,
	SS_BONE_BATCH_HEADER_PSLOTTOENTRY = 16,
	SS_BONE_BATCH_HEADER_PENTRIES = 24,
	SS_BONE_BATCH_HEADER_PMATRICES = 32,
	SS_BONE_BATCH_HEADER_SIZE = 40,
};

static_assert(offsetof(SourceSharpBoneBatchEntry_t, hEntity) == SS_BONE_BATCH_ENTRY_HENTITY, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, nBoneMask) == SS_BONE_BATCH_ENTRY_NBONEMASK, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, nBones) == SS_BONE_BATCH_ENTRY_NBONES, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, nFirstMatrix) == SS_BONE_BATCH_ENTRY_NFIRSTMATRIX,
			  "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, nAnswered) == SS_BONE_BATCH_ENTRY_NANSWERED, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, nModelIndex) == SS_BONE_BATCH_ENTRY_NMODELINDEX,
			  "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, nViewModel) == SS_BONE_BATCH_ENTRY_NVIEWMODEL,
			  "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, flOrigin) == SS_BONE_BATCH_ENTRY_FLORIGIN, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, flAngles) == SS_BONE_BATCH_ENTRY_FLANGLES, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, flRenderOrigin) == SS_BONE_BATCH_ENTRY_FLRENDERORIGIN,
			  "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatchEntry_t, flRenderAngles) == SS_BONE_BATCH_ENTRY_FLRENDERANGLES,
			  "batch entry layout");
static_assert(sizeof(SourceSharpBoneBatchEntry_t) == SS_BONE_BATCH_ENTRY_SIZE, "batch entry layout");
static_assert(offsetof(SourceSharpBoneBatch_t, nFrameCount) == SS_BONE_BATCH_HEADER_NFRAMECOUNT, "batch header layout");
static_assert(offsetof(SourceSharpBoneBatch_t, flCurrentTime) == SS_BONE_BATCH_HEADER_FLCURRENTTIME,
			  "batch header layout");
static_assert(offsetof(SourceSharpBoneBatch_t, nEntries) == SS_BONE_BATCH_HEADER_NENTRIES, "batch header layout");
static_assert(offsetof(SourceSharpBoneBatch_t, pSlotToEntry) == SS_BONE_BATCH_HEADER_PSLOTTOENTRY,
			  "batch header layout");
static_assert(offsetof(SourceSharpBoneBatch_t, pEntries) == SS_BONE_BATCH_HEADER_PENTRIES, "batch header layout");
static_assert(offsetof(SourceSharpBoneBatch_t, pMatrices) == SS_BONE_BATCH_HEADER_PMATRICES, "batch header layout");
static_assert(sizeof(SourceSharpBoneBatch_t) == SS_BONE_BATCH_HEADER_SIZE, "batch header layout");

// The entry that answers an ask, or NULL.
inline const SourceSharpBoneBatchEntry_t* SourceSharp_BoneBatchEntry(
	const SourceSharpBoneBatch_t* pBatch, uint32_t hEntity, int32_t nBoneMask, float flCurrentTime, int32_t nFrameCount,
	int32_t nModelIndex, int32_t nViewModel, const float* pOrigin, const float* pAngles, const float* pRenderOrigin,
	const float* pRenderAngles)
{
	if (!pBatch || !pBatch->pSlotToEntry || pBatch->nFrameCount != nFrameCount ||
		pBatch->flCurrentTime != flCurrentTime)
		return NULL;

	const int16_t nEntry = pBatch->pSlotToEntry[hEntity & (SS_BONE_BATCH_SLOTS - 1)];

	if (nEntry < 0 || nEntry >= pBatch->nEntries)
		return NULL;

	const SourceSharpBoneBatchEntry_t* pEntry = &pBatch->pEntries[nEntry];

	if (pEntry->hEntity != hEntity || (nBoneMask & ~pEntry->nBoneMask) != 0 || pEntry->nModelIndex != nModelIndex ||
		pEntry->nViewModel != nViewModel)
		return NULL;

	for (int i = 0; i < 3; i++)
	{
		if (pEntry->flOrigin[i] != pOrigin[i] || pEntry->flAngles[i] != pAngles[i] ||
			pEntry->flRenderOrigin[i] != pRenderOrigin[i] || pEntry->flRenderAngles[i] != pRenderAngles[i])
			return NULL;
	}

	return pEntry;
}
