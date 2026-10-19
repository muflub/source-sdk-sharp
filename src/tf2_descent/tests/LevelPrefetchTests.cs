extern alias client;
// The client's copies of the shared interop types this file hands to client
// code; each side compiles its own (plan_shim_areas_split phase 3).
using IModInfo = client::SourceSharp.Abi.IModInfo;

using System.Net;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Server;
using Xunit;
using MapPrefetcher = client::TF2.Descent.Client.MapPrefetcher;
using ClientNextLevel = client::TF2.Descent.Shared.NextLevelMessage;
using ClientNextLevelPayload = client::TF2.Descent.Shared.NextLevel;
using ClientNextLevelHook = client::TF2.Descent.Shared.INextLevelHook;
using ClientUserMessage = client::SourceSharp.Abi.IUserMessage;
using ClientUserMessageReader = client::SourceSharp.Abi.UserMessageReader;

namespace TF2.Descent.Tests;

/// <summary>
/// The next level's prefetch (the host's NextLevel, D-H14): the server tells
/// clients the map and its fastdl URL; a client downloads and unpacks it to
/// download/maps so the hop loads it from disk.
/// </summary>
public sealed class LevelPrefetchTests : IDisposable
{
    private const string Map = "tf2_descent-2-3d4ae1c8956d33f1";
    private const string Url = "http://127.0.0.1:5004/maps/" + Map + ".bsp.bz2";

    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "tf2_descent";
        public string GameDir => dir;
        public string ManagedDir => dir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    /// <summary>A fastdl that serves fixed bytes for one URL and counts requests.</summary>
    private sealed class FastDl(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    /// <summary>
    /// A fastdl whose request hangs until it is cancelled, then takes a while
    /// to unwind, as a socket being torn down does.
    /// </summary>
    private sealed class StalledFastDl : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                await Task.Delay(200, CancellationToken.None);
                throw;
            }
            throw new InvalidOperationException("an infinite delay ended");
        }
    }

    private sealed class Hook : ClientNextLevelHook
    {
        public (int Depth, string Map, string Url)? Seen;
        public byte[]? Sha256;
        public void OnNextLevel(int depth, string map, string url, byte[]? sha256)
        {
            Seen = (depth, map, url);
            Sha256 = sha256;
        }
    }

    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private readonly string _game = Directory.CreateTempSubdirectory("tf2_descent-prefetch-").FullName;

    public void Dispose() => Directory.Delete(_game, recursive: true);

    private static byte[] Bz2(byte[] raw)
    {
        using var packed = new MemoryStream();
        using (var bz = new BZip2OutputStream(packed) { IsStreamOwner = false })
            bz.Write(raw);
        return packed.ToArray();
    }

    // ---- the message ----------------------------------------------------

    private static byte[] Written(ClientNextLevelPayload payload)
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n > 0);
        return buffer[..n];
    }

    private static Hook Delivered(byte[] bytes)
    {
        var hook = new Hook();
        ((ClientUserMessage)new ClientNextLevel(new Hooks<ClientNextLevelHook>(hook))).OnReceived(new ClientUserMessageReader(bytes));
        return hook;
    }

    [Fact]
    public void The_message_carries_depth_map_and_url()
    {
        var hook = Delivered(Written(new ClientNextLevelPayload { Depth = 2, Map = Map, Url = Url }));

        Assert.Equal((2, Map, Url), hook.Seen);
        Assert.Null(hook.Sha256);
    }

    [Fact]
    public void The_message_carries_the_maps_sha256()
    {
        byte[] sha = SHA256.HashData([9, 8, 7]);

        var hook = Delivered(Written(new ClientNextLevelPayload { Depth = 2, Map = Map, Url = Url, Sha256 = sha }));

        Assert.Equal((2, Map, Url), hook.Seen);
        Assert.Equal(sha, hook.Sha256);
    }

    [Fact]
    public void A_hash_that_is_not_a_sha256_is_not_sent()
    {
        var net = new RecordingEngine();
        var messages = new ClientMessages(net);

        Assert.True(messages.NextLevel(0, 2, Map, Url, new byte[32]));
        Assert.False(messages.NextLevel(0, 2, Map, Url, new byte[31]));
        Assert.Single(net.Calls, c => c.Member == "UserMessages.Send");
    }

    [Fact]
    public void A_message_with_a_bad_hash_length_is_dropped()
    {
        byte[] bytes = Written(new ClientNextLevelPayload { Depth = 2, Map = Map, Url = Url, Sha256 = new byte[32] });
        int lengthAt = bytes.Length - 33;
        Assert.Equal(32, bytes[lengthAt]);
        Assert.NotNull(Delivered(bytes).Seen);

        // 31 bytes counted: within the most, so it reads, and the handler
        // refuses a hash that is not a SHA-256.
        Assert.Null(Delivered([.. bytes[..lengthAt], 31, .. bytes[(lengthAt + 1)..^1]]).Seen);

        // 33 counted: over the most, so the payload itself is refused.
        Assert.Null(Delivered([.. bytes[..lengthAt], 33, .. bytes[(lengthAt + 1)..], 0]).Seen);
    }

    // ---- the server -----------------------------------------------------

    [Fact]
    public void The_server_tells_every_client_the_next_level()
    {
        var net = new RecordingEngine();
        new LevelPrefetch(new ClientMessages(net), new ListLog())
            .OnNextLevel(new NextLevelInfo("c-1", 2, Map, "http://127.0.0.1:5004/"));

        var sent = Assert.Single(net.Calls, c => c.Member == "UserMessages.Send");
        Assert.Equal("TF2.Descent:next_level", sent.Arg(0));
        Assert.Equal(0, sent.Arg(2));   // every client
    }

    /// <summary>What the server sent, decoded by the client's message.</summary>
    private static Hook Received(RecordingEngine net)
    {
        var sent = Assert.Single(net.Calls, c => c.Member == "UserMessages.Send");
        var payload = Assert.IsType<byte[]>(sent.Arg(4));
        var hook = new Hook();
        ((ClientUserMessage)new ClientNextLevel(new Hooks<ClientNextLevelHook>(hook))).OnReceived(new ClientUserMessageReader(payload));
        Assert.Equal((2, Map, Url), hook.Seen);
        return hook;
    }

    [Fact]
    public void The_server_passes_the_hosts_hash_on()
    {
        byte[] sha = SHA256.HashData([4, 5, 6]);
        var net = new RecordingEngine();
        new LevelPrefetch(new ClientMessages(net), new ListLog())
            .OnNextLevel(new NextLevelInfo("c-1", 2, Map, "http://127.0.0.1:5004/") { MapSha256 = sha });

        Assert.Equal(sha, Received(net).Sha256);
    }

    [Fact]
    public void A_hash_that_is_not_a_sha256_is_dropped_and_the_level_still_sent()
    {
        var net = new RecordingEngine();
        var log = new ListLog();
        new LevelPrefetch(new ClientMessages(net), log)
            .OnNextLevel(new NextLevelInfo("c-1", 2, Map, "http://127.0.0.1:5004/") { MapSha256 = new byte[20] });

        Assert.Null(Received(net).Sha256);
        Assert.Contains(log.Lines, l => l.StartsWith("W:", StringComparison.Ordinal) && l.Contains("20 bytes", StringComparison.Ordinal));
    }

    private static NextLevelInfo Next(string url = "http://127.0.0.1:5004/") => new("c-1", 2, Map, url);

    private static (int Player, Hook Seen)[] SentTo(RecordingEngine net) =>
        net.Calls.Where(c => c.Member == "UserMessages.Send")
            .Select(c =>
            {
                var hook = new Hook();
                ((ClientUserMessage)new ClientNextLevel(new Hooks<ClientNextLevelHook>(hook)))
                    .OnReceived(new ClientUserMessageReader(Assert.IsType<byte[]>(c.Arg(4))));
                return ((int)c.Arg(2)!, hook);
            })
            .ToArray();

    [Fact]
    public void A_client_that_becomes_active_later_is_told_the_next_level()
    {
        var net = new RecordingEngine();
        var prefetch = new LevelPrefetch(new ClientMessages(net), new ListLog());

        prefetch.OnNextLevel(Next());
        prefetch.OnClientActive(3);

        var sent = SentTo(net);
        Assert.Equal([0, 3], sent.Select(s => s.Player).ToArray());
        Assert.Equal((2, Map, Url), sent[1].Seen.Seen);
    }

    [Fact]
    public void Before_any_next_level_an_active_client_is_told_nothing()
    {
        var net = new RecordingEngine();
        new LevelPrefetch(new ClientMessages(net), new ListLog()).OnClientActive(3);

        Assert.Empty(SentTo(net));
    }

    [Fact]
    public void A_level_shutdown_forgets_the_next_level()
    {
        var net = new RecordingEngine();
        var prefetch = new LevelPrefetch(new ClientMessages(net), new ListLog());

        prefetch.OnNextLevel(Next());
        prefetch.OnLevelShutdown();
        prefetch.OnClientActive(3);

        Assert.Equal([0], SentTo(net).Select(s => s.Player).ToArray());
    }

    [Fact]
    public void Without_a_fastdl_a_later_client_is_told_nothing_either()
    {
        var net = new RecordingEngine();
        var prefetch = new LevelPrefetch(new ClientMessages(net), new ListLog());

        prefetch.OnNextLevel(Next(""));
        prefetch.OnClientActive(3);

        Assert.Empty(SentTo(net));
    }

    [Fact]
    public void Without_a_fastdl_nothing_is_sent()
    {
        var net = new RecordingEngine();
        new LevelPrefetch(new ClientMessages(net), new ListLog())
            .OnNextLevel(new NextLevelInfo("c-1", 2, Map, ""));

        Assert.DoesNotContain(net.Calls, c => c.Member == "UserMessages.Send");
    }

    // ---- the client -----------------------------------------------------

    [Fact]
    public async Task The_client_downloads_and_unpacks_the_map_to_download_maps()
    {
        byte[] bsp = Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7)).ToArray();
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2(bsp));
        using var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);

        prefetch.OnNextLevel(2, Map, Url, null);
        await prefetch.Running!;

        string file = Path.Combine(_game, "download", "maps", Map + ".bsp");
        Assert.Equal(bsp, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".part"));
        Assert.Contains("prefetched", prefetch.Outcome);
    }

    private static byte[] SomeBsp() => Enumerable.Range(0, 70_000).Select(i => (byte)(i * 7)).ToArray();

    private string MapFile => Path.Combine(_game, "download", "maps", Map + ".bsp");

    [Fact]
    public async Task A_download_matching_the_hash_is_installed()
    {
        byte[] bsp = SomeBsp();
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2(bsp));
        using var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);

        prefetch.OnNextLevel(2, Map, Url, SHA256.HashData(bsp));
        await prefetch.Running!;

        Assert.Equal(bsp, File.ReadAllBytes(MapFile));
        Assert.Contains("prefetched", prefetch.Outcome);
    }

    [Fact]
    public async Task A_download_not_matching_the_hash_is_discarded()
    {
        byte[] bsp = SomeBsp();
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2(bsp));
        using var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);

        prefetch.OnNextLevel(2, Map, Url, SHA256.HashData([1, 2, 3]));
        await prefetch.Running!;

        Assert.Equal(1, fastdl.Requests);
        Assert.False(File.Exists(MapFile));
        Assert.False(File.Exists(MapFile + ".part"));
        Assert.Contains("SHA-256", prefetch.Outcome);
    }

    [Fact]
    public async Task A_downloaded_map_not_matching_the_hash_is_replaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MapFile)!);
        File.WriteAllText(MapFile, "stale");
        byte[] bsp = SomeBsp();
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2(bsp));
        using var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);

        prefetch.OnNextLevel(2, Map, Url, SHA256.HashData(bsp));
        await prefetch.Running!;

        Assert.Equal(1, fastdl.Requests);
        Assert.Equal(bsp, File.ReadAllBytes(MapFile));
    }

    [Fact]
    public async Task A_downloaded_map_matching_the_hash_is_kept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MapFile)!);
        byte[] bsp = SomeBsp();
        File.WriteAllBytes(MapFile, bsp);
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2([1, 2, 3]));
        using var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);

        prefetch.OnNextLevel(2, Map, Url, SHA256.HashData(bsp));
        await prefetch.Running!;

        Assert.Equal(0, fastdl.Requests);
        Assert.Equal(bsp, File.ReadAllBytes(MapFile));
        Assert.Contains("already downloaded", prefetch.Outcome);
    }

    [Fact]
    public void A_map_already_downloaded_is_not_fetched_again()
    {
        string dir = Path.Combine(_game, "download", "maps");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Map + ".bsp"), "here");
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2([1, 2, 3]));
        using var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);

        prefetch.OnNextLevel(2, Map, Url, null);

        Assert.Null(prefetch.Running);
        Assert.Equal(0, fastdl.Requests);
    }

    [Theory]
    [InlineData("../evil", "http://h/maps/../evil.bsp.bz2")]
    [InlineData(Map, "file:///etc/passwd")]
    [InlineData(Map, "http://h/maps/other.bsp.bz2")]
    public void A_bad_name_or_url_is_refused(string map, string url)
    {
        var fastdl = new FastDl(HttpStatusCode.OK, Bz2([1]));
        var log = new ListLog();
        using var prefetch = new MapPrefetcher(new Mod(_game), log, fastdl);

        prefetch.OnNextLevel(2, map, url, null);

        Assert.Null(prefetch.Running);
        Assert.Equal(0, fastdl.Requests);
        Assert.Contains(log.Lines, l => l.StartsWith("W:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_download_leaves_no_map_and_says_so_next_time()
    {
        var fastdl = new FastDl(HttpStatusCode.NotFound, []);
        var log = new ListLog();
        using var prefetch = new MapPrefetcher(new Mod(_game), log, fastdl);

        prefetch.OnNextLevel(2, Map, Url, null);
        await prefetch.Running!;

        string file = Path.Combine(_game, "download", "maps", Map + ".bsp");
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".part"));

        prefetch.OnNextLevel(3, "tf2_descent-3-aaaa", "http://h/maps/tf2_descent-3-aaaa.bsp.bz2", null);
        Assert.Contains(log.Lines, l => l.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disposing_waits_for_the_fetch_in_flight()
    {
        var fastdl = new StalledFastDl();
        var prefetch = new MapPrefetcher(new Mod(_game), new ListLog(), fastdl);
        prefetch.OnNextLevel(2, Map, Url, null);
        await fastdl.Entered.Task;

        prefetch.Dispose();

        Assert.True(prefetch.Running!.IsCompleted, "a fetch still running after Dispose writes into a game directory its owner may be deleting");
    }
}
