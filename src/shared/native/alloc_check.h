// Debug-build allocation checker: -Wl,--wrap= hooks on malloc and operator
// new/delete keep a side table of every block this DLL allocates and report
// mismatched releases and frees of blocks it did not allocate.
// SOURCESHARP_ALLOC_CHECK=report|abort|off picks what a finding does (default
// report); SOURCESHARP_ALLOC_CHECK_LOG names a log file. Blocks are not altered.

#pragma once

#include <stddef.h>
#include <stdint.h>

struct SourceSharpAllocCheckCounts
{
	uint64_t allocations;
	uint64_t releases;
	uint64_t mismatches;
	uint64_t foreignFrees;
	uint64_t live;
};

extern "C" SourceSharpAllocCheckCounts SourceSharp_AllocCheckCounts();

// Writes counts, foreign-free sites and the largest live sites, tagged `when`.
extern "C" void SourceSharp_AllocCheckReport(const char* when);

// Tests only: forget every record and zero the counters.
extern "C" void SourceSharp_AllocCheckResetForTest();
