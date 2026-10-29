//========= SourceSharp — vcollide_t as the managed side reads it ============//
//
// C# reads a model's collision keyvalue text in place:
// IVModelInfo::GetVCollide( index ) answers a vcollide_t the engine owns, and
// SourceSharp.Abi.VCollideLayout (EngineModelInfo.cs) reads pKeyValues at a
// fixed offset. These pin that offset, and the size, to the PRISTINE
// public/vcollide.h; VCollideLayoutTests pins the managed struct to the same
// numbers.
//
//=============================================================================//

#include <cstddef>

class CPhysCollide;
#include "vcollide.h"

#include "gtest/gtest.h"

TEST(VCollideLayout, TheKeyValueTextIsAtOffsetSixteen)
{
	EXPECT_EQ(16u, offsetof(vcollide_t, pKeyValues));
}

TEST(VCollideLayout, TheSolidsPointerIsAtOffsetEight)
{
	EXPECT_EQ(8u, offsetof(vcollide_t, solids));
}

TEST(VCollideLayout, TheStructIsTwentyFourBytes)
{
	EXPECT_EQ(24u, sizeof(vcollide_t));
}
