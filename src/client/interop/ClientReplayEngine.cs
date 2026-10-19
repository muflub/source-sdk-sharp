namespace SourceSharp.Abi;

/// <summary>The real one: the four step-6f slots, and the pointer work over them.</summary>
internal sealed unsafe class ClientReplayEngine : IClientReplayEngine
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private readonly PredictionClientArea? _predictionClient;
    private PredictionClientArea PredictionClient => _predictionClient ?? throw ShimAreaGuard.NotAttached(nameof(PredictionClient));

    internal ClientReplayEngine(PredictionClientArea? predictionClient)
    {
        _predictionClient = predictionClient;
    }

    public bool TryGetUserCmd(int commandNumber, out UserCmd cmd)
    {
        cmd = default;

        if (_predictionClient is null)
            return false;

        UserCmd fetched = default;

        if (PredictionClient.ClientGetUserCmd(commandNumber, &fetched) == 0)
            return false;

        cmd = fetched;
        return true;
    }

    public bool BeginCommand(EntityHandle player, int commandNumber,
                             out MoveFrameConstants constants)
    {
        constants = default;

        if (_predictionClient is null)
            return false;

        // ONLY THE CONSTANT WORDS ARE FILLED. The rest of the struct is the
        // managed player's state, and native has no copy of it to write here —
        // which is the whole of what this step moved.
        MoveStateBlob blob = default;

        if (PredictionClient.MoveBeginClientCommand(player.Value, commandNumber, &blob) == 0)
            return false;

        constants = new MoveFrameConstants(MovementBridge.ReadConstants(&blob),
                                           blob.CvJumpHeight, blob.EngineTime,
                                           blob.ModelScale, blob.CurrentGravity);
        return true;
    }

    public int EndCommand(bool ran)
    {
        if (_predictionClient is null)
            return -1;

        int groundIndex = -1;
        PredictionClient.MoveEndClientCommand(ran ? 1 : 0, &groundIndex);
        return groundIndex;
    }

    public void PublishView(EntityHandle player, in Vector origin, in Vector viewOffset,
                            in QAngle punch, in QAngle punchVel, bool driving, int health)
    {
        if (_predictionClient is null)
            return;

        ClientPlayerViewBlob view = default;
        view.StructSize = sizeof(ClientPlayerViewBlob);

        view.OriginX = origin.X;
        view.OriginY = origin.Y;
        view.OriginZ = origin.Z;

        view.ViewOffsetX = viewOffset.X;
        view.ViewOffsetY = viewOffset.Y;
        view.ViewOffsetZ = viewOffset.Z;

        view.PunchX = punch.Pitch;
        view.PunchY = punch.Yaw;
        view.PunchZ = punch.Roll;

        view.PunchVelX = punchVel.Pitch;
        view.PunchVelY = punchVel.Yaw;
        view.PunchVelZ = punchVel.Roll;

        view.Driving = driving ? 1 : 0;
        view.Health = health;

        PredictionClient.ClientPlayerPredictionDrive(player.Value, &view);
    }
}
