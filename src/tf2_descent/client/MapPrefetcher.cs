using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using TF2.Descent.Shared;
using SourceSharp.Abi;

namespace TF2.Descent.Client;

/// <summary>
/// Downloads the party's next level while they play (the host's NextLevel,
/// D-H14), so the hop loads it from disk instead of downloading at connect.
/// The fastdl's <c>maps/&lt;map&gt;.bsp.bz2</c> is unpacked to
/// <c>download/maps/&lt;map&gt;.bsp</c>, where the engine finds a downloaded
/// map. When the server sends the map's SHA-256, a download that does not
/// match is discarded, and a map already there is checked before it is kept.
///
/// <para>
/// The download runs in the background and never writes to the console from
/// there: the client has no game-thread dispatcher for mods, so the outcome
/// is logged by the next call on the game thread.
/// </para>
///
/// <para>
/// Disposing cancels every fetch still running and waits for it, so nothing
/// writes into the game directory once the prefetcher is gone.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class MapPrefetcher(IModInfo mod, ILog log, HttpMessageHandler? handler = null) : INextLevelHook, IDisposable
{
    /// <summary>The largest map accepted, unpacked; a larger one is abandoned.</summary>
    public const long MaxBytes = 512L * 1024 * 1024;

    private readonly HttpClient _http = new(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromMinutes(2) };
    private readonly CancellationTokenSource _stop = new();
    /// <summary>Every fetch started and maybe still running; <see cref="Dispose"/> waits for them.</summary>
    private Task _inflight = Task.CompletedTask;
    /// <summary>The fetch in flight, if any (tests await it).</summary>
    public Task? Running { get; private set; }

    /// <summary>The last map announced as the next level, which <see cref="LevelDownloads"/> keeps.</summary>
    public string? Next { get; private set; }

    /// <summary>Where downloaded maps go: the mod's download/maps.</summary>
    public string DownloadMaps => Path.Combine(mod.GameDir, "download", "maps");

    public void OnNextLevel(int depth, string map, string url, byte[]? sha256)
    {
        if (Interlocked.Exchange(ref _outcomeSlot, null) is { } previous)
            log.Msg(previous);

        if (!IsMapName(map) || !IsMapUrl(url, map))
        {
            log.Warning($"[TF2Descent] not prefetching '{map}' from '{url}': not a map name and its fastdl URL\n");
            return;
        }

        Next = map;

        string target = Path.Combine(DownloadMaps, map + ".bsp");
        if (File.Exists(target) && sha256 is null)
            return;

        log.Msg(File.Exists(target)
            ? $"[TF2Descent] checking the downloaded {map} (depth {depth})\n"
            : $"[TF2Descent] prefetching {map} (depth {depth})\n");
        var fetch = Task.Run(() => Fetch(new Uri(url), target, map, sha256, _stop.Token));
        Running = fetch;
        _inflight = _inflight.IsCompleted ? fetch : Task.WhenAll(_inflight, fetch);
    }

    private string? _outcomeSlot;

    /// <summary>The last finished fetch's outcome, for tests; the next call logs it.</summary>
    public string? Outcome => Volatile.Read(ref _outcomeSlot);

    private async Task Fetch(Uri url, string target, string map, byte[]? sha256, CancellationToken token)
    {
        string partial = target + ".part";
        try
        {
            // Hashed here rather than on the game thread: a map is tens of MB.
            if (sha256 is not null && File.Exists(target) && await Matches(target, sha256, token))
            {
                Volatile.Write(ref _outcomeSlot, $"[TF2Descent] {map} is already downloaded\n");
                return;
            }

            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(DownloadMaps);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long written;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var body = await response.Content.ReadAsStreamAsync(token))
            await using (var unpacked = new BZip2InputStream(body))
            await using (var file = File.Create(partial))
            {
                written = await CopyCapped(unpacked, file, hash, token);
            }

            if (sha256 is not null && !hash.GetHashAndReset().AsSpan().SequenceEqual(sha256))
                throw new InvalidDataException("the unpacked map does not match the server's SHA-256");

            File.Move(partial, target, overwrite: true);
            Volatile.Write(ref _outcomeSlot, $"[TF2Descent] prefetched {map} ({written} bytes)\n");
        }
        catch (Exception e)
        {
            try { File.Delete(partial); } catch (IOException) { }
            Volatile.Write(ref _outcomeSlot, $"[TF2Descent] prefetching {map} failed: {e.Message}\n");
        }
    }

    private static async Task<bool> Matches(string file, byte[] sha256, CancellationToken token)
    {
        await using var stream = File.OpenRead(file);
        return (await SHA256.HashDataAsync(stream, token)).AsSpan().SequenceEqual(sha256);
    }

    private static async Task<long> CopyCapped(Stream from, Stream to, IncrementalHash hash, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > MaxBytes)
                throw new InvalidDataException($"the map is larger than {MaxBytes} bytes unpacked");
            hash.AppendData(buffer, 0, read);
            await to.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return total;
    }

    /// <summary>A plain map name: no path, nothing the file system or console treats specially.</summary>
    public static bool IsMapName(string map)
        => map.Length is > 0 and <= 64 && map.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>An http(s) URL whose path ends in maps/&lt;map&gt;.bsp.bz2.</summary>
    public static bool IsMapUrl(string url, string map)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && uri.AbsolutePath.EndsWith($"/maps/{map}.bsp.bz2", StringComparison.Ordinal);

    /// <summary>
    /// Cancels the fetches still running and waits for them before closing
    /// the client: a fetch left running would go on creating and deleting
    /// files under the game directory after its owner is gone. Fetch catches
    /// everything, so the wait never throws.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        _inflight.Wait();
        _http.Dispose();
        _stop.Dispose();
    }
}
