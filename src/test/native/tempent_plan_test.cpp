//========= SourceSharp — tests for the temp entity PROP PLAN ================//
//
// plan_tmp.md Part 9, gate 2, the client half.
//
// WHAT THIS EXISTS TO CATCH, and why it is a header rather than a comment in
// two .cpp files. Until C-Phase 1 there was ONE builder walking a description
// into props, on the server, so the expansion rule was true by construction:
// the leading "baseclass" prop, one field becoming m_vecOrigin[0..2], the
// negated offset SENDINFO_VECTORELEM declares with, the array's
// element-template-then-DPT_Array pair. A second builder on the client gives
// that rule two implementations, and generic_nettable.h's NetTierInfo already
// records what happens next: a rule with two implementations drifts.
//
// The symptom is the worst one in this port. A client whose prop list differs
// from the server's by one NAME, one ORDER, one OFFSET or one WIDTH begins
// joining and stops, with nothing in any log — the props are paired by string
// at connect and a mismatch is a bitstream that stops making sense partway
// through.
//
// So SourceSharpTe_PlanProps decides it once for both sides, engine-free, and
// this is what checks it. Nothing here links dt_send.cpp or dt_recv.cpp; the
// half that needs real props is cs_sv_te_dump and cs_cl_te_dump, which read the
// ENGINE's own view of the built tables rather than recomputing this one.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/tempent_schema.h"

#include <string.h>

namespace
{

//-----------------------------------------------------------------------------
// A field, spelled the way a table row reads. The same helper
// tempent_schema_test.cpp uses, and deliberately a copy rather than a shared
// one: a fixture two suites can change from either side is a fixture neither
// owns.
//-----------------------------------------------------------------------------
SourceSharpTeField_t Field( const char *pName, int nKind, int nOffset, int nSize,
                            int nBits, int nFlags,
                            int nBitsFrom = SOURCESHARP_TE_BITS_LITERAL,
                            int nElems = 0, int nElemStride = 0 )
{
	SourceSharpTeField_t f;
	memset( &f, 0, sizeof( f ) );

	f.pName       = pName;
	f.nKind       = nKind;
	f.nOffset     = nOffset;
	f.nSizeBytes  = nSize;
	f.nBits       = nBits;
	f.nBitsFrom   = nBitsFrom;
	f.nFlags      = nFlags;
	f.nElems      = nElems;
	f.nElemStride = nElemStride;

	return f;
}

SourceSharpTeClass_t Class( const char *pClassName, const char *pTableName,
                            const char *pBaseTable, int nBlobBytes,
                            const SourceSharpTeField_t *pFields, int nFieldCount )
{
	SourceSharpTeClass_t c;
	memset( &c, 0, sizeof( c ) );

	c.pClassName     = pClassName;
	c.pTableName     = pTableName;
	c.pBaseTable     = pBaseTable;
	c.nBlobBytes     = nBlobBytes;
	c.nRegisterClass = 1;
	c.pFields        = pFields;
	c.nFieldCount    = nFieldCount;

	return c;
}

//=============================================================================//
// THE NAME.
//
// A prop is paired across the wire BY STRING, so this derivation is the one
// thing the two sides must spell identically. It is here rather than in either
// builder for exactly that reason.
//
// ONE EXCEPTION, AND IT IS STOCK'S: an array's element TEMPLATE is spelt
// `m_vecPoints` by the sender and `m_vecPoints[0]` by the receiver, because
// SENDINFO_ARRAY stringises the array and RECVINFO is handed the first element.
// SourceSharpTe_RecvPropName is that exception and
// TheArrayTemplateIsNamedForItsFirstElementOnTheRecvSideOnly is what pins it.
//=============================================================================//

TEST( TempEntPropName, ExpandsTheWayStocksOwnTablesSpellIt )
{
	char szName[ SOURCESHARP_TE_PROP_NAME_MAX ];

	// SENDINFO_VECTORELEM( m_vecOrigin, 1 ) stringises to "m_vecOrigin[1]"
	// (dt_send.h:631), and RECVINFO( m_vecOrigin[1] ) to the same. Both sides
	// have to arrive here.
	SourceSharpTe_PropName( szName, sizeof( szName ), "m_vecOrigin", 0, 1 );
	EXPECT_STREQ( "m_vecOrigin[0]", szName );

	SourceSharpTe_PropName( szName, sizeof( szName ), "m_vecOrigin", 2, 1 );
	EXPECT_STREQ( "m_vecOrigin[2]", szName );

	// Not expanded: the element index is ignored rather than appended, because
	// a single-prop field is named exactly as stock's SENDINFO( m_nMagnitude )
	// names it.
	SourceSharpTe_PropName( szName, sizeof( szName ), "m_nMagnitude", 0, 0 );
	EXPECT_STREQ( "m_nMagnitude", szName );

	SourceSharpTe_PropName( szName, sizeof( szName ), "m_nMagnitude", 7, 0 );
	EXPECT_STREQ( "m_nMagnitude", szName );
}

//-----------------------------------------------------------------------------
// TWO DIGITS, because there is a table with sixteen of something. Written
// without snprintf on purpose — this header is included by a gtest that links
// nothing — so the digit loop is code rather than a library call and is checked
// like code.
//-----------------------------------------------------------------------------
TEST( TempEntPropName, CountsPastNineTheRightWayRound )
{
	char szName[ SOURCESHARP_TE_PROP_NAME_MAX ];

	SourceSharpTe_PropName( szName, sizeof( szName ), "m_vecPoints", 10, 1 );
	EXPECT_STREQ( "m_vecPoints[10]", szName );

	SourceSharpTe_PropName( szName, sizeof( szName ), "m_vecPoints", 15, 1 );
	EXPECT_STREQ( "m_vecPoints[15]", szName );

	// The digits are emitted least-significant-first into a scratch buffer and
	// reversed on the way out. A reversal that went the wrong way would give
	// "[01]", which pairs against nothing and produces no error anywhere.
	SourceSharpTe_PropName( szName, sizeof( szName ), "x", 12, 1 );
	EXPECT_STREQ( "x[12]", szName );
}

TEST( TempEntPropName, TruncatesRatherThanRunningOffTheEnd )
{
	char szName[ 8 ];

	memset( szName, 0x7F, sizeof( szName ) );
	SourceSharpTe_PropName( szName, sizeof( szName ), "m_vecOriginTooLong", 1, 1 );

	EXPECT_EQ( '\0', szName[7] ) << "always terminated";
	EXPECT_EQ( 7u, strlen( szName ) );
	EXPECT_STREQ( "m_vecOr", szName );

	// A zero-length buffer writes nothing at all rather than a terminator into
	// somebody else's byte.
	char szGuard[2] = { 0x7F, 0x7F };
	SourceSharpTe_PropName( szGuard, 0, "anything", 0, 0 );
	EXPECT_EQ( 0x7F, szGuard[0] );
}

//=============================================================================//
// THE BASECLASS PROP IS TOLD APART FROM AN ORDINARY NESTED TABLE.
//
// iField is -1 for the leading "baseclass" prop and the field index for
// everything else, and BOTH BUILDERS NOW KEY A CRASH GUARD ON THAT
// DISTINCTION. dt_send.cpp:648 marks any identity-proxied datatable prop at
// offset ZERO as SPROP_COLLAPSIBLE, and the receiver applies a collapsed table
// at the OBJECT BASE -- which for a native shell is its vtable pointer, and for
// a blob is the outer table's own first field. Lane S4 found it with
// DT_EffectData: a clean join and a segfault on the first dispatch.
//
// "baseclass" is at offset zero ON PURPOSE and must stay exempt: a base's props
// address the same blob from the same place, and stock sets the same flag on
// its own. So the guard is "a nested table at offset zero that is NOT the
// baseclass", and if iField ever stopped distinguishing them the guard would
// either fire on every table or on none -- both silent.
//=============================================================================//

TEST( TempEntPlan, TheBaseclassPropIsTheOnlyOneThatMayLiveAtOffsetZero )
{
	SourceSharpTeField_t nested =
		Field( "m_DataEntry", SOURCESHARP_TE_PROP_DATATABLE, 4, 0, 0, 0 );
	nested.pSubTable = "DT_EffectData";

	const SourceSharpTeClass_t desc =
		Class( "CTEEffectDispatch", "DT_TEEffectDispatch", "DT_BaseTempEntity", 8, &nested, 1 );

	SourceSharpTePropPlan_t plan[ 8 ];
	ASSERT_EQ( 2, SourceSharpTe_PlanProps( &desc, plan, 8 ) );

	// The base: offset zero, and iField says it belongs to no described field.
	EXPECT_STREQ( "baseclass", plan[0].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_DATATABLE, plan[0].nKind );
	EXPECT_EQ( 0, plan[0].nOffset );
	EXPECT_EQ( -1, plan[0].iField ) << "which is what exempts it from the guard";

	// An ordinary nested table: its DECLARED offset, carried through, and an
	// iField the guard can see.
	EXPECT_STREQ( "m_DataEntry", plan[1].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_DATATABLE, plan[1].nKind );
	EXPECT_EQ( 4, plan[1].nOffset ) << "the pad TeClassBuilder puts in front of it";
	EXPECT_EQ( 4, plan[1].nDeclareOffset ) << "and the send side declares the same";
	EXPECT_STREQ( "DT_EffectData", plan[1].pSubTable );
	EXPECT_EQ( 0, plan[1].iField ) << "which is what SUBJECTS it to the guard";

	// AND THE OFFSET IS NOT INVENTED HERE. A nested table declared at zero comes
	// out at zero -- the plan reports what the description says and the builders
	// refuse it, rather than the plan quietly moving it and hiding the fault
	// from both of them.
	SourceSharpTeField_t atZero = nested;
	atZero.nOffset = 0;

	const SourceSharpTeClass_t bad =
		Class( "CTEBad", "DT_TEBad", NULL, 8, &atZero, 1 );

	ASSERT_EQ( 1, SourceSharpTe_PlanProps( &bad, plan, 8 ) );
	EXPECT_EQ( 0, plan[0].nOffset );
	EXPECT_EQ( 0, plan[0].iField ) << "a nested table at zero, and the guard can see it is one";
}

//=============================================================================//
// THE COUNT.
//
// Both sides allocate their prop array from this before planning into it. A
// short array is a buffer overrun into whatever the allocator put next, and
// every prop past the end still builds.
//=============================================================================//

TEST( TempEntPlanCount, CountsTheBaseAndTheExpansionsAndTheArrayPair )
{
	// DT_TEParticleSystem: a base, and one field that is three props.
	SourceSharpTeField_t origin =
		Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
		       SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM,
		       SOURCESHARP_TE_BITS_LITERAL, 3, 4 );

	const SourceSharpTeClass_t particles =
		Class( "CTEParticleSystem", "DT_TEParticleSystem", "DT_BaseTempEntity", 12, &origin, 1 );

	EXPECT_EQ( 4, SourceSharpTe_PlanCount( &particles ) );

	// DT_BaseTempEntity: NOBASE and no fields at all, which is a real table in
	// the inventory and not a degenerate case.
	const SourceSharpTeClass_t empty =
		Class( "CBaseTempEntity", "DT_BaseTempEntity", NULL, 0, NULL, 0 );

	EXPECT_EQ( 0, SourceSharpTe_PlanCount( &empty ) );

	// DT_TEBeamSpline: an int and an ARRAY, which is a PAIR and not sixteen
	// props (plan 3.3).
	SourceSharpTeField_t spline[2];
	spline[0] = Field( "m_nPoints", SOURCESHARP_TE_PROP_INT, 0, 4, 5, SOURCESHARP_TE_FLAG_UNSIGNED );
	spline[1] = Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 4, 0, -1,
	                   SOURCESHARP_TE_FLAG_COORD, SOURCESHARP_TE_BITS_LITERAL, 16, 12 );

	const SourceSharpTeClass_t beam =
		Class( "CTEBeamSpline", "DT_TEBeamSpline", NULL, 4 + 16 * 12, spline, 2 );

	EXPECT_EQ( 3, SourceSharpTe_PlanCount( &beam ) );

	EXPECT_EQ( -1, SourceSharpTe_PlanCount( NULL ) );
}

//=============================================================================//
// THE PLAN ITSELF.
//=============================================================================//

//-----------------------------------------------------------------------------
// DT_TEParticleSystem, which is the shape every VECTORELEM table has and the
// one the live join measured (te S1's gate 3 record).
//
// THE OFFSETS ARE ASSERTED IN BOTH FORMS, and that is the point of this test.
// nOffset is what the CLIENT hands RecvPropFloat and nDeclareOffset is what the
// SERVER hands SendPropFloat, and they differ by a sign for exactly the props
// SENDINFO_VECTORELEM covers. A client that declared with the send form would
// decode into memory in FRONT of the blob, which is a plausible number and no
// error.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, AVectorElemFieldIsThreePropsWithNegatedSendOffsets )
{
	SourceSharpTeField_t origin =
		Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
		       SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM,
		       SOURCESHARP_TE_BITS_LITERAL, 3, 4 );

	const SourceSharpTeClass_t particles =
		Class( "CTEParticleSystem", "DT_TEParticleSystem", "DT_BaseTempEntity", 12, &origin, 1 );

	SourceSharpTePropPlan_t plan[ 8 ];
	ASSERT_EQ( 4, SourceSharpTe_PlanProps( &particles, plan, 8 ) );

	// THE BASE GOES FIRST, which is where BEGIN_SEND_TABLE and BEGIN_RECV_TABLE
	// both put it, and it is named "baseclass" on both sides.
	EXPECT_STREQ( "baseclass", plan[0].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_DATATABLE, plan[0].nKind );
	EXPECT_EQ( 0, plan[0].nOffset );
	EXPECT_EQ( 0, plan[0].nDeclareOffset );
	EXPECT_STREQ( "DT_BaseTempEntity", plan[0].pSubTable );
	EXPECT_EQ( -1, plan[0].iField ) << "the base belongs to no described field";

	EXPECT_STREQ( "m_vecOrigin[0]", plan[1].szName );
	EXPECT_STREQ( "m_vecOrigin[1]", plan[2].szName );
	EXPECT_STREQ( "m_vecOrigin[2]", plan[3].szName );

	for ( int i = 1; i <= 3; i++ )
	{
		EXPECT_EQ( SOURCESHARP_TE_PROP_FLOAT, plan[i].nKind );
		EXPECT_EQ( 4 * ( i - 1 ), plan[i].nOffset ) << "receive offset, always positive";
		EXPECT_EQ( -4 * ( i - 1 ), plan[i].nDeclareOffset ) << "send offset, negated";
		EXPECT_EQ( 4, plan[i].nSizeBytes );
		EXPECT_EQ( -1, plan[i].nBits ) << "the flag decides the width, not a number";
		EXPECT_EQ( 0, plan[i].iField );
		EXPECT_EQ( i - 1, plan[i].iElem );

		// THE INSTRUCTION IS STRIPPED. SOURCESHARP_TE_FLAG_VECTOR_ELEM tells
		// this code to negate; it is SPROP_IS_A_VECTOR_ELEM's bit and the engine
		// sets that itself in SendTable_InitTable. Passing it through would tell
		// the engine about a prop that is not one.
		EXPECT_EQ( SOURCESHARP_TE_FLAG_COORD, plan[i].nFlags );
	}

	// THE DOCUMENTED HOLE, asserted rather than left to be discovered: -0 is 0,
	// so element 0 goes in without a negative and the engine sets no flag on it.
	// Stock has the same hole and never falls into it, because a
	// CBaseTempEntity's vtable pointer is at offset 0 and a member never is. No
	// decode path reads the flag.
	EXPECT_EQ( plan[1].nOffset, plan[1].nDeclareOffset );
	EXPECT_NE( plan[2].nOffset, plan[2].nDeclareOffset );
}

//-----------------------------------------------------------------------------
// DT_TESparks, whole. THIS IS THE TABLE THE TWO DUMPS PRINT, so the numbers
// here are the numbers a `grep '^TE|'` diff compares.
//
// The offsets are the ones te S1's live join measured through the engine
// (0/4/8 for the base's origin, then 12, 16, 20) — arrived at there by
// SendTable_InitTable and here by arithmetic with no engine in it.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, SparksPlansTheSixPropsTheJoinMeasured )
{
	SourceSharpTeField_t sparks[3];
	sparks[0] = Field( "m_nMagnitude",   SOURCESHARP_TE_PROP_INT, 12, 4, 4,
	                   SOURCESHARP_TE_FLAG_UNSIGNED );
	sparks[1] = Field( "m_nTrailLength", SOURCESHARP_TE_PROP_INT, 16, 4, 4,
	                   SOURCESHARP_TE_FLAG_UNSIGNED );
	sparks[2] = Field( "m_vecDir",       SOURCESHARP_TE_PROP_VECTOR, 20, 12, -1,
	                   SOURCESHARP_TE_FLAG_COORD );

	const SourceSharpTeClass_t desc =
		Class( "CTESparks", "DT_TESparks", "DT_TEParticleSystem", 32, sparks, 3 );

	SourceSharpTePropPlan_t plan[ 8 ];
	ASSERT_EQ( 4, SourceSharpTe_PlanProps( &desc, plan, 8 ) );

	EXPECT_STREQ( "baseclass", plan[0].szName );
	EXPECT_STREQ( "DT_TEParticleSystem", plan[0].pSubTable );

	EXPECT_STREQ( "m_nMagnitude", plan[1].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_INT, plan[1].nKind );
	EXPECT_EQ( 12, plan[1].nOffset );
	EXPECT_EQ( 12, plan[1].nDeclareOffset );
	EXPECT_EQ( 4, plan[1].nBits );
	EXPECT_EQ( 4, plan[1].nSizeBytes );
	EXPECT_EQ( SOURCESHARP_TE_FLAG_UNSIGNED, plan[1].nFlags );

	EXPECT_STREQ( "m_nTrailLength", plan[2].szName );
	EXPECT_EQ( 16, plan[2].nOffset );
	EXPECT_EQ( 4, plan[2].nBits );

	EXPECT_STREQ( "m_vecDir", plan[3].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_VECTOR, plan[3].nKind );
	EXPECT_EQ( 20, plan[3].nOffset );
	EXPECT_EQ( 20, plan[3].nDeclareOffset ) << "not a vector elem, so not negated";
	EXPECT_EQ( SOURCESHARP_TE_FLAG_COORD, plan[3].nFlags );
	EXPECT_EQ( -1, plan[3].nBits );
}

//-----------------------------------------------------------------------------
// THE ONE ARRAY (plan 3.3). Two props of ONE NAME: the element template, then
// the DPT_Array carrying the count and the stride. m_pArrayProp is left NULL on
// both sides and the engine links it to the PRECEDING prop, so the order of the
// pair is load-bearing rather than cosmetic — a DPT_Array emitted first would
// link to whatever came before it.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, AnArrayIsATemplateThenTheArrayPropOfTheSameName )
{
	SourceSharpTeField_t spline[2];
	spline[0] = Field( "m_nPoints", SOURCESHARP_TE_PROP_INT, 0, 4, 5, SOURCESHARP_TE_FLAG_UNSIGNED );
	spline[1] = Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 4, 12, -1,
	                   SOURCESHARP_TE_FLAG_COORD, SOURCESHARP_TE_BITS_LITERAL, 16, 12 );

	const SourceSharpTeClass_t beam =
		Class( "CTEBeamSpline", "DT_TEBeamSpline", NULL, 4 + 16 * 12, spline, 2 );

	SourceSharpTePropPlan_t plan[ 8 ];
	ASSERT_EQ( 3, SourceSharpTe_PlanProps( &beam, plan, 8 ) );

	EXPECT_STREQ( "m_nPoints", plan[0].szName );

	// The TEMPLATE first, and it is a VECTOR: there is exactly one array in all
	// 44 tables and its elements are vectors, which SourceSharpTe_PropValueBits
	// prices on the same assumption.
	EXPECT_STREQ( "m_vecPoints", plan[1].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_VECTOR, plan[1].nKind );
	EXPECT_EQ( 4, plan[1].nOffset );
	EXPECT_EQ( SOURCESHARP_TE_FLAG_COORD, plan[1].nFlags );

	// Then the array, OF THE SAME NAME.
	EXPECT_STREQ( "m_vecPoints", plan[2].szName );
	EXPECT_EQ( SOURCESHARP_TE_PROP_ARRAY, plan[2].nKind );
	EXPECT_EQ( 16, plan[2].nElems );
	EXPECT_EQ( 12, plan[2].nElemStride );
	EXPECT_EQ( 4, plan[2].nOffset );

	// AND THE TEMPLATE IS MARKED, because the recv side names it differently.
	EXPECT_TRUE( plan[1].bArrayElement != 0 );
	EXPECT_TRUE( plan[2].bArrayElement == 0 );
	EXPECT_TRUE( plan[0].bArrayElement == 0 );
}

//-----------------------------------------------------------------------------
// THE ONE PROP THE TWO SIDES DO NOT NAME THE SAME, and it refuses a join rather
// than degrading an effect.
//
// Stock declares the array's element template as
//
//   server  SendPropVector( SENDINFO_ARRAY(m_vecPoints), -1, SPROP_COORD )   -> "m_vecPoints"
//   client  RecvPropVector( RECVINFO(m_vecPoints[0]) )                       -> "m_vecPoints[0]"
//
// SENDINFO_ARRAY stringises the ARRAY (dt_send.h:622); RECVINFO stringises
// whatever it is handed, and stock hands it the first ELEMENT. Only the
// DPT_Array prop that follows is spelt the same on both sides, because both
// macros spell it #arrayName.
//
// WHAT GETTING IT WRONG DOES, measured live by lane CN on the first managed
// client class with an array in it: the recv side resolves a server prop to a
// client prop BY NAME, finds the DPT_Vector template where it wanted the
// DPT_Array prop, and the client prints
//
//   RecvProp type doesn't match server type for DT_TEBeamSpline/m_vecPoints
//   Host_EndGame: CL_ParseClassInfo_EndClasses: CreateDecoders failed.
//
// and drops. Not a missing effect -- a refused connection. The SEND side never
// looks a prop up by name, which is why the bare template name is correct there
// and why the server half of this port was green with it from S-Phase 1.
//
// This test is the thing that would notice a "tidy-up" that made the two names
// agree, since agreeing is what looks tidy and is what breaks it.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, TheArrayTemplateIsNamedForItsFirstElementOnTheRecvSideOnly )
{
	SourceSharpTeField_t spline[2];
	spline[0] = Field( "m_nPoints", SOURCESHARP_TE_PROP_INT, 0, 4, 5, SOURCESHARP_TE_FLAG_UNSIGNED );
	spline[1] = Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 4, 12, -1,
	                   SOURCESHARP_TE_FLAG_COORD, SOURCESHARP_TE_BITS_LITERAL, 16, 12 );

	const SourceSharpTeClass_t beam =
		Class( "CTEBeamSpline", "DT_TEBeamSpline", NULL, 4 + 16 * 12, spline, 2 );

	SourceSharpTePropPlan_t plan[ 8 ];
	ASSERT_EQ( 3, SourceSharpTe_PlanProps( &beam, plan, 8 ) );

	char szRecv[ SOURCESHARP_TE_PROP_NAME_MAX ];

	// An ordinary prop is spelt the same on both sides.
	SourceSharpTe_RecvPropName( szRecv, (int)sizeof( szRecv ), &plan[0] );
	EXPECT_STREQ( "m_nPoints", szRecv );
	EXPECT_STREQ( plan[0].szName, szRecv );

	// The TEMPLATE is not. RECVINFO(m_vecPoints[0]).
	SourceSharpTe_RecvPropName( szRecv, (int)sizeof( szRecv ), &plan[1] );
	EXPECT_STREQ( "m_vecPoints[0]", szRecv );
	EXPECT_STRNE( plan[1].szName, szRecv );

	// The DPT_Array prop IS -- both macros spell it #arrayName, and this is the
	// name the recv side's lookup has to resolve to a DPT_Array.
	SourceSharpTe_RecvPropName( szRecv, (int)sizeof( szRecv ), &plan[2] );
	EXPECT_STREQ( "m_vecPoints", szRecv );
	EXPECT_STREQ( plan[2].szName, szRecv );

	// AND THE TWO CLIENT NAMES DIFFER, which is the whole property: two props of
	// one name on the recv side is what CreateDecoders refuses.
	char szTemplate[ SOURCESHARP_TE_PROP_NAME_MAX ];
	char szArray[ SOURCESHARP_TE_PROP_NAME_MAX ];

	SourceSharpTe_RecvPropName( szTemplate, (int)sizeof( szTemplate ), &plan[1] );
	SourceSharpTe_RecvPropName( szArray,    (int)sizeof( szArray ),    &plan[2] );

	EXPECT_STRNE( szTemplate, szArray );
}

//-----------------------------------------------------------------------------
// Every OTHER planned prop in a table with no array is spelt identically on the
// two sides, so the rule above is one exception and not a licence.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, EveryPropThatIsNotAnArrayTemplateIsSpeltTheSameOnBothSides )
{
	SourceSharpTeField_t sparks[3];
	sparks[0] = Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
	                   SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM,
	                   SOURCESHARP_TE_BITS_LITERAL, 3, 4 );
	sparks[1] = Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 12, 4, 4,
	                   SOURCESHARP_TE_FLAG_UNSIGNED );
	sparks[2] = Field( "m_vecDir", SOURCESHARP_TE_PROP_VECTOR, 16, 12, -1,
	                   SOURCESHARP_TE_FLAG_COORD );

	const SourceSharpTeClass_t te =
		Class( "CTESparks", "DT_TESparks", NULL, 28, sparks, 3 );

	SourceSharpTePropPlan_t plan[ 16 ];
	const int nPlanned = SourceSharpTe_PlanProps( &te, plan, 16 );

	ASSERT_GT( nPlanned, 0 );

	int nChecked = 0;

	for ( int i = 0; i < nPlanned; i++ )
	{
		char szRecv[ SOURCESHARP_TE_PROP_NAME_MAX ];

		SourceSharpTe_RecvPropName( szRecv, (int)sizeof( szRecv ), &plan[i] );

		EXPECT_EQ( 0, plan[i].bArrayElement );
		EXPECT_STREQ( plan[i].szName, szRecv );
		nChecked++;
	}

	// The sweep looked at something. A table that planned no props would pass
	// the loop above by never entering it.
	EXPECT_EQ( nPlanned, nChecked );
	EXPECT_GE( nChecked, 5 );
}

//-----------------------------------------------------------------------------
// A SYMBOLIC WIDTH IS RESOLVED INTO THE PLAN, so neither builder ever sees a
// symbol and neither can resolve one differently. Nine of these exist and every
// one of them is a width stock spells with a #define (plan 3.1).
//-----------------------------------------------------------------------------
TEST( TempEntPlan, SymbolicWidthsAreResolvedBeforeEitherSideSeesThem )
{
	SourceSharpTeField_t entity =
		Field( "m_nEntity", SOURCESHARP_TE_PROP_INT, 0, 4, 0, SOURCESHARP_TE_FLAG_UNSIGNED,
		       SOURCESHARP_TE_BITS_MAX_EDICT );

	const SourceSharpTeClass_t desc =
		Class( "CTEFizz", "DT_TEFizz", NULL, 4, &entity, 1 );

	SourceSharpTePropPlan_t plan[ 4 ];
	ASSERT_EQ( 1, SourceSharpTe_PlanProps( &desc, plan, 4 ) );

	EXPECT_EQ( 11, plan[0].nBits ) << "MAX_EDICT_BITS, not the literal 0 beside it";
}

//-----------------------------------------------------------------------------
// AND A SYMBOL THIS BUILD DOES NOT KNOW IS REFUSED, never substituted. A width
// nobody chose is exactly the failure the symbols exist to prevent, and a
// plausible one is worse than none: it builds, it registers, and the bitstream
// stops making sense partway through the first event.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, AnUnknownWidthSymbolIsRefusedRatherThanGuessed )
{
	SourceSharpTeField_t bad =
		Field( "m_nWhat", SOURCESHARP_TE_PROP_INT, 0, 4, 8, 0, SOURCESHARP_TE_BITS_COUNT );

	const SourceSharpTeClass_t desc = Class( "CTEBad", "DT_TEBad", NULL, 4, &bad, 1 );

	SourceSharpTePropPlan_t plan[ 4 ];
	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &desc, plan, 4 ) );
}

TEST( TempEntPlan, AFieldWithNoNameIsRefused )
{
	SourceSharpTeField_t nameless = Field( "", SOURCESHARP_TE_PROP_INT, 0, 4, 8, 0 );
	const SourceSharpTeClass_t desc = Class( "CTEBad", "DT_TEBad", NULL, 4, &nameless, 1 );

	SourceSharpTePropPlan_t plan[ 4 ];
	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &desc, plan, 4 ) );
}

//-----------------------------------------------------------------------------
// A DATATABLE FIELD WITH NO TABLE NAMED is refused, because the prop it would
// build nests nothing and the client cannot resolve it.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, ANestedTableWithNoNameIsRefused )
{
	SourceSharpTeField_t nested =
		Field( "m_Data", SOURCESHARP_TE_PROP_DATATABLE, 0, 0, 0, 0 );

	const SourceSharpTeClass_t desc = Class( "CTEBad", "DT_TEBad", NULL, 4, &nested, 1 );

	SourceSharpTePropPlan_t plan[ 4 ];
	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &desc, plan, 4 ) );
}

//-----------------------------------------------------------------------------
// A SHORT BUFFER IS REFUSED, not filled to the brim.
//
// This is the check that pays for SourceSharpTe_PlanCount existing at all: both
// builders allocate from the count and plan into it, and a plan that wrote one
// prop past a caller's array would be a buffer overrun into whatever the
// allocator put next — with every prop past the end still building.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, RefusesRatherThanWritingPastTheCallersArray )
{
	SourceSharpTeField_t origin =
		Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
		       SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM,
		       SOURCESHARP_TE_BITS_LITERAL, 3, 4 );

	const SourceSharpTeClass_t particles =
		Class( "CTEParticleSystem", "DT_TEParticleSystem", "DT_BaseTempEntity", 12, &origin, 1 );

	SourceSharpTePropPlan_t plan[ 8 ];

	// Four wanted. Three, two, one and none all refuse; four succeeds.
	EXPECT_EQ( 4, SourceSharpTe_PlanCount( &particles ) );
	EXPECT_EQ(  4, SourceSharpTe_PlanProps( &particles, plan, 4 ) );
	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &particles, plan, 3 ) );
	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &particles, plan, 1 ) );
	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &particles, plan, 0 ) );

	// And the array's PAIR is refused as a pair rather than half-written.
	SourceSharpTeField_t spline =
		Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 0, 12, -1,
		       SOURCESHARP_TE_FLAG_COORD, SOURCESHARP_TE_BITS_LITERAL, 16, 12 );

	const SourceSharpTeClass_t beam =
		Class( "CTEBeamSpline", "DT_TEBeamSpline", NULL, 16 * 12, &spline, 1 );

	EXPECT_EQ( -1, SourceSharpTe_PlanProps( &beam, plan, 1 ) );
	EXPECT_EQ(  2, SourceSharpTe_PlanProps( &beam, plan, 2 ) );
}

//-----------------------------------------------------------------------------
// THE INVARIANT THE CLIENT BUILDER RESTS ON, stated as a rule rather than as a
// list of offsets: every plan entry's RECEIVE offset is positive, and its SEND
// offset is that number or its negation and nothing else.
//
// Selected by ROLE and not by value (docs/writing-tests.md's "the check that
// cannot fail"): the loop walks EVERY entry rather than the ones that look like
// vector elements, so a new kind that forgot to fill nOffset fails here instead
// of being skipped by the filter that was supposed to catch it.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, EveryReceiveOffsetIsPositiveAndEverySendOffsetIsItOrItsNegation )
{
	SourceSharpTeField_t fields[4];
	fields[0] = Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
	                   SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM,
	                   SOURCESHARP_TE_BITS_LITERAL, 3, 4 );
	fields[1] = Field( "m_clrColour", SOURCESHARP_TE_PROP_INT, 12, 1, 8,
	                   SOURCESHARP_TE_FLAG_UNSIGNED, SOURCESHARP_TE_BITS_LITERAL, 3, 1 );
	fields[2] = Field( "m_angRotation", SOURCESHARP_TE_PROP_ANGLE, 16, 4, 8, 0 );
	fields[3] = Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 20, 12, -1,
	                   SOURCESHARP_TE_FLAG_COORD, SOURCESHARP_TE_BITS_LITERAL, 4, 12 );

	const SourceSharpTeClass_t desc =
		Class( "CTEEverything", "DT_TEEverything", "DT_BaseTempEntity", 20 + 4 * 12, fields, 4 );

	SourceSharpTePropPlan_t plan[ 16 ];
	const int nPlanned = SourceSharpTe_PlanProps( &desc, plan, 16 );

	// 1 base + 3 origin + 3 colour + 1 angle + 2 array
	ASSERT_EQ( 10, nPlanned );

	for ( int i = 0; i < nPlanned; i++ )
	{
		EXPECT_GE( plan[i].nOffset, 0 )
			<< "prop " << i << " (" << plan[i].szName << ") is what RecvProp is given";

		EXPECT_TRUE( plan[i].nDeclareOffset == plan[i].nOffset
		          || plan[i].nDeclareOffset == -plan[i].nOffset )
			<< "prop " << i << " (" << plan[i].szName << ") sends at "
			<< plan[i].nDeclareOffset << " and receives at " << plan[i].nOffset;

		EXPECT_TRUE( plan[i].szName[0] != '\0' ) << "prop " << i << " is named";
	}

	// SENDINFO_ARRAYELEM does NOT negate — only SENDINFO_VECTORELEM does — so
	// the three colour bytes are declared positive on both sides. The two
	// expansions share one mechanism and are told apart by the flag, and this is
	// where that distinction is checked rather than assumed.
	EXPECT_STREQ( "m_clrColour[0]", plan[4].szName );
	EXPECT_EQ( 12, plan[4].nOffset );
	EXPECT_EQ( 12, plan[4].nDeclareOffset );
	EXPECT_STREQ( "m_clrColour[2]", plan[6].szName );
	EXPECT_EQ( 14, plan[6].nOffset );
	EXPECT_EQ( 14, plan[6].nDeclareOffset );
}

//-----------------------------------------------------------------------------
// THE COUNT AND THE PLAN AGREE, which is what makes the count safe to allocate
// from. Two functions walking one description would be a rule with two
// implementations all over again if nothing compared them.
//-----------------------------------------------------------------------------
TEST( TempEntPlan, TheCountIsExactlyWhatThePlanWrites )
{
	SourceSharpTeField_t fields[3];
	fields[0] = Field( "m_vecOrigin", SOURCESHARP_TE_PROP_FLOAT, 0, 4, -1,
	                   SOURCESHARP_TE_FLAG_COORD | SOURCESHARP_TE_FLAG_VECTOR_ELEM,
	                   SOURCESHARP_TE_BITS_LITERAL, 3, 4 );
	fields[1] = Field( "m_nMagnitude", SOURCESHARP_TE_PROP_INT, 12, 4, 4,
	                   SOURCESHARP_TE_FLAG_UNSIGNED );
	fields[2] = Field( "m_vecPoints", SOURCESHARP_TE_PROP_ARRAY, 16, 12, -1,
	                   SOURCESHARP_TE_FLAG_COORD, SOURCESHARP_TE_BITS_LITERAL, 8, 12 );

	const char *kBases[3] = { NULL, "DT_BaseTempEntity", "DT_TEParticleSystem" };

	for ( int b = 0; b < 3; b++ )
	{
		for ( int n = 0; n <= 3; n++ )
		{
			const SourceSharpTeClass_t desc =
				Class( "CTEX", "DT_TEX", kBases[b], 16 + 8 * 12, fields, n );

			SourceSharpTePropPlan_t plan[ 32 ];
			const int nCount = SourceSharpTe_PlanCount( &desc );

			EXPECT_EQ( nCount, SourceSharpTe_PlanProps( &desc, plan, nCount ) )
				<< "base " << b << ", " << n << " field(s)";
		}
	}
}

} // namespace
