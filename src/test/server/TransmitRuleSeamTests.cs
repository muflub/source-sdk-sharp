using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The per-client transmit rule's answer (tf2 F06): what
/// <c>ClientsBinding.ServerShouldTransmit</c> tells CheckTransmit to do with a
/// FULLCHECK edict and one client — the ITransmitRule chain, every rule asked,
/// a Never beating an Always and an Always the default, which is the PVS
/// check. The native walk's carrying-out is the shell_gameents_test's.
/// </summary>
public class TransmitRuleSeamTests
{
    /// <summary>A rule that answers <paramref name="answer"/> for one recipient only, and counts its asks.</summary>
    private sealed class FixedRule(EntityHandle entity, int recipient, TransmitAnswer answer, List<int>? asks = null) : ITransmitRule
    {
        public TransmitAnswer ShouldTransmit(EntityHandle e, int recipientSlot)
        {
            asks?.Add(recipientSlot);
            return e == entity && recipientSlot == recipient ? answer : TransmitAnswer.Default;
        }
    }

    private sealed class HooksOf(params ITransmitRule[] rules) : IHooks<ITransmitRule>
    {
        public IReadOnlyList<ITransmitRule> All => rules;
        public void Each(string what, Action<ITransmitRule> call) => throw new NotSupportedException();
    }

    private static ClientsBinding Binding(params ITransmitRule[] rules)
    {
        var module = DispatchProxy.Create<IServerModule, Module>();
        return new(new Crossings(), new QuietLog(), module, i => s_Directory.HandleOf(i), new HooksOf(rules));
    }

    private static readonly EntityDirectory s_Directory = new(new QuietLog());

    private class Module : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }

    private sealed class QuietLog : ILog
    {
        public void Msg(string message) { }

        public void Warning(string message) { }
    }

    private static readonly EntityHandle Entity = new(0x0000_0007);

    static TransmitRuleSeamTests()
    {
        Assert.True(s_Directory.Bind(Entity, new Thing()));
    }

    private sealed class Thing : ScriptedEntity
    {
    }

    [Fact]
    public void NoRuleAnswersThePvsCheck()
    {
        Assert.Equal(2, Binding().ServerShouldTransmit(1, 4));
    }

    [Fact]
    public void ANeverForOneRecipientKeepsItFromThatRecipientAndNoOther()
    {
        var binding = Binding(new FixedRule(Entity, recipient: 1, TransmitAnswer.Never));

        Assert.Equal(0, binding.ServerShouldTransmit(1, 7));
        Assert.Equal(2, binding.ServerShouldTransmit(2, 7));
    }

    [Fact]
    public void AnAlwaysSendsItWhateverThePvsSays()
    {
        Assert.Equal(1, Binding(new FixedRule(Entity, recipient: 3, TransmitAnswer.Always)).ServerShouldTransmit(3, 7));
    }

    [Fact]
    public void NeverBeatsAlwaysAndEveryRuleIsAsked()
    {
        var asks = new List<int>();
        var never = new FixedRule(Entity, 1, TransmitAnswer.Never, asks);

        // A second rule that would send it, so the precedence has something to
        // beat; its asks are counted by the first's shared list.
        ITransmitRule always = new AlwaysRule(Entity, asks);

        Assert.Equal(0, Binding(never, always).ServerShouldTransmit(1, 7));

        // Every rule heard about the edict, Never's and Always's both.
        Assert.Equal(2, asks.Count);
    }

    private sealed class AlwaysRule(EntityHandle entity, List<int> asks) : ITransmitRule
    {
        public TransmitAnswer ShouldTransmit(EntityHandle e, int recipientSlot)
        {
            asks.Add(recipientSlot);
            return e == entity ? TransmitAnswer.Always : TransmitAnswer.Default;
        }
    }

    [Fact]
    public void TheRuleSeesTheDirectoryHandleForTheEdict()
    {
        EntityHandle seen = EntityHandle.None;

        var binding = Binding(new SeeRule(Entity, s => seen = s));

        Assert.Equal(2, binding.ServerShouldTransmit(1, 7));
        Assert.Equal(Entity, seen);
    }

    private sealed class SeeRule(EntityHandle expected, Action<EntityHandle> seen) : ITransmitRule
    {
        public TransmitAnswer ShouldTransmit(EntityHandle e, int recipientSlot)
        {
            seen(e);
            return e == expected ? TransmitAnswer.Default : TransmitAnswer.Never;
        }
    }
}
