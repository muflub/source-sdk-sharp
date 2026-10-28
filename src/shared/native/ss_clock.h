//========= SourceSharp — seconds on a steady clock, from this DLL's start =====//
//
// tier0's Plat_FloatTime, for our own call sites, on std::chrono. Like
// Plat_FloatTime it counts from a base taken early (here: the first call in
// this DLL), so the value stays small enough for the float fields callers keep
// it in (mstudioanimdesc_t::zeroframestalltime) to hold milliseconds. Only
// differences and log stamps use it; nothing compares it with an engine time.
//
//=============================================================================//

#pragma once

#include <chrono>

namespace ss
{

inline double SecondsSinceStart()
{
	static const std::chrono::steady_clock::time_point s_Start = std::chrono::steady_clock::now();
	return std::chrono::duration< double >( std::chrono::steady_clock::now() - s_Start ).count();
}

} // namespace ss
