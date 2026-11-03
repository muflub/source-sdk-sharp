using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SourceSharp.Ai.Hl2mp.Chat;
using Xunit;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>
/// The HTTP client to the model server (plan_hl2mp_ai §3.6), over a loopback
/// listener that answers what a fact tells it to: a reply, a status, or nothing.
/// </summary>
public sealed class OllamaChatWriterTests
{
    private static readonly ChatPrompt Prompt = new("sys \"x\"", "user <chat>hi</chat>", 16);

    /// <summary>A one-connection server: reads a request and answers, or holds the socket open.</summary>
    internal sealed class Server : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _clients = new();
        private readonly Thread _thread;

        /// <summary>The HTTP answer, or null to hold the connection and say nothing.</summary>
        public string? Answer { get; set; }

        public volatile int Accepted;

        public volatile string Request = "";

        public Server(string? answer)
        {
            Answer = answer;
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public static string Ok(string body)
            => $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

        private void Loop()
        {
            try
            {
                while (true)
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    lock (_clients)
                        _clients.Add(client);
                    Accepted++;
                    NetworkStream stream = client.GetStream();
                    var read = new byte[8192];
                    int n = stream.Read(read);
                    Request = Encoding.UTF8.GetString(read, 0, n);
                    if (Answer is { } answer)
                    {
                        stream.Write(Encoding.UTF8.GetBytes(answer));
                        stream.Flush();
                        client.Close();
                    }
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or IOException or InvalidOperationException)
            {
                // the fact is over
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            lock (_clients)
                _clients.ForEach(c => c.Dispose());
            _thread.Join(2000);
        }
    }

    [Fact]
    public void TheBodyAsksForAShortLineFromTheModelWithTheJsonEscaped()
    {
        using JsonDocument doc = JsonDocument.Parse(OllamaChatWriter.Body(Prompt, new ChatTarget("u", "m")));
        JsonElement root = doc.RootElement;
        Assert.Equal(("m", false, false, 16), (root.GetProperty("model").GetString(), root.GetProperty("stream").GetBoolean(), root.GetProperty("think").GetBoolean(),
                                               root.GetProperty("options").GetProperty("num_predict").GetInt32()));
        JsonElement[] messages = [.. root.GetProperty("messages").EnumerateArray()];
        Assert.Equal(("system", "sys \"x\""), (messages[0].GetProperty("role").GetString(), messages[0].GetProperty("content").GetString()));
        Assert.Equal(("user", "user <chat>hi</chat>"), (messages[1].GetProperty("role").GetString(), messages[1].GetProperty("content").GetString()));
    }

    [Fact]
    public void AReplyIsTheMessagesContent()
    {
        using var server = new Server(Server.Ok("{\"message\":{\"role\":\"assistant\",\"content\":\"gg \\\"lol\\\"\"},\"done\":true}"));
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget(server.Url, "m"), CancellationToken.None);
        Assert.Equal(("gg \"lol\"", ChatFailure.None), (reply.Text, reply.Failure));
        Assert.Contains("POST /api/chat", server.Request, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingModelIsSaidAsSuch()
    {
        using var server = new Server("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget(server.Url, "nope"), CancellationToken.None);
        Assert.Equal(ChatFailure.ModelMissing, reply.Failure);
        Assert.Contains("nope", reply.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadStatusIsAFailureNotAThrow()
    {
        using var server = new Server("HTTP/1.1 500 Oops\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        Assert.Equal(ChatFailure.Other, writer.Write(Prompt, new ChatTarget(server.Url, "m"), CancellationToken.None).Failure);
    }

    [Fact]
    public void ABodyThatIsNotJsonIsAFailureNotAThrow()
    {
        using var server = new Server(Server.Ok("<html>hello</html>"));
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        Assert.Equal(ChatFailure.Other, writer.Write(Prompt, new ChatTarget(server.Url, "m"), CancellationToken.None).Failure);
    }

    [Fact]
    public void NothingListeningIsServerDown()
    {
        int port;
        using (var closed = new Server(null))
            port = new Uri(closed.Url).Port;
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget($"http://127.0.0.1:{port}", "m"), CancellationToken.None);
        Assert.Equal(ChatFailure.ServerDown, reply.Failure);
    }

    /// <summary>
    /// A writer whose connections never complete: the connect waits until the
    /// handler gives it up, through its ConnectTimeout or the caller's token.
    /// </summary>
    private static OllamaChatWriter NeverConnecting(TimeSpan connectTimeout) => new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = connectTimeout,
        ConnectCallback = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("the connect wait ended without its token");
        },
    });

    /// <summary>
    /// A connect that outlasts the handler's ConnectTimeout is a server that is
    /// not there, not a cancel: nobody cancelled. On Windows a refused loopback
    /// connect is retried for about two seconds, the writer's own ConnectTimeout,
    /// so a port with nothing listening ends here there (run 220 on main,
    /// win-x64: NothingListeningIsServerDown answered Cancelled).
    /// </summary>
    [Fact]
    public void AConnectThatTimesOutIsServerDown()
    {
        using OllamaChatWriter writer = NeverConnecting(TimeSpan.FromMilliseconds(200));
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget("http://127.0.0.1:11434", "m"), CancellationToken.None);
        Assert.Equal(ChatFailure.ServerDown, reply.Failure);
        Assert.Contains("nothing answers at http://127.0.0.1:11434", reply.Detail, StringComparison.Ordinal);
    }

    /// <summary>The caller's cancel while the connect still waits is a cancel.</summary>
    [Fact]
    public void ACancelWhileConnectingIsCancelled()
    {
        using OllamaChatWriter writer = NeverConnecting(Timeout.InfiniteTimeSpan);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget("http://127.0.0.1:11434", "m"), cancel.Token);
        Assert.Equal(ChatFailure.Cancelled, reply.Failure);
    }

    [Fact]
    public void AnAddressOffThisMachineIsRefusedWithoutAsking()
    {
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget("http://192.0.2.1:11434", "m"), CancellationToken.None);
        Assert.Equal(ChatFailure.ServerDown, reply.Failure);
        Assert.Contains("not an http address on this machine", reply.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11435", true)]
    [InlineData("http://localhost:11435/", true)]
    [InlineData("http://[::1]:11435", true)]
    [InlineData("https://127.0.0.1:11435", false)]
    [InlineData("http://example.com:11435", false)]
    [InlineData("127.0.0.1:11435", true)]
    [InlineData("localhost:11435", true)]
    [InlineData("http:", false)]
    [InlineData("example.com:11435", false)]
    [InlineData("", false)]
    public void OnlyAnHttpAddressOnThisMachineIsAnEndpoint(string url, bool ok)
        => Assert.Equal(ok, OllamaChatWriter.TryEndpoint(url, out _));

    /// <summary>The console cut an unquoted URL at its // (the live run's "http:"): the line says how to write it.</summary>
    [Fact]
    public void AUrlTheConsoleCutSaysHowToWriteIt()
    {
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget("http:", "m"), CancellationToken.None);
        Assert.Equal(ChatFailure.ServerDown, reply.Failure);
        Assert.Contains("an unquoted // starts a comment", reply.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ACancelEndsARequestTheServerNeverAnswers()
    {
        using var server = new Server(null);
        using OllamaChatWriter writer = OllamaChatWriter.Create();
        using var cancel = new CancellationTokenSource();
        // Cancelled only once the server holds the request: on a busy box a fixed
        // delay can fire before the connection is even made.
        var watcher = new Thread(() =>
        {
            long stop = Environment.TickCount64 + 10000;
            while (server.Request.Length == 0 && Environment.TickCount64 < stop)
                Thread.Sleep(1);
            cancel.Cancel();
        }) { IsBackground = true };
        watcher.Start();
        ChatWriterReply reply = writer.Write(Prompt, new ChatTarget(server.Url, "m"), cancel.Token);
        watcher.Join();
        Assert.Equal(ChatFailure.Cancelled, reply.Failure);
        Assert.NotEmpty(server.Request);
    }

    [Fact]
    public void AWarmUpHasNoMessages()
    {
        using JsonDocument doc = JsonDocument.Parse(OllamaChatWriter.Body(null, new ChatTarget("u", "m")));
        Assert.Equal(0, doc.RootElement.GetProperty("messages").GetArrayLength());
    }
}
