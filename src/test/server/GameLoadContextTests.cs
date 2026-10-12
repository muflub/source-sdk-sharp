using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using SourceSharp.Abi;
using SourceSharp.Bootstrap;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two side roots a listen server makes, built once for the class: each
/// is non-collectible and keeps its Abi for the process, as in the game.
/// </summary>
public sealed class SideRootPair
{
    internal static string TestBin => Path.GetDirectoryName(typeof(SideRootPair).Assembly.Location)!;

    internal SideRoot Server { get; } = new("ServerRoot", TestBin);
    internal SideRoot Client { get; } = new("ClientRoot", TestBin);
}

/// <summary>
/// Abi per side (plan_side_isolation §2.3): each side's load context hands its
/// assemblies the copy of SourceSharp.Abi in THAT SIDE'S ROOT, a
/// non-collectible context kept for the process. The two sides share no
/// managed type, and within one side the Abi keeps its identity across a
/// reload.
/// </summary>
[Collection(ProcessWide.Name)]
public sealed class GameLoadContextTests(SideRootPair roots) : IClassFixture<SideRootPair>
{
    private static string TestBin => SideRootPair.TestBin;

    private static Assembly AbiOf(GameLoadContext context)
        => context.LoadFromAssemblyName(new AssemblyName(SideRoot.AbiName));

    /// <summary>A ManagedVtables registry made from <paramref name="abi"/>'s own type.</summary>
    private static object Registry(Assembly abi)
    {
        Type type = abi.GetType("SourceSharp.Abi.ManagedVtables", throwOnError: true)!;
        object server = Enum.ToObject(abi.GetType("SourceSharp.Abi.Side", throwOnError: true)!, 0);
        return Activator.CreateInstance(type, [null, server])!;
    }

    [Fact]
    public void ASideRootIsNotCollectible()
    {
        Assert.False(roots.Server.IsCollectible);
        Assert.Same(roots.Server, AssemblyLoadContext.GetLoadContext(roots.Server.Abi));
    }

    [Fact]
    public void ASideContextResolvesTheAbiToItsSideRootsCopy()
    {
        var context = new GameLoadContext("GameLoadContextTests", TestBin, roots.Server);
        try
        {
            Assembly abi = AbiOf(context);
            Assert.Same(roots.Server.Abi, abi);
            // The control: not the copy this process loaded the ordinary way.
            Assert.NotSame(typeof(ILog).Assembly, abi);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void TheTwoSidesAbiAssembliesAreDifferentInstances()
    {
        var server = new GameLoadContext("ServerALC", TestBin, roots.Server);
        var client = new GameLoadContext("ClientALC", TestBin, roots.Client);
        try
        {
            Assembly serverAbi = AbiOf(server), clientAbi = AbiOf(client);

            // The control: both are the Abi, by name.
            Assert.Equal(serverAbi.GetName().Name, clientAbi.GetName().Name);
            Assert.NotSame(serverAbi, clientAbi);
            Assert.NotSame(AssemblyLoadContext.GetLoadContext(serverAbi), AssemblyLoadContext.GetLoadContext(clientAbi));
        }
        finally
        {
            server.Unload();
            client.Unload();
        }
    }

    [Fact]
    public void AServerAbiTypeIsNotAssignableToTheClients()
    {
        Type server = roots.Server.Abi.GetType("SourceSharp.Abi.ManagedVtables", throwOnError: true)!;
        Type client = roots.Client.Abi.GetType("SourceSharp.Abi.ManagedVtables", throwOnError: true)!;

        // The control: one name, and the server's own type takes its own objects.
        Assert.Equal(server.FullName, client.FullName);
        object serverRegistry = Registry(roots.Server.Abi);
        try
        {
            Assert.True(server.IsInstanceOfType(serverRegistry));

            Assert.False(client.IsAssignableFrom(server));
            Assert.False(server.IsAssignableFrom(client));
            Assert.False(client.IsInstanceOfType(serverRegistry));
        }
        finally
        {
            ((IDisposable)serverRegistry).Dispose();
        }
    }

    [Fact]
    public void WithinOneSideTheAbiKeepsItsIdentityAcrossAReload()
    {
        // The registry the Bootstrap keeps across reloads, as an opaque handle
        // made under the first load's context.
        var first = new GameLoadContext("ServerALC", TestBin, roots.Server);
        Assembly before = AbiOf(first);
        object registry = Registry(before);
        nint handle = GCHandle.ToIntPtr(GCHandle.Alloc(registry));
        first.Unload();

        var second = new GameLoadContext("ServerALC", TestBin, roots.Server);
        try
        {
            Assembly after = AbiOf(second);
            Assert.Same(before, after);

            // What the next load does with the handle: it casts it to its own
            // Abi's type, which only works if the Abi is the same one.
            object? kept = GCHandle.FromIntPtr(handle).Target;
            Assert.True(after.GetType("SourceSharp.Abi.ManagedVtables", throwOnError: true)!.IsInstanceOfType(kept));
        }
        finally
        {
            GCHandle.FromIntPtr(handle).Free();
            ((IDisposable)registry).Dispose();
            second.Unload();
        }
    }

    /// <summary>A game context that loaded a game assembly and made a registry from the root's Abi, unloaded.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(SideRoot root, out nint registryHandle)
    {
        var context = new GameLoadContext("ServerALC", TestBin, root);
        Assert.NotNull(context.LoadFromManagedDir("SourceSharp.Server"));
        registryHandle = GCHandle.ToIntPtr(GCHandle.Alloc(Registry(AbiOf(context))));
        var weak = new WeakReference(context, trackResurrection: true);
        context.Unload();
        return weak;
    }

    [Fact]
    public void AnUnloadCollectsTheGameContextWhileTheRootKeepsItsAbiAndRegistry()
    {
        WeakReference weak = LoadAndUnload(roots.Server, out nint handle);
        try
        {
            for (int i = 0; i < 10 && weak.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.False(weak.IsAlive, "the game context was collected");
            object? registry = GCHandle.FromIntPtr(handle).Target;
            Assert.NotNull(registry);
            Assert.Same(roots.Server.Abi, registry!.GetType().Assembly);
        }
        finally
        {
            var h = GCHandle.FromIntPtr(handle);
            ((IDisposable)h.Target!).Dispose();
            h.Free();
        }
    }

    [Fact]
    public void ASideContextLoadsItsOwnCopyOfAnythingElse()
    {
        // The control: the same context, asked for a game assembly, loads a copy
        // of its own, so the Abi arm above is not just "everything resolves to
        // the root".
        var context = new GameLoadContext("GameLoadContextTests", TestBin, roots.Server);
        try
        {
            Assembly shared = context.LoadFromAssemblyName(new AssemblyName("SourceSharp.Server"));
            Assert.NotSame(typeof(SourceSharp.Shared.AmmoTypes).Assembly, shared);
            Assert.Same(context, AssemblyLoadContext.GetLoadContext(shared));
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void BothSidesRunOneAbiBuildArtifact()
    {
        // Layout parity on both sides (plan_side_isolation P5 step 4):
        // AbiParityTests and the vtable-layout rows reflect over this process's
        // Abi. Both roots load the same file, so they run the same build, by
        // module version and by the loaded bytes' hash, and the parity facts
        // hold for each side's copy.
        Guid tested = typeof(ILog).Assembly.ManifestModule.ModuleVersionId;
        Assert.Equal(tested, roots.Server.Abi.ManifestModule.ModuleVersionId);
        Assert.Equal(tested, roots.Client.Abi.ManifestModule.ModuleVersionId);

        string hash = LoadFacts.HashOf(File.ReadAllBytes(typeof(ILog).Assembly.Location));
        Assert.Equal(hash, roots.Server.AbiFile?.Hash);
        Assert.Equal(hash, roots.Client.AbiFile?.Hash);
        Assert.Equal("SourceSharp.Abi.dll", roots.Server.AbiFile?.Name);
    }

    [Fact]
    public void TheAbiMovedWarningIsPerSideRoot()
    {
        string dir = Directory.CreateTempSubdirectory("p5-abimoved-").FullName;
        try
        {
            var loadedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string abi = Path.Combine(dir, SideRoot.AbiName + ".dll");
            File.Copy(Path.Combine(TestBin, SideRoot.AbiName + ".dll"), abi);
            File.SetLastWriteTimeUtc(abi, loadedAt);

            var server = new SideRoot("ServerRoot", dir);
            var client = new SideRoot("ClientRoot", dir);

            // The control: nothing moved yet.
            Assert.False(server.AbiMoved());
            Assert.False(client.AbiMoved());

            File.SetLastWriteTimeUtc(abi, loadedAt.AddDays(1));

            // Each side is told once per rebuild, whatever the other was told.
            Assert.True(server.AbiMoved());
            Assert.False(server.AbiMoved());
            Assert.True(client.AbiMoved());
            Assert.False(client.AbiMoved());

            // The stamp stays the Abi each root loaded.
            Assert.Equal(loadedAt, server.AbiFile?.WrittenUtc);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The isolated context hostfxr's component hosting loads the Bootstrap into.</summary>
    private sealed class ComponentContext(string dir) : AssemblyLoadContext("ComponentContext", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            string path = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    [Fact]
    public void UnderComponentHostingASideRootStillHoldsItsOwnAbi()
    {
        // Regression (live gate at 29612a2): the game loads the Bootstrap into
        // an isolated context, not Default. The side's Abi is its root's
        // whichever context the Bootstrap is in: never the host's, never Default's.
        var host = new ComponentContext(TestBin);
        try
        {
            Assembly bootstrap = host.LoadFromAssemblyPath(Path.Combine(TestBin, "SourceSharp.Bootstrap.dll"));
            Assert.NotSame(typeof(GameLoadContext).Assembly, bootstrap);

            Type rootType = bootstrap.GetType("SourceSharp.Bootstrap.SideRoot", throwOnError: true)!;
            var root = (AssemblyLoadContext)Activator.CreateInstance(
                rootType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, ["ComponentServerRoot", TestBin], null)!;
            var abi = (Assembly)rootType.GetProperty("Abi")!.GetValue(root)!;

            Assert.Same(root, AssemblyLoadContext.GetLoadContext(abi));
            Assert.NotSame(typeof(ILog).Assembly, abi);
            Assert.DoesNotContain(host.Assemblies, a => a.GetName().Name == SideRoot.AbiName);
        }
        finally
        {
            host.Unload();
        }
    }
}
