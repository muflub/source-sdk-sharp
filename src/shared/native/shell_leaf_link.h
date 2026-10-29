//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A shell renderable's link into the client leaf system: add at most once (a
// second add strands the first row), remove only when added, and report moves so
// the leaf system re-links it. Templated on the leaf system and the renderable,
// so it can be tested.

#pragma once

// AddRenderable does not check for an existing handle, so guard here.
template <typename TLeaf, typename TRenderable, typename TGroup>
inline void ShellLeaf_Add(TLeaf* pLeaf, TRenderable* pRenderable, TGroup group)
{
	if (pRenderable->IsInLeafSystem())
		return;

	pLeaf->AddRenderable(pRenderable, group);
}

// Out of the leaf system, only when it is in.
template <typename TLeaf, typename TRenderable>
inline void ShellLeaf_Remove(TLeaf* pLeaf, TRenderable* pRenderable)
{
	if (!pRenderable->IsInLeafSystem())
		return;

	pLeaf->RemoveRenderable(pRenderable->RenderHandle());
}

// C_BaseEntity::MarkRenderHandleDirty's job. Guarded because a renderable is
// often placed before it is added.
template <typename TLeaf, typename TRenderable>
inline void ShellLeaf_Moved(TLeaf* pLeaf, TRenderable* pRenderable)
{
	if (!pRenderable->IsInLeafSystem())
		return;

	pLeaf->RenderableChanged(pRenderable->RenderHandle());
}
