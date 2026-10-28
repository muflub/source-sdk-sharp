using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// A wall charger's client half: the sequence and cycle the server holds it
/// at (<see cref="IChargerNetState"/>), applied to the skeleton, as stock's
/// C_BaseAnimating draws a networked m_nSequence and m_flCycle that nothing on
/// the client advances.
/// </summary>
public abstract class ClientWallCharger : ClientStandaloneEntity
{
    protected ClientWallCharger(IEngineEntityRender render)
        : base(render)
    {
    }

    /// <summary>The charger's table.</summary>
    protected abstract IChargerNetState Display { get; }

    /// <summary>The sequence last started here, so an update that kept it does not restart it.</summary>
    private int _playing = -1;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (Display.Sequence != _playing)
        {
            Render.PlaySequence(Handle, Display.Sequence, 1f, restart: true);
            _playing = Display.Sequence;
        }

        Render.SetCycle(Handle, Display.Cycle);
    }

    public override void OnRemoved() => _playing = -1;
}

/// <summary>item_healthcharger's client half.</summary>
[ClientStandaloneEntity(EntityNames.ItemHealthcharger)]
public sealed class ClientHealthCharger : ClientWallCharger
{
    public ClientHealthCharger(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.ItemHealthcharger;

    [NetState] public HealthChargerNetState State = new();

    protected override IChargerNetState Display => State;
}

/// <summary>item_suitcharger's client half.</summary>
[ClientStandaloneEntity(EntityNames.ItemSuitcharger)]
public sealed class ClientSuitCharger : ClientWallCharger
{
    public ClientSuitCharger(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.ItemSuitcharger;

    [NetState] public SuitChargerNetState State = new();

    protected override IChargerNetState Display => State;
}
