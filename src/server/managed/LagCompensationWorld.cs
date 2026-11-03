//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// The two words of <c>CBasePlayer::ClientSettingsChanged</c> the lag
/// compensation reads (player.cpp:3474-3532): <c>m_fLerpTime</c> and
/// <c>m_bLagCompensation</c>, from the client's userinfo and the server's
/// bounds on it.
/// </summary>
public static class LagCompensationClientSettings
{
    /// <summary>
    /// <c>m_fLerpTime</c>: with <c>cl_interpolate</c> on, the larger of
    /// <c>cl_interp</c> and <c>cl_interp_ratio</c> over <c>cl_updaterate</c>,
    /// the rate clamped to <c>sv_minupdaterate</c>..<c>sv_maxupdaterate</c> and
    /// the ratio to <c>sv_client_min_interp_ratio</c>..<c>sv_client_max_interp_ratio</c>
    /// unless the minimum is -1; a ratio of 0 is 1. Off, no interpolation.
    /// </summary>
    /// <param name="client">The client's value of a convar it sent, or null.</param>
    /// <param name="server">The server's value of one of its own convars, or null where it has none.</param>
    public static float LerpTime(Func<string, string?> client, Func<string, string?> server)
    {
        int updateRate = Atoi(client("cl_updaterate"));
        if (server("sv_minupdaterate") is { } minRate && server("sv_maxupdaterate") is { } maxRate)
            updateRate = Math.Clamp(updateRate, (int)Atof(minRate), Math.Max((int)Atof(minRate), (int)Atof(maxRate)));

        if (Atoi(client("cl_interpolate")) == 0)
            return 0f;

        float lerpRatio = Atof(client("cl_interp_ratio"));
        if (lerpRatio == 0f)
            lerpRatio = 1f;

        float lerpAmount = Atof(client("cl_interp"));

        if (server("sv_client_min_interp_ratio") is { } minText && server("sv_client_max_interp_ratio") is { } maxText
            && Atof(minText) != -1f)
        {
            float min = Atof(minText), max = Atof(maxText);
            lerpRatio = Math.Clamp(lerpRatio, min, Math.Max(min, max));
        }
        else if (lerpRatio == 0f)
        {
            lerpRatio = 1f;
        }

        // Stock divides by a rate it never checks; a rate of 0 (no bounds and
        // nothing sent) would be an infinite lerp, so it is no ratio term here.
        return updateRate > 0 ? MathF.Max(lerpAmount, lerpRatio / updateRate) : lerpAmount;
    }

    /// <summary>
    /// <c>m_bLagCompensation</c>: <c>cl_predict</c> and <c>cl_lagcompensation</c>
    /// both on (a client that does not predict is never compensated).
    /// </summary>
    public static bool LagCompensation(Func<string, string?> client)
        => Atoi(client("cl_predict")) != 0 && Atoi(client("cl_lagcompensation")) != 0;

    /// <summary><c>Q_atoi</c>: the leading integer, 0 for none.</summary>
    public static int Atoi(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        bool negative = i < text.Length && text[i] == '-';
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
            i++;

        int value = 0;
        for (; i < text.Length && text[i] is >= '0' and <= '9'; i++)
            value = unchecked(value * 10 + (text[i] - '0'));

        return negative ? -value : value;
    }

    /// <summary><c>Q_atof</c>: the leading number, 0 for none.</summary>
    public static float Atof(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        string trimmed = text.Trim();
        int end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] is '.' or '-' or '+' or 'e' or 'E'))
            end++;

        for (; end > 0; end--)
        {
            if (float.TryParse(trimmed.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
        }

        return 0f;
    }
}

/// <summary>
/// The lag compensation's world over the server's seams: the globals, the
/// engine's players and their client settings, the player records' simulation
/// time and speed, each player's engine block (<see cref="IServerEntityPlacement"/>)
/// and skeleton (<see cref="ISkeletonAnimationRewind"/>).
/// </summary>
public sealed class EngineLagCompensationWorld(IEngineEnvironment environment, IEnginePlayers players,
                                               IEngineEntities entities, IEngineWorldQuery world,
                                               IEngineConsole console, IClientInfo clients,
                                               IFwdEngineServer server, IServerEntityPlacement placement,
                                               ISkeletonAnimationRewind skeletons, ServerPlayerMoves moves)
    : ILagCompensationWorld
{
    public float CurTime => environment.Globals is { IsAvailable: true } g ? g.CurTime : 0f;

    public int TickCount => environment.Globals is { IsAvailable: true } g ? g.TickCount : 0;

    public float TickInterval => environment.Globals is { IsAvailable: true } g ? g.IntervalPerTick : 0f;

    public int MaxClients => environment.Globals is { IsAvailable: true } g ? g.MaxClients : 0;

    public EntityHandle PlayerByIndex(int index) => players.GetHandle(index);

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    /// <summary>A client with no network channel: a fake client's.</summary>
    public bool IsBot(EntityHandle player) => server.ClientLatency(entities.GetIndex(player)) is null;

    public bool IsObserver(EntityHandle player) => players.GetObserverMode(player) != ObserverMode.None;

    public bool WantsLagCompensation(EntityHandle player)
    {
        int index = entities.GetIndex(player);
        return LagCompensationClientSettings.LagCompensation(name => clients.ClientConVar(index, name));
    }

    public float LerpTime(EntityHandle player)
    {
        int index = entities.GetIndex(player);
        return LagCompensationClientSettings.LerpTime(name => clients.ClientConVar(index, name),
                                                      console.FindConVarValue);
    }

    public float? Latency(EntityHandle player) => server.ClientLatency(entities.GetIndex(player));

    public float MaxSpeed(EntityHandle player) => moves.For(player).Move.MaxSpeed;

    public float GetSimulationTime(EntityHandle player) => moves.Find(player)?.SimulationTime ?? 0f;

    public void SetSimulationTime(EntityHandle player, float time) => moves.For(player).SimulationTime = time;

    public bool TryGetPlacement(EntityHandle player, out EntityPlacement where) => placement.TryRead(player, out where);

    public void Relink(EntityHandle player, Vector origin, QAngle angles) => placement.Relink(player, origin, angles);

    public void SetSize(EntityHandle player, Vector mins, Vector maxs) => entities.SetBounds(player, mins, maxs);

    public void SetOrigin(EntityHandle player, Vector origin) => entities.SetOrigin(player, origin);

    public EntityHullTrace TraceEntity(EntityHandle player, Vector start, Vector end, Vector mins, Vector maxs)
        => world.EntityTraceHull(player, start, end, mins, maxs, GameMovement.MaskPlayerSolid,
                                 GameMovement.CollisionGroupPlayerMovement);

    public bool CaptureAnimation(EntityHandle player, SkeletonAnimation into) => skeletons.Capture(player, into);

    public void PoseAnimation(EntityHandle player, int sequence, float cycle, ReadOnlySpan<float> poseParameters)
        => skeletons.Pose(player, sequence, cycle, poseParameters);

    public void RestoreAnimation(EntityHandle player, SkeletonAnimation saved) => skeletons.Restore(player, saved);

    public void InvalidateBoneCache(EntityHandle player) => skeletons.InvalidateBoneCache(player);
}
