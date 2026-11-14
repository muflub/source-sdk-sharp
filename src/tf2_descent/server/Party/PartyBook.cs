using SourceSharp.Abi;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The parties as this server holds them, by id, and the changes between what it held and what
/// it holds now: both party services write a party through <see cref="Commit"/>, which tells the
/// <see cref="IPartyHook"/> chain each change the new state makes, so the in-memory parties and the
/// host's tell the same changes for the same moves.
/// </summary>
public sealed class PartyBook(IHooks<IPartyHook> hooks)
{
    private readonly Dictionary<string, PartyInfo> _parties = new(StringComparer.Ordinal);
    private readonly List<PartyInfo> _list = [];

    /// <summary>Every party, in the order each was first held.</summary>
    public IReadOnlyList<PartyInfo> All => _list;

    public PartyInfo? ById(string id) => _parties.GetValueOrDefault(id);

    /// <summary>The party that has <paramref name="member"/>; null for none.</summary>
    public PartyInfo? WithMember(string member) => _list.FirstOrDefault(p => p.Has(member));

    /// <summary>The parties that invite <paramref name="member"/>, in the order they were held.</summary>
    public IReadOnlyList<PartyInfo> Inviting(string member) => [.. _list.Where(p => p.Invites(member))];

    /// <summary>
    /// Holds <paramref name="now"/> as party <paramref name="id"/> (null: it is gone) and tells each
    /// change from what was held. <paramref name="kicked"/> names a member who left by a kick.
    /// </summary>
    public void Commit(string id, PartyInfo? now, string? kicked = null)
    {
        var before = _parties.GetValueOrDefault(id);
        if (now is null)
        {
            if (before is null)
                return;
            _parties.Remove(id);
            _list.Remove(before);
            foreach (string invited in before.Invited)
                Tell(PartyChangeKind.Declined, before, invited);
            Tell(PartyChangeKind.Disbanded, before, before.Leader);
            return;
        }

        if (before is null)
            _list.Add(now);
        else
            _list[_list.IndexOf(before)] = now;
        _parties[id] = now;

        foreach (var change in Changes(before, now, kicked))
            Tell(change.Kind, now, change.Member);
    }

    /// <summary>Forgets every party without telling anyone (a new process state, the facts' reset).</summary>
    public void Clear()
    {
        _parties.Clear();
        _list.Clear();
    }

    /// <summary>The changes from <paramref name="before"/> (null: no such party) to <paramref name="now"/>, in the order they are told.</summary>
    public static IEnumerable<(PartyChangeKind Kind, string Member)> Changes(PartyInfo? before, PartyInfo now, string? kicked)
    {
        if (before is null)
            yield return (PartyChangeKind.Formed, now.Leader);
        var had = before?.Members ?? [];
        foreach (string gone in had.Where(m => !now.Has(m)))
            yield return (gone == kicked ? PartyChangeKind.Kicked : PartyChangeKind.Left, gone);
        foreach (string joined in now.Members.Where(m => !had.Contains(m) && (before is not null || m != now.Leader)))
            yield return (PartyChangeKind.Joined, joined);
        if (before is not null && before.Leader != now.Leader)
            yield return (PartyChangeKind.LeaderChanged, now.Leader);
        var invited = before?.Invited ?? [];
        foreach (string asked in now.Invited.Where(m => !invited.Contains(m)))
            yield return (PartyChangeKind.Invited, asked);
        foreach (string answered in invited.Where(m => !now.Invites(m) && !now.Has(m)))
            yield return (PartyChangeKind.Declined, answered);
    }

    private void Tell(PartyChangeKind kind, PartyInfo party, string member)
    {
        var change = new PartyChange(kind, party.Id, member, party);
        hooks.Each(nameof(IPartyHook.OnPartyChanged), h => h.OnPartyChanged(in change));
    }
}
