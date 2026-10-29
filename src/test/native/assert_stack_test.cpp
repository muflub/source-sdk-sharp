//========= SourceSharp — tests for the stack behind a failed assert =========//
//
// The probe this covers exists because an assert in a leaf names the leaf and
// not the bug. `imesh.h (1665) : IsFinite(x) && IsFinite(y) && IsFinite(z)` is
// CVertexBuilder::Position3f, and TWENTY-ONE translation units in this client
// compile a call to it. The report as shipped cannot tell a tracer from a
// particle from a shadow quad, and the difference is the entire question.
//
// Three pieces of arithmetic decide whether the stack that fixes that is
// printed, and each has a failure that looks exactly like success:
//
//   the key         dedup on the MESSAGE alone and the twenty-one callers
//                   collapse into one report — the very distinction being
//                   hunted. Dedup on the WHOLE stack and one caller reached
//                   through a different tail prints again every frame.
//
//   the dedup       "already seen" and "table full" are different answers and
//                   both return false. Conflating them means either a silent
//                   stop nobody notices or a line printed 3600 times.
//
//   the format      the offset is what makes the line usable after the process
//                   is gone: every module here is position independent, so an
//                   absolute address means nothing tomorrow. An address below
//                   the module base is nonsense, and formatted as an unsigned
//                   offset it WRAPS into a plausible-looking 16-digit number
//                   that addr2line will happily resolve to the wrong line.
//
// Engine-free on purpose: this is arithmetic over an int, some pointers and a
// string, and it is checked here rather than by staring at a log.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/assert_stack.h"

#include <cstring>
#include <string>

namespace
{

//-----------------------------------------------------------------------------
// A destination buffer whose SIZE the optimiser cannot work out.
//
// Two tests below format a 75-character module name into a buffer of sixteen
// or twenty-four bytes ON PURPOSE: truncating safely is the behaviour under
// test. GCC inlines FormatFrame, sees a constant array and constant strings,
// does the arithmetic and says so --
//
//   assert_stack.h:230: warning: '%s' directive output truncated writing 57
//   bytes into a region of size 10 [-Wformat-truncation=]
//
// -- and it is right about the arithmetic and wrong about it being a defect.
//
// The answer is NOT -Wno-format-truncation, in the build flags or in a pragma
// around the header: the same warning found a real bug in the emitter (the
// newline the line used to lose, see assert_stack.h), and a suppression broad
// enough to cover these two tests covers that too. Nor does a pragma in this
// file work -- the diagnostic's location is the snprintf in the header, not
// the call here, so a diagnostic region around the call has no effect on it.
//
// What the diagnostic actually needs is a destination whose size is a
// compile-time fact. So the truncation tests use one whose size is not: a
// heap block whose length has been through a volatile. Every byte passed and
// every truncation performed is identical -- it happens at run time, where the
// assertions can see it -- and the deduction simply cannot be made.
//
// The trailing sentinel byte is not padding for the warning's benefit: it is
// how these tests check that nothing was written past the size they gave.
//-----------------------------------------------------------------------------
class OpaqueBuffer
{
public:
	explicit OpaqueBuffer(size_t cch)
		: m_cch(Hide(cch))
		, m_pch(new char[Hide(cch) + 1])
	{
		memset(m_pch, 'X', m_cch + 1);
	}

	~OpaqueBuffer() { delete[] m_pch; }

	char* Data() const { return m_pch; }

	size_t Size() const { return m_cch; }

	std::string Str() const { return std::string(m_pch); }

	// Nothing was written past the length handed to the formatter.
	bool SentinelIntact() const { return m_pch[m_cch] == 'X'; }

private:
	static size_t Hide(size_t cch)
	{
		volatile size_t v = cch;
		return v;
	}

	OpaqueBuffer(const OpaqueBuffer&);
	OpaqueBuffer& operator=(const OpaqueBuffer&);

	size_t m_cch;
	char* m_pch;
};

// Stand-in return addresses. The values are never dereferenced — only their
// bit patterns take part — so any distinct constants will do.
void* Frame(unsigned long long ul)
{
	return (void*)(size_t)ul;
}

//-----------------------------------------------------------------------------
// The key: what makes two reports the same one.
//-----------------------------------------------------------------------------

TEST(AssertStackKey, SameInputsGiveSameKey)
{
	void* frames[] = {Frame(0x1000), Frame(0x2000), Frame(0x3000)};

	EXPECT_EQ(SourceSharpAssertStack_Key("imesh.h", 1665, frames, 3),
			  SourceSharpAssertStack_Key("imesh.h", 1665, frames, 3));
}

TEST(AssertStackKey, DifferentLineIsADifferentReport)
{
	void* frames[] = {Frame(0x1000), Frame(0x2000)};

	// imesh.h has TWO asserts in Position3f, one line apart: the null check on
	// 1664 and the finiteness check on 1665. They are different bugs.
	EXPECT_NE(SourceSharpAssertStack_Key("imesh.h", 1664, frames, 2),
			  SourceSharpAssertStack_Key("imesh.h", 1665, frames, 2));
}

TEST(AssertStackKey, DifferentFileIsADifferentReport)
{
	void* frames[] = {Frame(0x1000)};

	EXPECT_NE(SourceSharpAssertStack_Key("imesh.h", 100, frames, 1),
			  SourceSharpAssertStack_Key("c_tracer.cpp", 100, frames, 1));
}

// The clause the whole probe exists for. One leaf, two callers: if the key
// ignored the stack these would collapse and the report would be useless.
TEST(AssertStackKey, SameAssertFromADifferentCallerIsADifferentReport)
{
	void* fromTracer[] = {Frame(0x1000), Frame(0xAAAA)};
	void* fromParticle[] = {Frame(0x1000), Frame(0xBBBB)};

	EXPECT_NE(SourceSharpAssertStack_Key("imesh.h", 1665, fromTracer, 2),
			  SourceSharpAssertStack_Key("imesh.h", 1665, fromParticle, 2));
}

// The other half of the same trade-off: past the leading frames the stack does
// not take part, so one call site reached at a different depth is still one
// report rather than a fresh one every frame.
TEST(AssertStackKey, FramesPastTheKeyDepthDoNotTakePart)
{
	void* shortTail[kSourceSharpAssertStackKeyFrames + 1];
	void* longTail[kSourceSharpAssertStackKeyFrames + 1];

	for (int i = 0; i < kSourceSharpAssertStackKeyFrames; ++i)
	{
		shortTail[i] = Frame(0x1000 + i);
		longTail[i] = Frame(0x1000 + i);
	}

	// Identical up to the key depth, different immediately after it.
	shortTail[kSourceSharpAssertStackKeyFrames] = Frame(0xDEAD);
	longTail[kSourceSharpAssertStackKeyFrames] = Frame(0xBEEF);

	EXPECT_EQ(SourceSharpAssertStack_Key("imesh.h", 1665, shortTail, kSourceSharpAssertStackKeyFrames + 1),
			  SourceSharpAssertStack_Key("imesh.h", 1665, longTail, kSourceSharpAssertStackKeyFrames + 1));
}

// ...and the boundary itself: the LAST frame inside the depth must still count.
// Off by one here and the key silently uses five frames, not six.
TEST(AssertStackKey, TheLastFrameInsideTheKeyDepthDoesTakePart)
{
	void* a[kSourceSharpAssertStackKeyFrames];
	void* b[kSourceSharpAssertStackKeyFrames];

	for (int i = 0; i < kSourceSharpAssertStackKeyFrames; ++i)
	{
		a[i] = Frame(0x1000 + i);
		b[i] = Frame(0x1000 + i);
	}

	b[kSourceSharpAssertStackKeyFrames - 1] = Frame(0xFEED);

	EXPECT_NE(SourceSharpAssertStack_Key("imesh.h", 1665, a, kSourceSharpAssertStackKeyFrames),
			  SourceSharpAssertStack_Key("imesh.h", 1665, b, kSourceSharpAssertStackKeyFrames));
}

// A notify callback is a public entry point; a probe that crashes while
// explaining a crash is worse than no probe.
TEST(AssertStackKey, NullAndEmptyInputsAreTolerated)
{
	EXPECT_NO_FATAL_FAILURE(SourceSharpAssertStack_Key(NULL, 0, NULL, 0));
	EXPECT_EQ(SourceSharpAssertStack_Key(NULL, 7, NULL, 0), SourceSharpAssertStack_Key(NULL, 7, NULL, 0));

	// A negative count must not be read as a length.
	void* frames[] = {Frame(0x1000)};
	EXPECT_EQ(SourceSharpAssertStack_Key("x", 1, frames, -1), SourceSharpAssertStack_Key("x", 1, NULL, 0));
}

//-----------------------------------------------------------------------------
// The dedup.
//-----------------------------------------------------------------------------

TEST(AssertStackDedup, FirstTimeReportsAndTheRestDoNot)
{
	SourceSharpAssertStackSeen_t seen;
	SourceSharpAssertStack_Reset(&seen);

	bool bOverflow = true; // must be cleared by the call
	EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, 42, &bOverflow));
	EXPECT_FALSE(bOverflow);

	for (int i = 0; i < 100; ++i)
		EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 42, &bOverflow));
}

TEST(AssertStackDedup, DistinctKeysEachReportOnce)
{
	SourceSharpAssertStackSeen_t seen;
	SourceSharpAssertStack_Reset(&seen);

	EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, 1, NULL));
	EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, 2, NULL));
	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 1, NULL));
	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 2, NULL));
	EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, 3, NULL));
}

TEST(AssertStackDedup, ZeroInitialisedIsAValidEmptySet)
{
	// Asserts fire during module load, before any initialiser this file could
	// write has run. A file-scope instance is zeroed by the loader and must
	// already work.
	SourceSharpAssertStackSeen_t seen = {};

	EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, 99, NULL));
	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 99, NULL));
}

TEST(AssertStackDedup, TheTableFillsAndSaysSoExactlyOnce)
{
	SourceSharpAssertStackSeen_t seen;
	SourceSharpAssertStack_Reset(&seen);

	for (int i = 0; i < kSourceSharpAssertStackMaxSeen; ++i)
		EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, (SourceSharpAssertStackKey_t)(i + 1), NULL));

	// The 129th distinct key is refused, and that refusal is announced once.
	bool bOverflow = false;
	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 100000, &bOverflow));
	EXPECT_TRUE(bOverflow);

	// Announced ONCE. "Full" repeated per assert is the flood the dedup exists
	// to prevent, wearing a different hat.
	bOverflow = false;
	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 100001, &bOverflow));
	EXPECT_FALSE(bOverflow);
}

// A full table must not start forgetting: a key already recorded still answers
// "seen", rather than being evicted and printing again forever.
TEST(AssertStackDedup, AFullTableStillRecognisesWhatItHolds)
{
	SourceSharpAssertStackSeen_t seen;
	SourceSharpAssertStack_Reset(&seen);

	for (int i = 0; i < kSourceSharpAssertStackMaxSeen; ++i)
		SourceSharpAssertStack_ShouldReport(&seen, (SourceSharpAssertStackKey_t)(i + 1), NULL);

	SourceSharpAssertStack_ShouldReport(&seen, 100000, NULL); // refused, table full

	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 1, NULL));
	EXPECT_FALSE(
		SourceSharpAssertStack_ShouldReport(&seen, (SourceSharpAssertStackKey_t)kSourceSharpAssertStackMaxSeen, NULL));
}

TEST(AssertStackDedup, ResetEmptiesTheSetAndClearsTheFullNotice)
{
	SourceSharpAssertStackSeen_t seen;
	SourceSharpAssertStack_Reset(&seen);

	SourceSharpAssertStack_ShouldReport(&seen, 5, NULL);
	EXPECT_FALSE(SourceSharpAssertStack_ShouldReport(&seen, 5, NULL));

	SourceSharpAssertStack_Reset(&seen);
	EXPECT_TRUE(SourceSharpAssertStack_ShouldReport(&seen, 5, NULL));
}

//-----------------------------------------------------------------------------
// The line.
//-----------------------------------------------------------------------------

TEST(AssertStackFormat, ModuleAndOffsetAreWhatAddr2lineNeeds)
{
	char sz[256];
	SourceSharpAssertStack_FormatFrame(sz, sizeof(sz), 3, "/game/bin/linux64/client.so", 0x7f0000000000ULL,
									   0x7f00001234abULL, "Tracer_Draw");

	const std::string s(sz);
	EXPECT_NE(s.find("/game/bin/linux64/client.so"), std::string::npos);
	EXPECT_NE(s.find("+0x001234ab"), std::string::npos) << s;
	EXPECT_NE(s.find("Tracer_Draw"), std::string::npos);
	EXPECT_NE(s.find("#3"), std::string::npos);
}

// The trap this guards. An address below the module base is not an offset into
// it; formatted unsigned it wraps to a number addr2line resolves to a real —
// and entirely wrong — source line. Report it as unknown instead.
TEST(AssertStackFormat, AnAddressBelowTheModuleBaseIsNotFormattedAsAnOffset)
{
	char sz[256];
	SourceSharpAssertStack_FormatFrame(sz, sizeof(sz), 0, "/game/bin/linux64/client.so", 0x7f0000000000ULL, 0x100ULL,
									   "Something");

	const std::string s(sz);
	EXPECT_EQ(s.find("client.so+"), std::string::npos) << s;
	EXPECT_NE(s.find("??"), std::string::npos) << s;
	EXPECT_NE(s.find("0x0000000000000100"), std::string::npos) << s;
}

TEST(AssertStackFormat, NoModuleFallsBackToTheAbsoluteAddress)
{
	char sz[256];
	SourceSharpAssertStack_FormatFrame(sz, sizeof(sz), 11, NULL, 0, 0xCAFEBABEULL, NULL);

	const std::string s(sz);
	EXPECT_NE(s.find("??"), std::string::npos) << s;
	EXPECT_NE(s.find("cafebabe"), std::string::npos) << s;

	// An empty module name is the same case as no module at all.
	SourceSharpAssertStack_FormatFrame(sz, sizeof(sz), 11, "", 0, 0xCAFEBABEULL, NULL);
	EXPECT_NE(std::string(sz).find("??"), std::string::npos);
}

// A static function, or one --gc-sections kept but did not name, has no symbol.
// The offset is still exact, so the line is still the answer.
TEST(AssertStackFormat, NoSymbolStillLeavesAUsableOffset)
{
	char sz[256];
	SourceSharpAssertStack_FormatFrame(sz, sizeof(sz), 1, "/lib/client.so", 0x1000ULL, 0x1ABCULL, NULL);

	const std::string s(sz);
	EXPECT_NE(s.find("+0x00000abc"), std::string::npos) << s;
	EXPECT_NE(s.find("??"), std::string::npos) << s;
}

TEST(AssertStackFormat, TruncationIsSafeAndReported)
{
	OpaqueBuffer buf(16);

	const int nWritten = SourceSharpAssertStack_FormatFrame(buf.Data(), buf.Size(), 0,
															"/a/very/long/path/to/a/module/that/will/not/fit/client.so",
															0x1000ULL, 0x2000ULL, "AnAwfullyLongSymbolNameIndeed");

	// Never past the end, always terminated, never claims to have written more
	// than it did.
	EXPECT_LT(nWritten, (int)buf.Size());
	EXPECT_EQ(buf.Data()[buf.Size() - 1], '\0');
	EXPECT_EQ((int)strlen(buf.Data()), nWritten);
	EXPECT_TRUE(buf.SentinelIntact());
}

TEST(AssertStackFormat, ADegenerateBufferIsRefusedRatherThanWritten)
{
	char sz[4] = {'a', 'b', 'c', '\0'};

	EXPECT_EQ(0, SourceSharpAssertStack_FormatFrame(NULL, 100, 0, "m", 0, 1, "s"));
	EXPECT_EQ(0, SourceSharpAssertStack_FormatFrame(sz, 0, 0, "m", 0, 1, "s"));

	// cchOut == 0 must not have touched the buffer.
	EXPECT_EQ(sz[0], 'a');
}

//-----------------------------------------------------------------------------
// The LINE, as opposed to the frame: the newline that ends it.
//
// The emitter used to build the line with
// snprintf( szLine, 1024, "%s\n", szFrame ) out of a szFrame that was also
// 1024 -- so the one character that could not fit was the newline, and a long
// enough frame ran into the next one on the same line. GCC's
// -Wformat-truncation said so on every build; nothing failed, because nothing
// asked.
//
// A truncated line is fine. A line with no line ending is not, and that is
// what these separate.
//-----------------------------------------------------------------------------

// Long enough that no buffer here can hold it: the module path and the symbol
// are what get to four figures in a real report, where dli_sname is the
// undemangled _ZN name.
const char* const kLongModule = "/data/somebody/a/deeply/nested/checkout/game/mod_sharp/bin/linux64/client.so";
const char* const kLongSymbol = "_ZN20CVertexBuilderDetail9Position3fERKN9CMeshData19SomewhatLongTypeNameE";

TEST(AssertStackFormatLine, EndsInANewlineWhenEverythingFits)
{
	char sz[512];

	const int nLen =
		SourceSharpAssertStack_FormatFrameLine(sz, sizeof(sz), 7, "client.so", 0x1000ULL, 0x1abcULL, "Symbol");

	ASSERT_GT(nLen, 0);
	EXPECT_EQ((int)strlen(sz), nLen);
	EXPECT_EQ('\n', sz[nLen - 1]);

	// Exactly one newline, and it is the last character.
	EXPECT_EQ((size_t)(nLen - 1), std::string(sz).find('\n')) << sz;
}

// THE ONE THAT USED TO FAIL. A buffer the frame text cannot fit in: the text
// is clamped, the newline is not dropped.
TEST(AssertStackFormatLine, TheNewlineSurvivesAFrameThatDoesNotFit)
{
	OpaqueBuffer buf(24);

	const int nLen = SourceSharpAssertStack_FormatFrameLine(buf.Data(), buf.Size(), 0, kLongModule, 0x1000ULL,
															0x2000ULL, kLongSymbol);

	ASSERT_GT(nLen, 0);
	EXPECT_LT(nLen, (int)buf.Size());
	EXPECT_EQ((int)strlen(buf.Data()), nLen);
	EXPECT_EQ('\0', buf.Data()[buf.Size() - 1]);
	EXPECT_TRUE(buf.SentinelIntact());
	EXPECT_EQ('\n', buf.Data()[nLen - 1]) << "the line ran out of room and lost its ending: " << buf.Str();
}

// The same at every size from "only a newline fits" upwards, because the bug
// lived at exactly one boundary and a single hand-picked width can walk past
// it.
TEST(AssertStackFormatLine, EverySizeThatCanHoldALineEndsInOne)
{
	for (size_t cch = 2; cch <= 200; ++cch)
	{
		char sz[256];
		memset(sz, 'X', sizeof(sz));

		const int nLen =
			SourceSharpAssertStack_FormatFrameLine(sz, cch, 3, kLongModule, 0x1000ULL, 0x2000ULL, kLongSymbol);

		ASSERT_GT(nLen, 0) << "cch=" << cch;
		ASSERT_LT((size_t)nLen, cch) << "cch=" << cch;
		ASSERT_EQ((int)strlen(sz), nLen) << "cch=" << cch;
		ASSERT_EQ('\n', sz[nLen - 1]) << "cch=" << cch << " line='" << sz << "'";

		// Nothing written past what it claims.
		ASSERT_EQ('X', sz[cch]) << "cch=" << cch;
	}
}

TEST(AssertStackFormatLine, ABufferWithNoRoomForALineIsRefused)
{
	char sz[4] = {'a', 'b', 'c', '\0'};

	EXPECT_EQ(0, SourceSharpAssertStack_FormatFrameLine(NULL, 100, 0, "m", 0, 1, "s"));

	// cchOut == 0 must not have touched the buffer.
	EXPECT_EQ(0, SourceSharpAssertStack_FormatFrameLine(sz, 0, 0, "m", 0, 1, "s"));
	EXPECT_EQ('a', sz[0]);

	// One byte is room for a terminator and nothing else -- there is no line to
	// write, so the answer is an empty string rather than a stray newline.
	EXPECT_EQ(0, SourceSharpAssertStack_FormatFrameLine(sz, 1, 0, "m", 0, 1, "s"));
	EXPECT_EQ('\0', sz[0]);
}

//-----------------------------------------------------------------------------
// The counter channel.
//
// The hook is claimed once per process while anything asking about it is
// registered per side, so the counts travel as text through the environment
// rather than through a DLL-local static. Every way that channel can be broken
// has to answer zero, because zero means "did not move" and "did not move" is a
// loud failure — where a stale or garbage number reads as a pass.
//
// ITS ONE READER WAS THE SELF-TEST, which is gone; the hook still publishes,
// which is why the parsing stays pinned. A channel that starts answering
// garbage while nothing reads it is a channel that answers garbage on the day
// something does.
//-----------------------------------------------------------------------------

TEST(AssertStackCounter, APlainNumberReadsBack)
{
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("0"));
	EXPECT_EQ(7, SourceSharpAssertStack_ParseCounter("7"));
	EXPECT_EQ(1234567, SourceSharpAssertStack_ParseCounter("1234567"));
}

TEST(AssertStackCounter, LeadingBlanksAreTolerated)
{
	EXPECT_EQ(42, SourceSharpAssertStack_ParseCounter("  42"));
	EXPECT_EQ(42, SourceSharpAssertStack_ParseCounter("\t42"));
}

TEST(AssertStackCounter, AMissingOrEmptyVariableIsZeroRatherThanGarbage)
{
	// getenv on a name nothing set. The verdict compares before against after,
	// so zero here is read as "the hook did not fire" — which is what happened.
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter(NULL));
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter(""));
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("   "));
}

TEST(AssertStackCounter, ANegativeValueIsRefused)
{
	// These counts never go backwards. Accepting a sign would let a corrupt
	// value read as movement in one direction or the other.
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("-1"));
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("+3"));
}

TEST(AssertStackCounter, TrailingRubbishInvalidatesTheWholeValue)
{
	// Half-parsing "12x" as 12 would hide the fact that something other than the
	// hook wrote this variable.
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("12x"));
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("12 "));
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("1.5"));
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("9,9"));
}

TEST(AssertStackCounter, AValueTooLargeToBeACountIsRefusedRatherThanWrapped)
{
	// Overflowing a long long silently would produce a plausible small number,
	// and a plausible number is worse than an obvious zero.
	EXPECT_EQ(0, SourceSharpAssertStack_ParseCounter("99999999999999999999999"));
}

TEST(AssertStackCounter, WhatTheHookWritesIsWhatComesBack)
{
	// The round trip the hook actually performs: snprintf "%lld", then parse.
	const long long llValues[] = {0, 1, 9, 128, 4096, 2147483648LL};

	for (size_t i = 0; i < sizeof(llValues) / sizeof(llValues[0]); ++i)
	{
		char sz[32];
		snprintf(sz, sizeof(sz), "%lld", llValues[i]);
		EXPECT_EQ(llValues[i], SourceSharpAssertStack_ParseCounter(sz));
	}
}

} // namespace

//=============================================================================//
// WHETHER A FAILED ASSERT ENDS THE PROCESS.
//
// Four inputs, three of them switches a person sets, and every way this goes
// wrong is a wrong ANSWER rather than a crash: a self-test that kills the game,
// or a survey run that stops at the first assert and reports one finding where
// there were nine. Neither shows up as a failure anywhere, which is why the
// decision was made pure and put in this header. One fact per rule, so a broken
// rule names itself instead of failing a table.
//=============================================================================//

TEST(AssertFatality, ANonAssertSpewIsNeverFatal)
{
	// Msg/Warning/Error all reach the same spew function. Only SPEW_ASSERT may
	// end the process; a Warning that stopped the game would be catastrophic
	// and is exactly what a missing first test lets through.
	EXPECT_FALSE(SourceSharp_AssertShouldBeFatal(false, true, false, false));
}

TEST(AssertFatality, APlainFailedAssertIsFatal)
{
	EXPECT_TRUE(SourceSharp_AssertShouldBeFatal(true, true, false, false));
}

TEST(AssertFatality, AssertLogSurvivesTheAssert)
{
	// -assertlog is an explicit request to see EVERY assert in one pass.
	EXPECT_FALSE(SourceSharp_AssertShouldBeFatal(true, true, true, false));
}

TEST(AssertFatality, ADeliberateAssertIsNeverFatal)
{
	// The `bDeliberate` arm, which SOURCESHARP_ASSERTSTACK_FORCE sets. Its one
	// caller was cs_{cl,sv}_assertstack_selftest, which tripped an assert on
	// purpose to prove the stack hook was alive and is gone for having no
	// reader. The ARM is not: it is what keeps a deliberate assert from killing
	// the process, and the next thing that wants to trip one needs it.
	EXPECT_FALSE(SourceSharp_AssertShouldBeFatal(true, true, false, true));
}

TEST(AssertFatality, DeliberateBeatsFatalEvenWithoutAssertLog)
{
	// The precedence, stated as its own fact: DELIBERATE wins over FATAL with
	// no help from -assertlog. Swap the two tests in the implementation and
	// every other fact here still passes.
	EXPECT_FALSE(SourceSharp_AssertShouldBeFatal(true, true, false, true));
	EXPECT_TRUE(SourceSharp_AssertShouldBeFatal(true, true, false, false));
}

TEST(AssertFatality, FatalOffMeansNothingIsFatal)
{
	EXPECT_FALSE(SourceSharp_AssertShouldBeFatal(true, false, false, false));
	EXPECT_FALSE(SourceSharp_AssertShouldBeFatal(true, false, true, true));
}

//=============================================================================//
// WHETHER A SPEW LINE REACHES THE TERMINAL — SourceSharp_SpewShouldEcho.
//
// This table is the direct product of a three-crash misdiagnosis. `tools/
// live-gate`'s save/load step killed the game three times on 2026-09-07 and
// every report of it said `*** stack smashing detected ***`. The engine had in
// fact spewed
//
//     IVP Failed at ../ivp_utility/ivu_vhash.cxx 157
//
// at SPEW_ERROR, and this hook dropped it because it does not begin with
// "[SourceSharp". The message had to be recovered from a core dump. So the
// fourth input — "the engine will not return from this one" — has a fact of its
// own, and so does each of the two suppressors, because a suppressor that stops
// applying is a line printed twice and nothing louder.
//=============================================================================//

TEST(SpewEcho, OurOwnLineIsEchoed)
{
	EXPECT_TRUE(SourceSharp_SpewShouldEcho(/*claimed=*/true, /*stack=*/false,
										   /*ours=*/true, /*fatal=*/false));
}

TEST(SpewEcho, SomebodyElsesOrdinaryLineIsNot)
{
	// The prefix filter's whole purpose: the terminal is not a second copy of
	// the engine console. console.log already is that.
	EXPECT_FALSE(SourceSharp_SpewShouldEcho(true, false, /*ours=*/false,
											/*fatal=*/false));
}

TEST(SpewEcho, AFatalLineFromOutsideThisDllIsEchoed)
{
	// THE ONE THIS TABLE EXISTS FOR. vphysics's IVP failure is not ours and is
	// the last thing the process says; before this it was dropped and three
	// crashes were reported as a stack smash in the managed runtime.
	EXPECT_TRUE(SourceSharp_SpewShouldEcho(true, false, /*ours=*/false,
										   /*fatal=*/true));
}

TEST(SpewEcho, TheDllThatDidNotClaimTheEchoStaysQuietEvenForAFatal)
{
	// client.so and server.so both install the hook and BOTH see every message.
	// Without the claim a fatal prints twice — and the claim has to beat
	// fatality for that, which is the precedence this fact pins.
	EXPECT_FALSE(SourceSharp_SpewShouldEcho(/*claimed=*/false, false,
											/*ours=*/false, /*fatal=*/true));
	EXPECT_FALSE(SourceSharp_SpewShouldEcho(/*claimed=*/false, false,
											/*ours=*/true, /*fatal=*/false));
}

TEST(SpewEcho, AnAssertStackReportMidWriteIsNotEchoedOverEvenWhenFatal)
{
	// The -assertstack hook puts every line on stderr itself and then sends the
	// same line down the chain to reach console.log. Echoing here doubles it.
	EXPECT_FALSE(SourceSharp_SpewShouldEcho(true, /*stack=*/true,
											/*ours=*/true, /*fatal=*/false));
	EXPECT_FALSE(SourceSharp_SpewShouldEcho(true, /*stack=*/true,
											/*ours=*/false, /*fatal=*/true));
}

//=============================================================================//
// AND WHETHER A STACK IS PRINTED UNDER IT — lane 8e-physics.
//
// SourceSharp_SpewShouldEcho made the fatal LINE visible. It is still one line,
// and one line names the assert without naming a single caller — which is the
// whole argument assert_stack.h opens with. Diagnosing the last occurrence took
// three core dumps and a hand walk from a saved frame pointer.
//
// Four rules, one fact each. The two suppressors matter as much as the trigger:
// a fatal re-spewed on the way down would print thirty lines twice, and a frame
// walk started from inside an -assertstack report is the recursion this file
// already guards elsewhere.
//=============================================================================//

TEST(FatalSpewStack, AFatalGetsAStack)
{
	EXPECT_TRUE(SourceSharp_FatalSpewShouldWriteStack(/*fatal=*/true,
													  /*written=*/false,
													  /*stackWriting=*/false));
}

TEST(FatalSpewStack, AnOrdinaryLineDoesNot)
{
	// Every warning in the game passes through the same hook. A backtrace under
	// each one is the flood -assertstack exists to keep optional.
	EXPECT_FALSE(SourceSharp_FatalSpewShouldWriteStack(/*fatal=*/false, false, false));
}

TEST(FatalSpewStack, TheSecondFatalDoesNotPrintTheStackAgain)
{
	EXPECT_FALSE(SourceSharp_FatalSpewShouldWriteStack(/*fatal=*/true,
													   /*written=*/true, false));
}

TEST(FatalSpewStack, AReportMidWriteIsNotWalkedIntoAgain)
{
	EXPECT_FALSE(SourceSharp_FatalSpewShouldWriteStack(/*fatal=*/true, false,
													   /*stackWriting=*/true));
}
