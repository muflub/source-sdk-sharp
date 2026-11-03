//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server's default response system: CDefaultResponseSystem's lifecycle
/// (AI_ResponseSystem.cpp) over <see cref="ResponseRuleSet"/> — the stock
/// script loads once at game init and its groups reset every level, and the
/// stock count line is the load's receipt:
///
/// <code>
/// CResponseSystem:  scripts/talker/response_rules.txt (N rules, M criteria, and K responses)
/// </code>
/// </summary>
public sealed class ResponseRules : IResponseRules, IGameLifecycle
{
    /// <summary>GetScriptFile's answer for the default system.</summary>
    public const string DefaultScript = "scripts/talker/response_rules.txt";

    private readonly ILog _log;

    private readonly IEngineFiles _files;

    private readonly ResponseRuleSet _engine;

    private bool _loaded;

    public ResponseRules(ILog log, IEngineFiles files, IUniformRandom random)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _files = files ?? throw new ArgumentNullException(nameof(files));

        // the parse warnings stock sends through ResponseWarning
        _engine = new ResponseRuleSet(
            random ?? throw new ArgumentNullException(nameof(random)),
            warning => _log.Warning($"[SourceSharp/ResponseRules] {warning}"));
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.ResponseRules;

    public void OnLevelInitPreEntity(string mapName)
    {
        if (!_loaded)
        {
            _loaded = true;

            if (_engine.Load(DefaultScript, _files))
            {
                _log.Msg($"[SourceSharp/ResponseRules] {_engine.LoadedScript} " +
                    $"({_engine.RuleCount} rules, {_engine.CriterionCount} criteria, " +
                    $"and {_engine.ResponseCount} responses)\n");
            }
        }

        // LevelInitPreEntity's Precache-and-reset; the reset is ours to keep
        _engine.Reset();
    }

    /// <inheritdoc cref="IResponseRules"/>
    public bool FindBestResponse(ResponseCriteriaSet criteria, out MatchedResponse response, IResponseFilter? filter = null)
        => _engine.FindBestResponse(criteria, out response, filter);
}
