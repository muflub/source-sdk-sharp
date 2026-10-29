//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "hud.h"
#include "hud_element_helper.h"

#include "GameEventListener.h"

#undef new

// Purpose: Base class for all hud elements
class CHudElement : public CGameEventListener
{
public:
	DECLARE_CLASS_NOBASE(CHudElement);

	// constructor - registers object in global list
	CHudElement(const char* pElementName);
	// destructor - removes object from the global list
	virtual ~CHudElement();

	// called when the Hud is initialised (whenever the DLL is loaded)
	virtual void Init() { return; }

	// called whenever the video mode changes, and whenever Init() would be called, so the hud can vid init itself
	virtual void VidInit() { return; }

	// LevelInit's called whenever a new level is starting
	virtual void LevelInit() { return; };

	// LevelShutdown's called whenever a level is finishing
	virtual void LevelShutdown() { return; };

	// called whenever the hud receives "reset" message, which is (usually) every time the client respawns after getting killed
	virtual void Reset() { return; }

	// Called once per frame for visible elements before general key processing
	virtual void ProcessInput() { return; }

	virtual const char* GetName() const { return m_pElementName; };

	// Return true if this hud element should be visible in the current hud state
	virtual bool ShouldDraw();

	virtual bool IsActive() { return m_bActive; };

	virtual void SetActive(bool bActive);

	virtual GameActionSet_t GetPreferredActionSet() { return GAME_ACTION_SET_NONE; }

	// Hidden bits.
	// HIDEHUD_ flags that note when this element should be hidden in the HUD
	virtual void SetHiddenBits(int iBits);

	// memory handling, uses calloc so members are zero'd out on instantiation
	void* operator new(size_t stAllocateBlock)
	{
		Assert(stAllocateBlock != 0);
		void* pMem = malloc(stAllocateBlock);
		memset(pMem, 0, stAllocateBlock);
		return pMem;
	}

	void operator delete(void* pMem)
	{
#if defined(_DEBUG)
		int size = _msize(pMem);
		memset(pMem, 0xcd, size);
#endif
		free(pMem);
	}

	void SetNeedsRemove(bool needsremove);

	// For now, CHUdElements declare a single priority value. They will only be hidden
	// by panels with a lower priority and will only lock out panels with a lower priority
	virtual int GetRenderGroupPriority();

public: // IGameEventListener Interface
	virtual void FireGameEvent(IGameEvent* event) {}

public:
	// True if this element is visible, and should think
	bool m_bActive;

protected:
	int m_iHiddenBits;

private:
	const char* m_pElementName;
	bool m_bNeedsRemove;
	bool m_bIsParentedToClientDLLRootPanel;

	CUtlVector<int> m_HudRenderGroups;
};

#include "utlpriorityqueue.h"

inline bool RenderGroupLessFunc(CHudElement* const& lhs, CHudElement* const& rhs)
{
	return (lhs->GetRenderGroupPriority() < rhs->GetRenderGroupPriority());
}

// hud elements declare themselves to be part of a hud render group, by name
// we register with each hudelement a list of indeces of groups they are in
// then they can query by index the state of their render group
class CHudRenderGroup
{
public:
	CHudRenderGroup()
	{
		m_pLockingElements.SetLessFunc(RenderGroupLessFunc);
		bHidden = false;
	}

	bool bHidden;
	CUtlPriorityQueue<CHudElement*> m_pLockingElements;
};
