//========= SourceSharp =====================================================//
//
// THE VIDEO CONFIG FIELDS THE MANAGED VIDEO OPTIONS READ BY LAYOUT
// (plan_gameui_managed.md G2, src/client/interop/VideoInterfaces.cs).
//
// One case per offset of VideoConfigLayout, and IGameUIFuncs' mode record:
// the number here against the C++ declaration, the same number in
// VideoInterfacesTests against the C# constant.
//
//=============================================================================//

#include "materialsystem/materialsystem_config.h"
#include "modes.h"

#include <stddef.h>

#include "gtest/gtest.h"

TEST(VideoConfigLayout, ModeWidthIsAtZero)
{
	EXPECT_EQ(0u, offsetof(MaterialSystem_Config_t, m_VideoMode) + offsetof(MaterialVideoMode_t, m_Width));
}

TEST(VideoConfigLayout, ModeHeightIsAtFour)
{
	EXPECT_EQ(4u, offsetof(MaterialSystem_Config_t, m_VideoMode) + offsetof(MaterialVideoMode_t, m_Height));
}

TEST(VideoConfigLayout, FlagsAreAtFiftyTwo)
{
	EXPECT_EQ(52u, offsetof(MaterialSystem_Config_t, m_Flags));
}

TEST(VideoConfigLayout, WindowedIsTheFirstFlag)
{
	EXPECT_EQ(1u, (unsigned)MATSYS_VIDCFG_FLAGS_WINDOWED);
}

TEST(VideoConfigLayout, AModeIsFourInts)
{
	EXPECT_EQ(16u, sizeof(vmode_t));
	EXPECT_EQ(12u, offsetof(vmode_t, refreshRate));
}
