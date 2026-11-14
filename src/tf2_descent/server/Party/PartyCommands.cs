using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Party;

/// <summary>Every party, its leader, members and invitations, as the dev print shows them; each member by slot and name when he is here.</summary>
[SideService(Side.Server)]
public sealed class PartyReport(IParties parties, IPartyPlayers players)
{
    public string Lines()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"[TF2Descent/Party] {parties.All.Count} parties (at most {parties.MaxSize} members each)\n");
        foreach (var party in parties.All)
        {
            text.Append(CultureInfo.InvariantCulture, $"[TF2Descent/Party] party {party.Id}: leader {Who(party.Leader)}; members");
            foreach (string member in party.Members)
                text.Append(' ').Append(Who(member)).Append(member == party.Leader ? "*" : "");
            text.Append("; invited");
            if (party.Invited.Length == 0)
                text.Append(" nobody");
            foreach (string invited in party.Invited)
                text.Append(' ').Append(Who(invited));
            text.Append('\n');
        }

        foreach (int slot in players.Heroes())
            if (players.HeroOf(slot) is { } hero && parties.Of(hero) is null)
                text.Append(CultureInfo.InvariantCulture, $"[TF2Descent/Party] player {slot} ({players.NameOf(slot)}) is solo\n");
        return text.ToString();
    }

    /// <summary>A member as the print names him: <c>slot:name</c> when he is a hero here, else his member id and "(off level)".</summary>
    private string Who(string member) =>
        parties.HeroOf(member) is { } hero && players.SlotOf(hero) is > 0 and var slot
            ? $"{slot}:{players.NameOf(slot)}"
            : $"{parties.Card(member)?.Name ?? member}(off level)";
}

/// <summary><c>tf2_descent_party</c>: every party, its leader, members and invitations, and the heroes who are solo (dev).</summary>
[ConCommand("tf2_descent_party", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_party: print every party, its leader (*), members and invitations, and the heroes in none")]
public sealed class PartyListCommand(PartyReport report, ILog log)
{
    public void Execute() => log.Msg(report.Lines());
}

/// <summary>
/// <c>tf2_descent_party_make &lt;slot&gt; &lt;slot&gt; [slot] [slot]</c>: forms one party of those heroes,
/// the first leading, with no invitations (dev: other lanes' proofs).
/// </summary>
[ConCommand("tf2_descent_party_make", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_party_make <slot> <slot> [slot] [slot]: form a party of these heroes, the first leading, without the prompts")]
public sealed class PartyMakeCommand(IParties parties, IPartyPlayers players, PartyReport report, ILog log)
{
    public void Execute([ArgRange(1, 64)] int a, [ArgRange(1, 64)] int b, [ArgRange(0, 64)] int c = 0, [ArgRange(0, 64)] int d = 0)
    {
        var heroes = new List<string>();
        foreach (int slot in new[] { a, b, c, d }.Where(s => s > 0))
        {
            if (players.HeroOf(slot) is not { } hero)
            {
                log.Warning($"[TF2Descent/Party] tf2_descent_party_make: player {slot} is no hero\n");
                return;
            }
            heroes.Add(hero);
        }

        _ = Report(parties.Make(heroes));
    }

    private async Task Report(Task<PartyAnswer> made)
    {
        var answer = await made;
        log.Msg(answer.Ok
            ? $"[TF2Descent/Party] tf2_descent_party_make: party {answer.Party?.Id} made\n" + report.Lines()
            : $"[TF2Descent/Party] tf2_descent_party_make: refused {answer.Refusal} {answer.Reason}\n");
    }
}
