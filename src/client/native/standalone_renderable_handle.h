#pragma once

// True, with the handle word, when pUnknown is an entity. A non-entity's
// GetRefEHandle() dereferences null, so it is never asked.
template <class Unknown>
inline bool RenderableEntityHandleWord(Unknown* pUnknown, uint32_t& nWord)
{
	if (!pUnknown || !pUnknown->GetIClientEntity())
		return false;
	nWord = (uint32_t)pUnknown->GetRefEHandle().ToInt();
	return true;
}
