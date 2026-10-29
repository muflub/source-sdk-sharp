//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// THE LIP SYNC ORACLE: stock's viseme pass, the arm of public/sentence.cpp
// client.so lost when C_BaseFlex left with the managed entities
// (docs/client-novalve-rulings.tsv: sentence.cpp is ruled `port`).
//
// What runs is stock's own code, from the PRISTINE clone at valve-sdk.lock:
//   - public/sentence.cpp, linked whole: CSentence's cached-format restore
//     (the runtime phonemes and emphasis samples the engine hands the client
//     through IVEngineClient::GetSentence) and GetIntensity, the emphasis
//     spline.
//   - game/client/c_baseflex.cpp's viseme functions, cut out of the file by
//     the CMake (lipsync_subject/c_baseflex_visemes.inl) between named
//     markers and compiled inside the stand-in C_BaseFlex below:
//     SetupMappings, ComputeBlendedSetting, AddViseme, SetupEmphasisBlend,
//     AddVisemesForSentence, ProcessVisemes, AddGlobalFlexController, and the
//     phonemedelay / phonemefilter / phonemesnap ConVars. The whole file
//     cannot compile outside the client; these functions need only what the
//     stand-in gives them.
//
// STAND-IN, NOT STOCK (each is data or plumbing, no decision of the arm):
//   - C_BaseFlex itself: the members the functions read (GetModelPtr,
//     m_iAccumulatedBoneMask, MouthInfo, g_flexweight).
//   - CStudioHdr: only flags(), the one member the pass reads.
//   - FindSceneFile: hands back this harness's .vfe blob for a class name (or
//     NULL), and maps the file's keys as stock's EnsureTranslations does
//     (c_baseflex.cpp: every key, in order, through AddGlobalFlexController)
//     the first time a file is found; FlexControllerLocalToGlobal reads that
//     map.
//   - engine: GetSentence and GetSentenceLength answer from the case.
//
// The scenario is seeded (xorshift32, seed 0x1F5A3C07): 120 entities, each
// with up to four voice sources (some ignoring phonemes, some without a
// sentence), sentences of touching and gapped phonemes whose codes are partly
// missing from the files, emphasis tracks from none to dense, the three
// phoneme class files present or missing, the model's crossfade flag, the
// accumulated LOD bone mask and the three ConVars varied, and 48 frames that
// step each voice from before its start to past its end plus two seconds.
//
// THE GOLDEN, src/test/client/Fixtures/lipsync-stock.tsv, is what this code
// wrote; LipSyncOracleTests (SourceSharp.Client.Tests) holds the managed port
// (src/client/managed/LipSync) to it bit for bit. Regenerating the golden to
// make a test green deletes the check: it changes only when the scenario
// does, and then on purpose (touch lipsync_golden.regen beside this file, run,
// delete the marker, and say why in the commit).

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"
#include "mathlib_test_env.h"

// studio.h's CStudioHdr needs a whole model; the pass reads only its flags.
#define CStudioHdr OracleStudioHdrUnused
#include "studio.h"
#undef CStudioHdr

#include "mouthinfo.h"
#include "sentence.h"
#include "icvar.h"
#include "tier1/convar.h"
#include "tier1/strtools.h"
#include "tier1/utlbuffer.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

class CStudioHdr
{
public:
	int m_nFlags = 0;

	int flags() const { return m_nFlags; }
};

// c_baseflex.h's, field for field.
enum
{
	PHONEME_CLASS_WEAK = 0,
	PHONEME_CLASS_NORMAL,
	PHONEME_CLASS_STRONG,

	NUM_PHONEME_CLASSES
};

struct Emphasized_Phoneme
{
	char classname[64];
	bool required;
	bool basechecked;
	const flexsettinghdr_t* base;
	const flexsetting_t* exp;
	bool valid;
	float amount;
};

namespace
{
struct OracleEngine
{
	std::map<CAudioSource*, CSentence*> sentences;
	std::map<CAudioSource*, float> lengths;

	CSentence* GetSentence(CAudioSource* pSource)
	{
		auto it = sentences.find(pSource);
		return it == sentences.end() ? NULL : it->second;
	}

	float GetSentenceLength(CAudioSource* pSource)
	{
		auto it = lengths.find(pSource);
		return it == lengths.end() ? 0.0f : it->second;
	}
};

OracleEngine g_Engine;
OracleEngine* engine = &g_Engine;
} // namespace

class C_BaseFlex
{
public:
	// The case's answers.
	CStudioHdr* m_pHdr = NULL;
	int m_iAccumulatedBoneMask = 0;
	std::map<std::string, const flexsettinghdr_t*> m_Files;
	std::map<const flexsettinghdr_t*, std::vector<int>> m_Translations;
	CMouthInfo m_Mouth;
	Emphasized_Phoneme m_PhonemeClasses[NUM_PHONEME_CLASSES];

	CStudioHdr* GetModelPtr() { return m_pHdr; }

	CMouthInfo& MouthInfo() { return m_Mouth; }

	void* FindSceneFile(const char* filename)
	{
		auto it = m_Files.find(filename);
		if (it == m_Files.end())
			return NULL;
		EnsureTranslations(it->second);
		return (void*)it->second;
	}

	void EnsureTranslations(const flexsettinghdr_t* pSettinghdr)
	{
		if (m_Translations.count(pSettinghdr))
			return;
		std::vector<int>& map = m_Translations[pSettinghdr];
		for (int i = 0; i < pSettinghdr->numkeys; ++i)
			map.push_back(AddGlobalFlexController(pSettinghdr->pLocalName(i)));
	}

	int FlexControllerLocalToGlobal(const flexsettinghdr_t* pSettinghdr, int key)
	{
		return m_Translations[pSettinghdr][key];
	}

	// The stock members, below from c_baseflex.cpp.
	void SetupMappings(char const* pchFileRoot);
	void ComputeBlendedSetting(Emphasized_Phoneme* classes, float emphasis_intensity);
	void AddViseme(Emphasized_Phoneme* classes, float emphasis_intensity, int phoneme, float scale, bool newexpression);
	bool SetupEmphasisBlend(Emphasized_Phoneme* classes, int phoneme);
	void AddVisemesForSentence(Emphasized_Phoneme* classes, float emphasis_intensity, CSentence* sentence, float t,
							   float dt, bool juststarted);
	void ProcessVisemes(Emphasized_Phoneme* classes);
	static int AddGlobalFlexController(const char* szName);

	static int g_numflexcontrollers;
	static char* g_flexcontroller[MAXSTUDIOFLEXCTRL * 4];
	static float g_flexweight[MAXSTUDIOFLEXDESC];
};

// The pristine functions (see the CMake for the markers).
#include "c_baseflex_visemes.inl"

// The console: ConVar::SetValue tells g_pCVar's global change callbacks, so
// the scenario's ConVar settings need one there. Nothing else is asked of it.
class CLipSyncCvar final : public ICvar
{
public:
	virtual bool Connect(CreateInterfaceFn) { return true; }

	virtual void Disconnect() {}

	virtual void* QueryInterface(const char*) { return NULL; }

	virtual InitReturnVal_t Init() { return INIT_OK; }

	virtual void Shutdown() {}

	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 1; }

	virtual void RegisterConCommand(ConCommandBase*) {}

	virtual void UnregisterConCommand(ConCommandBase*) {}

	virtual void UnregisterConCommands(CVarDLLIdentifier_t) {}

	virtual const char* GetCommandLineValue(const char*) { return NULL; }

	virtual ConCommandBase* FindCommandBase(const char*) { return NULL; }

	virtual const ConCommandBase* FindCommandBase(const char*) const { return NULL; }

	virtual ConVar* FindVar(const char*) { return NULL; }

	virtual const ConVar* FindVar(const char*) const { return NULL; }

	virtual ConCommand* FindCommand(const char*) { return NULL; }

	virtual const ConCommand* FindCommand(const char*) const { return NULL; }

	virtual ConCommandBase* GetCommands(void) { return NULL; }

	virtual const ConCommandBase* GetCommands(void) const { return NULL; }

	virtual void InstallGlobalChangeCallback(FnChangeCallback_t) {}

	virtual void RemoveGlobalChangeCallback(FnChangeCallback_t) {}

	virtual void CallGlobalChangeCallbacks(ConVar*, const char*, float) {}

	virtual void InstallConsoleDisplayFunc(IConsoleDisplayFunc*) {}

	virtual void RemoveConsoleDisplayFunc(IConsoleDisplayFunc*) {}

	virtual void ConsoleColorPrintf(const Color&, const char*, ...) const {}

	virtual void ConsolePrintf(const char*, ...) const {}

	virtual void ConsoleDPrintf(const char*, ...) const {}

	virtual void RevertFlaggedConVars(int) {}

	virtual void InstallCVarQuery(ICvarQuery*) {}

	virtual void PublishToVXConsole() {}

	virtual bool IsMaterialThreadSetAllowed() const { return true; }

	virtual void QueueMaterialThreadSetValue(ConVar*, const char*) {}

	virtual void QueueMaterialThreadSetValue(ConVar*, int) {}

	virtual void QueueMaterialThreadSetValue(ConVar*, float) {}

	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }

	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }

	virtual ICVarIteratorInternal* FactoryInternalIterator(void) { return NULL; }
};

CLipSyncCvar g_LipSyncCvar;

namespace
{
const int kCases = 120;
const int kFrames = 48;
const int kIndexes = 40; // phoneme codes 0..39 index the files; 40..43 do not
const int kKeysMax = 6;
const int kGlobals = 12; // the registry span recorded each frame: every name kKeyNames can register

const char* const kKeyNames[] = {
	"jaw_drop",           "right_lip_raiser", "left_lip_raiser", "right_corner_puller",
	"left_corner_puller", "right_puckerer",   "left_puckerer",   "Jaw_Clencher",
	"lower_lip",          "JAW_DROP",         "tongue_out",      "chin_raiser",
};
const int kKeyNameCount = sizeof(kKeyNames) / sizeof(kKeyNames[0]);

struct Rng
{
	uint32_t s = 0x1F5A3C07u;

	uint32_t Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Int(int lo, int hi) { return lo + (int)(Next() % (uint32_t)(hi - lo + 1)); }

	// k/1024 steps: every value is exact in a float and in the golden.
	float Unit() { return (float)(Next() % 1025u) / 1024.0f; }

	bool Chance(int pct) { return (int)(Next() % 100u) < pct; }
};

std::string Hex(float f)
{
	uint32_t u;
	memcpy(&u, &f, sizeof(u));
	char sz[16];
	snprintf(sz, sizeof(sz), "%08x", u);
	return sz;
}

std::string HexBytes(const std::vector<unsigned char>& bytes)
{
	std::string out;
	char sz[4];
	for (unsigned char b : bytes)
	{
		snprintf(sz, sizeof(sz), "%02x", b);
		out += sz;
	}
	return out;
}

// A .vfe image: header, settings, the phoneme index, each setting's weights,
// the key name offsets and the names, laid out as studio.h's accessors read.
std::vector<unsigned char> BuildFile(Rng& rng, const char* pszName)
{
	const int nKeys = rng.Int(1, kKeysMax);
	std::vector<int> keyNames;
	for (int k = 0; k < nKeys; k++)
		keyNames.push_back(rng.Int(0, kKeyNameCount - 1));

	std::vector<int> index(kIndexes, -1);
	std::vector<std::vector<std::pair<int, float>>> settings;
	for (int p = 0; p < kIndexes; p++)
	{
		if (rng.Chance(25))
			continue;
		index[p] = (int)settings.size();
		std::vector<std::pair<int, float>> weights;
		const int nWeights = rng.Int(0, 3);
		for (int w = 0; w < nWeights; w++)
			weights.push_back({rng.Int(0, nKeys - 1), rng.Unit() * 1.5f - 0.25f});
		settings.push_back(weights);
	}

	const int nHdr = (int)sizeof(flexsettinghdr_t);
	const int nSettingsAt = nHdr;
	const int nIndexAt = nSettingsAt + (int)(settings.size() * sizeof(flexsetting_t));
	int at = nIndexAt + kIndexes * (int)sizeof(int);
	std::vector<int> weightsAt;
	for (const auto& s : settings)
	{
		weightsAt.push_back(at);
		at += (int)(s.size() * sizeof(flexweight_t));
	}
	const int nKeyIndexAt = at;
	at += nKeys * (int)sizeof(int);
	std::vector<int> nameAt;
	for (int k = 0; k < nKeys; k++)
	{
		nameAt.push_back(at);
		at += (int)strlen(kKeyNames[keyNames[k]]) + 1;
	}
	std::vector<unsigned char> img(at, 0);

	flexsettinghdr_t* pHdr = (flexsettinghdr_t*)img.data();
	pHdr->id = ('V' << 16) + ('F' << 8) + ('E');
	pHdr->version = 0;
	V_strncpy(pHdr->name, pszName, sizeof(pHdr->name));
	pHdr->length = at;
	pHdr->numflexsettings = (int)settings.size();
	pHdr->flexsettingindex = nSettingsAt;
	pHdr->numindexes = kIndexes;
	pHdr->indexindex = nIndexAt;
	pHdr->numkeys = nKeys;
	pHdr->keynameindex = nKeyIndexAt;
	pHdr->keymappingindex = 0;
	memcpy(img.data() + nIndexAt, index.data(), kIndexes * sizeof(int));
	for (size_t i = 0; i < settings.size(); i++)
	{
		flexsetting_t* pSet = (flexsetting_t*)(img.data() + nSettingsAt) + i;
		const int nSetAt = nSettingsAt + (int)(i * sizeof(flexsetting_t));
		pSet->numsettings = (int)settings[i].size();
		pSet->settingindex = weightsAt[i] - nSetAt;
		flexweight_t* pW = (flexweight_t*)(img.data() + weightsAt[i]);
		for (size_t w = 0; w < settings[i].size(); w++)
		{
			pW[w].key = settings[i][w].first;
			pW[w].weight = settings[i][w].second;
			pW[w].influence = 0.0f;
		}
	}
	for (int k = 0; k < nKeys; k++)
	{
		((int*)(img.data() + nKeyIndexAt))[k] = nameAt[k];
		strcpy((char*)img.data() + nameAt[k], kKeyNames[keyNames[k]]);
	}
	return img;
}

struct Voice_t
{
	bool ignore = false;
	bool hasSentence = true;
	float length = 0.0f;
	float start = 0.0f; // elapsed time at frame 0
	std::vector<int> codes;
	std::vector<float> starts, ends;
	std::vector<float> sampleTimes, sampleValues;
	CSentence sentence;
};

// CSentence::CacheRestoreFromBuffer's aligned format: the one way stock fills
// the runtime phonemes and the samples outside the phoneme editor.
void Restore(Voice_t& v)
{
	CUtlBuffer buf;
	buf.PutChar(CACHED_SENTENCE_VERSION_ALIGNED);
	buf.PutChar(0);
	buf.PutChar(0);
	buf.PutChar(0);
	buf.PutInt((int)v.codes.size());
	for (size_t i = 0; i < v.codes.size(); i++)
	{
		buf.PutInt(v.codes[i]);
		buf.PutFloat(v.starts[i]);
		buf.PutFloat(v.ends[i]);
	}
	buf.PutInt((int)v.sampleTimes.size());
	for (size_t i = 0; i < v.sampleTimes.size(); i++)
	{
		buf.PutFloat(v.sampleTimes[i]);
		buf.PutFloat(v.sampleValues[i]);
	}
	buf.PutInt(0);
	v.sentence.CacheRestoreFromBuffer(buf);
}

void BuildVoice(Rng& rng, Voice_t& v)
{
	v.ignore = rng.Chance(10);
	v.hasSentence = !rng.Chance(8);
	float t = rng.Unit() * 0.25f;
	const int nPhonemes = rng.Int(0, 14);
	for (int i = 0; i < nPhonemes; i++)
	{
		const float dur = 0.015625f + rng.Unit() * 0.25f;
		v.codes.push_back(rng.Chance(5) ? -1 : rng.Int(0, kIndexes + 3));
		v.starts.push_back(t);
		v.ends.push_back(t + dur);
		t += dur;
		if (rng.Chance(35))
			t += rng.Unit() * 0.125f; // a gap: the next does not touch
	}
	v.length = t + rng.Unit() * 0.25f;
	const int nSamples = rng.Chance(20) ? 0 : rng.Int(1, 12);
	float st = 0.0f;
	for (int i = 0; i < nSamples; i++)
	{
		st += rng.Unit() * (v.length / (float)nSamples);
		v.sampleTimes.push_back(st);
		v.sampleValues.push_back(rng.Unit());
	}
	v.start = -0.25f - rng.Unit() * 0.25f;
	Restore(v);
}

const int kBoneMasks[] = {0,
						  BONE_USED_BY_VERTEX_LOD0,
						  BONE_USED_BY_VERTEX_LOD1,
						  BONE_USED_BY_VERTEX_LOD2,
						  BONE_USED_BY_VERTEX_LOD3,
						  BONE_USED_BY_VERTEX_LOD0 | BONE_USED_BY_VERTEX_LOD2,
						  BONE_USED_BY_ATTACHMENT};

struct Counts_t
{
	int frames = 0, nonzeroFrames = 0, skippedDone = 0, ignored = 0, noSentence = 0;
	int crossfadeForced = 0, noModel = 0;
};

std::string RunScenario(Counts_t* pCounts = NULL)
{
	g_pCVar = &g_LipSyncCvar;
	Counts_t counts;
	Rng rng;
	std::ostringstream out;
	out << "# lipsync-stock.tsv: written by src/test/oracle/native/lipsync_oracle_test.cpp; see README-lipsync-stock.md\n";

	for (int c = 0; c < kCases; c++)
	{
		// The registry is process-wide in stock; each case starts it empty so
		// a row reads on its own.
		for (int i = 0; i < C_BaseFlex::g_numflexcontrollers; i++)
			free(C_BaseFlex::g_flexcontroller[i]);
		C_BaseFlex::g_numflexcontrollers = 0;

		C_BaseFlex* pEnt = new C_BaseFlex;
		CStudioHdr hdr;
		const bool bModel = !rng.Chance(4);
		hdr.m_nFlags = rng.Chance(25) ? STUDIOHDR_FLAGS_FORCE_PHONEME_CROSSFADE : 0;
		pEnt->m_pHdr = bModel ? &hdr : NULL;
		pEnt->m_iAccumulatedBoneMask = kBoneMasks[rng.Int(0, (int)(sizeof(kBoneMasks) / sizeof(kBoneMasks[0])) - 1)];
		pEnt->SetupMappings("phonemes");

		const int snap = rng.Int(0, 4);
		const float delay = rng.Chance(50) ? 0.0f : rng.Unit() * 0.125f;
		const float filter = rng.Chance(40) ? 0.08f : 0.0078125f + rng.Unit() * 0.25f;
		g_CV_PhonemeSnap.SetValue(snap);
		g_CV_PhonemeDelay.SetValue(delay);
		g_CV_PhonemeFilter.SetValue(filter);

		out << "C\t" << c << "\t" << (bModel ? 1 : 0) << "\t" << hdr.m_nFlags << "\t" << pEnt->m_iAccumulatedBoneMask
			<< "\t" << g_CV_PhonemeSnap.GetInt() << "\t" << Hex(g_CV_PhonemeDelay.GetFloat()) << "\t"
			<< Hex(g_CV_PhonemeFilter.GetFloat()) << "\n";

		static const char* const kClasses[] = {"phonemes_weak", "phonemes", "phonemes_strong"};
		std::vector<std::vector<unsigned char>> files(3);
		for (int k = 0; k < 3; k++)
		{
			const int pct = (k == 1) ? 92 : 60;
			if (!rng.Chance(pct))
				continue;
			files[k] = BuildFile(rng, kClasses[k]);
			pEnt->m_Files[kClasses[k]] = (const flexsettinghdr_t*)files[k].data();
			out << "F\t" << kClasses[k] << "\t" << HexBytes(files[k]) << "\n";
		}

		const int nVoices = rng.Int(0, 4);
		std::vector<Voice_t> voices(nVoices);
		g_Engine.sentences.clear();
		g_Engine.lengths.clear();
		for (int v = 0; v < nVoices; v++)
		{
			Voice_t& vo = voices[v];
			BuildVoice(rng, vo);
			CAudioSource* pSource = (CAudioSource*)(uintptr_t)(0x1000 + v * 16);
			pEnt->m_Mouth.AddSource(pSource, vo.ignore);
			if (vo.hasSentence)
				g_Engine.sentences[pSource] = &vo.sentence;
			g_Engine.lengths[pSource] = vo.length;
			out << "V\t" << v << "\t" << (vo.ignore ? 1 : 0) << "\t" << (vo.hasSentence ? 1 : 0) << "\t"
				<< Hex(vo.length) << "\t" << Hex(vo.start) << "\n";
			for (size_t i = 0; i < vo.codes.size(); i++)
				out << "P\t" << vo.codes[i] << "\t" << Hex(vo.starts[i]) << "\t" << Hex(vo.ends[i]) << "\n";
			for (size_t i = 0; i < vo.sampleTimes.size(); i++)
				out << "E\t" << Hex(vo.sampleTimes[i]) << "\t" << Hex(vo.sampleValues[i]) << "\n";
		}

		for (int f = 0; f < kFrames; f++)
		{
			// Each voice moves from before its start to past length + 2.
			float base[kGlobals];
			for (int g = 0; g < kGlobals; g++)
				base[g] = rng.Chance(70) ? 0.0f : rng.Unit();
			memset(C_BaseFlex::g_flexweight, 0, sizeof(C_BaseFlex::g_flexweight));
			memcpy(C_BaseFlex::g_flexweight, base, sizeof(base));

			out << "T\t" << f;
			for (int v = 0; v < nVoices; v++)
			{
				Voice_t& vo = voices[v];
				const float span = vo.length + 2.5f - vo.start;
				const float elapsed = vo.start + span * ((float)f / (float)(kFrames - 1));
				pEnt->m_Mouth.GetVoiceSource(v)->SetElapsedTime(elapsed);
				out << "\t" << Hex(elapsed);

				if (vo.ignore)
					counts.ignored++;
				else if (!vo.hasSentence)
					counts.noSentence++;
				else if (elapsed >= vo.length + 2.0f)
					counts.skippedDone++;
			}
			out << "\n";

			// GetIntensity at the time the pass asks it, for every voice that
			// reaches the call (a diagnostic row; the weights are the result).
			for (int v = 0; v < nVoices; v++)
			{
				Voice_t& vo = voices[v];
				const float elapsed = pEnt->m_Mouth.GetVoiceSource(v)->GetElapsedTime();
				if (vo.ignore || !vo.hasSentence || elapsed >= vo.length + 2.0f)
					continue;
				const float t = elapsed - g_CV_PhonemeDelay.GetFloat();
				out << "I\t" << v << "\t" << Hex(vo.sentence.GetIntensity(t, vo.length)) << "\n";
			}

			out << "B";
			for (int g = 0; g < kGlobals; g++)
				out << "\t" << Hex(base[g]);
			out << "\n";

			pEnt->ProcessVisemes(pEnt->m_PhonemeClasses);

			out << "W\t" << C_BaseFlex::g_numflexcontrollers;
			bool bMoved = false;
			for (int g = 0; g < kGlobals; g++)
			{
				out << "\t" << Hex(C_BaseFlex::g_flexweight[g]);
				bMoved |= C_BaseFlex::g_flexweight[g] != base[g];
			}
			out << "\n";
			counts.frames++;
			counts.nonzeroFrames += bMoved ? 1 : 0;
		}
		if (hdr.m_nFlags)
			counts.crossfadeForced++;
		if (!bModel)
			counts.noModel++;
		delete pEnt;
	}
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
	return RepoPath("src/test/client/Fixtures/lipsync-stock.tsv");
}

std::string MarkerPath()
{
	return RepoPath("src/test/oracle/native/lipsync_golden.regen");
}
} // namespace

TEST(LipSyncOracle, StockProducesTheGolden)
{
	SS_SKIP_ON_CI(
		"a golden recorded bit for bit on the owner's machine; another CPU or C library differs in the last bit");
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
	ASSERT_GT(golden.size(), 100000u) << "the golden is empty or truncated";

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

// The stimulus reaches every arm: weights moved, and voices were ignored,
// sentence-less and past their end; models without a model pointer and with
// the forced crossfade both occur.
TEST(LipSyncOracle, TheScenarioReachesEveryArm)
{
	Counts_t counts;
	RunScenario(&counts);
	EXPECT_EQ(counts.frames, kCases * kFrames);
	EXPECT_GT(counts.nonzeroFrames, 500);
	EXPECT_GT(counts.ignored, 0);
	EXPECT_GT(counts.noSentence, 0);
	EXPECT_GT(counts.skippedDone, 0);
	EXPECT_GT(counts.crossfadeForced, 0);
	EXPECT_GT(counts.noModel, 0);
}

// The stock defaults the managed ConVars must carry.
TEST(LipSyncOracle, TheConVarDefaultsAreStocks)
{
	EXPECT_STREQ(g_CV_PhonemeDelay.GetDefault(), "0");
	EXPECT_STREQ(g_CV_PhonemeFilter.GetDefault(), "0.08");
	EXPECT_STREQ(g_CV_PhonemeSnap.GetDefault(), "2");
}
