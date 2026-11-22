using System.Reflection;

using TF2.Descent.Server.Items;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// An item ledger whose answers a fact holds back and gives when it likes:
/// each claim, kill drop, potion drop and boss drop is a task the fact
/// completes (or answered at once with <see cref="Answer"/>, <see cref="Drops"/>).
/// Australium collects succeed at once. Anything else is not the floor's and throws.
/// </summary>
internal sealed class HeldLedger
{
    public HeldLedger()
    {
        Proxy = DispatchProxy.Create<IItemLedger, Dispatch>();
        ((Dispatch)(object)Proxy).Owner = this;
    }

    public IItemLedger Proxy { get; }

    public LedgerKind Kind { get; set; } = LedgerKind.InMemory;

    /// <summary>When set, claims answer this at once.</summary>
    public LedgerResult? Answer { get; set; }

    /// <summary>When set, drops answer this at once with the items it makes.</summary>
    public Func<KillDrop, DropResult>? Drops { get; set; }

    public List<TaskCompletionSource<LedgerResult>> Claims { get; } = [];
    public List<(KillDrop Kill, TaskCompletionSource<DropResult> Answer)> KillDrops { get; } = [];
    public List<string> Asked { get; } = [];

    /// <summary>Every pile asked for; answered at once (with <see cref="PileAnswer"/> when set, else the game's roll).</summary>
    public List<KillPile> KillPiles { get; } = [];

    /// <summary>When set, a pile is answered this (the host's replay).</summary>
    public Func<KillPile, PileAnswer>? PileAnswer { get; set; }

    /// <summary>Every pile claim: who, which pile, and the members given.</summary>
    public List<(string Hero, long Pile, IReadOnlyList<string> Members)> PileClaims { get; } = [];

    public class Dispatch : DispatchProxy
    {
        public HeldLedger Owner { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var o = Owner;
            o.Asked.Add(method!.Name);
            switch (method.Name)
            {
                case "get_Kind":
                    return o.Kind;
                case nameof(IItemLedger.Claim):
                    if (o.Answer is { } answer)
                        return Task.FromResult(answer);
                    var claim = new TaskCompletionSource<LedgerResult>();
                    o.Claims.Add(claim);
                    return claim.Task;
                case nameof(IItemLedger.CollectAustralium):
                    return Task.FromResult(new LedgerResult(true, null, null, null));
                case nameof(IItemLedger.KillDrop):
                    var kill = (KillDrop)args![0]!;
                    if (o.Drops is { } drops)
                        return Task.FromResult(drops(kill));
                    var held = new TaskCompletionSource<DropResult>();
                    o.KillDrops.Add((kill, held));
                    return held.Task;
                case nameof(IItemLedger.KillAustralium):
                    var pile = (KillPile)args![0]!;
                    o.KillPiles.Add(pile);
                    return Task.FromResult(o.PileAnswer?.Invoke(pile) ?? new PileAnswer(true, null, new AustraliumPile(9000 + o.KillPiles.Count, pile.Australium, pile.Killer)));
                case nameof(IItemLedger.ClaimAustralium):
                    o.PileClaims.Add(((string)args![0]!, (long)args[1]!, (IReadOnlyList<string>)args[2]!));
                    return Task.FromResult(new PileTake(true, null, []));
                case nameof(IItemLedger.PotionDrop):
                case nameof(IItemLedger.ContainerDrop):
                case nameof(IItemLedger.BossDrop):
                    return Task.FromResult(DropResult.Nothing);
                default:
                    throw new NotSupportedException($"the floor's facts do not expect {method.Name}");
            }
        }
    }
}
