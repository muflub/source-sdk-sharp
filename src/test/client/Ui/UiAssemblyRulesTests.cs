using System.Reflection;

using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The assembly-level UI rules (plan_client_ui §5.8.5a), kept as facts rather
/// than analyzer rules: the UI assemblies reference no core, engine or interop
/// assembly, and every screen has a [UiSample]. (A panel's sources being read
/// is UiSourceUseTests.)
/// </summary>
public class UiAssemblyRulesTests
{
    private static readonly string[] Allowed = ["SourceSharp.Client.Interface", "SourceSharp.Client.Ui.Toolkit"];

    /// <summary>What <paramref name="asm"/> references beyond the runtime and the UI contract.</summary>
    internal static List<string> Forbidden(IEnumerable<AssemblyName> references)
        => references.Select(r => r.Name!)
            .Where(n => !(n.StartsWith("System", StringComparison.Ordinal) || n is "netstandard" or "mscorlib" || Allowed.Contains(n)))
            .ToList();

    [Fact]
    public void The_ui_assemblies_reference_only_the_runtime_and_the_ui_contract()
    {
        foreach (var asm in new[] { typeof(UiFrame).Assembly, typeof(ScreensAssembly).Assembly })
        {
            var refs = asm.GetReferencedAssemblies();
            Assert.Contains(refs, r => r.Name == "SourceSharp.Client.Interface");   // the scan sees references
            var bad = Forbidden(refs);
            Assert.True(bad.Count == 0, $"{asm.GetName().Name} references {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void A_core_or_engine_reference_is_reported()
    {
        Assert.Equal(["SourceSharp.Client", "SourceSharp.Abi"],
            Forbidden([new AssemblyName("System.Runtime"), new AssemblyName("SourceSharp.Client"), new AssemblyName("SourceSharp.Abi"),
                       new AssemblyName("SourceSharp.Client.Interface")]));
    }

    /// <summary>Screen types with no sample whose screen has their name.</summary>
    internal static List<string> Unsampled(IEnumerable<Type> screens, IEnumerable<UiSample> samples)
    {
        var sampled = samples.Where(s => !s.Gallery).Select(s => s.Create().GetType()).ToHashSet();
        return screens.Where(t => !sampled.Contains(t)).Select(t => t.Name).ToList();
    }

    private static IEnumerable<Type> Screens()
        => typeof(ScreensAssembly).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(UiScreen).IsAssignableFrom(t));

    [Fact]
    public void Every_screen_has_a_sample()
    {
        var screens = Screens().ToList();
        Assert.True(screens.Count >= 5, $"{screens.Count} screens");
        var missing = Unsampled(screens, PreviewRun.AllSamples());
        Assert.True(missing.Count == 0, "screens with no [UiSample]: " + string.Join(", ", missing));
    }

    private sealed class Unpreviewed : UiScreen
    {
        public override string Name => "unpreviewed";
        public override void Describe(ref UiFrame ui) { }
    }

    [Fact]
    public void A_screen_without_a_sample_is_reported()
        => Assert.Equal(["Unpreviewed"], Unsampled([typeof(Hud), typeof(Unpreviewed)], PreviewRun.AllSamples()));
}
