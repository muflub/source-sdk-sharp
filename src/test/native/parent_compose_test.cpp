//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the golden table for parented composition ==========//
//
// Parenting's arithmetic moved to C# (plan_entity.md step 2h). This is the half
// of the agreement that faces NATIVE: every row of parent_compose_cases.txt is
// recomputed here with the real mathlib calls shell_standalone_hierarchy.cpp
// made — AngleMatrix, MatrixInvert, ConcatTransforms, MatrixAngles — and
// asserted against what the file says.
//
// WHY THIS EXISTS AND NOT JUST THE C# TEST. A fixture that stands in for native
// drifts silently: the managed test would agree with the table, the table would
// agree with nothing, and a wrong number would be checked against itself
// forever. Here the table is pinned to the arithmetic it was taken from — so the
// managed test on the other side of it is a real comparison rather than a
// self-portrait. See docs/writing-tests.md, "A fixture that stands in for native
// must be checked against it".
//
// WHY IT IS NOT template_transform_test's TABLE. That one records VMatrix:
// SetupMatrixOrgAngles, VMatrix::InverseTR, MatrixMultiply, MatrixToAngles.
// Parenting never touched a VMatrix — it composes matrix3x4_t through a
// different set of functions, and "the two sets agree" is exactly the kind of
// thing that is true until it is not. So the parenting table is taken from the
// parenting calls.
//
// THE ROTATIONS ARE THE POINT, and the table says so as well: a composition that
// ignored the parent's rotation entirely still reproduces `translation-only`,
// which is why that row is in the table beside eleven that it fails.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "mathlib/mathlib.h"

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include "mathlib_test_env.h"

namespace
{

struct Row
{
	std::string name;
	float pOrg[3], pAng[3];
	float cOrg[3], cAng[3];
	float nOrg[3], nAng[3];
	float outOrg[3], outAng[3];
};

//-----------------------------------------------------------------------------
// The table, found by walking up from wherever the test binary was run.
//
// Not an absolute path and not a compiled-in copy: a compiled-in copy would be a
// SECOND record of the numbers and the two would part company, which is the
// failure this whole file exists to prevent.
//-----------------------------------------------------------------------------
std::string FindCases()
{
	const char *kRelative = "sourcesharp/test/parent_compose_cases.txt";
	std::string prefix;

	for ( int i = 0; i < 8; i++ )
	{
		std::string candidate = prefix + kRelative;
		if ( FILE *f = fopen( candidate.c_str(), "r" ) )
		{
			fclose( f );
			return candidate;
		}
		prefix += "../";
	}

	return std::string();
}

std::vector< Row > LoadCases()
{
	std::vector< Row > rows;

	const std::string path = FindCases();
	if ( path.empty() )
		return rows;

	FILE *f = fopen( path.c_str(), "r" );
	if ( !f )
		return rows;

	char line[1024];
	while ( fgets( line, sizeof( line ), f ) )
	{
		if ( line[0] == '#' || line[0] == '\n' || line[0] == '\r' )
			continue;

		char name[128];
		Row r;
		int n = sscanf( line,
		                "%127s %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f %f",
		                name,
		                &r.pOrg[0], &r.pOrg[1], &r.pOrg[2], &r.pAng[0], &r.pAng[1], &r.pAng[2],
		                &r.cOrg[0], &r.cOrg[1], &r.cOrg[2], &r.cAng[0], &r.cAng[1], &r.cAng[2],
		                &r.nOrg[0], &r.nOrg[1], &r.nOrg[2], &r.nAng[0], &r.nAng[1], &r.nAng[2],
		                &r.outOrg[0], &r.outOrg[1], &r.outOrg[2],
		                &r.outAng[0], &r.outAng[1], &r.outAng[2] );

		if ( n != 25 )
			continue;

		r.name = name;
		rows.push_back( r );
	}

	fclose( f );
	return rows;
}

//-----------------------------------------------------------------------------
// The two halves of parenting, in the calls the shell made.
//
//   link      childToParent = worldToParent * childToWorld
//   compose   childToWorld  = parentToWorld * childToParent
//
// The offset is measured ONCE, at link time, and every composition afterwards
// reads it rather than deriving it. A version that recomputed it from the
// current absolutes each frame would decay to the identity within a few frames,
// because each composition's output is the next derivation's input — so the two
// halves are written out separately here rather than folded into one product.
//-----------------------------------------------------------------------------
void Place( const Row &r, Vector &vecOut, QAngle &angOut )
{
	matrix3x4_t parentToWorld, worldToParent, childToWorld, childToParent;

	AngleMatrix( QAngle( r.pAng[0], r.pAng[1], r.pAng[2] ),
	             Vector( r.pOrg[0], r.pOrg[1], r.pOrg[2] ), parentToWorld );
	MatrixInvert( parentToWorld, worldToParent );

	AngleMatrix( QAngle( r.cAng[0], r.cAng[1], r.cAng[2] ),
	             Vector( r.cOrg[0], r.cOrg[1], r.cOrg[2] ), childToWorld );

	ConcatTransforms( worldToParent, childToWorld, childToParent );

	matrix3x4_t movedToWorld, composed;

	AngleMatrix( QAngle( r.nAng[0], r.nAng[1], r.nAng[2] ),
	             Vector( r.nOrg[0], r.nOrg[1], r.nOrg[2] ), movedToWorld );
	ConcatTransforms( movedToWorld, childToParent, composed );

	MatrixAngles( composed, angOut, vecOut );
}

//-----------------------------------------------------------------------------
// The file has to be FOUND, and it has to hold every row the managed side
// expects. A table that silently loaded as empty would pass the loop below
// without executing it once — the shape of "check that cannot fail" this tree
// keeps finding.
//-----------------------------------------------------------------------------
TEST( ParentCompose, TheTableIsThereAndHasEveryRow )
{
	const std::vector< Row > rows = LoadCases();

	ASSERT_FALSE( rows.empty() ) << "parent_compose_cases.txt was not found next to the test";
	EXPECT_EQ( 12u, rows.size() ) << "the table has the rows both sides were written against";
}

//-----------------------------------------------------------------------------
// Every row, against the arithmetic it was taken from.
//-----------------------------------------------------------------------------
TEST( ParentCompose, EveryRowIsWhatMathlibComputes )
{
	const std::vector< Row > rows = LoadCases();
	ASSERT_FALSE( rows.empty() );

	for ( size_t i = 0; i < rows.size(); i++ )
	{
		const Row &r = rows[i];

		Vector vecOut;
		QAngle angOut;
		Place( r, vecOut, angOut );

		EXPECT_NEAR( r.outOrg[0], vecOut.x, 0.001f ) << r.name << " origin x";
		EXPECT_NEAR( r.outOrg[1], vecOut.y, 0.001f ) << r.name << " origin y";
		EXPECT_NEAR( r.outOrg[2], vecOut.z, 0.001f ) << r.name << " origin z";

		EXPECT_NEAR( r.outAng[0], angOut[0], 0.001f ) << r.name << " pitch";
		EXPECT_NEAR( r.outAng[1], angOut[1], 0.001f ) << r.name << " yaw";
		EXPECT_NEAR( r.outAng[2], angOut[2], 0.001f ) << r.name << " roll";
	}
}

//-----------------------------------------------------------------------------
// THE TABLE IS NOT A TABLE OF TRANSLATIONS, stated as a check rather than as a
// comment.
//
// A composition that dropped the parent's rotation — took the parent's origin
// and added the child's offset to it — is the single most likely way for the
// port to be wrong, and it is invisible to any row whose parent does not rotate.
// So: recompute every row that way, and require that at least ten of the twelve
// come out DIFFERENT. If somebody ever trims this table down to the easy cases,
// this fails rather than the suite quietly becoming a check that cannot fail.
//-----------------------------------------------------------------------------
TEST( ParentCompose, MostRowsWouldFailARotationBlindComposition )
{
	const std::vector< Row > rows = LoadCases();
	ASSERT_FALSE( rows.empty() );

	size_t nDiffer = 0;

	for ( size_t i = 0; i < rows.size(); i++ )
	{
		const Row &r = rows[i];

		// The wrong answer: the offset as a plain vector difference, added to the
		// parent's new origin, with the child's own facing left alone.
		const Vector vecBlind(
			r.nOrg[0] + ( r.cOrg[0] - r.pOrg[0] ),
			r.nOrg[1] + ( r.cOrg[1] - r.pOrg[1] ),
			r.nOrg[2] + ( r.cOrg[2] - r.pOrg[2] ) );

		const bool bSameOrigin =
			fabsf( vecBlind.x - r.outOrg[0] ) < 0.01f &&
			fabsf( vecBlind.y - r.outOrg[1] ) < 0.01f &&
			fabsf( vecBlind.z - r.outOrg[2] ) < 0.01f;

		const bool bSameAngles =
			fabsf( r.cAng[0] - r.outAng[0] ) < 0.01f &&
			fabsf( r.cAng[1] - r.outAng[1] ) < 0.01f &&
			fabsf( r.cAng[2] - r.outAng[2] ) < 0.01f;

		if ( !bSameOrigin || !bSameAngles )
			nDiffer++;
	}

	EXPECT_GE( nDiffer, 10u )
		<< "the table must keep enough rotating rows to catch a composition that "
		   "ignores the parent's rotation";
}

} // namespace
