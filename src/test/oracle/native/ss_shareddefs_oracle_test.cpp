//====== SourceSharp — ORACLE: ss_shareddefs.h's constants are upstream's ======//
//
// ss_shareddefs.h (src/server/native) restates the few stock game constants
// server.so's own code uses, so that TUs built without game/ headers can name
// them. Each one is checked here against the PRISTINE upstream header it came
// from (valve_sdk.h): a #define by its replacement, an enumerator by walking its
// enum (explicit values and the implicit +1 steps alike).
//
//=============================================================================//

#include <cctype>
#include <cstdlib>
#include <string>
#include "gtest/gtest.h"
#include "valve_sdk.h"
#include "ss_shareddefs.h"

namespace
{

// Comments out, line structure kept.
std::string StripComments(const std::string& s)
{
	std::string out;
	for (size_t i = 0; i < s.size(); i++)
	{
		if (s.compare(i, 2, "//") == 0)
		{
			while (i < s.size() && s[i] != '\n')
				i++;
			if (i < s.size())
				out += '\n';
			continue;
		}
		if (s.compare(i, 2, "/*") == 0)
		{
			const size_t end = s.find("*/", i + 2);
			i = (end == std::string::npos) ? s.size() : end + 1;
			continue;
		}
		out += s[i];
	}
	return out;
}

bool IsIdent(char c)
{
	return isalnum((unsigned char)c) || c == '_';
}

// The position of NAME as a whole word, or npos.
size_t FindWord(const std::string& s, const std::string& name, size_t from = 0)
{
	for (size_t at = s.find(name, from); at != std::string::npos; at = s.find(name, at + 1))
	{
		const bool bLeft = at == 0 || !IsIdent(s[at - 1]);
		const bool bRight = at + name.size() >= s.size() || !IsIdent(s[at + name.size()]);
		if (bLeft && bRight)
			return at;
	}
	return std::string::npos;
}

// `#define NAME <integer>`: the first definition, or with bLast the last one
// (the #else arm of a chain of #if/#elif definitions). Sets *pbFound.
long MacroValue(const std::string& header, const std::string& name, bool* pbFound, bool bLast = false)
{
	*pbFound = false;
	long nFound = 0;
	const std::string code = StripComments(header);
	for (size_t at = FindWord(code, name); at != std::string::npos; at = FindWord(code, name, at + 1))
	{
		const size_t bol = code.rfind('\n', at) + 1;
		const std::string lead = code.substr(bol, at - bol);
		if (lead.find("#") == std::string::npos || lead.find("define") == std::string::npos)
			continue;
		const size_t eol = code.find('\n', at);
		const std::string rest = code.substr(at + name.size(), eol - at - name.size());
		char* end = NULL;
		long v = strtol(rest.c_str(), &end, 0);
		const size_t q = rest.find_first_not_of(" \t");
		if (q != std::string::npos && rest[q] == '\'' && q + 2 < rest.size() && rest[q + 2] == '\'')
			v = (unsigned char)rest[q + 1]; // a character literal, 'C'
		else if (end == rest.c_str())
		{
			// `(1 << N)`, spaced or not: the bit-flag form.
			std::string tight;
			for (char c : rest)
				if (c != ' ' && c != '\t')
					tight += c;
			if (tight.compare(0, 4, "(1<<") != 0)
				continue;
			v = 1L << strtol(tight.c_str() + 4, NULL, 0);
		}
		*pbFound = true;
		nFound = v;
		if (!bLast)
			return v;
	}
	return nFound;
}

// An enumerator's value: walks the enum that declares NAME from its `{`,
// counting implicit values as the compiler does. Preprocessor lines are
// skipped. Sets *pbFound.
long EnumValue(const std::string& header, const std::string& name, bool* pbFound)
{
	*pbFound = false;
	const std::string code = StripComments(header);
	for (size_t at = FindWord(code, name); at != std::string::npos; at = FindWord(code, name, at + 1))
	{
		const size_t open = code.rfind('{', at);
		const size_t close = code.rfind('}', at);
		if (open == std::string::npos || (close != std::string::npos && close > open))
			continue;
		const size_t kw = FindWord(code.substr(0, open), "enum");
		if (kw == std::string::npos || code.rfind("enum", open) == std::string::npos ||
			code.substr(code.rfind("enum", open), open - code.rfind("enum", open)).find(';') != std::string::npos)
			continue;

		long next = 0;
		size_t p = open + 1;
		while (p < code.size() && code[p] != '}')
		{
			size_t eoe = code.find_first_of(",}", p);
			if (eoe == std::string::npos)
				eoe = code.size();
			std::string entry;
			size_t line = p;
			while (line < eoe) // drop preprocessor lines inside the entry
			{
				size_t nl = code.find('\n', line);
				if (nl == std::string::npos || nl > eoe)
					nl = eoe;
				const std::string one = code.substr(line, nl - line);
				const size_t first = one.find_first_not_of(" \t\r\n");
				if (first != std::string::npos && one[first] != '#')
					entry += one + " ";
				line = nl + 1;
			}
			const size_t b = entry.find_first_not_of(" \t\r\n");
			if (b != std::string::npos)
			{
				size_t e = b;
				while (e < entry.size() && IsIdent(entry[e]))
					e++;
				const std::string ident = entry.substr(b, e - b);
				const size_t eq = entry.find('=', e);
				if (eq != std::string::npos)
					next = strtol(entry.c_str() + eq + 1, NULL, 0);
				if (ident == name)
				{
					*pbFound = true;
					return next;
				}
				next++;
			}
			p = (eoe < code.size() && code[eoe] == ',') ? eoe + 1 : eoe;
		}
	}
	return 0;
}

enum Kind_t
{
	kEnum,
	kMacro,
	kMacroLastArm
};

struct Row_t
{
	const char* pszHeader;
	const char* pszName;
	Kind_t eKind;
	long nOurs;
};

const Row_t kRows[] = {
	{"src/game/shared/shareddefs.h", "DONT_BLEED", kEnum, ss::DONT_BLEED},
	{"src/game/shared/shareddefs.h", "BLOOD_COLOR_RED", kEnum, ss::BLOOD_COLOR_RED},
	{"src/game/shared/shareddefs.h", "BLOOD_COLOR_MECH", kEnum, ss::BLOOD_COLOR_MECH},
	{"src/game/shared/shareddefs.h", "TRACER_FLAG_WHIZ", kMacro, ss::kTracerFlagWhiz},
	{"src/game/shared/shareddefs.h", "TRACER_FLAG_USEATTACHMENT", kMacro, ss::kTracerFlagUseAttachment},
	{"src/game/shared/shareddefs.h", "TRACER_DONT_USE_ATTACHMENT", kMacro, ss::kTracerDontUseAttachment},
	{"src/game/shared/shareddefs.h", "DMG_GENERIC", kMacro, ss::kDmgGeneric},
	{"src/game/shared/shareddefs.h", "DMG_CRUSH", kMacro, ss::kDmgCrush},
	{"src/game/shared/shareddefs.h", "DMG_BULLET", kMacro, ss::kDmgBullet},
	{"src/game/shared/shareddefs.h", "DMG_BURN", kMacro, ss::kDmgBurn},
	{"src/game/shared/shareddefs.h", "DMG_FALL", kMacro, ss::kDmgFall},
	{"src/game/shared/shareddefs.h", "DMG_BLAST", kMacro, ss::kDmgBlast},
	{"src/game/shared/shareddefs.h", "DMG_CLUB", kMacro, ss::kDmgClub},
	{"src/game/shared/shareddefs.h", "DMG_SHOCK", kMacro, ss::kDmgShock},
	{"src/game/shared/shareddefs.h", "DMG_SONIC", kMacro, ss::kDmgSonic},
	{"src/game/shared/shareddefs.h", "DMG_PREVENT_PHYSICS_FORCE", kMacro, ss::kDmgPreventPhysicsForce},
	{"src/game/shared/shareddefs.h", "DMG_DROWN", kMacro, ss::kDmgDrown},
	{"src/game/shared/shareddefs.h", "DMG_PARALYZE", kMacro, ss::kDmgParalyze},
	{"src/game/shared/shareddefs.h", "DMG_NERVEGAS", kMacro, ss::kDmgNervegas},
	{"src/game/shared/shareddefs.h", "DMG_POISON", kMacro, ss::kDmgPoison},
	{"src/game/shared/shareddefs.h", "DMG_RADIATION", kMacro, ss::kDmgRadiation},
	{"src/game/shared/shareddefs.h", "DMG_DROWNRECOVER", kMacro, ss::kDmgDrownrecover},
	{"src/game/shared/shareddefs.h", "DMG_ACID", kMacro, ss::kDmgAcid},
	{"src/game/shared/shareddefs.h", "DMG_SLOWBURN", kMacro, ss::kDmgSlowburn},
	{"src/game/shared/shareddefs.h", "DMG_PHYSGUN", kMacro, ss::kDmgPhysgun},
	{"src/game/shared/shareddefs.h", "DMG_PLASMA", kMacro, ss::kDmgPlasma},
	// The #else arm: this build defines none of CSTRIKE_DLL, TF_DLL, HL2MP.
	{"src/game/shared/shareddefs.h", "MAX_PLAYERS", kMacroLastArm, ss::kMaxPlayers},
	// The #else arm again: TF_DLL / TF_CLIENT_DLL send 18 HUD bits, this build 12.
	{"src/game/shared/shareddefs.h", "HIDEHUD_BITCOUNT", kMacroLastArm, ss::kHideHudBitCount},
	{"src/game/shared/util_shared.h", "LANGUAGE_ENGLISH", kMacro, ss::kLanguageEnglish},
	{"src/game/shared/util_shared.h", "LANGUAGE_GERMAN", kMacro, ss::kLanguageGerman},
	{"src/game/server/util.h", "MAX_SPHERE_QUERY", kMacro, ss::kMaxSphereQuery},
	{"src/game/shared/shareddefs.h", "DAMAGE_YES", kMacro, ss::kDamageYes},
	{"src/game/shared/gamerules.h", "GR_NOTTEAMMATE", kEnum, ss::GR_NOTTEAMMATE},
	{"src/game/shared/gamerules.h", "GR_TEAMMATE", kEnum, ss::GR_TEAMMATE},
	{"src/game/shared/rumble_shared.h", "RUMBLE_FLAGS_NONE", kMacro, ss::kRumbleFlagsNone},
	{"src/game/server/soundent.h", "SOUND_COMBAT", kEnum, ss::SOUND_COMBAT},
	{"src/game/server/soundent.h", "SOUND_CONTEXT_EXPLOSION", kEnum, ss::SOUND_CONTEXT_EXPLOSION},
	{"src/game/server/soundent.h", "SOUNDENT_CHANNEL_UNSPECIFIED", kEnum, ss::SOUNDENT_CHANNEL_UNSPECIFIED},
	{"src/game/shared/decals.h", "CHAR_TEX_CONCRETE", kMacro, ss::kCharTexConcrete},
	{"src/game/shared/decals.h", "CHAR_TEX_DIRT", kMacro, ss::kCharTexDirt},
	{"src/game/shared/decals.h", "CHAR_TEX_GRATE", kMacro, ss::kCharTexGrate},
	{"src/game/shared/decals.h", "CHAR_TEX_METAL", kMacro, ss::kCharTexMetal},
	{"src/game/shared/baseentity_shared.h", "VPHYSICS_MAX_OBJECT_LIST_COUNT", kMacro, ss::kMaxPhysicsObjectList},
	{"src/game/shared/shareddefs.h", "POSITIONWATCHER", kEnum, ss::POSITIONWATCHER},
	{"src/game/shared/shareddefs.h", "VPHYSICSWATCHER", kEnum, ss::VPHYSICSWATCHER},
	{"src/game/shared/shareddefs.h", "NUM_DATAOBJECT_TYPES", kEnum, ss::NUM_DATAOBJECT_TYPES},
};

} // namespace

// The parsers themselves, on text whose answers are known -- including an
// implicit step after an explicit value and a skipped preprocessor line.
TEST(SsSharedDefsOracle, TheParsersReadKnownText)
{
	const std::string text =
		"#define ALPHA 0x10 // sixteen\n"
		"enum\n{\n\tA_NEG = -1,\n\n\tA_ZERO = 0,\n\tA_ONE,\n#if defined( X )\n\tA_ODD,\n#endif\n\tA_TWO,\n};\n"
		"enum Named_t { B_FIVE = 5, B_SIX };\n";
	bool b = false;
	EXPECT_EQ(16, MacroValue(text, "ALPHA", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ(1L << 15, MacroValue("#define F1\t\t(1 << 15)\t// x\n", "F1", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ(1L << 23, MacroValue("#define F2 (1<<23)\n", "F2", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ('C', MacroValue("#define\tC1\t\t'C'\n", "C1", &b));
	EXPECT_TRUE(b);
	const std::string chain = "#if defined( X )\n#define BETA 65\n#else\n#define BETA 33\n#endif\n";
	EXPECT_EQ(65, MacroValue(chain, "BETA", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ(33, MacroValue(chain, "BETA", &b, true));
	EXPECT_TRUE(b);
	EXPECT_EQ(-1, EnumValue(text, "A_NEG", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ(1, EnumValue(text, "A_ONE", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ(3, EnumValue(text, "A_TWO", &b));
	EXPECT_TRUE(b);
	EXPECT_EQ(6, EnumValue(text, "B_SIX", &b));
	EXPECT_TRUE(b);
	EnumValue(text, "A_MISSING", &b);
	EXPECT_FALSE(b);
}

TEST(SsSharedDefsOracle, EveryRestatedConstantHasUpstreamsValue)
{
	int nChecked = 0;
	for (const Row_t& row : kRows)
	{
		const std::string header = ReadValve(row.pszHeader);
		ASSERT_FALSE(header.empty()) << row.pszHeader;

		bool bFound = false;
		const long nUpstream = row.eKind == kEnum
			? EnumValue(header, row.pszName, &bFound)
			: MacroValue(header, row.pszName, &bFound, row.eKind == kMacroLastArm);
		EXPECT_TRUE(bFound) << row.pszName << " is not in upstream " << row.pszHeader;
		EXPECT_EQ(nUpstream, row.nOurs) << "ss_shareddefs.h says " << row.pszName << " = " << row.nOurs << "; upstream "
										<< row.pszHeader << " says " << nUpstream;
		nChecked += bFound ? 1 : 0;
	}
	EXPECT_EQ((int)(sizeof(kRows) / sizeof(kRows[0])), nChecked);
}
