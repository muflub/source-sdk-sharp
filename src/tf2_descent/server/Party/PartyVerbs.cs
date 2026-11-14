using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The party's client verbs (WO9), each acting for the player who sent it and decided by the
/// party service; a refusal is told to him as <c>#Descent_Party_&lt;code&gt;</c> (its reason as the
/// argument), what was done is told by <see cref="PartyNotices"/>:
/// <list type="bullet">
/// <item><c>tf2_descent_party_invite &lt;player&gt;</c>: invite a hero on this server, named by
/// slot, by his name, or by a part of it only he has.</item>
/// <item><c>tf2_descent_party_accept</c> / <c>_decline</c>: answer the newest invitation.</item>
/// <item><c>tf2_descent_party_leave</c>: leave the party.</item>
/// <item><c>tf2_descent_party_kick &lt;player&gt;</c>: the leader puts a member out (not one who is down).</item>
/// <item><c>tf2_descent_party_disband</c>: the leader ends the party.</item>
/// </list>
/// A verb from a player who is not a hero passes on.
/// </summary>
[SideService(Side.Server)]
public sealed class PartyVerbs(IParties parties, IPartyPlayers players, ClientMessages messages, ILog log)
    : IClientCommandHook, IOrderedHook
{
    public const string InviteVerb = "tf2_descent_party_invite";
    public const string AcceptVerb = "tf2_descent_party_accept";
    public const string DeclineVerb = "tf2_descent_party_decline";
    public const string LeaveVerb = "tf2_descent_party_leave";
    public const string KickVerb = "tf2_descent_party_kick";
    public const string DisbandVerb = "tf2_descent_party_disband";

    /// <summary>The refusal notice's token for a code.</summary>
    public static string RefusalToken(string refusal) => "#Descent_Party_" + refusal;

    /// <summary>The code of a player argument that names no hero, or more than one.</summary>
    public const string NoSuchPlayer = PartyRefusals.NoSuchPlayer;

    public int HookOrder => Parties.Order + 3;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command is not (InviteVerb or AcceptVerb or DeclineVerb or LeaveVerb or KickVerb or DisbandVerb))
            return false;
        int slot = (int)(player.EntryIndex & 0xFFFF);
        if (players.HeroOf(slot) is null)
            return false;
        _ = Act(slot, command, args.Length > 0 ? string.Join(' ', args.ToArray()) : "");
        return true;
    }

    /// <summary>Carries out <paramref name="verb"/> for the hero in <paramref name="slot"/>; answers what the party service said.</summary>
    public async Task<PartyAnswer> Act(int slot, string verb, string argument)
    {
        if (players.HeroOf(slot) is not { } hero)
            return PartyAnswer.Refused(PartyRefusals.NotAHero);

        PartyAnswer answer;
        try
        {
            answer = verb switch
            {
                InviteVerb => Find(argument) is { } target && players.HeroOf(target) is { } other
                    ? await parties.Invite(hero, other)
                    : PartyAnswer.Refused(NoSuchPlayer, argument),
                AcceptVerb => await parties.Accept(hero),
                DeclineVerb => await parties.Decline(hero),
                LeaveVerb => await parties.Leave(hero),
                KickVerb => await Kick(slot, hero, argument),
                DisbandVerb => await parties.Disband(hero),
                _ => PartyAnswer.Refused(PartyRefusals.NotAHero),
            };
        }
        catch (Exception e)
        {
            log.Warning($"[TF2Descent/Party] {verb} for player {slot} failed: {e.Message}\n");
            answer = PartyAnswer.Refused(PartyRefusals.Host, e.Message);
        }

        log.Msg(answer.Ok
            ? $"[TF2Descent/Party] player {slot} {verb} {argument}: done\n"
            : $"[TF2Descent/Party] player {slot} {verb} {argument}: refused {answer.Refusal} {answer.Reason}\n");
        if (!answer.Ok)
            messages.Notice(slot, RefusalToken(answer.Refusal), answer.Reason.Length > 0 ? answer.Reason : argument);
        return answer;
    }

    private async Task<PartyAnswer> Kick(int slot, string hero, string argument)
    {
        if (parties.Of(hero) is not { } party)
            return PartyAnswer.Refused(PartyRefusals.NotInParty);
        string? member = null;
        if (Find(argument) is { } target && players.HeroOf(target) is { } other)
        {
            member = parties.MemberId(other);
            if (member is not null && party.Has(member) && players.IsDown(target))
                return PartyAnswer.Refused(PartyRefusals.TargetDown, players.NameOf(target));
        }
        else
        {
            // A member not on this server, by the name the host's card gives him.
            member = party.Members.FirstOrDefault(m => parties.HeroOf(m) is null
                                                       && string.Equals(parties.Card(m)?.Name, argument, StringComparison.OrdinalIgnoreCase));
        }

        return member is null ? PartyAnswer.Refused(NoSuchPlayer, argument) : await parties.Kick(hero, member);
    }

    /// <summary>
    /// The hero's slot that <paramref name="argument"/> names: a slot number, a name (any case),
    /// or a part of a name only one hero's has; null for none or more than one.
    /// </summary>
    public int? Find(string argument)
    {
        string text = argument.Trim();
        if (text.Length == 0)
            return null;
        var heroes = players.Heroes();
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            return heroes.Contains(n) ? n : null;
        var exact = heroes.Where(s => string.Equals(players.NameOf(s), text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return exact[0];
        var part = heroes.Where(s => players.NameOf(s).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        return part.Count == 1 ? part[0] : null;
    }
}
