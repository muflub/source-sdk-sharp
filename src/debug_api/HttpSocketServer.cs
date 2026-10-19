using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Debug.Api;

/// <summary>
/// What a handler answers with.
///
/// The content type is part of the reply rather than fixed by the transport
/// because the same server now hands out both the API's JSON and the pages of
/// the web UI, and a browser handed HTML labelled as JSON displays the source
/// instead of the page.
/// </summary>
public readonly record struct HttpReply(int Status, string Body, string ContentType = "application/json");

/// <summary>A parsed request: everything a handler needs and nothing else.</summary>
public readonly record struct HttpRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    string Body);

/// <summary>
/// A very small HTTP/1.1 server over a socket.
///
/// This replaced HttpListener, which cannot listen on a Unix domain socket —
/// and a socket per process is what lets several game instances be driven at
/// once, where a fixed TCP port means the second one silently loses.
///
/// Taking an EndPoint rather than a URL is the whole trick: an IPEndPoint and a
/// UnixDomainSocketEndPoint go through identical accept, parse and reply code,
/// so there is one implementation to get right instead of two.
///
/// It is deliberately not a general web server. It handles what a debugging
/// client sends — a request line, headers, an optional Content-Length body — and
/// closes the connection after replying. No keep-alive, no chunked encoding, no
/// pipelining. Anything it does not understand gets a 400 rather than a guess.
/// </summary>
public sealed class HttpSocketServer : IDisposable
{
    /// <summary>
    /// How large a request may be.
    ///
    /// Sized for the largest thing anyone sends, which is a whole map's entity
    /// list on its way to being written back — several hundred kilobytes for an
    /// ordinary map and more for a dense one. It was 64k when the only requests
    /// were console commands, and saving a map failed at the transport with a
    /// bare 400 that said nothing about why.
    ///
    /// Still bounded: this listens on loopback with no authentication, and a
    /// request that can grow without limit is a way to exhaust the game's memory
    /// from a browser tab that got stuck in a loop.
    /// </summary>
    private const int MaxRequestBytes = 16 * 1024 * 1024;

    private readonly Socket _listener;
    private readonly Func<HttpRequest, HttpReply> _handler;
    private readonly Action<string> _onError;

    private volatile bool _running;

    // Kept so Dispose can join it. A background thread that is merely signalled
    // is still on the stack, and a thread on the stack pins its load context.
    private Thread? _thread;

    /// <summary>
    /// Which requests may take a long time to answer -- a <c>/wait</c>, a
    /// settle -- and are served on a thread of their own so the accept loop
    /// keeps answering everything else meanwhile.
    ///
    /// <para>
    /// WHY NOT EVERY REQUEST. The accept loop serves one connection at a time
    /// on one thread, and that is what makes Dispose's join cheap and
    /// certain. A request that is answered within a frame gains nothing from a
    /// thread; a request that waits for a hundred ticks would hold every other
    /// caller -- the web UI's once-a-second status poll among them -- for its
    /// whole wait. So only the ones that wait go elsewhere, and each such
    /// thread is joined on Dispose like the accept thread is, because a live
    /// thread in this assembly pins its load context.
    /// </para>
    /// </summary>
    private readonly Func<HttpRequest, bool> _isLongPoll;

    /// <summary>The long-poll sessions in flight, joined on Dispose.</summary>
    private readonly List<Thread> _sessions = new();

    /// <summary>More long-poll sessions than this at once are refused with a 503.</summary>
    public const int MaxSessions = 32;

    /// <summary>
    /// How the loop takes the next connection off the listener: the listener's
    /// own <c>Accept</c>, unless a test stands in a failing one.
    /// </summary>
    private readonly Func<Socket, Socket> _accept;

    /// <summary>How long the loop waits after a failed accept before it tries again.</summary>
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(50);

    public HttpSocketServer(EndPoint endPoint, Func<HttpRequest, HttpReply> handler, Action<string> onError,
                            Func<HttpRequest, bool>? isLongPoll = null)
        : this(endPoint, handler, onError, isLongPoll, accept: null)
    {
    }

    /// <summary>
    /// The server with the listener's accept replaced, for a fact that makes
    /// one accept fail.
    /// </summary>
    internal HttpSocketServer(EndPoint endPoint, Func<HttpRequest, HttpReply> handler, Action<string> onError,
                              Func<HttpRequest, bool>? isLongPoll, Func<Socket, Socket>? accept)
    {
        _handler = handler;
        _onError = onError;
        _isLongPoll = isLongPoll ?? (_ => false);
        _accept = accept ?? (listener => listener.Accept());

        _listener = endPoint is UnixDomainSocketEndPoint
            ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            : new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        _listener.Bind(endPoint);
        _listener.Listen(backlog: 16);
    }

    /// <summary>
    /// Starts accepting. On its own thread rather than the thread pool: it
    /// blocks for the life of the server, and a pool thread parked forever is a
    /// thread the rest of the process cannot have back.
    /// </summary>
    public void Start()
    {
        _running = true;

        _thread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "SourceSharp DebugApi",
        };

        _thread.Start();
    }

    /// <summary>
    /// Closes the listener, and WAITS for the accept loop to come off the stack.
    ///
    /// <para>
    /// Same reason as <see cref="ConsoleTail.Dispose"/>, and the same bug: a
    /// live thread keeps every assembly on its stack alive, so this one pinned
    /// the server's collectible load context and made every reload leak the
    /// build it replaced. Closing the socket unblocks the accept; the join is
    /// what makes "closed" mean "gone".
    /// </para>
    ///
    /// <para>
    /// A request being served when this runs is given its second to finish,
    /// because cutting one off mid-reply would look to the caller exactly like
    /// the game crashing.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        _running = false;

        try { _listener.Close(); }
        catch { /* shutting down; a failure here has nowhere useful to go */ }

        var thread = _thread;
        _thread = null;

        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(1)))
        {
            _onError("the accept loop did not stop; this side's load context cannot " +
                     "unload and a reload will leak it");
        }

        // The long-poll sessions. Their owner ended every wait before this
        // (DebugApiServer.Dispose cancels first), so each is writing its reply
        // or already gone; the budget is for the write.
        Thread[] sessions;
        lock (_sessions)
            sessions = _sessions.ToArray();

        foreach (var session in sessions)
        {
            if (!session.Join(TimeSpan.FromSeconds(1)))
            {
                _onError("a long-poll session did not stop; this side's load context cannot " +
                         "unload and a reload will leak it");
            }
        }
    }

    /// <summary>Long-poll sessions still running. For the tests.</summary>
    internal int Sessions
    {
        get { lock (_sessions) return _sessions.Count(t => t.IsAlive); }
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            Socket client;

            try
            {
                client = _accept(_listener);
            }
            catch (Exception e)
            {
                // Dispose closed the listener: the loop is done.
                if (!_running)
                    return;

                // ANY OTHER FAILED ACCEPT IS ONE CONNECTION'S, NOT THE SERVER'S.
                // A connection the peer reset before it was taken, an
                // interrupted call: the loop used to return on any of them, and
                // every client after that connected into the backlog and waited
                // for a reply that never came (a Windows runner's
                // DebugApiWaitTests hung five minutes on exactly that). So say
                // what failed and keep taking connections; the pause keeps a
                // listener that fails every time from spinning.
                _onError($"accept failed: {e.Message}");
                Thread.Sleep(AcceptRetryDelay);
                continue;
            }

            // Serially, one connection at a time. A debugging client makes one
            // request at a time, and handling them in order means the log
            // sequence a caller reads back is the one its command produced.
            bool handedOff = false;
            try
            {
                handedOff = Serve(client);
            }
            catch (Exception e)
            {
                _onError($"request failed: {e.Message}");
            }
            finally
            {
                if (!handedOff)
                {
                    try { client.Close(); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Reads and answers one request, or hands a long-poll one to a session
    /// thread. True when handed off: the session owns the socket then.
    /// </summary>
    private bool Serve(Socket client)
    {
        client.ReceiveTimeout = 5000;
        client.SendTimeout = 5000;

        string? raw = ReadRequest(client);

        if (raw is null)
        {
            Reply(client, 400,
                $"{{\"error\":\"malformed request, or larger than the {MaxRequestBytes / (1024 * 1024)}MB limit\"}}",
                "application/json");
            return false;
        }

        if (!TryParse(raw, out HttpRequest request))
        {
            Reply(client, 400, "{\"error\":\"could not parse request\"}", "application/json");
            return false;
        }

        if (_isLongPoll(request))
            return StartSession(client, request);

        HttpReply reply = _handler(request);
        Reply(client, reply.Status, reply.Body, reply.ContentType);
        return false;
    }

    private bool StartSession(Socket client, HttpRequest request)
    {
        lock (_sessions)
        {
            _sessions.RemoveAll(t => !t.IsAlive);

            if (!_running || _sessions.Count >= MaxSessions)
            {
                Reply(client, 503, $"{{\"error\":\"{MaxSessions} long requests are already waiting\"}}",
                      "application/json");
                return false;
            }

            var session = new Thread(() =>
            {
                try
                {
                    HttpReply reply = _handler(request);
                    Reply(client, reply.Status, reply.Body, reply.ContentType);
                }
                catch (Exception e)
                {
                    _onError($"request failed: {e.Message}");
                }
                finally
                {
                    try { client.Close(); } catch { }
                }
            })
            {
                IsBackground = true,
                Name = "SourceSharp DebugApi wait",
            };

            _sessions.Add(session);
            session.Start();
        }

        return true;
    }

    //-------------------------------------------------------------------------
    // Reading. Headers first, then exactly as many body bytes as declared —
    // reading to end-of-stream instead would hang until the client gave up.
    //-------------------------------------------------------------------------
    private static string? ReadRequest(Socket client)
    {
        var buffer = new byte[8192];
        var received = new MemoryStream();

        int headerEnd = -1;

        while (headerEnd < 0)
        {
            int read = client.Receive(buffer);
            if (read <= 0)
                return null;

            received.Write(buffer, 0, read);

            if (received.Length > MaxRequestBytes)
                return null;

            headerEnd = IndexOfHeaderEnd(received.GetBuffer(), (int)received.Length);
        }

        int contentLength = ContentLengthOf(
            Encoding.UTF8.GetString(received.GetBuffer(), 0, headerEnd));

        int bodyStart = headerEnd + 4;
        int have = (int)received.Length - bodyStart;

        while (have < contentLength)
        {
            int read = client.Receive(buffer);
            if (read <= 0)
                break;

            received.Write(buffer, 0, read);
            have += read;

            if (received.Length > MaxRequestBytes)
                return null;
        }

        return Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length);
    }

    private static int IndexOfHeaderEnd(byte[] bytes, int length)
    {
        for (int i = 0; i + 3 < length; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    private static int ContentLengthOf(string headers)
    {
        foreach (string line in headers.Split("\r\n"))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                continue;

            return int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out int length) && length >= 0
                ? length
                : 0;
        }

        return 0;
    }

    //-------------------------------------------------------------------------
    // Parsing.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Splits a raw request into the pieces a handler needs. Internal so the
    /// tests can drive it with a literal request rather than a real socket.
    /// </summary>
    internal static bool TryParse(string raw, out HttpRequest request)
    {
        request = default;

        int headerEnd = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
            return false;

        string head = raw[..headerEnd];
        string body = raw[(headerEnd + 4)..];

        string requestLine = head.Split("\r\n")[0];
        string[] parts = requestLine.Split(' ');

        if (parts.Length < 2)
            return false;

        string method = parts[0];
        string target = parts[1];

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int queryStart = target.IndexOf('?');
        string path = queryStart < 0 ? target : target[..queryStart];

        if (queryStart >= 0)
            ParseQuery(target[(queryStart + 1)..], query);

        // Trailing slashes are noise from a caller's point of view, and a route
        // table that has to spell every path twice will eventually miss one.
        path = path.TrimEnd('/');

        request = new HttpRequest(method, path, query, body.Trim());
        return true;
    }

    private static void ParseQuery(string queryString, Dictionary<string, string> into)
    {
        foreach (string pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');

            if (equals < 0)
            {
                into[Decode(pair)] = string.Empty;
                continue;
            }

            into[Decode(pair[..equals])] = Decode(pair[(equals + 1)..]);
        }
    }

    /// <summary>
    /// Percent- and plus-decoding. Console commands are the reason this matters:
    /// "sv_cheats 1" arrives as "sv_cheats+1" or "sv_cheats%201" depending on
    /// the client, and running either literally would do nothing useful.
    /// </summary>
    private static string Decode(string text) => WebUtility.UrlDecode(text.Replace('+', ' '));

    //-------------------------------------------------------------------------
    // Writing.
    //-------------------------------------------------------------------------
    private static void Reply(Socket client, int status, string content, string contentType)
    {
        byte[] body = Encoding.UTF8.GetBytes(content);

        string head =
            $"HTTP/1.1 {status} {Reason(status)}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            // A debugging tool that serves a stale page after a rebuild costs
            // more time than it ever saves by caching.
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            "\r\n";

        try
        {
            client.Send(Encoding.ASCII.GetBytes(head));
            client.Send(body);
        }
        catch
        {
            // The caller hung up. Nothing to report and nobody to report it to.
        }
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        408 => "Request Timeout",
        409 => "Conflict",
        503 => "Service Unavailable",
        500 => "Internal Server Error",
        _   => "Unknown",
    };
}
