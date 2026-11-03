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

// The client frame's watch, over the engine's GameUI panel (shell_clientdll.cpp
// HudUpdate); defined in shell_clientdll_gameclientexports.cpp beside the
// exports whose work it stands in for.
void SourceSharp_GameUIEdge();

// The edge's other half, carried out on the same answer: the menu root's home
// follows the GameUI, under the gameui's root while it is up and back under
// the client viewport after (the stock gameui's own answer for its main-menu
// override); defined in shell_hud.cpp beside the menu root.
void SourceSharp_MenuRootFollowGameUI(bool bGameUiVisible);
