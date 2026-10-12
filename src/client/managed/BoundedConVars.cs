//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The rules of game/client/cdll_bounded_cvars.cpp (and the engine's
/// cl_updaterate clamp they read), as pure functions (plan_client_novalve C8).
/// A null input is a convar the client cannot find, which stock's FindVar
/// answers NULL for and falls back on the client's own value.
/// </summary>
public static class BoundedConVarRules
{
    /// <summary>mathlib's clamp: a maximum below the minimum answers the maximum.</summary>
    public static float Clamp(float value, float min, float max)
        => max < min ? max : value < min ? min : value > max ? max : value;

    /// <summary>cl_predict: sv_client_predict when it is not -1 (GetInt, a truncated float), else the client's.</summary>
    public static float Predict(float clientValue, int? serverPredict)
        => serverPredict is int s && s != -1 ? s : clientValue;

    /// <summary>cl_interp_ratio: clamped to the server's bounds unless the minimum is -1.</summary>
    public static float InterpRatio(float clientValue, float? serverMin, float? serverMax)
        => serverMin is float min && serverMax is float max && min != -1f ? Clamp(clientValue, min, max) : clientValue;

    /// <summary>The engine's cl_updaterate: clamped to sv_minupdaterate and sv_maxupdaterate.</summary>
    public static float UpdateRate(float clientValue, float? serverMin, float? serverMax)
        => serverMin is float min && serverMax is float max ? Clamp(clientValue, min, max) : clientValue;

    /// <summary>cl_interp: at least the server's minimum ratio over the bounded update rate, unless that minimum is -1.</summary>
    public static float Interp(float clientValue, float? serverMinRatio, float? boundedUpdateRate)
        => boundedUpdateRate is float rate && serverMinRatio is float min && min != -1f
            ? MathF.Max(clientValue, min / rate)
            : clientValue;

    /// <summary>GetClientInterpAmount: the larger of the bounded interp and ratio over rate; 0.1 with no update rate.</summary>
    public static float InterpAmount(float boundedInterp, float boundedRatio, float? boundedUpdateRate)
        => boundedUpdateRate is float rate ? MathF.Max(boundedInterp, boundedRatio / rate) : 0.1f;
}

/// <summary>
/// The bounded convars' decision, pushed (plan_client_novalve C8, the
/// integrator's ruling): the native ConVar_ServerBounded objects stay, because
/// the engine reads them through their vtable (CClientState::GetClientInterpAmount
/// calls cl_interp_ratio's GetFloat), but each answers a value this decided and
/// stored, so a read costs no crossing. Every input is watched; a change of
/// any recomputes all four values and pushes them.
/// </summary>
public sealed class BoundedConVars(IClientConVarSlots slots, Func<string, string?> readConVar)
{
    /// <summary>The stored value GetClientInterpAmount answers.</summary>
    public const string InterpAmountName = "GetClientInterpAmount";

    /// <summary>Every convar a value is decided from: the client's three, the update rate and the server's bounds.</summary>
    public const string InputNames =
        "cl_predict cl_interp cl_interp_ratio cl_updaterate sv_client_predict " +
        "sv_client_min_interp_ratio sv_client_max_interp_ratio sv_minupdaterate sv_maxupdaterate";

    private readonly HashSet<int> _watched = [];

    /// <summary>The four values last pushed (cl_predict, cl_interp_ratio, cl_interp, the amount), for cs_cl_bounded.</summary>
    public (float Predict, float Ratio, float Interp, float Amount) Pushed { get; private set; }

    /// <summary>How many pushes since this load (the start and every watched change).</summary>
    public int Pushes { get; private set; }

    /// <summary>How many inputs a watch answered.</summary>
    public int Watched => _watched.Count;

    /// <summary>Watch every input and push once (the load, and every reload).</summary>
    public void Start()
    {
        _watched.Clear();
        foreach (string name in InputNames.Split(' '))
        {
            int id = slots.Watch(name);
            if (id >= 0)
                _watched.Add(id);
        }
        Push();
    }

    /// <summary>A forwarded change: true when it was one of the inputs (and the values were pushed).</summary>
    public bool OnChanged(int id)
    {
        if (!_watched.Contains(id))
            return false;
        Push();
        return true;
    }

    private float? Read(string name) => readConVar(name) is string v ? VariantParse.Atof(v) : null;

    private void Push()
    {
        float predict = BoundedConVarRules.Predict(Read("cl_predict") ?? 1f,
            Read("sv_client_predict") is float sp ? (int)sp : null);
        float ratio = BoundedConVarRules.InterpRatio(Read("cl_interp_ratio") ?? 2f,
            Read("sv_client_min_interp_ratio"), Read("sv_client_max_interp_ratio"));
        float? rate = Read("cl_updaterate") is float ur
            ? BoundedConVarRules.UpdateRate(ur, Read("sv_minupdaterate"), Read("sv_maxupdaterate"))
            : null;
        float interp = BoundedConVarRules.Interp(Read("cl_interp") ?? 0.1f, Read("sv_client_min_interp_ratio"), rate);

        slots.SetValue("cl_predict", predict);
        slots.SetValue("cl_interp_ratio", ratio);
        slots.SetValue("cl_interp", interp);
        float amount = BoundedConVarRules.InterpAmount(interp, ratio, rate);
        slots.SetValue(InterpAmountName, amount);
        Pushed = (predict, ratio, interp, amount);
        Pushes++;
    }
}

/// <summary>The bounded convars' pushed values and their inputs (the live check of C8's push).</summary>
[ConCommand("cs_cl_bounded", Help = "Print the server-bounded convars' values C# decided and pushed, and how many pushes")]
public sealed class BoundedConVarsCommand(ILog log, BoundedConVars bounded) : IConCommand
{
    public void Execute(string args)
    {
        var p = bounded.Pushed;
        log.Msg(FormattableString.Invariant(
            $"[SourceSharp/Client] bounded: cl_predict {p.Predict:R} cl_interp_ratio {p.Ratio:R} cl_interp {p.Interp:R} amount {p.Amount:R} (watched {bounded.Watched}, pushes {bounded.Pushes})\n"));
    }
}
