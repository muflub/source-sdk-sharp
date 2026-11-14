using SourceSharp.Client.Input;

namespace TF2.Descent.Client.Party;

// The party window (WO9) on P, the key the invitation's notice names. It takes
// TF2's say_party (TF2's party chat: Descent's heroes are one team, so team
// chat reaches the party). Order after the windows' (1110 to 1140), so on a
// fresh config this bind is the one P ends with.
[KeyAction("tf2_descent_ui party", "Party", Section = "Descent", Order = 1150, Keys = ["p"])]
public sealed class PartyKey;
