using System.Reflection;

using SourceSharp.Ai.Hl2mp.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.UiPreview;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// <see cref="UiSourceUseTests"/>' two facts over the HL2MP AI's bot-setup
/// screens (plan_hl2mp_ai §3.5): a screen takes only the sources and actions
/// of SourceSharp.Ai.Hl2mp.Client.Interface it reads, and every member of
/// every one is read by some screen or panel. Discovered through the
/// preview's list, so this sees exactly what ui-preview draws.
/// </summary>
public class AiUiSourceUseTests
{
    private static readonly Assembly Screens =
        PreviewRun.ScreenAssemblies().Single(a => a.GetName().Name == "SourceSharp.Ai.Hl2mp.Ui");

    private static readonly Assembly Contract = typeof(ICreateServerSource).Assembly;

    private static bool IsPanel(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b == typeof(UiPanel) || (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(UiPanel<>)))
                return true;
        return false;
    }

    private static bool IsAiSource(Type t) => t.IsInterface && t.Assembly == Contract;

    private static List<Type> UiTypes()
        => Screens.GetTypes().Where(t => !t.IsAbstract && (typeof(UiScreen).IsAssignableFrom(t) || typeof(CreateServerPage).IsAssignableFrom(t) || IsPanel(t)))
                  .ToList();

    [Fact]
    public void The_ai_screens_are_discovered_and_take_their_sources()
    {
        var types = UiTypes();
        Assert.Contains(typeof(BotsPage), types);
        Assert.Contains(typeof(CustomBotList), types);
        Assert.Contains(typeof(BotEditor), types);
        Assert.Contains(typeof(SkillRowPanel), types);
        Assert.True(types.Count(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => IsAiSource(p.ParameterType))) >= 5);
    }

    /// <summary>
    /// The bots are a page of the core's create-server window, which keeps its
    /// server and game pages (hostname, password, ...): the window finds the
    /// page in the AI's UI assembly, and no AI screen answers the window's
    /// name (at the landing, the AI's screen answered <c>createserver</c> and
    /// hid the core's pages).
    /// </summary>
    [Fact]
    public void The_ai_bots_are_a_page_of_the_cores_create_server_window_and_no_ai_screen_takes_its_name()
    {
        Assert.Equal([typeof(BotsPage)], CreateServerPages.Discover(Screens));
        var screens = Screens.GetTypes().Where(t => !t.IsAbstract && typeof(UiScreen).IsAssignableFrom(t)).ToList();
        Assert.NotEmpty(screens);   // the control: the editor is a screen of the assembly
        foreach (Type t in screens)
        {
            var screen = (UiScreen)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
            Assert.NotEqual(CreateServerWindow.Id, screen.Name);
        }

        var page = new FakeCreateServer().Register(new UiSampleServices()).Create<BotsPage>();
        Assert.Equal("#Ai_CreateServer_BotsTab", page.TabToken);
    }

    /// <summary>
    /// The window grows to what the page needs: at the core's minimum (380 du)
    /// the custom list ran under the buttons (seen in the minimum sample). The
    /// page asks for its chrome (title, tabs, form, list header, buttons: 420
    /// du) and three narrow rows.
    /// </summary>
    [Fact]
    public void The_window_grows_to_show_the_bots_page_three_narrow_rows()
    {
        var page = new FakeCreateServer().Register(new UiSampleServices()).Create<BotsPage>();
        (float w, float h) = CreateServerWindow.Minimum([page]);
        Assert.True(h >= 420 + 3 * CustomBotList.NarrowRowDu, $"the window's minimum is {h} du tall");
        Assert.True(w >= CreateServerWindow.MinWidthDu);
        Assert.Equal((CreateServerWindow.MinWidthDu, CreateServerWindow.MinHeightDu), CreateServerWindow.Minimum([]));   // the control: no page, the core's own
    }

    [Fact]
    public void Every_ai_source_a_screen_or_panel_takes_is_read_by_it()
    {
        var unread = UiTypes().SelectMany(t => UiSourceUseTests.UnreadSources(t, IsAiSource)).ToList();
        Assert.True(unread.Count == 0, "taken but never read:\n" + string.Join("\n", unread));
    }

    [Fact]
    public void Every_member_of_every_ai_source_is_read_somewhere()
    {
        var calls = UiTypes().SelectMany(UiSourceUseTests.Calls).ToHashSet();
        var sources = Contract.GetTypes().Where(IsAiSource).ToList();
        Assert.Contains(typeof(ICreateServerActions), sources);

        var unread = sources.SelectMany(s => s.GetMethods().Where(m => !calls.Contains(m)).Select(m => $"{s.Name}.{m.Name}")).ToList();
        Assert.True(unread.Count == 0, "no screen or panel reads:\n" + string.Join("\n", unread));
    }
}
