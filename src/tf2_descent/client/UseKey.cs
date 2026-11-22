using SourceSharp.Client.Input;

namespace TF2.Descent.Client;

// Use (+use) on E. TF2 has no use key: its E calls for a Medic and drops the
// flag. Descent is played by using things (a stall, a cage's button, loot on
// the floor, a gravestone, a portal pad, a downed friend), so it takes E back.
// Order after TF2's rows on E (dropitem 290 and the call for a Medic), so on a
// fresh config this bind is the one E ends with; the label lists it on the
// keyboard page, where a player whose config predates it can bind it.
[KeyAction("+use", "Use", Section = "Descent", Order = 1005, Keys = ["e"])]
public sealed class UseKey;
