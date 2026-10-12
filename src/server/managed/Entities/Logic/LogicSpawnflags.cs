using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The map logic entities, in C#.
///
/// These replace the classes in game/server/logicentities.cpp, which is excluded
/// from this mod's build — both cannot claim the same class names.
///
/// Each entity is a plain class and reads as a description rather than a
/// dispatcher: the attribute names it, [ScriptVars] keys are the settings a mapper
/// configures, [ScriptedEvent] methods are the inputs it answers to, and Output
/// fields are what it can fire. Names default to the member's own, so the FGD
/// names — which existing maps depend on — are visible in the declaration
/// instead of buried in a string switch.
/// </summary>
internal static class LogicSpawnflags
{
    public const int TimerUpDown = 1;
    public const int RelayRemoveOnFire = 1;
    public const int RelayAllowFastRetrigger = 2;
    public const int AutoFireOnce = 1;
    public const int RemapIgnoreOutOfRange = 1;
}
