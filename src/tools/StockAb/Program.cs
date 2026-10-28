using System.Globalization;
using System.Text.Json;

using SourceSharp.MapGen;
using SourceSharp.StockAb;

// tools/stock-ab's image and census half. The harness takes the screenshots
// and the entity dumps; this scores them and writes the page.
//
//   compare --out DIR --name NAME [--title T] [--threshold N] [--tolerance P]
//           STOCK_A.tga STOCK_B.tga SHARP_A.tga SHARP_B.tga
//                     writes NAME-{stock,sharp,diff}.png into DIR and appends
//                     the score to DIR/shots.jsonl; exits 1 on a Fail
//   census STOCK.tsv SHARP.json OUT.json
//                     the entity census, SourceSharp's classnames renamed as
//                     MapGen's StockMap renamed them for stock
//   report DIR [key=value...]
//                     DIR/index.html from shots.jsonl and census.json; the
//                     pairs head the page, except threshold= and tolerance=,
//                     which are the settings compare ran with
if (args.Length == 0)
    return Usage();

try
{
    return args[0] switch
    {
        "compare" => Compare(args[1..]),
        "census" => Census(args[1..]),
        "report" => WriteReport(args[1..]),
        _ => Usage(),
    };
}
catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or JsonException)
{
    Console.Error.WriteLine($"stock-ab: {error.Message}");
    return 2;
}

static int Compare(string[] args)
{
    string? outDir = null, name = null, title = null;
    var settings = new ComparisonSettings();
    var files = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--out": outDir = args[++i]; break;
            case "--name": name = args[++i]; break;
            case "--title": title = args[++i]; break;
            case "--threshold": settings = settings with { Threshold = int.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
            case "--tolerance": settings = settings with { TolerancePercent = double.Parse(args[++i], CultureInfo.InvariantCulture) }; break;
            default: files.Add(args[i]); break;
        }
    }

    if (outDir is null || name is null || files.Count != 4)
        return Usage();

    var images = files.Select(f => RgbImage.ReadTga(File.ReadAllBytes(f))).ToArray();
    var score = ShotComparison.Score(images[0], images[1], images[2], images[3], settings, out var heatmap);

    Directory.CreateDirectory(outDir);
    File.WriteAllBytes(Path.Combine(outDir, $"{name}-stock.png"), images[0].ToPng());
    File.WriteAllBytes(Path.Combine(outDir, $"{name}-sharp.png"), images[2].ToPng());
    File.WriteAllBytes(Path.Combine(outDir, $"{name}-diff.png"), heatmap.ToPng());

    var record = new ShotRecord(name, title ?? name, $"{name}-stock.png", $"{name}-sharp.png", $"{name}-diff.png", score);
    string line = JsonSerializer.Serialize(record, Report.Json);
    File.AppendAllText(Path.Combine(outDir, "shots.jsonl"), line + "\n");
    Console.WriteLine(line);
    return score.Verdict == Verdict.Fail ? 1 : 0;
}

static int Census(string[] args)
{
    if (args.Length != 3)
        return Usage();

    var stock = EntityCensus.ReadStock(File.ReadAllText(args[0]));
    var sharp = EntityCensus.ReadSharp(File.ReadAllText(args[1]));
    var census = EntityCensus.Compare(stock, sharp, StockMap.Renames);
    File.WriteAllText(args[2], JsonSerializer.Serialize(census, Report.Json));

    var differing = census.Where(c => c.Stock != c.Sharp || c.Moved > 0).ToList();
    Console.WriteLine($"census: stock {stock.Count} entities, SourceSharp {sharp.Count}; "
                      + $"{differing.Count} of {census.Count} classnames differ");
    foreach (var c in differing)
        Console.WriteLine($"  {c.ClassName}: stock {c.Stock}, SourceSharp {c.Sharp}, moved {c.Moved}");
    return 0;
}

static int WriteReport(string[] args)
{
    if (args.Length < 1)
        return Usage();

    string dir = args[0];
    var run = args[1..]
        .Select(a => a.Split('=', 2))
        .Where(kv => kv.Length == 2)
        .ToDictionary(kv => kv[0], kv => kv[1]);

    string shotsPath = Path.Combine(dir, "shots.jsonl");
    var shots = File.Exists(shotsPath)
        ? File.ReadAllLines(shotsPath).Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<ShotRecord>(l, Report.Json)!).ToList()
        : [];

    string censusPath = Path.Combine(dir, "census.json");
    var census = File.Exists(censusPath)
        ? JsonSerializer.Deserialize<List<ClassCensus>>(File.ReadAllText(censusPath), Report.Json)!
        : [];

    var settings = new ComparisonSettings();
    if (run.Remove("threshold", out string? threshold))
        settings = settings with { Threshold = int.Parse(threshold, CultureInfo.InvariantCulture) };
    if (run.Remove("tolerance", out string? tolerance))
        settings = settings with { TolerancePercent = double.Parse(tolerance, CultureInfo.InvariantCulture) };

    File.WriteAllText(Path.Combine(dir, "index.html"), Report.Html(shots, census, run, settings));
    Console.WriteLine(Path.Combine(dir, "index.html"));
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: SourceSharp.StockAb compare --out DIR --name NAME [--title T] [--threshold N] [--tolerance P] STOCK_A STOCK_B SHARP_A SHARP_B");
    Console.Error.WriteLine("       SourceSharp.StockAb census STOCK.tsv SHARP.json OUT.json");
    Console.Error.WriteLine("       SourceSharp.StockAb report DIR [key=value...]");
    return 2;
}
