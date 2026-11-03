namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// One tab of an options window (stock's options dialog and its property
/// pages): every page loads when the window opens, and the window's Apply,
/// OK and Cancel act on all of them at once. A <see cref="UiForm{TModel}"/> is
/// one; a page whose choices the engine lists at run time is written by hand.
/// </summary>
public interface IUiOptionsPage
{
    /// <summary>The tab's title token.</summary>
    string TabToken { get; }

    /// <summary>The page's name, a panel its window opens on (<c>cs_ui_open audiooptions</c>).</summary>
    string PageName { get; }

    /// <summary>From the engine into the page's saved and working copies.</summary>
    void Load();

    /// <summary>The changed settings to the engine; false, writing nothing, while any is invalid.</summary>
    bool Apply();

    bool AnyDirty { get; }

    bool IsValid { get; }

    /// <summary>Keys the page takes before its window sees them (an armed key capture's Escape).</summary>
    void TakeKeys(ref UiFrame ui);

    /// <summary>The page's fields, inside a window the caller has opened.</summary>
    void DescribeBody(ref UiFrame ui);
}
