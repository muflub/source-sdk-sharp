//========= SourceSharp — what slot a virtual method sits in =================//
//
// Asking the COMPILER which vtable slot a method occupies, rather than counting
// `virtual` lines in a header.
//
// Under the Itanium C++ ABI a pointer to a member function carries the slot in
// it, so `&IVEngineServer::SetPausedForced` is the header the mod compiled
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
#if defined( __GNUC__ ) || defined( __clang__ )

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

// MSVC represents pointers to member functions as thunks rather than as a slot
// index, so the slot cannot be recovered this way. Reported as not virtual,
// which makes every caller refuse to install rather than install something
// wrong.
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
