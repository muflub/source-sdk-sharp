// SourceSharp: the console line buffer the forwarder appends to and the
// managed side drains (console_line_buffer.h). Order, numbering, the bound and
// its overflow count, the swap, producers on many threads, a drain that never
// holds a print up, and the layout the managed reader reads.

#include "gtest/gtest.h"

#include "console_line_buffer_drain.h"

#include <atomic>
#include <chrono>
#include <map>
#include <memory>
#include <string>
#include <thread>
#include <vector>

namespace
{

using Line = ConsoleLineDrained;

std::vector<Line> Parse( const ConsoleLineTaken &taken )
{
	return ConsoleLineParse( taken );
}

std::vector<Line> Drain( CConsoleLineBuffer &buffer )
{
	return Parse( ConsoleLineSwap( buffer ) );
}

// 1 MiB: on the heap, never on a test thread's stack.
std::unique_ptr<CConsoleLineBuffer> Fresh()
{
	return std::make_unique<CConsoleLineBuffer>();
}

TEST( ConsoleLineBuffer, LinesComeOutInPrintOrderNumberedFromOne )
{
	auto buffer = Fresh();
	EXPECT_TRUE( buffer->Append( "first\n", 0 ) );
	EXPECT_TRUE( buffer->Append( "second\n", 1 ) );
	EXPECT_TRUE( buffer->Append( "", 0 ) );

	auto lines = Drain( *buffer );
	ASSERT_EQ( 3u, lines.size() );
	EXPECT_EQ( "first\n", lines[ 0 ].text );
	EXPECT_EQ( 1u, lines[ 0 ].seq );
	EXPECT_EQ( 0u, lines[ 0 ].warning );
	EXPECT_EQ( "second\n", lines[ 1 ].text );
	EXPECT_EQ( 2u, lines[ 1 ].seq );
	EXPECT_EQ( 1u, lines[ 1 ].warning );
	EXPECT_EQ( "", lines[ 2 ].text );
	EXPECT_EQ( 3u, lines[ 2 ].seq );
}

TEST( ConsoleLineBuffer, ADrainEmptiesAndTheNextDrainSeesOnlyNewLines )
{
	auto buffer = Fresh();
	buffer->Append( "a\n", 0 );
	ASSERT_EQ( 1u, Drain( *buffer ).size() );
	EXPECT_TRUE( Drain( *buffer ).empty() );

	buffer->Append( "b\n", 0 );
	auto lines = Drain( *buffer );
	ASSERT_EQ( 1u, lines.size() );
	EXPECT_EQ( "b\n", lines[ 0 ].text );
	EXPECT_EQ( 2u, lines[ 0 ].seq ) << "numbers run on across drains";
}

TEST( ConsoleLineBuffer, ALineThatDoesNotFitIsCountedAndKeepsItsNumber )
{
	auto buffer = Fresh();
	std::string big( kConsoleLineHalfBytes / 2, 'x' );
	EXPECT_TRUE( buffer->Append( big.c_str(), 0 ) );     // seq 1
	EXPECT_FALSE( buffer->Append( big.c_str(), 0 ) );    // seq 2: no room left
	EXPECT_FALSE( buffer->Append( big.c_str(), 0 ) );    // seq 3
	EXPECT_TRUE( buffer->Append( "small\n", 0 ) );       // seq 4 still fits

	auto taken = ConsoleLineSwap( *buffer );
	EXPECT_EQ( 2u, taken.nOverflow );
	auto lines = Parse( taken );
	ASSERT_EQ( 2u, lines.size() );
	EXPECT_EQ( 1u, lines[ 0 ].seq );
	EXPECT_EQ( 4u, lines[ 1 ].seq ) << "a dropped line's number is spent, so the gap shows";
	EXPECT_EQ( 0u, ConsoleLineSwap( *buffer ).nOverflow ) << "the swap hands the count over once";
}

TEST( ConsoleLineBuffer, ALineLargerThanAHalfIsCountedNeverWritten )
{
	auto buffer = Fresh();
	std::string huge( kConsoleLineHalfBytes, 'y' );
	EXPECT_FALSE( buffer->Append( huge.c_str(), 0 ) );
	auto taken = ConsoleLineSwap( *buffer );
	EXPECT_EQ( 1u, taken.nOverflow );
	EXPECT_EQ( 0u, taken.nBytes );
}

TEST( ConsoleLineBuffer, AfterASwapProducersWriteTheOtherHalf )
{
	auto buffer = Fresh();
	buffer->Append( "before\n", 0 );
	auto taken = ConsoleLineSwap( *buffer );
	buffer->Append( "after\n", 0 );

	// The taken half is the consumer's until its next swap: a print after the
	// swap must not land in it.
	auto lines = Parse( taken );
	ASSERT_EQ( 1u, lines.size() );
	EXPECT_EQ( "before\n", lines[ 0 ].text );
	auto next = Drain( *buffer );
	ASSERT_EQ( 1u, next.size() );
	EXPECT_EQ( "after\n", next[ 0 ].text );
}

TEST( ConsoleLineBuffer, NumbersWrapAndStillOrderBySignedDifference )
{
	auto buffer = Fresh();
	buffer->m_Header.nSeq = 0xFFFFFFFEu;
	buffer->Append( "a\n", 0 );
	buffer->Append( "b\n", 0 );
	buffer->Append( "c\n", 0 );
	auto lines = Drain( *buffer );
	ASSERT_EQ( 3u, lines.size() );
	EXPECT_EQ( 0xFFFFFFFFu, lines[ 0 ].seq );
	EXPECT_EQ( 0u, lines[ 1 ].seq );
	EXPECT_EQ( 1u, lines[ 2 ].seq );
	EXPECT_GT( (int32_t)( lines[ 1 ].seq - lines[ 0 ].seq ), 0 );
	EXPECT_GT( (int32_t)( lines[ 2 ].seq - lines[ 0 ].seq ), 0 );
}

TEST( ConsoleLineBuffer, ManyThreadsEachLineOnceEachThreadInOrder )
{
	auto buffer = Fresh();
	const int kThreads = 8, kLines = 2000;
	std::vector<Line> all;
	std::atomic<bool> done{ false };
	std::thread consumer( [ & ] {
		while ( !done.load() )
		{
			auto lines = Drain( *buffer );
			all.insert( all.end(), lines.begin(), lines.end() );
		}
		auto rest = Drain( *buffer );
		all.insert( all.end(), rest.begin(), rest.end() );
	} );
	std::vector<std::thread> producers;
	for ( int t = 0; t < kThreads; ++t )
		producers.emplace_back( [ &, t ] {
			for ( int i = 0; i < kLines; ++i )
				buffer->Append( ( std::to_string( t ) + ":" + std::to_string( i ) ).c_str(), 0 );
		} );
	for ( auto &p : producers )
		p.join();
	done = true;
	consumer.join();

	ASSERT_EQ( (size_t)( kThreads * kLines ), all.size() ) << "nothing lost, nothing doubled";
	std::map<int, int> nextOf;
	for ( size_t i = 0; i < all.size(); ++i )
	{
		EXPECT_EQ( (uint32_t)( i + 1 ), all[ i ].seq ) << "drained in number order";
		const auto colon = all[ i ].text.find( ':' );
		const int t = std::stoi( all[ i ].text.substr( 0, colon ) );
		const int n = std::stoi( all[ i ].text.substr( colon + 1 ) );
		EXPECT_EQ( nextOf[ t ]++, n ) << "thread " << t << "'s lines out of order";
	}
}

TEST( ConsoleLineBuffer, ASwapReleasesTheLockBeforeTheConsumerParses )
{
	// A drain swaps, then parses OUTSIDE the lock: a print made while the
	// consumer is still busy with what it took must not wait for it. The
	// parse is held for 300 ms here; the print must finish long before.
	auto buffer = Fresh();
	buffer->Append( "taken\n", 0 );
	std::atomic<bool> swapped{ false }, release{ false };
	std::thread consumer( [ & ] {
		auto taken = ConsoleLineSwap( *buffer );
		swapped = true;
		while ( !release.load() )
			std::this_thread::sleep_for( std::chrono::milliseconds( 1 ) );
		EXPECT_EQ( 1u, Parse( taken ).size() );
	} );
	while ( !swapped.load() )
		std::this_thread::yield();
	EXPECT_FALSE( ConsoleLineLocked( *buffer ) ) << "the swap still held the lock";

	std::atomic<bool> printed{ false };
	std::thread producer( [ & ] { buffer->Append( "during the parse\n", 0 ); printed = true; } );
	auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds( 300 );
	while ( !printed.load() && std::chrono::steady_clock::now() < deadline )
		std::this_thread::sleep_for( std::chrono::milliseconds( 1 ) );
	EXPECT_TRUE( printed.load() ) << "a print waited on the consumer's parse";
	release = true;
	consumer.join();
	producer.join();
}

TEST( ConsoleLineBuffer, AHeldLockHoldsAPrintOnlyUntilItIsReleased )
{
	// The positive arm of the test above: the lock IS what a print waits on,
	// so the bounded wait there is a property of the swap, not of a lock that
	// does nothing.
	auto buffer = Fresh();
	buffer->Lock();
	std::atomic<bool> printed{ false };
	std::thread producer( [ & ] { buffer->Append( "held\n", 0 ); printed = true; } );
	std::this_thread::sleep_for( std::chrono::milliseconds( 50 ) );
	EXPECT_FALSE( printed.load() ) << "the lock did not hold the print";
	buffer->Unlock();
	producer.join();
	EXPECT_TRUE( printed.load() );
}

// The managed reader (NativeConsoleBuffer.cs) reads these offsets;
// NativeConsoleBufferTests pins the same literals on its side.
TEST( ConsoleLineBuffer, LayoutIsTheOneTheManagedReaderReads )
{
	EXPECT_EQ( 0u, (unsigned)offsetof( ConsoleLineBufferHeader, nSeq ) );
	EXPECT_EQ( 4u, (unsigned)offsetof( ConsoleLineBufferHeader, nOverflow ) );
	EXPECT_EQ( 8u, (unsigned)offsetof( ConsoleLineBufferHeader, nLock ) );
	EXPECT_EQ( 12u, (unsigned)offsetof( ConsoleLineBufferHeader, nActive ) );
	EXPECT_EQ( 16u, (unsigned)offsetof( ConsoleLineBufferHeader, nUsed ) );
	EXPECT_EQ( 24u, (unsigned)offsetof( ConsoleLineBufferHeader, nHalfBytes ) );
	EXPECT_EQ( 32u, (unsigned)sizeof( ConsoleLineBufferHeader ) );
	EXPECT_EQ( 0u, (unsigned)offsetof( ConsoleLineRecord, nSeq ) );
	EXPECT_EQ( 4u, (unsigned)offsetof( ConsoleLineRecord, nLength ) );
	EXPECT_EQ( 8u, (unsigned)offsetof( ConsoleLineRecord, nWarning ) );
	EXPECT_EQ( 12u, (unsigned)sizeof( ConsoleLineRecord ) );
	EXPECT_EQ( 0u, (unsigned)offsetof( CConsoleLineBuffer, m_Header ) );
	EXPECT_EQ( 32u, (unsigned)offsetof( CConsoleLineBuffer, m_Halves ) );
	EXPECT_EQ( 524288u, kConsoleLineHalfBytes );
	EXPECT_EQ( 16u, ConsoleLineRecordBytes( 1 ) );
	EXPECT_EQ( 12u, ConsoleLineRecordBytes( 0 ) );
}

} // namespace
