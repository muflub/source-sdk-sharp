using SourceSharp.Abi;

using TF2.Descent.Rules;
using TF2.Descent.Server;
using TF2.Descent.Server.Party;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// The party's server pieces over fakes (WO9): the in-memory parties, heroes in slots named
/// <c>hero&lt;slot&gt;</c> with player names, the views and notices on the chain, the verbs, and a
/// wire that records every user message.
/// </summary>
internal sealed class PartyKit
{
    public readonly Players Heroes = new();
    public readonly RecordingEngine Wire = new();
    public readonly InMemoryParties Parties;
    public readonly PartySync Sync;
    public readonly PartyNotices Notices;
    public readonly PartyVerbs Verbs;
    public readonly PartyRoster Roster;
    private readonly List<IPartyHook> _chain = [];

    public PartyKit(params int[] slots)
    {
        Parties = new InMemoryParties(new TF2DescentRules(), new Chain(_chain));
        Sync = new PartySync(Parties, Heroes, Wire);
        Notices = new PartyNotices(Parties, Heroes, new ClientMessages(Wire));
        _chain.Add(Sync);
        _chain.Add(Notices);
        Verbs = new PartyVerbs(Parties, Heroes, new ClientMessages(Wire), new RecordingLog());
        Roster = new PartyRoster(Parties, Heroes);
        foreach (int slot in slots.Length == 0 ? [1, 2, 3] : slots)
            Heroes.Add(slot);
    }

    public static string Hero(int slot) => "hero" + slot;

    /// <summary>The verb as the client sends it, completed.</summary>
    public PartyAnswer Act(int slot, string verb, string argument = "")
    {
        var task = Verbs.Act(slot, verb, argument);
        Assert.True(task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    public IReadOnlyList<(string Key, int Player, byte[] Bytes)> Sent =>
        [.. Wire.Calls.Where(c => c.Member == "UserMessages.Send").Select(c => (c.ArgAs<string>(0), c.ArgAs<int>(2), c.ArgAs<byte[]>(4)))];

    private static string KeyOf<T>() where T : IUserMessagePayload<T> => T.Key;

    /// <summary>The notices sent to <paramref name="slot"/>, as "token [arg]".</summary>
    public List<string> NoticesTo(int slot) =>
        [.. Sent.Where(s => s.Player == slot && s.Key == KeyOf<Notice>()).Select(s =>
        {
            var reader = new UserMessageReader(s.Bytes);
            var n = Notice.Read(ref reader);
            return n.Arg.Length > 0 ? $"{n.Text} [{n.Arg}]" : n.Text;
        })];

    /// <summary>The last party view sent to <paramref name="slot"/>; null when none was.</summary>
    public PartyViewState? ViewOf(int slot) =>
        Sent.LastOrDefault(s => s.Player == slot && s.Key == KeyOf<PartyView>()) is { Bytes: { } bytes }
            ? Decode(bytes)
            : null;

    private static PartyViewState Decode(byte[] bytes)
    {
        var reader = new UserMessageReader(bytes);
        return PartyView.Read(ref reader).View();
    }

    public int ViewsTo(int slot) => Sent.Count(s => s.Player == slot && s.Key == KeyOf<PartyView>());

    private sealed class Chain(List<IPartyHook> hooks) : IHooks<IPartyHook>
    {
        public IReadOnlyList<IPartyHook> All => hooks;
        public void Each(string what, Action<IPartyHook> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>Heroes by slot: ledger name <c>hero&lt;slot&gt;</c>, player name <c>Name&lt;slot&gt;</c>, a Soldier of level 10 + slot.</summary>
    internal sealed class Players : IPartyPlayers, IPartySlots
    {
        private readonly SortedSet<int> _slots = [];
        public HashSet<int> Down { get; } = [];

        public void Add(int slot) => _slots.Add(slot);
        public void Remove(int slot) => _slots.Remove(slot);

        public IReadOnlyList<int> Heroes() => [.. _slots];
        public string? HeroOf(int slot) => _slots.Contains(slot) ? Hero(slot) : null;
        public int SlotOf(string hero) => _slots.FirstOrDefault(s => Hero(s) == hero);
        public string NameOf(int slot) => "Name" + slot;
        public int ClassOf(int slot) => 3;
        public int LevelOf(int slot) => 10 + slot;
        public bool IsDown(int slot) => Down.Contains(slot);
    }
}
