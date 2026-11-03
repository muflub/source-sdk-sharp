using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;

namespace SourceSharp.Ai.Hl2mp.Chat;

/// <summary>
/// <see cref="IBotChatWriter"/> over a local model server's HTTP API (plan_hl2mp_ai
/// §3.6; ollama's <c>/api/chat</c>). Runs on the chat worker with the blocking
/// <see cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>,
/// so there is no continuation to outlive a level. JSON is written and read with
/// <see cref="Utf8JsonWriter"/> and <see cref="JsonDocument"/>, never the
/// serializer: its caches keep the server's load context alive
/// (<c>NoAiRouteCallsTheSerializer</c>). Only a loopback address is asked:
/// players' text goes in the prompt and leaves this machine for nowhere.
/// </summary>
public sealed class OllamaChatWriter(HttpMessageHandler handler) : IBotChatWriter, IDisposable
{
    /// <summary>The model chosen by measurement (§12.3): see <c>tools/ai-chat-model</c>.</summary>
    public const string DefaultModel = "llama3.2:3b";

    private readonly HttpClient _http = new(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>A writer over a handler of its own, without a proxy and without keeping a connection past a level's use of it.</summary>
    public static OllamaChatWriter Create() => new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(2) });

    /// <summary>Whether <paramref name="url"/> is an http address on this machine, and the API's address for it.</summary>
    public static bool TryEndpoint(string url, out Uri? endpoint)
    {
        endpoint = null;
        // The console takes an unquoted // for a comment, so host:port is enough.
        string address = url.Contains("://", StringComparison.Ordinal) ? url : "http://" + url;
        if (!Uri.TryCreate(address.TrimEnd('/') + "/api/chat", UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttp)
            return false;
        if (!uri.IsLoopback)
            return false;
        endpoint = uri;
        return true;
    }

    public ChatWriterReply Write(ChatPrompt prompt, ChatTarget target, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return Post(Body(prompt, target), target, cancel);
    }

    /// <summary>A chat request with no messages: the server loads the model and answers at once.</summary>
    public ChatWriterReply Warm(ChatTarget target, CancellationToken cancel) => Post(Body(null, target), target, cancel);

    private ChatWriterReply Post(byte[] body, ChatTarget target, CancellationToken cancel)
    {
        if (!TryEndpoint(target.Url, out Uri? endpoint))
            return new ChatWriterReply(null, ChatFailure.ServerDown, $"'{target.Url}' is not an http address on this machine (in the console, write 127.0.0.1:11435, or quote a URL: an unquoted // starts a comment)");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using HttpResponseMessage response = _http.Send(request, HttpCompletionOption.ResponseContentRead, cancel);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ChatWriterReply(null, ChatFailure.ModelMissing, $"{target.Url} has no model {target.Model}");
            if (!response.IsSuccessStatusCode)
                return new ChatWriterReply(null, ChatFailure.Other, $"{target.Url} answered {(int)response.StatusCode}");
            using Stream stream = response.Content.ReadAsStream(cancel);
            return new ChatWriterReply(ReplyText(stream));
        }
        catch (OperationCanceledException)
        {
            return new ChatWriterReply(null, ChatFailure.Cancelled);
        }
        catch (HttpRequestException e) when (e.InnerException is SocketException or IOException || e.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return new ChatWriterReply(null, ChatFailure.ServerDown, $"nothing answers at {target.Url}");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or InvalidOperationException)
        {
            return new ChatWriterReply(null, ChatFailure.Other, e.Message);
        }
    }

    /// <summary>The request's JSON: a short, single-line completion, keeping the model loaded; no thinking pass on a model that has one. A null prompt only loads it.</summary>
    public static byte[] Body(ChatPrompt? prompt, ChatTarget target)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("model", target.Model);
            w.WriteBoolean("stream", false);
            w.WriteBoolean("think", false);
            w.WriteString("keep_alive", "30m");
            w.WriteStartObject("options");
            w.WriteNumber("num_ctx", 2048);
            w.WriteNumber("num_predict", prompt?.MaxTokens ?? 1);
            w.WriteNumber("temperature", 0.9);
            w.WriteStartArray("stop");
            w.WriteStringValue("\n");
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteStartArray("messages");
            if (prompt is not null)
            {
                Message(w, "system", prompt.System);
                Message(w, "user", prompt.User);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void Message(Utf8JsonWriter w, string role, string content)
    {
        w.WriteStartObject();
        w.WriteString("role", role);
        w.WriteString("content", content);
        w.WriteEndObject();
    }

    /// <summary>The assistant's text in a <c>/api/chat</c> answer; empty for a warm-up's, which has none.</summary>
    public static string ReplyText(Stream body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("message", out JsonElement m) && m.TryGetProperty("content", out JsonElement c) ? c.GetString() ?? "" : "";
    }

    public void Dispose() => _http.Dispose();
}
