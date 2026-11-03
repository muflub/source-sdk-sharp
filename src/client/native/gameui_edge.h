//========= SourceSharp — the GameUI visibility edge ==========================//
//
// The engine's GameUI hands its activation to the client through
// IGameClientExports::OnGameUIActivated/OnGameUIHidden — but only when the
// -game argument is literally tf or tf_beta (the engine's GameUI's
// OnGameUIActivated): the name is compared whole, so a mod run from
// a path, as ours are, never reaches them (measured 2026-10-31: no
// OnGameUIActivated call through gameui_activate with -game <path>). The
// client therefore watches the engine's own answer, IEngineVGui::
// IsGameUIVisible, and fires the same two game events on the edge. This state
// machine is the whole of that decision, kept engine-free so the shipped
// class is the thing under test; the caller does the firing.
//
//=============================================================================//

#pragma once

// One watch's answer for a frame: what changed about the GameUI's visibility.
enum class GameUIEdge
{
	None,
	Activated,
	Hidden
};

// The client's watch: fire the game event the answer names, once per change.
// The first reading only primes -- unless the GameUI is already up, the launch
// (the engine raises it before the first client frame): then it activates.
class GameUIVisibility
{
public:
	GameUIEdge Update(bool bVisible)
	{
		if (!m_bPrimed)
		{
			m_bPrimed = true;
			m_bVisible = bVisible;
			return bVisible ? GameUIEdge::Activated : GameUIEdge::None;
		}

		if (bVisible == m_bVisible)
			return GameUIEdge::None;

		m_bVisible = bVisible;
		return bVisible ? GameUIEdge::Activated : GameUIEdge::Hidden;
	}

private:
	bool m_bPrimed = false;
	bool m_bVisible = false;
};

// Whether the menu's override panel goes to the gameui on a frame: while the
// GameUI is up and the panel is not already under the gameui's base panel.
// IGameUI::SetMainMenuOverride (stock CHudMainMenuOverride::AttachToGameUI,
// tf_hud_mainmenuoverride.cpp:323-331) parents it there, drops the gameui's
// own menu items and keeps the panel up; the gameui re-parents nothing after,
// so a hand is once per home.
inline bool MenuOverrideHand(bool bGameUiVisible, bool bUnderGameUI)
{
	return bGameUiVisible && !bUnderGameUI;
}

// Whether the gameui holds the panel as its override: stock takes it back at
// the client's shutdown (~CHudMainMenuOverride, :255-262), once, and only
// after it was handed. Value-initialise a local ({}).
class GameUIOverrideHold
{
public:
	void Handed() { m_bHanded = true; }

	bool TakeBack()
	{
		const bool bWasHanded = m_bHanded;
		m_bHanded = false;
		return bWasHanded;
	}

private:
	bool m_bHanded; // trivially constructible: a zeroed global, no static initializer
};

// The client frame's watch, over the engine's GameUI panel (shell_clientdll.cpp
// HudUpdate); defined in shell_clientdll_gameclientexports.cpp beside the
// exports whose work it stands in for.
void SourceSharp_GameUIEdge();

// The edge's other half, carried out on the same answer: the menu's override
// panel goes to the gameui (MenuOverrideHand); defined in shell_hud.cpp beside
// the menu panels.
void SourceSharp_MenuRootFollowGameUI(bool bGameUiVisible);

// The client's shutdown: the gameui lets go of the override (GameUIOverrideHold).
void SourceSharp_MenuRootReleaseGameUI(void);
