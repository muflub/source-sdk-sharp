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
        => Screens.GetTypes().Where(t => !t.IsAbstract && (typeof(UiScreen).IsAssignableFrom(t) || IsPanel(t))).ToList();

    [Fact]
    public void The_ai_screens_are_discovered_and_take_their_sources()
    {
        var types = UiTypes();
        Assert.Contains(typeof(CreateServer), types);
        Assert.Contains(typeof(CustomBotList), types);
        Assert.Contains(typeof(BotEditor), types);
        Assert.Contains(typeof(SkillRowPanel), types);
        Assert.True(types.Count(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => IsAiSource(p.ParameterType))) >= 5);
    }

    /// <summary>
    /// The AI's create-server screen and the core's are both on the test mod's
    /// menu layer: the AI's has a name of its own and answers the core's
    /// (<c>createserver</c>, stock's OpenCreateMultiplayerGameDialog) as an alias,
    /// so the main menu's Create server opens it (UiMenuHostTests pins the host's half).
    /// </summary>
    [Fact]
    public void The_ai_create_server_answers_the_cores_name_under_a_name_of_its_own()
    {
        var screen = new FakeCreateServer().Register(new UiSampleServices()).Create<CreateServer>();
        var core = new SourceSharp.Client.Ui.Screens.CreateServer(null!, null!, null!, null!);
        Assert.Equal("createserver", core.Name);
        Assert.NotEqual(core.Name, screen.Name);
        Assert.Contains(core.Name, screen.Aliases);
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
