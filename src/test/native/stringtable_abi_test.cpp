//====== SourceSharp — the mod-declared string-table ABI ======================//
//
// plan_gamedll.md Part 13, plan_gamedll_after.md Phase A8.
//
// WHAT IS UNDER TEST IS THE SHIPPED CODE, not a transcription of it.
// shell_stringtables.h is ENGINE-FREE on purpose -- networkstringtabledefs.h
// includes nothing and INetworkStringTable / INetworkStringTableContainer are
// pure abstract classes over int and char* -- so CShellStringTables can be
// compiled and RUN here against a container this file writes. Everything below
// exercises the real bodies. The six entry points in shell_stringtables.cpp are
// one line each and are the only part that needs the engine; they are covered
// by the live gate and by AbiParityTests' wiring check, not from here.
//
// WHY THERE IS ANYTHING TO TEST IN WHAT LOOKS LIKE SIX FORWARDS. The forwards
// are the .cpp. What is in the header is the part that decides:
//
//   * a TABLEID that no longer resolves -- which is what a mod is holding after
//     a hot reload -- must be REFUSED and must say so, not indexed into;
//   * a create for a name that already exists must answer the EXISTING table,
//     because CreateNetworkStringTables runs once per map load and a
//     cs_sv_reload runs between two of them, so a mod asking again is the
//     normal case rather than a mistake;
//   * every miss must be -1 and never INVALID_STRING_INDEX, which is an
//     unsigned short 0xFFFF and would cross this boundary as 65535 -- a
//     plausible index that every `if (i >= 0)` on the managed side reads as
//     success;
//   * "the table is not there" and "the table is empty" must not collapse into
//     one answer, or a mod that asked for the wrong name iterates zero entries
//     and concludes its own table is empty;
//   * an entry that does not fit the caller's buffer must be REFUSED rather
//     than truncated, because half a material path names a different file.
//
// None of those is visible in the source; all of them are properties of a body.
//
// THE ORDER of the managed slot against the two index-0 precaches is a
// different question and is NOT here: sourcesharp/test/stringtable_index0_test.cpp
// replays the whole shipped sequence for it, with this ABI's call-out modelled
// as an add. Two instruments, two questions -- and the replay is the one that
// must not be duplicated, because a second copy of it would cover half the
// sequence with nobody noticing which half.
//
//=============================================================================//

#include <string>
#include <vector>
#include "gtest/gtest.h"

#include "shell_stringtables.h"

namespace
{

//-----------------------------------------------------------------------------
// The console half the shipped header declares and does not define. Recorded
// rather than printed, so that "it refused" and "it refused SILENTLY" are
// different results here -- which is the whole of Part 7's rule applied one
// level down.
//-----------------------------------------------------------------------------
std::vector<std::string> *g_pWarnings = NULL;

//-----------------------------------------------------------------------------
// A string table, modelled the way the engine numbers one: AddString appends a
// value it does not already hold and answers its position, an existing value
// answers the position it already had, and a full table answers
// INVALID_STRING_INDEX.
//-----------------------------------------------------------------------------
class FakeTable : public INetworkStringTable
{
public:
	FakeTable( const char *pszName, TABLEID id, int nMax )
		: m_bLastAddWasServer( false ), m_Name( pszName ), m_Id( id ), m_nMax( nMax )
	{
	}

	virtual const char *GetTableName( void ) const { return m_Name.c_str(); }
	virtual TABLEID     GetTableId( void ) const   { return m_Id; }
	virtual int         GetNumStrings( void ) const { return (int)m_Strings.size(); }
	virtual int         GetMaxStrings( void ) const { return m_nMax; }
	virtual int         GetEntryBits( void ) const  { return 16; }

	virtual void SetTick( int tick ) {}
	virtual bool ChangedSinceTick( int tick ) const { return false; }

	virtual int AddString( bool bIsServer, const char *value, int length = -1, const void *userdata = 0 )
	{
		m_bLastAddWasServer = bIsServer;

		int nExisting = FindStringIndex( value );
		if ( nExisting != (int)INVALID_STRING_INDEX )
			return nExisting;

		if ( (int)m_Strings.size() >= m_nMax )
			return (int)INVALID_STRING_INDEX;

		m_Strings.push_back( value );
		return (int)m_Strings.size() - 1;
	}

	virtual const char *GetString( int stringNumber )
	{
		if ( stringNumber < 0 || stringNumber >= (int)m_Strings.size() )
			return NULL;

		return m_Strings[ stringNumber ].c_str();
	}

	virtual void SetStringUserData( int stringNumber, int length, const void *userdata ) {}
	virtual const void *GetStringUserData( int stringNumber, int *length ) { return NULL; }

	virtual int FindStringIndex( char const *string )
	{
		if ( string == NULL )
			return (int)INVALID_STRING_INDEX;

		for ( size_t i = 0; i < m_Strings.size(); i++ )
			if ( m_Strings[ i ] == string )
				return (int)i;

		return (int)INVALID_STRING_INDEX;
	}

	virtual void SetStringChangedCallback( void *object, pfnStringChanged changeFunc ) {}

	// FALSE until an add happens, so EveryAddIsAServerAdd is asserting on a
	// value something wrote rather than on whatever was on the heap.
	bool m_bLastAddWasServer;

private:
	std::string m_Name;
	TABLEID m_Id;
	int m_nMax;
	std::vector<std::string> m_Strings;
};

//-----------------------------------------------------------------------------
// The container. Counts its creates, because "did not create a second table"
// is the assertion the hot-reload case actually rests on, and a create the
// caller then ignored would look identical from the answer alone.
//
// m_bAllowCreation is the ENGINE's window, modelled: the real container refuses
// a create outside IServerGameDLL::CreateNetworkStringTables, which is the
// reason that slot exists at all.
//-----------------------------------------------------------------------------
class FakeContainer : public INetworkStringTableContainer
{
public:
	FakeContainer() : m_nCreates( 0 ), m_bAllowCreation( true ) {}

	virtual ~FakeContainer()
	{
		for ( size_t i = 0; i < m_Tables.size(); i++ )
			delete m_Tables[ i ];
	}

	virtual INetworkStringTable *CreateStringTable( const char *tableName, int maxentries,
	                                                int userdatafixedsize = 0, int userdatanetworkbits = 0 )
	{
		m_nCreates++;

		if ( !m_bAllowCreation )
			return NULL;

		FakeTable *pTable = new FakeTable( tableName, (TABLEID)m_Tables.size(), maxentries );
		m_Tables.push_back( pTable );
		return pTable;
	}

	virtual void RemoveAllTables() {}

	virtual INetworkStringTable *FindTable( const char *tableName ) const
	{
		if ( tableName == NULL )
			return NULL;

		for ( size_t i = 0; i < m_Tables.size(); i++ )
			if ( std::string( m_Tables[ i ]->GetTableName() ) == tableName )
				return m_Tables[ i ];

		return NULL;
	}

	virtual INetworkStringTable *GetTable( TABLEID stringTable ) const
	{
		if ( stringTable < 0 || stringTable >= (TABLEID)m_Tables.size() )
			return NULL;

		return m_Tables[ stringTable ];
	}

	virtual int GetNumTables() const { return (int)m_Tables.size(); }

	virtual INetworkStringTable *CreateStringTableEx( const char *tableName, int maxentries,
	                                                  int userdatafixedsize = 0, int userdatanetworkbits = 0,
	                                                  bool bIsFilenames = false )
	{
		return CreateStringTable( tableName, maxentries, userdatafixedsize, userdatanetworkbits );
	}

	virtual void SetAllowClientSideAddString( INetworkStringTable *table, bool bAllowClientSideAddString ) {}

	int  m_nCreates;
	bool m_bAllowCreation;

private:
	std::vector<FakeTable *> m_Tables;
};

//-----------------------------------------------------------------------------
// One container, one warning log, per fact.
//-----------------------------------------------------------------------------
class StringTableAbi : public ::testing::Test
{
protected:
	virtual void SetUp()
	{
		m_Warnings.clear();
		g_pWarnings = &m_Warnings;
	}

	virtual void TearDown()
	{
		g_pWarnings = NULL;
	}

	CShellStringTables Abi()          { return CShellStringTables( &m_Container ); }
	CShellStringTables AbiWithNoContainer() { return CShellStringTables( NULL ); }

	// Every warning, joined -- so a fact can ask whether one NAMED the thing it
	// is about rather than merely that something was said.
	std::string Warnings() const
	{
		std::string all;
		for ( size_t i = 0; i < m_Warnings.size(); i++ )
			all += m_Warnings[ i ];
		return all;
	}

	FakeContainer m_Container;
	std::vector<std::string> m_Warnings;
};

} // namespace

//-----------------------------------------------------------------------------
// The definition the shipped header declares. Outside the anonymous namespace
// because it is an ordinary external symbol there.
//-----------------------------------------------------------------------------
void ShellStringTables_Warning( const char *pMessage )
{
	if ( g_pWarnings != NULL && pMessage != NULL )
		g_pWarnings->push_back( pMessage );
}

//-----------------------------------------------------------------------------
// CREATE, FILL, READ BACK -- the phase's own gate, run.
//
// Read back through GetString rather than compared with the string that was
// written: comparing a value with itself passes against a GetString that
// answers its argument, against one that answers nothing, and against a table
// that was never filled.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, ATableIsCreatedFilledAndReadBack )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "SourceSharpManaged", 32 );
	ASSERT_GE( id, 0 ) << "the table was not created at all";

	const int index = abi.AddString( id, "abi 208" );
	EXPECT_EQ( index, 0 ) << "the first string in a fresh table is index 0";

	char buf[ 64 ];
	const int nBytes = abi.GetString( id, index, buf, sizeof( buf ) );

	EXPECT_EQ( nBytes, 7 ) << "the byte count is the string's length without its terminator";
	EXPECT_STREQ( buf, "abi 208" ) << "what came back is not what went in";
	EXPECT_EQ( abi.GetNumStrings( id ), 1 );
}

TEST_F( StringTableAbi, TheIdIsTheEnginesOwnTableId )
{
	CShellStringTables abi = Abi();

	const int first  = abi.Create( "First", 8 );
	const int second = abi.Create( "Second", 8 );

	EXPECT_NE( first, second ) << "two tables share one id";
	EXPECT_EQ( abi.Find( "First" ), first )
		<< "the id a create answered is not the id a find answers for the same "
		<< "table. A mod that keeps the first and looks up the second is then "
		<< "reading a different table with no error anywhere.";
	EXPECT_EQ( abi.Find( "Second" ), second );
}

//-----------------------------------------------------------------------------
// THE HOT-RELOAD CASE, which is the reason Find exists and the reason Create
// does not simply fail on a taken name.
//
// CreateNetworkStringTables runs once per MAP LOAD. cs_sv_reload runs between
// two of them and rebuilds the managed side, so the mod asks for its table
// again with no second window to create it in. The right answer is the table
// that is already there.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, CreatingATakenNameAnswersTheExistingTable )
{
	CShellStringTables abi = Abi();

	const int first = abi.Create( "SourceSharpManaged", 32 );
	abi.AddString( first, "abi 208" );

	const int again = abi.Create( "SourceSharpManaged", 32 );

	EXPECT_EQ( again, first )
		<< "asking again for a table that exists answered a different id. After "
		<< "a cs_sv_reload that is every mod's first call, and a second id "
		<< "means the mod is writing somewhere nobody reads.";

	EXPECT_EQ( m_Container.m_nCreates, 1 )
		<< "the container was asked to CREATE a second table for a name it "
		<< "already had. Two tables of one name is not something the engine has "
		<< "an answer for, and the caller cannot tell from the id which it got.";

	EXPECT_EQ( abi.GetNumStrings( again ), 1 )
		<< "the table the second create answered is empty, so it is not the one "
		<< "the first create made";
}

TEST_F( StringTableAbi, CreatingATakenNameSaysSoRatherThanDoingItQuietly )
{
	CShellStringTables abi = Abi();

	abi.Create( "SourceSharpManaged", 32 );
	m_Warnings.clear();
	abi.Create( "SourceSharpManaged", 32 );

	ASSERT_FALSE( m_Warnings.empty() )
		<< "answering the existing table is right for a reload and WRONG for "
		<< "two pieces of mod code claiming one name -- and the two are "
		<< "indistinguishable from inside. The warning is the only thing that "
		<< "makes the second case findable.";

	EXPECT_NE( Warnings().find( "SourceSharpManaged" ), std::string::npos )
		<< "the warning does not name the table, so a reader has nothing to "
		<< "grep for and nothing to fix";
}

//-----------------------------------------------------------------------------
// A STALE ID. What a mod is holding after a reload if it cached one, and what
// it is holding after a level change either way.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, AStaleTableIdIsRefusedRatherThanIndexedInto )
{
	CShellStringTables abi = Abi();

	abi.Create( "Real", 8 );

	EXPECT_EQ( abi.AddString( 99, "x" ), -1 );
	EXPECT_EQ( abi.FindStringIndex( 99, "x" ), -1 );
	EXPECT_EQ( abi.GetNumStrings( 99 ), -1 );

	char buf[ 8 ];
	EXPECT_EQ( abi.GetString( 99, 0, buf, sizeof( buf ) ), -1 );
}

TEST_F( StringTableAbi, AStaleTableIdSaysWhatToDoAboutIt )
{
	CShellStringTables abi = Abi();
	abi.AddString( 99, "x" );

	EXPECT_NE( Warnings().find( "stale" ), std::string::npos )
		<< "a refused id says nothing about WHY it was refused. The failure "
		<< "mode is a number kept across a reload, and the fix is to ask for "
		<< "the table by name -- neither of which a bare -1 conveys.";
}

TEST_F( StringTableAbi, TheInvalidTableSentinelIsNotResolved )
{
	CShellStringTables abi = Abi();

	// INVALID_STRING_TABLE is what every refusal on this ABI answers, so it is
	// also what a caller that did not check hands straight back in. It must not
	// resolve to a table, and it must not warn either -- warning on the value
	// this ABI itself produces would put a line on the console for every mod
	// that handled its own error correctly.
	EXPECT_EQ( abi.GetNumStrings( INVALID_STRING_TABLE ), -1 );
	EXPECT_TRUE( m_Warnings.empty() )
		<< "passing back the refusal sentinel warned. Every mod that checks its "
		<< "return value and then cleans up would produce that line.";
}

//-----------------------------------------------------------------------------
// EVERY MISS IS -1. The INVALID_STRING_INDEX trap, which is the one failure
// here that would look like data.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, AMissingStringIsMinusOneAndNotSixtyFiveThousand )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	abi.AddString( id, "present" );

	const int miss = abi.FindStringIndex( id, "absent" );

	EXPECT_EQ( miss, -1 )
		<< "a missing string answered " << miss << ". The engine's own "
		<< "INVALID_STRING_INDEX is an unsigned short 0xFFFF, which crosses "
		<< "this boundary as 65535 -- a positive number that every "
		<< "'if (i >= 0)' on the managed side reads as SUCCESS, and that then "
		<< "gets used as an index.";

	EXPECT_NE( miss, 65535 );
}

TEST_F( StringTableAbi, AFullTableRefusesAndNamesItself )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "Small", 2 );
	EXPECT_EQ( abi.AddString( id, "a" ), 0 );
	EXPECT_EQ( abi.AddString( id, "b" ), 1 );

	m_Warnings.clear();
	EXPECT_EQ( abi.AddString( id, "c" ), -1 )
		<< "a full table answered an index. The engine hands back "
		<< "INVALID_STRING_INDEX there, which is 65535.";

	EXPECT_NE( Warnings().find( "Small" ), std::string::npos )
		<< "the table filled up and nothing said which one. A string table is "
		<< "sized once when it is created and cannot be resized, so this is a "
		<< "sizing decision somebody has to revisit -- silently dropping the "
		<< "entry makes it a bug report about missing content instead.";
}

TEST_F( StringTableAbi, TheSameStringTwiceIsOneEntryAtOneIndex )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );

	EXPECT_EQ( abi.AddString( id, "once" ), 0 );
	EXPECT_EQ( abi.AddString( id, "once" ), 0 )
		<< "the engine answers the index a string already had rather than "
		<< "appending a duplicate. Anything else here would silently spend the "
		<< "table's fixed budget on repeats.";
	EXPECT_EQ( abi.GetNumStrings( id ), 1 );
}

//-----------------------------------------------------------------------------
// READING.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, AnIndexPastTheEndAnswersMinusOneAndWritesNothing )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	abi.AddString( id, "only" );

	char buf[ 16 ];
	buf[ 0 ] = 'X';

	EXPECT_EQ( abi.GetString( id, 1, buf, sizeof( buf ) ), -1 );
	EXPECT_STREQ( buf, "" )
		<< "a refused read left the caller's buffer holding whatever was in it. "
		<< "The managed side reads the buffer when the count is not negative, "
		<< "and a caller that gets the check slightly wrong then reads stack "
		<< "garbage instead of nothing.";

	EXPECT_EQ( abi.GetString( id, -1, buf, sizeof( buf ) ), -1 );
}

TEST_F( StringTableAbi, AnEntryThatDoesNotFitIsRefusedRatherThanTruncated )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	const int index = abi.AddString( id, "materials/models/props/verylongname.vmt" );

	char buf[ 8 ];
	EXPECT_EQ( abi.GetString( id, index, buf, sizeof( buf ) ), -1 )
		<< "an entry longer than the buffer was truncated into it. Half a "
		<< "material path is a DIFFERENT material path -- it names a file that "
		<< "may well exist -- so a short answer here is worse than no answer.";

	EXPECT_NE( Warnings().find( "T" ), std::string::npos )
		<< "the read was refused and nothing said so";
}

TEST_F( StringTableAbi, AnEntryThatExactlyFitsIsNotRefused )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	const int index = abi.AddString( id, "1234567" );

	// Seven characters and a terminator in eight bytes. The off-by-one on the
	// other side of the refusal above, which is the edit that "fixes" a
	// truncation bug by refusing everything.
	char buf[ 8 ];
	EXPECT_EQ( abi.GetString( id, index, buf, sizeof( buf ) ), 7 );
	EXPECT_STREQ( buf, "1234567" );
}

TEST_F( StringTableAbi, AnEmptyEntryIsZeroBytesAndNotARefusal )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	const int index = abi.AddString( id, "" );

	ASSERT_GE( index, 0 );

	char buf[ 8 ];
	EXPECT_EQ( abi.GetString( id, index, buf, sizeof( buf ) ), 0 )
		<< "an empty entry read as a refusal. A table may legitimately hold an "
		<< "empty string, and answering -1 for it makes 'there is no such entry' "
		<< "indistinguishable from 'the entry is blank'.";
}

//-----------------------------------------------------------------------------
// "NOT THERE" AND "EMPTY" ARE DIFFERENT ANSWERS.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, AnAbsentTableCountsMinusOneAndAnEmptyOneCountsZero )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "Empty", 8 );

	EXPECT_EQ( abi.GetNumStrings( id ), 0 );
	EXPECT_EQ( abi.GetNumStrings( 77 ), -1 )
		<< "a table that is not there counted 0, which is what an empty one "
		<< "counts. A mod that asked for the wrong name then iterates zero "
		<< "entries and concludes its own table is empty.";
}

TEST_F( StringTableAbi, FindingAnAbsentTableIsMinusOneAndSilent )
{
	CShellStringTables abi = Abi();

	EXPECT_EQ( abi.Find( "NoSuchTable" ), INVALID_STRING_TABLE );
	EXPECT_TRUE( m_Warnings.empty() )
		<< "asking whether a table exists warned when it does not. Find is the "
		<< "question 'is it there', and a mod's startup asks it precisely "
		<< "because it does not know -- warning there puts a line on the "
		<< "console for correct code.";
}

//-----------------------------------------------------------------------------
// REFUSALS THAT ARE NOT ABOUT AN ID.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, ANamelessTableIsRefused )
{
	CShellStringTables abi = Abi();

	EXPECT_EQ( abi.Create( NULL, 8 ), INVALID_STRING_TABLE );
	EXPECT_EQ( abi.Create( "", 8 ), INVALID_STRING_TABLE );
	EXPECT_EQ( m_Container.m_nCreates, 0 )
		<< "the container was asked to create a table with no name";
	EXPECT_FALSE( m_Warnings.empty() );
}

TEST_F( StringTableAbi, ATableWithNoRoomInItIsRefused )
{
	CShellStringTables abi = Abi();

	EXPECT_EQ( abi.Create( "T", 0 ), INVALID_STRING_TABLE );
	EXPECT_EQ( abi.Create( "T", -1 ), INVALID_STRING_TABLE );
	EXPECT_EQ( m_Container.m_nCreates, 0 )
		<< "a table sized zero or less was passed to the engine. The size is "
		<< "fixed for the table's life, so this is not a value that becomes "
		<< "workable later.";
}

TEST_F( StringTableAbi, ANullStringIsRefusedRatherThanAdded )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );

	EXPECT_EQ( abi.AddString( id, NULL ), -1 );
	EXPECT_EQ( abi.GetNumStrings( id ), 0 );
	EXPECT_EQ( abi.FindStringIndex( id, NULL ), -1 );
}

TEST_F( StringTableAbi, ARefusedBufferIsRefusedBeforeTheTableIsTouched )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	abi.AddString( id, "value" );

	char buf[ 4 ];
	EXPECT_EQ( abi.GetString( id, 0, NULL, 16 ), -1 );
	EXPECT_EQ( abi.GetString( id, 0, buf, 0 ), -1 );
	EXPECT_EQ( abi.GetString( id, 0, buf, -1 ), -1 );
}

//-----------------------------------------------------------------------------
// THE ENGINE'S CREATION WINDOW, which is why the managed slot exists at all.
//
// The real container refuses a create outside
// IServerGameDLL::CreateNetworkStringTables. What matters here is that the
// refusal comes back as -1 with an explanation, rather than as a table pointer
// nobody checked.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, AnEngineRefusalToCreateIsReportedAndNotReturnedAsAnId )
{
	CShellStringTables abi = Abi();
	m_Container.m_bAllowCreation = false;

	EXPECT_EQ( abi.Create( "TooLate", 8 ), INVALID_STRING_TABLE );

	EXPECT_NE( Warnings().find( "TooLate" ), std::string::npos )
		<< "the engine refused the create and the caller got a bare -1. The "
		<< "reason is a timing one -- tables may only be made inside "
		<< "CreateNetworkStringTables -- and nothing else in the log would say "
		<< "so.";
	EXPECT_NE( Warnings().find( "CreateNetworkStringTables" ), std::string::npos )
		<< "the warning does not name the one place a table can be created, "
		<< "which is the only actionable thing it could say";
}

TEST_F( StringTableAbi, WithNoContainerEveryOperationIsRefused )
{
	CShellStringTables abi = AbiWithNoContainer();

	EXPECT_EQ( abi.Create( "T", 8 ), INVALID_STRING_TABLE );
	EXPECT_EQ( abi.Find( "T" ), INVALID_STRING_TABLE );
	EXPECT_EQ( abi.AddString( 0, "x" ), -1 );
	EXPECT_EQ( abi.FindStringIndex( 0, "x" ), -1 );
	EXPECT_EQ( abi.GetNumStrings( 0 ), -1 );

	char buf[ 8 ];
	EXPECT_EQ( abi.GetString( 0, 0, buf, sizeof( buf ) ), -1 );
}

//-----------------------------------------------------------------------------
// THE ADD IS THE SERVER'S. bIsServer is not a parameter on this ABI and must
// not become one: these slots are filled inside #ifndef CLIENT_DLL, and a
// client-side add to a server table is the desync string tables exist to
// prevent.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, EveryAddIsAServerAdd )
{
	CShellStringTables abi = Abi();

	const int id = abi.Create( "T", 8 );
	abi.AddString( id, "x" );

	FakeTable *pTable = static_cast<FakeTable *>( m_Container.GetTable( id ) );
	ASSERT_TRUE( pTable != NULL );

	EXPECT_TRUE( pTable->m_bLastAddWasServer )
		<< "the add crossed as a CLIENT add. On a listen server both DLLs are "
		<< "in one process and the wrong flag there is a table entry the client "
		<< "invents for itself, which is the exact disagreement a networked "
		<< "string table exists to remove.";
}

//-----------------------------------------------------------------------------
// NO STATE, told from a global the only way a running test can tell them apart:
// two objects, one used, the other still answering for itself.
//
// The project rule is that state lives in a class as a field; the stronger
// statement here is that there is NO state, because the engine remembers the
// tables. A size is the one thing a cache cannot hide from.
//-----------------------------------------------------------------------------
TEST_F( StringTableAbi, TheObjectRemembersNothingBetweenCalls )
{
	EXPECT_EQ( sizeof( CShellStringTables ), sizeof( void * ) )
		<< "CShellStringTables is bigger than the one container pointer it is "
		<< "built from, so it is holding something. A cached table id or a "
		<< "cached table POINTER is exactly what goes stale across a level "
		<< "change, and the six entry points build one of these per call.";
}

TEST_F( StringTableAbi, ASecondObjectOverTheSameContainerSeesTheSameTables )
{
	const int id = Abi().Create( "Shared", 8 );
	ASSERT_GE( id, 0 );

	EXPECT_EQ( Abi().AddString( id, "written by another instance" ), 0 );
	EXPECT_EQ( Abi().GetNumStrings( id ), 1 );
	EXPECT_EQ( Abi().Find( "Shared" ), id );

	char buf[ 64 ];
	EXPECT_EQ( Abi().GetString( id, 0, buf, sizeof( buf ) ), 27 );
	EXPECT_STREQ( buf, "written by another instance" );
}
