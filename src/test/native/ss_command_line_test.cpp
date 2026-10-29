//========= SourceSharp — ss_command_line.h reads this process's argv ========//
//
// The matching is LocalCommandLine.cs's (and ICommandLine's): a parameter is
// its own argument, found ignoring case; its value is the next argument unless
// that is another parameter. The *In forms take an argv block; the last two
// facts read the real /proc/self/cmdline of this binary.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_command_line.h"

#include <errno.h>
#include <stdlib.h>
#include <string>

namespace
{
// "a\0b\0" from { "a", "b" }: /proc/self/cmdline's shape.
std::string Argv(std::initializer_list<const char*> args)
{
	std::string block;
	for (const char* a : args)
	{
		block += a;
		block += '\0';
	}
	return block;
}

bool Has(const std::string& argv, const char* param)
{
	return ss::CommandLineHasIn(argv.data(), argv.size(), param);
}

std::string Value(const std::string& argv, const char* param, const char* fallback, bool* pFound = nullptr)
{
	char out[64];
	const bool found = ss::CommandLineValueIn(argv.data(), argv.size(), param, fallback, out, sizeof(out));
	if (pFound)
		*pFound = found;
	return out;
}
}

TEST(SsCommandLine, AParameterIsFoundAsItsOwnArgumentIgnoringCase)
{
	const std::string argv = Argv({"hl2_linux", "-game", "mods/test", "-AssertLog"});
	EXPECT_TRUE(Has(argv, "-assertlog"));
	EXPECT_TRUE(Has(argv, "-game"));
}

TEST(SsCommandLine, APrefixOrAPieceOfAnArgumentIsNotTheParameter)
{
	const std::string argv = Argv({"hl2_linux", "-assertlogx", "x-assertlog"});
	EXPECT_FALSE(Has(argv, "-assertlog"));
}

TEST(SsCommandLine, TheValueIsTheNextArgument)
{
	bool found = false;
	EXPECT_EQ("66", Value(Argv({"hl2_linux", "-tickrate", "66", "-x"}), "-tickrate", "0", &found));
	EXPECT_TRUE(found);
}

TEST(SsCommandLine, AParameterFollowedByAnotherHasNoValue)
{
	bool found = true;
	EXPECT_EQ("0", Value(Argv({"hl2_linux", "-tickrate", "-debugapi"}), "-tickrate", "0", &found));
	EXPECT_FALSE(found);
	EXPECT_EQ("0", Value(Argv({"hl2_linux", "-tickrate", "+map"}), "-tickrate", "0"));
}

TEST(SsCommandLine, AParameterAtTheEndOrAbsentGetsTheDefault)
{
	EXPECT_EQ("d", Value(Argv({"hl2_linux", "-tickrate"}), "-tickrate", "d"));
	EXPECT_EQ("d", Value(Argv({"hl2_linux"}), "-tickrate", "d"));
	EXPECT_EQ("", Value(Argv({"hl2_linux"}), "-tickrate", nullptr));
}

TEST(SsCommandLine, ALongValueIsCutToTheBufferAndTerminated)
{
	const std::string longValue(200, 'v');
	const std::string argv = Argv({"hl2_linux", "-p", longValue.c_str()});
	char out[8];
	EXPECT_TRUE(ss::CommandLineValueIn(argv.data(), argv.size(), "-p", "", out, sizeof(out)));
	EXPECT_EQ(std::string(7, 'v'), out);
}

TEST(SsCommandLine, AnEmptyBlockOrParameterFindsNothing)
{
	EXPECT_FALSE(ss::CommandLineHasIn("", 0, "-x"));
	EXPECT_FALSE(Has(Argv({"hl2_linux"}), ""));
}

TEST(SsCommandLine, TheProcesssOwnCommandLineIsRead)
{
	// argv[0] as this process was started: the positive arm, which a reader
	// that saw nothing would fail.
	ASSERT_NE(nullptr, program_invocation_name);
	EXPECT_TRUE(ss::CommandLineHas(program_invocation_name));
	EXPECT_FALSE(ss::CommandLineHas("-surely-not-a-parameter-of-this-run"));
}

// SOURCESHARP_ARGS: the launcher moves SourceSharp's own flags (-assertlog,
// -debugapi, ...) off the engine's command line, which libtier0 caps at 512
// bytes, into this variable, one argument per line. They are read as if they
// followed argv.

TEST(SsCommandLine, LaunchArgsAreOneArgumentPerLine)
{
	EXPECT_EQ(Argv({"-assertlog", "-debuguiport", "0"}), ss::LaunchArgsBlock("-assertlog\n-debuguiport\n0"));
}

TEST(SsCommandLine, NoLaunchArgsVariableAddsNothing)
{
	EXPECT_EQ(std::string(), ss::LaunchArgsBlock(nullptr));
	EXPECT_EQ(std::string(), ss::LaunchArgsBlock(""));
}

TEST(SsCommandLine, EmptyLinesAreNotArguments)
{
	EXPECT_EQ(Argv({"-a", "-b"}), ss::LaunchArgsBlock("-a\n\n-b\n"));
}

TEST(SsCommandLine, TheVariableIsNamedSourceSharpArgs)
{
	EXPECT_STREQ("SOURCESHARP_ARGS", ss::kLaunchArgsVariable);
}

TEST(SsCommandLine, AFlagInTheLaunchArgsVariableIsOnTheCommandLine)
{
	EXPECT_FALSE(ss::CommandLineHas("-ss-launch-args-probe"));
	ASSERT_EQ(0, setenv("SOURCESHARP_ARGS", "-other\n-ss-launch-args-probe", 1));
	const bool has = ss::CommandLineHas("-ss-launch-args-probe");
	unsetenv("SOURCESHARP_ARGS");
	EXPECT_TRUE(has);
}
