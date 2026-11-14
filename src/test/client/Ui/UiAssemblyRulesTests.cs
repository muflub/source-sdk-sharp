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

    /// <summary>
    /// A mod's own client contract (TF2.Client.Interface): interfaces only, held by the core's
    /// context, the one identity its screens and its client adapters share.
    /// </summary>
    private static bool ModContract(string name) => name.EndsWith(".Client.Interface", StringComparison.Ordinal);

    /// <summary>
    /// What <paramref name="asm"/> references beyond the runtime, the UI contract and the
    /// <paramref name="held"/> UI assemblies: another screen assembly the same rule holds
    /// (TF2's bots page names the game-neutral AI screens' custom bot rows), which the UI's
    /// load context holds beside it when a mod lists both.
    /// </summary>
    internal static List<string> Forbidden(IEnumerable<AssemblyName> references, IReadOnlyCollection<string>? held = null)
        => references.Select(r => r.Name!)
            .Where(n => !(n.StartsWith("System", StringComparison.Ordinal) || n is "netstandard" or "mscorlib" || Allowed.Contains(n) || ModContract(n)
                          || (held?.Contains(n) ?? false)))
            .ToList();

    [Fact]
    public void The_ui_assemblies_reference_only_the_runtime_and_the_ui_contract()
    {
        // The core's two, every game UI assembly the preview lists, and the
        // facts' own mod UI assembly (stage 0.9): what a mod's client_ui holds
        // is held to the core's rule.
        var held = PreviewRun.ScreenAssemblies().Select(a => a.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        foreach (var asm in PreviewRun.ScreenAssemblies().Append(typeof(ModUi.ModProbe).Assembly))
        {
            var refs = asm.GetReferencedAssemblies();
            Assert.Contains(refs, r => r.Name == "SourceSharp.Client.Interface");   // the scan sees references
            var bad = Forbidden(refs, held);
            Assert.True(bad.Count == 0, $"{asm.GetName().Name} references {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void A_core_or_engine_reference_is_reported()
    {
        Assert.Equal(["SourceSharp.Client", "SourceSharp.Abi", "TF2.Client"],
            Forbidden([new AssemblyName("System.Runtime"), new AssemblyName("SourceSharp.Client"), new AssemblyName("SourceSharp.Abi"),
                       new AssemblyName("SourceSharp.Client.Interface"), new AssemblyName("TF2.Client"),
                       new AssemblyName("TF2.Client.Interface")]));
    }

    [Fact]
    public void A_held_ui_assembly_is_allowed_and_only_when_held()
    {
        AssemblyName[] refs = [new("SourceSharp.Client.Interface"), new("SourceSharp.Ai.Ui")];
        Assert.Equal(["SourceSharp.Ai.Ui"], Forbidden(refs));   // the control: not held, reported
        Assert.Empty(Forbidden(refs, ["SourceSharp.Ai.Ui"]));
    }

    /// <summary>Screen types with no sample whose screen has their name.</summary>
    internal static List<string> Unsampled(IEnumerable<Type> screens, IEnumerable<UiSample> samples)
    {
        var sampled = samples.Where(s => !s.Gallery).Select(s => s.Create().GetType()).ToHashSet();
        return screens.Where(t => !sampled.Contains(t)).Select(t => t.Name).ToList();
    }

    /// <summary>
    /// Every screen the game would place from a screen assembly the preview
    /// draws (the core's, and each listed game UI assembly), by the game's own
    /// discovery (stage 0.9): what the game shows, the preview has drawn.
    /// </summary>
    private static IEnumerable<Type> Screens()
        => UiScreens.Discover([.. PreviewRun.ScreenAssemblies().Where(a => a != typeof(UiScreen).Assembly)]);

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
