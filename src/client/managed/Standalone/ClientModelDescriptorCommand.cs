using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of the model-descriptor report. Run it and
/// <c>cs_sv_modeldesc</c> on the same join and diff the two — see
/// <see cref="ModelDescriptorReport"/> for the two questions that answers.
/// </summary>
[ConCommand("cs_cl_modeldesc",
            Help = "cs_cl_modeldesc [classname substring]. For each client entity, what " +
                   "its model answers for a handful of sequence names, and what the " +
                   "per-model descriptor crossing has cost this level. Run cs_sv_modeldesc " +
                   "too and diff: before Phase 2d the two sides disagreed on an ACT_ name.")]
public sealed class ClientModelDescriptorCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;
    private readonly ModelDescriptors _models;

    public ClientModelDescriptorCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render, ModelDescriptors models)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
        _models = models;
    }

    public void Execute(string args)
    {
        string filter = args.Trim();
        int matched = 0;

        foreach (var entity in _standalones.Live())
        {
            if (filter.Length > 0 &&
                entity.ClassName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            matched++;
            _log.Msg($"[SourceSharp/Client] {entity.ClassName}\n");

            foreach (string line in ModelDescriptorReport.Describe(
                         _render, _models, entity.Handle, ModelDescriptorReport.ViewModelProbes))
            {
                _log.Msg($"[SourceSharp/Client]   {line}\n");
            }
        }

        _log.Msg($"[SourceSharp/Client] cs_cl_modeldesc: {matched} entit(ies)\n");
    }
}
