using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The console door onto <see cref="ModelAnimationCheck"/>: what the view model
/// in hand actually fires, against what managed code declared it would.
///
/// <para>
/// The live half of the check, and the only half that can catch the real
/// mistake. A unit test can only pin the CHECKER — it runs against a table the
/// test wrote, so it proves the comparison works and proves nothing at all
/// about any .mdl on disk. What proves something about the models is running
/// this against them, which is why it is a console command and not only a test.
/// </para>
///
/// <para>
/// It says MISSING out loud, in the tradition <c>cs_cl_viewmodel_report</c>
/// established with STALE. Two distinct failures, deliberately not merged:
/// <c>MISSING SEQUENCE</c> is the animation not resolving at all — the exact
/// shape of the bug that made every HL2 fire animation silent for months — and
/// <c>MISSING EVENT</c> is the sequence being there and not carrying what was
/// expected. They have different causes and different fixes.
/// </para>
/// </summary>
public static class ClientAnimEventCheck
{
    /// <summary>
    /// The check for one weapon class against one loaded view model.
    ///
    /// <para>
    /// Separate from the command so it can be tested, and taking the
    /// expectations as an argument rather than looking them up, so a test can
    /// choose them. The command does the lookup.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Describe(IEngineEntityRender render,
                                                 EntityHandle viewModel,
                                                 string weaponClass,
                                                 IReadOnlyList<AnimationEventExpectation>? expectations)
    {
        if (expectations is null)
        {
            // NOT the same as an empty set, and the difference is the whole
            // point of the null. An empty set says somebody dumped this model
            // and it carries nothing; null says nobody has looked, and a check
            // that answered "all clear" to that would be the assumption this
            // file exists to remove, wearing a tick.
            return [$"{weaponClass}  NOT DECLARED  (nobody has recorded what this model should fire)"];
        }

        return ModelAnimationCheck.Verify(render, viewModel, weaponClass, expectations);
    }
}

/// <summary>
/// The console name for <see cref="ClientAnimEventCheck"/>.
/// </summary>
[ConCommand("cs_animcheck",
            Help = "Check the view model in hand against what managed code declared it fires. " +
                   "Says MISSING SEQUENCE or MISSING EVENT out loud.")]
public sealed class AnimEventCheckCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;
    // The side's weapon table: the check sweeps what this client predicts
    // with, and that is the mods' rows too now (the same table the arsenal's
    // machine was built with).
    private readonly WeaponCatalog _weapons;

    public AnimEventCheckCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render,
                                 WeaponCatalog? weapons = null)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
        _weapons = weapons ?? WeaponCatalog.Stock();
    }

    public void Execute(string args)
    {
        // The ss_weapon entities the client holds, each of which knows its own
        // class. Checked against the VIEW MODEL entity, because that is the
        // entity with the v_ model loaded — a weapon's own handle wears the
        // world model and would answer about a different .mdl entirely.
        var live = _standalones.Live();
        var viewModel = live.FirstOrDefault(e => e.ClassName == EntityNames.SsViewModel);

        if (viewModel is null)
        {
            _log.Warning("[SourceSharp/Client] cs_animcheck: no viewmodel entity to ask\n");
            return;
        }

        if (_render.IsDormant(viewModel.Handle))
        {
            // Refused rather than answered. A dormant entity reports the last
            // values that reached it without complaint, and a model check run
            // against a stale model index is a check about a model nobody is
            // holding.
            _log.Warning("[SourceSharp/Client] cs_animcheck: the viewmodel is DORMANT — " +
                        "every answer would be the last one that arrived, not this one\n");
            return;
        }

        string model = _render.GetModelName(viewModel.Handle);
        string filter = args.Trim();

        _log.Msg($"[SourceSharp/Client] cs_animcheck: viewmodel wears {(model.Length > 0 ? model : "(no model)")}\n");

        int unmet = 0;

        foreach (var weapon in _weapons.All)
        {
            if (filter.Length > 0 && !weapon.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Only the weapon whose model is actually loaded can be checked:
            // the sequences and events come from the entity's studio header,
            // so asking about the shotgun while holding the crowbar would
            // check the crowbar's model against the shotgun's expectations and
            // report a fiction.
            if (!string.Equals(weapon.ViewModel, model, StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = ClientAnimEventCheck.Describe(_render, viewModel.Handle, weapon.Name,
                                                      WeaponAnimEvents.For(weapon.Name));

            foreach (string line in lines)
                _log.Msg($"[SourceSharp/Client] {line}\n");

            if (ModelAnimationCheck.AnyUnmet(lines))
                unmet++;
        }

        if (unmet == 0)
            _log.Msg("[SourceSharp/Client] cs_animcheck: nothing unmet on the model in hand\n");
    }
}
