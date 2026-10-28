// Seconds on a steady clock since this DLL's first call, like Plat_FloatTime.
// The late base keeps values small enough for float fields to hold milliseconds.

#pragma once

#include <chrono>

namespace ss
{

inline double SecondsSinceStart()
{
	static const std::chrono::steady_clock::time_point s_Start = std::chrono::steady_clock::now();
	return std::chrono::duration<double>( std::chrono::steady_clock::now() - s_Start ).count();
}

} // namespace ss
