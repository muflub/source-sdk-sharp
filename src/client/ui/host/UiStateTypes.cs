using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// Finds a carried state type by its full name in this build (plan_client_ui
/// U5): the toolkit's, the screens', this assembly's, or the runtime's own
/// (bool, long). A name none of them has is a state type the edit removed.
/// </summary>
public static class UiStateTypes
{
    public static Type? Resolve(string fullName)
        => typeof(UiContext).Assembly.GetType(fullName)
           ?? typeof(SourceSharp.Client.Ui.Screens.ScreensAssembly).Assembly.GetType(fullName)
           ?? typeof(UiStateTypes).Assembly.GetType(fullName)
           ?? typeof(object).Assembly.GetType(fullName);
}
