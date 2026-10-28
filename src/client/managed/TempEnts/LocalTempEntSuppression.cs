//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// Stock c_te.cpp's <c>C_TempEntsSystem::SuppressTE</c>: whether a temp entity
/// the client makes for itself (<c>te-&gt;DispatchEffect</c>) is dropped before
/// it plays. Native hands over the two facts it holds, the prediction system's
/// <c>CanPredict()</c> and the recipient filter's count
/// (shell_te_forwards_client.cpp), and carries out the answer.
/// </summary>
public static class LocalTempEntSuppression
{
    /// <summary>
    /// True drops the effect: when prediction says the effect must not play
    /// (<c>!CanPredict()</c>), and when the filter holds nobody to play it to.
    /// </summary>
    public static bool Suppress(bool canPredict, int recipientCount)
    {
        if (!canPredict)
            return true;

        // There's at least one recipient (stock's comment) or it is suppressed.
        return recipientCount <= 0;
    }
}
