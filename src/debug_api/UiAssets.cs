using System.Reflection;
using System.Runtime.CompilerServices;

namespace Debug.Api;

/// <summary>
/// The web interface's files, as the HTTP server hands them out.
///
/// They ship inside the assembly rather than beside it. This assembly is loaded
/// by name out of the managed directory and nothing references it, so a folder
/// of loose files next to it is a folder that can be half-deployed, stale, or
/// missing entirely — and the failure would look like a broken UI rather than a
/// missing file. An embedded resource cannot drift from the code that serves it.
///
/// A debug build looks on disk first, at the directory the source was compiled
/// from. That is the whole iteration loop for a UI: edit the file, reload the
/// page. Reaching for the source path would be indefensible in a shipped build,
/// which is exactly why it is compiled out of one.
/// </summary>
internal static class UiAssets
{
    /// <summary>What a path with no file on it means.</summary>
    private const string DefaultAsset = "index.html";

    /// <summary>The asset name that answers with a build stamp rather than a file.</summary>
    private const string StampAsset = "stamp";

    /// <summary>
    /// Answers a request under /ui.
    ///
    /// The transport has already trimmed the trailing slash, so "/ui" and "/ui/"
    /// arrive the same way and both mean the page.
    /// </summary>
    public static HttpReply Serve(string path)
    {
        string asset = path.Length <= 3 ? DefaultAsset : path[4..];

        // Everything is served out of one flat directory, so any separator at
        // all means the request is trying to leave it. Rejecting the character
        // rather than resolving the path is the version that cannot be wrong.
        if (asset.Length == 0)
            asset = DefaultAsset;

        if (asset.Contains('/') || asset.Contains('\\') || asset.Contains(".."))
            return new HttpReply(404, "{\"error\":\"no such asset\"}");

        if (asset == StampAsset)
            return new HttpReply(200, $"{{\"stamp\":{DebugApiServer.Quote(Stamp())},\"live\":{(LiveFromDisk ? "true" : "false")}}}");

        string? content = FromDisk(asset) ?? FromResource(asset);

        return content is null
            ? new HttpReply(404, $"{{\"error\":\"no such asset\",\"asset\":{DebugApiServer.Quote(asset)}}}")
            : new HttpReply(200, content, ContentTypeFor(asset));
    }

    /// <summary>
    /// What build the page is looking at.
    ///
    /// A debug build serving from disk answers with when those files last
    /// changed, so a page can notice a rebuild and reload itself — which is the
    /// difference between editing the interface and editing it, alt-tabbing, and
    /// remembering to press F5. Anything else answers with a constant: an
    /// embedded copy cannot change under a running game.
    /// </summary>
    private static string Stamp()
    {
#if DEBUG
        try
        {
            string directory = Path.Combine(SourceDirectory(), "ui");

            if (Directory.Exists(directory))
            {
                long newest = 0;
                long total = 0;

                // Size as well as time: two writes inside the same tick of the
                // filesystem's resolution are a real thing when a bundler writes
                // several files at once.
                foreach (string file in Directory.GetFiles(directory))
                {
                    var info = new FileInfo(file);
                    newest = Math.Max(newest, info.LastWriteTimeUtc.Ticks);
                    total += info.Length;
                }

                return $"{newest:x}-{total:x}";
            }
        }
        catch
        {
            // Unreadable or gone, which is the embedded case below.
        }
#endif

        return "embedded";
    }

    /// <summary>Whether this build watches the tree, and so can be watched back.</summary>
    private static bool LiveFromDisk
    {
        get
        {
#if DEBUG
            try
            {
                return Directory.Exists(Path.Combine(SourceDirectory(), "ui"));
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }

    internal static string? FromResource(string asset)
    {
        var assembly = typeof(UiAssets).Assembly;

        // The build names an embedded file <root namespace>.ui.<file>, and the
        // root namespace is the assembly's name. Read from the assembly rather
        // than spelled out, so renaming the project cannot orphan the lookup:
        // a spelled-out prefix once lacked ".Server" and every release /ui 404d.
        using var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.ui.{asset}");
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

#if DEBUG
    /// <summary>
    /// The file as it sits in the working tree, if it is still there.
    ///
    /// Null rather than an error when it is not: a debug build handed to someone
    /// else has no source tree, and it should fall through to the copy compiled
    /// into it rather than serving a 404 for a file it demonstrably has.
    /// </summary>
    private static string? FromDisk(string asset)
    {
        try
        {
            string path = Path.Combine(SourceDirectory(), "ui", asset);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            // Unreadable, gone, or on a machine that never had it. The embedded
            // copy is the answer in every one of those cases.
            return null;
        }
    }

    /// <summary>
    /// Where this file was compiled from. Baked in at build time by the
    /// compiler, which is the only way to know it — there is no run-time
    /// relationship between a loaded assembly and the tree it came from.
    /// </summary>
    private static string SourceDirectory([CallerFilePath] string thisFile = "")
        => Path.GetDirectoryName(thisFile) ?? ".";
#else
    private static string? FromDisk(string asset) => null;
#endif

    private static string ContentTypeFor(string asset) => asset switch
    {
        _ when asset.EndsWith(".html", StringComparison.OrdinalIgnoreCase) => "text/html; charset=utf-8",
        _ when asset.EndsWith(".css", StringComparison.OrdinalIgnoreCase)  => "text/css; charset=utf-8",
        _ when asset.EndsWith(".js", StringComparison.OrdinalIgnoreCase)   => "text/javascript; charset=utf-8",
        _ when asset.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)  => "image/svg+xml",
        _ => "application/octet-stream",
    };
}
