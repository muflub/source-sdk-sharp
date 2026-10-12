namespace SourceSharp.Abi;

/// <summary>The real one: the four step-6f slots, and the pointer work over them.</summary>
internal sealed unsafe class ClientReplayEngine : IClientReplayEngine
{
    /// <summary>
    /// The side's own slot routing, handed over by the area that holds this
    /// (plan_abi_hotreload D8): it was <c>Engine.Current.Slots</c>, which
    /// followed the active side. One field read per call, as before.
    /// </summary>
    private readonly EngineSide _api;

    internal ClientReplayEngine(EngineSide api) => _api = api;

    public bool TryGetUserCmd(int commandNumber, out UserCmd cmd)
    {
        cmd = default;

        if (!_api.ClientAttached)
            return false;

        UserCmd fetched = default;

        if (_api.ClientShims.ClientGetUserCmd(commandNumber, &fetched) == 0)
            return false;

        cmd = fetched;
        return true;
    }

    public bool BeginCommand(EntityHandle player, int commandNumber,
                             out MoveFrameConstants constants)
    {
        constants = default;

        if (!_api.ClientAttached)
            return false;

        // ONLY THE CONSTANT WORDS ARE FILLED. The rest of the struct is the
        // managed player's state, and native has no copy of it to write here —
        // which is the whole of what this step moved.
        MoveStateBlob blob = default;

        if (_api.ClientShims.MoveBeginClientCommand(player.Value, commandNumber, &blob) == 0)
            return false;

        constants = new MoveFrameConstants(MovementBridge.ReadConstants(&blob),
                                           blob.CvJumpHeight, blob.EngineTime,
                                           blob.ModelScale, blob.CurrentGravity);
        return true;
    }

    public int EndCommand(bool ran)
    {
        if (!_api.ClientAttached)
            return -1;

        int groundIndex = -1;
        _api.ClientShims.MoveEndClientCommand(ran ? 1 : 0, &groundIndex);
        return groundIndex;
    }

    public void PublishView(EntityHandle player, in Vector origin, in Vector viewOffset,
                            in QAngle punch, in QAngle punchVel, bool driving, int health)
    {
        if (!_api.ClientAttached)
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

        _api.ClientShims.ClientPlayerPredictionDrive(player.Value, &view);
    }
}
