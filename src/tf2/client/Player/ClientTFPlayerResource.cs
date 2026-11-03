using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The client half of <c>tf_player_manager</c>: the decoded TF scoreboard
/// columns, the server's word (stock's <c>C_TFPlayerResource</c>). Kept in
/// <see cref="TFClientPlayerResources"/> from its first update to its removal,
/// for the scoreboard (Stage 3.1) and <c>tf2_cl_player_resource</c>.
/// </summary>
[ClientStandaloneEntity(TFEntityNames.PlayerResource)]
public sealed class ClientTFPlayerResource : ClientStandaloneEntity
{
    private readonly TFClientPlayerResources _resources;

    public ClientTFPlayerResource(IEngineEntityRender render, TFClientPlayerResources resources)
        : base(render)
    {
        _resources = resources;
    }

    public override string ClassName => TFEntityNames.PlayerResource;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFPlayerResourceNetState State = new();

    /// <summary>How many updates have decoded into <see cref="State"/>.</summary>
    public int Decodes { get; private set; }

    public override void OnSpawn() => _resources.Live = this;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;
        _resources.Live = this;
    }

    public override void OnRemoved()
    {
        if (ReferenceEquals(_resources.Live, this))
            _resources.Live = null;
    }
}

/// <summary>The client's live <c>tf_player_manager</c>, when one has arrived.</summary>
[SideService(Side.Client)]
public sealed class TFClientPlayerResources
{
    public ClientTFPlayerResource? Live { get; set; }

    /// <summary>The rows of the players with a class, one line each.</summary>
    public string Report()
    {
        if (Live is not { } resource)
            return $"[TF2] {TFEntityNames.PlayerResource}: none has arrived";

        var state = resource.State;
        var text = new StringBuilder($"[TF2] {TFEntityNames.PlayerResource}: {resource.Decodes} update(s)");
        for (int i = 1; i < TFPlayerResourceNetState.Slots; i++)
        {
            if (state.PlayerClass[i] == 0 && state.MaxHealth[i] == 0)
                continue;

            text.Append($"\n  player {i}: class {TFClassNames.NonLocalized((TFClass)state.PlayerClass[i])}, " +
                        $"max health {state.MaxHealth[i]}, score {state.TotalScore[i]}, damage {state.Damage[i]}, " +
                        $"healing {state.Healing[i]}, respawn at {state.NextRespawnTime[i]:F1}");
        }

        return text.ToString();
    }
}

/// <summary>The console door onto the client's TF scoreboard columns.</summary>
[ConCommand("tf2_cl_player_resource", Help = "The tf_player_manager rows this client has decoded")]
public sealed class TFPlayerResourceReportCommand(ILog log, TFClientPlayerResources resources) : IConCommand
{
    public void Execute(string args) => log.Msg(resources.Report() + "\n");
}
