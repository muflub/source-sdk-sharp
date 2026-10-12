//========= SourceSharp — the descriptor blob's two halves agree =============//
//
// plan_shim.md Phase 2d. model_descriptor.cpp WRITES a model's whole table in
// one go and SourceSharp.Shared.Interop/ModelDescriptor.cs READS it, and the two are in
// different languages with no shared declaration between them. The format is
// therefore the thing that can silently disagree — and a disagreement does not
// crash: it produces a model whose labels are activity names, whose durations
// are flags, and whose sequences resolve to plausible wrong indices. Every
// caller already handles a wrong index, because -1 and "some other animation"
// are both legal outcomes.
//
// WHAT THIS PINS, and what it deliberately does not. It extracts the ORDER AND
// KIND of every field each side touches — Int, Float, String — and requires
// the two sequences to be identical. That catches a field added to one side, a
// field removed from one side, two fields swapped, and an int written where a
// float is read: every edit that leaves both halves compiling.
//
// It does NOT check the VALUES; ModelDescriptorTests drives the decoder over a
// hand-built blob for that, and neither check subsumes the other. This one
// would pass for two sides agreeing on a format neither implements correctly;
// that one would pass for a decoder matching a stand-in that had drifted from
// the writer. Together they close the loop the way a shared declaration would
// if these were one language.
//
// It is a SOURCE read, and has to be: the writer needs a studio header and an
// mdl cache, so no test binary can call it, and the reader is managed. What is
// at risk is an ordering, which no running program can compare either.
//
//=============================================================================//

#include <stdio.h>
#include <string>
#include <cstring>
#include <cstdlib>
#include <vector>

#include "gtest/gtest.h"

namespace
{

const char *const kWriter = "sourcesharp/game/shared/model_descriptor.cpp";
const char *const kReader = "sourcesharp/managed/SourceSharp.Shared.Interop/ModelDescriptor.cs";
const char *const kFormat = "sourcesharp/game/shared/model_descriptor.h";

std::string ReadFile( const char *pPath )
{
	FILE *fp = fopen( pPath, "rb" );

	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 4096 ];
	size_t n;

	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );

	fclose( fp );
	return out;
}

// anim_cluster_ledger_test.cpp's stripper. LOAD-BEARING: both files carry
// prose that names the very calls this scan is counting -- the header's format
// block spells the whole layout out in a comment.
std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	size_t i = 0;

	while ( i < src.size() )
	{
		if ( src.compare( i, 2, "//" ) == 0 )
		{
			while ( i < src.size() && src[ i ] != '\n' )
				i++;

			continue;
		}

		if ( src.compare( i, 2, "/*" ) == 0 )
		{
			const size_t end = src.find( "*/", i + 2 );

			if ( end == std::string::npos )
				break;

			i = end + 2;
			continue;
		}

		out += src[ i ];
		i++;
	}

	return out;
}

std::string Code( const char *pPath )
{
	return StripComments( ReadFile( pPath ) );
}

//-----------------------------------------------------------------------------
// The ordered field kinds a body touches, as a string of I/F/S.
//
// `pszPrefix` is the sink's or reader's call prefix -- "sink." on the writing
// side, "r." on the reading side -- so that a local variable happening to be
// called Int does not join the sequence.
//-----------------------------------------------------------------------------
std::string FieldKinds( const std::string &code,
                        const std::string &szBegin, const std::string &szEnd,
                        const char *pszPrefix )
{
	const size_t at = code.find( szBegin );

	if ( at == std::string::npos )
		return std::string();

	size_t end = szEnd.empty() ? code.size() : code.find( szEnd, at );

	if ( end == std::string::npos )
		end = code.size();

	const std::string body = code.substr( at, end - at );

	const std::string szInt    = std::string( pszPrefix ) + "Int(";
	const std::string szFloat  = std::string( pszPrefix ) + "Float(";
	const std::string szString = std::string( pszPrefix ) + "String(";

	std::string kinds;

	for ( size_t i = 0; i < body.size(); i++ )
	{
		if ( body.compare( i, szInt.size(), szInt ) == 0 )
		{
			kinds += 'I';
			i += szInt.size() - 1;
		}
		else if ( body.compare( i, szFloat.size(), szFloat ) == 0 )
		{
			kinds += 'F';
			i += szFloat.size() - 1;
		}
		else if ( body.compare( i, szString.size(), szString ) == 0 )
		{
			kinds += 'S';
			i += szString.size() - 1;
		}
	}

	return kinds;
}

} // namespace

//=============================================================================//
// THE STRIPPER, because both files' PROSE spells out the layout it is scanning.
//=============================================================================//

TEST( ModelDescriptorFormat, TheStripperRemovesBothCommentForms )
{
	EXPECT_EQ( "a;\nb;\n",
	           StripComments( "a;// sink.Int( nBones )\nb;/* r.Float(out f) */\n" ) );
}

TEST( ModelDescriptorFormat, TheStripperKeepsCallsThatAreCode )
{
	EXPECT_EQ( "IFS", FieldKinds( StripComments(
		"void W(){ sink.Int( 1 ); // sink.Int( 2 )\n sink.Float( 3 ); sink.String( \"x\" ); }" ),
		"void W()", "", "sink." ) );
}

TEST( ModelDescriptorFormat, TheScannerIgnoresACallThroughSomeOtherObject )
{
	// `other.Int(` must not join the sequence: the prefix is what says the
	// call is the sink's.
	EXPECT_EQ( "II", FieldKinds( "void W(){ sink.Int(a); other.Int(b); sink.Int(c); }",
	                             "void W()", "", "sink." ) );
}

//=============================================================================//
// THE TWO HALVES.
//=============================================================================//

TEST( ModelDescriptorFormat, BothSidesAreOnDisk )
{
	EXPECT_FALSE( ReadFile( kWriter ).empty() ) << kWriter << " is not there.";
	EXPECT_FALSE( ReadFile( kReader ).empty() ) << kReader << " is not there.";
	EXPECT_FALSE( ReadFile( kFormat ).empty() ) << kFormat << " is not there.";
}

TEST( ModelDescriptorFormat, TheWriterWritesTheFieldsTheReaderReadsInThatOrder )
{
	const std::string szWrite =
		FieldKinds( Code( kWriter ), "ModelDescriptor_Walk( CStudioHdr *pHdr", "", "sink." );

	const std::string szRead =
		FieldKinds( Code( kReader ), "public static ModelDescriptor Decode(", "private ref struct Reader", "r." );

	// NOT VACUOUS: a scan that matched nothing gives two empty strings, which
	// compare equal. The blob has a version word, a bone count, two counts,
	// four per-sequence scalars and five per-event ones, so the real sequence
	// is well into double figures on both sides.
	ASSERT_GE( szWrite.size(), 15u )
		<< "the WRITER scan found " << szWrite.size() << " fields in "
		<< kWriter << " -- the walk's name or the sink's spelling has changed, "
		   "so the comparison below would be between two mistakes. Found: "
		<< szWrite;

	ASSERT_GE( szRead.size(), 15u )
		<< "the READER scan found " << szRead.size() << " fields in "
		<< kReader << ". Found: " << szRead;

	EXPECT_EQ( szWrite, szRead )
		<< "model_descriptor.cpp writes\n  " << szWrite
		<< "\nand ModelDescriptor.cs reads\n  " << szRead
		<< "\n(I = int32, F = float, S = length-prefixed string).\n"
		   "These are two implementations of one format in two languages with "
		   "no shared declaration, and a mismatch does not crash: it decodes a "
		   "model whose labels are activity names and whose durations are "
		   "flags. Every caller already handles a wrong sequence index, "
		   "because -1 and 'some other animation' are both legal outcomes.";
}

TEST( ModelDescriptorFormat, TheVersionWordIsFirstOnBothSidesAndIsTheSameNumber )
{
	const std::string write = Code( kWriter );
	const std::string read  = Code( kReader );
	const std::string fmt   = Code( kFormat );

	// The writer's first field is the version.
	const std::string szWrite =
		FieldKinds( write, "ModelDescriptor_Walk( CStudioHdr *pHdr", "", "sink." );
	ASSERT_FALSE( szWrite.empty() );
	EXPECT_EQ( 'I', szWrite[ 0 ] );

	EXPECT_NE( write.find( "sink.Int( kModelDescriptorVersion );" ), std::string::npos )
		<< kWriter << " does not write the version word first. A stale decoder "
		   "then reads a differently shaped model instead of refusing.";

	// And the two constants are the same number, read out of each side rather
	// than pinned here: a format change moves both in the same commit, and this
	// is what says they moved together.
	const auto VersionAfter = []( const std::string &text, const char *pszNeedle ) -> int
	{
		const size_t at = text.find( pszNeedle );
		if ( at == std::string::npos )
			return -1;
		return atoi( text.c_str() + at + strlen( pszNeedle ) );
	};
	const int nNative  = VersionAfter( fmt, "kModelDescriptorVersion = " );
	const int nManaged = VersionAfter( read, "public const int Version = " );

	ASSERT_GE( nNative, 1 ) << kFormat << " no longer declares kModelDescriptorVersion";
	ASSERT_GE( nManaged, 1 ) << kReader << " no longer declares Version";
	EXPECT_EQ( nNative, nManaged )
		<< kFormat << " says version " << nNative << ", " << kReader << " says " << nManaged
		<< ": if the format moved, both move in the same commit.";
}

TEST( ModelDescriptorFormat, TheWriterMeasuresAndWritesWithTheSameWalk )
{
	const std::string write = Code( kWriter );

	// TWO PASSES OVER ONE BODY is what makes the size answer and the bytes
	// written describe the same model. A second, separate sizing routine is
	// the classic way for the two to disagree by one string.
	EXPECT_EQ( 2, (int)[ &write ]() {
		int n = 0;
		size_t at = 0;
		while ( ( at = write.find( "ModelDescriptor_Walk( &hdr,", at ) ) != std::string::npos )
		{
			n++;
			at += 1;
		}
		return n;
	}() )
		<< kWriter << " does not call ModelDescriptor_Walk exactly twice -- "
		   "once to measure and once to write. A separate sizing routine is "
		   "how the two come to disagree by one string.";

	EXPECT_NE( write.find( "if ( !pBuffer || nBytes < nNeeded )" ), std::string::npos )
		<< kWriter << " no longer refuses to write into a buffer that is too "
		   "small. A half-written blob decodes as a differently shaped model "
		   "rather than as a failure.";
}
