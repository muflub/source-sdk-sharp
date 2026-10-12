using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugEntityCatalog"/> over the composition's entity registry
/// (group G3). Reads through the given functions on every call, so a
/// registry the composition replaces is the one described.
/// </summary>
public sealed class DebugEntityCatalog(
    Func<IReadOnlyList<ScriptedClassInfo>> classes,
    Func<IReadOnlyList<ScriptedInstanceInfo>> instances) : IDebugEntityCatalog
{
    private readonly Func<IReadOnlyList<ScriptedClassInfo>> _classes = classes ?? throw new ArgumentNullException(nameof(classes));
    private readonly Func<IReadOnlyList<ScriptedInstanceInfo>> _instances = instances ?? throw new ArgumentNullException(nameof(instances));

    public IReadOnlyList<DebugEntityClass> Classes()
        => _classes().Select(c => new DebugEntityClass(
                c.ClassName, c.Kind, c.TypeName, c.Inputs, c.Outputs,
                c.Vars.Select(v => new DebugField(v.Name, v.TypeName)).ToArray(), c.LiveCount))
            .ToArray();

    public IReadOnlyList<DebugEntityInstance> Instances()
        => _instances().Select(i => new DebugEntityInstance(
                i.ClassName, i.Handle, i.Index, i.IsValid, new DebugVec3(i.Origin.X, i.Origin.Y, i.Origin.Z),
                i.Health, i.SpawnFlags,
                i.Vars.Select(v => new DebugVar(v.Name, v.TypeName, v.Value)).ToArray(),
                i.Outputs.Select(o => new DebugOutputStat(o.Name, o.FireCount, o.LastValue)).ToArray()))
            .ToArray();
}
