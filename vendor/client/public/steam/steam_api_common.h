//====== Copyright Valve Corporation, All rights reserved. ====================
// Steamworks SDK minimal include
// Defines the minimal set of things we need to use any single interface
// or register for any callback.

#pragma once

#include "steamclientpublic.h"

// S_API defines the linkage and calling conventions for steam_api.dll exports
#if defined(_WIN32)
#define S_API extern "C" __declspec(dllimport)
#elif defined(__GNUC__)
#define S_API extern "C"
#else // !WIN32
#define S_API extern "C"
#endif

#if (defined(STEAM_API_EXPORTS) || defined(STEAM_API_NODLL)) && !defined(API_GEN)
#define STEAM_PRIVATE_API(...) __VA_ARGS__
#endif

// handle to single instance of a steam user
typedef int32 HSteamUser;

// #define away __cdecl on posix.
// This is really, really bad.  We're sorry.  But it's been this way for
// a long time now and it's scary to change it, as there may be others that
// depend on it.
#ifndef _WIN32
#define __cdecl
#endif

//	steam callback and call-result helpers
//	The following macros and classes are used to register your application for
//	callbacks and call-results, which are delivered in a predictable manner.
//	STEAM_CALLBACK macros are meant for use inside of a C++ class definition.
//	They map a Steam notification callback directly to a class member function
//	which is automatically prototyped as "void func( callback_type *pParam )".
//	CCallResult is used with specific Steam APIs that return "result handles".
//	The handle can be passed to a CCallResult object's Set function, along with
//	an object pointer and member-function pointer. The member function will
//	be executed once the results of the Steam API call are available.
//	CCallback and CCallbackManual classes can be used instead of STEAM_CALLBACK
//	macros if you require finer control over registration and unregistration.
//	Callbacks and call-results are queued automatically and are only
//	delivered/executed when your application calls SteamAPI_RunCallbacks().
//  Note that there is an alternative, lower level callback dispatch mechanism.
//  See SteamAPI_ManualDispatch_Init

// Declares a callback member function plus a helper member variable which
// registers the callback on object creation and unregisters on destruction.
// The optional fourth 'var' param exists only for backwards-compatibility
// and can be ignored.
#define STEAM_CALLBACK(thisclass, func, ... /*callback_type, [deprecated] var*/) \
	_STEAM_CALLBACK_SELECT((__VA_ARGS__, 4, 3), (/**/, thisclass, func, __VA_ARGS__))

// Declares a callback function and a named CCallbackManual variable which
// has Register and Unregister functions instead of automatic registration.
#define STEAM_CALLBACK_MANUAL(thisclass, func, callback_type, var) \
	CCallbackManual<thisclass, callback_type> var;                 \
	void func(callback_type* pParam)

// Same as STEAM_CALLBACK, but for callbacks on the gameserver interface.
// These will be dispatched during SteamGameServer_RunCallbacks

// Purpose: base for callbacks and call results - internal implementation detail
class CCallbackBase
{
public:
	CCallbackBase()
	{
		m_nCallbackFlags = 0;
		m_iCallback = 0;
	}

	// don't add a virtual destructor because we export this binary interface across dll's
	virtual void Run(void* pvParam) = 0;
	virtual void Run(void* pvParam, bool bIOFailure, SteamAPICall_t hSteamAPICall) = 0;

	int GetICallback() { return m_iCallback; }

	virtual int GetCallbackSizeBytes() = 0;

protected:
	enum
	{
		k_ECallbackFlagsRegistered = 0x01,
		k_ECallbackFlagsGameServer = 0x02
	};

	uint8 m_nCallbackFlags;
	int m_iCallback;
	friend class CCallbackMgr;

private:
	CCallbackBase(const CCallbackBase&);
};

// Purpose: templated base for callbacks - internal implementation detail
template <int sizeof_P>
class CCallbackImpl : protected CCallbackBase
{
public:
	virtual ~CCallbackImpl()
	{
		if (m_nCallbackFlags & k_ECallbackFlagsRegistered)
			SteamAPI_UnregisterCallback(this);
	}

	void SetGameserverFlag() { m_nCallbackFlags |= k_ECallbackFlagsGameServer; }

protected:
	friend class CCallbackMgr;
	virtual void Run(void* pvParam) = 0;

	virtual void Run(void* pvParam, bool /*bIOFailure*/, SteamAPICall_t /*hSteamAPICall*/) { Run(pvParam); }

	virtual int GetCallbackSizeBytes() { return sizeof_P; }
};

// Purpose: maps a steam async call result to a class member function
//			template params: T = local class, P = parameter struct
template <class T, class P>
class CCallResult : private CCallbackBase
{
public:
	typedef void (T::*func_t)(P*, bool);

	CCallResult();
	~CCallResult();

	void Set(SteamAPICall_t hAPICall, T* p, func_t func);
	bool IsActive() const;
	void Cancel();

	void SetGameserverFlag() { m_nCallbackFlags |= k_ECallbackFlagsGameServer; }

private:
	virtual void Run(void* pvParam);
	virtual void Run(void* pvParam, bool bIOFailure, SteamAPICall_t hSteamAPICall);

	virtual int GetCallbackSizeBytes() { return sizeof(P); }

	SteamAPICall_t m_hAPICall;
	T* m_pObj;
	func_t m_Func;
};

// Purpose: maps a steam callback to a class member function
//			template params: T = local class, P = parameter struct,
//			bGameserver = listen for gameserver callbacks instead of client callbacks
template <class T, class P, bool bGameserver = false>
class CCallback : public CCallbackImpl<sizeof(P)>
{
public:
	typedef void (T::*func_t)(P*);

	// NOTE: If you can't provide the correct parameters at construction time, you should
	// use the CCallbackManual callback object (STEAM_CALLBACK_MANUAL macro) instead.
	CCallback(T* pObj, func_t func);

	void Register(T* pObj, func_t func);
	void Unregister();

protected:
	virtual void Run(void* pvParam);

	T* m_pObj;
	func_t m_Func;
};

// Purpose: subclass of CCallback which allows default-construction in
//			an unregistered state; you must call Register manually
template <class T, class P, bool bGameServer = false>
class CCallbackManual : public CCallback<T, P, bGameServer>
{
public:
	CCallbackManual()
		: CCallback<T, P, bGameServer>(nullptr, nullptr)
	{
	}

	// Inherits public Register and Unregister functions from base class
};

// Internal implementation details for all of the above
#include "steam_api_internal.h"
