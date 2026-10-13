using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// physics_shared.cpp leaving the DLLs (plan_physics_bones_native_zero 2.6),
/// one removal half per side, each with the retention half that doing less
/// cannot satisfy: what replaced it is managed and still asked for.
/// </summary>
public class PhysicsSharedEvictionTests
{
    private static bool Lists(string target, string file) =>
        CMakeGraph.ListedSources(target).Any(s => s.EndsWith("/" + file, System.StringComparison.Ordinal));

    private static string Code(params string[] path) =>
        Regex.Replace(File.ReadAllText(RepoLayout.Repo(path)), @"//[^\n]*", "");

    // ---- server.so -----------------------------------------------------------------

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void PhysicsSharedLeftTheServer()
    {
        Assert.False(Lists(CMakeGraph.Server, "physics_shared.cpp"));
        Assert.DoesNotContain("physics_shared.cpp", Code("src", "server", "native", "CMakeLists.txt").Replace("# physics_shared.cpp left", ""));
    }

    [Fact]
    public void TheServersDataObjectSystemAndNativePairHashLeftWithIt()
    {
        string systems = Code("src", "server", "native", "shell_serversystems.cpp");
        Assert.Contains("kServerSystems[]", systems);
        Assert.DoesNotContain("DataObjectAccessGameSystem", systems);

        string physics = Code("src", "server", "native", "physics.cpp");
        Assert.Contains("CPhysicsHook::LevelInitPreEntity", physics);
        Assert.DoesNotContain("g_EntityCollisionHash", physics);
        Assert.DoesNotContain("CreateObjectPairHash", physics);
        Assert.DoesNotContain("PhysCreateWorld_Shared", physics);
        Assert.DoesNotContain("PrecachePhysicsSounds", physics);
    }

    [Fact]
    public void TheServerAsksManagedForTheWorldTheSoundsAndTheSolid()
    {
        string physics = Code("src", "server", "native", "physics.cpp");
        Assert.Contains("->CreateWorld()", physics);
        Assert.Contains("->PrecacheSurfaceSounds()", physics);
        Assert.Contains("IPhysicsSurfaceProps\t*physprops = NULL;", File.ReadAllText(RepoLayout.Repo("src", "server", "native", "physics.cpp")));
        // The entity's solid: native's InitPhysics asked ParseModelSolid until
        // the entity physics init went managed (2026-10-12); managed's
        // EntityPhysicsCreation parses it itself.
        string shellBase = Code("src", "server", "native", "shell_standalone_base.cpp");
        Assert.Contains("void CStandaloneEntity::DestroyPhysics()", shellBase);
        Assert.DoesNotContain("ParseModelSolid", shellBase);
        Assert.Contains("ModelSolidParse.Parse(_world, f.CollisionModelIndex, -1, scratch)",
                        File.ReadAllText(RepoLayout.Repo("src", "server", "managed", "EntityPhysicsCreation.cs")));

        Assert.True(typeof(IServerPhysicsWorld).IsAssignableFrom(typeof(ServerPhysicsWorld)));
        Assert.NotNull(typeof(PhysicsWorldBuilder).GetMethod(nameof(PhysicsWorldBuilder.Create)));
        Assert.NotNull(typeof(ModelSolidParse).GetMethod(nameof(ModelSolidParse.Parse)));
        Assert.NotNull(typeof(SurfaceSoundPrecache).GetMethod(nameof(SurfaceSoundPrecache.Run)));
    }

    // ---- client.so and the repository ----------------------------------------------

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void PhysicsSharedLeftTheClientAndTheRepository()
    {
        Assert.False(Lists(CMakeGraph.Client, "physics_shared.cpp"));
        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", "physics_shared.cpp")));
        Assert.DoesNotContain("physics_shared.cpp", Code("src", "client", "native", "CMakeLists.txt").Replace("# physics_shared.cpp left", ""));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void TheClientRemnantForwardsEveryDecisionToManaged()
    {
        // What fork game/client/physics.cpp links stays, and each decision in
        // it is an ask of IManagedPhysicsClient.
        Assert.True(Lists(CMakeGraph.Client, "shell_physics_shared_client.cpp"));
        string remnant = Code("src", "client", "native", "shell_physics_shared_client.cpp");
        foreach (string ask in new[] { "->CreateWorld()", "->ParseSurfaceData()", "->PrecacheSurfaceSounds()",
                                       "->SolidKeyValue(", "->SolidDefaults(" })
            Assert.Contains(ask, remnant);

        // And none of stock's bodies came with it: no parse loop, no manifest
        // read, no surface sound walk, no world object made here.
        foreach (string body in new[] { "VPhysicsKeyParserCreate", "CreatePolyObjectStatic", "LoadFromFile",
                                        "GameSound_PrecacheScript", "GetSurfaceData", "CreateFrictionSnapshot" })
            Assert.DoesNotContain(body, remnant);
    }
}
