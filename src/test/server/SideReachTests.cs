using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The transitive walk's own facts, on synthetic assemblies. The real-tree
/// audit lives in <see cref="SideAuditTests"/>; these prove the walk can see
/// what the direct audit could not: a side calling a helper that calls a
/// server-only member.
/// </summary>
public class SideReachTests
{
    private const string Helper = """
        namespace SourceSharp.Abi
        {
            public interface IEngineCollisionRules { void RegisterCollisionRules(uint[] rows); }

            public static class CollisionRules
            {
                public static void Register(IEngineCollisionRules engine) => Push(engine);
                public static void Register(int unused) { }
                private static void Push(IEngineCollisionRules engine) => engine.RegisterCollisionRules(new uint[1]);

                public static System.Action Later(IEngineCollisionRules engine) => () => engine.RegisterCollisionRules(new uint[2]);

                public static int Pure(int x) => x + 1;
            }
        }
        """;

    private static readonly Dictionary<(string, string), HashSet<string>> Binding = new()
    {
        [("IEngineCollisionRules", "RegisterCollisionRules")] = new(StringComparer.Ordinal) { "RegisterCollisionRules" },
    };

    private static readonly Dictionary<string, HashSet<string>> Wrappers = new(StringComparer.Ordinal)
    {
        ["RegisterCollisionRules"] = new(StringComparer.Ordinal) { "RegisterCollisionRules" },
    };

    private static readonly Dictionary<string, SlotSide> Sides = new(StringComparer.Ordinal)
    {
        ["RegisterCollisionRules"] = SlotSide.Server,
    };

    private static bool ClientAllows(SlotSide side) => side is SlotSide.Client or SlotSide.Both or SlotSide.Dual;

    [Fact]
    public void AClientCallingAHelperThatReachesAServerOnlySlotIsNamedWithItsPath()
    {
        string dir = Pair("SourceSharp.Abi.CollisionRules.Register(e);");

        var reach = SideReach.EngineReach(Path.Combine(dir, "Synth.Client.dll"),
            new HashSet<string> { "Synth.Abi" }, out var unresolved);
        var violations = SideReach.ReachViolations(reach, Binding, Wrappers, Sides, ClientAllows);

        Assert.True(unresolved.Count == 0, "every helper resolved: " + string.Join("; ", unresolved));
        Assert.True(violations.SequenceEqual(new[]
            {
                "Synth.Client -> CollisionRules.Register -> CollisionRules.Push -> IEngineCollisionRules.RegisterCollisionRules"
                + " is backed by Server-only slot 'RegisterCollisionRules'",
            }),
            "the helper chain is one complaint naming every step: " + string.Join("; ", violations));
    }

    [Fact]
    public void TheDirectAuditCannotSeeThroughAHelper()
    {
        // The blind spot this walk exists for, kept as a fact: the client
        // assembly's own member references name no IEngine* member at all.
        string dir = Pair("SourceSharp.Abi.CollisionRules.Register(e);");

        var direct = SideAudit.EngineMemberRefs(Path.Combine(dir, "Synth.Client.dll"));

        Assert.True(direct.Count == 0, $"the direct audit sees no engine member ({direct.Count} found)");
    }

    [Fact]
    public void ALambdaInAHelperIsFollowedThroughItsDelegate()
    {
        string dir = Pair("SourceSharp.Abi.CollisionRules.Later(e)();");

        var reach = SideReach.EngineReach(Path.Combine(dir, "Synth.Client.dll"),
            new HashSet<string> { "Synth.Abi" }, out _);
        var violations = SideReach.ReachViolations(reach, Binding, Wrappers, Sides, ClientAllows);

        Assert.True(violations.Count == 1
                    && violations[0].StartsWith("Synth.Client -> CollisionRules.Later -> ", StringComparison.Ordinal)
                    && violations[0].Contains("IEngineCollisionRules.RegisterCollisionRules", StringComparison.Ordinal),
            "the lambda's body is reached from the helper that made it: " + string.Join("; ", violations));
    }

    [Fact]
    public void AnOverloadIsResolvedBySignature()
    {
        // Register(int) touches nothing; matching by name alone would charge it
        // with Register(IEngineCollisionRules)'s reach.
        string dir = Pair("SourceSharp.Abi.CollisionRules.Register(3);");

        var reach = SideReach.EngineReach(Path.Combine(dir, "Synth.Client.dll"),
            new HashSet<string> { "Synth.Abi" }, out var unresolved);

        Assert.True(unresolved.Count == 0, "every helper resolved: " + string.Join("; ", unresolved));
        Assert.True(reach.Count == 0, "the int overload reaches nothing: " + string.Join("; ", reach));
    }

    [Fact]
    public void AHelperThatReachesNoEngineMemberIsNoComplaint()
    {
        string dir = Pair("_ = SourceSharp.Abi.CollisionRules.Pure(1);");

        var reach = SideReach.EngineReach(Path.Combine(dir, "Synth.Client.dll"),
            new HashSet<string> { "Synth.Abi" }, out _);

        Assert.True(reach.Count == 0, "a pure helper reaches nothing: " + string.Join("; ", reach));
    }

    [Fact]
    public void AMissingHelperAssemblyIsAFailureNotAPass()
    {
        string dir = Pair("SourceSharp.Abi.CollisionRules.Register(e);");
        File.Delete(Path.Combine(dir, "Synth.Abi.dll"));

        Assert.Throws<FileNotFoundException>(() => SideReach.EngineReach(Path.Combine(dir, "Synth.Client.dll"),
            new HashSet<string> { "Synth.Abi" }, out _));
    }

    //-------------------------------------------------------------------------
    // A PURE FORWARDER IS PASS-THROUGH (P1 run 3, the integrator's ruling: no
    // split of IEngineEffects). A member whose whole body is
    // `Inner.Same(args)` on an instance of the interface it implements costs a
    // caller exactly what calling Inner would, so its own reference is not a
    // use. What counts is what a subclass overrides and calls, and what
    // callers call through it.
    //-------------------------------------------------------------------------

    private const string FxInterface = """
        namespace SourceSharp.Abi
        {
            public interface IEngineFx
            {
                void ServerFx(int a);
                void ClientFx(int a, float b);
                int ClientCount();
                int ServerCount();
            }
        }
        """;

    /// <summary>A forwarder of every IEngineFx member; <paramref name="clientCount"/> is ClientCount's body.</summary>
    private static string Forwarder(string clientCount = "=> Inner.ClientCount();",
                                    string serverCount = "=> Inner.ServerCount();") => $$"""
        public abstract class FxForwarder : SourceSharp.Abi.IEngineFx
        {
            protected readonly SourceSharp.Abi.IEngineFx Inner;
            protected FxForwarder(SourceSharp.Abi.IEngineFx inner) => Inner = inner;
            public virtual void ServerFx(int a) => Inner.ServerFx(a);
            public virtual void ClientFx(int a, float b) => Inner.ClientFx(a, b);
            public virtual int ClientCount() {{clientCount}}
            public virtual int ServerCount() {{serverCount}}
        }
        """;

    private static string Over(string serverFxBody) => $$"""
        public sealed class Over : FxForwarder
        {
            public Over(SourceSharp.Abi.IEngineFx inner) : base(inner) { }
            public override void ServerFx(int a) { {{serverFxBody}} }
        }
        """;

    private static readonly Dictionary<(string, string), HashSet<string>> FxBinding = new()
    {
        [("IEngineFx", "ServerFx")] = new(StringComparer.Ordinal) { "ServerFx" },
        [("IEngineFx", "ClientFx")] = new(StringComparer.Ordinal) { "ClientFx" },
        [("IEngineFx", "ClientCount")] = new(StringComparer.Ordinal) { "ClientCount" },
        [("IEngineFx", "ServerCount")] = new(StringComparer.Ordinal) { "ServerCount" },
    };

    private static readonly Dictionary<string, HashSet<string>> FxWrappers = new(StringComparer.Ordinal)
    {
        ["ServerFx"] = new(StringComparer.Ordinal) { "ServerFx" },
        ["ClientFx"] = new(StringComparer.Ordinal) { "ClientFx" },
        ["ClientCount"] = new(StringComparer.Ordinal) { "ClientCount" },
        ["ServerCount"] = new(StringComparer.Ordinal) { "ServerCount" },
    };

    private static readonly Dictionary<string, SlotSide> FxSides = new(StringComparer.Ordinal)
    {
        ["ServerFx"] = SlotSide.Server,
        ["ClientFx"] = SlotSide.Client,
        ["ClientCount"] = SlotSide.Client,
        ["ServerCount"] = SlotSide.Server,
    };

    private static bool ServerAllows(SlotSide side) => side is SlotSide.Server or SlotSide.Both or SlotSide.Dual;

    /// <summary>The server's violations, with the forwarder and its subclass compiled INTO the audited assembly.</summary>
    private static List<string> ServerViolations(string serverSource)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ss-sidereach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string helperPath = Path.Combine(dir, "Synth.Abi.dll");
        Emit("Synth.Abi", FxInterface, Platform, helperPath);
        Emit("Synth.Server", serverSource, Platform.Append(MetadataReference.CreateFromFile(helperPath)).ToArray(),
             Path.Combine(dir, "Synth.Server.dll"));

        var reach = SideReach.EngineReach(Path.Combine(dir, "Synth.Server.dll"),
            new HashSet<string> { "Synth.Abi" }, out var unresolved);
        Assert.True(unresolved.Count == 0, "every helper resolved: " + string.Join("; ", unresolved));
        Assert.True(reach.Count > 0, $"the server reaches the engine at all ({reach.Count} found)");
        return SideReach.ReachViolations(reach, FxBinding, FxWrappers, FxSides, ServerAllows);
    }

    [Fact]
    public void AServerClassOverAPureForwarderIsCleanWhenItNeverCallsTheClientMembers()
    {
        var violations = ServerViolations(Forwarder() + Over("Inner.ServerFx(a + 1);"));

        Assert.True(violations.Count == 0,
            "the forwarder's own forwards are not uses: " + string.Join("; ", violations));
    }

    [Fact]
    public void ASubclassCallingAClientMemberThroughBaseIsFlaggedThroughTheForwarder()
    {
        var violations = ServerViolations(Forwarder() + Over("base.ClientFx(a, 1f);"));

        Assert.True(violations.SequenceEqual(new[]
            {
                "Synth.Server -> FxForwarder.ClientFx -> IEngineFx.ClientFx is backed by Client-only slot 'ClientFx'",
            }),
            "calling base costs what calling Inner would: " + string.Join("; ", violations));
    }

    [Fact]
    public void ASubclassCallingAClientMemberThroughInnerIsFlagged()
    {
        var violations = ServerViolations(Forwarder() + Over("Inner.ClientFx(a, 2f);"));

        Assert.True(violations.SequenceEqual(new[] { "IEngineFx.ClientFx is backed by Client-only slot 'ClientFx'" }),
            "a direct call on Inner is the assembly's own use: " + string.Join("; ", violations));
    }

    [Fact]
    public void ACallerCallingAClientMemberThroughTheForwarderIsFlagged()
    {
        var violations = ServerViolations(Forwarder() + Over("Inner.ServerFx(a);")
            + "public static class Caller { public static int Go(Over o) => o.ClientCount(); }");

        Assert.True(violations.SequenceEqual(new[]
            {
                "Synth.Server -> FxForwarder.ClientCount -> IEngineFx.ClientCount is backed by Client-only slot 'ClientCount'",
            }),
            "a caller of the forwarder is charged with what it forwards to: " + string.Join("; ", violations));
    }

    [Fact]
    public void AForwarderMemberThatDoesMoreThanForwardIsNotPassThrough()
    {
        var violations = ServerViolations(
            Forwarder("=> Inner.ClientCount() + 1;") + Over("Inner.ServerFx(a);"));

        Assert.True(violations.SequenceEqual(new[] { "IEngineFx.ClientCount is backed by Client-only slot 'ClientCount'" }),
            "a body with work besides the forward is a use; its siblings stay pass-through: " + string.Join("; ", violations));
    }

    [Fact]
    public void AForwarderMemberThatForwardsToAnotherMemberIsNotPassThrough()
    {
        // Same shape, same signature, another member: ServerCount answering
        // with the client's count is a use of ClientCount, not a forward.
        var violations = ServerViolations(
            Forwarder(serverCount: "=> Inner.ClientCount();") + Over("Inner.ServerFx(a);"));

        Assert.True(violations.SequenceEqual(new[] { "IEngineFx.ClientCount is backed by Client-only slot 'ClientCount'" }),
            "ServerCount calling ClientCount is not ServerCount forwarding: " + string.Join("; ", violations));
    }

    //-------------------------------------------------------------------------
    // THE AUDIT'S OWN CHAIN IS PASS-THROUGH IN THE AUDITED ASSEMBLY (P1 run 3,
    // the integrator's ruling 1). EngineSide's wrappers are the wrapper map and
    // the IEngine* binding classes are the binding map: when they sit in the
    // assembly being audited (Shared.Interop, after Phase 1), their bodies are
    // not uses, and the assembly's OTHER bodies are charged for their internal
    // calls into EngineSide or IEngine* members exactly as member references
    // were charged when the chain lived in Abi.
    //-------------------------------------------------------------------------

    private const string SlotsHelper = """
        namespace SourceSharp.Abi
        {
            public sealed class ShimAreas
            {
                public void ServerOnly() { }
                public void Both() { }
            }
        }
        """;

    private const string ChainSource = """
        namespace SourceSharp.Abi
        {
            public interface IEngineThing { void ServerOnly(); void Both(); }

            public sealed class EngineSide
            {
                private readonly ShimAreas _areas = new();
                public void ServerOnly() => _areas.ServerOnly();
                public void Both() => _areas.Both();
            }

            internal sealed class EngineThing : IEngineThing
            {
                private readonly EngineSide E = new();
                public void ServerOnly() => E.ServerOnly();
                public void Both() => E.Both();
            }
        }
        """;

    private static readonly Dictionary<(string, string), HashSet<string>> ThingBinding = new()
    {
        [("IEngineThing", "ServerOnly")] = new(StringComparer.Ordinal) { "ServerOnly" },
        [("IEngineThing", "Both")] = new(StringComparer.Ordinal) { "Both" },
    };

    private static readonly Dictionary<string, HashSet<string>> ThingWrappers = new(StringComparer.Ordinal)
    {
        ["ServerOnly"] = new(StringComparer.Ordinal) { "ServerOnly" },
        ["Both"] = new(StringComparer.Ordinal) { "Both" },
    };

    private static readonly Dictionary<string, SlotSide> ThingSides = new(StringComparer.Ordinal)
    {
        ["ServerOnly"] = SlotSide.Server,
        ["Both"] = SlotSide.Both,
    };

    private static readonly HashSet<string> ThingChain = new(StringComparer.Ordinal) { "EngineSide", "EngineThing" };

    private static bool BothAllow(SlotSide side) => side is SlotSide.Both or SlotSide.Dual;

    /// <summary>
    /// Compiles Synth.Abi (the slots), Synth.Interop (the chain plus a User
    /// class running <paramref name="interopBody"/>) and, when given, a second
    /// assembly Synth.Shared running <paramref name="sharedBody"/>; audits
    /// <paramref name="audited"/> for slots both sides fill.
    /// </summary>
    private static List<string> ChainViolations(string interopBody, string? sharedBody = null, string audited = "Synth.Interop")
    {
        string dir = Path.Combine(Path.GetTempPath(), "ss-sidereach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string abi = Path.Combine(dir, "Synth.Abi.dll");
        Emit("Synth.Abi", SlotsHelper, Platform, abi);
        string interop = Path.Combine(dir, "Synth.Interop.dll");
        Emit("Synth.Interop",
             ChainSource + "public static class User { public static void Go(SourceSharp.Abi.IEngineThing t) { " + interopBody + " } }",
             Platform.Append(MetadataReference.CreateFromFile(abi)).ToArray(), interop);
        if (sharedBody is not null)
        {
            Emit("Synth.Shared",
                 "public static class Other { public static void Go(SourceSharp.Abi.IEngineThing t) { " + sharedBody + " } }",
                 Platform.Append(MetadataReference.CreateFromFile(abi)).Append(MetadataReference.CreateFromFile(interop)).ToArray(),
                 Path.Combine(dir, "Synth.Shared.dll"));
        }

        var reach = SideReach.EngineReach(Path.Combine(dir, audited + ".dll"),
            new HashSet<string> { "Synth.Abi", "Synth.Interop" }, out var unresolved, ThingChain);
        Assert.True(unresolved.Count == 0, "every helper resolved: " + string.Join("; ", unresolved));
        Assert.True(reach.Count > 0, $"{audited} reaches the engine at all ({reach.Count} found)");
        return SideReach.ReachViolations(reach, ThingBinding, ThingWrappers, ThingSides, BothAllow);
    }

    [Fact]
    public void AWrapperBodyInsideTheAuditedAssemblyIsNotCharged()
    {
        var violations = ChainViolations("t.Both();");

        Assert.True(violations.Count == 0,
            "EngineSide's and the binding's own bodies are the chain, not uses: " + string.Join("; ", violations));
    }

    [Fact]
    public void AnInternalCallThroughTheInterfaceToAOneSideMemberIsCharged()
    {
        var violations = ChainViolations("t.ServerOnly();");

        Assert.True(violations.SequenceEqual(new[] { "IEngineThing.ServerOnly is backed by Server-only slot 'ServerOnly'" }),
            "a non-chain body's internal interface call is charged as a member reference was: " + string.Join("; ", violations));
    }

    [Fact]
    public void AnInternalCallToAOneSideWrapperIsCharged()
    {
        var violations = ChainViolations("new SourceSharp.Abi.EngineSide().ServerOnly();");

        Assert.True(violations.SequenceEqual(new[] { "Synth.Interop -> EngineSide.ServerOnly is backed by Server-only slot 'ServerOnly'" }),
            "a non-chain body's internal wrapper call is charged: " + string.Join("; ", violations));
    }

    [Fact]
    public void TheSameCallFromASecondAssemblyIsStillChargedThroughMemberRefs()
    {
        var violations = ChainViolations("t.Both();", sharedBody: "t.ServerOnly();", audited: "Synth.Shared");

        Assert.True(violations.SequenceEqual(new[] { "IEngineThing.ServerOnly is backed by Server-only slot 'ServerOnly'" }),
            "a second assembly's member reference is charged: " + string.Join("; ", violations));
    }

    //-------------------------------------------------------------------------

    // The runtime's own assemblies only: the test directory's SourceSharp.Abi
    // would clash with the synthetic one.
    private static readonly MetadataReference[] Platform =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.Length > 0
                        && Path.GetDirectoryName(p) == Path.GetDirectoryName(typeof(object).Assembly.Location))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();

    /// <summary>
    /// Compiles the helper as Synth.Abi and a client whose one method runs
    /// <paramref name="clientBody"/> with an <c>e</c> in scope, both into a
    /// fresh directory.
    /// </summary>
    private static string Pair(string clientBody)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ss-sidereach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string helperPath = Path.Combine(dir, "Synth.Abi.dll");
        Emit("Synth.Abi", Helper, Platform, helperPath);

        string client = "public static class Caller { public static void Go(SourceSharp.Abi.IEngineCollisionRules e) { "
                      + clientBody + " } }";
        Emit("Synth.Client", client, Platform.Append(MetadataReference.CreateFromFile(helperPath)).ToArray(),
             Path.Combine(dir, "Synth.Client.dll"));
        return dir;
    }

    private static void Emit(string name, string source, MetadataReference[] refs, string path)
    {
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        Assert.True(result.Success, $"{name} compiles: " + string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }
}
