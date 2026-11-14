using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// Test contributions to a hero's Now (HE3's dev source), so the stats can be
/// exercised before items and skills exist: what <c>tf2_descent_contrib</c>
/// adds and <c>tf2_descent_contrib_clear</c> removes, per player slot. Kept
/// across a level change, as a hero's gear would be.
/// </summary>
[SideService(Side.Server)]
public sealed class DevContributions : IHeroContributionSource, IOrderedHook
{
    /// <summary>The dev block, after every real source TF2 Descent will have.</summary>
    public const int Order = ModStages.First + 900;

    private readonly Dictionary<int, List<Contribution>> _bySlot = new();
    private int _next;

    public int HookOrder => Order;

    public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) =>
        _bySlot.TryGetValue(playerIndex, out var list) ? list.ToArray() : [];

    /// <summary>Adds <paramref name="amount"/> of <paramref name="stat"/> to the slot, from a source of its own in <paramref name="state"/>.</summary>
    public Contribution Add(int playerIndex, string stat, double amount, SourceState state = SourceState.Active)
    {
        var contribution = new Contribution(stat, amount, new ContributionSource(SourceKind.Buff, $"dev:{++_next}", state));
        if (!_bySlot.TryGetValue(playerIndex, out var list))
            _bySlot[playerIndex] = list = new List<Contribution>();
        list.Add(contribution);
        return contribution;
    }

    /// <summary>Removes every test contribution of the slot; answers how many went.</summary>
    public int Clear(int playerIndex) => _bySlot.Remove(playerIndex, out var list) ? list.Count : 0;
}
