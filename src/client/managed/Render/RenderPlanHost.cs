using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render;

/// <summary>
/// The render plan's managed half (plan_client_novalve.md §3.3, C7):
/// View_Render asks it once a frame for the frame's view decision and whether
/// native records it (shell_render_trace_client.cpp).
///
/// <para>
/// RECORDING. A caller asks for the next frame with <see cref="RequestTrace"/>
/// and gets the frame's text back through its callback once native hands it
/// over; the requests are served one frame each, in order. A request can come
/// from the console or the dev API's channel thread, so the queue is locked;
/// the answer is always given on the frame that was recorded.
/// </para>
/// </summary>
public sealed class RenderPlanHost : IClientRenderPlan
{
    private readonly object _lock = new();
    private readonly Queue<(int Tag, Action<string> Done)> _requests = new();
    private readonly Dictionary<int, Action<string>> _inFlight = [];
    private int _nextTag = 1;

    /// <summary>The game's: the debug overlays' materials are found through <paramref name="fx"/> by the names <paramref name="console"/> holds.</summary>
    public RenderPlanHost(IFxRender fx, IEngineConsole console, PostProcessHost post, ViewEffectsHost viewEffects,
                          IUniformRandom random, DemoViewCamera demoCamera, ScreenSpaceEffectManager screenEffects)
    {
        _screenEffects = screenEffects;
        _demoCamera = demoCamera;
        _fx = fx;
        _console = console;
        _post = post;
        _viewEffects = viewEffects;
        _randomByte = () => random.RandomInt(0, 255);
    }

    // gl_clear_randomcolor's colour: stock's rand()%256 is the engine's
    // uniform stream here (a debug clear; the source of its noise is not kept).
    private readonly Func<int> _randomByte = () => 0;
    private int _overviewCrc;

    /// <summary>Frames whose view.cpp decisions (SetUpViews' planes, Render's eye) this host made.</summary>
    public long ViewDecisions { get; private set; }


    /// <summary>CViewRender::SetUpViews' planes and FOV (ViewFrame.SetUpView).</summary>
    public void ViewSetUp(in ViewSetUpIn input, out ViewSetUpOut output) => output = ViewFrame.SetUpView(input);

    /// <summary>The save-game screenshot's view (ViewFrame.Screenshot).</summary>
    public ViewSetup ViewScreenshot(in ViewSetup eye, int width, int height) => ViewFrame.Screenshot(eye, width, height);

    // CViewRender::Render's decisions for this frame, and SetUpOverView's
    // movement message.
    private void DecideView(in RenderFrameIn frame, ref RenderFrameOut answer)
    {
        ViewFrame.Render(frame, ref answer, _randomByte);
        lock (_lock)
            ViewDecisions++;
        if (answer.Overview == 0)
            return;
        int crc = ViewFrame.OverviewCrc(answer.Eye);
        if (crc == _overviewCrc)
            return;
        _overviewCrc = crc;
        Vector o = answer.Eye.Origin;
        _console?.Msg(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                    $"Overview: scale {frame.LevelOverview:F2}, pos_x {o.X:F0}, pos_y {o.Y:F0}\n"));
    }

    private readonly ViewEffectsHost? _viewEffects;

    public void ViewEffectsFade(int duration, int holdTime, int flags, byte r, byte g, byte b, byte a, float curTime)
        => _viewEffects?.Fade(duration, holdTime, (FadeFlags)flags, r, g, b, a, curTime);

    public void ViewEffectsCalcShake(float curTime, float frameTime, Span<float> output)
    {
        if (_viewEffects is null)
            return;
        ShakeFrame f = _viewEffects.CalcShake(curTime, frameTime);
        output[0] = f.Offset.X;
        output[1] = f.Offset.Y;
        output[2] = f.Offset.Z;
        output[3] = f.Angle;
    }

    public void ViewEffectsFadeParams(float curTime, Span<int> output)
    {
        if (_viewEffects is null)
            return;
        FadeParams p = _viewEffects.Effects.GetFadeParams(curTime);
        output[0] = p.R;
        output[1] = p.G;
        output[2] = p.B;
        output[3] = p.A;
        output[4] = p.Modulate ? 1 : 0;
    }

    public void ViewEffectsReset(int which)
    {
        ViewEffects? fx = _viewEffects?.Effects;
        switch (which)
        {
            case 0: fx?.LevelInit(); break;
            case 1: fx?.ClearAllFades(); break;
            case 2: fx?.ClearPermanentFades(); break;
        }
    }

    private readonly PostProcessHost? _post;

    private readonly IFxRender? _fx;
    private readonly IEngineConsole? _console;
    private readonly Dictionary<string, int> _materials = new(StringComparer.Ordinal);

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

    /// <summary>
    /// The debug overlays' materials for this frame (viewdebug.cpp's names and
    /// cl_drawmaterial's), each found once and kept: a frame that asks the same
    /// names allocates nothing.
    /// </summary>
    internal DebugOverlayMaterials DebugMaterials()
    {
        if (_fx is null || _console is null)
            return DebugOverlayMaterials.None;
        string drawMaterial = _console.FindConVarValue("cl_drawmaterial") ?? "";
        string drawTexture = _console.FindConVarValue("mat_drawTexture") ?? "";
        return new DebugOverlayMaterials(
            Material("debug/yuv"), Material("debug/hsv"), Material("debug/debugreflect"), Material("debug/debugrefract"),
            Material("debug/debugcamerarendertarget"), Material("debug/debugfbtexture0"), Material("debug/debugfbtexture1"),
            Material(drawMaterial), drawTexture.Length > 0);
    }

    internal RenderPlanHost()
    {
    }

    /// <summary>A test's: no engine, the screen-space effects <paramref name="screenEffects"/>.</summary>
    internal RenderPlanHost(ScreenSpaceEffectManager screenEffects)
    {
        _screenEffects = screenEffects;
    }

    /// <param name="post">The post program the plan's post ops ask for.</param>
    internal RenderPlanHost(PostProcessHost post)
    {
        _post = post;
    }

    /// <param name="post">The post program the plan's post ops ask for.</param>
    /// <param name="viewEffects">The view effects native's vieweffects forwards to.</param>
    internal RenderPlanHost(PostProcessHost post, ViewEffectsHost viewEffects)
        : this(post)
    {
        _viewEffects = viewEffects;
    }

    /// <summary>How many RenderViews were planned, and how many the plan handed back to stock.</summary>
    public long Planned { get; private set; }

    public long DeclinedToStock { get; private set; }

    // The plan's builder and the last plan's copy, kept across frames so a warm
    // plan allocates nothing (C7: a fresh list and array were 89,720 bytes a
    // frame at dm_lockdown's poses).
    private readonly RenderPlanBuilder _builder = new();

    // This frame's second-camera views (C7 item 6), queued at
    // FRAME_RENDER_START and drawn by the next plan; kept, so no frame
    // allocates for them.
    private readonly List<MonitorView> _monitors = [];

    /// <summary>Monitor passes the plans drew.</summary>
    public long MonitorPasses { get; private set; }

    /// <summary>
    /// Queues a second camera's view for this frame's plan. False for a target
    /// the plan has no render target for: the caller hands the view to
    /// native's own queue then.
    /// </summary>
    public bool QueueMonitor(int targetNumber, ViewTargetSetup setup)
    {
        if (targetNumber != 0)
            return false;
        _monitors.RemoveAll(m => m.TargetNumber == targetNumber);
        _monitors.Add(MonitorView.From(setup, RenderTarget.ViewTarget0, targetNumber));
        return true;
    }

    private readonly DemoViewCamera? _demoCamera;

    // ScreenSpaceEffects.cpp's manager: its Init and Shutdown at the view's
    // level start and end, its RenderEffects in the frame's plan.
    private readonly ScreenSpaceEffectManager? _screenEffects;

    /// <summary>CViewRender::LevelInit's InitScreenSpaceEffects (0) and LevelShutdown's ShutdownScreenSpaceEffects (1).</summary>
    public void ScreenSpaceEffectsStage(int stage)
    {
        if (stage == 0)
            _screenEffects?.Init();
        else if (stage == 1)
            _screenEffects?.Shutdown();
    }

    public int ViewDemoCamera(int stage, ReadOnlySpan<float> input, Span<float> output)
    {
        if (_demoCamera is null)
            return 0;
        if (stage == 0)
        {
            bool on = _demoCamera.Begin(new Vector(input[0], input[1], input[2]), new Vector(input[3], input[4], input[5]), out Vector engine);
            output[0] = engine.X;
            output[1] = engine.Y;
            output[2] = engine.Z;
            return on ? 1 : 0;
        }
        (Vector origin, Vector angles) = _demoCamera.Move(new Vector(input[0], input[1], input[2]), input[3]);
        output[0] = origin.X;
        output[1] = origin.Y;
        output[2] = origin.Z;
        output[3] = angles.X;
        output[4] = angles.Y;
        output[5] = angles.Z;
        return 1;
    }

    // CViewRender's freeze frame, CShellViewRender::FreezeFrame's (C7 step 3).
    private readonly FreezeFrameState _freeze = new();

    /// <summary>CViewRender::FreezeFrame (test_freezeframe until the eviction makes it a managed command).</summary>
    public void ViewFreezeFrame(float freezeTime, float curTime)
    {
        lock (_lock)
            _freeze.Freeze(freezeTime, curTime);
    }

    public int BuildPlan(in RenderInputs native, Span<RenderOp> ops)
    {
        // The freeze frame's state is this host's: the plan reads it here.
        RenderInputs input = native;
        lock (_lock)
        {
            input.FreezeActive = _freeze.Showing(native.CurTime) ? 1 : 0;
            input.FreezeTake = _freeze.CaptureDue ? 1 : 0;
            input.FreezeFlash = _freeze.Flash;
        }
        ReadOnlySpan<MonitorView> monitors = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_monitors);
        bool planned = RenderPlanner.Plan(input, _builder, monitors, DebugMaterials(), _screenEffects);
        if (planned && input.FreezeTake != 0)
        {
            lock (_lock)
                _freeze.Captured();
        }
        MonitorPasses += planned ? monitors.Length : 0;
        _monitors.Clear();
        if (!planned || _builder.Span.Length > ops.Length)
        {
            lock (_lock)
                DeclinedToStock++;
            return -1;
        }
        bool mutate;
        lock (_lock)
            mutate = _compareStage == CompareStage.Plan && _compareMutated && _compareMutateArmed;

        // The compare's control drops DrawLights, whose one recorded call is in
        // every frame stock draws.
        int n = 0;
        foreach (ref readonly RenderOp op in _builder.Span)
        {
            if (mutate && op.Kind == (int)RenderOpKind.DrawLights)
                continue;
            ops[n++] = op;
        }
        lock (_lock)
        {
            Planned++;
            _lastInputs = input;
            if (_lastPlan.Length < n)
                _lastPlan = new RenderOp[Math.Max(n, 2 * _lastPlan.Length)];
            ops[..n].CopyTo(_lastPlan);
            _lastPlanLength = n;
        }
        return n;
    }

    private RenderInputs _lastInputs;
    private readonly RenderPlanBuilder _viewProgram = new();
    private readonly RenderPlanBuilder _shadowDepth = new();

    /// <summary>Shadow-depth views planned (CViewRender::UpdateShadowDepthTexture).</summary>
    public long ShadowDepthViews { get; private set; }

    /// <summary>
    /// A shadow-depth view's ops, asked inside the last plan's
    /// ShadowComputeDepthTextures op with that plan's inputs. The ops are kept
    /// after the frame's plan, so the view's ViewScene finds its own context:
    /// <paramref name="indexBase"/> is where they start. -1 when there is no
    /// plan this frame or the ops do not fit.
    /// </summary>
    public int BuildShadowDepthPlan(in ViewSetup shadow, Span<RenderOp> ops, out int indexBase)
    {
        indexBase = 0;
        lock (_lock)
        {
            if (_lastPlanLength == 0)
                return -1;
            RenderPlanner.PlanShadowDepthView(_lastInputs, shadow, _shadowDepth);
            ReadOnlySpan<RenderOp> plan = _shadowDepth.Span;
            if (plan.Length > ops.Length)
                return -1;
            int n = _lastPlanLength + plan.Length;
            if (_lastPlan.Length < n)
                Array.Resize(ref _lastPlan, Math.Max(n, 2 * _lastPlan.Length));
            plan.CopyTo(_lastPlan.AsSpan(_lastPlanLength));
            indexBase = _lastPlanLength;
            _lastPlanLength = n;
            plan.CopyTo(ops);
            ShadowDepthViews++;
            return plan.Length;
        }
    }

    /// <summary>How many view programs were made, and how many views were handed back to stock's primitives.</summary>
    public long ViewPrograms { get; private set; }

    public long ViewProgramsDeclined { get; private set; }

    public int BuildPostProgram(in PostProcessInputs input, Span<RenderOp> ops) => _post?.Build(input, ops) ?? -1;

    public int BuildViewProgram(ReadOnlySpan<byte> sceneBytes, Span<RenderOp> ops)
    {
        ViewSceneData? scene = ViewSceneData.FromBytes(sceneBytes);
        ViewDrawProgram.Context? context = null;
        if (scene != null)
        {
            lock (_lock)
                context = ViewDrawProgram.ContextOf(_lastPlan.AsSpan(0, _lastPlanLength), scene.Header.OpIndex, _lastInputs);
        }
        if (scene == null || context == null)
        {
            lock (_lock)
                ViewProgramsDeclined++;
            return -1;
        }

        _viewProgram.Clear();
        ViewDrawProgram.Build(scene, context, _viewProgram);
        ReadOnlySpan<RenderOp> program = _viewProgram.Span;
        if (program.Length > ops.Length)
        {
            lock (_lock)
                ViewProgramsDeclined++;
            return -1;
        }
        program.CopyTo(ops);
        lock (_lock)
            ViewPrograms++;
        return program.Length;
    }

    //-------------------------------------------------------------------------
    // The compare: a frozen stock frame (render-stock's recordings) against
    // the next plan frame, recorded (C7 step 3; the live stock path is gone).
    //-------------------------------------------------------------------------
    private enum CompareStage { None, Plan, Done }

    private CompareStage _compareStage = CompareStage.None;
    private int _compareSequence;
    private bool _compareMutated;
    private bool _compareMutateArmed;
    private RenderTrace? _compareStock;
    private RenderTrace? _comparePlan;
    private RenderOp[] _lastPlan = [];
    private int _lastPlanLength;
    private bool _planFrameAsked;

    private void CompareRecorded(string text)
    {
        lock (_lock)
        {
            _comparePlan = RenderTrace.Parse(text);
            _compareStage = CompareStage.Done;
            _compareMutateArmed = false;
        }
    }

    /// <summary>
    /// Arms a compare of the next plan frame against <paramref name="frozen"/>,
    /// a frozen stock frame (C7 step 3); false when one is already running.
    /// </summary>
    public bool RequestFrozenCompare(RenderTrace frozen, bool mutate, out int sequence)
    {
        lock (_lock)
        {
            if (_compareStage == CompareStage.Plan)
            {
                sequence = _compareSequence;
                return false;
            }
            _compareSequence++;
            _compareStage = CompareStage.Plan;
            _planFrameAsked = false;
            _compareMutated = mutate;
            _compareMutateArmed = false;
            _compareStock = frozen;
            _comparePlan = null;
            sequence = _compareSequence;
            return true;
        }
    }

    /// <summary>The last compare, as the route reports it.</summary>
    public RenderCompareReading CompareReading()
    {
        lock (_lock)
        {
            string state = _compareStage switch
            {
                CompareStage.None => "none",
                CompareStage.Plan => "plan",
                _ => "done",
            };
            RenderTraceDifference? diff = _compareStock != null && _comparePlan != null
                ? RenderTrace.CompareAcrossSessions(_compareStock, _comparePlan) : null;
            bool done = _compareStage == CompareStage.Done;
            return new RenderCompareReading(_compareSequence, state, _compareMutated, done && diff == null,
                                            diff?.Index ?? -1, diff?.Left, diff?.Right,
                                            _compareStock?.Calls.Count ?? 0, _comparePlan?.Calls.Count ?? 0,
                                            _compareStock?.FrameCount ?? -1, _comparePlan?.FrameCount ?? -1,
                                            _comparePlan?.PlanNote,
                                            _compareStock != null && _comparePlan != null
                                                ? RenderTrace.SceneDifferenceAcrossSessions(_compareStock, _comparePlan) : null,
                                            _compareStock is { Calls.Count: > 0 } st && st.Calls[0].StartsWith("render.SetMainView\t", StringComparison.Ordinal) ? st.Calls[0] : null,
                                            _compareStock?.FrameStartCopyName, _comparePlan?.FrameStartCopyName);
        }
    }

    /// <summary>The last plan's ops, by kind, for the route.</summary>
    public IReadOnlyList<string> LastPlanKinds()
    {
        lock (_lock)
            return [.. _lastPlan.Take(_lastPlanLength).Select(op => ((RenderOpKind)op.Kind).ToString())];
    }

    /// <summary>Records the next frame View_Render draws; <paramref name="done"/> gets its text.</summary>
    public int RequestTrace(Action<string> done)
    {
        lock (_lock)
        {
            int tag = _nextTag++;
            _requests.Enqueue((tag, done));
            return tag;
        }
    }

    /// <summary>Requests not yet handed to a frame, and frames recorded but not yet returned.</summary>
    public int PendingTraces
    {
        get
        {
            lock (_lock)
                return _requests.Count + _inFlight.Count;
        }
    }

    /// <summary>
    /// How long CViewRender::Render took over the frames since the last
    /// <see cref="ResetTimes"/> (native measures, Plat_FloatTime): the render
    /// step's "ms per stage".
    /// </summary>
    public (long Frames, double TotalMs, double MaxMs) PlanTimes { get; private set; }

    public void ResetTimes()
    {
        lock (_lock)
            PlanTimes = default;
    }

    public void Frame(in RenderFrameIn frame, ref RenderFrameOut answer)
    {
        DecideView(frame, ref answer);
        if (frame.LastRenderMs > 0)
        {
            lock (_lock)
            {
                var t = PlanTimes;
                PlanTimes = (t.Frames + 1, t.TotalMs + frame.LastRenderMs, Math.Max(t.MaxMs, frame.LastRenderMs));
            }
        }
        // Every frame is the plan's: the stock path left before the eviction
        // (C7 step 3), and Path stays in the frame's answer only as its ABI.
        answer.Path = 1;
        lock (_lock)
        {
            if (_compareStage == CompareStage.Plan && !_compareMutateArmed && _comparePlan == null && !_planFrameAsked)
            {
                int planTag = _nextTag++;
                _inFlight[planTag] = CompareRecorded;
                answer.Trace = 1;
                answer.TraceTag = planTag;
                _compareMutateArmed = true;
                _planFrameAsked = true;
                return;
            }
            if (_requests.Count == 0)
                return;
            (int tag, Action<string> done) = _requests.Dequeue();
            _inFlight[tag] = done;
            answer.Trace = 1;
            answer.TraceTag = tag;
        }
    }

    public void TraceDone(int tag, string text)
    {
        Action<string>? done;
        lock (_lock)
        {
            if (!_inFlight.Remove(tag, out done))
                return;
        }
        done(text);
    }
}

/// <summary>A compare's state, as <see cref="RenderPlanHost.CompareReading"/> answers it.</summary>
public sealed record RenderCompareReading(int Sequence, string State, bool Mutated, bool Equal, int Index, string? Stock,
                                          string? Plan, int StockCalls, int PlanCalls, int StockFrame, int PlanFrame,
                                          string? PlanNote, string? SceneDifference, string? MainView,
                                          string? StockCopyStart = null, string? PlanCopyStart = null);

/// <summary>
/// <c>cs_cl_draw_batch</c>: 1 (the default) prepares a view's custom-draw
/// entities -- sprites and beams -- in one crossing ahead of their DrawModel
/// (C7 item 7, shell_standalone_client.cpp's draw batch); 0 draws each through
/// its own crossing, as before. The switch is the batch's A/B.
/// </summary>
[ConVar("cs_cl_draw_batch", "1", Help = "1: prepare a view's custom draws in one crossing; 0: one crossing per entity")]
public sealed class DrawBatchConVar : BoolConVar
{
}

/// <summary>
/// <c>cs_cl_render_trace &lt;file&gt;</c>: records the next frame's render calls
/// (shell_render_trace_client.cpp's lines) into a file. The render plan's oracle
/// is recorded with it (plan_client_novalve C7 step 1).
/// </summary>
[ConCommand("cs_cl_render_trace", Help = "Record the next frame's render calls: cs_cl_render_trace <file>")]
public sealed class RenderTraceCommand(ILog log, RenderPlanHost plan) : IConCommand
{
    public void Execute(string args)
    {
        string path = args.Trim().Trim('"');
        if (path.Length == 0)
        {
            log.Msg("usage: cs_cl_render_trace <file>\n");
            return;
        }
        plan.RequestTrace(text =>
        {
            File.WriteAllText(path, text);
            log.Msg($"[SourceSharp/Render] recorded {text.Count(c => c == '\n')} lines to {path}\n");
        });
    }
}
