using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// The post crossing's managed side (C7 post-processing): asks
/// <see cref="PostProcessPlanner"/> for the program at the plan's
/// EnginePostProcessing or MotionBlur op, carries out the program's effects
/// that are not render calls (its convar writes, its overlay text) and hands
/// back its ops; and answers the post proxies' values.
/// </summary>
public sealed class PostProcessHost : MaterialProxies.IPostProxyValues
{
    private readonly IFxRender? _fx;
    private readonly IEngineConsole? _console;
    private readonly ILog? _log;
    private readonly Func<IFwdEngineClient?> _engine;
    private readonly Dictionary<string, int> _materials = new(StringComparer.Ordinal);
    private readonly RenderPlanBuilder _builder = new();
    private readonly PostProgramEffects _effects = new();
    private readonly PostOpsWriter _writer;

    public PostProcessHost(IHooks<IPostProcessContributor> contributors, IFxRender fx,
                           IEngineConsole console, ILog log, Func<IFwdEngineClient?> engine)
        : this(engine, contributors)
    {
        _fx = fx;
        _console = console;
        _log = log;
    }

    internal PostProcessHost(Func<IFwdEngineClient?> engine, IHooks<IPostProcessContributor>? contributors = null)
    {
        _engine = engine;
        Planner = new PostProcessPlanner(() => _engine()?.GameDirectory() ?? string.Empty, contributors);
        _writer = new PostOpsWriter(_builder, Material);
    }

    /// <summary>The decisions, and what the proxies read.</summary>
    public PostProcessPlanner Planner { get; }

    public MotionBlurValues MotionBlur => Planner.MotionBlur.Values;

    public EnginePostValues EnginePost => Planner.EnginePost;

    /// <summary>Programs built, by kind.</summary>
    public long EnginePostPrograms { get; private set; }

    public long MotionBlurPrograms { get; private set; }

    private int Material(string name)
    {
        if (_fx is null || name.Length == 0)
            return 0;
        if (!_materials.TryGetValue(name, out int id))
        {
            id = _fx.MaterialFind(name);
            _materials[name] = id;
        }
        return id;
    }

    /// <summary>The program for one post op: its ops written into <paramref name="ops"/>, their count, or -1.</summary>
    public int Build(in PostProcessInputs input, Span<RenderOp> ops)
    {
        _builder.Clear();
        _effects.Clear();
        Planner.Plan(input, _writer, _effects);

        foreach ((string name, string value) in _effects.CvarWrites)
            _console?.SetConVarValueByName(name, value);
        if (_effects.Prints.Count > 0 && _engine() is { } engine)
        {
            foreach ((int line, string text) in _effects.Prints)
                engine.ConsolePrintAt(line, text);
        }
        foreach (string message in _effects.DevMessages)
            _log?.Msg(message);

        ReadOnlySpan<RenderOp> program = _builder.Span;
        if (program.Length > ops.Length)
            return -1;
        program.CopyTo(ops);
        if (input.Kind == (int)PostProgramKind.MotionBlur)
            MotionBlurPrograms++;
        else
            EnginePostPrograms++;
        return program.Length;
    }
}
