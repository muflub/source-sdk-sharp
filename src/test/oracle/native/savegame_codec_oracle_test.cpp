//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// THE SAVE CODEC ORACLE: stock's CSave::WriteFields and CRestore::ReadFields,
// the code that encoded every SaveWriteFields / SaveReadFields block the
// engine hands a game DLL (the client's DECALLIST and MUSICLIST, the server's
// GameHeader, Save Header, ADJACENCY and LIGHTSTYLE).
//
// What runs is stock's own code, from the PRISTINE clone at valve-sdk.lock:
//   - game/shared/saverestore.cpp's field path, cut out of the file by the
//     CMake (savegame_subject/saverestore_cut.inl) between named markers:
//     gSizes, CSave's constructor, DataEmpty, the primitive and named writes,
//     ShouldSaveField, WriteBasicField, WriteField, WriteFields, the block
//     framing (StartBlock, EndBlock, BufferField, WriteHeader, BufferData),
//     WriteTime, WritePositionVector, WriteGameField; CRestore's constructor,
//     ReadBasicField, ReadField, ShouldReadField, FindField, EmptyFields,
//     ReadFields, ReadHeader, the primitive reads (ReadSimple), ReadGameField,
//     ReadTime and ReadPositionVector. The whole file cannot compile outside a
//     game DLL; these functions need only what the stand-ins give them.
//   - game/shared/saverestore.h (CSave, CRestore), public/isaverestore.h,
//     public/saverestoretypes.h (CSaveRestoreData, its cursor and token table)
//     and public/datamap.h, copied into savegame_subject/.
//
// STAND-IN, NOT STOCK (each is plumbing no field of these maps reaches):
//   - filesystem.h: FileHandle_t only (CSave's log handle; nothing logs).
//   - modelinfo, AllocPooledString, GetMaterialNameFromIndex, GetMaterialIndex:
//     the model, string and material arms of WriteGameField/ReadGameField.
//   - The entity, edict, handle, tick and function members, which abort: no
//     field of these maps is of those types (levellist_t's FIELD_EDICT is
//     always NULL, which ShouldSaveField skips before WriteEdictPtr).
//
// The maps are the engine's own (the engine's datadescs, field for field):
// GAME_HEADER, SAVE_HEADER, levellist_t, SAVELIGHTSTYLE, musicsave_t and
// decallist_t, plus WRITER VARIANTS of the last two -- reordered, renamed in
// case, missing fields, an extra field, a narrower and a wider char array, an
// int where the reader has a short -- each written by stock and read back by
// stock under the real map, which is how a reader meets a file written by
// another build. The values are seeded (xorshift32, seed 0x5A7E0C0D): zero
// fields (stock omits them), the time sentinels and the clamp around the base
// time, vec3_invalid, a landmark used and not, garbage after a string's NUL.
//
// THE GOLDEN, src/test/client/Fixtures/savegame-stock.tsv, is what this code
// wrote; SaveCodecOracleTests (SourceSharp.Client.Tests) holds the managed
// codec (src/shared/interop/SaveBlockCodec.cs) to it byte for byte, both ways:
// it writes every input and must produce stock's bytes, and reads stock's
// bytes and must fill the struct as stock did. Regenerating the golden to make
// a test green deletes the check: it changes only when the scenario does, and
// then on purpose (touch savegame_golden.regen beside this file, run, delete
// the marker, and say why in the commit).

#include <cfloat>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "mathlib_test_env.h"

#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
#include "datamap.h"
#include "string_t.h"
#include "const.h"
#include "saverestoretypes.h"
#include "saverestore.h"

//-----------------------------------------------------------------------------
// The stand-ins the cut code names.
//-----------------------------------------------------------------------------
struct model_t;

namespace
{
class COracleModelInfo
{
public:
	const model_t* GetModel(int) { return NULL; }

	const char* GetModelName(const model_t*) { return ""; }

	int GetModelIndex(const char*) { return -1; }
};

COracleModelInfo g_OracleModelInfo;

[[noreturn]] void NotTheSubject(const char* pszWhat)
{
	fprintf(stderr,
			"savegame_codec_oracle_test: %s is a stand-in and was called -- a field "
			"type outside the oracle's maps reached it\n",
			pszWhat);
	abort();
}
} // namespace

static COracleModelInfo* modelinfo = &g_OracleModelInfo;

static string_t AllocPooledString(const char* pszValue)
{
	static std::set<std::string> s_Pool;
	return MAKE_STRING(s_Pool.insert(pszValue).first->c_str());
}

static const char* GetMaterialNameFromIndex(int)
{
	return NULL;
}

static int GetMaterialIndex(const char*)
{
	return 0;
}

#include "saverestore_cut.inl"

void CSave::StartLogging(const char*)
{
	NotTheSubject("CSave::StartLogging");
}

void CSave::EndLogging(void)
{
	NotTheSubject("CSave::EndLogging");
}

int CSave::EntityIndex(const edict_t*)
{
	NotTheSubject("CSave::EntityIndex( edict_t )");
}

int CSave::EntityIndex(const CBaseEntity*)
{
	NotTheSubject("CSave::EntityIndex( CBaseEntity )");
}

int CSave::EntityFlagsSet(int, int)
{
	NotTheSubject("CSave::EntityFlagsSet");
}

void CSave::WriteTick(const char*, const int*, int)
{
	NotTheSubject("CSave::WriteTick");
}

void CSave::WriteTick(const int*, int)
{
	NotTheSubject("CSave::WriteTick");
}

void CSave::WriteFunction(datamap_t*, const char*, inputfunc_t**, int)
{
	NotTheSubject("CSave::WriteFunction");
}

void CSave::WriteEntityPtr(const char*, CBaseEntity**, int)
{
	NotTheSubject("CSave::WriteEntityPtr");
}

void CSave::WriteEntityPtr(CBaseEntity**, int)
{
	NotTheSubject("CSave::WriteEntityPtr");
}

void CSave::WriteEdictPtr(const char*, edict_t**, int)
{
	NotTheSubject("CSave::WriteEdictPtr");
}

void CSave::WriteEdictPtr(edict_t**, int)
{
	NotTheSubject("CSave::WriteEdictPtr");
}

void CSave::WriteEHandle(const char*, const EHANDLE*, int)
{
	NotTheSubject("CSave::WriteEHandle");
}

void CSave::WriteEHandle(const EHANDLE*, int)
{
	NotTheSubject("CSave::WriteEHandle");
}

CBaseEntity* CRestore::EntityFromIndex(int)
{
	NotTheSubject("CRestore::EntityFromIndex");
}

int CRestore::ReadEntityPtr(CBaseEntity**, int, int)
{
	NotTheSubject("CRestore::ReadEntityPtr");
}

int CRestore::ReadEdictPtr(edict_t**, int, int)
{
	NotTheSubject("CRestore::ReadEdictPtr");
}

int CRestore::ReadEHandle(EHANDLE*, int, int)
{
	NotTheSubject("CRestore::ReadEHandle");
}

int CRestore::ReadTick(int*, int, int)
{
	NotTheSubject("CRestore::ReadTick");
}

int CRestore::ReadFunction(datamap_t*, inputfunc_t**, int, int)
{
	NotTheSubject("CRestore::ReadFunction");
}

namespace
{

//-----------------------------------------------------------------------------
// The engine's structs, member for member (the engine's own declarations).
//-----------------------------------------------------------------------------
struct GameHeader_t
{
	int mapCount;
	char mapName[32];
	char comment[80];
	char originMapName[32];
	char landmark[256];
};

struct SaveHeader_t
{
	int saveId;
	int version;
	int skillLevel;
	int connectionCount;
	int lightStyleCount;
	int mapVersion;
	float time__USE_VCR_MODE;
	char mapName[32];
	char skyName[32];
};

struct LevelList_t
{
	char mapName[32];
	char landmarkName[32];
	edict_t* pentLandmark;
	Vector vecLandmarkOrigin;
};

struct SaveLightStyle_t
{
	int index;
	char style[64];
};

struct MusicSave_t
{
	char songname[128];
	int sampleposition;
	short master_volume;
};

struct DecalList_t
{
	Vector position;
	char name[128];
	short entityIndex;
	unsigned char depth;
	unsigned char flags;
	Vector impactPlaneNormal;
};

// The writer variants' structs.
struct DecalExtra_t
{
	DecalList_t decal;
	int lifetime;
};

struct DecalIntIndex_t
{
	Vector position;
	char name[128];
	int entityIndex;
	unsigned char flags;
	Vector impactPlaneNormal;
};

struct MusicNarrow_t
{
	char songname[64];
	int sampleposition;
	short master_volume;
};

struct MusicWide_t
{
	char songname[200];
	int sampleposition;
	short master_volume;
};

//-----------------------------------------------------------------------------
// A map: what DEFINE_FIELD / DEFINE_ARRAY build, by hand (the datadesc macros
// need a class's static members).
//-----------------------------------------------------------------------------
struct Map_t
{
	std::string name;
	int structBytes;
	std::vector<typedescription_t> fields;
};

void Add(Map_t& map, fieldtype_t type, const char* pszName, int offset, int count, int sizeInBytes)
{
	typedescription_t f;
	memset(&f, 0, sizeof(f));
	f.fieldType = type;
	f.fieldName = pszName;
	f.fieldOffset[TD_OFFSET_NORMAL] = offset;
	f.fieldSize = (unsigned short)count;
	f.flags = FTYPEDESC_SAVE;
	f.fieldSizeInBytes = sizeInBytes;
	map.fields.push_back(f);
}

#define ADD_FIELD(map, S, member, type) \
	Add(map, type, #member, (int)offsetof(S, member), 1, (int)sizeof(((S*)0)->member))
#define ADD_ARRAY(map, S, member, type, count) \
	Add(map, type, #member, (int)offsetof(S, member), count, (int)sizeof(((S*)0)->member))

std::vector<Map_t> Maps()
{
	std::vector<Map_t> maps;

	Map_t game = {"GAME_HEADER", (int)sizeof(GameHeader_t), {}};
	ADD_FIELD(game, GameHeader_t, mapCount, FIELD_INTEGER);
	ADD_ARRAY(game, GameHeader_t, mapName, FIELD_CHARACTER, 32);
	ADD_ARRAY(game, GameHeader_t, comment, FIELD_CHARACTER, 80);
	ADD_ARRAY(game, GameHeader_t, originMapName, FIELD_CHARACTER, 32);
	ADD_ARRAY(game, GameHeader_t, landmark, FIELD_CHARACTER, 256);
	maps.push_back(game);

	Map_t save = {"SAVE_HEADER", (int)sizeof(SaveHeader_t), {}};
	ADD_FIELD(save, SaveHeader_t, skillLevel, FIELD_INTEGER);
	ADD_FIELD(save, SaveHeader_t, connectionCount, FIELD_INTEGER);
	ADD_FIELD(save, SaveHeader_t, lightStyleCount, FIELD_INTEGER);
	ADD_FIELD(save, SaveHeader_t, mapVersion, FIELD_INTEGER);
	ADD_FIELD(save, SaveHeader_t, time__USE_VCR_MODE, FIELD_TIME);
	ADD_ARRAY(save, SaveHeader_t, mapName, FIELD_CHARACTER, 32);
	ADD_ARRAY(save, SaveHeader_t, skyName, FIELD_CHARACTER, 32);
	maps.push_back(save);

	Map_t level = {"levellist_t", (int)sizeof(LevelList_t), {}};
	ADD_ARRAY(level, LevelList_t, mapName, FIELD_CHARACTER, 32);
	ADD_ARRAY(level, LevelList_t, landmarkName, FIELD_CHARACTER, 32);
	ADD_FIELD(level, LevelList_t, pentLandmark, FIELD_EDICT);
	ADD_FIELD(level, LevelList_t, vecLandmarkOrigin, FIELD_VECTOR);
	maps.push_back(level);

	Map_t light = {"SAVELIGHTSTYLE", (int)sizeof(SaveLightStyle_t), {}};
	ADD_FIELD(light, SaveLightStyle_t, index, FIELD_INTEGER);
	ADD_ARRAY(light, SaveLightStyle_t, style, FIELD_CHARACTER, 64);
	maps.push_back(light);

	Map_t music = {"musicsave_t", (int)sizeof(MusicSave_t), {}};
	ADD_ARRAY(music, MusicSave_t, songname, FIELD_CHARACTER, 128);
	ADD_FIELD(music, MusicSave_t, sampleposition, FIELD_INTEGER);
	ADD_FIELD(music, MusicSave_t, master_volume, FIELD_SHORT);
	maps.push_back(music);

	Map_t decal = {"decallist_t", (int)sizeof(DecalList_t), {}};
	ADD_FIELD(decal, DecalList_t, position, FIELD_POSITION_VECTOR);
	ADD_ARRAY(decal, DecalList_t, name, FIELD_CHARACTER, 128);
	ADD_FIELD(decal, DecalList_t, entityIndex, FIELD_SHORT);
	ADD_FIELD(decal, DecalList_t, flags, FIELD_CHARACTER);
	ADD_FIELD(decal, DecalList_t, impactPlaneNormal, FIELD_VECTOR);
	maps.push_back(decal);

	// The writer variants, each read back under the real map above.
	Map_t reordered = {"decallist_t:reordered", (int)sizeof(DecalList_t), {}};
	ADD_FIELD(reordered, DecalList_t, impactPlaneNormal, FIELD_VECTOR);
	ADD_FIELD(reordered, DecalList_t, flags, FIELD_CHARACTER);
	ADD_FIELD(reordered, DecalList_t, entityIndex, FIELD_SHORT);
	ADD_ARRAY(reordered, DecalList_t, name, FIELD_CHARACTER, 128);
	ADD_FIELD(reordered, DecalList_t, position, FIELD_POSITION_VECTOR);
	maps.push_back(reordered);

	Map_t cased = {"decallist_t:case", (int)sizeof(DecalList_t), {}};
	Add(cased, FIELD_POSITION_VECTOR, "POSITION", (int)offsetof(DecalList_t, position), 1, (int)sizeof(Vector));
	Add(cased, FIELD_CHARACTER, "Name", (int)offsetof(DecalList_t, name), 128, 128);
	Add(cased, FIELD_SHORT, "EntityIndex", (int)offsetof(DecalList_t, entityIndex), 1, 2);
	Add(cased, FIELD_CHARACTER, "fLaGs", (int)offsetof(DecalList_t, flags), 1, 1);
	Add(cased, FIELD_VECTOR, "impactplanenormal", (int)offsetof(DecalList_t, impactPlaneNormal), 1,
		(int)sizeof(Vector));
	maps.push_back(cased);

	Map_t missing = {"decallist_t:missing", (int)sizeof(DecalList_t), {}};
	ADD_FIELD(missing, DecalList_t, position, FIELD_POSITION_VECTOR);
	ADD_FIELD(missing, DecalList_t, entityIndex, FIELD_SHORT);
	ADD_FIELD(missing, DecalList_t, impactPlaneNormal, FIELD_VECTOR);
	maps.push_back(missing);

	Map_t extra = {"decallist_t:extra", (int)sizeof(DecalExtra_t), {}};
	Add(extra, FIELD_POSITION_VECTOR, "position", (int)offsetof(DecalExtra_t, decal.position), 1, (int)sizeof(Vector));
	Add(extra, FIELD_INTEGER, "lifetime", (int)offsetof(DecalExtra_t, lifetime), 1, 4);
	Add(extra, FIELD_CHARACTER, "name", (int)offsetof(DecalExtra_t, decal.name), 128, 128);
	Add(extra, FIELD_SHORT, "entityIndex", (int)offsetof(DecalExtra_t, decal.entityIndex), 1, 2);
	Add(extra, FIELD_CHARACTER, "flags", (int)offsetof(DecalExtra_t, decal.flags), 1, 1);
	Add(extra, FIELD_VECTOR, "impactPlaneNormal", (int)offsetof(DecalExtra_t, decal.impactPlaneNormal), 1,
		(int)sizeof(Vector));
	maps.push_back(extra);

	Map_t intIndex = {"decallist_t:intindex", (int)sizeof(DecalIntIndex_t), {}};
	ADD_FIELD(intIndex, DecalIntIndex_t, position, FIELD_POSITION_VECTOR);
	ADD_ARRAY(intIndex, DecalIntIndex_t, name, FIELD_CHARACTER, 128);
	ADD_FIELD(intIndex, DecalIntIndex_t, entityIndex, FIELD_INTEGER);
	ADD_FIELD(intIndex, DecalIntIndex_t, flags, FIELD_CHARACTER);
	ADD_FIELD(intIndex, DecalIntIndex_t, impactPlaneNormal, FIELD_VECTOR);
	maps.push_back(intIndex);

	Map_t narrow = {"musicsave_t:narrow", (int)sizeof(MusicNarrow_t), {}};
	ADD_ARRAY(narrow, MusicNarrow_t, songname, FIELD_CHARACTER, 64);
	ADD_FIELD(narrow, MusicNarrow_t, sampleposition, FIELD_INTEGER);
	ADD_FIELD(narrow, MusicNarrow_t, master_volume, FIELD_SHORT);
	maps.push_back(narrow);

	Map_t wide = {"musicsave_t:wide", (int)sizeof(MusicWide_t), {}};
	ADD_ARRAY(wide, MusicWide_t, songname, FIELD_CHARACTER, 200);
	ADD_FIELD(wide, MusicWide_t, sampleposition, FIELD_INTEGER);
	ADD_FIELD(wide, MusicWide_t, master_volume, FIELD_SHORT);
	maps.push_back(wide);

	return maps;
}

const Map_t& MapNamed(const std::vector<Map_t>& maps, const std::string& name)
{
	for (const Map_t& m : maps)
	{
		if (m.name == name)
			return m;
	}
	NotTheSubject(name.c_str());
}

// The real map a variant is read under (its name before the ':').
std::string ReaderOf(const std::string& variant)
{
	return variant.substr(0, variant.find(':'));
}

// The block name the engine writes a map's struct under.
const char* BlockOf(const std::string& map)
{
	const std::string real = ReaderOf(map);
	if (real == "GAME_HEADER")
		return "GameHeader";
	if (real == "SAVE_HEADER")
		return "Save Header";
	if (real == "levellist_t")
		return "ADJACENCY";
	if (real == "SAVELIGHTSTYLE")
		return "LIGHTSTYLE";
	if (real == "musicsave_t")
		return "MUSICLIST";
	return "DECALLIST";
}

//-----------------------------------------------------------------------------
// The seeded values.
//-----------------------------------------------------------------------------
class CRandom
{
public:
	explicit CRandom(uint32_t seed)
		: m_state(seed)
	{
	}

	uint32_t Next()
	{
		m_state ^= m_state << 13;
		m_state ^= m_state >> 17;
		m_state ^= m_state << 5;
		return m_state;
	}

	int Below(int n) { return (int)(Next() % (uint32_t)n); }

	float Range(float lo, float hi) { return lo + (hi - lo) * ((float)(Next() & 0xffffff) / (float)0x1000000); }

private:
	uint32_t m_state;
};

uint32_t Bits(float f)
{
	uint32_t u;
	memcpy(&u, &f, 4);
	return u;
}

std::string Hex(const void* p, int n)
{
	static const char kDigits[] = "0123456789abcdef";
	std::string s;
	s.reserve((size_t)n * 2);
	for (int i = 0; i < n; i++)
	{
		const unsigned char b = ((const unsigned char*)p)[i];
		s += kDigits[b >> 4];
		s += kDigits[b & 15];
	}
	return s;
}

std::string FloatHex(float f)
{
	char sz[16];
	snprintf(sz, sizeof(sz), "%08x", Bits(f));
	return sz;
}

// A time the way the save sees one: the sentinels, zero (both signs), the
// clamp around the base time, and ordinary times either side of it.
float RandomTime(CRandom& r, float base)
{
	switch (r.Below(10))
	{
		case 0:
			return 0.0f;
		case 1:
			return -0.0f;
		case 2:
			return base;
		case 3:
			return base + 0.0004f;
		case 4:
			return base - 0.0004f;
		case 5:
			return -FLT_MAX; // INVALID_TIME
		case 6:
			return FLT_MAX;
		case 7:
			return r.Range(0.0f, 4000.0f);
		default:
			return base + r.Range(-100.0f, 100.0f);
	}
}

void RandomVector(CRandom& r, float* v, bool bPosition)
{
	const int pick = r.Below(8);
	for (int i = 0; i < 3; i++)
		v[i] = r.Range(-4096.0f, 4096.0f);

	if (pick == 0)
		v[0] = v[1] = v[2] = 0.0f;
	else if (pick == 1)
		v[r.Below(3)] = 0.0f;
	else if (bPosition && pick == 2)
		v[0] = v[1] = v[2] = FLT_MAX; // vec3_invalid
	else if (bPosition && pick == 3)
		v[r.Below(3)] = FLT_MAX; // one component only: not vec3_invalid
}

void RandomChars(CRandom& r, char* p, int count)
{
	memset(p, 0, (size_t)count);
	if (count == 1)
	{
		p[0] = r.Below(3) == 0 ? 0 : (char)(1 + r.Below(255));
		return;
	}

	const int len = r.Below(4) == 0 ? 0 : r.Below(count);
	for (int i = 0; i < len; i++)
		p[i] = (char)('a' + r.Below(26));

	// Garbage after the NUL: stock writes the whole array.
	if (len + 1 < count && r.Below(3) == 0)
		p[len + 1 + r.Below(count - len - 1)] = (char)(1 + r.Below(255));
}

// Fills one struct field by field; a quarter of the fields stay zero, which
// stock's ShouldSaveField omits.
void RandomStruct(CRandom& r, const Map_t& map, unsigned char* p, float base)
{
	memset(p, 0, (size_t)map.structBytes);
	for (const typedescription_t& f : map.fields)
	{
		unsigned char* pField = p + f.fieldOffset[TD_OFFSET_NORMAL];
		if (r.Below(4) == 0 || f.fieldType == FIELD_EDICT)
			continue;

		switch (f.fieldType)
		{
			case FIELD_INTEGER:
			{
				int v = r.Below(2) ? (int)r.Next() : r.Below(64);
				memcpy(pField, &v, 4);
				break;
			}
			case FIELD_SHORT:
			{
				short v = (short)r.Next();
				memcpy(pField, &v, 2);
				break;
			}
			case FIELD_CHARACTER:
				RandomChars(r, (char*)pField, f.fieldSize);
				break;
			case FIELD_TIME:
			{
				float v = RandomTime(r, base);
				memcpy(pField, &v, 4);
				break;
			}
			case FIELD_VECTOR:
			case FIELD_POSITION_VECTOR:
			{
				float v[3];
				RandomVector(r, v, f.fieldType == FIELD_POSITION_VECTOR);
				memcpy(pField, v, 12);
				break;
			}
			default:
				NotTheSubject("RandomStruct's field type");
		}
	}
}

//-----------------------------------------------------------------------------
// One CSaveRestoreData, the way the engine and ::SaveInit place it: the
// structure, its buffer, a 0xfff-slot token table, and the level info stamp.
//-----------------------------------------------------------------------------
struct Level_t
{
	float base;
	bool bUseLandmark;
	float landmark[3];

	std::string Columns() const
	{
		return FloatHex(base) + "\t" + (bUseLandmark ? "1" : "0") + "\t" + FloatHex(landmark[0]) + "," +
			FloatHex(landmark[1]) + "," + FloatHex(landmark[2]);
	}
};

Level_t RandomLevel(CRandom& r)
{
	Level_t l;
	l.base = r.Below(5) == 0 ? 0.0f : r.Range(0.0f, 2000.0f);
	l.bUseLandmark = r.Below(3) == 0;
	for (int i = 0; i < 3; i++)
		l.landmark[i] = r.Range(-2048.0f, 2048.0f);
	return l;
}

const int kTokens = 0xfff;
const int kBuffer = 1 << 16;

class CSaveBuffer
{
public:
	CSaveBuffer()
	{
		m_pData = MakeSaveRestoreData(m_store);
		m_pData->Init(m_buffer, kBuffer);
		memset(m_tokens, 0, sizeof(m_tokens));
		m_pData->InitSymbolTable(m_tokens, kTokens);
	}

	void SetLevel(const Level_t& l)
	{
		m_pData->levelInfo.time = l.base;
		m_pData->levelInfo.fUseLandmark = l.bUseLandmark;
		m_pData->levelInfo.vecLandmarkOffset.Init(l.landmark[0], l.landmark[1], l.landmark[2]);
	}

	CSaveRestoreData* Data() { return m_pData; }

	std::string Written() const { return Hex(m_buffer, m_pData->GetCurPos()); }

	// The table as `slot:name`, comma separated, in slot order.
	std::string Tokens() const
	{
		std::string s;
		for (int i = 0; i < kTokens; i++)
		{
			if (m_tokens[i] == NULL)
				continue;
			if (!s.empty())
				s += ",";
			s += std::to_string(i) + ":" + m_tokens[i];
		}
		return s;
	}

	// The bytes another buffer wrote, as the engine lays a .sav back down:
	// the same bytes and the same table.
	void LoadFrom(const CSaveBuffer& other)
	{
		memcpy(m_buffer, other.m_buffer, (size_t)other.m_pData->GetCurPos());
		memcpy(m_tokens, other.m_tokens, sizeof(m_tokens));
		m_pData->Init(m_buffer, other.m_pData->GetCurPos());
	}

private:
	alignas(16) char m_store[sizeof(CSaveRestoreData)];
	char m_buffer[kBuffer];
	char* m_tokens[kTokens];
	CSaveRestoreData* m_pData;
};

void WriteOne(CSaveBuffer& buf, const Map_t& map, const char* pszBlock, const unsigned char* pStruct)
{
	CSave save(buf.Data());
	save.WriteFields(pszBlock, pStruct, NULL, const_cast<typedescription_t*>(map.fields.data()),
					 (int)map.fields.size());
}

// Reads one block into `out` (prefilled by the caller); answers stock's
// return (1, or 0 for a block of another name).
int ReadOne(CSaveBuffer& buf, const Map_t& map, const char* pszBlock, unsigned char* out)
{
	CRestore restore(buf.Data());
	return restore.ReadFields(pszBlock, out, NULL, const_cast<typedescription_t*>(map.fields.data()),
							  (int)map.fields.size());
}

std::string MapLine(const Map_t& map)
{
	std::string s = "M\t" + map.name + "\t" + std::to_string(map.structBytes) + "\t";
	for (size_t i = 0; i < map.fields.size(); i++)
	{
		const typedescription_t& f = map.fields[i];
		if (i)
			s += ";";
		s += std::string(f.fieldName) + ":" + std::to_string((int)f.fieldType) + ":" +
			std::to_string((int)f.fieldSize) + ":" + std::to_string(f.fieldOffset[TD_OFFSET_NORMAL]);
	}
	return s;
}

struct Counts_t
{
	int writes = 0;
	int reads = 0;
	int omitted = 0; // fields stock did not write (zero)
	int zeroTimes = 0; // ZERO_TIME stored
	int clamped = 0; // a time stored as 0.001
	int invalidPositions = 0;
	int landmarks = 0; // reads under a used landmark
	int refused = 0; // ReadFields answered 0
	int streams = 0;
};

const int kCasesPerMap = 24;

// One write, then its read under another level, for a case of `map`.
void WriteAndRead(std::ostringstream& out, CRandom& r, const std::vector<Map_t>& maps, const Map_t& map,
				  const std::string& caseName, Counts_t& counts)
{
	const Level_t wl = RandomLevel(r);
	std::vector<unsigned char> in((size_t)map.structBytes);
	RandomStruct(r, map, in.data(), wl.base);

	CSaveBuffer wbuf;
	wbuf.SetLevel(wl);
	const char* pszBlock = BlockOf(map.name);
	WriteOne(wbuf, map, pszBlock, in.data());
	counts.writes++;

	out << "W\t" << caseName << "\t" << map.name << "\t" << pszBlock << "\t" << wl.Columns() << "\t"
		<< Hex(in.data(), map.structBytes) << "\t" << wbuf.Written() << "\t" << wbuf.Tokens() << "\n";

	// What the stimulus reached, read off the bytes stock wrote.
	int nSaved = 0;
	memcpy(&nSaved, wbuf.Data()->GetBuffer() + 4, 4);
	counts.omitted += (int)map.fields.size() - nSaved;
	for (const typedescription_t& f : map.fields)
	{
		const unsigned char* pField = in.data() + f.fieldOffset[TD_OFFSET_NORMAL];
		float v[3];
		memcpy(v, pField, f.fieldType == FIELD_TIME ? 4 : (f.fieldType == FIELD_POSITION_VECTOR ? 12 : 0));
		if (f.fieldType == FIELD_TIME && v[0] == 0.0f && Bits(v[0]) != 0)
			counts.zeroTimes++;
		if (f.fieldType == FIELD_TIME && v[0] != 0.0f && fabsf(v[0] - wl.base) < 0.001 && v[0] != FLT_MAX)
			counts.clamped++;
		if (f.fieldType == FIELD_POSITION_VECTOR && v[0] == FLT_MAX && v[1] == FLT_MAX && v[2] == FLT_MAX)
			counts.invalidPositions++;
	}

	// Read back under the real map (a variant's reader), at another level.
	const Map_t& reader = MapNamed(maps, ReaderOf(map.name));
	const Level_t rl = RandomLevel(r);
	CSaveBuffer rbuf;
	rbuf.LoadFrom(wbuf);
	rbuf.SetLevel(rl);
	std::vector<unsigned char> got((size_t)reader.structBytes, 0xcd);
	const std::string prefill = Hex(got.data(), reader.structBytes);
	const int ret = ReadOne(rbuf, reader, pszBlock, got.data());
	counts.reads++;
	if (rl.bUseLandmark)
		counts.landmarks++;

	out << "R\t" << caseName << "\t" << reader.name << "\t" << pszBlock << "\t" << rl.Columns() << "\t" << prefill
		<< "\t" << ret << "\t" << rbuf.Data()->GetCurPos() << "\t" << Hex(got.data(), reader.structBytes) << "\n";
}

// A block read under another block's name: stock refuses it, rewinds the
// header and leaves the struct as it was.
void WrongName(std::ostringstream& out, CRandom& r, const std::vector<Map_t>& maps, Counts_t& counts)
{
	const Map_t& decal = MapNamed(maps, "decallist_t");
	const Map_t& music = MapNamed(maps, "musicsave_t");
	const Level_t wl = RandomLevel(r);
	std::vector<unsigned char> in((size_t)decal.structBytes);
	RandomStruct(r, decal, in.data(), wl.base);

	CSaveBuffer wbuf;
	wbuf.SetLevel(wl);
	WriteOne(wbuf, decal, "DECALLIST", in.data());
	out << "W\twrongname\t" << decal.name << "\tDECALLIST\t" << wl.Columns() << "\t"
		<< Hex(in.data(), decal.structBytes) << "\t" << wbuf.Written() << "\t" << wbuf.Tokens() << "\n";

	CSaveBuffer rbuf;
	rbuf.LoadFrom(wbuf);
	rbuf.SetLevel(wl);
	std::vector<unsigned char> got((size_t)music.structBytes, 0xcd);
	const std::string prefill = Hex(got.data(), music.structBytes);
	const int ret = ReadOne(rbuf, music, "MUSICLIST", got.data());
	if (ret == 0)
		counts.refused++;
	out << "R\twrongname\t" << music.name << "\tMUSICLIST\t" << wl.Columns() << "\t" << prefill << "\t" << ret << "\t"
		<< rbuf.Data()->GetCurPos() << "\t" << Hex(got.data(), music.structBytes) << "\n";
}

// The engine's own sequences through one buffer and one table: the client
// state file's decals then music, and the server's header then light styles,
// read back in the same order at another level.
void Stream(std::ostringstream& out, CRandom& r, const std::vector<Map_t>& maps, const std::string& caseName,
			const std::vector<std::string>& order, Counts_t& counts)
{
	const Level_t wl = RandomLevel(r);
	const Level_t rl = RandomLevel(r);
	out << "S\t" << caseName << "\t" << wl.Columns() << "\t" << rl.Columns() << "\n";

	CSaveBuffer wbuf;
	wbuf.SetLevel(wl);
	for (const std::string& mapName : order)
	{
		const Map_t& map = MapNamed(maps, mapName);
		std::vector<unsigned char> in((size_t)map.structBytes);
		RandomStruct(r, map, in.data(), wl.base);
		WriteOne(wbuf, map, BlockOf(mapName), in.data());
		out << "SW\t" << caseName << "\t" << mapName << "\t" << BlockOf(mapName) << "\t"
			<< Hex(in.data(), map.structBytes) << "\n";
	}
	out << "SO\t" << caseName << "\t" << wbuf.Written() << "\t" << wbuf.Tokens() << "\n";

	CSaveBuffer rbuf;
	rbuf.LoadFrom(wbuf);
	rbuf.SetLevel(rl);
	for (const std::string& mapName : order)
	{
		const Map_t& map = MapNamed(maps, mapName);
		std::vector<unsigned char> got((size_t)map.structBytes, 0xcd);
		const int ret = ReadOne(rbuf, map, BlockOf(mapName), got.data());
		out << "SR\t" << caseName << "\t" << mapName << "\t" << BlockOf(mapName) << "\t" << ret << "\t"
			<< rbuf.Data()->GetCurPos() << "\t" << Hex(got.data(), map.structBytes) << "\n";
	}
	counts.streams++;
}

std::string RunScenario(Counts_t* pCounts = NULL)
{
	CRandom r(0x5A7E0C0Du);
	Counts_t counts;
	const std::vector<Map_t> maps = Maps();

	std::ostringstream out;
	out << "# savegame-stock.tsv: written by src/test/oracle/native/savegame_codec_oracle_test.cpp; "
		   "see README-savegame-stock.md\n";
	for (const Map_t& map : maps)
		out << MapLine(map) << "\n";

	for (const Map_t& map : maps)
	{
		for (int i = 0; i < kCasesPerMap; i++)
			WriteAndRead(out, r, maps, map, map.name + "#" + std::to_string(i), counts);
	}

	WrongName(out, r, maps, counts);

	// A struct with every field zero: stock writes the count record alone.
	{
		const Map_t& decal = MapNamed(maps, "decallist_t");
		std::vector<unsigned char> zero((size_t)decal.structBytes, 0);
		CSaveBuffer wbuf;
		const Level_t wl = {12.5f, false, {0, 0, 0}};
		wbuf.SetLevel(wl);
		WriteOne(wbuf, decal, "DECALLIST", zero.data());
		out << "W\tallzero\t" << decal.name << "\tDECALLIST\t" << wl.Columns() << "\t"
			<< Hex(zero.data(), decal.structBytes) << "\t" << wbuf.Written() << "\t" << wbuf.Tokens() << "\n";
		counts.writes++;
	}

	std::vector<std::string> client;
	for (int i = 0; i < 9; i++)
		client.push_back("decallist_t");
	for (int i = 0; i < 3; i++)
		client.push_back("musicsave_t");
	Stream(out, r, maps, "client", client, counts);

	std::vector<std::string> server = {"SAVE_HEADER"};
	for (int i = 0; i < 6; i++)
		server.push_back("SAVELIGHTSTYLE");
	Stream(out, r, maps, "server", server, counts);

	std::vector<std::string> game = {"GAME_HEADER", "levellist_t", "levellist_t"};
	Stream(out, r, maps, "gameheader", game, counts);

	if (pCounts)
		*pCounts = counts;
	return out.str();
}

std::string RepoPath(const char* pszRel)
{
	const char* pszRepo = getenv("SOURCESHARP_REPO");
	return std::string(pszRepo ? pszRepo : ".") + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoPath("src/test/client/Fixtures/savegame-stock.tsv");
}

std::string MarkerPath()
{
	return RepoPath("src/test/oracle/native/savegame_golden.regen");
}

} // namespace

TEST(SaveGameCodecOracle, StockProducesTheGolden)
{
	const std::string run = RunScenario();

	if (std::ifstream(MarkerPath()).good())
	{
		std::ofstream out(GoldenPath());
		out << run;
	}

	std::ifstream in(GoldenPath());
	ASSERT_TRUE(in.good()) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();
	ASSERT_GT(golden.size(), 50000u) << "the golden is empty or truncated";

	std::istringstream a(run), b(golden);
	std::string la, lb;
	int n = 0;
	while (true)
	{
		const bool ha = (bool)std::getline(a, la);
		const bool hb = (bool)std::getline(b, lb);
		++n;
		if (!ha && !hb)
			break;
		ASSERT_TRUE(ha && hb && la == lb)
			<< "line " << n << ": stock now writes\n  " << (ha ? la : "<end>") << "\nand the golden has\n  "
			<< (hb ? lb : "<end>") << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

// The stimulus reaches every arm: omitted zero fields, both time sentinels'
// neighbours, the clamp, vec3_invalid, a used landmark, a refused block, and
// the three engine sequences.
TEST(SaveGameCodecOracle, TheScenarioReachesEveryArm)
{
	Counts_t counts;
	RunScenario(&counts);
	EXPECT_GT(counts.writes, 300);
	EXPECT_GT(counts.reads, 300);
	EXPECT_GT(counts.omitted, 100);
	EXPECT_GT(counts.zeroTimes, 0);
	EXPECT_GT(counts.clamped, 0);
	EXPECT_GT(counts.invalidPositions, 0);
	EXPECT_GT(counts.landmarks, 50);
	EXPECT_EQ(counts.refused, 1);
	EXPECT_EQ(counts.streams, 3);
}

// Stock's record framing, read off one write: a four-byte count record under
// the block's token, then one record per non-empty field, each a two-byte
// width and a two-byte token -- the layout the managed codec writes.
TEST(SaveGameCodecOracle, StocksFramingIsCountThenWidthTokenRecords)
{
	const std::vector<Map_t> maps = Maps();
	const Map_t& music = MapNamed(maps, "musicsave_t");
	MusicSave_t song;
	memset(&song, 0, sizeof(song));
	Q_strncpy(song.songname, "music/hl2_song1.mp3", sizeof(song.songname));
	song.sampleposition = 0; // zero: omitted
	song.master_volume = 77;

	CSaveBuffer buf;
	const Level_t l = {3.0f, false, {0, 0, 0}};
	buf.SetLevel(l);
	WriteOne(buf, music, "MUSICLIST", (const unsigned char*)&song);

	const unsigned char* p = (const unsigned char*)buf.Data()->GetBuffer();
	short w = 0, t = 0;
	int n = 0;
	memcpy(&w, p, 2);
	memcpy(&n, p + 4, 4);
	EXPECT_EQ(4, w) << "the count record's width";
	EXPECT_EQ(2, n) << "songname and master_volume; the zero sampleposition is not written";
	memcpy(&w, p + 8, 2);
	memcpy(&t, p + 10, 2);
	EXPECT_EQ(128, w) << "a char array's record carries the whole array";
	EXPECT_STREQ("songname", buf.Data()->StringFromSymbol(t));
	EXPECT_EQ(8 + 4 + 128 + 4 + 2, buf.Data()->GetCurPos());
}
