using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Bestiary;

/// <summary>
/// bestiary.json (ruling L6, default E7): the kills of a robot template a hero needs before
/// each part of its entry shows: the kill count, its life numbers, its resistances and
/// immunities, its temperament style.
/// </summary>
public sealed record BestiarySettings(int Count, int Life, int Resistances, int Style);

public sealed class BestiaryTable : SingleTable<BestiarySettings>
{
    public override string Name => ContentTables.Bestiary;

    protected override BestiarySettings Read(JsonFields f)
    {
        var at = f.Obj("revealAt");
        var s = new BestiarySettings(at.Int("count"), at.Int("life"), at.Int("resistances"), at.Int("style"));
        at.End();
        return s;
    }

    /// <summary>Each threshold is at least 1 and none is below the one before it: an entry fills in, it never skips back.</summary>
    protected override void Validate(DescentContent content, BestiarySettings s, ContentErrors errors)
    {
        if (ContentChecks.Range(errors, Name, null, "revealAt.count", s.Count, 1, 1_000_000)
            && ContentChecks.Range(errors, Name, null, "revealAt.life", s.Life, s.Count, 1_000_000)
            && ContentChecks.Range(errors, Name, null, "revealAt.resistances", s.Resistances, s.Life, 1_000_000))
            ContentChecks.Range(errors, Name, null, "revealAt.style", s.Style, s.Resistances, 1_000_000);
    }
}
