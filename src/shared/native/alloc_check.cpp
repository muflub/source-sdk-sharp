// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// Each __wrap_X is what this DLL's references to X resolve to; __real_X is the
// unwrapped definition. Nothing here may allocate through a wrapped function:
// the table uses __real_calloc/__real_free, the lock spins, output goes through
// write(2), and a thread-local depth lets nested calls pass through untracked.

#include "alloc_check.h"

#ifdef SOURCESHARP_ALLOC_CHECK

#include <atomic>
#include <dlfcn.h>
#include <execinfo.h>
#include <fcntl.h>
#include <new>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

// client.so overrides the tag so both DLLs' reports in one log stay apart.
#ifndef SOURCESHARP_ALLOC_CHECK_TAG
#define SOURCESHARP_ALLOC_CHECK_TAG "[SourceSharp/AllocCheck]"
#endif

typedef std::align_val_t AlignVal;

extern "C"
{
void *__real_malloc( size_t );
void *__real_calloc( size_t, size_t );
void *__real_realloc( void *, size_t );
void __real_free( void * );
int __real_posix_memalign( void **, size_t, size_t );
void *__real_aligned_alloc( size_t, size_t );
void *__real_memalign( size_t, size_t );
char *__real_strdup( const char * );
char *__real_strndup( const char *, size_t );

void *__real__Znwm( size_t );
void *__real__Znam( size_t );
void *__real__ZnwmRKSt9nothrow_t( size_t, const std::nothrow_t & );
void *__real__ZnamRKSt9nothrow_t( size_t, const std::nothrow_t & );
void *__real__ZnwmSt11align_val_t( size_t, AlignVal );
void *__real__ZnamSt11align_val_t( size_t, AlignVal );
void *__real__ZnwmSt11align_val_tRKSt9nothrow_t( size_t, AlignVal, const std::nothrow_t & );
void *__real__ZnamSt11align_val_tRKSt9nothrow_t( size_t, AlignVal, const std::nothrow_t & );
void __real__ZdlPv( void * );
void __real__ZdaPv( void * );
void __real__ZdlPvm( void *, size_t );
void __real__ZdaPvm( void *, size_t );
void __real__ZdlPvSt11align_val_t( void *, AlignVal );
void __real__ZdaPvSt11align_val_t( void *, AlignVal );
void __real__ZdlPvmSt11align_val_t( void *, size_t, AlignVal );
void __real__ZdaPvmSt11align_val_t( void *, size_t, AlignVal );
void __real__ZdlPvRKSt9nothrow_t( void *, const std::nothrow_t & );
void __real__ZdaPvRKSt9nothrow_t( void *, const std::nothrow_t & );
}

namespace
{

enum Kind : uint8_t
{
	KIND_NONE = 0,
	KIND_MALLOC, // malloc calloc realloc posix_memalign aligned_alloc memalign strdup strndup
	KIND_NEW,
	KIND_NEW_ARRAY,
	KIND_NEW_ALIGNED,
	KIND_NEW_ARRAY_ALIGNED,
};

const char *KindName( uint8_t k )
{
	switch ( k )
	{
		case KIND_MALLOC:
			return "malloc";
		case KIND_NEW:
			return "new";
		case KIND_NEW_ARRAY:
			return "new[]";
		case KIND_NEW_ALIGNED:
			return "new(align)";
		case KIND_NEW_ARRAY_ALIGNED:
			return "new[](align)";
		default:
			return "?";
	}
}

struct Slot
{
	uintptr_t ptr; // 0 empty, 1 tombstone
	size_t size;
	void *site;
	uint8_t kind;
};

const uintptr_t kEmpty = 0;
const uintptr_t kTomb = 1;

// Foreign-free sites, deduplicated by caller address; a fixed array because a
// grown one would have to allocate while the lock is held.
struct Site
{
	void *site;
	uint64_t count;
};

const int kMaxSites = 256;

// Plain zero-initialised data with no constructor: it must work for the first
// allocation any static initialiser makes, whatever the link order.
struct State
{
	std::atomic<bool> lock;
	Slot *slots;
	size_t cap; // power of two
	size_t used; // live + tombstones
	size_t live;
	uint64_t allocations;
	uint64_t releases;
	uint64_t mismatches;
	uint64_t foreign;
	Site foreignSites[kMaxSites];
	int foreignSiteCount;
	int mode; // 0 not read, 1 off, 2 report, 3 abort
};

State g_State;
thread_local int t_depth = 0;

struct Guard
{
	Guard()
	{
		while ( g_State.lock.exchange( true, std::memory_order_acquire ) )
		{
		}
	}

	~Guard() { g_State.lock.store( false, std::memory_order_release ); }
};

struct Depth
{
	Depth() { ++t_depth; }

	~Depth() { --t_depth; }
};

// 0 off, 1 report, 2 abort (stored plus one, so zero means "not read yet").
int Mode()
{
	if ( g_State.mode == 0 )
	{
		const char *v = getenv( "SOURCESHARP_ALLOC_CHECK" );
		if ( v && !strcmp( v, "off" ) )
			g_State.mode = 1;
		else if ( v && !strcmp( v, "abort" ) )
			g_State.mode = 3;
		else
			g_State.mode = 2;
	}
	return g_State.mode - 1;
}

void Out( const char *fmt, ... )
{
	char buf[1024];
	va_list ap;
	va_start( ap, fmt );
	int n = vsnprintf( buf, sizeof( buf ), fmt, ap );
	va_end( ap );
	if ( n <= 0 )
		return;
	if ( n > (int)sizeof( buf ) - 1 )
		n = sizeof( buf ) - 1;
	ssize_t ignored = write( 2, buf, n );
	(void)ignored;
	if ( const char *path = getenv( "SOURCESHARP_ALLOC_CHECK_LOG" ) )
	{
		int fd = open( path, O_WRONLY | O_CREAT | O_APPEND, 0644 );
		if ( fd >= 0 )
		{
			ignored = write( fd, buf, n );
			close( fd );
		}
	}
}

size_t Hash( uintptr_t p )
{
	p ^= p >> 33;
	p *= 0xff51afd7ed558ccdULL;
	p ^= p >> 33;
	return (size_t)p;
}

// Lock held.
void Grow()
{
	size_t ncap = g_State.cap ? g_State.cap * 2 : 1 << 16;
	// Rehash in place when the table is mostly tombstones rather than full.
	if ( g_State.cap && g_State.live * 4 < g_State.cap )
		ncap = g_State.cap;
	Slot *n = (Slot *)__real_calloc( ncap, sizeof( Slot ) );
	if ( !n )
		abort();
	for ( size_t i = 0; i < g_State.cap; i++ )
	{
		const Slot &s = g_State.slots[i];
		if ( s.ptr <= kTomb )
			continue;
		size_t j = Hash( s.ptr ) & ( ncap - 1 );
		while ( n[j].ptr != kEmpty )
			j = ( j + 1 ) & ( ncap - 1 );
		n[j] = s;
	}
	__real_free( g_State.slots );
	g_State.slots = n;
	g_State.cap = ncap;
	g_State.used = g_State.live;
}

void Insert( void *p, size_t size, uint8_t kind, void *site )
{
	if ( !p || Mode() == 0 )
		return;
	Guard g;
	if ( ( g_State.used + 1 ) * 2 > g_State.cap )
		Grow();
	size_t mask = g_State.cap - 1;
	size_t j = Hash( (uintptr_t)p ) & mask;
	size_t tomb = (size_t)-1;
	while ( g_State.slots[j].ptr != kEmpty )
	{
		if ( g_State.slots[j].ptr == (uintptr_t)p )
		{
			// The engine freed the block behind our back and the address was reused.
			g_State.slots[j] = Slot{ (uintptr_t)p, size, site, kind };
			g_State.allocations++;
			return;
		}
		if ( g_State.slots[j].ptr == kTomb && tomb == (size_t)-1 )
			tomb = j;
		j = ( j + 1 ) & mask;
	}
	if ( tomb != (size_t)-1 )
		j = tomb;
	else
		g_State.used++;
	g_State.slots[j] = Slot{ (uintptr_t)p, size, site, kind };
	g_State.live++;
	g_State.allocations++;
}

// Lock held. True the first time a site is seen, so the caller can print one
// backtrace per site after dropping the lock.
bool NoteForeign( void *p, void *site )
{
	g_State.foreign++;
	for ( int i = 0; i < g_State.foreignSiteCount; i++ )
	{
		if ( g_State.foreignSites[i].site == site )
		{
			g_State.foreignSites[i].count++;
			return false;
		}
	}
	(void)p;
	if ( g_State.foreignSiteCount < kMaxSites )
	{
		g_State.foreignSites[g_State.foreignSiteCount++] = Site{ site, 1 };
		return true;
	}
	return false;
}

void Symbolise( void *site, char *out, size_t n )
{
	Dl_info info;
	if ( site && dladdr( site, &info ) && info.dli_fname )
	{
		const char *base = strrchr( info.dli_fname, '/' );
		base = base ? base + 1 : info.dli_fname;
		// Module-relative, so addr2line can read it back (most symbols are hidden).
		snprintf( out, n, "%s+0x%lx%s%s", base, (unsigned long)( (char *)site - (char *)info.dli_fbase ),
				  info.dli_sname ? " " : "", info.dli_sname ? info.dli_sname : "" );
	}
	else
	{
		snprintf( out, n, "%p", site );
	}
}

void Finding( const char *what, void *p, uint8_t had, uint8_t want, size_t hadSize, size_t wantSize, void *site )
{
	Depth d;
	char where[512];
	Symbolise( site, where, sizeof( where ) );
	Out( SOURCESHARP_ALLOC_CHECK_TAG " %s: %p allocated as %s (%zu bytes), released as %s (%zu bytes) at %s\n", what, p,
		 KindName( had ), hadSize, KindName( want ), wantSize, where );
	void *frames[32];
	int nf = backtrace( frames, 32 );
	backtrace_symbols_fd( frames, nf, 2 );
	for ( int i = 1; i < nf; i++ )
	{
		char at[512];
		Dl_info fi;
		if ( dladdr( frames[i], &fi ) && fi.dli_fname )
		{
			const char *b = strrchr( fi.dli_fname, '/' );
			snprintf( at, sizeof( at ), "%s+0x%lx", b ? b + 1 : fi.dli_fname,
					  (unsigned long)( (char *)frames[i] - (char *)fi.dli_fbase ) );
		}
		else
		{
			snprintf( at, sizeof( at ), "%p", frames[i] );
		}
		Out( SOURCESHARP_ALLOC_CHECK_TAG "     #%d %s\n", i, at );
	}
	if ( Mode() == 2 )
		abort();
}

// true if the release may go on to the real function.
bool Remove( void *p, uint8_t kind, size_t size, bool sized, void *site )
{
	if ( !p || Mode() == 0 )
		return true;
	uint8_t had = KIND_NONE;
	size_t hadSize = 0;
	bool firstAtSite = false;
	{
		Guard g;
		g_State.releases++;
		if ( g_State.cap )
		{
			size_t mask = g_State.cap - 1;
			size_t j = Hash( (uintptr_t)p ) & mask;
			while ( g_State.slots[j].ptr != kEmpty )
			{
				if ( g_State.slots[j].ptr == (uintptr_t)p )
				{
					had = g_State.slots[j].kind;
					hadSize = g_State.slots[j].size;
					g_State.slots[j].ptr = kTomb;
					g_State.live--;
					break;
				}
				j = ( j + 1 ) & mask;
			}
		}
		if ( had == KIND_NONE )
		{
			firstAtSite = NoteForeign( p, site );
		}
		else if ( had != kind || ( sized && hadSize != size ) )
		{
			g_State.mismatches++;
		}
		else
		{
			return true;
		}
	}
	if ( had == KIND_NONE )
	{
		if ( Mode() == 2 || firstAtSite )
			Finding( "FOREIGN FREE", p, KIND_NONE, kind, 0, size, site );
		return true;
	}
	Finding( "MISMATCH", p, had, kind, hadSize, size, site );
	return true;
}

} // namespace

#define SITE __builtin_return_address( 0 )
#define PASS       \
	if ( t_depth ) \
	return

extern "C"
{
void *__wrap_malloc( size_t n )
{
	PASS __real_malloc( n );
	Depth d;
	void *p = __real_malloc( n );
	Insert( p, n, KIND_MALLOC, SITE );
	return p;
}

void *__wrap_calloc( size_t c, size_t n )
{
	PASS __real_calloc( c, n );
	Depth d;
	void *p = __real_calloc( c, n );
	Insert( p, c * n, KIND_MALLOC, SITE );
	return p;
}

void *__wrap_realloc( void *old, size_t n )
{
	PASS __real_realloc( old, n );
	Depth d;
	if ( old )
		Remove( old, KIND_MALLOC, 0, false, SITE );
	void *p = __real_realloc( old, n );
	if ( p )
		Insert( p, n, KIND_MALLOC, SITE );
	else if ( old && n )
		Insert( old, 0, KIND_MALLOC, SITE ); // failed: the old block is still ours
	return p;
}

void __wrap_free( void *p )
{
	PASS __real_free( p );
	Depth d;
	Remove( p, KIND_MALLOC, 0, false, SITE );
	__real_free( p );
}

int __wrap_posix_memalign( void **out, size_t a, size_t n )
{
	PASS __real_posix_memalign( out, a, n );
	Depth d;
	int r = __real_posix_memalign( out, a, n );
	if ( r == 0 )
		Insert( *out, n, KIND_MALLOC, SITE );
	return r;
}

void *__wrap_aligned_alloc( size_t a, size_t n )
{
	PASS __real_aligned_alloc( a, n );
	Depth d;
	void *p = __real_aligned_alloc( a, n );
	Insert( p, n, KIND_MALLOC, SITE );
	return p;
}

// tier0's aligned allocator (MemAlloc_AllocAligned) on POSIX.
void *__wrap_memalign( size_t a, size_t n )
{
	PASS __real_memalign( a, n );
	Depth d;
	void *p = __real_memalign( a, n );
	Insert( p, n, KIND_MALLOC, SITE );
	return p;
}

char *__wrap_strdup( const char *s )
{
	PASS __real_strdup( s );
	Depth d;
	char *p = __real_strdup( s );
	Insert( p, p ? strlen( p ) + 1 : 0, KIND_MALLOC, SITE );
	return p;
}

char *__wrap_strndup( const char *s, size_t n )
{
	PASS __real_strndup( s, n );
	Depth d;
	char *p = __real_strndup( s, n );
	Insert( p, p ? strlen( p ) + 1 : 0, KIND_MALLOC, SITE );
	return p;
}

#define WRAP_NEW( name, kind, params, args ) \
	void *__wrap_##name params               \
	{                                        \
		PASS __real_##name args;             \
		Depth d;                             \
		void *p = __real_##name args;        \
		Insert( p, n, kind, SITE );          \
		return p;                            \
	}

WRAP_NEW( _Znwm, KIND_NEW, ( size_t n ), ( n ) )
WRAP_NEW( _Znam, KIND_NEW_ARRAY, ( size_t n ), ( n ) )
WRAP_NEW( _ZnwmRKSt9nothrow_t, KIND_NEW, ( size_t n, const std::nothrow_t &t ), ( n, t ) )
WRAP_NEW( _ZnamRKSt9nothrow_t, KIND_NEW_ARRAY, ( size_t n, const std::nothrow_t &t ), ( n, t ) )
WRAP_NEW( _ZnwmSt11align_val_t, KIND_NEW_ALIGNED, ( size_t n, AlignVal a ), ( n, a ) )
WRAP_NEW( _ZnamSt11align_val_t, KIND_NEW_ARRAY_ALIGNED, ( size_t n, AlignVal a ), ( n, a ) )
WRAP_NEW( _ZnwmSt11align_val_tRKSt9nothrow_t, KIND_NEW_ALIGNED, ( size_t n, AlignVal a, const std::nothrow_t &t ),
		  ( n, a, t ) )
WRAP_NEW( _ZnamSt11align_val_tRKSt9nothrow_t, KIND_NEW_ARRAY_ALIGNED, ( size_t n, AlignVal a, const std::nothrow_t &t ),
		  ( n, a, t ) )

#define WRAP_DELETE( name, kind, sized, size, params, args ) \
	void __wrap_##name params                                \
	{                                                        \
		PASS __real_##name args;                             \
		Depth d;                                             \
		Remove( p, kind, size, sized, SITE );                \
		__real_##name args;                                  \
	}

WRAP_DELETE( _ZdlPv, KIND_NEW, false, 0, ( void *p ), ( p ) )
WRAP_DELETE( _ZdaPv, KIND_NEW_ARRAY, false, 0, ( void *p ), ( p ) )
WRAP_DELETE( _ZdlPvm, KIND_NEW, true, n, ( void *p, size_t n ), ( p, n ) )
WRAP_DELETE( _ZdaPvm, KIND_NEW_ARRAY, false, n, ( void *p, size_t n ), ( p, n ) )
WRAP_DELETE( _ZdlPvSt11align_val_t, KIND_NEW_ALIGNED, false, 0, ( void *p, AlignVal a ), ( p, a ) )
WRAP_DELETE( _ZdaPvSt11align_val_t, KIND_NEW_ARRAY_ALIGNED, false, 0, ( void *p, AlignVal a ), ( p, a ) )
WRAP_DELETE( _ZdlPvmSt11align_val_t, KIND_NEW_ALIGNED, true, n, ( void *p, size_t n, AlignVal a ), ( p, n, a ) )
WRAP_DELETE( _ZdaPvmSt11align_val_t, KIND_NEW_ARRAY_ALIGNED, false, n, ( void *p, size_t n, AlignVal a ), ( p, n, a ) )
WRAP_DELETE( _ZdlPvRKSt9nothrow_t, KIND_NEW, false, 0, ( void *p, const std::nothrow_t &t ), ( p, t ) )
WRAP_DELETE( _ZdaPvRKSt9nothrow_t, KIND_NEW_ARRAY, false, 0, ( void *p, const std::nothrow_t &t ), ( p, t ) )

SourceSharpAllocCheckCounts SourceSharp_AllocCheckCounts()
{
	Guard g;
	return SourceSharpAllocCheckCounts{ g_State.allocations, g_State.releases, g_State.mismatches, g_State.foreign,
										g_State.live };
}

void SourceSharp_AllocCheckResetForTest()
{
	Guard g;
	__real_free( g_State.slots );
	g_State.slots = nullptr;
	g_State.cap = g_State.used = g_State.live = 0;
	g_State.allocations = g_State.releases = g_State.mismatches = g_State.foreign = 0;
	g_State.foreignSiteCount = 0;
	g_State.mode = 0;
}

void SourceSharp_AllocCheckReport( const char *when )
{
	Depth d;
	SourceSharpAllocCheckCounts c = SourceSharp_AllocCheckCounts();
	Out( SOURCESHARP_ALLOC_CHECK_TAG " %s: %llu allocations, %llu releases, %llu mismatches, "
									 "%llu foreign frees, %llu live\n",
		 when ? when : "report", (unsigned long long)c.allocations, (unsigned long long)c.releases,
		 (unsigned long long)c.mismatches, (unsigned long long)c.foreignFrees, (unsigned long long)c.live );

	// Snapshot the foreign sites under the lock, symbolise outside it.
	Site sites[kMaxSites];
	int ns;
	{
		Guard g;
		ns = g_State.foreignSiteCount;
		memcpy( sites, g_State.foreignSites, sizeof( Site ) * ns );
	}
	for ( int i = 0; i < ns; i++ )
	{
		char where[512];
		Symbolise( sites[i].site, where, sizeof( where ) );
		Out( SOURCESHARP_ALLOC_CHECK_TAG "   foreign free x%llu at %s\n", (unsigned long long)sites[i].count, where );
	}

	// Every live allocation site; sites past kTop are counted, not dropped.
	const int kTop = 512;

	struct Agg
	{
		void *site;
		uint64_t n;
		uint64_t bytes;
	};

	Agg top[kTop]; // 12 KB of stack: the report runs on an ordinary thread
	int nt = 0;
	uint64_t nOverflow = 0;
	{
		Guard g;
		for ( size_t i = 0; i < g_State.cap; i++ )
		{
			const Slot &s = g_State.slots[i];
			if ( s.ptr <= kTomb )
				continue;
			int k = 0;
			for ( ; k < nt; k++ )
				if ( top[k].site == s.site )
					break;
			if ( k < nt )
			{
				top[k].n++;
				top[k].bytes += s.size;
			}
			else if ( nt < kTop )
			{
				top[nt++] = Agg{ s.site, 1, s.size };
			}
			else
			{
				nOverflow++;
			}
		}
	}
	if ( nOverflow )
		Out( SOURCESHARP_ALLOC_CHECK_TAG "   live blocks at sites past the first %d: %llu (not itemised)\n", kTop,
			 (unsigned long long)nOverflow );
	for ( int i = 0; i < nt; i++ )
	{
		char where[512];
		Symbolise( top[i].site, where, sizeof( where ) );
		Out( SOURCESHARP_ALLOC_CHECK_TAG "   live x%llu (%llu bytes) from %s\n", (unsigned long long)top[i].n,
			 (unsigned long long)top[i].bytes, where );
	}
}

} // extern "C"

// Runs after every static destructor in this DLL, at both exit and dlclose: a
// destructor attribute would run before them at dlclose. __cxa_atexit handlers
// run in reverse registration order, and this registers before any static.
extern "C" int __cxa_atexit( void ( * )( void * ), void *, void * );
extern "C" void *__dso_handle;

static void AllocCheckAtUnload( void * )
{
	if ( Mode() != 0 )
		SourceSharp_AllocCheckReport( "unload" );
}

__attribute__( ( constructor( 101 ) ) ) static void AllocCheckArmUnloadReport()
{
	__cxa_atexit( AllocCheckAtUnload, nullptr, &__dso_handle );
}

#endif // SOURCESHARP_ALLOC_CHECK
