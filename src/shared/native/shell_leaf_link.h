//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a renderable's link into the client leaf system ====//
//
// THE THING DE-ENTIFYING A RENDERABLE ACTUALLY TAKES AWAY, and it is not the
// drawing.
//
// C_PointWorldText drew because it was in the client leaf system, and it was in
// the client leaf system because C_BaseEntity put it there and kept it honest:
// InitializeAsClientEntity added it, ~C_BaseEntity removed it, and every
// SetAbsOrigin went through MarkRenderHandleDirty so the leaf system re-linked
// it into the leaves it had moved into. A CShellRenderable
// (shell_renderable.h) has to do all three itself, and each one fails silently
// in a different way:
//
//   ADD TWICE      the leaf system writes the new handle through the
//                  renderable's RenderHandle(), overwriting the old one. The
//                  first row is then unreachable: nothing can ever remove it,
//                  and it holds a pointer to an object that will be deleted.
//   REMOVE UNADDED  RemoveRenderable( INVALID ) is survivable by luck rather
//                  than by design -- it early-outs on IsValidIndex -- and an
//                  INVALID handle that happens to be a live index is not
//                  something to rely on not happening.
//   MOVE WITHOUT TELLING
//                  the object keeps drawing, from the leaves it was FIRST added
//                  to, for as long as it lives. Text that follows a moving
//                  entity looks right until the camera is somewhere that should
//                  see it and does not.
//
// Two of those three are guards and the third is a call that must not be
// skipped, so all three live here with a test that drives them, rather than as
// three `if`s inside an engine-coupled class where nothing can reach them.
//
// Header-only and engine-free -- templated on the LEAF SYSTEM and on the
// RENDERABLE, so the game passes ClientLeafSystem() and a CShellRenderable and
// shell_leaf_link_test.cpp passes two recorders. What is required of a
// renderable is IsInLeafSystem() and RenderHandle(); of a leaf system,
// AddRenderable, RemoveRenderable and RenderableChanged.
//
//=============================================================================//

#ifndef SOURCESHARP_SHELL_LEAF_LINK_H
#define SOURCESHARP_SHELL_LEAF_LINK_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Into the leaf system, at most once.
//
// The guard is the whole of it. AddRenderable does not check -- it calls
// NewRenderable unconditionally and writes the fresh handle through the
// renderable -- so a second add strands the first row rather than being
// refused.
//-----------------------------------------------------------------------------
template < typename TLeaf, typename TRenderable, typename TGroup >
inline void ShellLeaf_Add( TLeaf *pLeaf, TRenderable *pRenderable, TGroup group )
{
	if ( pRenderable->IsInLeafSystem() )
		return;

	pLeaf->AddRenderable( pRenderable, group );
}

//-----------------------------------------------------------------------------
// Out of the leaf system, at most once, and never when it was never in.
//-----------------------------------------------------------------------------
template < typename TLeaf, typename TRenderable >
inline void ShellLeaf_Remove( TLeaf *pLeaf, TRenderable *pRenderable )
{
	if ( !pRenderable->IsInLeafSystem() )
		return;

	pLeaf->RemoveRenderable( pRenderable->RenderHandle() );
}

//-----------------------------------------------------------------------------
// "Where I am has changed" — C_BaseEntity::MarkRenderHandleDirty's job.
//
// Guarded rather than unconditional because a renderable is placed BEFORE it is
// added as often as after: the factory sets an origin on a fresh object, and at
// that moment there is no handle to report against.
//-----------------------------------------------------------------------------
template < typename TLeaf, typename TRenderable >
inline void ShellLeaf_Moved( TLeaf *pLeaf, TRenderable *pRenderable )
{
	if ( !pRenderable->IsInLeafSystem() )
		return;

	pLeaf->RenderableChanged( pRenderable->RenderHandle() );
}

#endif // SOURCESHARP_SHELL_LEAF_LINK_H
