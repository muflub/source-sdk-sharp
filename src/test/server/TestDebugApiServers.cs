using Debug.Api;

namespace SourceSharp.Tests;

/// <summary>
/// The directories a fact's started debug API server is given: its own socket.
///
/// <para>
/// The game names its socket by pid, so two games never share one. A test
/// process starts many servers, though, and xUnit runs test classes in
/// parallel: every server from the public constructor would land on
/// <c>{DEBUG_API_DIR}/{pid}.sock</c>. Start deletes a stale file at its own
/// path before binding, so a second server deleted the first one's socket, or
/// lost the bind to it. "The test server could not listen" flaked in
/// DebugConsoleCaptureTests, DebugApiExitRecordTests and the rest.
/// Each server made here gets a directory of its own under the temp directory
/// (falling back to /tmp when that path would pass a Unix socket's 108-byte
/// limit), and the maps directory the environment names.
/// </para>
/// </summary>
internal static class TestDebugApiServers
{
    /// <summary>Directories for one server: a socket directory of its own, the environment's maps.</summary>
    public static DebugApiDirectories OwnSocket()
        => new(OwnDirectory(), DebugApiDirectories.FromEnvironment().MapDirectory);

    /// <summary>
    /// A fresh directory nothing else in this process will name, under one
    /// parent for the process that is removed when the process exits (a
    /// started server deletes its socket on Dispose; the directories would
    /// otherwise stay behind, one per started server).
    /// </summary>
    public static string OwnDirectory()
    {
        string dir = Path.Combine(s_Parent.Value, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static readonly Lazy<string> s_Parent = new(() =>
    {
        string name = $"ss-dbgapi-{Environment.ProcessId}";
        string parent = Path.Combine(Path.GetTempPath(), name);
        // The socket is parent/<12>/<pid>.sock: keep it under sun_path's 108 bytes.
        if (parent.Length + 1 + 12 + 1 + 12 > 100)
            parent = Path.Combine("/tmp", name);
        Directory.CreateDirectory(parent);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(parent, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        return parent;
    });
}
