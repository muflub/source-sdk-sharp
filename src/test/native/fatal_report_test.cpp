//========= SourceSharp — the one way this mod stops the process ==============//
//
// SourceSharp_FatalReport (fatal_report.cpp): the user's design for a failed
// assert or Error() -- the report and the stack to stderr, the same report to
// $SOURCESHARP_ASSERT_DIR/assert-<pid>.txt, and a negative exit code the
// launcher keys on.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "fatal_report.h"

#include <dirent.h>
#include <stdlib.h>
#include <string>
#include <sys/stat.h>
#include <unistd.h>
#include <fstream>
#include <sstream>

namespace
{
std::string MakeTempDir()
{
	char szTemplate[] = "/tmp/fatal_report_test.XXXXXX";
	const char *pszDir = mkdtemp( szTemplate );
	return pszDir ? pszDir : "";
}

// The one assert-<pid>.txt a child left in pszDir, or "".
std::string ReportIn( const std::string &dir )
{
	std::string found;
	if ( DIR *d = opendir( dir.c_str() ) )
	{
		while ( dirent *e = readdir( d ) )
		{
			const std::string name = e->d_name;
			if ( name.rfind( "assert-", 0 ) == 0 && name.size() > 11 &&
			     name.compare( name.size() - 4, 4, ".txt" ) == 0 )
			{
				std::ifstream in( dir + "/" + name );
				std::stringstream text;
				text << in.rdbuf();
				found = text.str();
			}
		}
		closedir( d );
	}
	return found;
}
}

TEST( FatalReportDeathTest, TheExitCodeIsNegativeThreeAsTheParentSeesIt )
{
	EXPECT_EQ( 253, kSourceSharpFatalExitCode );
	EXPECT_EQ( 253, (unsigned char)-3 );
	EXPECT_EXIT( SourceSharp_FatalReport( "ASSERT", "boom\n" ),
	             ::testing::ExitedWithCode( 253 ), "" );
}

TEST( FatalReportDeathTest, TheReportAndTheStackGoToStderr )
{
	EXPECT_EXIT( SourceSharp_FatalReport( "ASSERT", "the widget broke\n" ),
	             ::testing::ExitedWithCode( kSourceSharpFatalExitCode ),
	             "FATAL ASSERT: the widget broke(.|\n)*stopping here(.|\n)*stack \\([0-9]+ frame" );
}

TEST( FatalReportDeathTest, AnErrorSaysErrorAndIsNotToldAboutAssertlog )
{
	EXPECT_EXIT( SourceSharp_FatalReport( "ERROR", "no engine" ),
	             ::testing::ExitedWithCode( kSourceSharpFatalExitCode ),
	             "FATAL ERROR: no engine\n\\[SourceSharp\\] the process is stopping here.\n" );
}

TEST( FatalReportDeathTest, TheSameReportGoesToAssertPidTxtInTheNamedDirectory )
{
	const std::string dir = MakeTempDir();
	ASSERT_FALSE( dir.empty() );
	setenv( "SOURCESHARP_ASSERT_DIR", dir.c_str(), 1 );

	EXPECT_EXIT( SourceSharp_FatalReport( "ASSERT", "written down\n" ),
	             ::testing::ExitedWithCode( kSourceSharpFatalExitCode ), "" );
	unsetenv( "SOURCESHARP_ASSERT_DIR" );

	const std::string report = ReportIn( dir );
	EXPECT_NE( std::string::npos, report.find( "FATAL ASSERT: written down" ) ) << report;
	EXPECT_NE( std::string::npos, report.find( "stack (" ) ) << report;
}

TEST( FatalReportDeathTest, WithNoDirectoryNamedNoFileIsWritten )
{
	const std::string dir = MakeTempDir();
	ASSERT_FALSE( dir.empty() );
	unsetenv( "SOURCESHARP_ASSERT_DIR" );
	ASSERT_EQ( 0, chdir( dir.c_str() ) );

	EXPECT_EXIT( SourceSharp_FatalReport( "ASSERT", "nowhere\n" ),
	             ::testing::ExitedWithCode( kSourceSharpFatalExitCode ), "" );

	EXPECT_EQ( "", ReportIn( dir ) );
}
