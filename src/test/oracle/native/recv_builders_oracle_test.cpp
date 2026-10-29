// engineabi/recv.h's builders and standard proxies against upstream's
// public/dt_recv.cpp, linked into the same binary (plan_client_novalve C8).
//
// Upstream's file supplies the Valve names (RecvPropInt, RecvProxy_FloatToFloat,
// g_StandardRecvProxies, RecvProp::RecvProp ...); ours supplies
// engineabi::recv::*. Each builder is called with the same arguments on both
// sides and the two props compared field by field; each proxy is run over the
// same decoded values and the bytes it wrote compared. Ours is handed
// upstream's g_StandardRecvProxies, so a default proxy must come out as the
// very pointer upstream picks.

#include "gtest/gtest.h"

#include "engineabi/recv.h"

#include <climits>
#include <cstring>
#include <random>
#include <string>
#include <vector>

// RecvPropArray3 in upstream's file names these, and nothing here calls it.
// dt_utlvector_common.cpp defines them in the DLLs; the oracle does not link it.
const char* DT_ArrayElementNameForIdx(size_t)
{
	return "000";
}

char* AllocateStringHelper(const char*, ...)
{
	return NULL;
}

namespace
{

void ExpectSameProp(const RecvProp& ours, const RecvProp& stock, const char* pWhat)
{
	SCOPED_TRACE(pWhat);
	EXPECT_EQ(ours.m_pVarName, stock.m_pVarName);
	EXPECT_EQ(ours.m_RecvType, stock.m_RecvType);
	EXPECT_EQ(ours.m_Flags, stock.m_Flags);
	EXPECT_EQ(ours.GetOffset(), stock.GetOffset());
	EXPECT_EQ(ours.GetProxyFn(), stock.GetProxyFn());
	EXPECT_EQ(ours.GetDataTableProxyFn(), stock.GetDataTableProxyFn());
	EXPECT_EQ(ours.GetDataTable(), stock.GetDataTable());
	EXPECT_EQ(ours.GetNumElements(), stock.GetNumElements());
	EXPECT_EQ(ours.GetElementStride(), stock.GetElementStride());
	EXPECT_EQ(ours.GetArrayLengthProxy(), stock.GetArrayLengthProxy());
	EXPECT_EQ(ours.GetArrayProp(), stock.GetArrayProp());
	EXPECT_EQ(ours.IsInsideArray(), stock.IsInsideArray());
	EXPECT_EQ(ours.GetExtraData(), stock.GetExtraData());
	if (stock.m_RecvType == DPT_String)
		EXPECT_EQ(ours.m_StringBufferSize, stock.m_StringBufferSize);
}

void OtherProxy(const CRecvProxyData*, void*, void*) {}

void OtherTableProxy(const RecvProp*, void**, void*, int) {}

void OtherLengthProxy(void*, int, int) {}

const char kName[] = "m_flTest";

std::mt19937& Rng()
{
	static std::mt19937 s_rng(20261011u);
	return s_rng;
}

} // namespace

TEST(RecvBuildersOracle, TheStandardTableIsUpstreamsAndNotEmpty)
{
	// The stimulus reaches the subject: every builder below defaults from it.
	EXPECT_EQ(g_StandardRecvProxies.m_Int32ToInt8, &RecvProxy_Int32ToInt8);
	EXPECT_EQ(g_StandardRecvProxies.m_Int32ToInt32, &RecvProxy_Int32ToInt32);
	EXPECT_EQ(g_StandardRecvProxies.m_FloatToFloat, &RecvProxy_FloatToFloat);
}

TEST(RecvBuildersOracle, IntPicksUpstreamsProxyForEveryWidth)
{
	for (int size : {1, 2, 4})
	{
		for (int flags : {0, SPROP_UNSIGNED, SPROP_CHANGES_OFTEN})
		{
			ExpectSameProp(engineabi::recv::Int(kName, 12, size, flags, NULL, g_StandardRecvProxies),
						   RecvPropInt(kName, 12, size, flags, NULL), "Int, default proxy");
			ExpectSameProp(engineabi::recv::Int(kName, 12, size, flags, OtherProxy, g_StandardRecvProxies),
						   RecvPropInt(kName, 12, size, flags, OtherProxy), "Int, named proxy");
		}
	}
	// The control: the widths really do pick different proxies.
	EXPECT_NE(RecvPropInt(kName, 0, 1, 0, NULL).GetProxyFn(), RecvPropInt(kName, 0, 4, 0, NULL).GetProxyFn());
}

TEST(RecvBuildersOracle, FloatVectorStringAndTableMatchUpstream)
{
	ExpectSameProp(engineabi::recv::Float(kName, 40, 4, SPROP_NOSCALE, RecvProxy_FloatToFloat, g_StandardRecvProxies),
				   RecvPropFloat(kName, 40, 4, SPROP_NOSCALE, RecvProxy_FloatToFloat), "Float");
	ExpectSameProp(engineabi::recv::Float(kName, 40, 0, 0, OtherProxy, g_StandardRecvProxies),
				   RecvPropFloat(kName, 40, 0, 0, OtherProxy), "Float, named proxy");
	ExpectSameProp(engineabi::recv::Vector(kName, 64, 12, SPROP_COORD, RecvProxy_VectorToVector, g_StandardRecvProxies),
				   RecvPropVector(kName, 64, 12, SPROP_COORD, RecvProxy_VectorToVector), "Vector");
	ExpectSameProp(engineabi::recv::String(kName, 8, 260, 0, RecvProxy_StringToString),
				   RecvPropString(kName, 8, 260, 0, RecvProxy_StringToString), "String");

	RecvTable table;
	ExpectSameProp(
		engineabi::recv::DataTable(kName, 16, SPROP_PROXY_ALWAYS_YES, &table, DataTableRecvProxy_StaticDataTable),
		RecvPropDataTable(kName, 16, SPROP_PROXY_ALWAYS_YES, &table, DataTableRecvProxy_StaticDataTable), "DataTable");
	ExpectSameProp(engineabi::recv::DataTable(kName, 16, 0, &table, OtherTableProxy),
				   RecvPropDataTable(kName, 16, 0, &table, OtherTableProxy), "DataTable, named proxy");
	ExpectSameProp(engineabi::recv::ArrayLength(33, 4, kName, OtherLengthProxy),
				   InternalRecvPropArray(33, 4, kName, OtherLengthProxy), "ArrayLength");
}

TEST(RecvBuildersOracle, TheIntegerProxiesWriteUpstreamsBytes)
{
	const RecvVarProxyFn ours[] = {engineabi::recv::Int32ToInt8, engineabi::recv::Int32ToInt16,
								   engineabi::recv::Int32ToInt32};
	const RecvVarProxyFn stock[] = {RecvProxy_Int32ToInt8, RecvProxy_Int32ToInt16, RecvProxy_Int32ToInt32};
	std::uniform_int_distribution<int> any(INT_MIN, INT_MAX);

	int nCompared = 0;
	for (int round = 0; round < 2000; round++)
	{
		CRecvProxyData data;
		memset(&data, 0, sizeof(data));
		const int kEdges[] = {0, -1, 255, 65536};
		data.m_Value.m_Int = (round < 4) ? kEdges[round] : any(Rng());
		for (int k = 0; k < 3; k++)
		{
			unsigned char a[8], b[8];
			memset(a, 0xAB, sizeof(a));
			memset(b, 0xAB, sizeof(b));
			ours[k](&data, NULL, a);
			stock[k](&data, NULL, b);
			ASSERT_EQ(0, memcmp(a, b, sizeof(a))) << "proxy " << k << " value " << data.m_Value.m_Int;
			nCompared++;
		}
	}
	EXPECT_EQ(6000, nCompared);
}

TEST(RecvBuildersOracle, TheFloatAndVectorProxiesWriteUpstreamsBytes)
{
	std::uniform_real_distribution<float> any(-65536.f, 65536.f);
	for (int round = 0; round < 2000; round++)
	{
		CRecvProxyData data;
		memset(&data, 0, sizeof(data));
		for (int i = 0; i < 3; i++)
			data.m_Value.m_Vector[i] = any(Rng());

		float a[4] = {1, 2, 3, 4}, b[4] = {1, 2, 3, 4};
		engineabi::recv::FloatToFloat(&data, NULL, a);
		RecvProxy_FloatToFloat(&data, NULL, b);
		ASSERT_EQ(0, memcmp(a, b, sizeof(a)));

		float c[4] = {1, 2, 3, 4}, d[4] = {1, 2, 3, 4};
		engineabi::recv::VectorToVector(&data, NULL, c);
		RecvProxy_VectorToVector(&data, NULL, d);
		ASSERT_EQ(0, memcmp(c, d, sizeof(c)));
	}
}

TEST(RecvBuildersOracle, TheStringProxyWritesUpstreamsBytesForEveryBufferSize)
{
	const std::vector<std::string> texts = {"", "a", "player", "exactly8", std::string(300, 'x')};
	int nCompared = 0;
	for (int size : {0, -1, 1, 2, 8, 9, 64, 260})
	{
		RecvProp prop = RecvPropString(kName, 0, size, 0, RecvProxy_StringToString);
		for (const std::string& text : texts)
		{
			CRecvProxyData data;
			memset(&data, 0, sizeof(data));
			data.m_pRecvProp = &prop;
			data.m_Value.m_pString = text.c_str();

			char a[512], b[512];
			memset(a, '#', sizeof(a));
			memset(b, '#', sizeof(b));
			engineabi::recv::StringToString(&data, NULL, a);
			RecvProxy_StringToString(&data, NULL, b);
			ASSERT_EQ(0, memcmp(a, b, sizeof(a))) << "size " << size << " text '" << text << "'";
			nCompared++;
		}
	}
	EXPECT_EQ(40, nCompared);
}

TEST(RecvBuildersOracle, TheStaticTableProxyAnswersTheSameObject)
{
	int object = 0;
	void *pOurs = NULL, *pStock = NULL;
	engineabi::recv::StaticDataTable(NULL, &pOurs, &object, 7);
	DataTableRecvProxy_StaticDataTable(NULL, &pStock, &object, 7);
	EXPECT_EQ(pStock, pOurs);
	EXPECT_EQ(static_cast<void*>(&object), pOurs);
}
