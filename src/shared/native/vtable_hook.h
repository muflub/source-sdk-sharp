//========= SourceSharp — what slot a virtual method sits in =================//
//
// Asking the COMPILER which vtable slot a method occupies, rather than counting
// `virtual` lines in a header.
//
// Under the Itanium C++ ABI a pointer to a member function carries the slot in
// it (under Microsoft's, the vcall thunk it points at does, MemberOf below),
// so `&IVEngineServer::SetPausedForced` is the header the mod compiled
// against answering the question itself. The alternative is a number nobody can
// check that silently changes when a method is INSERTED rather than appended.
//
// WHO READS IT. shell_vtable_interfaces.cpp turns `MemberOf( &I::LastMethod
// ).slot + 1` into the entry count of five engine interfaces; shell_frame.cpp
// and shell_level.cpp do the same for the two shim tables they publish. All
// three are counting, not hooking.
//
// WHAT USED TO BE HERE. This file was the vtable INTERCEPTION machinery: a
// CTable that cloned an object's vtable, patched the copy and moved the object's
// pointer at it in one write, plus the Length()/IsExecutable() walk that found
// the end of a table and the OffsetOf<Base>() upcast that located a subobject.
// All of it existed for one caller -- shell_filesystem.cpp's file-open recorder,
// which put our own vtable on CFileSystem_Stdio so every read could be logged.
// That recorder had no reader; it is gone, and the clone/swap half (234 lines of
// vtable_hook.cpp, and 128 of the 256 here) goes with it. What is left is the
// half three live files still ask questions of.
//
// Deliberately free of Source headers so the machinery can be tested on its own,
// against a class this file's tests declare. See test/vtable_hook_test.cpp.
//
//=============================================================================//
#ifndef SOURCESHARP_VTABLE_HOOK_H
#define SOURCESHARP_VTABLE_HOOK_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace VTableHook
{

//-----------------------------------------------------------------------------
// Where a virtual member function lives.
//-----------------------------------------------------------------------------
struct Member
{
	// Index into the vtable of the subobject named by Adjust.
	int       slot;

	// What the ABI adds to `this` before the call.
	//
	// Zero for a pointer to a member of the class that declares it, which is
	// the only form worth passing in. Non-zero means the pointer was converted
	// from a base's member to a derived's, and the slot is numbered against the
	// base's vtable while this says where the base sits -- so the two facts
	// disagree about which table they are about, which is why every caller here
	// treats a non-zero adjust as a reason to stop. `&IFileSystem::Open` is the
	// shape that does it: Open is declared by IBaseFileSystem, so the slot is
	// numbered against IBaseFileSystem's table.
	ptrdiff_t adjust;

	// False for a non-virtual member, and on a compiler whose pointer-to-member
	// layout this does not understand. Either way the caller must not install.
	bool      isVirtual;
};

//-----------------------------------------------------------------------------
// The slot a pointer-to-member-function refers to.
//
// Under the Itanium C++ ABI a pointer to a member function is a pair: a code
// address or a vtable offset, and an adjustment to apply to `this`. A virtual
// function is distinguished by the low bit being set, and the rest of the word
// is its byte offset into the vtable.
//
// This is what makes the whole thing defensible. The alternative -- counting
// `virtual` lines in a header -- is a number nobody can check and that silently
// changes when a method is inserted rather than appended.
//-----------------------------------------------------------------------------
#if defined( _MSC_VER )

//-----------------------------------------------------------------------------
// The Microsoft ABI (MSVC, and clang-cl, which defines __clang__ too and so
// must be told apart first). A pointer to a member function of a class with
// single inheritance is one code address; with multiple inheritance it is
// that address and a 32-bit `this` adjustment. A pointer to a VIRTUAL member
// points at a small "vcall" thunk the compiler emits, which loads the vtable
// from `this` and jumps through the slot:
//
//     48 8B 01             mov rax, [rcx]
//     FF 20 | FF 60 d8 | FF A0 d32      jmp [rax] / [rax+d8] / [rax+d32]
//
// or, the load and the jump split (48 8B 00 / 40 d8 / 80 d32, then FF E0, as
// clang-cl writes it), either jump with the REX.W prefix (48) a tail jump
// carries.
// The slot is the displacement over the pointer size. Anything else (a plain
// function's code) is not virtual. An incremental-link stub (E9 rel32, a
// jump to the real code) is followed first. The bytes are read from the
// thunk itself, which is ordinary readable code in this module.
//-----------------------------------------------------------------------------
namespace Detail
{
	inline const unsigned char *FollowJumps( const unsigned char *p )
	{
		for ( int i = 0; i < 4 && p && p[0] == 0xE9; ++i )
		{
			int32_t rel;
			memcpy( &rel, p + 1, sizeof( rel ) );
			p = p + 5 + rel;
		}
		return p;
	}

	// An indirect jump, `jmp [rax+disp]` (FF 20 / FF 60 d8 / FF A0 d32) when
	// bDisp, else `jmp rax` (FF E0), with or without the REX.W prefix (48)
	// both compilers put on a tail jump. The displacement, 0 for none; -1 when
	// the bytes are not that jump.
	inline long IndirectJump( const unsigned char *q, bool bDisp )
	{
		if ( q[0] == 0x48 )
			++q;
		if ( q[0] != 0xFF )
			return -1;
		if ( !bDisp )
			return q[1] == 0xE0 ? 0 : -1;
		if ( q[1] == 0x20 )
			return 0;
		if ( q[1] == 0x60 )
			return (signed char)q[2];
		if ( q[1] == 0xA0 )
		{
			int32_t d32;
			memcpy( &d32, q + 2, sizeof( d32 ) );
			return d32;
		}
		return -1;
	}

	// The byte offset into the vtable the thunk jumps through, or -1.
	inline long VcallOffset( const unsigned char *p )
	{
		if ( !p || !( p[0] == 0x48 && p[1] == 0x8B && p[2] == 0x01 ) )   // mov rax, [rcx]
			return -1;
		const unsigned char *q = p + 3;
		const long jump = IndirectJump( q, true );                        // jmp [rax+disp]
		if ( jump >= 0 )
			return jump;
		if ( !( q[0] == 0x48 && q[1] == 0x8B ) )                          // mov rax, [rax+disp]
			return -1;
		long disp;
		const unsigned char *next;
		if ( q[2] == 0x00 )
		{
			disp = 0;
			next = q + 3;
		}
		else if ( q[2] == 0x40 )
		{
			disp = (signed char)q[3];
			next = q + 4;
		}
		else if ( q[2] == 0x80 )
		{
			int32_t d32;
			memcpy( &d32, q + 3, sizeof( d32 ) );
			disp = d32;
			next = q + 7;
		}
		else
		{
			return -1;
		}
		return IndirectJump( next, false ) == 0 ? disp : -1;            // jmp rax
	}
}

template <typename Fn>
inline Member MemberOf( Fn pmf )
{
	Member member;
	member.slot = -1;
	member.adjust = 0;
	member.isVirtual = false;

	// Single inheritance: the address alone. Multiple: address, then the
	// adjustment. Anything larger (virtual inheritance) is not guessed at.
	struct Multi { void *ptr; int32_t adj; };
	void *code = NULL;
	if ( sizeof( Fn ) == sizeof( void * ) )
	{
		memcpy( &code, &pmf, sizeof( code ) );
	}
	else if ( sizeof( Fn ) == sizeof( Multi ) )
	{
		Multi raw;
		memcpy( &raw, &pmf, sizeof( raw ) );
		code = raw.ptr;
		member.adjust = raw.adj;
	}
	else
	{
		return member;
	}

	const long offset = Detail::VcallOffset( Detail::FollowJumps( (const unsigned char *)code ) );
	if ( offset >= 0 && offset % (long)sizeof( void * ) == 0 )
	{
		member.isVirtual = true;
		member.slot = (int)( offset / (long)sizeof( void * ) );
	}
	return member;
}

#elif defined( __GNUC__ ) || defined( __clang__ )

template <typename Fn>
inline Member MemberOf( Fn pmf )
{
	struct Raw { uintptr_t ptr; ptrdiff_t adj; };

	Member member;
	member.slot = -1;
	member.adjust = 0;
	member.isVirtual = false;

	// A layout this does not recognise must not be guessed at.
	if ( sizeof( Fn ) != sizeof( Raw ) )
		return member;

	Raw raw;
	memcpy( &raw, &pmf, sizeof( raw ) );

	member.adjust = raw.adj;
	member.isVirtual = ( raw.ptr & 1 ) != 0;

	if ( member.isVirtual )
		member.slot = (int)( ( raw.ptr - 1 ) / sizeof( void * ) );

	return member;
}

#else

// A compiler whose pointer-to-member layout this does not know. Reported as
// not virtual, which makes every caller refuse to install rather than install
// something wrong.
template <typename Fn>
inline Member MemberOf( Fn )
{
	Member member;
	member.slot = -1;
	member.adjust = 0;
	member.isVirtual = false;
	return member;
}

#endif

} // namespace VTableHook

#endif // SOURCESHARP_VTABLE_HOOK_H
