//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// Whether a level is predicted: stock CHLClient::LevelInitPreEntity's
/// cl_predict arm (cdll_client_int.cpp), "don't do prediction if single
/// player!". Moved out of shell_clientdll.cpp's LevelInitPreEntity (lane
/// M1-B), which now only tells managed the level is starting.
///
/// <para>
/// A multiplayer game turns prediction on and a single-player one turns it
/// off, each only when the value read says otherwise, and always as a console
/// command rather than a direct set ("don't set direct because of
/// FCVAR_USERINFO": the server must hear it).
/// </para>
///
/// <para>
/// The value stock read is <c>cl_predict-&gt;GetInt()</c> on the
/// ConVar_ServerBounded object, which is the BOUNDED value: sv_client_predict
/// when the server sets it, else the client's own, truncated to an int.
/// <see cref="BoundedConVarRules.Predict"/> is that bound, and this reads it
/// the same way.
/// </para>
/// </summary>
public sealed class LevelPrediction(Func<int> maxClients, Func<string, string?> readConVar, IClientPredictCommand command)
{
    /// <summary>
    /// The switch a level needs: true to turn prediction on, false to turn it
    /// off, null when it already reads as wanted.
    /// </summary>
    /// <param name="maxClients">gpGlobals-&gt;maxClients: more than one is multiplayer.</param>
    /// <param name="boundedPredict">cl_predict's bounded value, as GetFloat answers it.</param>
    public static bool? Switch(int maxClients, float boundedPredict)
    {
        bool predicting = (int)boundedPredict != 0;
        if (maxClients > 1)
            return predicting ? null : true;
        return predicting ? false : null;
    }

    /// <summary>
    /// The level is starting (IBaseClientDLL::LevelInitPreEntity): queue the
    /// switch, if one is needed. With no cl_predict to read there is nothing to
    /// switch.
    /// </summary>
    /// <returns>The switch queued, or null for none.</returns>
    public bool? LevelInitPreEntity()
    {
        if (Read("cl_predict") is not float client)
            return null;
        float predict = BoundedConVarRules.Predict(client, Read("sv_client_predict") is float server ? (int)server : null);
        bool? wanted = Switch(maxClients(), predict);
        if (wanted is bool on)
            command.SetPredict(on);
        return wanted;
    }

    private float? Read(string name) => readConVar(name) is string v ? VariantParse.Atof(v) : null;
}
