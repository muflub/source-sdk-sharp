using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Skills;

namespace TF2.Descent.Rules.Spells;

/// <summary>
/// A cross-class spell Merasmus sells as tomes and the dungeon drops as tomes and scrolls
/// (plan_tf2_rpg 2e, R27, D15): an action under R17 (mana, its own damage of exactly one
/// type, no weapon, no family), built from the skills' primitives, with a rising Smarts
/// requirement per rank read from Now. The stock Halloween spellbook is the reference for
/// what each does; the numbers are this table's.
/// </summary>
public sealed record SpellDef(
    string Id,
    string Name,
    int MaxRank,
    string? DamageType,
    string? Scope,
    RankCurve ManaCost,
    RankCurve Cooldown,
    RankCurve Smarts,
    IReadOnlyList<EffectDef> Effect) : IContentRow;

/// <summary>
/// spells.json. Load errors: R17's (an item base named, an id or name that is an item
/// base's, a family named at all, no damage type or an unknown one), an id or name that is a
/// skill's, a max rank other than skill_settings' spell rank cap, a Smarts requirement that
/// falls from one rank to the next, and the effects' own rules.
/// </summary>
public sealed class SpellTable : RowTable<SpellDef>
{
    public override string Name => ContentTables.Spells;

    protected override SpellDef ReadRow(JsonFields f)
    {
        int maxRank = f.Int("maxRank");
        int ranks = Math.Clamp(maxRank, 1, 100);
        return new SpellDef(f.Str("id"), f.Str("name"), maxRank, f.StrOr("damageType", null), f.StrOr("scope", null),
            SkillChecks.Curve(f, "manaCost", ranks, 0), SkillChecks.Curve(f, "cooldown", ranks, 0),
            SkillChecks.Curve(f, "smarts", ranks, 0), SkillChecks.ReadEffects(f, ranks));
    }

    protected override void ValidateRow(DescentContent content, SpellDef row, ContentErrors errors)
    {
        string t = Name;
        SkillChecks.Identity(content, errors, t, row.Id, row.Name);
        if (content.TryGet<Table<SkillDef>>(ContentTables.Skills, out var skills))
        {
            if (skills.Contains(row.Id))
                errors.Add(t, row.Id, ContentRule.NameCollision, $"id '{row.Id}' is a skill's");
            if (skills.Rows.FirstOrDefault(s => ItemBaseKeys.Normal(s.Name) == ItemBaseKeys.Normal(row.Name)) is { } same)
                errors.Add(t, row.Id, ContentRule.NameCollision, $"name '{row.Name}' is skill '{same.Id}''s");
        }
        if (content.TryGet<SkillSettings>(ContentTables.SkillSettings, out var s))
            ContentChecks.Range(errors, t, row.Id, "maxRank", row.MaxRank, s.SpellMaxRank, s.SpellMaxRank);
        SkillChecks.DamageType(content, errors, t, row.Id, row.DamageType, required: true);
        if (row.Scope is { } scope)
            SkillChecks.FamilyOnAction(content, errors, t, row.Id, "scope", scope);
        SkillChecks.Costs(errors, t, row.Id, row.ManaCost, row.Cooldown, row.MaxRank, cast: true);
        double previous = 0;
        for (int r = 1; r <= Math.Clamp(row.MaxRank, 1, 100); r++)
        {
            if (!ContentChecks.Range(errors, t, row.Id, $"smarts[{r}]", row.Smarts.At(r), previous, 1000)) break;
            previous = row.Smarts.At(r);
        }
        SkillChecks.Effects(content, errors, t, row.Id, row.Effect, row.MaxRank, cast: true);
    }
}
