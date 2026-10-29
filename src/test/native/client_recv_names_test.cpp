// The Valve names client.so links for the recv tables (engineabi_recv_names.cpp,
// plan_client_novalve C8): RecvProp's and RecvTable's starting state, the
// standard proxy table, and the pointer identity the engine's local backdoor
// relies on. The builders' fields and the proxies' bytes are the oracle's
// (recv_builders_oracle_test); this holds what the oracle cannot link beside
// upstream's file, which defines the same names.

#include "gtest/gtest.h"

#include "engineabi/recv.h"
#include "client_class.h"

TEST(ClientRecvNames, AFreshPropIsOneUnnamedScalarIntWithNothingAttached)
{
	RecvProp prop;
	EXPECT_EQ(NULL, prop.m_pVarName);
	EXPECT_EQ(DPT_Int, prop.m_RecvType);
	EXPECT_EQ(0, prop.m_Flags);
	EXPECT_EQ(0, prop.m_StringBufferSize);
	EXPECT_EQ(0, prop.GetOffset());
	EXPECT_EQ(1, prop.GetNumElements());
	EXPECT_EQ(-1, prop.GetElementStride());
	EXPECT_EQ(NULL, prop.GetProxyFn());
	EXPECT_EQ(NULL, prop.GetDataTableProxyFn());
	EXPECT_EQ(NULL, prop.GetDataTable());
	EXPECT_EQ(NULL, prop.GetArrayProp());
	EXPECT_EQ(NULL, prop.GetArrayLengthProxy());
	EXPECT_EQ(NULL, prop.GetExtraData());
	EXPECT_EQ(NULL, prop.GetParentArrayPropName());
	EXPECT_FALSE(prop.IsInsideArray());
}

TEST(ClientRecvNames, ATableStartsUnreadByTheEngine)
{
	RecvProp props[3];
	RecvTable table(props, 3, "DT_Test");
	EXPECT_EQ(props, table.m_pProps);
	EXPECT_EQ(3, table.m_nProps);
	EXPECT_STREQ("DT_Test", table.m_pNetTableName);
	EXPECT_EQ(NULL, table.m_pDecoder);
	EXPECT_FALSE(table.IsInitialized());
	EXPECT_FALSE(table.IsInMainList());

	RecvTable empty;
	EXPECT_EQ(NULL, empty.m_pProps);
	EXPECT_EQ(0, empty.m_nProps);
	EXPECT_EQ(NULL, empty.m_pNetTableName);
}

TEST(ClientRecvNames, TheStandardTableHoldsTheValveNamedProxies)
{
	EXPECT_EQ(&RecvProxy_Int32ToInt8, g_StandardRecvProxies.m_Int32ToInt8);
	EXPECT_EQ(&RecvProxy_Int32ToInt16, g_StandardRecvProxies.m_Int32ToInt16);
	EXPECT_EQ(&RecvProxy_Int32ToInt32, g_StandardRecvProxies.m_Int32ToInt32);
	EXPECT_EQ(&RecvProxy_FloatToFloat, g_StandardRecvProxies.m_FloatToFloat);
	EXPECT_EQ(&RecvProxy_VectorToVector, g_StandardRecvProxies.m_VectorToVector);
}

TEST(ClientRecvNames, ADefaultIntProxyIsThePointerTheEngineComparesAgainst)
{
	// LocalNetworkBackdoor / dt_localtransfer copy directly when a prop's proxy
	// IS the standard table's: identity, not behaviour.
	EXPECT_EQ(g_StandardRecvProxies.m_Int32ToInt8, RecvPropInt("a", 0, 1, 0, NULL).GetProxyFn());
	EXPECT_EQ(g_StandardRecvProxies.m_Int32ToInt16, RecvPropInt("a", 0, 2, 0, NULL).GetProxyFn());
	EXPECT_EQ(g_StandardRecvProxies.m_Int32ToInt32, RecvPropInt("a", 0, 4, 0, NULL).GetProxyFn());
	EXPECT_EQ(g_StandardRecvProxies.m_FloatToFloat, RecvPropFloat("f", 0, 4, 0, RecvProxy_FloatToFloat).GetProxyFn());
}

TEST(ClientRecvNames, TheValveNamedProxiesDoTheWork)
{
	CRecvProxyData data;
	data.m_Value.m_Int = 0x12345678;
	unsigned short out = 0;
	RecvProxy_Int32ToInt16(&data, NULL, &out);
	EXPECT_EQ(0x5678, out);
}
