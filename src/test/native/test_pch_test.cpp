// The native tests' precompiled gtest header (cmake/SourceSharpTests.cmake):
// one per set of tests that share every compile flag, built by the set's first
// test and reused by the rest.
//
// Read from the build's own compile database. The fact that matters is the
// third: a TU reads a header built with exactly its own flags. clang refuses
// some mismatches (an -O level, a -D), but not a different include path, and
// gcc refuses none: with -Winvalid-pch off it parses the header itself, and the
// header buys nothing. So the flags are compared here, token for token, rather
// than left to the compiler.
//
// The header puts gtest.h, and the standard library with it, ahead of a TU's
// first line, so only a TU that reads nothing but system headers before
// gtest.h may have it. Configure decides that from the files as they were; the
// fourth fact reads them as they are now.

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <map>
#include <regex>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kPchSuffix = "cmake_pch.hxx.cxx";

std::vector< std::string > Tokens( const std::string &strCommand )
{
	std::vector< std::string > out;
	std::istringstream in( strCommand );
	std::string tok;
	while ( in >> tok )
		out.push_back( tok );
	return out;
}

bool EndsWith( const std::string &s, const std::string &suffix )
{
	return s.size() >= suffix.size() && s.compare( s.size() - suffix.size(), suffix.size(), suffix ) == 0;
}

// The header a TU reads (-Xclang -include-pch -Xclang <path>), or "".
std::string PchRead( const std::vector< std::string > &tokens )
{
	for ( size_t i = 0; i + 2 < tokens.size(); i++ )
	{
		if ( tokens[ i ] == "-include-pch" && tokens[ i + 1 ] == "-Xclang" )
			return tokens[ i + 2 ];
	}
	return "";
}

// A command's flags with what names its input and output, and what says how to
// build or read the header, taken out: what is left must be the same for a
// header and every TU that reads it.
std::vector< std::string > Flags( const std::vector< std::string > &tokens )
{
	std::vector< std::string > out;
	for ( size_t i = 1; i < tokens.size(); i++ )   // [0] is the compiler
	{
		const std::string &t = tokens[ i ];
		if ( t == "-o" || t == "-c" || t == "-x" )
		{
			i++;
			continue;
		}
		if ( t == "-Xclang" && i + 1 < tokens.size() )
		{
			const std::string &next = tokens[ i + 1 ];
			if ( next == "-emit-pch" )
			{
				i++;
				continue;
			}
			if ( ( next == "-include" || next == "-include-pch" ) && i + 3 < tokens.size() )
			{
				i += 3;   // -Xclang -include -Xclang <path>
				continue;
			}
		}
		if ( t == "-fpch-instantiate-templates" )
			continue;   // how the header is built, not how a TU is compiled
		out.push_back( t );
	}
	return out;
}

// A header's name, the same however a command spells its path: a reader names
// it by its absolute path, its own build by the path under the build directory,
// and a target's directory under CMakeFiles/ is unique to it.
std::string HeaderKey( const std::string &strPath )
{
	const size_t at = strPath.rfind( "CMakeFiles/" );
	return at == std::string::npos ? strPath : strPath.substr( at );
}

// _ss_cpptest_pch_safe's rule: the file reads gtest/gtest.h, and every
// directive before it is an #include <...>.
bool SystemHeadersOnlyBeforeGtest( std::istream &in )
{
	static const std::regex directive( "^[ \\t]*#" );
	static const std::regex gtest( "^[ \\t]*#[ \\t]*include[ \\t]*[<\"]gtest/gtest\\.h[>\"]" );
	static const std::regex system( "^[ \\t]*#[ \\t]*include[ \\t]*<" );
	std::string line;
	while ( std::getline( in, line ) )
	{
		if ( !std::regex_search( line, directive ) )
			continue;
		if ( std::regex_search( line, gtest ) )
			return true;
		if ( !std::regex_search( line, system ) )
			return false;
	}
	return false;
}

bool SystemHeadersOnlyBeforeGtest( const std::string &strText )
{
	std::istringstream in( strText );
	return SystemHeadersOnlyBeforeGtest( static_cast< std::istream & >( in ) );
}

struct Header
{
	std::vector< std::string > flags;     // the header's own build
	std::vector< std::string > readers;   // the TUs that read it
};

// Each header (by HeaderKey) with its build's flags and its readers.
std::map< std::string, Header > Headers()
{
	std::map< std::string, Header > out;
	for ( const cmake_graph::CompileEntry &e : cmake_graph::CompileCommands() )
	{
		const std::vector< std::string > tokens = Tokens( e.strCommand );
		if ( EndsWith( e.strFile, kPchSuffix ) )
		{
			out[ HeaderKey( e.strOutput ) ].flags = Flags( tokens );
			continue;
		}
		const std::string strPch = PchRead( tokens );
		if ( !strPch.empty() )
			out[ HeaderKey( strPch ) ].readers.push_back( e.strFile );
	}
	return out;
}

} // namespace

TEST( TestPch, TheTestsCompileAgainstAPrecompiledGtestHeader )
{
	// 191 test TUs at 2026-10-26: the floor is well under that, and
	// fails if the headers are switched off or attached to nothing.
	size_t nReaders = 0;
	for ( const auto &h : Headers() )
		nReaders += h.second.readers.size();
	EXPECT_GE( nReaders, 150u ) << "few test TUs read a precompiled gtest.h: is SS_CPPTEST_PCH off?";
}

TEST( TestPch, EveryHeaderIsBuiltAndReadByTwoOrMoreTUs )
{
	// One TU gains nothing from a header: building it costs more than the one
	// parse it saves. And a header nothing builds is one no TU can read.
	const std::map< std::string, Header > headers = Headers();
	ASSERT_FALSE( headers.empty() );
	for ( const auto &h : headers )
	{
		EXPECT_FALSE( h.second.flags.empty() ) << h.first << " is read but nothing builds it";
		EXPECT_GE( h.second.readers.size(), 2u ) << h.first << " serves one TU";
	}
}

TEST( TestPch, EveryTuReadsAHeaderBuiltWithItsOwnFlags )
{
	const std::map< std::string, Header > headers = Headers();
	for ( const cmake_graph::CompileEntry &e : cmake_graph::CompileCommands() )
	{
		const std::vector< std::string > tokens = Tokens( e.strCommand );
		const std::string strPch = PchRead( tokens );
		if ( strPch.empty() )
			continue;
		const auto h = headers.find( HeaderKey( strPch ) );
		ASSERT_NE( h, headers.end() ) << e.strFile << " reads " << strPch << ", which nothing builds";
		EXPECT_EQ( h->second.flags, Flags( tokens ) ) << e.strFile << " reads " << strPch;
	}
}

TEST( TestPch, TheFlagComparisonSeesAnIncludeDirectory )
{
	// The control for the fact above: the one difference clang would let
	// through is an include directory, and Flags() must keep it.
	const std::vector< std::string > a = Flags( Tokens( "clang++ -I/a -O1 -o x.o -c x.cpp" ) );
	const std::vector< std::string > b = Flags( Tokens( "clang++ -I/a -I/b -O1 -o y.o -c y.cpp" ) );
	EXPECT_NE( a, b );
	const std::vector< std::string > header = Flags( Tokens(
		"clang++ -I/a -O1 -fpch-instantiate-templates -Xclang -emit-pch -Xclang -include -Xclang /h.hxx "
		"-x c++-header -o h.pch -c h.cxx" ) );
	const std::vector< std::string > reader = Flags( Tokens(
		"clang++ -I/a -O1 -Xclang -include-pch -Xclang /h.pch -Xclang -include -Xclang /h.hxx -o x.o -c x.cpp" ) );
	EXPECT_EQ( header, reader );
}

TEST( TestPch, EveryTuThatReadsAHeaderReadsOnlySystemHeadersBeforeGtest )
{
	size_t nRead = 0;
	for ( const auto &h : Headers() )
	{
		for ( const std::string &strFile : h.second.readers )
		{
			std::ifstream in( strFile );
			ASSERT_TRUE( in.good() ) << strFile << " is in the compile database but cannot be read";
			nRead++;
			EXPECT_TRUE( SystemHeadersOnlyBeforeGtest( in ) )
				<< strFile << " reads a precompiled gtest.h but has a directive other than #include <...> before"
				<< " its own gtest.h, or none: reconfigure, and _ss_cpptest_pch_safe leaves it out";
		}
	}
	EXPECT_GT( nRead, 0u );
}

TEST( TestPch, TheRuleKeepsOnlySystemHeadersAheadOfGtest )
{
	// The control for the fact above, and the rule's every arm.
	EXPECT_TRUE( SystemHeadersOnlyBeforeGtest( "// note\n#include <string>\n#include \"gtest/gtest.h\"\n#include \"x.h\"\n" ) );
	EXPECT_TRUE( SystemHeadersOnlyBeforeGtest( "#include <gtest/gtest.h>\n" ) );
	EXPECT_FALSE( SystemHeadersOnlyBeforeGtest( "#include \"tier0/memdbgon.h\"\n#include \"gtest/gtest.h\"\n" ) );
	EXPECT_FALSE( SystemHeadersOnlyBeforeGtest( "#define X 1\n#include \"gtest/gtest.h\"\n" ) );
	EXPECT_FALSE( SystemHeadersOnlyBeforeGtest( "  #ifdef X\n#include \"gtest/gtest.h\"\n#endif\n" ) );
	EXPECT_FALSE( SystemHeadersOnlyBeforeGtest( "#include <string>\nint f();\n" ) );   // never reads gtest.h
}
