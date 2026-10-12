//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The crossing's layout, read and written: pure functions of the blob, shared
/// by <see cref="MovementRunner"/> and the client's replay bracket.
/// </summary>
public static unsafe class MovementBridge
{
    /// <summary>
    /// The constant words of the crossing, as the loop's own struct.
    ///
    /// <para>
    /// INTERNAL RATHER THAN PRIVATE SINCE STEP 6f: the client's replay bracket
    /// (<see cref="ClientReplayEngine"/>) is handed the same words by native
    /// and must read them the same way. Two readers of one layout is how the
    /// two sides stop agreeing.
    /// </para>
    /// </summary>
    internal static MoveConstants ReadConstants(MoveStateBlob* b) => new()
    {
        Friction = b->CvFriction,
        Accelerate = b->CvAccelerate,
        AirAccelerate = b->CvAirAccelerate,
        StopSpeed = b->CvStopSpeed,
        MaxVelocity = b->CvMaxVelocity,
        MaxSpeed = b->CvMaxSpeed,
        Bounce = b->CvBounce,
        RollAngle = b->CvRollAngle,
        RollSpeed = b->CvRollSpeed,
        NoClipSpeed = b->CvNoClipSpeed,
        NoClipAccelerate = b->CvNoClipAccelerate,
        SpecSpeed = b->CvSpecSpeed,
        SpecAccelerate = b->CvSpecAccelerate,
        AirSpeedCap = b->CvAirSpeedCap,
        DuckSpeedScale = b->CvDuckSpeedScale,
        Optimizations = b->CvOptimizations != 0,
        SpecNoClip = b->CvSpecNoClip != 0,
        MaxClients = b->CvMaxClients,
        TickInterval = b->CvTickInterval,
        View = new Vector(b->CvViewX, b->CvViewY, b->CvViewZ),
        HullMin = new Vector(b->CvHullMinX, b->CvHullMinY, b->CvHullMinZ),
        HullMax = new Vector(b->CvHullMaxX, b->CvHullMaxY, b->CvHullMaxZ),
        DuckHullMin = new Vector(b->CvDuckHullMinX, b->CvDuckHullMinY, b->CvDuckHullMinZ),
        DuckHullMax = new Vector(b->CvDuckHullMaxX, b->CvDuckHullMaxY, b->CvDuckHullMaxZ),
        DuckView = new Vector(b->CvDuckViewX, b->CvDuckViewY, b->CvDuckViewZ),
        ObsHullMin = new Vector(b->CvObsHullMinX, b->CvObsHullMinY, b->CvObsHullMinZ),
        ObsHullMax = new Vector(b->CvObsHullMaxX, b->CvObsHullMaxY, b->CvObsHullMaxZ),
        DeadViewHeight = new Vector(b->CvDeadViewHeightX, b->CvDeadViewHeightY, b->CvDeadViewHeightZ),
    };

    /// <summary>The blob as the loop's own state.</summary>
    public static MoveState ReadState(MoveStateBlob* b) => new()
    {
        CommandNumber = b->CommandNumber,
        FrameTime = b->FrameTime,
        MoveType = b->MoveType,
        Flags = b->Flags,
        WaterLevel = b->WaterLevel,
        WaterType = b->WaterType,
        DeadFlag = b->DeadFlag != 0,
        Origin = new Vector(b->OriginX, b->OriginY, b->OriginZ),
        Velocity = new Vector(b->VelocityX, b->VelocityY, b->VelocityZ),
        BaseVelocity = new Vector(b->BaseVelocityX, b->BaseVelocityY, b->BaseVelocityZ),
        ViewAngles = new Vector(b->ViewAnglesX, b->ViewAnglesY, b->ViewAnglesZ),
        ForwardMove = b->ForwardMove,
        SideMove = b->SideMove,
        UpMove = b->UpMove,
        Buttons = b->Buttons,
        OldButtons = b->OldButtons,
        MaxSpeed = b->MaxSpeed,
        ClientMaxSpeed = b->ClientMaxSpeed,
        Ducked = b->Ducked != 0,
        Ducking = b->Ducking != 0,
        InDuckJump = b->InDuckJump != 0,
        DuckTime = b->DuckTime,
        DuckJumpTime = b->DuckJumpTime,
        JumpTime = b->JumpTime,
        FallVelocity = b->FallVelocity,
        StepSize = b->StepSize,
        ViewOffset = new Vector(b->ViewOffsetX, b->ViewOffsetY, b->ViewOffsetZ),
        PunchAngle = new Vector(b->PunchAngleX, b->PunchAngleY, b->PunchAngleZ),
        PunchAngleVel = new Vector(b->PunchAngleVelX, b->PunchAngleVelY, b->PunchAngleVelZ),
        EntityGravity = b->EntityGravity,
        CurrentGravity = b->CurrentGravity,
        SurfaceFriction = b->SurfaceFriction,
        HasGround = b->HasGround != 0,
        GroundVelocity = new Vector(b->GroundVelocityX, b->GroundVelocityY, b->GroundVelocityZ),
        WaterJumpTime = b->WaterJumpTime,
        WaterJumpVel = new Vector(b->WaterJumpVelX, b->WaterJumpVelY, b->WaterJumpVelZ),
        SwimSoundTime = b->SwimSoundTime,
        StepSoundTime = b->StepSoundTime,
        Health = b->Health,
        GameCodeMovedPlayer = b->GameCodeMovedPlayer != 0,
        ConstraintRadius = b->ConstraintRadius,
        ConstraintWidth = b->ConstraintWidth,
        ConstraintSpeedFactor = b->ConstraintSpeedFactor,
        ConstraintCenter = new Vector(b->ConstraintCenterX, b->ConstraintCenterY, b->ConstraintCenterZ),
        OldForwardMove = b->OldForwardMove,
        ImpulseCommand = b->ImpulseCommand,
        LaggedMovementValue = b->LaggedMovementValue,
        SurfaceProps = b->SurfaceProps,
        LadderNormal = new Vector(b->LadderNormalX, b->LadderNormalY, b->LadderNormalZ),
        OutStepHeight = b->OutStepHeight,
        OutWishVel = new Vector(b->OutWishVelX, b->OutWishVelY, b->OutWishVelZ),
        OutJumpVel = new Vector(b->OutJumpVelX, b->OutJumpVelY, b->OutJumpVelZ),
        MoveCollide = b->MoveCollide,
        OnLadder = b->OnLadder,
        OldWaterLevel = b->OldWaterLevel,
        IsAlive = b->IsAlive != 0,
        ObserverMode = b->ObserverMode,
        InAVehicle = b->InAVehicle != 0,
        ModelScale = b->ModelScale,
        EntIndex = b->EntIndex,
        StuckLast = b->StuckLast,
        Angles = new Vector(b->AnglesX, b->AnglesY, b->AnglesZ),
        OldAngles = new Vector(b->OldAnglesX, b->OldAnglesY, b->OldAnglesZ),
        FirstRunOfFunctions = b->FirstRunOfFunctions != 0,
        CurTime = b->CurTime,
        SurfaceDataValid = b->SurfaceDataValid != 0,
        AllowAutoMovement = b->AllowAutoMovement != 0,
        SlowMovement = b->SlowMovement != 0,
        IsServer = b->IsServer != 0,
    };

    /// <summary>
    /// What the loop produced, back into the blob.
    ///
    /// <para>
    /// ONLY the fields the loop writes. The constants, the identity and the
    /// command's own inputs are not written back, so a native side that reads
    /// the whole struct back gets its own values for them — which is what stock
    /// does, since CMoveData's inputs are copied in before the call and only the
    /// outputs are copied out after it.
    /// </para>
    /// </summary>
    public static void WriteState(MoveStateBlob* b, MoveState s)
    {
        b->MoveType = s.MoveType;
        b->Flags = s.Flags;
        b->WaterLevel = s.WaterLevel;
        b->WaterType = s.WaterType;
        b->OriginX = s.Origin.X; b->OriginY = s.Origin.Y; b->OriginZ = s.Origin.Z;
        b->VelocityX = s.Velocity.X; b->VelocityY = s.Velocity.Y; b->VelocityZ = s.Velocity.Z;
        b->BaseVelocityX = s.BaseVelocity.X; b->BaseVelocityY = s.BaseVelocity.Y; b->BaseVelocityZ = s.BaseVelocity.Z;
        b->ForwardMove = s.ForwardMove;
        b->SideMove = s.SideMove;
        b->UpMove = s.UpMove;
        b->OldButtons = s.OldButtons;
        b->MaxSpeed = s.MaxSpeed;
        b->Ducked = s.Ducked ? 1 : 0;
        b->Ducking = s.Ducking ? 1 : 0;
        b->InDuckJump = s.InDuckJump ? 1 : 0;
        b->DuckTime = s.DuckTime;
        b->DuckJumpTime = s.DuckJumpTime;
        b->JumpTime = s.JumpTime;
        b->FallVelocity = s.FallVelocity;
        b->ViewOffsetX = s.ViewOffset.X; b->ViewOffsetY = s.ViewOffset.Y; b->ViewOffsetZ = s.ViewOffset.Z;
        b->PunchAngleX = s.PunchAngle.X; b->PunchAngleY = s.PunchAngle.Y; b->PunchAngleZ = s.PunchAngle.Z;
        b->PunchAngleVelX = s.PunchAngleVel.X; b->PunchAngleVelY = s.PunchAngleVel.Y; b->PunchAngleVelZ = s.PunchAngleVel.Z;
        b->EntityGravity = s.EntityGravity;
        b->SurfaceFriction = s.SurfaceFriction;
        b->HasGround = s.HasGround ? 1 : 0;
        b->GroundVelocityX = s.GroundVelocity.X; b->GroundVelocityY = s.GroundVelocity.Y; b->GroundVelocityZ = s.GroundVelocity.Z;
        b->WaterJumpTime = s.WaterJumpTime;
        b->WaterJumpVelX = s.WaterJumpVel.X; b->WaterJumpVelY = s.WaterJumpVel.Y; b->WaterJumpVelZ = s.WaterJumpVel.Z;
        b->SwimSoundTime = s.SwimSoundTime;
        b->StepSoundTime = s.StepSoundTime;
        b->SurfaceProps = s.SurfaceProps;
        b->LadderNormalX = s.LadderNormal.X; b->LadderNormalY = s.LadderNormal.Y; b->LadderNormalZ = s.LadderNormal.Z;
        b->OutStepHeight = s.OutStepHeight;
        b->OutWishVelX = s.OutWishVel.X; b->OutWishVelY = s.OutWishVel.Y; b->OutWishVelZ = s.OutWishVel.Z;
        b->OutJumpVelX = s.OutJumpVel.X; b->OutJumpVelY = s.OutJumpVel.Y; b->OutJumpVelZ = s.OutJumpVel.Z;
        b->MoveCollide = s.MoveCollide;
        b->OnLadder = s.OnLadder;
        b->OldWaterLevel = s.OldWaterLevel;
        b->StuckLast = s.StuckLast;
        b->AnglesX = s.Angles.X; b->AnglesY = s.Angles.Y; b->AnglesZ = s.Angles.Z;
        b->SurfaceDataValid = s.SurfaceDataValid ? 1 : 0;
        b->OldForwardMove = s.OldForwardMove;
    }

    /// <summary>
    /// The world, over the trace ABI.
    ///
    /// <para>
    /// No game state: every method is one call through the slot routing of the
    /// side whose runner built it, and the player it is asked about is the one
    /// native is holding for the duration of the crossing. That is why these
    /// slots refuse outside a crossing rather than answering a default.
    /// </para>
    /// </summary>
    internal sealed class EngineMoveWorld : IMoveWorld
    {
        // The side's slot routing, given by the runner that owns this world
        // (plan_abi_hotreload D8); it was Engine.Current.Slots per call.
        private readonly EngineSide _api;

        internal EngineMoveWorld(EngineSide api) => _api = api;

        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                               uint mask, int collisionGroup)
        {
            if (!(_api.ServerAttached || _api.ClientAttached))
                return MoveTrace.Clear(end);

            float* s = stackalloc float[3] { start.X, start.Y, start.Z };
            float* e = stackalloc float[3] { end.X, end.Y, end.Z };
            float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
            float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

            MoveTraceBlob blob = default;
            if (_api.ServerShimsOrNull is { } __att1) __att1.MoveTrace(s, e, lo, hi, (int)mask, collisionGroup, &blob);
            else _api.ClientShims.MoveTrace(s, e, lo, hi, (int)mask, collisionGroup, &blob);

            return new MoveTrace
            {
                Fraction = blob.Fraction,
                EndPos = new Vector(blob.EndPosX, blob.EndPosY, blob.EndPosZ),
                PlaneNormal = new Vector(blob.PlaneNormalX, blob.PlaneNormalY, blob.PlaneNormalZ),
                PlaneDist = blob.PlaneDist,
                StartSolid = blob.StartSolid != 0,
                AllSolid = blob.AllSolid != 0,
                SurfaceProps = blob.SurfaceProps,
                SurfaceFlags = blob.SurfaceFlags,
                Contents = blob.Contents,
                HasGround = blob.HasGround != 0,
                GroundVelocity = new Vector(blob.GroundVelocityX, blob.GroundVelocityY, blob.GroundVelocityZ),
                HasEntity = blob.HasEntity != 0,
                HitWorld = blob.HitWorld != 0,
            };
        }

        public int PointContents(in Vector point, int slot)
        {
            if (!(_api.ServerAttached || _api.ClientAttached))
                return 0;

            float* p = stackalloc float[3] { point.X, point.Y, point.Z };
            return (_api.ServerShimsOrNull is { } __att2 ? __att2.MovePointContents(p, slot) : _api.ClientShims.MovePointContents(p, slot));
        }

        public MoveSurface Surface(int surfaceProps)
        {
            if (!(_api.ServerAttached || _api.ClientAttached))
                return default;

            MoveSurfaceBlob blob = default;
            if ((_api.ServerShimsOrNull is { } __att3 ? __att3.MoveSurface(surfaceProps, &blob) : _api.ClientShims.MoveSurface(surfaceProps, &blob)) == 0)
                return default;

            return new MoveSurface
            {
                Valid = blob.Valid != 0,
                Friction = blob.Friction,
                Material = blob.Material,
                Climbable = blob.Climbable,
                MaxSpeedFactor = blob.MaxSpeedFactor,
                JumpFactor = blob.JumpFactor,
            };
        }

        private int Effect(MoveEffectKind kind, in Vector xyz, float value, int flag)
        {
            if (!(_api.ServerAttached || _api.ClientAttached))
                return 1;

            float* p = stackalloc float[3] { xyz.X, xyz.Y, xyz.Z };
            return (_api.ServerShimsOrNull is { } __att4 ? __att4.MoveEffect((int)kind, p, value, flag) : _api.ClientShims.MoveEffect((int)kind, p, value, flag));
        }

        public void ResetTouchList() => Effect(MoveEffectKind.ResetTouchList, Vector.Zero, 0f, 0);

        public void AddToTouched(in MoveTrace trace, in Vector velocity)
            => Effect(MoveEffectKind.AddToTouched, trace.EndPos, trace.Fraction, 0);

        public void StartSound(in Vector origin, string sound)
            => Effect(MoveEffectKind.StartSoundSwim, origin, 0f, 0);

        public void PlayerSetAnimation(int activity)
            => Effect(MoveEffectKind.SetAnimation, Vector.Zero, 0f, activity);

        public bool PlayerFallingDamage(float damage)
            => Effect(MoveEffectKind.FallingDamage, Vector.Zero, damage, 0) != 0;

        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force)
            => Effect(MoveEffectKind.PlayStepSound, origin, volume, surfaceProps);

        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps)
            => Effect(MoveEffectKind.UpdateStepSound, origin, 0f, surfaceProps);

        public void Splash() => Effect(MoveEffectKind.Splash, Vector.Zero, 0f, 0);

        public void RumbleEffect(byte index, byte data, byte flags)
            => Effect(MoveEffectKind.Rumble, Vector.Zero, 0f, index);

        public void ResetDuckLatched() => Effect(MoveEffectKind.ResetDuckLatched, Vector.Zero, 0f, 0);

        public void SetPlayerSurface(int material)
            => Effect(MoveEffectKind.SetPlayerSurface, Vector.Zero, 0f, material);

        /// <summary>
        /// <c>engine-&gt;Time()</c>, which travels on the state rather than
        /// through a slot of its own — see <c>MoveStateBlob.EngineTime</c>.
        /// </summary>
        public float EngineTime() => Time;

        /// <summary>Set for the duration of one crossing, from the state.</summary>
        public float Time { get; set; }
    }
}
