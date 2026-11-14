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
                case nameof(IItemLedger.PotionDrop):
                case nameof(IItemLedger.BossDrop):
                    return Task.FromResult(DropResult.Nothing);
                default:
                    throw new NotSupportedException($"the floor's facts do not expect {method.Name}");
            }
        }
    }
}
