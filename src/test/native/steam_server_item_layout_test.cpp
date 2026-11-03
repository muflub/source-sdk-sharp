//========= SourceSharp =====================================================//
//
// THE STEAM SERVER FIELDS THE MANAGED SERVER BROWSER READS BY LAYOUT
// (plan_gameui_managed.md G3, src/client/interop/SteamServerBrowser.cs).
//
// One case per offset of SteamServerItemLayout: the number here against the
// C++ declaration, the same number in SteamServerBrowserTests against the C#
// constant. The server's name is private: its offset is found through its
// own accessor, once a name is set.
//
//=============================================================================//

#include "steam/steamtypes.h"
#include "steam/steamclientpublic.h"
#include "steam/matchmakingtypes.h"

#include <stddef.h>

#include "gtest/gtest.h"

namespace
{
size_t At(const gameserveritem_t& item, const void* pMember)
{
	return (size_t)((const char*)pMember - (const char*)&item);
}
}

TEST(SteamServerItemLayout, TheAddressIsConnectionThenQueryPortThenIp)
{
	gameserveritem_t item;
	item.m_NetAdr.Init(0x01020304, 1111, 2222);
	unsigned short ports[2];
	unsigned int ip;
	memcpy(ports, &item, sizeof(ports));
	memcpy(&ip, (const char*)&item + 4, sizeof(ip));
	EXPECT_EQ(2222, ports[0]);
	EXPECT_EQ(1111, ports[1]);
	EXPECT_EQ(0x01020304u, ip);
}

TEST(SteamServerItemLayout, TheFieldsAreWhereManagedReadsThem)
{
	gameserveritem_t item;
	item.SetName("named"); // GetName answers the address string while the name is empty
	EXPECT_EQ(8u, At(item, &item.m_nPing));
	EXPECT_EQ(12u, At(item, &item.m_bHadSuccessfulResponse));
	EXPECT_EQ(14u, At(item, item.m_szGameDir));
	EXPECT_EQ(46u, At(item, item.m_szMap));
	EXPECT_EQ(78u, At(item, item.m_szGameDescription));
	EXPECT_EQ(148u, At(item, &item.m_nPlayers));
	EXPECT_EQ(152u, At(item, &item.m_nMaxPlayers));
	EXPECT_EQ(156u, At(item, &item.m_nBotPlayers));
	EXPECT_EQ(160u, At(item, &item.m_bPassword));
	EXPECT_EQ(161u, At(item, &item.m_bSecure));
	EXPECT_EQ(172u, At(item, item.GetName()));
}

TEST(SteamServerItemLayout, TheStringsAreTheirDeclaredSizes)
{
	EXPECT_EQ(32, k_cbMaxGameServerGameDir);
	EXPECT_EQ(32, k_cbMaxGameServerMapName);
	EXPECT_EQ(64, k_cbMaxGameServerGameDescription);
	EXPECT_EQ(64, k_cbMaxGameServerName);
	EXPECT_EQ(512u, sizeof(MatchMakingKeyValuePair_t));
	EXPECT_EQ(256u, offsetof(MatchMakingKeyValuePair_t, m_szValue));
}
