namespace SourceSharp.Abi;

/// <summary>
/// The replay engine for a process with no game under it: every ask refuses.
///
/// <para>
/// REFUSES RATHER THAN ANSWERS A DEFAULT, and that is the same rule step 3e
/// wrote into the trace slots themselves. "No command" ends a replay; a
/// fabricated command would replay something the player never sent.
/// </para>
/// </summary>
public sealed class NoClientReplayEngine : IClientReplayEngine
{
    public static readonly NoClientReplayEngine Instance = new();

    private NoClientReplayEngine() { }

    public bool TryGetUserCmd(int commandNumber, out UserCmd cmd)
    {
        cmd = default;
        return false;
    }

    public bool BeginCommand(EntityHandle player, int commandNumber,
                             out MoveFrameConstants constants)
    {
        constants = default;
        return false;
    }

    public int EndCommand(bool ran) => -1;

    public void PublishView(EntityHandle player, in Vector origin, in Vector viewOffset,
                            in QAngle punch, in QAngle punchVel, bool driving, int health)
    {
    }
}
