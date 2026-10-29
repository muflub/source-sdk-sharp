//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "tier1/interface.h"

// old game event manager, don't use it!
#define INTERFACEVERSION_GAMEEVENTSMANAGER2 "GAMEEVENTSMANAGER002" // new game event manager,

#include "tier1/bitbuf.h"
// Purpose: Engine interface into global game event management

/* 

The GameEventManager keeps track and fires of all global game events. Game events 
are fired by game.dll for events like player death or team wins. Each event has a
unique name and comes with a KeyValue structure providing informations about this
event. Some events are generated also by the engine.

Events are networked to connected clients and invoked there to. Therefore you
have to specify all data fields and there data types in an public resource
file which is parsed by server and broadcasted to it's clients. A typical game 
event is defined like this:

	"game_start"				// a new game starts
	{
		"roundslimit"	"long"		// max round
		"timelimit"		"long"		// time limit
		"fraglimit"		"long"		// frag limit
		"objective"		"string"	// round objective
	}

All events must have unique names (case sensitive) and may have a list
of data fields. each data field must specify a data type, so the engine
knows how to serialize/unserialize that event for network transmission.
Valid data types are string, float, long, short, byte & bool. If a 
data field should not be broadcasted to clients, use the type "local".
*/

// max game event name length
// max bits needed for an event index
// max number of events allowed
// max size in bytes for a serialized event

class KeyValues;

abstract_class IGameEvent
{
public:
	virtual ~IGameEvent(){};
	virtual const char* GetName() const = 0; // get event name

	virtual bool IsReliable() const = 0; // if event handled reliable
	virtual void unused_IsLocal() = 0; // if event is never networked
	virtual void unused_IsEmpty() = 0; // check if data field exists

	// Data access
	virtual bool GetBool(const char* keyName = NULL, bool defaultValue = false) const = 0;
	virtual int GetInt(const char* keyName = NULL, int defaultValue = 0) const = 0;
	virtual float GetFloat(const char* keyName = NULL, float defaultValue = 0.0f) const = 0;
	virtual const char* GetString(const char* keyName = NULL, const char* defaultValue = "") const = 0;

	virtual void SetBool(const char* keyName, bool value) = 0;
	virtual void SetInt(const char* keyName, int value) = 0;
	virtual void SetFloat(const char* keyName, float value) = 0;
	virtual void SetString(const char* keyName, const char* value) = 0;

	// New stuff!
	virtual void unused_GetUint64() = 0;
	virtual void unused_GetWString() = 0;
	virtual void unused_GetPtr() = 0; // LOCAL only

	virtual void SetUint64(const char* keyName, uint64 value) = 0;
	virtual void unused_SetWString() = 0;
	virtual void SetPtr(const char* keyName, const void* value) = 0; // LOCAL only

	virtual void unused_GetDataKeys() = 0;

	// returns true if iteration aborted normally, false if it was aborted by the visitor callback
	virtual void unused_ForEventData() = 0;
};

abstract_class IGameEventListener2
{
public:
	virtual ~IGameEventListener2(){};

	// FireEvent is called by EventManager if event just occurred
	// KeyValue memory will be freed by manager if not needed anymore
	virtual void FireGameEvent(IGameEvent * event) = 0;
};

abstract_class IGameEventManager2 : public IBaseInterface
{
public:
	virtual ~IGameEventManager2(){};

	// load game event descriptions from a file eg "resource\gameevents.res"
	virtual int LoadEventsFromFile(const char* filename) = 0;

	// removes all and anything
	virtual void Reset() = 0;

	// adds a listener for a particular event
	virtual bool AddListener(IGameEventListener2 * listener, const char* name, bool bServerSide) = 0;

	// returns true if this listener is listens to given event
	virtual void unused_FindListener() = 0;

	// removes a listener
	virtual void RemoveListener(IGameEventListener2 * listener) = 0;

	// create an event by name, but doesn't fire it. returns NULL is event is not
	// known or no listener is registered for it. bForce forces the creation even if no listener is active
	virtual IGameEvent* CreateEvent(const char* name, bool bForce = false) = 0;

	// fires a server event created earlier, if bDontBroadcast is set, event is not send to clients
	virtual bool FireEvent(IGameEvent * event, bool bDontBroadcast = false) = 0;

	// fires an event for the local client only, should be used only by client code
	virtual bool FireEventClientSide(IGameEvent * event) = 0;

	// create a new copy of this event, must be free later
	virtual void unused_DuplicateEvent() = 0;

	// if an event was created but not fired for some reason, it has to bee freed, same UnserializeEvent
	virtual void unused_FreeEvent() = 0;

	// write/read event to/from bitbuffer
	virtual void unused_SerializeEvent() = 0;
	virtual void unused_UnserializeEvent() = 0; // create new KeyValues, must be deleted
};

// the old game event manager interface, don't use it. Rest is legacy support:

abstract_class IGameEventListener
{
public:
	virtual ~IGameEventListener(){};

	// FireEvent is called by EventManager if event just occurred
	// KeyValue memory will be freed by manager if not needed anymore
	virtual void FireGameEvent(KeyValues * event) = 0;
};

abstract_class IGameEventManager : public IBaseInterface
{
public:
	virtual ~IGameEventManager(){};

	// load game event descriptions from a file eg "resource\gameevents.res"
	virtual int LoadEventsFromFile(const char* filename) = 0;

	// removes all and anything
	virtual void Reset() = 0;

	virtual void unused_GetEvent() = 0; // returns keys for event

	// adds a listener for a particular event
	virtual bool AddListener(IGameEventListener * listener, const char* event, bool bIsServerSide) = 0;
	// registers for all known events
	virtual bool AddListener(IGameEventListener * listener, bool bIsServerSide) = 0;

	// removes a listener
	virtual void RemoveListener(IGameEventListener * listener) = 0;

	// fires an global event, specific event data is stored in KeyValues
	// local listeners will receive the event instantly
	// a network message will be send to all connected clients to invoke
	// the same event there
	virtual bool FireEvent(KeyValues * event) = 0;

	// fire a side server event, that it wont be broadcasted to player clients
	virtual void unused_FireEventServerOnly() = 0;

	// fires an event only on this local client
	// can be used to fake events coming over the network
	virtual void unused_FireEventClientOnly() = 0;

	// write/read event to/from bitbuffer
	virtual void unused_SerializeKeyValues() = 0;
	virtual void unused_UnserializeKeyValue() = 0; // create new KeyValues, must be deleted
};
