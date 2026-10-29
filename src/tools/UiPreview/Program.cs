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
//   --content DIR a game dir to read HUD art from (--content, user ruling
//                 2026-10-26): PreviewUiTextureLibrary decodes its .vmts
//   --mod ID      draw as the mod ID does in game: its theme, its fonts and
//                 HUD art from what its gameinfo.txt mounts (VPKs too), to
//                 ui-preview/ID
//   --quiet       no summary line

string? root = null, outDir = null, only = null, content = null, mod = null;
bool png = false, quiet = false, lint = false, docs = false, icons = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--root": root = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--only": only = args[++i]; break;
        case "--content": content = args[++i]; break;
        case "--mod": mod = args[++i]; break;
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
string config = RepoRoot.Config(AppContext.BaseDirectory);
outDir ??= mod is null
    ? Path.Combine(root, "bin", config, "ui-preview")
    : Path.Combine(root, "bin", config, "ui-preview", mod);
if (icons)
    return IconAtlasBuilder.Write(root, outDir);

// The mod's game dir as `make setup` lays it out (mods.yaml's default game_dir, mods/{id}).
string? modDir = mod is null ? null : Path.Combine(root, "bin", config, "mods", mod);
if (modDir is not null && !File.Exists(Path.Combine(modDir, "gameinfo.txt")))
{
    Console.Error.WriteLine($"ui-preview: --mod {mod}: no gameinfo.txt in {modDir} (run make setup)");
    return 2;
}
var options = new PreviewOptions(root, outDir, png, quiet, only, content, modDir);
if (modDir is not null && !quiet && options.Files() is { } files)
    foreach (string mount in files.Mounted)
        Console.WriteLine($"ui-preview: --mod {mod} mounts {mount}");
return docs ? DocExamples.Write(options) : lint ? LintRun.Run(options) : new PreviewRun(options).Run();
