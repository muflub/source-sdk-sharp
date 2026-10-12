using System.Reflection;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

using TF2.Descent.Client.Ui;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The same two facts as <see cref="UiSourceUseTests"/> over the descent
/// screens (docs/plans/descent_ui.md §4): a Descent screen takes only the
/// IDescent* sources it reads, and every member of every IDescent* interface
/// is read by some screen or panel — the data interfaces stay honest as the
/// screens land. The assembly is discovered through the preview's list, so
/// this test sees exactly what ui-preview draws.
/// </summary>
public class DescentUiSourceUseTests
{
    private static readonly Assembly Descent =
        PreviewRun.ScreenAssemblies().Single(a => a.GetName().Name == "TF2.Descent.Client.Ui");

    private static readonly Type ScreenBase = typeof(UiScreen);

    private static bool IsPanel(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b == typeof(UiPanel) || (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(UiPanel<>)))
                return true;
        return false;
    }

    /// <summary>A descent data interface: the game's own namespace, an IDescent* name.</summary>
    private static bool IsDescentSource(Type t)
        => t.IsInterface && t.Assembly == Descent && t.Namespace == "TF2.Descent.Client.Ui"
           && t.Name.StartsWith("IDescent", StringComparison.Ordinal);

    private static List<Type> UiTypes()
        => Descent.GetTypes().Where(t => !t.IsAbstract && (ScreenBase.IsAssignableFrom(t) || IsPanel(t))).ToList();

    [Fact]
    public void The_descent_assembly_is_discovered_with_its_screens()
    {
        var types = UiTypes();
        Assert.Contains(types, t => t.Name == "DescentHud");
        Assert.Contains(types, t => t.Name == "ItemSlot");
        // The scan reaches the screens the lanes add; an empty-ish scan means
        // the assembly was loaded from the wrong place.
        Assert.True(types.Count >= 15, $"only {types.Count} descent screens/panels: the scan missed them");
        int sourced = types.Count(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => IsDescentSource(p.ParameterType)));
        Assert.True(sourced >= 8, $"only {sourced} types take an IDescent source");
    }

    [Fact]
    public void Every_descent_source_a_screen_or_panel_takes_is_read_by_it()
    {
        var unread = UiTypes().SelectMany(t => UiSourceUseTests.UnreadSources(t, IsDescentSource)).ToList();
        Assert.True(unread.Count == 0, "taken but never read:\n" + string.Join("\n", unread));
    }

    [Fact]
    public void Every_member_of_every_descent_source_is_read_somewhere()
    {
        var calls = UiTypes().SelectMany(UiSourceUseTests.Calls).ToHashSet();
        var sources = Descent.GetTypes().Where(IsDescentSource).ToList();
        Assert.Contains(typeof(IDescentVitals), sources);

        var unread = sources.SelectMany(s => s.GetMethods().Where(m => !calls.Contains(m)).Select(m => $"{s.Name}.{m.Name}")).ToList();
        Assert.True(unread.Count == 0, "no screen or panel reads:\n" + string.Join("\n", unread));
    }

    // The planted half: a descent panel that holds a source it never reads is
    // reported; one that reads it is not. This proves the scan above is live.
    private sealed class Greedy(IDescentVitals vitals, IDescentXp xp) : UiPanel
    {
        public override void Describe(ref UiFrame ui) => ui.Number(vitals.Life);
        public IDescentXp Unused => xp;
    }

    private sealed class Lean(IDescentVitals vitals) : UiPanel
    {
        private readonly Func<int> _read = () => vitals.LifeMax;
        public override void Describe(ref UiFrame ui) => ui.Number(_read());
    }

    [Fact]
    public void The_scan_reports_a_planted_greedy_panel_and_not_a_lean_one()
    {
        Assert.NotEmpty(UiSourceUseTests.UnreadSources(typeof(Greedy), IsDescentSource));
        Assert.Empty(UiSourceUseTests.UnreadSources(typeof(Lean), IsDescentSource));
    }
}
