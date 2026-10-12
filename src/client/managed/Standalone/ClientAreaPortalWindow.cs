using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_areaportalwindow.
///
/// <para>
/// It hands the shell two things and draws nothing itself, which is the whole
/// difference between this and <see cref="ClientBeam"/>: a window IS a brush
/// model, so the shell already knows how to draw it, and what this side adds is
/// only how solid it looks and what sits behind it.
/// </para>
///
/// <para>
/// The fade is the entity, not a finish on it. The server stops sending what is
/// beyond the pane once the player is past FadeDist, and the pane goes opaque
/// over the same band so that there is nothing to see through by the time
/// there is nothing behind it. Both sides read the same two numbers off this
/// state for exactly that reason.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncAreaportalwindow)]
public sealed class ClientAreaPortalWindow : ClientStandaloneEntity
{
    public ClientAreaPortalWindow(IEngineEntityRender render)
        : base(render)
    {
    }



    public override string ClassName => EntityNames.FuncAreaportalwindow;

    [NetState] public AreaPortalWindowNetState State = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Render.SetFade(Handle, FadeStyle.PortalWindow,
                                 State.FadeStartDist, State.FadeDist,
                                 State.TranslucencyLimit);

        Render.SetOverlayModel(Handle, State.BackgroundModelIndex);
    }
}
