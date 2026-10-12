using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The parts of the development API that need neither a socket nor a game: how
/// the log ring behaves when it wraps, and whether what it emits is valid JSON.
///
/// The ring is worth testing because its whole job is to be read from a
/// different thread while the game keeps writing to it, and the sequence
/// numbering is what makes "give me what is new" safe under that.
/// </summary>
public class DebugApiTests
{

    //-------------------------------------------------------------------------
    // Request parsing. Testable at all only because the transport hands the
    // handler a parsed request rather than a socket — which is also what let a
    // TCP port and a Unix socket share one implementation.
    //-------------------------------------------------------------------------
    [Fact]
    public void RequestParsing()
    {
        Assert.True(HttpSocketServer.TryParse("GET /status HTTP/1.1\r\nHost: x\r\n\r\n", out var status)
            && status.Method == "GET" && status.Path == "/status" && status.Body.Length == 0, "a bare GET parses");

        // A route table that has to spell every path twice will eventually miss
        // one, so the trailing slash is dropped here instead.
        Assert.True(HttpSocketServer.TryParse("GET /status/ HTTP/1.1\r\n\r\n", out var slash)
            && slash.Path == "/status", "a trailing slash is trimmed");

        Assert.True(HttpSocketServer.TryParse("GET / HTTP/1.1\r\n\r\n", out var root) && root.Path == "", "the root becomes empty rather than a slash");

        Assert.True(HttpSocketServer.TryParse("GET /log?since=42&max=10 HTTP/1.1\r\n\r\n", out var log)
            && log.Query["since"] == "42" && log.Query["max"] == "10", "query values parse");

        Assert.True(HttpSocketServer.TryParse("GET /log?Since=1 HTTP/1.1\r\n\r\n", out var mixed)
            && mixed.Query["since"] == "1", "query keys ignore case");

        // Console commands are the reason decoding matters: a space arrives as
        // a plus or an escape depending on the client, and running either
        // literally would do nothing useful.
        Assert.True(HttpSocketServer.TryParse("GET /command?c=sv_cheats+1 HTTP/1.1\r\n\r\n", out var plus)
            && plus.Query["c"] == "sv_cheats 1", "plus decodes to a space");

        Assert.True(HttpSocketServer.TryParse("GET /command?c=sv_cheats%201 HTTP/1.1\r\n\r\n", out var pct)
            && pct.Query["c"] == "sv_cheats 1", "percent escapes decode");

        Assert.True(HttpSocketServer.TryParse("GET /log?verbose HTTP/1.1\r\n\r\n", out var flag)
            && flag.Query.ContainsKey("verbose") && flag.Query["verbose"] == "", "a valueless key is empty, not missing");

        Assert.True(HttpSocketServer.TryParse("POST /command HTTP/1.1\r\nContent-Length: 11\r\n\r\nsv_cheats 1", out var post)
            && post.Method == "POST" && post.Body == "sv_cheats 1", "a POST body comes through");

        // Anything unparseable gets a 400 rather than a guess.
        Assert.True(!HttpSocketServer.TryParse("GET /status HTTP/1.1\r\n", out _), "a request with no header end is refused");

        Assert.True(!HttpSocketServer.TryParse("GARBAGE\r\n\r\n", out _), "a request line with one word is refused");
    }

    //-------------------------------------------------------------------------
    // Routing, driven directly rather than through a socket.
    //-------------------------------------------------------------------------
    [Fact]
    public void Routing()
    {
        var server = NeverPumped.Server();

        Assert.True(Reply(server, "GET", "/nope").Status == 404, "an unknown path is a 404");
        Assert.True(Reply(server, "GET", "").Body.Contains("endpoints"), "the root is the help page");

        var log = Reply(server, "GET", "/log");
        Assert.True(log.Status == 200 && log.Body.Contains("\"next\":"), "the log answers with a sequence");

        // Status must answer even when nothing is ticking. It used to go through
        // the game thread, which made it time out during map load and look
        // exactly like a game that had died — the opposite of what it is for.
        var status = Reply(server, "GET", "/status");
        Assert.True(status.Status == 200, "status answers with no game thread at all");
        Assert.True(status.Body.Contains("\"ticking\":false"), "and says the game is not ticking");
        Assert.True(status.Body.Contains("\"pid\":"), "while still reporting who it is");

        // Missing arguments are reported rather than guessed at.
        Assert.True(Reply(server, "POST", "/command").Body.Contains("no command given"), "a command with nothing to run is refused");

        Assert.True(Reply(server, "GET", "/convar").Body.Contains("no convar named"), "a convar with no name is refused");

        Assert.True(Reply(server, "GET", "/entities").Body.Contains("no class given"), "entities with no class is refused");

        // G1: the listings come through the server's IDebugConsole, which is
        // always there, so there is no "did not supply" answer left to test.
    }

    //-------------------------------------------------------------------------
    // The listings the web UI is built on.
    //
    // Driven through fakes rather than a registry, because what is being checked
    // is the shape of what comes out — a listing that emits invalid JSON fails
    // in the browser, where there is nothing to see but an empty page.
    //-------------------------------------------------------------------------
    [Fact]
    public void Listings()
    {
        IReadOnlyList<ScriptedClassInfo> classList = [];
        IReadOnlyList<ScriptedInstanceInfo> instanceList = [];
        var console = new ConsoleFixture();
        var server = DebugApiServer.ForTest(TestDebugHooks.Server(classes: () => classList, instances: () => instanceList,
                                                              console: console.Hook()));

        console.ConVars = new ConVarBase[] { new TestConVar(), new BoundedTestConVar() };
        console.Commands = new[]
        {
            // A name and help text with a quote in it: the emitter escapes by
            // hand, and a listing is where unescaped text first gets in.
            new ConCommandInfo("cs_do_thing", "does \"the\" thing", "DoThingCommand"),
        };
        classList = new[]
        {
            new ScriptedClassInfo("cs_timer", "Logical", "TimerEntity",
                new[] { "Start", "Stop" },
                new[] { "OnTimer" },
                new[] { new ScriptedFieldInfo("interval", "float") },
                2),
        };

        var convars = Reply(server, "GET", "/convars");
        Assert.True(IsJson(convars.Body), "convars answer with valid JSON");
        Assert.True(convars.Body.Contains("\"value\":\"7\"")
            && convars.Body.Contains("\"default\":\"3\"")
            && convars.Body.Contains("\"type\":\"int\""), "a convar carries its value, default and type");

        // A convar the engine refused still lists. Hiding it would leave a real
        // setting missing from the one view meant to show what exists.
        Assert.True(convars.Body.Contains("\"registered\":false"), "a refused convar lists as unregistered");

        // What a value is allowed to be appears nowhere in a convar's name, its
        // help or its flags. A tool offering an edit without it invites one the
        // engine silently rewrites.
        Assert.True(convars.Body.Contains("\"min\":0,\"max\":100"), "declared bounds reach the listing");

        // null rather than an omitted field or a sentinel: zero is an ordinary
        // minimum, and any number meaning "none" is one some convar clamps to.
        Assert.True(convars.Body.Contains("\"min\":null,\"max\":null"), "a convar with no bounds says null, not a number");

        // The same describer as a native convar's, so one set of flags reads the
        // same way whoever declared it.
        Assert.True(convars.Body.Contains("\"flags\":\"Archive\""), "declared flags are spelled out like the engine's");

        var commands = Reply(server, "GET", "/commands");
        Assert.True(IsJson(commands.Body), "commands answer with valid JSON");
        Assert.True(commands.Body.Contains("does \\\"the\\\" thing"), "a command's help is escaped, not broken");

        var classes = Reply(server, "GET", "/entityclasses");
        Assert.True(IsJson(classes.Body), "entity classes answer with valid JSON");
        Assert.True(classes.Body.Contains("\"inputs\":[\"Start\",\"Stop\"]")
            && classes.Body.Contains("\"outputs\":[\"OnTimer\"]")
            && classes.Body.Contains("\"name\":\"interval\""), "a class carries its inputs, outputs and keys");

        // Live instances are the one listing that reads engine state, so it goes
        // through the game thread. Nothing runs it here but this loop — which is
        // exactly the arrangement the real server has.
        instanceList = new[]
        {
            new ScriptedInstanceInfo("cs_timer", 42u, 7, true, new Vector(1f, 2f, 3f), 100, 0,
                new[] { new ScriptedVarInfo("interval", "float", "0.5") },
                new[] { new ScriptedOutputInfo("OnTimer", 3, "fired") }),
        };

        string instances = OnAPumpedServer(server, s => Reply(s, "GET", "/instances").Body);

        Assert.True(IsJson(instances), "live instances answer with valid JSON");
        Assert.True(instances.Contains("\"value\":\"0.5\"")
            && instances.Contains("\"fireCount\":3")
            && instances.Contains("\"lastValue\":\"fired\""), "an instance carries its keys and what its outputs did");

        // An output that has never fired carries no value at all, and null is
        // the only honest way to say that — "" is a value it might have sent.
        instanceList = new[]
        {
            new ScriptedInstanceInfo("cs_timer", 1u, 1, false, Vector.Zero, 0, 0,
                Array.Empty<ScriptedVarInfo>(),
                new[] { new ScriptedOutputInfo("OnTimer", 0, null) }),
        };

        string never = OnAPumpedServer(server, s => Reply(s, "GET", "/instances").Body);
        Assert.True(IsJson(never) && never.Contains("\"lastValue\":null"), "an output that never fired reports a null value, not an empty one");

        Assert.True(never.Contains("\"valid\":false"), "an entity removed under us is reported, not dropped");

        NativeListings(server, console);
    }

    /// <summary>What the server's IDebugConsole reads, settable as a test goes (G1).</summary>
    private sealed class ConsoleFixture
    {
        public IReadOnlyList<ConVarBase> ConVars { get; set; } = [];
        public IReadOnlyList<ConCommandInfo> Commands { get; set; } = [];
        public IReadOnlyList<NativeConsoleEntry> Native { get; set; } = [];
        public Func<string, string?> NativeValue { get; set; } = _ => null;

        public SourceSharp.Server.DebugConsoleHook Hook()
            => new(() => ConVars, () => Commands, _ => null, () => Native, name => NativeValue(name), _ => { });
    }

    //-------------------------------------------------------------------------
    // The engine's own registry, merged into the listings.
    //
    // Stood in for rather than walked: there is no engine under a test, and what
    // is worth checking is the merge and the flagging, neither of which the walk
    // decides. The walk itself is the one part only a running game can prove.
    //-------------------------------------------------------------------------
    private static void NativeListings(DebugApiServer server, ConsoleFixture console)
    {
        console.Native = new[]
        {
            new NativeConsoleEntry("sv_cheats", "1", "0", "Allow cheats on server",
                (int)(ConVarFlags.Notify | ConVarFlags.Replicated), false, 0f, 1f),

            // The engine's copy of a convar this mod declared. Registering one
            // puts it in BOTH registries, so this is the row that must not
            // appear twice — and the typed managed one is the row that wins.
            new NativeConsoleEntry("cs_test_value", "99", "0", "the engine's copy of ours", 0, false),

            new NativeConsoleEntry("changelevel", "", "", "Change server to the specified map",
                (int)(ConVarFlags.GameDll | ConVarFlags.Cheat), true),
        };

        // The default has not moved. A UI polls this every few seconds and the
        // engine's registry costs a frame to read, so asking for it is opt-in.
        var mine = Reply(server, "GET", "/convars");
        Assert.True(IsJson(mine.Body) && !mine.Body.Contains("sv_cheats")
            && mine.Body.Contains("\"source\":\"managed\""), "the default convar listing is still this mod's alone");

        string all = OnAPumpedServer(server, s => Reply(s, "GET", "/convars?source=all").Body);

        Assert.True(IsJson(all), "a merged convar listing answers with valid JSON");
        Assert.True(all.Contains("\"name\":\"sv_cheats\"") && all.Contains("\"source\":\"native\""), "a native convar is listed and flagged as native");

        // Flags are the reason a native listing is worth having at all: what a
        // convar will refuse to do is written there and nowhere else.
        Assert.True(all.Contains("\"flags\":\"Notify, Replicated\""), "native flag bits are spelled out");

        // No type was ever declared for a native convar, and a guess would be
        // the tool inventing a rule the game does not have.
        Assert.True(all.Contains("\"type\":\"unknown\""), "a native convar reports an unknown type");

        // The engine records what its own convars clamp to, and a listing that
        // dropped it would offer edits the game quietly rewrites.
        Assert.True(all.Contains("\"min\":0,\"max\":1"), "a native convar's bounds come through");

        Assert.True(CountOf(all, "\"name\":\"cs_test_value\"") == 1 && all.Contains("\"type\":\"int\""), "a convar in both registries is listed once, as ours");

        Assert.True(!all.Contains("changelevel"), "a native command is not listed among the convars");

        // Native alone is the engine's table MINUS ours. Our convars are in that
        // table — registering one puts them there — but asking for the engine's
        // is asking which of these came from somewhere other than here, and
        // answering it with this mod's own in untyped form answers a question
        // nobody has.
        string native = OnAPumpedServer(server, s => Reply(s, "GET", "/convars?source=native").Body);
        Assert.True(IsJson(native) && native.Contains("sv_cheats")
            && !native.Contains("cs_test_value"), "a native-only listing is the engine's table minus ours");

        string nativeCommands = OnAPumpedServer(server, s => Reply(s, "GET", "/commands?source=native").Body);
        Assert.True(IsJson(nativeCommands) && nativeCommands.Contains("changelevel")
            && !nativeCommands.Contains("cs_do_thing"), "and the same for the commands");

        string commands = OnAPumpedServer(server, s => Reply(s, "GET", "/commands?source=all").Body);

        Assert.True(IsJson(commands), "a merged command listing answers with valid JSON");
        Assert.True(commands.Contains("\"name\":\"changelevel\"")
            && commands.Contains("\"declaredBy\":\"server.dll\"")
            && commands.Contains("\"flags\":\"GameDll, Cheat\""), "a native command is listed, flagged, and says which DLL owns it");

        Assert.True(commands.Contains("cs_do_thing") && commands.Contains("\"source\":\"managed\""), "this mod's commands survive the merge");

        Assert.True(!commands.Contains("sv_cheats"), "a native convar is not listed among the commands");

        // A misspelled source is refused rather than quietly answered with the
        // default, which would look like the native listing coming back empty.
        var wrong = Reply(server, "GET", "/convars?source=nativ");
        Assert.True(IsJson(wrong.Body) && wrong.Body.Contains("no such source"), "an unknown source is an error, not a silent default");

        // Reading back a convar this mod never declared. Until the registry
        // could be walked these were settable and not readable.
        console.NativeValue = name => name == "sv_gravity" ? "600" : null;

        string read = OnAPumpedServer(server, s => Reply(s, "GET", "/convar?name=sv_gravity").Body);
        Assert.True(read.Contains("\"value\":\"600\""), "a native convar reads back through the engine");

        string missing = OnAPumpedServer(server, s => Reply(s, "GET", "/convar?name=cs_nonsense").Body);
        Assert.True(missing.Contains("no convar of that name"), "a name in neither registry says so");
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;

        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Runs a request that needs the game thread, with something playing the
    /// part of one. The real server drains this queue from the engine's think;
    /// without a stand-in the request would simply time out.
    /// </summary>
    private static string OnAPumpedServer(DebugApiServer server, Func<DebugApiServer, string> request)
    {
        using var stop = new CancellationTokenSource();

        var pump = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                server.Pump();
                Thread.Sleep(1);
            }
        }) { IsBackground = true };

        pump.Start();

        try { return request(server); }
        finally { stop.Cancel(); }
    }

    //-------------------------------------------------------------------------
    // The map routes.
    //
    // Nothing here touches the engine, which is the point: the static view is
    // meant to work with no game running, so it must answer without one.
    //-------------------------------------------------------------------------
    [Fact]
    public void MapRouting()
    {
        string currentMap = "";
        var server = DebugApiServer.ForTest(TestDebugHooks.Server(map: () => currentMap));

        // No map named and none loaded. Reported rather than guessed at -- an
        // empty entity list would read as "this map has no entities".
        Assert.True(Reply(server, "GET", "/map/entities").Body.Contains("no map loaded"), "map entities with no map says so");

        Assert.True(Reply(server, "GET", "/map/geometry").Body.Contains("no map loaded"), "map geometry with no map says so");

        var info = Reply(server, "GET", "/map");
        Assert.True(IsJson(info.Body), "map info answers with valid JSON");
        Assert.True(info.Body.Contains("\"available\":"), "map info says which maps it can see");
        Assert.True(info.Body.Contains("\"directory\":"), "map info says where it looked");

        // A map that is not there is named in the reply. The usual cause is a
        // map loaded from somewhere this does not look, and an empty view would
        // not say that.
        currentMap = "no_such_map";
        var missing = Reply(server, "GET", "/map/entities");
        Assert.True(IsJson(missing.Body) && missing.Body.Contains("no_such_map"), "a map that cannot be opened is reported, with its name");

        // The events feed needs no game thread either, so a wedged game can
        // still be asked what its entities were doing.
        var events = Reply(server, "GET", "/events");
        Assert.True(IsJson(events.Body), "events answer with valid JSON");
        Assert.True(events.Body.Contains("\"sampling\":"), "events report whether sampling is running");

        // The console feed reports where it looked, because the usual reason it
        // is empty is a game started without -condebug.
        var console = Reply(server, "GET", "/console");
        Assert.True(IsJson(console.Body), "the console feed answers with valid JSON");
        Assert.True(console.Body.Contains("\"path\":"), "the console feed says where it is reading from");
    }

    //-------------------------------------------------------------------------
    // Which entities one is touching -- what the physics contact-touch gate
    // names its pair with.
    //-------------------------------------------------------------------------
    [Fact]
    public void Touching_with_no_index_is_refused()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server(touching: () => []));

        Assert.True(Reply(server, "GET", "/entity/touching").Body.Contains("index required"),
            "a touching query with no index is refused");
    }

    [Fact]
    public void Touching_with_no_directory_says_so()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());

        Assert.True(Reply(server, "GET", "/entity/touching?index=5").Body.Contains("no touch directory"),
            "a host with no touch directory says so rather than answering an empty list");
    }

    [Fact]
    public void Touching_answers_the_linked_indices_for_that_entity()
    {
        // The server's IDebugWorld over two links, 5-7 and 9-5, read in that
        // order: the engine answers each handle's index as it is asked.
        var server = DebugApiServer.ForTest(DebugApiGoldenTouchingTests.Hooks());

        string body = OnAPumpedServer(server, s => Reply(s, "GET", "/entity/touching?index=5").Body);

        Assert.True(IsJson(body), "touching answers with valid JSON");
        Assert.True(body.Contains("\"index\":5") && body.Contains("\"touching\":[7,9]"),
            "the reply names the entity asked about and every entity linked to it: " + body);
    }

    //-------------------------------------------------------------------------
    // Firing at entities.
    //-------------------------------------------------------------------------
    [Fact]
    public void Firing()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());

        Assert.True(Reply(server, "GET", "/entity/input?input=Kill").Body.Contains("no target"), "an input with no target is refused");

        Assert.True(Reply(server, "GET", "/entity/input?target=door").Body.Contains("no input"), "an input with no name is refused");

        Assert.True(Reply(server, "GET", "/entity/output?output=OnTrigger").Body.Contains("no entity"), "an output with no entity is refused");

        Assert.True(Reply(server, "GET", "/entity/connect?output=OnTrigger&target=a&input=Kill")
                .Body.Contains("no source"), "a connection with no source is refused");

        // AddOutput separates its fields with colons, so a colon inside one
        // would shift every field after it along by one and wire up something
        // else entirely.
        Assert.True(Reply(server, "GET", "/entity/connect?source=a&output=On:X&target=b&input=Kill")
                .Body.Contains("colon"), "a colon in a connection field is refused");

        // The console splits on whitespace. An unquoted targetname with a space
        // in it would address the first word and pass the rest as arguments,
        // which fires at the wrong entity or at nothing.
        Assert.True(DebugApiServer.BuildEntFire("door", "Open", null, null) == "ent_fire door Open", "a plain argument is left alone");

        Assert.True(DebugApiServer.BuildEntFire("front door", "Open", null, null)
                == "ent_fire \"front door\" Open", "a name with a space is quoted");

        Assert.True(DebugApiServer.BuildEntFire("door", "SetSpeed", "50", null) == "ent_fire door SetSpeed 50", "a parameter is passed through");

        // ent_fire reads its arguments by position, so a delay cannot be given
        // without something in the parameter slot ahead of it.
        Assert.True(DebugApiServer.BuildEntFire("door", "Open", "", "2") == "ent_fire door Open \"\" 2", "a delay forces an empty parameter to be spelled out");

        Assert.True(DebugApiServer.BuildEntFire("door", "Open", null, "0")
            == "ent_fire door Open", "a zero delay is left off");
    }

    //-------------------------------------------------------------------------
    // The web interface's own files.
    //-------------------------------------------------------------------------
    [Fact]
    public void UiServing()
    {
        var server = DebugApiServer.ForTest(TestDebugHooks.Server());

        var page = Reply(server, "GET", "/ui");
        Assert.True(page.Status == 200 && page.Body.Contains("<html"), "the UI root serves the page");

        // A browser handed HTML labelled as JSON shows the source instead of
        // the page, which is why the reply carries its own content type.
        Assert.True(page.ContentType.StartsWith("text/html"), "the page is labelled as HTML");

        // The transport trims a trailing slash, so both spellings of the root
        // arrive identically and only one of them can be tested through Route.
        Assert.True(Reply(server, "GET", "/ui/app.css").ContentType.StartsWith("text/css"), "the stylesheet is served as CSS");

        Assert.True(Reply(server, "GET", "/ui/app.js").ContentType.StartsWith("text/javascript"), "the script is served as JavaScript");

        Assert.True(Reply(server, "GET", "/ui/nothing.html").Status == 404, "an asset that does not exist is a 404");

        // Everything lives in one flat directory, so a separator at all means
        // the request is trying to leave it.
        Assert.True(Reply(server, "GET", "/ui/../DebugApiServer.cs").Status == 404, "a traversal attempt is refused");

        Assert.True(Reply(server, "GET", "/ui/sub/app.js").Status == 404, "a nested path is refused");
    }

    /// <summary>
    /// The copy compiled into the assembly answers on its own. A debug build
    /// serves the page from the working tree first, so UiServing passes there
    /// even when the embedded lookup is broken, which is every release build:
    /// the prefix once named "SourceSharp.DevApi.ui." while the build embedded
    /// "SourceSharp.DevApi.Server.ui.*" (the assembly's name before it became
    /// Debug.Api.Server), and /ui answered 404 in release.
    /// </summary>
    [Fact]
    public void UiEmbeddedCopyAnswers()
    {
        string? page = UiAssets.FromResource("index.html");
        Assert.True(page is not null && page.Contains("<html"), "the embedded index.html is found by the prefix UiAssets looks it up with");
    }

    /// <summary>
    /// Whether text parses as JSON. The emitters build it by hand, so this is
    /// the check that matters: a listing with one unescaped character is a blank
    /// page in a browser and nothing else.
    /// </summary>
    private static bool IsJson(string text)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A convar the engine never accepted, which is the interesting case: it
    /// still holds its declared value and still lists, but writing to it does
    /// nothing. Bind is internal, so this reaches it the way the registry does.
    /// </summary>
    private sealed class TestConVar : IntConVar
    {
        public TestConVar()
        {
            Bind(-1, "cs_test_value", "3", "a test convar", ConVarFlags.Archive);
            NotifyChanged("7", "3");
        }
    }

    /// <summary>One declared with bounds, which the listing has to carry.</summary>
    private sealed class BoundedTestConVar : IntConVar
    {
        public BoundedTestConVar()
            => Bind(-1, "cs_test_bounded", "50", "a bounded test convar",
                    ConVarFlags.None, min: 0f, max: 100f);
    }

    /// <summary>
    /// The whole-file report, through the endpoint.
    ///
    /// The reader has its own tests; what this covers is the half that only
    /// exists here -- that the JSON is well formed with every section in it, and
    /// that a request naming no map says so rather than answering about nothing.
    ///
    /// The maps directory is named outright rather than deduced, because a test
    /// runs from the repository and the deduction is about a mod's layout.
    /// </summary>
    [Fact]
    public void MapFileRouting()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sourcesharp-maps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "test_report.bsp"), BspReportTests.SampleMap());

            // The maps directory named outright, as SOURCESHARP_MAPS names it
            // where the server is composed (DebugApiDirectories.FromEnvironment).
            var server = DebugApiServer.ForTest(TestDebugHooks.Server(),
                                                new DebugApiDirectories(DebugApiServer.DefaultSocketDirectory, directory));

            Assert.True(Reply(server, "GET", "/map/file").Body.Contains("no map loaded"), "the file report with no map says so");

            var reply = Reply(server, "GET", "/map/file?map=test_report");

            Assert.True(IsJson(reply.Body), "the file report answers with valid JSON");
            Assert.True(reply.Body.Contains("\"map\":\"test_report\""), "the file report names the map");
            Assert.True(reply.Body.Contains("\"ident\":\"VBSP\"") && reply.Body.Contains("\"revision\":4242"), "the file report carries the header");

            // Every lump, not only the ones with something in them: the lump
            // table is what the view is navigated by, and a short one would be a
            // map with lumps missing rather than lumps that are empty.
            Assert.True(reply.Body.Split("\"index\":").Length > 64, "the file report carries all sixty-four lumps");

            Assert.True(reply.Body.Contains("concrete/floor01"), "the file report decodes the materials");
            Assert.True(reply.Body.Contains("models/props_junk/wood_crate001a.mdl"), "the file report decodes the props");
            Assert.True(reply.Body.Contains("\"cubemaps\":[{\"origin\":[64,128,16],\"size\":2}]"), "the file report decodes the cubemaps");
            Assert.True(reply.Body.Contains("\"classname\":\"worldspawn\",\"count\":1"), "the file report counts the classnames");

            // A name that is a path is the way out of the maps directory, and it
            // is refused for a report exactly as it is for a read or a write.
            Assert.True(Reply(server, "GET", "/map/file?map=../secret").Body.Contains("not a path"), "a map name that is a path is refused");

            Assert.True(Reply(server, "GET", "/map/file?map=no_such_map").Body.Contains("no_such_map"), "a map that is not there is reported by name");

            // The lumps read one at a time. Their own endpoint because they are
            // the big ones, and sending them with the report would mean paying
            // megabytes to look at something small.
            var faces = Reply(server, "GET", "/map/lump?map=test_report&lump=7");

            Assert.True(IsJson(faces.Body), "a lump read on its own answers with valid JSON");
            Assert.True(faces.Body.Contains("\"lump\":7") && faces.Body.Contains("\"name\":\"FACES\""), "a lump read on its own says which lump it is");

            // A lump nothing decodes and a lump with nothing in it are different
            // answers, and only one of them is about the map.
            Assert.True(Reply(server, "GET", "/map/lump?map=test_report&lump=8").Body.Contains("\"known\":false"), "a lump nothing decodes says so rather than coming back empty");

            Assert.True(Reply(server, "GET", "/map/lump?map=test_report&lump=beans").Body.Contains("0 to 63"), "a lump that is not a number is refused");
            Assert.True(Reply(server, "GET", "/map/lump?map=test_report&lump=64").Body.Contains("0 to 63"), "a lump past the end of the header is refused");
            Assert.True(Reply(server, "GET", "/map/lump?lump=7").Body.Contains("no map loaded"), "a lump with no map named says so");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    //-------------------------------------------------------------------------
    // The container's own view.
    //
    // Driven through a real ServiceContainer rather than a fake listing,
    // because unlike the others this listing's SHAPE is the thing under test:
    // the rows are computed from two dictionaries with different contents, and
    // a fake would only prove the emitter can print what it was handed.
    //-------------------------------------------------------------------------
    [Fact]
    public void ServicesListing()
    {
        // G7: the listing is always wired now, through the server's IDebugStatus.
        var container = new ServiceContainer();
        var server = DebugApiServer.ForTest(TestDebugHooks.Server(services: () => container.Describe()));
        var convar = new TestConVar();

        container.AddInstance<IServiceFixture>(new ServiceFixture());
        container.Add<ILazyFixture, LazyFixture>();
        container.AddInstance(convar.GetType(), convar);
        container.AddNamed("cs_test_value", convar);
        container.AddNamed("cs_do_thing", new DoThingCommand());

        var byType = Reply(server, "GET", "/services");
        Assert.True(IsJson(byType.Body), "services answer with valid JSON");

        // A contract says what may be called; the implementation says which
        // object arrives. Both, because only the second is wrong when an entity
        // consults something that is not the live system.
        Assert.True(byType.Body.Contains("\"key\":\"IServiceFixture\"")
            && byType.Body.Contains("\"implementation\":\"ServiceFixture\""), "a contract lists the class that answers it");

        Assert.True(byType.Body.Contains("\"kind\":\"instance\",\"implementation\":\"ServiceFixture\",\"built\":true"), "a ready-made registration reads as an instance");

        // A lazy service is DESCRIBED, not built: asking what exists must not be
        // what causes it to exist.
        Assert.True(byType.Body.Contains("\"kind\":\"lazy\",\"implementation\":\"LazyFixture\",\"built\":false"), "a lazy registration lists unbuilt, naming what it would build");
        Assert.True(!LazyFixture.WasBuilt, "and describing it did not build it");

        Assert.True(byType.Body.Contains("\"key\":\"IServiceLocator\"")
            && byType.Body.Contains("\"kind\":\"self\""), "the container lists itself, marked as itself");

        // The default view is contracts only. A convar reached by name is a
        // different question from a contract reached by type, and interleaving
        // them makes the common one harder to read.
        Assert.True(!byType.Body.Contains("\"lookedUpBy\":\"name\""), "the default listing is the type table");
        Assert.True(byType.Body.Contains("\"kind\":\"convar\""), "a convar registered under its own class still appears there");

        var byName = Reply(server, "GET", "/services?by=name");
        Assert.True(byName.Body.Contains("\"key\":\"cs_test_value\"")
            && byName.Body.Contains("\"key\":\"cs_do_thing\""), "the name table lists the console names");
        Assert.True(byName.Body.Contains("\"kind\":\"command\""), "and tells a convar from a command");
        Assert.True(!byName.Body.Contains("\"lookedUpBy\":\"type\""), "and holds nothing from the type table");

        var all = Reply(server, "GET", "/services?by=all");
        Assert.True(all.Body.Contains("\"lookedUpBy\":\"type\"")
            && all.Body.Contains("\"lookedUpBy\":\"name\""), "asking for both returns both");

        // The error must not name the convar listing's options, which is what
        // reusing its message would have done.
        var bad = Reply(server, "GET", "/services?by=managed");
        Assert.True(bad.Body.Contains("no such lookup"), "an unknown lookup is refused");
        Assert.True(bad.Body.Contains("type") && bad.Body.Contains("name")
            && !bad.Body.Contains("native"), "and names the options this listing actually has");
    }

    private interface IServiceFixture { }

    private sealed class ServiceFixture : IServiceFixture { }

    private interface ILazyFixture { }

    private sealed class LazyFixture : ILazyFixture
    {
        public LazyFixture() => WasBuilt = true;

        /// <summary>Set the moment one is constructed. See the listing test.</summary>
        public static bool WasBuilt;
    }

    private sealed class DoThingCommand : IConCommand
    {
        public void Execute(string args) { }
    }

    private static HttpReply Reply(DebugApiServer server, string method, string path, string body = "")
    {
        HttpSocketServer.TryParse($"{method} {(path.Length == 0 ? "/" : path)} HTTP/1.1\r\n\r\n{body}", out var request);
        return server.Route(request);
    }

    //-------------------------------------------------------------------------
    // Socket naming. What keeps two instances apart.
    //-------------------------------------------------------------------------
    [Fact]
    public void SocketNaming()
    {
        var paths = DebugApiServer.ForTest(TestDebugHooks.Server());
        Assert.True(paths.SocketPathFor(1234).EndsWith("/1234.sock"), "a socket is named for its process");

        Assert.True(paths.SocketPathFor(1) != paths.SocketPathFor(2), "different processes get different sockets");
    }


    [Fact]
    public void Sequencing()
    {
        var ring = new LogRing(capacity: 8);

        Assert.True(ring.NextSequence == 0, "a fresh ring starts at zero");
        Assert.True(ring.Since(0).Count == 0, "and has nothing to give");

        ring.Add("first\n", false);
        ring.Add("second\n", true);

        var all = ring.Since(0);
        Assert.True(all.Count == 2 && all[0].Text == "first", "lines come back oldest first");
        Assert.True(!all[0].IsWarning && all[1].IsWarning, "warnings are marked");

        // Log calls arrive newline-terminated, which is noise once each line is
        // its own record.
        Assert.True(all[0].Text == "first" && all[1].Text == "second", "trailing newlines are stripped");

        // Sequence numbers are what let a reader ask for exactly what is new
        // without an index into a buffer that moves under it.
        Assert.True(all[0].Sequence == 0 && all[1].Sequence == 1, "sequences count up");
        Assert.True(ring.NextSequence == 2, "next sequence follows the last");

        var since = ring.Since(1);
        Assert.True(since.Count == 1 && since[0].Text == "second", "asking from a sequence skips what came before");
        Assert.True(ring.Since(2).Count == 0, "asking from the end gives nothing");

        // A blank line carries nothing and would only pad the feed.
        ring.Add("\n", false);
        ring.Add("", false);
        Assert.True(ring.NextSequence == 2, "empty lines are dropped");
    }

    [Fact]
    public void Wrapping()
    {
        var ring = new LogRing(capacity: 4);

        for (int i = 0; i < 10; i++)
            ring.Add($"line {i}", false);

        var all = ring.Since(0);

        Assert.True(all.Count == 4, "a full ring holds only its capacity");
        Assert.True(all[0].Text == "line 6" && all[3].Text == "line 9", "and keeps the newest");

        // Sequences keep counting past the wrap, so a reader that fell behind
        // gets what is left rather than the same lines again under new numbers.
        Assert.True(all[0].Sequence == 6 && all[3].Sequence == 9, "sequences survive wrapping");

        // A reader further behind than the ring is deep silently gets what
        // remains. Losing the oldest lines is what a ring is for.
        Assert.True(ring.Since(0).Count == 4, "a reader that fell too far behind still gets the rest");

        Assert.True(ring.Since(0, max: 2).Count == 2, "the limit is honoured");
    }

    //-------------------------------------------------------------------------
    // Where the mod's files are.
    //
    // Worth testing because getting it wrong is invisible: every path still
    // resolves, against the wrong directory, and the API answers with an empty
    // map list and an empty console feed rather than an error. That is what it
    // did in the game for as long as it took somebody to notice the debugger was
    // showing nothing at all.
    //-------------------------------------------------------------------------
    [Fact]
    public void FindingTheModsFiles()
    {
        string managed = Path.Combine("mod_sharp", "bin", "managed");

        Assert.True(ModLayout.Above(managed, 2) == "mod_sharp", "the mod is two levels above the managed assemblies");

        // Rather than the working directory, which is what "" turns into the
        // moment it is combined with anything.
        Assert.True(ModLayout.Above("managed", 2) == "", "an unknown layout says so instead of guessing");

        // The bootstrap is the only thing that knows, because assemblies are
        // loaded from memory and have no Location to ask. It tells the side's
        // engine where it loaded from, and the layout reads that side's host:
        // a side of this test's own, not the process's (plan_abi_hotreload D8).
        var server = new EngineSide(Side.Server)
        {
            ManagedDirectory = Path.Combine("elsewhere", "bin", "managed"),
        };
        var layout = new ModLayout(() => ((IEngineHost)new EngineHost(server)).ManagedDirectory);

        Assert.True(layout.ManagedDirectory == Path.Combine("elsewhere", "bin", "managed")
            && layout.ModDirectory == "elsewhere", "the bootstrap's answer is the one used");

        Assert.True(new MapSource(layout).MapDirectory == Path.Combine("elsewhere", "maps"), "and the maps are read from beside it");

        Assert.True(new ConsoleTail(new LogRing(4), layout, new RecordingLog().Warning).Path
                == Path.Combine("elsewhere", ConsoleTail.FileName), "as is the console log");
    }

    //-------------------------------------------------------------------------
    // Two servers over two compositions each ask their OWN engine
    // (plan_abi_hotreload D8). The dev API used to reach the active side's
    // default areas through a static holder; it is handed the container of
    // the side that loads it, so a route on one server is a call on that
    // server's engine and never on the other's.
    //-------------------------------------------------------------------------
    [Fact]
    public void EachServerAsksTheEngineItWasHanded()
    {
        RecordingEngine first = AimEngine(new EntityHandle(7));
        RecordingEngine second = AimEngine(new EntityHandle(9));

        using var a = DebugApiServer.ForTest(TestDebugHooks.Server(engine: EngineOf(first)));
        using var b = DebugApiServer.ForTest(TestDebugHooks.Server(engine: EngineOf(second)));

        string aimed = OnAPumpedServer(a, s => Reply(s, "GET", "/player/aim?player=3").Body);

        Assert.True(aimed.Contains("no player in slot 3"), "the route reached an engine and read its answer");
        Assert.True(first.CountOf("Players.GetHandle") == 1, "the first server asked its own engine for the player");
        Assert.True(first.Calls.Any(c => c.Member == "Entities.IsValid" && Equals(c.Args[0], new EntityHandle(7))),
                    "and checked the handle that engine answered");
        Assert.True(second.CountOf("Players.GetHandle") == 0 && second.CountOf("Entities.IsValid") == 0,
                    "the second server's engine heard nothing of it");

        OnAPumpedServer(b, s => Reply(s, "GET", "/player/aim?player=4").Body);

        Assert.True(second.CountOf("Players.GetHandle") == 1
            && second.Calls.Any(c => c.Member == "Entities.IsValid" && Equals(c.Args[0], new EntityHandle(9))),
            "the second server asks its own engine, about the handle its own engine answered");
        Assert.True(first.CountOf("Players.GetHandle") == 1, "and the first engine was not asked again");
    }

    private static RecordingEngine AimEngine(EntityHandle player)
        => TestServices.Mock()
            .Returns("Host.ManagedDirectory", string.Empty)
            .Returns("Players.GetHandle", player)
            .Returns("Entities.IsValid", false)
            .Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());

    private static ServiceContainer EngineOf(RecordingEngine engine)
    {
        var services = TestServices.Areas(engine);
        services.AddInstance<IGameClock>(engine);
        return services;
    }

    //-------------------------------------------------------------------------
    // Two layouts over two sides' hosts each answer their own side's
    // directory (plan_abi_hotreload D8): what a server reads its maps and log
    // from is the engine its composition handed it, never a shared one.
    //-------------------------------------------------------------------------
    [Fact]
    public void EachLayoutReadsItsOwnSidesHost()
    {
        var first = new EngineSide(Side.Server) { ManagedDirectory = Path.Combine("first", "bin", "managed") };
        var second = new EngineSide(Side.Server) { ManagedDirectory = Path.Combine("second", "bin", "managed") };

        var a = new ModLayout(() => ((IEngineHost)new EngineHost(first)).ManagedDirectory);
        var b = new ModLayout(() => ((IEngineHost)new EngineHost(second)).ManagedDirectory);

        Assert.True(a.ModDirectory == "first", "the first layout answers its own side's directory");
        Assert.True(b.ModDirectory == "second", "and the second its own, not the first's");
    }

    //-------------------------------------------------------------------------
    // Following the game's console log.
    //-------------------------------------------------------------------------
    [Fact]
    public void FollowingTheConsoleLog()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ss-console-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, ConsoleTail.FileName);

        try
        {
            var ring = new LogRing(64);
            // Polled every 20 ms rather than the game's 200: the waits below
            // are for a poll to notice a write, not for time to pass.
            using var tail = new ConsoleTail(ring, path, new RecordingLog().Warning) { PollInterval = TimeSpan.FromMilliseconds(20) };

            tail.Start();

            // The page shows an empty feed either way; this flag is the only
            // thing that can tell the reader which kind of empty it is, and it
            // used to say "following" for a file that was never there.
            Assert.True(!tail.IsFollowing, "a log that does not exist is not being followed");

            File.WriteAllText(path, "a line\nanother\n");

            bool caught = Wait(() => ring.Since(0).Count >= 2);

            Assert.True(caught, "a log that appears later is picked up");
            Assert.True(tail.IsFollowing, "and is reported as followed once it is");

            var lines = ring.Since(0);
            Assert.True(lines.Count >= 2 && lines[0].Text == "a line", "its lines arrive whole");

            // A warning in the console is the line worth finding, and the feed
            // marks them so the page can filter to them.
            File.AppendAllText(path, "Warning: something\n");
            Assert.True(Wait(() => ring.Since(0).Any(line => line.IsWarning)), "warnings are marked");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* a temp dir */ }
        }
    }

    /// <summary>Waits briefly for the tail's own thread to catch up.</summary>
    private static bool Wait(Func<bool> until)
    {
        for (int i = 0; i < 100; i++)
        {
            if (until()) return true;
            Thread.Sleep(20);
        }

        return false;
    }

    [Fact]
    public void JsonEscaping()
    {
        Assert.True(DebugApiServer.Quote("hello") == "\"hello\"", "plain text is quoted");

        // Log lines carry paths and quoted names constantly, and one unescaped
        // character breaks the whole response rather than just that line.
        Assert.True(DebugApiServer.Quote("say \"hi\"") == "\"say \\\"hi\\\"\"", "quotes are escaped");
        Assert.True(DebugApiServer.Quote(@"a\b") == "\"a\\\\b\"", "backslashes are escaped");
        Assert.True(DebugApiServer.Quote("a\nb") == "\"a\\nb\"", "newlines are escaped");
        Assert.True(DebugApiServer.Quote("a\tb") == "\"a\\tb\"", "tabs are escaped");

        string control = "a" + (char)1 + "b";
        Assert.True(DebugApiServer.Quote(control) == "\"a\\u0001b\"", "control characters become escapes");
    }

    // ---- the socket directory, and why it is overridable ------------------
    //
    // SocketDirectory is one path for the whole machine, so two live runs in
    // different worktrees meet in it. A tool that launches a game and adopts
    // "whichever socket appeared" then adopts someone else's: three parallel
    // probes each printed the same six pids and killed each other's games.
    // DEBUG_API_DIR gives a run its own directory. These facts are
    // what stop the variable being read by the launcher but not by the game,
    // which would leave the clash in place with every command line looking
    // right.

    // The environment is read once, where the server is composed
    // (DebugApiDirectories.FromEnvironment); these drive that reading through
    // a lookup the fact supplies, so no fact moves the process's variables.
    private static DebugApiDirectories Env(string name, string? value)
        => DebugApiDirectories.From(n => n == name ? value : null);

    [Fact]
    public void SocketDirectoryDefaultsToTheSharedPathWhenTheVariableIsUnset()
    {
        Assert.Equal(DebugApiServer.DefaultSocketDirectory, Env(DebugApiServer.SocketDirectoryVar, null).SocketDirectory);
    }

    [Fact]
    public void SocketDirectoryDefaultsToTheSharedPathWhenTheVariableIsEmpty()
    {
        // An exported-but-empty variable is what a shell leaves behind when an
        // expansion produced nothing. It must read as "unset", not as "".
        Assert.Equal(DebugApiServer.DefaultSocketDirectory, Env(DebugApiServer.SocketDirectoryVar, "").SocketDirectory);
    }

    [Fact]
    public void SocketDirectoryFollowsTheEnvironmentVariable()
    {
        // By its name: DEBUG_API_DIR, the one the launcher and tools export.
        Assert.Equal("DEBUG_API_DIR", DebugApiServer.SocketDirectoryVar);
        Assert.Equal("/tmp/ss-debug-api-lane-7", Env("DEBUG_API_DIR", "/tmp/ss-debug-api-lane-7").SocketDirectory);
    }

    [Fact]
    public void TwoDifferentDirectoriesGiveTheSamePidDifferentSocketPaths()
    {
        // The whole point: the pid keeps instances apart WITHIN a directory,
        // and the directory keeps concurrent runs apart from each other.
        string a = DebugApiServer.ForTest(TestDebugHooks.Server(), Env("DEBUG_API_DIR", "/tmp/ss-debug-api-a")).SocketPathFor(4242);
        string b = DebugApiServer.ForTest(TestDebugHooks.Server(), Env("DEBUG_API_DIR", "/tmp/ss-debug-api-b")).SocketPathFor(4242);
        Assert.NotEqual(a, b);
        Assert.Equal("/tmp/ss-debug-api-a/4242.sock", a);
        Assert.Equal("/tmp/ss-debug-api-b/4242.sock", b);
    }

    [Fact]
    public void EachServerUsesTheDirectoryItWasHanded()
    {
        // SocketDirectory was a const, then a static property read from the
        // environment on every call. It is the server's own now: two servers
        // in one process, handed two directories, each keep their own.
        var first = DebugApiServer.ForTest(TestDebugHooks.Server(), Env("DEBUG_API_DIR", "/tmp/ss-debug-api-first"));
        var second = DebugApiServer.ForTest(TestDebugHooks.Server(), Env("DEBUG_API_DIR", "/tmp/ss-debug-api-second"));
        Assert.Equal("/tmp/ss-debug-api-first/1.sock", first.SocketPathFor(1));
        Assert.Equal("/tmp/ss-debug-api-second/1.sock", second.SocketPathFor(1));
    }

    [Fact]
    public void TheMapsDirectoryVariableNamesTheMapsOutright()
    {
        Assert.Equal("SOURCESHARP_MAPS", MapSource.DirectoryVariable);
        Assert.Equal("/srv/maps", Env("SOURCESHARP_MAPS", "/srv/maps").MapDirectory);
        Assert.Null(Env("SOURCESHARP_MAPS", "  ").MapDirectory);
        Assert.Null(Env("SOURCESHARP_MAPS", null).MapDirectory);
    }

}
