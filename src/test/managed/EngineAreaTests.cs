extern alias client;
using System.Reflection;
using System.Text.RegularExpressions;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine split into areas, the real binding behind each, and the double a
/// test hands to an entity instead.
///
/// <para>
/// The parity check at the bottom is the one that matters most over time.
/// Everything else here proves the machinery works today; that one fails the
/// build the day somebody adds a member to <c>Engine</c> and forgets to
/// put it on an area — which would otherwise show up as an entity reaching for
/// the static again, compiling, working, and being untestable.
/// </para>
/// </summary>
public class EngineAreaTests
{
    /// <summary>
    /// Members of <c>Engine</c> that are deliberately NOT on an area.
    ///
    /// The first two are infrastructure rather than calls into the game.
    /// FromUtf8 decodes a raw pointer, which cannot cross an interface and is
    /// not an engine call in the first place. SetActiveSide marks which side is
    /// executing, and it has to run before anything can resolve out of a
    /// container — so a container cannot be what supplies it.
    ///
    /// <para>
    /// <b>EntityIsValidBySlot is off an area ON PURPOSE, and being off one is
    /// its entire function</b> (plan_crossings.md Phase X5). It is the
    /// unmirrored <c>EntityIsValid</c>, kept callable so
    /// <c>cs_sv_liveness_check</c> has an INDEPENDENT oracle to hold the
    /// liveness mirror against. Putting it on a binding is precisely what would
    /// let game code reach it and start answering liveness questions from the
    /// slot the phase removed — and a divergence gate whose oracle game code
    /// also uses stops being independent. Reached through
    /// <c>LivenessSlotReference</c>, which says the same thing at its own call
    /// site; <c>StandaloneSlotReference</c> is the X2/X3 precedent.
    /// </para>
    /// </summary>
    private static readonly string[] StaticByDesign =
        { "FromUtf8", "SetActiveSide", "EntityIsValidBySlot",

          // The thread's side marker (a forwarder to ActiveSide since D5). No
          // binding forwards it since plan_abi_hotreload D8: IEngineHost's
          // ActiveSide answers the side its binding is bound to.
          "ActiveSide",

          // THE VTABLE BINDING'S FIVE NATIVE SLOTS, and they are on this list
          // rather than on an area because an area is a CAPABILITY GAME CODE
          // ASKS FOR and these are the mechanism underneath one.
          //
          // AcquireInterface is called by Forwarders.TryAcquire and by
          // nothing else ever; the other four are C++ proxies for calls a fixed unmanaged
          // signature cannot spell, reached only through the one-line
          // delegations on EngineClient. Game code asks IVtableEngineClient for
          // a level name -- it never asks Engine for a vtable.
          //
          // Putting them on an area would publish the raw interface pointer to
          // every consumer of that area, which is the opposite of what the impl
          // classes exist to prevent.
          "AcquireInterface", "ConNPrintf", "ConNXPrintf",
          "TryGetLightForPoint", "TryGetLightForPointFast",

          // plan_entity.md step 2c. DestroyShells is the mechanism under
          // Entities.Remove, not a capability beside it: game code asks for an
          // entity to be REMOVED, and the directory decides when the shell
          // behind it is freed and frees the whole frame's worth in one call.
          // EntityDirectory.ShellDestroyer is the only caller there will ever
          // be. On an area it would publish "free this native object now" to
          // every consumer of IEngineEntities, which is exactly the deferral
          // this step exists to make impossible to skip.
          "DestroyShells",

          // plan_shim.md Phase 3, lane 3c-entityshell. THE OPPOSITE OF THE
          // ENTRIES ABOVE: these three are not mechanism under a capability,
          // they are RETIRED. Their native bodies are gone, their slots answer
          // NULL, and the IEnginePlayers methods that used to forward to them
          // (GetWeaponAmmo, SetWeaponClip, TakeAmmo) were removed with the
          // bodies -- which is why this test noticed. See
          // docs/abi-retirements.md for the 0-caller measurement.
          //
          // The Engine.cs wrappers stay because the ABI protocol says so: a
          // slot removed from the middle of sourcesharp.h renumbers every slot
          // after it and AbiParityTests pins names in order, so removals are
          // batched into one serialised pass. THE PASS THAT REMOVES THE FIELDS
          // DELETES THESE THREE NAMES FROM THIS LIST -- leaving them would
          // waive a member that no longer exists, and this list's whole value
          // is that every name on it had to be argued for.
          "GetPlayerWeaponAmmo", "SetPlayerWeaponClip", "TakePlayerAmmo" };

    // EngineServices.IsArea rather than a second copy of the rule. The two
    // copies disagreeing is precisely how an area gets registered that this
    // file cannot see -- and there IS an exception to the naming rule now
    // (IEngineStringTable, the per-table handle), so the rule has a body worth
    // sharing rather than one line worth repeating.
    private static Type[] Areas()
        => typeof(EngineServices).Assembly.GetTypes()
            .Where(EngineServices.IsArea)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// plan_abi_hotreload D8: every binding is built over the side it serves.
    /// A parameterless constructor could only find its side by following the
    /// active one, which is the process-wide reach two instances cannot share.
    /// </summary>
    [Fact]
    public void NoBindingCanBeBuiltWithoutItsSide()
    {
        var composition = new ServiceContainer().AddInstance(new StandaloneMirror());
        var bindings = EngineServices.Bindings(new EngineSide(Side.Server), composition).ToArray();
        Assert.True(bindings.Length > 20, $"{bindings.Length} bindings reflected over");

        var sideless = bindings
            .Select(b => b.Binding.GetType())
            .Where(t => t.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                                         Type.EmptyTypes) is not null)
            .Select(t => t.Name)
            .ToArray();

        Assert.True(sideless.Length == 0, $"bindings with a parameterless constructor: {string.Join(", ", sideless)}");
    }

    [Fact]
    public void EveryAreaHasABinding()
    {
        var areas = Areas();
        // 29 since IEngineFiles, plan_gamedll_after.md Phase A3: reading a GAME
        // SCRIPT off the engine's own search paths.
        //
        // It exists because of a measurement rather than for symmetry. A3 moves
        // CDecalEmitterSystem and half of CPropData into managed code and both
        // begin with a KeyValues parse -- which Part 9a's ownership test sends
        // to C# -- while FINDING the file stays IFileSystem's. And that half is
        // load-bearing: scripts/decals_subrect.txt and scripts/propdata.txt are
        // in NEITHER the mod nor any mounted VPK, they are loose files under
        // the SDK's hl2/scripts, so a managed System.IO read finds nothing and
        // finds it silently.
        // Before that, 28 since IEngineStringTables, plan_gamedll.md Part 13 /
        // plan_gamedll_after.md Phase A8: the route by which a MOD declares a
        // network string table of its own. ADDITIVE -- the seven shared tables
        // stay native and 141 native references keep reading the globals behind
        // them -- so this area is not a port of anything, it is the managed half
        // of the CreateNetworkStringTables slot that was reserved for it.
        // Before that, 27 since IEngineFrame, plan_gamedll.md Part 4's GAMEFRAME steps. The
        // SECOND area whose members are steps rather than capabilities, and it
        // exists for the same reason IEngineLevel does: the frame's order is the
        // most-read thing in a game DLL and it runs sixty-six times a second, so
        // it belongs in ServerGameDll.cs where it can be read and tested rather
        // than in C++. Before that, 26 since IEngineLevel, plan_gamedll.md
        // Part 4's map-load steps -- the
        // one area whose members are STEPS rather than capabilities, so that
        // LevelInit's order can live in ServerGameDll.cs instead of in C++.
        // Before that, 25 since IEngineEnvironment, the abstract environment of
        // plan_gamedll.md Part 0a #1: the engine's interface factory and its
        // live CGlobalVars. It is an area rather than something beside them
        // because it is what a class touches to reach the engine at all, and
        // because the naming rule ENROLS it -- IEngineFoo is bound to EngineFoo
        // by reflection, so an interface named this way is an area whether or
        // not anybody meant it to be. Before that, 24 since IEngineCollisionRules,
        // which pushes the collision matrix the way IEngineGameRulesPolicy pushes
        // the rules' constants and IEngineDamage pushes the damage masks.
        // 30 since IEngineDiagnostics (debug-api lane, 2026-10-04): the
        // player-shadow counters and the exit recorders, which only native
        // code can hold. 27 since plan_physics_bones_native_zero 2.4: the
        // constraint, spring and motion-controller tables are the server's own
        // (PhysicsConstraints, PhysicsMotionControllers), not engine areas.
        Assert.True(areas.Length == 27, "the engine is split into areas");

        // Paired by name, and the pairing is reflected over rather than listed —
        // so this is what catches an area added with no binding behind it.
        // Built over a container holding what a side's composition registers
        // before its areas (the render binding's mirror, plan_abi_hotreload D7).
        var composition = new ServiceContainer().AddInstance(new StandaloneMirror());
        var bindings = EngineServices.Bindings(new EngineSide(Side.Server), composition).ToArray();

        Assert.True(bindings.Length == areas.Length, "every area has a binding");

        foreach (var (contract, binding) in bindings)
        {
            if (contract.IsInstanceOfType(binding))
                continue;

            Assert.Fail($"{contract.Name}'s binding does not implement it");
            return;
        }

        Assert.True(true, "and every binding implements the area it is paired with");

        // internal, so nothing outside the ABI assembly can name one. A class
        // holding EngineEntities rather than IEngineEntities would compile, work
        // and be untestable — and be the one nobody noticed.
        Assert.True(bindings.All(b => !b.Binding.GetType().IsPublic), "no binding is visible outside the ABI assembly");
    }

    [Fact]
    public void TheContainerGetsThemAll()
    {
        // The render binding's mirror first, as a side's composition registers
        // it (plan_abi_hotreload D7).
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        int added = EngineServices.AddTo(services, new EngineSide(Side.Server));

        Assert.True(added == Areas().Length, "registering the engine adds every area");

        // By interface, and resolvable as one. Registering the concrete class
        // would let a caller ask for it by name and defeat the whole thing.
        Assert.True(services.Resolve<IEngineEntities>() is not null, "an area resolves from the container");
        Assert.True(services.Resolve<IEngineScripted>() is not null, "and so does the smallest one");

        // The listing the services view reads: an area must appear there like
        // any other contract, or the view would answer "what may an entity ask
        // for" with everything except the engine.
        var described = services.Describe();
        Assert.True(described.Any(row => row.Key == "IEngineEntities" && row.Kind == "instance"), "the areas appear in the container's own listing");
    }

    [Fact]
    public void TheDoubleCoversEveryArea()
    {
        var fake = new RecordingEngine();

        foreach (var area in Areas())
        {
            if (area.IsInstanceOfType(fake))
                continue;

            Assert.Fail($"the double does not implement {area.Name}");
            return;
        }

        Assert.True(true, "the double implements every area");
    }

    [Fact]
    public void RecordingWhatWasAsked()
    {
        // Ignoring the reads: this test is about the LOG, not the answers.
        var fake = new RecordingEngine().Ignores("WorldQuery.EntitiesInSphere", "Entities.GetHealth", "Entities.GetName");
        var entities = (IEngineEntities)fake;
        var handle = new EntityHandle(0x0042_0001u);

        entities.SetHealth(handle, 25);

        Assert.True(fake.Called("Entities.SetHealth"), "a call is recorded");
        Assert.True(fake.Last("Entities.SetHealth") is { } call
            && call.ArgAs<EntityHandle>(0) == handle
            && call.ArgAs<int>(1) == 25, "with its arguments");

        // The assertion this whole exercise exists to make possible: not what
        // the entity computed, but what it asked the engine to do.
        Assert.True(!fake.Called("Entities.Remove"), "a call nobody made is not recorded");

        entities.SetHealth(handle, 50);
        Assert.True(fake.CountOf("Entities.SetHealth") == 2, "repeated calls are counted");
        Assert.True(fake.Last("Entities.SetHealth")!.Value.ArgAs<int>(1) == 50, "and the last one is the last one");
        Assert.True(fake.AllOf("Entities.SetHealth").Select(c => c.ArgAs<int>(1)).SequenceEqual(new[] { 25, 50 }), "and all of them are readable in order");

        // A span cannot be boxed into the log; its length can, and the length is
        // what a test asserting "it asked for room for four" wants.
        Span<EntityHandle> room = stackalloc EntityHandle[4];
        ((IEngineWorldQuery)fake).EntitiesInSphere(default, 128f, room);
        Assert.True(fake.Last("WorldQuery.EntitiesInSphere")!.Value.ArgAs<int>(2) == 4, "a span argument is recorded as its length");

        Assert.True(Cleared(fake), "clearing forgets the calls");
    }

    private static bool Cleared(RecordingEngine fake)
    {
        fake.Clear();
        return fake.Calls.Count == 0;
    }

    [Fact]
    public void AnsweringWhatItWasTold()
    {
        // Strict OFF, because this is the test OF the non-strict path: what a
        // double answers when a test has said it does not care.
        var fake = new RecordingEngine { Strict = false };
        var entities = (IEngineEntities)fake;

        Assert.True(entities.GetHealth(default) == 0, "an unstubbed answer is the type's default");

        fake.Returns("Entities.GetHealth", 73);
        Assert.True(entities.GetHealth(default) == 73, "a stubbed answer comes back");
        Assert.True(fake.CountOf("Entities.GetHealth") == 2, "and the read is still recorded");

        // An out parameter is an ANSWER, so it comes from the stub table under
        // its own key rather than being recorded as an argument.
        fake.Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(1f, 2f, 3f));
        ((IEngineWorldQuery)fake).TracePlayerAim(default, 100f, out var hit);

        Assert.True(hit.X == 1f && hit.Y == 2f && hit.Z == 3f, "an out parameter is answered from the stub table");
        Assert.True(fake.Last("WorldQuery.TracePlayerAim")!.Value.Args.Length == 2, "and is not recorded as an argument");
    }

    /// <summary>
    /// Springs, constraints and motion controllers each declare
    /// <c>Count()</c> — same name, same signature, three different tables,
    /// because VPhysics keeps three. A double implementing them implicitly
    /// would answer all three from one member and a test could not tell which
    /// table it had counted. Every member is explicit for exactly this. The
    /// tables are the server's own since plan_physics_bones_native_zero 2.4,
    /// so their recorder is <see cref="RecordingJointTables"/>, over the double.
    /// </summary>
    [Fact]
    public void TellingTheThreeTablesApart()
    {
        var fake = new RecordingEngine();
        var tables = new RecordingJointTables(fake);

        fake.Returns("Constraints.Count", 3);
        fake.Returns("Springs.Count", 7);
        fake.Returns("MotionControllers.Count", 11);

        Assert.True(((IEngineConstraints)tables).Count() == 3, "constraints count themselves");
        Assert.True(((IEngineSprings)tables).Count() == 7, "springs count themselves");
        Assert.True(((IEngineMotionControllers)tables).Count() == 11, "motion controllers count themselves");

        // And each is logged separately, which is what a leak hunt needs.
        Assert.True(fake.Called("Constraints.Count")
            && fake.Called("Springs.Count")
            && fake.Called("MotionControllers.Count"), "each table's count is its own entry");

        // Destroy(uint) collides the same way.
        ((IEngineSprings)tables).Destroy(9u);
        Assert.True(fake.Called("Springs.Destroy") && !fake.Called("Constraints.Destroy"), "destroying a spring is not destroying a constraint");
    }

    [Fact]
    public void StrictMode()
    {
        var fake = new RecordingEngine { Strict = true };
        var entities = (IEngineEntities)fake;

        // A call that returns nothing has no answer to stub, so strict has no
        // opinion about it — the entity is telling the engine, not asking.
        entities.SetHealth(default, 1);
        Assert.True(fake.Called("Entities.SetHealth"), "strict mode allows a call that asks for nothing");

        bool threw = false;
        try { entities.GetHealth(default); }
        catch (UnstubbedEngineCallException) { threw = true; }

        Assert.True(threw, "strict mode refuses an unanswered question");

        fake.Returns("Entities.GetHealth", 5);
        Assert.True(entities.GetHealth(default) == 5, "and accepts it once stubbed");

        // Off by default, so an ordinary test is not made to stub every reply
        // before it can assert what was asked.
        // ON by default, and this is the assertion that pins it: a double that
        // answered zero to anything nobody stubbed would rebuild the silence
        // the whole exercise is against.
        Assert.True(new RecordingEngine().Strict, "strict is on by default");
    }

    /// <summary>
    /// Every public member of <c>Engine</c> is forwarded by a binding,
    /// or is named above as deliberately static.
    ///
    /// <para>
    /// The guard against drift, and it earns its keep immediately: writing it
    /// found two members that had been missed — both return a tuple, and the
    /// tool that built the area map had skipped them without saying so. The
    /// map reported full coverage of an incomplete list, which is the exact
    /// shape of check this project keeps shipping by accident.
    /// </para>
    ///
    /// <para>
    /// Checked against the BINDING SOURCE rather than against a table repeated
    /// here. A binding member is one forwarding call and nothing else, so
    /// "Engine.X appears in EngineBinding.cs" is precisely the property wanted,
    /// and a second copy of the map would be a second thing to keep in step.
    /// Reading a source file from a test is the same trick
    /// <see cref="AbiParityTests"/> already uses on the native header.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryEngineMemberIsOnAnArea()
    {
        string? binding = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "managed", "SourceSharp.Shared.Interop", "EngineBinding.cs"));

        if (binding is null)
        {
            // A failure, not a skip: the farm is always beside the tests
            // (RepoLayout), and a skip would hide the file moving.
            Assert.Fail("the engine binding is found for the coverage check");
            return;
        }

        string text = File.ReadAllText(binding);
        var forwarded = new HashSet<string>(StringComparer.Ordinal);

        // `E.X` since plan_engineapi_vtables B2: a binding calls its side's
        // EngineSide, and the members it can reach are EngineSide's.
        foreach (Match call in Regex.Matches(text, @"\b(?:Engine|E)\.(\w+)"))
            forwarded.Add(call.Groups[1].Value);

        var missing = new List<string>();

        // EngineSide's own: the static Engine is deleted (plan_abi_hotreload
        // Phase 1), and it had no public members left by then.
        var members = typeof(EngineSide).GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == typeof(EngineSide));

        foreach (var member in members)
        {
            if (member is not (MethodInfo or PropertyInfo))
                continue;

            // Property accessors; the property itself is counted.
            if (member is MethodInfo method && method.IsSpecialName)
                continue;

            if (StaticByDesign.Contains(member.Name))
                continue;

            if (!forwarded.Contains(member.Name))
                missing.Add(member.Name);
        }

        if (missing.Count > 0)
        {
            Assert.Fail($"Engine members no binding forwards: {string.Join(", ", missing.OrderBy(m => m))}");
        }

        Assert.True(true, "every Engine member is forwarded by a binding or named as deliberately static");

        // And the other direction: a binding forwarding something Engine no
        // longer has would not compile, so that needs no test — but a binding
        // forwarding a member NAMED in StaticByDesign would mean the list is
        // stale, and that compiles perfectly.
        Assert.True(!StaticByDesign.Any(forwarded.Contains), "nothing named as deliberately static is forwarded anyway");
    }

    /// <summary>
    /// <c>Engine</c> is unreachable from the game assemblies.
    ///
    /// <para>
    /// The rule the areas exist to make enforceable, checked rather than
    /// trusted. A class that reached past its declared interfaces to call the
    /// engine directly would compile, would work, and would be untestable —
    /// and it would be the one nobody noticed had done it. It is internal to
    /// the ABI assembly, so now it does not compile.
    /// </para>
    ///
    /// <para>
    /// Two assemblies still see it through <c>InternalsVisibleTo</c> and both
    /// have to: the bootstrap installs the function-pointer table before any
    /// container exists, and this test project checks that table against the
    /// native header. Neither is game code.
    /// </para>
    /// </summary>
    /// <summary>
    /// No game assembly names EngineSide, read from the compiled metadata (P1
    /// run 3). Server sees Abi's internals since the entity cluster moved there
    /// (plan M3), so "it is internal" no longer keeps it out on its own; the
    /// cluster reaches its engine through ServerEntityEngine (Server.Interop).
    /// </summary>
    [Fact]
    public void NoGameAssemblyNamesTheEngineSide()
    {
        var named = new[] { typeof(SourceSharp.Server.ServerModule).Assembly, typeof(client::SourceSharp.Client.ClientModule).Assembly }
            .Where(a => TypeRefNames(a.Location).Contains("SourceSharp.Abi.EngineSide"))
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.True(TypeRefNames(typeof(ServerEntityEngine).Assembly.Location).Contains("SourceSharp.Abi.EngineSide"),
            "the reader sees a reference where there is one (ServerEntityEngine's assembly)");
        Assert.True(named.Count == 0, "no game assembly names EngineSide: " + string.Join(", ", named));
    }

    private static HashSet<string> TypeRefNames(string path)
    {
        using var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path));
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return md.TypeReferences.Select(h => md.GetTypeReference(h))
                 .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name))
                 .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void NothingOutsideTheAbiCanReachTheEngine()
    {
        // The static Engine is deleted (plan_abi_hotreload Phase 1); what is
        // left of it is each side's EngineSide, internal for the same reason.
        Assert.True(!typeof(EngineSide).IsPublic, "EngineSide is not public");

        // And what replaced it for the one thing every class still does: the
        // ILog a composition hands in (statics to zero), which can only print.
        Assert.True(typeof(ILog).IsPublic && typeof(ILog).IsInterface, "logging is reachable without it");
        Assert.True(typeof(ILog).GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m is MethodInfo { IsSpecialName: false })
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(new[] { "Msg", "Warning" }), "and can only print");
    }
}
