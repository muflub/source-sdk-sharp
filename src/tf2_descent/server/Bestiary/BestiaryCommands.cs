using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Rules;
using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server.Bestiary;

/// <summary>
/// <c>tf2_descent_bestiary &lt;player&gt; [template] [kills]</c>: a hero's bestiary (UI6's live proof).
/// With a template and a count, sets his kills of that template to it (sv_cheats; unhosted only, as a
/// hosted checkpoint would claim kills that never happened); the counts and robot views follow at the
/// next look (<see cref="RobotViewSender"/>). With none, prints his counts and what each reveals.
/// </summary>
[ConCommand("tf2_descent_bestiary", Help = "tf2_descent_bestiary <player slot> [template] [kills]: print a hero's bestiary, or set his kills of a template (sv_cheats; unhosted only)")]
public sealed class BestiaryCommand(SvCheatsConVar cheats, Heroes heroes, HostService host, TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string template = "", int kills = -1)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_bestiary", slot) is not { Sheet: { } sheet } hero)
            return;

        if (template.Length > 0)
        {
            if (!rules.Content.Robots.TryGet(template, out _))
            {
                log.Warning($"[TF2Descent/Bestiary] no robot template '{template}' (robots.json: {string.Join(' ', rules.Content.Robots.Rows.Select(r => r.Id))})\n");
                return;
            }
            if (kills < 0)
            {
                log.Warning("[TF2Descent/Bestiary] usage: tf2_descent_bestiary <player slot> <template> <kills>\n");
                return;
            }
            if (host.Mode == HostingMode.Hosted)
            {
                log.Warning("[TF2Descent/Bestiary] refused hosted, where the checkpoint would claim kills that never happened\n");
                return;
            }
            var audit = hero.Apply(s => s with { Bestiary = kills == 0 ? s.Bestiary.Remove(template) : s.Bestiary.SetItem(template, kills) });
            if (!audit.Ok)
                return;
            sheet = hero.Sheet!;
        }

        log.Msg(Describe(slot, sheet, rules));
    }

    /// <summary>A hero's counts and what each reveals, one line.</summary>
    public static string Describe(int slot, TF2.Descent.Rules.Hero.HeroSheet sheet, TF2DescentRules rules)
    {
        var s = rules.Content.Bestiary;
        string counts = sheet.Bestiary.IsEmpty
            ? "none"
            : string.Join(", ", sheet.Bestiary.Select(b =>
            {
                var r = rules.Bestiary.Reveal(b.Value);
                string shows = string.Join("+", new[] { r.Count ? "count" : null, r.Life ? "life" : null,
                                                        r.Resistances ? "resistances" : null, r.Style ? "style" : null }
                                                .Where(x => x is not null));
                return string.Create(CultureInfo.InvariantCulture, $"{b.Key} {b.Value} ({(shows.Length == 0 ? "nothing" : shows)})");
            }));
        return string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Bestiary] player {slot}: {counts} | reveals at count {s.Count}, life {s.Life}, resistances {s.Resistances}, style {s.Style}\n");
    }
}
