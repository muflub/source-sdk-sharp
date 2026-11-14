namespace TF2.Descent.Rules.Content;

/// <summary>The validation rules every table shares, so each table states only which fields they apply to.</summary>
public static class ContentChecks
{
    /// <summary><paramref name="value"/> must lie in [min, max]; else <see cref="ContentRule.OutOfRange"/>.</summary>
    public static bool Range(ContentErrors errors, string table, string? row, string field, double value, double min, double max)
    {
        if (value >= min && value <= max) return true;
        errors.Add(table, row, ContentRule.OutOfRange, $"'{field}' is {value}, outside [{min}, {max}]");
        return false;
    }

    /// <summary>A roll range: low must not exceed high; else <see cref="ContentRule.OutOfRange"/>.</summary>
    public static bool Roll(ContentErrors errors, string table, string? row, string field, double low, double high)
    {
        if (low <= high) return true;
        errors.Add(table, row, ContentRule.OutOfRange, $"'{field}' rolls from {low} to {high}: the low end is above the high end");
        return false;
    }

    /// <summary>
    /// <paramref name="stat"/> must be a row of the stat table (<see cref="ContentRule.UnknownStat"/>),
    /// and an amount given to it must lie inside that stat's bounds (<see cref="ContentRule.OutOfRange"/>).
    /// </summary>
    public static bool Stat(DescentContent content, ContentErrors errors, string table, string? row, string stat, double? amount = null)
    {
        if (!content.TryGet<Table<StatRow>>(ContentTables.Stats, out var stats)) return true; // the stat table's own error stands
        if (!stats.TryGet(stat, out var s))
        {
            errors.Add(table, row, ContentRule.UnknownStat, $"'{stat}' is not a stat");
            return false;
        }
        if (s.Min > s.Max) return true;   // the stat's own range error stands; no cascade
        return amount is not { } a || Range(errors, table, row, stat, a, s.Min, s.Max);
    }

    /// <summary><paramref name="id"/> must be a row of <paramref name="target"/>'s table; else <see cref="ContentRule.DanglingReference"/>.</summary>
    public static bool Reference<TRow>(DescentContent content, ContentErrors errors, string table, string? row, string field, string id, string target)
        where TRow : IContentRow
    {
        if (!content.TryGet<Table<TRow>>(target, out var t)) return true; // the target's own error stands
        if (t.Contains(id)) return true;
        errors.Add(table, row, ContentRule.DanglingReference, $"'{field}' names '{id}', which is not a row of '{target}'");
        return false;
    }
}
