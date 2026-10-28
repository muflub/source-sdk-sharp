//========= SourceSharp — client.so's own tier0 thread calls =================//
//
// tier0_thread_port.cpp: ThreadGetCurrentId and CThreadFastMutex's contended
// Lock, defined in client.so for the fork headers' inline code
// (plan_server_novalve.md P4, then plan_client_novalve C9). This binary links NO tier0, so
// every call below reaches the port. ss_clock.h, which replaced our own
// Plat_FloatTime calls, is held here too.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier0/platform.h"
#include "tier0/threadtools.h"

#include "ss_clock.h"

#include <atomic>
#include <pthread.h>
#include <thread>
#include <unistd.h>
#include <vector>

TEST( Tier0ThreadPort, TheThreadIdIsPthreadSelfAndNeverTheUnownedZero )
{
	EXPECT_EQ( (ThreadId_t)pthread_self(), ThreadGetCurrentId() );
	EXPECT_NE( (ThreadId_t)0, ThreadGetCurrentId() );
}

TEST( Tier0ThreadPort, EachThreadHasItsOwnIdAndKeepsIt )
{
	const ThreadId_t mine = ThreadGetCurrentId();
	ThreadId_t first = 0, second = 0;
	std::thread other(
		[&]
		{
			first = ThreadGetCurrentId();
			second = ThreadGetCurrentId();
		} );
	other.join();

	EXPECT_NE( mine, first );
	EXPECT_EQ( first, second );
	EXPECT_EQ( mine, ThreadGetCurrentId() );
}

TEST( Tier0ThreadPort, TheFastMutexExcludesUnderContention )
{
	// Every thread fights for the lock, so the contended path (the port's
	// Lock( id, sleep )) runs; an unguarded read-modify-write loses counts.
	static volatile CThreadFastMutex s_Mutex;
	static long s_nCount;
	s_nCount = 0;

	enum
	{
		kThreads = 8,
		kEach = 20000
	};

	std::vector<std::thread> threads;
	for ( int t = 0; t < kThreads; ++t )
	{
		threads.emplace_back(
			[]
			{
				for ( int i = 0; i < kEach; ++i )
				{
					s_Mutex.Lock();
					const long n = s_nCount;
					ThreadPause();
					s_nCount = n + 1;
					s_Mutex.Unlock();
				}
			} );
	}
	for ( std::thread &t : threads )
		t.join();

	EXPECT_EQ( (long)kThreads * kEach, s_nCount );
}

TEST( Tier0ThreadPort, TheFastMutexNestsOnItsOwnerAndRefusesOthersUntilFullyUnlocked )
{
	static volatile CThreadFastMutex s_Mutex;
	s_Mutex.Lock();
	s_Mutex.Lock();

	bool bOtherGot = true;
	std::thread( [&] { bOtherGot = s_Mutex.TryLock(); } ).join();
	EXPECT_FALSE( bOtherGot );

	s_Mutex.Unlock();
	std::thread( [&] { bOtherGot = s_Mutex.TryLock(); } ).join();
	EXPECT_FALSE( bOtherGot ) << "one Unlock of two released the lock";

	s_Mutex.Unlock();
	std::thread(
		[&]
		{
			bOtherGot = s_Mutex.TryLock();
			if ( bOtherGot )
				s_Mutex.Unlock();
		} )
		.join();
	EXPECT_TRUE( bOtherGot );
}

TEST( Tier0ThreadPort, TheContendedLockWaitsForTheOwnerToLetGo )
{
	static volatile CThreadFastMutex s_Mutex;
	s_Mutex.Lock();

	std::atomic<bool> bGot{ false };
	std::thread waiter(
		[&]
		{
			s_Mutex.Lock( 1 ); // the sleeping variant of the contended path
			bGot = true;
			s_Mutex.Unlock();
		} );
	usleep( 20 * 1000 );
	EXPECT_FALSE( bGot.load() ) << "Lock returned while another thread held it";

	s_Mutex.Unlock();
	waiter.join();
	EXPECT_TRUE( bGot.load() );
}

TEST( SsClock, CountsFromNearZeroAndMeasuresASleep )
{
	const double t0 = ss::SecondsSinceStart();
	usleep( 50 * 1000 );
	const double t1 = ss::SecondsSinceStart();

	EXPECT_LT( t0, 60.0 ) << "the base is not this DLL's start: a float would lose milliseconds";
	EXPECT_GE( t1 - t0, 0.049 );
	EXPECT_LT( t1 - t0, 1.0 );
}
