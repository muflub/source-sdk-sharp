using System.Collections.Immutable;
using System.Text.Json;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Bestiary;

/// <summary>Which parts of a template's entry a hero may see (E7).</summary>
public sealed record BestiaryReveal(bool Count, bool Life, bool Resistances, bool Style)
{
    public static BestiaryReveal Nothing { get; } = new(false, false, false, false);
}

/// <summary>
/// What the server knows of a robot, for an entry or a health bar: its template, life now
/// and at most, resistance per damage type (percent), the one damage type a champion affix
/// makes it immune to (R25), and its temperament style (B.11). An input: the robot's roll is
/// RU7's.
/// </summary>
public sealed record RobotFacts(
    string Template,
    int Life,
    int MaxLife,
    ImmutableSortedDictionary<string, double> Resistances,
    string? Immunity,
    string Style);

/// <summary>
/// One player's view of a robot: each part the bestiary has not revealed is null, and
/// <see cref="BestiaryRules.Serialise"/> leaves it out of the bytes altogether.
/// </summary>
public sealed record BestiaryView(
    string Template,
    int? Kills,
    int? Life,
    int? MaxLife,
    ImmutableSortedDictionary<string, double>? Resistances,
    string? Immunity,
    string? Style);

/// <summary>
/// The bestiary (ruling L6, default E7; plan_descent_logic RU12): a hero's kills of each
/// template live in its sheet (<see cref="HeroSheet.Bestiary"/>), and more kills show more
/// of the template: the count at 1, life at 15, resistances and immunities at 30, the
/// temperament style at 60, as bestiary.json says. The server builds each player's view;
/// what a player has not earned never leaves it.
/// </summary>
public sealed class BestiaryRules
{
    private readonly DescentContent _content;

    public BestiaryRules(DescentContent content) => _content = content;

    private BestiarySettings S => _content.Bestiary;

    /// <summary>What <paramref name="kills"/> kills of a template reveal.</summary>
    public BestiaryReveal Reveal(int kills) =>
        new(kills >= S.Count, kills >= S.Life, kills >= S.Resistances, kills >= S.Style);

    /// <summary>What <paramref name="sheet"/>'s kills of <paramref name="template"/> reveal.</summary>
    public BestiaryReveal Reveal(HeroSheet sheet, string template) => Reveal(Kills(sheet, template));

    public static int Kills(HeroSheet sheet, string template) => sheet.Bestiary.TryGetValue(template, out var n) ? n : 0;

    /// <summary>The sheet after one more kill of <paramref name="template"/>.</summary>
    public static HeroSheet RecordKill(HeroSheet sheet, string template) =>
        sheet with { Bestiary = sheet.Bestiary.SetItem(template, Kills(sheet, template) + 1) };

    /// <summary>The robot as <paramref name="sheet"/>'s player may see it: every part not yet revealed is null.</summary>
    public BestiaryView View(HeroSheet sheet, RobotFacts robot)
    {
        int kills = Kills(sheet, robot.Template);
        var r = Reveal(kills);
        return new BestiaryView(
            robot.Template,
            r.Count ? kills : null,
            r.Life ? robot.Life : null,
            r.Life ? robot.MaxLife : null,
            r.Resistances ? robot.Resistances : null,
            r.Resistances ? robot.Immunity : null,
            r.Style ? robot.Style : null);
    }

    /// <summary>The view's bytes, as sent: canonical JSON with no field for a hidden part.</summary>
    public static byte[] Serialise(BestiaryView view)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("template", view.Template);
            if (view.Kills is { } kills) w.WriteNumber("kills", kills);
            if (view.Life is { } life) w.WriteNumber("life", life);
            if (view.MaxLife is { } max) w.WriteNumber("maxLife", max);
            if (view.Resistances is { } resists)
            {
                w.WriteStartObject("resist");
                foreach (var (type, pct) in resists) w.WriteNumber(type, pct);
                w.WriteEndObject();
            }
            if (view.Immunity is { } immune) w.WriteString("immune", immune);
            if (view.Style is { } style) w.WriteString("style", style);
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
