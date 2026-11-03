//========= SourceSharp — the GameUI's calls before the managed root ========//
//
// The engine initialises its GameUI before the client (plan_client_novalve.md
// §8, C0: IGameUI::Initialize, Start, the console's Initialize and SetParent,
// OnGameUIActivated arrive before IBaseClientDLL::Init brings the CLR up), so
// SSGameUI and SSGameConsole hold those calls here and play them to the
// managed GameUI, in their order, at the first call after it exists
// (plan_gameui_managed.md G4). Only calls with no string to keep are held; a
// question asked before then answers no. Engine-free, so the shipped queue is
// the thing under test.
//
//=============================================================================//
#pragma once

#include <stdint.h>

namespace GameUiEarly
{

enum class Call : uint8_t
{
	Initialize,
	PostInit,
	Connect,
	Start,
	Shutdown,
	OnGameUIActivated,
	OnGameUIHidden,
	OnLevelLoadingStarted,
	SetLoadingBackgroundDialog,
	SetMainMenuOverride,
	ConsoleActivate,
	ConsoleInitialize,
	ConsoleHide,
	ConsoleClear,
	ConsoleSetParent,
};

struct Entry
{
	Call call;
	uintptr_t arg; // the factory, the panel, the flag: each call's one argument
};

// The held calls, at most kCapacity; any past it are counted and dropped.
class Queue
{
public:
	static constexpr int kCapacity = 32;

	void Push(Call call, uintptr_t arg = 0)
	{
		if (m_nCount >= kCapacity)
		{
			m_nDropped++;
			return;
		}
		m_Entries[m_nCount].call = call;
		m_Entries[m_nCount].arg = arg;
		m_nCount++;
	}

	int Count() const { return m_nCount; }

	int Dropped() const { return m_nDropped; }

	// Plays every held call to pSink ( pSink( entry ) ), oldest first, and
	// empties the queue first, so a call the sink makes back is not replayed.
	template <typename Sink>
	void Replay(Sink& sink)
	{
		const int n = m_nCount;
		m_nCount = 0;
		for (int i = 0; i < n; i++)
			sink(m_Entries[i]);
	}

private:
	Entry m_Entries[kCapacity];
	int m_nCount;
	int m_nDropped;
};

} // namespace GameUiEarly
