namespace Debug.Api.Client.Interfaces;

/// <summary>
/// Everything the client serves its dev API, one property per group
/// (plan_devapi_interfaces §2, P3). SourceSharp.Client builds one per client
/// composition and hands it to the dev API's constructor.
/// </summary>
public interface IDebugClientHooks
{
    /// <summary>The client's console.</summary>
    IDebugClientLog Log { get; }

    /// <summary>The client's build and the crossing counters.</summary>
    IDebugClientBuild Build { get; }

    /// <summary>The client's own prediction ring.</summary>
    IDebugClientPrediction Prediction { get; }

    /// <summary>The client's frame counters and pose. Game thread.</summary>
    IDebugClientFrame Frame { get; }

    /// <summary>The client's own entity list. Game thread.</summary>
    IDebugClientEntities Entities { get; }

    /// <summary>The client's recv tables and send/recv agreement. Game thread.</summary>
    IDebugClientNetTables NetTables { get; }

    /// <summary>The client's managed reload: the last, and a request for the next. Game thread.</summary>
    IDebugClientReload Reload { get; }

    /// <summary>The client's net channel, as the net graph drew it. Game thread.</summary>
    IDebugClientNetGraph NetGraph { get; }

    /// <summary>The absences raised this run. Game thread.</summary>
    IDebugClientAbsences Absences { get; }

    /// <summary>The material proxies: names, binds per frame, last output per material (lane c5-proxies). Game thread.</summary>
    IDebugClientProxies Proxies { get; }

    /// <summary>The render plan: its path, its last plan, its same-run A/B (C7).</summary>
    IDebugClientRender Render { get; }

    /// <summary>The UI: its last frame's draws and its fonts (plan_client_ui U2).</summary>
    IDebugClientUi Ui { get; }

    /// <summary>The debug camera: its state, and a cheat-gated switch. Game thread.</summary>
    IDebugClientViewCamera ViewCamera { get; }
    /// <summary>The client's particle systems and definition reads. Game thread.</summary>
    IDebugClientParticles Particles { get; }
    /// <summary>The input: key states, the mouse, the last commands, and the write that injects one (lane c6-input).</summary>
    IDebugClientInput Input { get; }
}
