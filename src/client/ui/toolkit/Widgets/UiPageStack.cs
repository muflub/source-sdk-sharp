using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>A page of a dialog's page stack: options → video → advanced.</summary>
public interface IUiPage
{
    /// <summary>The page's title, a #Token.</summary>
    string TitleToken { get; }

    void Describe(ref UiFrame ui);
}

/// <summary>
/// A page stack (plan_client_ui §5.7a): a dialog pushes and pops sub-pages with
/// Back and Esc wired. Esc pops one page and is consumed, so it never closes two
/// things; Esc on the first page answers true from <see cref="Describe"/> so the
/// owner can close.
/// </summary>
public sealed class UiPageStack
{
    private readonly List<IUiPage> _pages = new(4);

    public UiPageStack(IUiPage root) => _pages.Add(root);

    public int Depth => _pages.Count;

    public IUiPage Top => _pages[^1];

    public void Push(IUiPage page) => _pages.Add(page);

    /// <summary>Back one page; false at the root.</summary>
    public bool Pop()
    {
        if (_pages.Count == 1)
            return false;
        _pages.RemoveAt(_pages.Count - 1);
        return true;
    }

    /// <summary>
    /// Describes the top page under a scope of its depth (so a page's state is
    /// not another page's), after handling Esc. Answers true when Esc was pressed
    /// on the root page: the owner closes.
    /// </summary>
    public bool Describe(ref UiFrame ui)
    {
        bool close = false;
        if (ui.ConsumeKey((int)UiKey.Escape) && !Pop())
            close = true;

        ui.Context.PushScope(_pages.Count);
        Top.Describe(ref ui);
        ui.Context.PopScope();
        return close;
    }
}
