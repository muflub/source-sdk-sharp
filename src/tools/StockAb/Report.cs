using System.Net;
using System.Text;
using System.Text.Json;

namespace SourceSharp.StockAb;

/// <summary>One camera's line in shots.jsonl, as the compare command writes it.</summary>
public sealed record ShotRecord(
    string Name, string Title, string Stock, string Sharp, string Diff, ShotScore Score);

/// <summary>
/// The run's page: a verdict per camera with the two screenshots and the
/// difference beside each other, then the entity census.
/// </summary>
public static class Report
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string Html(
        IReadOnlyList<ShotRecord> shots, IReadOnlyList<ClassCensus> census,
        IReadOnlyDictionary<string, string> run, ComparisonSettings settings)
    {
        int passed = shots.Count(s => s.Score.Verdict == Verdict.Pass);
        int failed = shots.Count(s => s.Score.Verdict == Verdict.Fail);
        int unsure = shots.Count(s => s.Score.Verdict == Verdict.Inconclusive);
        var mismatched = census.Where(c => c.Stock != c.Sharp || c.Moved > 0).ToList();

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>Stock vs SourceSharp</title><style>")
            .Append(":root{--bg:#f6f6f4;--fg:#1d1d1b;--muted:#6b6b66;--card:#fff;--line:#deded9;--pass:#1f7a3a;--fail:#b3261e;--unsure:#8a6d00}")
            .Append("@media (prefers-color-scheme: dark){:root:not([data-theme=\"light\"]){--bg:#161615;--fg:#ececea;--muted:#a3a39e;--card:#1f1f1d;--line:#34342f;--pass:#5cc97c;--fail:#ff8a80;--unsure:#e6c34d}}")
            .Append(":root[data-theme=\"dark\"]{--bg:#161615;--fg:#ececea;--muted:#a3a39e;--card:#1f1f1d;--line:#34342f;--pass:#5cc97c;--fail:#ff8a80;--unsure:#e6c34d}")
            .Append("body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,sans-serif}")
            .Append("main{max-width:1400px;margin:0 auto;padding:24px 16px}h1{font-size:24px;margin:0 0 4px}")
            .Append(".muted{color:var(--muted)}table{border-collapse:collapse;width:100%;margin:12px 0 28px;font-variant-numeric:tabular-nums}")
            .Append("th,td{text-align:left;padding:6px 10px;border-bottom:1px solid var(--line)}td.n{text-align:right}")
            .Append(".Pass{color:var(--pass);font-weight:600}.Fail{color:var(--fail);font-weight:600}.Inconclusive{color:var(--unsure);font-weight:600}")
            .Append(".shot{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px;margin:0 0 18px}")
            .Append(".row{display:grid;grid-template-columns:repeat(3,1fr);gap:8px}.row img{width:100%;height:auto;display:block;border-radius:4px}")
            .Append(".row figcaption{font-size:12px;color:var(--muted)}@media (max-width:800px){.row{grid-template-columns:1fr}}")
            .Append(".wrap{overflow-x:auto}</style></head><body><main>");

        html.Append("<h1>Stock hl2mp vs SourceSharp on ss_sandbox</h1><p class=\"muted\">");
        foreach (var (key, value) in run)
            html.Append(E(key)).Append(": ").Append(E(value)).Append(" · ");
        html.Append("threshold ").Append(settings.Threshold).Append("/255, tolerance ")
            .Append(settings.TolerancePercent.ToString("0.##")).Append("% of still pixels</p>");

        html.Append("<p><span class=\"Pass\">").Append(passed).Append(" pass</span> · <span class=\"Fail\">")
            .Append(failed).Append(" fail</span> · <span class=\"Inconclusive\">").Append(unsure)
            .Append(" inconclusive</span> · ").Append(mismatched.Count).Append(" of ").Append(census.Count)
            .Append(" classnames differ in the census</p>");

        html.Append("<div class=\"wrap\"><table><thead><tr><th>Camera</th><th>Verdict</th><th>Differing</th><th>Mean diff</th><th>Luma ratio</th><th>Still</th></tr></thead><tbody>");
        foreach (var shot in shots)
        {
            html.Append("<tr><td><a href=\"#").Append(E(shot.Name)).Append("\">").Append(E(shot.Title)).Append("</a></td>")
                .Append("<td class=\"").Append(shot.Score.Verdict).Append("\">").Append(shot.Score.Verdict).Append("</td>")
                .Append("<td class=\"n\">").Append(shot.Score.DifferingPercent.ToString("0.00")).Append("%</td>")
                .Append("<td class=\"n\">").Append(shot.Score.MeanAbsDiffPercent.ToString("0.00")).Append("%</td>")
                .Append("<td class=\"n\">").Append(shot.Score.LumaRatio.ToString("0.000")).Append("</td>")
                .Append("<td class=\"n\">").Append(shot.Score.StillPercent.ToString("0.0")).Append("%</td></tr>");
        }
        html.Append("</tbody></table></div>");

        html.Append("<h2>Entity census</h2><p class=\"muted\">Server entities after the map loaded; SourceSharp's classnames mapped as the stock map renamed them. Moved: entities with no counterpart of the same class within one unit.</p>");
        html.Append("<div class=\"wrap\"><table><thead><tr><th>Classname</th><th>Stock</th><th>SourceSharp</th><th>Moved</th></tr></thead><tbody>");
        foreach (var c in mismatched)
        {
            html.Append("<tr><td>").Append(E(c.ClassName)).Append("</td><td class=\"n\">").Append(c.Stock)
                .Append("</td><td class=\"n\">").Append(c.Sharp).Append("</td><td class=\"n\">").Append(c.Moved).Append("</td></tr>");
        }
        html.Append("</tbody></table></div>");

        html.Append("<h2>Cameras</h2>");
        foreach (var shot in shots)
        {
            html.Append("<section class=\"shot\" id=\"").Append(E(shot.Name)).Append("\"><h3>").Append(E(shot.Title))
                .Append(" <span class=\"").Append(shot.Score.Verdict).Append("\">").Append(shot.Score.Verdict).Append("</span></h3>")
                .Append("<div class=\"row\">")
                .Append(Figure(shot.Stock, "Stock hl2mp"))
                .Append(Figure(shot.Sharp, "SourceSharp"))
                .Append(Figure(shot.Diff, "Difference: red differs, blue moved"))
                .Append("</div></section>");
        }

        html.Append("</main></body></html>");
        return html.ToString();
    }

    private static string Figure(string path, string caption) =>
        $"<figure style=\"margin:0\"><img loading=\"lazy\" src=\"{E(path)}\" alt=\"{E(caption)}\"><figcaption>{E(caption)}</figcaption></figure>";

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
