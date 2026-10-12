using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

// This file holds no type. It is where the Logic folder's design note lives,
// and it used to also hold a class called LogicSpawnflags -- five constants
// belonging to four different entities, collected because each was one or two
// bits and had nowhere smaller to go. Grouping by SIZE is not grouping by
// domain: three of the five were the value 1 and meant three unrelated things,
// which is why they could not have become one enum without aliasing. They are
// private enums on their own entities now, and the note they were sharing a
// file with is the only thing that was ever folder-wide.
//
// The map logic entities, in C#.
//
// These replace the classes in game/server/logicentities.cpp, which is excluded
// from this mod's build — both cannot claim the same class names.
//
// Each entity is a plain class and reads as a description rather than a
// dispatcher: the attribute names it, [ScriptVars] fields are the settings a mapper
// configures, [ScriptedEvent] methods are the inputs it answers to, and Output
// fields are what it can fire. Names default to the member's own, so the FGD
// names — which existing maps depend on — are visible in the declaration
// instead of buried in a string switch.
