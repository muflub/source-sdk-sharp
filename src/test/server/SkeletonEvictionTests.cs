using System.IO;
using System.Linq;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// bone_library.cpp left server.so (plan physics_bones_native_zero 1.2): the
/// server's skeleton is managed, so nothing on the server calls it. The
/// removal half reads the server's source list; the retention half is that
/// the client still compiles it (its skeleton is native until its own flip).
/// bone_setup.cpp left the server too (2026-10-11): model_descriptor.cpp's last
/// two asks and their closure moved, unchanged, to studio_sequence_timing.cpp,
/// which the server compiles in its place. The behaviour the files carried is
/// held by ServerSkeletonsTests and the Bones facts.
/// bone_library.cpp left client.so with the client's flip (the same step's
/// client half): ClientSkeletons answers every bone ask, and the file is
/// deleted. bone_setup.cpp left client.so and the repository with the ragdoll
/// lane (plan physics_bones_native_zero Phase 3; RagdollEvictionTests): its
/// last caller was ragdoll_shared.cpp's three bone asks.
/// studio_sequence_timing.cpp left both sides and the repository on
/// 2026-10-12: model_descriptor.cpp's two asks went with the descriptor's
/// duration field (format v3), which managed computes (StudioSequenceTiming).
/// </summary>
public class SkeletonEvictionTests
{
    private static bool Lists(string target, string file) =>
        CMakeGraph.ListedSources(target).Any(s => s.EndsWith("/" + file, System.StringComparison.Ordinal));

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void BoneLibraryLeftTheServer()
    {
        Assert.False(Lists(CMakeGraph.Server, "bone_library.cpp"));
        Assert.DoesNotContain(CMakeGraph.DebugListedSources(CMakeGraph.Server),
                              s => s.EndsWith("/bone_library.cpp", System.StringComparison.Ordinal));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void BoneSetupLeftTheServer()
    {
        Assert.False(Lists(CMakeGraph.Server, "bone_setup.cpp"));
        Assert.DoesNotContain(CMakeGraph.DebugListedSources(CMakeGraph.Server),
                              s => s.EndsWith("/bone_setup.cpp", System.StringComparison.Ordinal));
    }

    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void BoneLibraryLeftTheClient()
    {
        Assert.False(Lists(CMakeGraph.Client, "bone_library.cpp"));
        Assert.DoesNotContain(CMakeGraph.DebugListedSources(CMakeGraph.Client),
                              s => s.EndsWith("/bone_library.cpp", System.StringComparison.Ordinal));
        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", "bone_library.cpp")));
    }

    // A source line names the file: a non-comment line of a CMakeLists, which
    // covers every list in it (server_sourcesharp_sources and the novalve row).
    private static int SourceLinesNaming(string target, string file) =>
        CMakeGraph.ListText(target).Split('\n')
            .Select(l => l.Trim())
            .Count(l => !l.StartsWith('#') && l.EndsWith("/" + file, System.StringComparison.Ordinal));

    /// <summary>
    /// studio_sequence_timing.cpp left both DLLs and the repository
    /// (2026-10-12): model_descriptor.cpp, its only caller, writes no duration
    /// any more (descriptor format v3).
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void SequenceTimingLeftBothSidesAndTheRepository()
    {
        foreach (string target in new[] { CMakeGraph.Server, CMakeGraph.Client })
        {
            Assert.Equal(0, SourceLinesNaming(target, "studio_sequence_timing.cpp"));
            Assert.DoesNotContain(CMakeGraph.DebugListedSources(target),
                                  s => s.EndsWith("/studio_sequence_timing.cpp", System.StringComparison.Ordinal));
        }

        Assert.False(File.Exists(RepoLayout.Repo("src", "shared", "native", "studio_sequence_timing.cpp")));
    }

    /// <summary>
    /// The retention half: the descriptor itself stays native on both sides
    /// (the server in both of its lists), and each side's module hands its
    /// descriptors the managed sequence timing that replaced the native
    /// durations.
    /// </summary>
    [LinuxOnlyFact("reads the Linux build's CMake graph (farm/<config>/build: its compile database and ninja file), which a Windows checkout never configures")]
    public void BothSidesKeepTheDescriptorAndTimeItsSequencesInManagedCode()
    {
        Assert.True(Lists(CMakeGraph.Client, "model_descriptor.cpp"));
        Assert.True(Lists(CMakeGraph.Server, "model_descriptor.cpp"));
        Assert.Equal(2, SourceLinesNaming(CMakeGraph.Server, "model_descriptor.cpp"));

        Assert.True(typeof(SourceSharp.Abi.IModelSequenceTiming)
            .IsAssignableFrom(typeof(SourceSharp.Shared.Bones.StudioSequenceTiming)));

        string server = File.ReadAllText(RepoLayout.Repo("src", "server", "managed", "ServerModule.cs"));
        string client = File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"));
        Assert.Contains("_timingAttachment = _engine.Models.AttachTiming(new StudioSequenceTiming(_skeletons.Models));",
                        server, System.StringComparison.Ordinal);
        Assert.Contains("_timingAttachment = _engine.Models.AttachTiming(new SourceSharp.Shared.Bones.StudioSequenceTiming(_skeletons.Models));",
                        client, System.StringComparison.Ordinal);
    }
}
