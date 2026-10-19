using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Physics;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client physics leftovers (fork game/client/physics.cpp and what only it
/// called) leaving client.so and the repository, each removal half with the
/// retention half that doing less cannot satisfy: what replaced it is managed
/// and native still asks it.
/// </summary>
public class ClientPhysicsEvictionTests
{
    private static bool Lists(string file) =>
        CMakeGraph.ListedSources(CMakeGraph.Client).Any(s => s.EndsWith("/" + file, System.StringComparison.Ordinal));

    private static string Code(params string[] path) =>
        Regex.Replace(File.ReadAllText(RepoLayout.Repo(path)), @"//[^\n]*", "");

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void ForkPhysicsCppLeftTheClient()
    {
        // The positive control: the reader reads the client's list.
        Assert.True(Lists("shell_physics_client.cpp"));
        Assert.False(Lists("physics.cpp"));
        Assert.DoesNotContain("game/client/physics.cpp", Code("src", "client", "native", "CMakeLists.txt"));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void WhatOnlyPhysicsCppCalledLeftTheRepository()
    {
        foreach (string file in new[] { "ragdoll_library.cpp", "ragdoll_library.h", "shell_physics_shared_client.cpp" })
        {
            Assert.False(Lists(file), file);
            Assert.False(File.Exists(RepoLayout.Repo("src", "client", "native", file)), file);
        }
    }

    [Fact]
    public void NativeStillForwardsTheStepTheLevelAndTheResetToManaged()
    {
        string native = Code("src", "client", "native", "shell_physics_client.cpp");
        foreach (string ask in new[] { "->Simulate()", "->Reset()", "->LevelInitPreEntity()", "->LevelInitPostEntity()",
                                       "->LevelShutdownPreEntity()", "->LevelShutdownPostEntity()", "->ParseSurfaceData()" })
            Assert.Contains(ask, native);

        // ...and decides none of it: no environment made, stepped or destroyed here.
        foreach (string body in new[] { "CreateEnvironment", "->Simulate(", "DestroyEnvironment", "SetGravity", "StepPoses" })
            Assert.DoesNotContain(body, native.Replace("pPhysics->Simulate()", ""));

        // The shells still read physprops by name, so it is held here.
        Assert.Contains("IPhysicsSurfaceProps\t*physprops = NULL;", File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_physics_client.cpp")));
    }

    [Fact]
    public void ManagedMakesStepsAndTearsDownTheLevel()
    {
        Assert.True(typeof(IClientPhysicsFrame).IsAssignableFrom(typeof(ClientPhysicsSystem)));
        Assert.NotNull(typeof(ClientPhysicsSystem).GetMethod(nameof(ClientPhysicsSystem.Simulate)));
        Assert.True(typeof(IClientPhysicsLevelCalls).IsAssignableFrom(typeof(VPhysicsClientLevel)));
        Assert.Contains("_physicsSystem = new Physics.ClientPhysicsSystem(", File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs")));
    }
}
