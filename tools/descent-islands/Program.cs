// descent-islands: prints every island (floor no spawn reaches) of a compiled
// map, as `ssmap preview <bsp> -game <dir> -movement heavy` computes them. The
// preview's index.json lists only the ten largest islands
// (checks.movement.islands, a Take(10) in PreviewSession.MovementAsync) beside
// the true island_count, but the Descent reach gate (tools/descent-gate) must
// see all of them to tell floor from jump ledges, and the map tools may not be
// changed. So this tool calls the map tools' own assemblies through the same
// steps PreviewCommand and PreviewSession take: load the BSP, mount -game and
// layer the map's pak in front of it, load the props' models, build the scene
// with the preview's default scene options, take its spawns and its content
// bounds widened 64 units in z, build the hull-trace world, and run
// Reachability.Analyze with ReachabilityOptions.Default and the profile
// PreviewCommand.MovementFor gives. Islands are ordered as the preview orders
// them: most nodes first, then by first node. Two internal map-tools helpers
// are reproduced here: VbspCommand.MountGameAsync and VbspHost.SteamFor.

using System.Text.Json;

using SourceSharp.MapCompile;
using SourceSharp.MapFormats.Bsp;
using SourceSharp.MapFormats.Bsp.Structs;
using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapPreview.Cameras;
using SourceSharp.MapPreview.Scene;
using SourceSharp.MapTools.Io;
using SourceSharp.MapTools.Movement;

namespace DescentIslands;

internal static class IslandsTool
{
    private const string Usage = "usage: descent-islands <map.bsp> -game <gamedir> [-movement <class>]  (class defaults to heavy)";

    private static async Task<int> Main(string[] args)
    {
        string? map = null, game = null, playerClass = "heavy";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-game" or "--game" && i + 1 < args.Length && game is null)
            {
                game = args[++i];
            }
            else if (args[i] is "-movement" or "--movement")
            {
                // As the preview: an optional class follows; none means its default.
                playerClass = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : null;
            }
            else if (map is null && !args[i].StartsWith('-'))
            {
                map = args[i];
            }
            else
            {
                await Console.Error.WriteLineAsync(Usage);
                return 2;
            }
        }

        if (map is null || game is null)
        {
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        string mapText = Path.GetFullPath(map);
        string gameDirectory = Path.GetFullPath(game);
        string mapName = Path.GetFileNameWithoutExtension(mapText);
        MovementProfile profile = PreviewCommand.MovementFor(playerClass, PreviewCommand.IsTf2(gameDirectory));
        CancellationToken ct = CancellationToken.None;

        PhysicalFileSystem disk = new("/");
        BspData bsp;
        await using (Stream stream = await disk.OpenReadAsync(VPath.Create(mapText), ct))
        {
            bsp = await BspFile.LoadAsync(stream, ct);
        }

        // VbspCommand.MountGameAsync with VbspHost.SteamFor (both internal).
        IReadOnlyList<VPath> roots = [.. SourceSharp.MapCompile.Program.DefaultSteamRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Select(VPath.Create)];
        ISteamAppLocator? steam = roots.Count == 0 ? null : SteamLibraryFolders.Deferred(disk, roots[0].Directory.Directory);
        ReadOnlyFileSystem readOnly = new(disk);
        VPath gameInfoPath = VPath.Create(Path.Combine(gameDirectory, "gameinfo.txt"));
        GameInfo gameInfo = await GameInfo.LoadAsync(readOnly, gameInfoPath, ct);
        GameContentMounter.Result mounted = await GameContentMounter.MountAsync(
            readOnly,
            gameInfo,
            new GameContentRoots(gameInfoPath.Directory, VPath.Create(Path.GetDirectoryName(gameDirectory)!)) { Steam = steam },
            cancellationToken: ct);

        await using ContentFileSystem gameContent = mounted.Content;

        // PreviewSession.RenderAsync: the map's pak in front of the game.
        ReadOnlyMemory<byte> pakBytes = bsp[BspLump.PakFile].Data;
        ContentFileSystem? pak = null;
        try
        {
            IContentFileSystem content = gameContent;
            if (pakBytes.Length > 0)
            {
                pak = new ContentFileSystem([ArchiveContentMount.Mount(PakArchive.Open(pakBytes, mapName + ".bsp"))]);
                content = new LayeredContent(pak, gameContent);
            }

            // PreviewSession.RenderWithAsync and MovementAsync.
            (IReadOnlyDictionary<string, PreviewModel> models, _) =
                await PreviewModelLoader.LoadAsync(BspSceneBuilder.ModelNames(bsp), content, ct);
            PreviewScene scene = BspSceneBuilder.Build(bsp, models, new PreviewSceneOptions { Hdr = false, IncludeTools = false });
            (HullTraceWorld world, _) = await HullTraceWorld.FromBspAsync(bsp, content, ct);
            (Vec3 min, Vec3 max) = CameraPlanner.ContentBounds(scene);
            ReachSpawn[] spawns = [.. scene.Spawns.Select(s => new ReachSpawn(s.Team, s.Origin))];
            ReachabilityResult result = Reachability.Analyze(
                world, profile, spawns, min - new Vec3(0, 0, 64), max + new Vec3(0, 0, 64), ReachabilityOptions.Default, ct);

            using Stream stdout = Console.OpenStandardOutput();
            using (Utf8JsonWriter json = new(stdout, new JsonWriterOptions { Indented = false }))
            {
                json.WriteStartObject();
                json.WriteNumber("island_count", result.Islands.Count);
                json.WriteStartArray("islands");
                foreach (NodeGroup g in result.Islands.OrderByDescending(g => g.Nodes.Count).ThenBy(g => g.Nodes[0]))
                {
                    json.WriteStartObject();
                    json.WriteNumber("nodes", g.Nodes.Count);
                    WriteVec(json, "sample", g.Sample);
                    WriteVec(json, "min", g.Min);
                    WriteVec(json, "max", g.Max);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteNumber("traps", result.Traps.Count);
                json.WriteNumber("nodes", result.Nodes.Count);
                json.WriteEndObject();
            }

            stdout.WriteByte((byte)'\n');
            return 0;
        }
        finally
        {
            if (pak is not null)
            {
                await pak.DisposeAsync();
            }
        }
    }

    // PreviewSession.V: each coordinate rounded to two places.
    private static void WriteVec(Utf8JsonWriter json, string name, Vec3 v)
    {
        json.WriteStartArray(name);
        json.WriteNumberValue(MathF.Round(v.X, 2));
        json.WriteNumberValue(MathF.Round(v.Y, 2));
        json.WriteNumberValue(MathF.Round(v.Z, 2));
        json.WriteEndArray();
    }
}
