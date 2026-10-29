//========= SourceSharp — ss_string.h against tier1's strtools.cpp ==========//
//
// The string helpers in src/shared/native/ss_string.h replace tier1 string
// routines at our call sites. This binary links the PRISTINE strtools.cpp
// (from the clone at valve-sdk.lock; see this directory's CMakeLists.txt) and
// runs each helper beside the function it replaces, over hand-picked edge
// cases and a few thousand seeded random inputs drawn from alphabets rich in
// the characters each function treats specially. Same inputs, same answers,
// same bytes written.
//
// Every fact also counts its comparisons and requires a floor, so a generator
// that produced nothing cannot pass.
//
// Where tier1 is undefined (a negative size, a zero-size format buffer,
// GetFileName of "") the helper deliberately writes nothing; those inputs are
// not generated. The case-insensitive compares are held to the SIGN of
// tier1's answer: its CRT fallback for bytes of 0x80 and above answers a
// difference rather than 1, and every caller reads only the sign.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier1/strtools.h"

#include "ss_string.h"

#include <cstdarg>
#include <cstring>
#include <random>
#include <string>

namespace
{

const int kRandomRounds = 4000;

std::mt19937& Rng()
{
	static std::mt19937 s_rng(20260929u);
	return s_rng;
}

int Pick(int lo, int hi) // inclusive
{
	return std::uniform_int_distribution<int>(lo, hi)(Rng());
}

std::string Random(const char* alphabet, size_t alphabetLen, int maxLen)
{
	std::string s;
	const int n = Pick(0, maxLen);
	for (int i = 0; i < n; ++i)
		s += alphabet[Pick(0, (int)alphabetLen - 1)];
	return s;
}

// No '\0' in these: the strings are C strings. Each is the characters its
// functions treat specially, with ordinary letters and high bytes around them.
const char kText[] = "aAbBzZ@`[{_ -.\x7f\x80\xC0\xE0\xE9\xFF";
const char kNumber[] = "0123456789aAfFgxX-+.eE' ";
const char kPath[] = "ab/\\.cC.";
const char kTokens[] = "ab,,c ";

#define RANDOM(alphabet, maxLen) Random(alphabet, sizeof(alphabet) - 1, maxLen)

int Sign(int v)
{
	return (v > 0) - (v < 0);
}

// Offset of a found pointer, or -1 for NULL, so two different buffers compare.
long Offset(const char* base, const char* p)
{
	return p ? (long)(p - base) : -1L;
}

int ValveVsn(char* dst, int size, const char* fmt, ...)
{
	va_list args;
	va_start(args, fmt);
	const int n = V_vsnprintf(dst, size, fmt, args);
	va_end(args);
	return n;
}

int OursVsn(char* dst, int size, const char* fmt, ...)
{
	va_list args;
	va_start(args, fmt);
	const int n = ss::FormatV(dst, size, fmt, args);
	va_end(args);
	return n;
}

struct Pair
{
	char valve[48];
	char ours[48];

	Pair()
	{
		std::memset(valve, '#', sizeof(valve));
		std::memset(ours, '#', sizeof(ours));
	}

	void Load(const std::string& s)
	{
		std::memcpy(valve, s.c_str(), s.size() + 1);
		std::memcpy(ours, s.c_str(), s.size() + 1);
	}

	bool Same() const { return std::memcmp(valve, ours, sizeof(valve)) == 0; }
};

std::string Show(const std::string& s)
{
	std::string out;
	for (unsigned char c : s)
	{
		if (c >= 0x20 && c < 0x7f)
			out += (char)c;
		else
		{
			char hex[8];
			snprintf(hex, sizeof(hex), "\\x%02X", c);
			out += hex;
		}
	}
	return "\"" + out + "\"";
}

} // namespace

TEST(SsStringOracle, StrCopyIsVStrncpy)
{
	int n = 0;
	const char* fixed[] = {"", "a", "abc", "abcdefghijklmnop"};
	for (int round = 0; round < kRandomRounds + 4; ++round)
	{
		const std::string src = round < 4 ? fixed[round] : RANDOM(kText, 30);
		for (int size = 0; size <= 32; size += (round < 4 ? 1 : Pick(1, 9)))
		{
			Pair p;
			V_strncpy(p.valve, src.c_str(), size);
			ss::StrCopy(p.ours, src.c_str(), size);
			ASSERT_TRUE(p.Same()) << Show(src) << " size " << size;
			++n;
		}
	}
	EXPECT_GT(n, kRandomRounds * 3);
}

TEST(SsStringOracle, StrAppendIsVStrncat)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string dst = RANDOM(kText, 14);
		const std::string src = RANDOM(kText, 14);
		const size_t size = (size_t)Pick(0, 32);
		const int maxChars = round % 3 == 0 ? COPY_ALL_CHARACTERS : Pick(-4, 16);
		Pair p;
		p.Load(dst);
		char* rv = V_strncat(p.valve, src.c_str(), size, maxChars);
		char* ro = ss::StrAppend(p.ours, src.c_str(), size, maxChars);
		ASSERT_TRUE(p.Same()) << Show(dst) << " + " << Show(src) << " size " << size << " max " << maxChars;
		ASSERT_EQ(rv - p.valve, ro - p.ours);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, FormatIsVSnprintf)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string arg = RANDOM(kText, 20);
		const int num = Pick(-100000, 100000);
		const int size = Pick(1, 32);
		Pair p;
		const int rv = V_snprintf(p.valve, size, "%s:%d", arg.c_str(), num);
		const int ro = ss::Format(p.ours, size, "%s:%d", arg.c_str(), num);
		ASSERT_EQ(rv, ro) << Show(arg) << " " << num << " size " << size;
		ASSERT_TRUE(p.Same()) << Show(arg) << " " << num << " size " << size;

		Pair q;
		const int vv = ValveVsn(q.valve, size, "[%s]", arg.c_str());
		const int vo = OursVsn(q.ours, size, "[%s]", arg.c_str());
		ASSERT_EQ(vv, vo) << Show(arg) << " size " << size;
		ASSERT_TRUE(q.Same()) << Show(arg) << " size " << size;
		n += 2;
	}
	EXPECT_EQ(2 * kRandomRounds, n);
}

namespace
{
// A second string near the first: equal, case-flipped, cut, extended or with
// one byte changed, so the compares see long equal runs and not only a
// difference at byte 0.
std::string Near(const std::string& a)
{
	std::string b = a;
	switch (Pick(0, 4))
	{
		case 0:
			break;
		case 1:
			for (char& c : b)
				if (Pick(0, 1) && ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')))
					c ^= 0x20;
			break;
		case 2:
			b = b.substr(0, (size_t)Pick(0, (int)b.size()));
			break;
		case 3:
			b += RANDOM(kText, 3);
			break;
		case 4:
			if (!b.empty())
				b[(size_t)Pick(0, (int)b.size() - 1)] = kText[Pick(0, (int)sizeof(kText) - 2)];
			break;
	}
	return b;
}
} // namespace

TEST(SsStringOracle, StrICmpHasTheSignOfVStricmp)
{
	int n = 0, equal = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string a = RANDOM(kText, 12);
		const std::string b = round % 4 == 0 ? RANDOM(kText, 12) : Near(a);
		const int rv = V_stricmp(a.c_str(), b.c_str());
		const int ro = ss::StrICmp(a.c_str(), b.c_str());
		ASSERT_EQ(Sign(rv), ro) << Show(a) << " vs " << Show(b);
		equal += ro == 0;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
	EXPECT_GT(equal, kRandomRounds / 10) << "the equal arm was barely exercised";
}

TEST(SsStringOracle, StrNICmpHasTheSignOfVStrnicmp)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string a = RANDOM(kText, 12);
		const std::string b = Near(a);
		const int count = Pick(-2, 14);
		const int rv = V_strnicmp(a.c_str(), b.c_str(), count);
		const int ro = ss::StrNICmp(a.c_str(), b.c_str(), count);
		ASSERT_EQ(Sign(rv), ro) << Show(a) << " vs " << Show(b) << " n " << count;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StrIStrIsVStristr)
{
	int n = 0, found = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string h = RANDOM(kText, 16);
		std::string needle;
		if (round % 3 == 0 || h.empty())
			needle = RANDOM(kText, 3);
		else
		{
			const int at = Pick(0, (int)h.size() - 1);
			needle = Near(h.substr((size_t)at, (size_t)Pick(0, 4)));
		}
		const char* rv = V_stristr(h.c_str(), needle.c_str());
		const char* ro = ss::StrIStr(h.c_str(), needle.c_str());
		ASSERT_EQ(rv, ro) << Show(h) << " / " << Show(needle);
		found += ro != nullptr;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
	EXPECT_GT(found, kRandomRounds / 4) << "the found arm was barely exercised";
}

TEST(SsStringOracle, AtoIIsVAtoi)
{
	const char* fixed[] = {"",
						   "0",
						   "-0",
						   "+7",
						   "--7",
						   "0x",
						   "0X1f",
						   "-0xFF",
						   "'a",
						   "-'a",
						   "'",
						   "12ab",
						   "0xFFFFFFFF",
						   "4294967297",
						   "-9000000000000000000",
						   "9223372036854775807"};
	int n = 0;
	for (const char* s : fixed)
	{
		ASSERT_EQ(V_atoi(s), ss::AtoI(s)) << s;
		ASSERT_EQ(V_atoi64(s), ss::AtoI64(s)) << s;
		++n;
	}
	for (int round = 0; round < kRandomRounds; ++round)
	{
		// At most 18 characters: tier1 accumulates in a signed 64-bit value,
		// and more decimal digits than that overflow it (undefined).
		std::string s = RANDOM(kNumber, 18);
		if (round % 4 == 0)
			s = std::string(Pick(0, 1) ? "-" : "") + "0x" + s;
		ASSERT_EQ(V_atoi(s.c_str()), ss::AtoI(s.c_str())) << Show(s);
		ASSERT_EQ(V_atoi64(s.c_str()), ss::AtoI64(s.c_str())) << Show(s);
		++n;
	}
	EXPECT_GT(n, kRandomRounds);
}

TEST(SsStringOracle, AtoFIsVAtofBitForBit)
{
	const char* fixed[] = {"",       ".",    "-.",   "1.5",    ".25",   "3.", "1.5.7", "1.5e2",
						   "1.5E-2", "3e+2", "7e",   "0x10.8", "-0x1F", "'A", "1e",    "123456789.987654321",
						   "0.1",    "1e30", "1e-30"};
	int n = 0;
	auto check = [&](const std::string& s)
	{
		const float fv = V_atof(s.c_str());
		const float fo = ss::AtoF(s.c_str());
		if (fv != fv && fo != fo)
			return true; // both NaN (an infinite exponent times zero)
		return std::memcmp(&fv, &fo, sizeof(float)) == 0;
	};
	for (const char* s : fixed)
	{
		ASSERT_TRUE(check(s)) << s;
		++n;
	}
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kNumber, 18);
		ASSERT_TRUE(check(s)) << Show(s);
		++n;
	}
	EXPECT_GT(n, kRandomRounds);
}

TEST(SsStringOracle, StrLowerIsVStrlower)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kText, 30);
		Pair p;
		p.Load(s);
		char* rv = V_strlower(p.valve);
		char* ro = ss::StrLower(p.ours);
		ASSERT_TRUE(p.Same()) << Show(s);
		ASSERT_EQ(rv - p.valve, ro - p.ours);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, NextTokenIsNexttoken)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kTokens, 14);
		const size_t size = (size_t)Pick(0, 8);
		Pair p;
		const char* rv = nexttoken(p.valve, size, s.c_str(), ',');
		const char* ro = ss::NextToken(p.ours, size, s.c_str(), ',');
		ASSERT_TRUE(p.Same()) << Show(s) << " size " << size;
		ASSERT_EQ(Offset(s.c_str(), rv), Offset(s.c_str(), ro)) << Show(s) << " size " << size;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, FixSlashesIsVFixSlashes)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kPath, 20);
		Pair p;
		p.Load(s);
		if (round % 2)
		{
			V_FixSlashes(p.valve);
			ss::FixSlashes(p.ours);
		}
		else
		{
			V_FixSlashes(p.valve, '\\');
			ss::FixSlashes(p.ours, '\\');
		}
		ASSERT_TRUE(p.Same()) << Show(s);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StripExtensionIsVStripExtension)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kPath, 20);
		const int size = Pick(0, 24);
		Pair p;
		V_StripExtension(s.c_str(), p.valve, size);
		ss::StripExtension(s.c_str(), p.ours, size);
		ASSERT_TRUE(p.Same()) << Show(s) << " size " << size;

		// In place, with a buffer that holds the string.
		Pair q;
		q.Load(s);
		const int inPlace = (int)s.size() + 1 + Pick(0, 3);
		V_StripExtension(q.valve, q.valve, inPlace);
		ss::StripExtension(q.ours, q.ours, inPlace);
		ASSERT_TRUE(q.Same()) << Show(s) << " in place";
		n += 2;
	}
	EXPECT_EQ(2 * kRandomRounds, n);
}

TEST(SsStringOracle, FileBaseIsVFileBase)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kPath, 20);
		const int size = Pick(1, 24);
		Pair p;
		V_FileBase(s.c_str(), p.valve, size);
		ss::FileBase(s.c_str(), p.ours, size);
		ASSERT_TRUE(p.Same()) << Show(s) << " size " << size;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StripFilenameIsVStripFilename)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kPath, 20);
		Pair p;
		p.Load(s);
		V_StripFilename(p.valve);
		ss::StripFilename(p.ours);
		ASSERT_TRUE(p.Same()) << Show(s);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StripLastDirIsVStripLastDir)
{
	const char* fixed[] = {"", "./", ".\\", "./x", "tf2", "tf2/", "/", "a/b", "a\\b\\", "A/./"};
	int n = 0;
	auto one = [&](const std::string& s, int size)
	{
		Pair p;
		p.Load(s);
		const bool rv = V_StripLastDir(p.valve, size);
		const bool ro = ss::StripLastDir(p.ours, size);
		EXPECT_EQ(rv, ro) << Show(s) << " size " << size;
		EXPECT_TRUE(p.Same()) << Show(s) << " size " << size;
		++n;
	};
	for (const char* s : fixed)
		one(s, 32);
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kPath, 20);
		one(s, (int)s.size() + Pick(1, 4));
	}
	EXPECT_GT(n, kRandomRounds);
}

TEST(SsStringOracle, GetFileNameIsVGetFileName)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		std::string s = RANDOM(kPath, 20);
		if (s.empty())
			s = "x"; // tier1 answers path-1 for "": undefined, not generated
		ASSERT_EQ(V_GetFileName(s.c_str()), ss::GetFileName(s.c_str())) << Show(s);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

//=============================================================================
// THE CLIENT'S SET (plan_client_novalve C9, tier1).
//=============================================================================
namespace
{
const char kDots[] = "ab/\\.";
const char kSpace[] = " \t\n\v\f\rab\x80";
}

TEST(SsStringOracle, StrNCmpIsVStrncmp)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string a = RANDOM(kText, 8),
						  b = Pick(0, 3) ? a.substr(0, Pick(0, (int)a.size())) + RANDOM(kText, 4) : RANDOM(kText, 8);
		const int count = Pick(0, 12);
		ASSERT_EQ(V_strncmp(a.c_str(), b.c_str(), count), ss::StrNCmp(a.c_str(), b.c_str(), count))
			<< Show(a) << " vs " << Show(b) << " n " << count;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StrNIStrIsVStrnistr)
{
	int n = 0, found = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string h = RANDOM("aAbB\x80"
									 "c",
									 14),
						  needle = RANDOM("aAbB", 3);
		const int count = Pick(-1, 16);
		const char* rv = V_strnistr(h.c_str(), needle.c_str(), count);
		const char* ro = ss::StrNIStr(h.c_str(), needle.c_str(), count);
		ASSERT_EQ(rv, ro) << Show(h) << " / " << Show(needle) << " n " << count;
		found += rv != nullptr;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
	EXPECT_GT(found, kRandomRounds / 10);
}

TEST(SsStringOracle, StrNChrIsVStrnchr)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM("abc", 10);
		const int count = Pick(0, 12);
		const char c = "abcd"[Pick(0, 3)];
		ASSERT_EQ(V_strnchr(s.c_str(), c, count), ss::StrNChr(s.c_str(), c, count)) << Show(s) << " n " << count;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StrUpperIsVStrupr)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kText, 20);
		Pair p;
		p.Load(s);
		char* rv = V_strupr(p.valve);
		char* ro = ss::StrUpper(p.ours);
		ASSERT_EQ(rv - p.valve, ro - p.ours);
		ASSERT_TRUE(p.Same()) << Show(s);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, StrTrimIsVStrTrim)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kSpace, 20);
		Pair p;
		p.Load(s);
		ASSERT_EQ(V_StrTrim(p.valve), ss::StrTrim(p.ours)) << Show(s);
		ASSERT_TRUE(p.Same()) << Show(s);
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, BinaryToHexIsVBinarytohex)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		unsigned char in[16];
		const int bytes = Pick(0, 16);
		for (int i = 0; i < bytes; ++i)
			in[i] = (unsigned char)Pick(0, 255);
		const int size = Pick(1, 40);
		Pair p;
		V_binarytohex(in, bytes, p.valve, size);
		ss::BinaryToHex(in, bytes, p.ours, size);
		ASSERT_TRUE(p.Same()) << bytes << " bytes, size " << size;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, FileExtensionsAreTheVFunctions)
{
	int n = 0, some = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM(kPath, 16);
		const char* rv = V_GetFileExtension(s.c_str());
		const char* ro = ss::GetFileExtension(s.c_str());
		ASSERT_EQ(rv, ro) << Show(s);
		some += rv != nullptr;

		const int size = Pick(1, 12);
		Pair p;
		V_ExtractFileExtension(s.c_str(), p.valve, size);
		ss::ExtractFileExtension(s.c_str(), p.ours, size);
		ASSERT_TRUE(p.Same()) << Show(s) << " size " << size;

		Pair q;
		q.Load(s);
		const std::string ext = RANDOM("x.y", 4);
		const int pathSize = (int)s.size() + 1 + Pick(0, 8);
		V_SetExtension(q.valve, ext.c_str(), pathSize);
		ss::SetExtension(q.ours, ext.c_str(), pathSize);
		ASSERT_TRUE(q.Same()) << Show(s) << " ext " << Show(ext) << " size " << pathSize;
		n += 3;
	}
	EXPECT_EQ(3 * kRandomRounds, n);
	EXPECT_GT(some, kRandomRounds / 10);
}

TEST(SsStringOracle, PathPiecesAreTheVFunctions)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string s = RANDOM("ab/\\:", 12);
		ASSERT_EQ(V_IsAbsolutePath(s.c_str()), ss::IsAbsolutePath(s.c_str())) << Show(s);
		if (!s.empty())
			ASSERT_EQ(V_UnqualifiedFileName(s.c_str()), ss::GetFileName(s.c_str())) << Show(s);

		Pair p;
		p.Load(s);
		V_StripTrailingSlash(p.valve);
		ss::StripTrailingSlash(p.ours);
		ASSERT_TRUE(p.Same()) << Show(s);

		const int size = Pick(0, 14);
		Pair q;
		const bool rv = V_ExtractFilePath(s.c_str(), q.valve, size);
		const bool ro = ss::ExtractFilePath(s.c_str(), q.ours, size);
		ASSERT_EQ(rv, ro) << Show(s) << " size " << size;
		ASSERT_TRUE(q.Same()) << Show(s) << " size " << size;
		n += 4;
	}
	EXPECT_EQ(4 * kRandomRounds, n);
}

TEST(SsStringOracle, ComposeFileNameIsVComposeFileName)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		const std::string path = RANDOM(kPath, 10), file = RANDOM(kPath, 8);
		// Room for the separator: tier1 calls Error where it would not fit.
		const int size = (int)path.size() + 2 + Pick(0, 12);
		Pair p;
		V_ComposeFileName(path.c_str(), file.c_str(), p.valve, size);
		ASSERT_TRUE(ss::ComposeFileName(path.c_str(), file.c_str(), p.ours, size));
		ASSERT_TRUE(p.Same()) << Show(path) << " + " << Show(file) << " size " << size;
		++n;
	}
	EXPECT_EQ(kRandomRounds, n);
}

TEST(SsStringOracle, RemoveDotSlashesIsVRemoveDotSlashes)
{
	const char* fixed[] = {"./foo", "k./foo", "foo./", "../foo", "a/../b",   "a/b/..",       "a//b",
						   "/./a",  "a/.",    "..",    "a/..",   "/a/../..", "x/./y/../z/.", ".\\a\\..\\b"};
	int n = 0, refused = 0;
	auto one = [&](const std::string& s, char sep)
	{
		Pair p;
		p.Load(s);
		const bool rv = V_RemoveDotSlashes(p.valve, sep);
		const bool ro = ss::RemoveDotSlashes(p.ours, sep);
		ASSERT_EQ(rv, ro) << Show(s) << " sep " << sep;
		ASSERT_TRUE(p.Same()) << Show(s) << " sep " << sep;
		refused += !rv;
		++n;
	};
	for (const char* s : fixed)
	{
		one(s, '/');
		one(s, '\\');
	}
	for (int round = 0; round < kRandomRounds; ++round)
		one(RANDOM(kDots, 16), Pick(0, 1) ? '/' : '\\');
	EXPECT_GT(n, kRandomRounds);
	EXPECT_GT(refused, 10);
}

TEST(SsStringOracle, MakeAbsolutePathIsVMakeAbsolutePath)
{
	int n = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		// No "..": tier1 calls Error for one past the root, which this cannot catch.
		const std::string path = RANDOM("ab/.", 10), start = RANDOM("ab/.", 8);
		const char* pStart = Pick(0, 3) == 0 ? nullptr : start.c_str();
		if (path.find("..") != std::string::npos || start.find("..") != std::string::npos)
			continue;
		char valve[512], ours[512];
		std::memset(valve, '#', sizeof(valve));
		std::memset(ours, '#', sizeof(ours));
		V_MakeAbsolutePath(valve, sizeof(valve), path.c_str(), pStart);
		ASSERT_TRUE(ss::MakeAbsolutePath(ours, sizeof(ours), path.c_str(), pStart));
		ASSERT_EQ(0, std::memcmp(valve, ours, sizeof(valve)))
			<< Show(path) << " from " << (pStart ? Show(start) : "(null)");
		++n;
	}
	EXPECT_GT(n, kRandomRounds / 3);
}

// ss::UTF32ToUTF8 against pristine strtools_unicode.cpp's Q_UTF32ToUTF8
// under its default policy (what ILocalize::ConvertUnicodeToANSI calls on
// POSIX): every boundary code point and the invalid ones, into buffers too
// small for the whole string, and the NULL-destination length form.
TEST(SsStringOracle, UTF32ToUTF8IsQUTF32ToUTF8)
{
	const uint32_t kEdges[] = {1,        0x41,       0x7F,        0x80,       0x7FF,   0x800,    0xD7FF,
							   0xD800,   0xDFFF,     0xE000,      0xFDCF,     0xFDD0,  0xFDEF,   0xFDF0,
							   0xFFFD,   0xFFFE,     0xFFFF,      0x10000,    0x1FFFE, 0x10FFFD, 0x10FFFF,
							   0x110000, 0x7FFFFFFF, 0x80000000u, 0xFFFFFFFFu};
	const int nEdges = (int)(sizeof(kEdges) / sizeof(kEdges[0]));
	int n = 0, invalid = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		uint32_t src[24];
		const int len = Pick(0, 20);
		for (int i = 0; i < len; ++i)
			src[i] = Pick(0, 3) ? kEdges[Pick(0, nEdges - 1)] : (uint32_t)Pick(1, 0x10FFFF);
		src[len] = 0;
		for (int i = 0; i < len; ++i)
			invalid += !(src[i] < 0x110000u && (src[i] - 0xD800u) > 0x7FFu && (src[i] & 0xFFFFu) < 0xFFFEu &&
						 (src[i] - 0xFDD0u) > 0x1Fu);

		const int size = Pick(0, 3) ? Pick(0, 100) : Pick(0, 8);
		char valve[128], ours[128];
		std::memset(valve, '#', sizeof(valve));
		std::memset(ours, '#', sizeof(ours));
		const int rv = Q_UTF32ToUTF8((const uchar32*)src, valve, size);
		const int ro = ss::UTF32ToUTF8(src, ours, size);
		ASSERT_EQ(rv, ro) << "round " << round << " size " << size;
		ASSERT_EQ(0, std::memcmp(valve, ours, sizeof(valve))) << "round " << round << " size " << size;
		ASSERT_EQ(Q_UTF32ToUTF8((const uchar32*)src, NULL, 0), ss::UTF32ToUTF8(src, NULL, 0)) << "round " << round;
		++n;
	}
	EXPECT_EQ(n, kRandomRounds);
	EXPECT_GT(invalid, 100);
	EXPECT_EQ(0, ss::UTF32ToUTF8(NULL, NULL, 0));
}

// ss::UTF16ToUTF8 against pristine Q_UTF16ToUTF8 under its default policy
// (what ILocalize::ConvertUnicodeToANSI calls on Windows): valid units, every
// surrogate boundary, pairs whose code point is valid and pairs whose is not
// (U+1FFFE), halves unpaired at either end and in either order, into buffers
// too small for the whole string, and the NULL-destination length form.
TEST(SsStringOracle, UTF16ToUTF8IsQUTF16ToUTF8)
{
	const uint16_t kEdges[] = {1,      0x41,   0x7F,   0x80,   0x7FF,  0x800,  0xD7FF, 0xD800, 0xDBFF, 0xDC00,
							   0xDFFF, 0xE000, 0xFDCF, 0xFDD0, 0xFDEF, 0xFDF0, 0xFFFD, 0xFFFE, 0xFFFF};
	const int nEdges = (int)(sizeof(kEdges) / sizeof(kEdges[0]));
	int n = 0, pairs = 0, unpaired = 0;
	for (int round = 0; round < kRandomRounds; ++round)
	{
		uint16_t src[48];
		int len = 0;
		const int nChars = Pick(0, 20);
		for (int i = 0; i < nChars; ++i)
		{
			const int kind = Pick(0, 4);
			if (kind == 0)
			{
				// A pair: any code point past the BMP, U+1FFFE among them.
				const uint32_t v = Pick(0, 4) ? (uint32_t)Pick(0x10000, 0x10FFFF) : 0x1FFFEu;
				src[len++] = (uint16_t)(0xD800u + ((v - 0x10000u) >> 10));
				src[len++] = (uint16_t)(0xDC00u + ((v - 0x10000u) & 0x3FFu));
				++pairs;
			}
			else
			{
				src[len++] = Pick(0, 1) ? kEdges[Pick(0, nEdges - 1)] : (uint16_t)Pick(1, 0xFFFF);
			}
		}
		src[len] = 0;
		for (int i = 0; i < len; ++i)
			unpaired += (src[i] - 0xD800u) < 0x800u;

		const int size = Pick(0, 3) ? Pick(0, 100) : Pick(0, 8);
		char valve[128], ours[128];
		std::memset(valve, '#', sizeof(valve));
		std::memset(ours, '#', sizeof(ours));
		const int rv = Q_UTF16ToUTF8((const uchar16*)src, valve, size);
		const int ro = ss::UTF16ToUTF8(src, ours, size);
		ASSERT_EQ(rv, ro) << "round " << round << " size " << size;
		ASSERT_EQ(0, std::memcmp(valve, ours, sizeof(valve))) << "round " << round << " size " << size;
		ASSERT_EQ(Q_UTF16ToUTF8((const uchar16*)src, NULL, 0), ss::UTF16ToUTF8(src, NULL, 0)) << "round " << round;
		++n;
	}
	EXPECT_EQ(n, kRandomRounds);
	EXPECT_GT(pairs, 100);
	EXPECT_GT(unpaired, 100);
	EXPECT_EQ(0, ss::UTF16ToUTF8(NULL, NULL, 0));
}

// WideToUTF8 is the converter for this platform's wchar_t: here, the one its
// width names, byte for byte on a string with a character past the BMP.
TEST(SsStringOracle, WideToUTF8ConvertsThisPlatformsWchar)
{
	const wchar_t* pWide = L"A\u00e9\U0001F600";
	char wide[32], direct[32];
	std::memset(wide, '#', sizeof(wide));
	std::memset(direct, '#', sizeof(direct));
	const int rw = ss::WideToUTF8(pWide, wide, sizeof(wide));
	const int rd = sizeof(wchar_t) == 4 ? ss::UTF32ToUTF8((const uint32_t*)pWide, direct, sizeof(direct))
										: ss::UTF16ToUTF8((const uint16_t*)pWide, direct, sizeof(direct));
	EXPECT_EQ(rd, rw);
	EXPECT_EQ(0, std::memcmp(wide, direct, sizeof(wide)));
	EXPECT_STREQ("A\xc3\xa9\xf0\x9f\x98\x80", wide);
}
