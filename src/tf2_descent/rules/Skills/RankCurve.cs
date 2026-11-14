using System.Text.Json;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Skills;

/// <summary>How a <see cref="RankCurve"/> was written in its table.</summary>
public enum CurveForm
{
    /// <summary><c>4</c>: the same at every rank.</summary>
    Constant,
    /// <summary><c>{ "from": 20, "to": 60 }</c>: linear from rank 1 to max rank (Appendix A's "20 → 60").</summary>
    Linear,
    /// <summary><c>{ "perRank": 3 }</c> or <c>{ "base": -3, "perRank": 4 }</c>: base + perRank · rank ("+3 %/rank", "4r − 3").</summary>
    PerRank,
    /// <summary><c>[0, 1, 1, 2, 2]</c>: one value per rank, as written.</summary>
    Table,
}

/// <summary>
/// A number that depends on a skill's rank: every parameter of a skill's effect, its mana
/// cost, cooldown and per-rank level floor (plan_tf2_rpg 2e, Appendix A). Ranks start at 1.
/// Bonus ranks from gear may take a skill past its max rank (A.1); a curve answers those
/// ranks too: a linear or per-rank curve continues its line, a table continues with its
/// last step, a constant stays.
/// </summary>
public sealed class RankCurve
{
    private readonly double[] _values;   // rank r is _values[r - 1], r = 1..maxRank

    private RankCurve(CurveForm form, double[] values)
    {
        Form = form;
        _values = values;
    }

    public CurveForm Form { get; }

    /// <summary>The ranks the table wrote (the max rank of the row it was read for).</summary>
    public int Ranks => _values.Length;

    /// <summary>The value at <paramref name="rank"/> (1 or more); past the written ranks the curve continues its last step.</summary>
    public double At(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        if (rank <= _values.Length) return _values[rank - 1];
        double last = _values[^1];
        double step = _values.Length >= 2 ? last - _values[^2] : 0;
        return last + step * (rank - _values.Length);
    }

    /// <summary>The value at rank 1.</summary>
    public double First => _values[0];

    /// <summary>The value at the last written rank.</summary>
    public double Last => _values[^1];

    public IReadOnlyList<double> Values => _values;

    public static RankCurve Constant(double value, int ranks) => new(CurveForm.Constant, Fill(ranks, _ => value));

    public static RankCurve Linear(double from, double to, int ranks) =>
        new(CurveForm.Linear, Fill(ranks, r => ranks == 1 ? from : from + (to - from) * (r - 1) / (ranks - 1)));

    public static RankCurve PerRank(double perRank, double @base, int ranks) => new(CurveForm.PerRank, Fill(ranks, r => @base + perRank * r));

    public static RankCurve Of(params double[] values) => new(CurveForm.Table, [.. values]);

    private static double[] Fill(int ranks, Func<int, double> at)
    {
        var v = new double[Math.Max(1, ranks)];
        for (int r = 1; r <= v.Length; r++)
            v[r - 1] = at(r);
        return v;
    }

    /// <summary>
    /// Reads a curve written for a row of <paramref name="ranks"/> ranks: a number, an
    /// array of exactly <paramref name="ranks"/> numbers, <c>{from, to}</c> or
    /// <c>{perRank[, base]}</c>. Anything else is <see cref="ContentRule.Malformed"/>.
    /// </summary>
    public static RankCurve Read(JsonElement e, int ranks, string field, string table, string? row, ContentErrors errors)
    {
        ranks = Math.Max(1, ranks);
        switch (e.ValueKind)
        {
            case JsonValueKind.Number when e.TryGetDouble(out var d) && double.IsFinite(d):
                return Constant(d, ranks);
            case JsonValueKind.Array:
            {
                var list = new List<double>();
                foreach (var v in e.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) && double.IsFinite(x)) list.Add(x);
                    else errors.Add(table, row, ContentRule.Malformed, $"'{field}' must hold only numbers");
                }
                if (list.Count != ranks)
                {
                    errors.Add(table, row, ContentRule.Malformed, $"'{field}' gives {list.Count} ranks, the row has {ranks}");
                    return Constant(list.Count > 0 ? list[0] : 0, ranks);
                }
                return Of([.. list]);
            }
            case JsonValueKind.Object:
            {
                var f = new JsonFields(e, table, row, errors);
                RankCurve curve;
                if (e.TryGetProperty("perRank", out _))
                    curve = PerRank(f.Num("perRank"), f.NumOr("base", 0), ranks);
                else
                    curve = Linear(f.Num("from"), f.Num("to"), ranks);
                f.End();
                return curve;
            }
            default:
                errors.Add(table, row, ContentRule.Malformed, $"'{field}' must be a number, an array of numbers, {{from, to}} or {{perRank}}");
                return Constant(0, ranks);
        }
    }
}
