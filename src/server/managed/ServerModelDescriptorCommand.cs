using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The server half of the model-descriptor report. Run it and
/// <c>cs_cl_modeldesc</c> on the same join and diff the two — see
/// <see cref="ModelDescriptorReport"/> for the two questions that answers.
/// </summary>
[ConCommand("cs_sv_modeldesc",
            Help = "cs_sv_modeldesc <classname>. For each server entity of that class, what " +
                   "its model answers for a handful of sequence names, and what the " +
                   "per-model descriptor crossing has cost this level. Run cs_cl_modeldesc " +
                   "too and diff: before Phase 2d the two sides disagreed on an ACT_ name.")]
public sealed class ServerModelDescriptorCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEntityDirectory _entities;
    private readonly IEngineEntityRender _render;
    private readonly ModelDescriptors _models;

    public ServerModelDescriptorCommand(ILog log, IEntityDirectory entities, IEngineEntityRender render, ModelDescriptors models)
    {

        _log = log;
        _entities = entities;
        _render = render;
        _models = models;
    }

    public void Execute(string args)
    {
        string className = args.Trim();

        // An EXACT classname, for cs_sv_attachments' reason: IEntityDirectory
        // answers OfClass and has no "everything" — the server's live table is
        // the registry's own and is not enumerable from out here.
        if (className.Length == 0)
        {
            _log.Msg("[SourceSharp/Server] cs_sv_modeldesc <classname>\n");
            return;
        }

        int matched = 0;

        foreach (var entity in _entities.OfClass(className))
        {
            matched++;
            _log.Msg($"[SourceSharp/Server] {entity.ClassName}\n");

            foreach (string line in ModelDescriptorReport.Describe(
                         _render, _models, entity.Handle, ModelDescriptorReport.ViewModelProbes))
            {
                _log.Msg($"[SourceSharp/Server]   {line}\n");
            }
        }

        _log.Msg($"[SourceSharp/Server] cs_sv_modeldesc: {matched} entit(ies)\n");
    }
}
