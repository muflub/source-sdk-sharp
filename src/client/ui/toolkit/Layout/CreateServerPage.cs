namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>What the create-server window has chosen so far, handed to a mod's page: the map (null for a random one) and the player count.</summary>
public readonly record struct CreateServerChoices(string? Map, int MaxPlayers);

/// <summary>
/// A page a mod adds to the core's create-server window (plan_gameui_managed.md
/// G3; plan_hl2mp_ai §3.5): a tab after the core's Server and Game pages. The
/// window finds every concrete page in the mod's UI assemblies as it finds
/// the mod's screens (<see cref="CreateServerPages.Discover"/>), builds it
/// through the side's container, and never names a mod's type. A page may
/// refuse Start with a reason, and adds the console lines its choices need,
/// which the window runs after the server's size and before the map.
/// </summary>
public abstract class CreateServerPage
{
    /// <summary>The page's tab title.</summary>
    public abstract string TabToken { get; }

    /// <summary>The window size the page needs to show its body whole, du: the window's minimum grows to the largest page's.</summary>
    public virtual float MinWidthDu => 0;

    public virtual float MinHeightDu => 0;

    /// <summary>The page's body, in the window's content area (it fills it).</summary>
    public abstract void Describe(ref UiFrame ui, in CreateServerChoices choices);

    /// <summary>Why Start is refused with these choices (a token), or "" when the page allows it.</summary>
    public virtual string StartRefusalToken(in CreateServerChoices choices) => "";

    /// <summary>The console lines Start runs for this page, after <c>maxplayers</c> and before the map.</summary>
    public virtual void BeforeMap(in CreateServerChoices choices, List<string> lines)
    {
    }
}

/// <summary>Every concrete <see cref="CreateServerPage"/> in some assemblies, by full name (the rule <see cref="UiScreens"/> keeps).</summary>
public static class CreateServerPages
{
    public static IReadOnlyList<Type> Discover(params System.Reflection.Assembly[] assemblies)
        => [.. assemblies.SelectMany(a => a.GetTypes()
                                          .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(CreateServerPage).IsAssignableFrom(t))
                                          .OrderBy(t => t.FullName, StringComparer.Ordinal))];
}

/// <summary>
/// The create-server window's frame, one for the core's window and a mod
/// page's preview: its id, title, size, the core's two tabs, and the button
/// row with Start's refusal beside it.
/// </summary>
public static class CreateServerWindow
{
    public const string Id = "createserver", TitleToken = "#SS_UI_Create_Title", ServerTab = "#SS_UI_Create_Server", GameTab = "#SS_UI_Create_Game";

    /// <summary>The core's pages before a mod's.</summary>
    public const int CorePages = 2;

    /// <summary>The core's own minimum, du.</summary>
    public const float MinWidthDu = 480, MinHeightDu = 380;

    /// <summary>
    /// 760 x 600 du by default, at least 480 x 380, both grown to what the
    /// pages need (<see cref="Minimum"/>); narrow below 600 du.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH, (float W, float H) minimum)
        => new(MathF.Min(MathF.Max(760, minimum.W), screenW), MathF.Min(MathF.Max(600, minimum.H), screenH),
               minimum.W, minimum.H, screenW, screenH, 600);

    /// <summary>The window's minimum: the core's, grown to each page's.</summary>
    public static (float W, float H) Minimum(IReadOnlyList<CreateServerPage> pages)
        => (pages.Select(p => p.MinWidthDu).Append(MinWidthDu).Max(), pages.Select(p => p.MinHeightDu).Append(MinHeightDu).Max());

    /// <summary>The tabs: the core's two, then each page's.</summary>
    public static string[] Tabs(IReadOnlyList<CreateServerPage> pages)
        => [ServerTab, GameTab, .. pages.Select(p => p.TabToken)];

    /// <summary>Start's refusal (when there is one), Cancel and Start; true when Start was clicked.</summary>
    public static bool Buttons(ref UiFrame ui, UiWindows windows, string refusalToken, bool canStart)
    {
        bool start = false;
        using (ui.DialogButtons().Width(Size.Fill))
        {
            if (ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                windows.RequestClose(Id);
            ui.Flex();
            if (refusalToken.Length > 0)
                ui.Text(refusalToken, Text.Body, Tone.Danger).Elide();
            if (ui.Button("start", "#SS_UI_Create_Start", primary: true, enabled: canStart && refusalToken.Length == 0).Clicked)
                start = true;
        }

        return start;
    }
}

/// <summary>
/// A mod page in the create-server window's frame, on its own tab, for the
/// mod's samples (a mod's UI assembly cannot build the core's window). Lives
/// in the toolkit, which the game never places screens from.
/// </summary>
public sealed class CreateServerPagePreview(UiWindows windows, CreateServerPage page, CreateServerChoices choices) : UiScreen, IUiWindowed
{
    private readonly string[] _tabs = CreateServerWindow.Tabs([page]);
    private readonly string _name = CreateServerWindow.Id + "-" + page.GetType().Name.ToLowerInvariant();
    private readonly (float W, float H) _minimum = CreateServerWindow.Minimum([page]);

    /// <summary><c>createserver-&lt;page type&gt;</c>: apart from the core's window, whose previews and goldens are its own.</summary>
    public override string Name => _name;

    public string WindowId => CreateServerWindow.Id;

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Window(windows, WindowId, CreateServerWindow.TitleToken, CreateServerWindow.Spec(ui.DesignWidth, ui.DesignHeight, _minimum)))
        {
            ui.TabBar(_tabs, CreateServerWindow.CorePages);
            using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, Size.Fill))
                page.Describe(ref ui, choices);
            CreateServerWindow.Buttons(ref ui, windows, page.StartRefusalToken(choices), canStart: true);
        }
    }
}
