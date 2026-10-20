namespace SourceSharp.Client.Interop;

/// <summary>
/// The client mode's layout (IShimUiClient's <c>ClientModeLayout</c>):
/// g_pClientMode->Layout(), which sizes the viewport to the client's root
/// panel. When to ask is the caller's.
/// </summary>
public interface IClientModeLayout
{
    void Layout();
}
