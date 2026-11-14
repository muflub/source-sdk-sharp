using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Shared.Combat;

namespace TF2.Client.Ui;

/// <summary>
/// The kill feeds' namer for a killer that is no player (<see cref="IKillFeedKillerNames"/>),
/// over the names the server sends ahead of each such death
/// (<see cref="TFKillerName"/>): the dead player's last name, held for
/// <see cref="HoldSeconds"/>, its token resolved as stock resolves a boss's
/// (<c>g_pVGuiLocalize-&gt;Find</c>, tf_hud_deathnotice.cpp:1074), the token
/// itself where no table holds it. A name older than the hold, or from before
/// the clock went back (a new level), names nobody.
/// </summary>
[SideService(Side.Client, As = [typeof(IKillFeedKillerNames), typeof(ITFKillerNameHook)])]
public sealed class TFKillerNames(IGameClock clock, IUiLocalisation localisation) : IKillFeedKillerNames, ITFKillerNameHook, IOrderedHook
{
    /// <summary>How long a name waits for its death: the event follows it in the same packet.</summary>
    public const float HoldSeconds = 1f;

    /// <summary>TF2's band of the client's hook orders.</summary>
    public const int Order = 1_000_000 - 1_000 + 712;

    public int HookOrder => Order;

    private readonly Dictionary<int, (string Token, int Team, float At)> _names = new();

    public void OnKillerName(TFKillerName name) => _names[name.Victim] = (name.Token ?? "", name.Team, clock.Now);

    public bool TryName(in PlayerDeathArgs death, out string name, out int team)
    {
        (name, team) = ("", 0);
        if (!_names.TryGetValue(death.UserId, out var held))
            return false;
        float age = clock.Now - held.At;
        if (age < 0f || age > HoldSeconds || held.Token.Length == 0)
            return false;
        name = localisation.Find(held.Token) ?? held.Token;
        team = held.Team;
        return true;
    }
}
