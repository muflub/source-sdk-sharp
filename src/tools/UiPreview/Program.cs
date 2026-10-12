using SourceSharp.UiPreview;

// ui-preview: renders every UI sample (plan_client_ui §7). Run through
// tools/ui-preview or `make ui-preview [PNG=1] [WATCH=1]`, which build it first.
//
//   --root DIR    the repo (default: found upward from this assembly)
//   --out DIR     where pictures go (default: <root>/bin/<config>/ui-preview)
//   --png         also draw PNGs with SkiaSharp, over the world backdrops
//   --only NAME   one screen or gallery part
//   --lint        lint every sample at every size; exit 1 on any defect
//   --docs        draw docs/ui/img's do/don't pairs and their lint
//   --icons       rasterise src/client/ui/icons into its atlas (make ui-icons)
//   --quiet       no summary line

string? root = null, outDir = null, only = null;
bool png = false, quiet = false, lint = false, docs = false, icons = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--root": root = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--only": only = args[++i]; break;
        case "--png": png = true; break;
        case "--quiet": quiet = true; break;
        case "--lint": lint = true; break;
        case "--docs": docs = true; break;
        case "--icons": icons = true; break;
        default:
            Console.Error.WriteLine($"ui-preview: unknown argument {args[i]}");
            return 2;
    }
}

root ??= RepoRoot.Find(AppContext.BaseDirectory);
outDir ??= Path.Combine(root, "bin", RepoRoot.Config(AppContext.BaseDirectory), "ui-preview");
if (icons)
    return IconAtlasBuilder.Write(root, outDir);
var options = new PreviewOptions(root, outDir, png, quiet, only);
return docs ? DocExamples.Write(options) : lint ? LintRun.Run(options) : new PreviewRun(options).Run();
