//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Voice;

/// <summary>
/// The response rules' level precache, stock's
/// <c>CResponseSystem::Precache</c> (game/server/AI_ResponseSystem.cpp:1831-1894)
/// at the stage its caller ran in — LevelInitPreEntity's
/// <c>AI_ResponseSystem::LevelInitPreEntity</c> (:2931-2943) under
/// <c>ShouldPrecache</c>:
///
/// <list type="bullet">
/// <item>a <c>RESPONSE_SCENE</c> walks PrecacheInstancedScene
/// (server/sceneentity.cpp:4780-4816): the scene's every cached sound through
/// <c>CBaseEntity::PrecacheScriptSound</c>, and the <c>$gender</c> fixup
/// (:1850-1884) that splits one response into its male and female scenes
/// before the walk;</item>
/// <item>a <c>RESPONSE_SPEAK</c> is the sound script itself, precached whole
/// (:1887-1891).</item>
/// </list>
///
/// <para>
/// Without it the scene's waves and the speak entries reach
/// <c>SV_StartSound</c> unprecached and the line is refused before it is ever
/// broadcast — the silence the lane set out to fix.
/// </para>
///
/// <para>
/// The one divergence: stock's PrecacheInstancedScene also adds the scene's
/// name to the client choreography string table (:4815); no reader of that
/// table is ported, so nothing is written to it.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFResponsePrecache(IResponseRules rules, TFSceneImage scenes, IEnginePrecache precache)
    : IGameLifecycle
{
    /// <summary>
    /// A stage of the TF player's own, after the core response rules' load
    /// (2400) and the class models' precache: the same window, one entry later
    /// in its band.
    /// </summary>
    public int LifecycleOrder => Player.TFPlayerOrders.ResponsePrecache;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        foreach (var (kind, value) in rules.Responses)
        {
            switch (kind)
            {
                case ResponseKind.Scene:
                    PrecacheScene(value);
                    break;

                case ResponseKind.Speak:
                    precache.PrecacheScriptSound(value);
                    break;
            }
        }
    }

    /// <summary>PrecacheInstancedScene over the response's name, $gender split as stock's (:1850-1884).</summary>
    private void PrecacheScene(string scene)
    {
        int genderAt = scene.IndexOf("$gender", StringComparison.Ordinal);
        if (genderAt < 0)
        {
            PrecacheInstancedScene(scene);
            return;
        }

        PrecacheInstancedScene(string.Concat(scene.AsSpan(0, genderAt), "male", scene.AsSpan(genderAt + 7)));
        PrecacheInstancedScene(string.Concat(scene.AsSpan(0, genderAt), "female", scene.AsSpan(genderAt + 7)));
    }

    /// <summary>The scene's every cached sound, script sound and all (:4806-4813).</summary>
    private void PrecacheInstancedScene(string sceneFile)
    {
        foreach (string sound in scenes.SceneSounds(sceneFile))
            precache.PrecacheScriptSound(sound);
    }
}
