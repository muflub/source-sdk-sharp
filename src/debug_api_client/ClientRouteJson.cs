using System.Buffers;
using System.Text;
using System.Text.Json;

using Debug.Api.Client.Interfaces;

namespace Debug.Api;

/// <summary>
/// The JSON of the client's own routes (plan_client_novalve §4.4), each from
/// its Client.Interface record. Utf8JsonWriter rather than a serializer: it
/// reflects over nothing, and its number and string forms are invariant, so a
/// golden holds byte for byte.
/// </summary>
internal static class ClientRouteJson
{
    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var json = new Utf8JsonWriter(buffer))
            body(json);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Vec3(Utf8JsonWriter json, string name, DebugClientVec3 v)
    {
        json.WriteStartArray(name);
        json.WriteNumberValue(v.X);
        json.WriteNumberValue(v.Y);
        json.WriteNumberValue(v.Z);
        json.WriteEndArray();
    }

    private static void Counts(Utf8JsonWriter json, string name, IReadOnlyList<DebugCount> counts)
    {
        json.WriteStartObject(name);
        foreach (var c in counts)
            json.WriteNumber(c.Name, c.Count);
        json.WriteEndObject();
    }

    /// <summary><c>/client/{slot}/entities[?class=]</c>.</summary>
    public static string Entities(string className, IReadOnlyList<DebugClientEntity> entities) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteString("class", className);
        json.WriteNumber("count", entities.Count);
        json.WriteStartArray("entities");
        foreach (var e in entities)
        {
            json.WriteStartObject();
            EntityRow(json, e);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/entity/{n}</c>.</summary>
    public static string Entity(DebugClientEntityState s) => Write(json =>
    {
        json.WriteStartObject();
        EntityRow(json, s.Entity);
        Vec3(json, "angles", s.Angles);
        Vec3(json, "renderAngles", s.RenderAngles);
        json.WriteStartArray("states");
        foreach (var block in s.States)
        {
            json.WriteStartObject();
            json.WriteString("class", block.ClassName);
            json.WriteString("type", block.StateType);
            json.WriteBoolean("live", block.Live);
            json.WriteNumber("bytes", block.Bytes);
            json.WriteStartArray("fields");
            foreach (var f in block.Fields)
            {
                json.WriteStartObject();
                json.WriteString("name", f.Name);
                json.WriteString("kind", f.Kind);
                json.WriteNumber("offset", f.Offset);
                json.WriteNumber("bits", f.Bits);
                json.WriteBoolean("present", f.Present);
                json.WriteString("value", f.Value);
                json.WriteString("hex", f.Hex);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("interpolated");
        foreach (var i in s.Interpolated)
        {
            json.WriteStartObject();
            json.WriteString("field", i.Field);
            json.WriteNumber("depth", i.Depth);
            json.WriteNumber("newestTime", i.NewestTime);
            Vec3(json, "newest", i.Newest);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    private static void EntityRow(Utf8JsonWriter json, DebugClientEntity e)
    {
        {
            json.WriteNumber("index", e.Index);
            json.WriteString("handle", $"0x{e.Handle:X8}");
            json.WriteString("class", e.ClassName);
            json.WriteString("model", e.Model);
            json.WriteNumber("modelIndex", e.ModelIndex);
            Vec3(json, "origin", e.Origin);
            Vec3(json, "renderOrigin", e.RenderOrigin);
            json.WriteBoolean("dormant", e.Dormant);
            json.WriteNumber("interpolationDepth", e.InterpolationDepth);
            json.WriteStartObject("shadow");
            json.WriteNumber("type", e.ShadowType);
            json.WriteNumber("choice", e.ShadowChoice);
            json.WriteBoolean("held", e.HasShadow);
            json.WriteEndObject();
            json.WriteStartObject("policy");
            json.WriteNumber("renderGroup", e.RenderGroup);
            json.WriteBoolean("drawn", e.Drawn);
            json.WriteBoolean("viewModel", e.ViewModel);
            json.WriteNumber("inputs", e.PolicyInputs);
            json.WriteNumber("outputs", e.PolicyOutputs);
            json.WriteEndObject();
        }
    }

    /// <summary><c>/client/{slot}/nettables</c>.</summary>
    public static string NetTables(DebugClientNetTables n) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteStartObject("agreement");
        json.WriteBoolean("agrees", n.Agrees);
        json.WriteStartArray("tiers");
        foreach (var t in n.Tiers)
        {
            json.WriteStartObject();
            json.WriteString("table", t.Table);
            json.WriteNumber("tierProps", t.TierProps);
            json.WriteNumber("engineProps", t.EngineProps);
            json.WriteBoolean("agrees", t.Agrees);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteNumber("refusedUpdates", n.RefusedUpdates);
        json.WriteStartArray("refusals");
        foreach (var r in n.Refusals)
        {
            json.WriteStartObject();
            json.WriteString("class", r.ClassName);
            json.WriteString("typeId", $"0x{r.TypeId:X8}");
            json.WriteString("expected", $"0x{r.Expected:X8}");
            json.WriteString("seen", $"0x{r.Seen:X8}");
            json.WriteNumber("count", r.Count);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();

        // The engine's recv tables, each once.
        json.WriteNumber("tableCount", n.Tables.Count);
        json.WriteStartArray("tables");
        foreach (var t in n.Tables)
        {
            json.WriteStartObject();
            json.WriteString("name", t.Name);
            json.WriteString("class", t.ClassName);
            json.WriteStartArray("props");
            foreach (var p in t.Props)
            {
                json.WriteStartObject();
                json.WriteString("name", p.Name);
                json.WriteString("type", p.Type);
                json.WriteNumber("flags", p.Flags);
                json.WriteNumber("offset", p.Offset);
                json.WriteNumber("elements", p.Elements);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();

        // This side's declared types: names, kinds, offsets and bits.
        json.WriteStartArray("types");
        foreach (var t in n.Types)
        {
            json.WriteStartObject();
            json.WriteString("class", t.ClassName);
            json.WriteString("typeId", $"0x{t.TypeId:X8}");
            json.WriteString("schemaHash", $"0x{t.SchemaHash:X8}");
            json.WriteString("table", t.Table);
            json.WriteNumber("layoutBytes", t.LayoutBytes);
            json.WriteNumber("words", t.Words);
            json.WriteStartArray("fields");
            foreach (var f in t.Fields)
            {
                json.WriteStartObject();
                json.WriteString("name", f.Name);
                json.WriteString("kind", f.Kind);
                json.WriteNumber("offset", f.Offset);
                json.WriteNumber("bytes", f.Bytes);
                json.WriteNumber("bits", f.Bits);
                json.WriteNumber("shift", f.Shift);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    private static void Strings(Utf8JsonWriter json, string name, IReadOnlyList<string> values)
    {
        json.WriteStartArray(name);
        foreach (string v in values)
            json.WriteStringValue(v);
        json.WriteEndArray();
    }

    /// <summary><c>/client/{slot}/reload</c>.</summary>
    public static string Reload(DebugClientReload r) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("sequence", r.Sequence);
        json.WriteNumber("savedAt", r.SavedAt);
        json.WriteNumber("restoredAt", r.RestoredAt);
        json.WriteBoolean("triggerPending", r.TriggerPending);
        if (r.Milliseconds > 0)
            json.WriteNumber("milliseconds", Math.Round(r.Milliseconds, 1));
        json.WriteStartArray("carried");
        foreach (var s in r.Carried)
        {
            json.WriteStartObject();
            json.WriteString("section", s.Name);
            json.WriteNumber("bytes", s.Bytes);
            json.WriteNumber("count", s.Count);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartObject("participants");
        json.WriteNumber("saved", r.ParticipantsSaved);
        json.WriteNumber("restored", r.ParticipantsRestored);
        Strings(json, "orphans", r.Orphans);
        Strings(json, "failed", r.Failed);
        Strings(json, "missing", r.Missing);
        json.WriteEndObject();
        json.WriteStartObject("entities");
        json.WriteNumber("saved", r.EntitiesSaved);
        json.WriteNumber("restored", r.EntitiesRestored);
        json.WriteEndObject();
        json.WriteStartObject("leak");
        json.WriteNumber("cachesDropped", r.CachesDropped);
        json.WriteString("oldContext", r.OldContext);
        json.WriteNumber("collections", r.Collections);
        json.WriteEndObject();
        if (r.Ui is { } u)
        {
            json.WriteStartObject("ui");
            json.WriteString("kind", u.Kind);
            json.WriteNumber("sequence", u.Sequence);
            json.WriteString("trigger", u.Trigger);
            json.WriteNumber("milliseconds", Math.Round(u.Milliseconds, 2));
            json.WriteNumber("carriedBytes", u.CarriedBytes);
            json.WriteNumber("reopened", u.Reopened);
            json.WriteString("oldContext", u.OldContext);
            json.WriteNumber("collections", u.Collections);
            json.WriteNumber("collectMilliseconds", Math.Round(u.CollectMilliseconds, 2));
            json.WriteString("deploy", u.Deploy);
            Strings(json, "deployChanged", u.DeployChanged);
            json.WriteEndObject();
        }
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/viewcam[?mode=0|1|2]</c>: the debug camera, and the write's outcome.</summary>
    public static string ViewCamera(DebugClientViewCamera v) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteString("mode", v.Mode);
        json.WriteString("active", v.Active);
        json.WriteNumber("every", v.Every);
        json.WriteBoolean("cheats", v.Cheats);
        json.WriteNumber("passes", v.Passes);
        json.WriteNumber("requested", v.Requested);
        json.WriteNumber("queued", v.Queued);
        json.WriteNumber("refused", v.Refused);
        json.WriteNumber("drawn", v.Drawn);
        json.WriteNumber("width", v.Width);
        json.WriteNumber("height", v.Height);
        json.WriteNumber("texture", v.Texture);
        json.WriteNumber("monitorPasses", v.MonitorPasses);
        if (v.Write is { } write)
            json.WriteString("write", write);
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/reload/trigger</c>.</summary>
    public static string ReloadRequest(DebugClientReloadRequest r) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteBoolean("accepted", r.Accepted);
        json.WriteString("reason", r.Reason);
        json.WriteNumber("sequence", r.Sequence);
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/input</c> (lane c6-input).</summary>
    public static string Input(DebugClientInput i) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteStartArray("buttons");
        foreach (var b in i.Buttons)
        {
            json.WriteStartObject();
            json.WriteString("name", b.Name);
            json.WriteNumber("state", b.State);
            json.WriteNumber("down0", b.Down0);
            json.WriteNumber("down1", b.Down1);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartObject("mouse");
        json.WriteBoolean("active", i.MouseActive);
        json.WriteNumber("accumulatedX", i.AccumulatedX);
        json.WriteNumber("accumulatedY", i.AccumulatedY);
        json.WriteNumber("previousX", i.PreviousX);
        json.WriteNumber("previousY", i.PreviousY);
        json.WriteEndObject();
        var s = i.Sensitivity;
        json.WriteStartObject("sensitivity");
        json.WriteNumber("sensitivity", s.Sensitivity);
        json.WriteNumber("hud", s.HudSensitivity);
        json.WriteNumber("customAccel", s.CustomAccel);
        json.WriteNumber("accelScale", s.AccelScale);
        json.WriteNumber("accelExponent", s.AccelExponent);
        json.WriteNumber("accelMax", s.AccelMax);
        json.WriteBoolean("filter", s.Filter);
        json.WriteNumber("yaw", s.Yaw);
        json.WriteNumber("pitch", s.Pitch);
        json.WriteBoolean("rawInput", s.RawInput);
        json.WriteEndObject();
        json.WriteStartObject("camera");
        json.WriteBoolean("thirdPerson", i.ThirdPerson);
        json.WriteBoolean("orthographic", i.Orthographic);
        json.WriteEndObject();
        json.WriteStartObject("controllers");
        json.WriteBoolean("joystick", i.Joystick);
        json.WriteStartArray("axes");
        foreach (var a in i.Axes)
        {
            json.WriteStartObject();
            json.WriteNumber("axis", a.Axis);
            json.WriteNumber("map", a.Map);
            json.WriteBoolean("relative", a.Relative);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteNumber("actionSet", i.ActionSet);
        json.WriteStartArray("actions");
        foreach (var a in i.Actions)
        {
            json.WriteStartObject();
            json.WriteString("action", a.Action);
            json.WriteString("command", a.Command);
            json.WriteNumber("handle", a.Handle);
            json.WriteBoolean("state", a.State);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteStartArray("commands");
        foreach (var c in i.Commands)
        {
            json.WriteStartObject();
            json.WriteNumber("command", c.Command);
            json.WriteNumber("tick", c.Tick);
            json.WriteStartArray("angles");
            json.WriteNumberValue(c.Pitch);
            json.WriteNumberValue(c.Yaw);
            json.WriteNumberValue(c.Roll);
            json.WriteEndArray();
            json.WriteNumber("forward", c.Forward);
            json.WriteNumber("side", c.Side);
            json.WriteNumber("up", c.Up);
            json.WriteNumber("buttons", c.Buttons);
            json.WriteNumber("impulse", c.Impulse);
            json.WriteNumber("weaponSelect", c.WeaponSelect);
            json.WriteNumber("mouseDx", c.MouseDx);
            json.WriteNumber("mouseDy", c.MouseDy);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartObject("inject");
        json.WriteNumber("pending", i.InjectPending);
        json.WriteNumber("applied", i.Injected);
        // The last read that took an inject: its counts and the device's in
        // the same read (input-usercmd's turn is their sum).
        json.WriteNumber("lastX", i.LastInjectX);
        json.WriteNumber("lastY", i.LastInjectY);
        json.WriteNumber("deviceX", i.LastInjectDeviceX);
        json.WriteNumber("deviceY", i.LastInjectDeviceY);
        json.WriteEndObject();
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/input/inject?e=</c> (lane c6-input).</summary>
    public static string InputInject(DebugClientInputInject r) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteBoolean("accepted", r.Accepted);
        json.WriteString("reason", r.Reason);
        json.WriteNumber("dx", r.Dx);
        json.WriteNumber("dy", r.Dy);
        if (r.Key is null)
            json.WriteNull("key");
        else
            json.WriteString("key", r.Key);
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/netgraph[?frames=]</c>.</summary>
    public static string NetGraph(DebugClientNetGraph n) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteBoolean("connected", n.Connected);
        json.WriteNumber("timeConnected", n.TimeConnected);
        json.WriteNumber("dataRate", n.DataRate);
        json.WriteBoolean("loopback", n.Loopback);
        json.WriteBoolean("timingOut", n.TimingOut);
        json.WriteNumber("sinceLastReceivedMs", n.SinceLastReceivedMs);
        json.WriteNumber("remoteFrameTimeMs", n.RemoteFrameTimeMs);
        json.WriteNumber("remoteFrameTimeDeviationMs", n.RemoteFrameTimeDeviationMs);
        json.WriteNumber("interpMs", n.InterpMs);
        json.WriteStartArray("flows");
        foreach (var f in n.Flows)
        {
            json.WriteStartObject();
            json.WriteString("flow", f.Name);
            json.WriteNumber("latencyMs", f.LatencyMs);
            json.WriteNumber("avgLatencyMs", f.AvgLatencyMs);
            json.WriteNumber("avgLoss", f.AvgLoss);
            json.WriteNumber("avgChoke", f.AvgChoke);
            json.WriteNumber("avgBytesPerSecond", f.AvgBytesPerSecond);
            json.WriteNumber("avgPacketsPerSecond", f.AvgPacketsPerSecond);
            json.WriteNumber("totalBytes", f.TotalBytes);
            json.WriteNumber("sequence", f.Sequence);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("packets");
        foreach (var p in n.Packets)
        {
            json.WriteStartObject();
            json.WriteNumber("sequence", p.Sequence);
            json.WriteBoolean("valid", p.Valid);
            json.WriteNumber("time", p.Time);
            json.WriteNumber("commandInterpolation", p.CommandInterpolation);
            json.WriteNumber("responseLatencyMs", p.ResponseLatencyMs);
            json.WriteNumber("choke", p.Choke);
            json.WriteNumber("bytes", p.TotalBytes);
            Counts(json, "groups", p.Groups);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/absences</c>.</summary>
    public static string Ui(DebugClientUi u) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteBoolean("built", u.Built);
        json.WriteNumber("w", u.W);
        json.WriteNumber("h", u.H);
        json.WriteNumber("scale", u.Scale);
        json.WriteNumber("frames", u.Frames);
        json.WriteNumber("meanMicroseconds", Math.Round(u.MeanMicroseconds, 1));
        json.WriteNumber("lastMicroseconds", Math.Round(u.LastMicroseconds, 1));
        json.WriteNumber("maxMicroseconds", Math.Round(u.MaxMicroseconds, 1));
        Counts(json, "kinds", u.Kinds);
        json.WriteStartArray("draws");
        foreach (var d in u.Draws)
        {
            json.WriteStartObject();
            json.WriteString("kind", d.Kind);
            json.WriteNumber("x0", d.X0);
            json.WriteNumber("y0", d.Y0);
            json.WriteNumber("x1", d.X1);
            json.WriteNumber("y1", d.Y1);
            json.WriteString("colour", d.Colour);
            if (d.Text.Length > 0)
            {
                json.WriteNumber("font", d.Font);
                json.WriteString("text", d.Text);
            }
            json.WriteNumber("element", d.Element);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        if (u.Open is not null)
        {
            json.WriteStartArray("open");
            foreach (string o in u.Open)
                json.WriteStringValue(o);
            json.WriteEndArray();
            json.WriteBoolean("menuInput", u.MenuInput);
        }
        if (u.Svg is not null)
            json.WriteString("svg", u.Svg);
        json.WriteEndObject();
    });

    public static string UiFonts(IReadOnlyList<DebugClientUiFont> fonts) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteStartArray("fonts");
        foreach (var f in fonts)
        {
            json.WriteStartObject();
            json.WriteString("family", f.Family);
            json.WriteNumber("px", f.Px);
            json.WriteNumber("weight", f.Weight);
            json.WriteNumber("flags", f.Flags);
            json.WriteNumber("handle", f.Handle);
            json.WriteNumber("tall", f.Tall);
            json.WriteNumber("ascent", f.Ascent);
            json.WriteNumber("firstChar", f.FirstChar);
            json.WriteStartArray("advances");
            foreach (int a in f.Advances)
                json.WriteNumberValue(a);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    public static string Absences(DebugClientAbsences a) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("notShippedTotal", a.NotShippedTotal);
        json.WriteNumber("refusedTotal", a.RefusedTotal);
        json.WriteStartArray("rows");
        foreach (var r in a.Rows)
        {
            json.WriteStartObject();
            json.WriteString("ledger", r.Ledger);
            json.WriteString("name", r.Name);
            json.WriteString("reason", r.Reason);
            json.WriteNumber("count", r.Count);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/proxies</c> (lane c5-proxies).</summary>
    public static string Proxies(DebugClientProxies p) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("registered", p.Registered);
        json.WriteNumber("unknownBinds", p.UnknownBinds);
        json.WriteNumber("droppedOps", p.DroppedOps);
        json.WriteStartArray("names");
        foreach (var n in p.Names)
        {
            json.WriteStartObject();
            json.WriteString("name", n.Name);
            json.WriteNumber("live", n.Live);
            json.WriteNumber("binds", n.Binds);
            json.WriteNumber("bindsLastFrame", n.BindsLastFrame);
            json.WriteNumber("unanswered", n.Unanswered);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("proxies");
        foreach (var x in p.Proxies)
        {
            json.WriteStartObject();
            json.WriteNumber("handle", x.Handle);
            json.WriteString("name", x.Name);
            json.WriteString("material", x.Material);
            json.WriteBoolean("initialised", x.Initialised);
            json.WriteNumber("binds", x.Binds);
            json.WriteStartArray("lastOutput");
            foreach (string op in x.LastOutput)
                json.WriteStringValue(op);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/render</c> (plan_client_novalve C7).</summary>
    public static string Render(DebugClientRender r) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("planned", r.Planned);
        json.WriteNumber("declinedToStock", r.DeclinedToStock);
        json.WriteNumber("viewPrograms", r.ViewPrograms);
        json.WriteNumber("viewProgramsDeclined", r.ViewProgramsDeclined);
        json.WriteNumber("monitorPasses", r.MonitorPasses);
        json.WriteNumber("viewDecisions", r.ViewDecisions);
        json.WriteStartArray("lastPlan");
        foreach (string op in r.LastPlan)
            json.WriteStringValue(op);
        json.WriteEndArray();
        var c = r.Compare;
        json.WriteStartObject("compare");
        json.WriteNumber("sequence", c.Sequence);
        json.WriteString("state", c.State);
        json.WriteBoolean("mutated", c.Mutated);
        json.WriteBoolean("equal", c.Equal);
        json.WriteNumber("index", c.Index);
        json.WriteString("stock", c.Stock);
        json.WriteString("plan", c.Plan);
        json.WriteNumber("stockCalls", c.StockCalls);
        json.WriteNumber("planCalls", c.PlanCalls);
        json.WriteNumber("stockFrame", c.StockFrame);
        json.WriteNumber("planFrame", c.PlanFrame);
        json.WriteString("planNote", c.PlanNote);
        json.WriteString("sceneDifference", c.SceneDifference);
        json.WriteString("mainView", c.MainView);
        json.WriteString("stockCopyStart", c.StockCopyStart);
        json.WriteString("planCopyStart", c.PlanCopyStart);
        json.WriteEndObject();
        foreach (var (name, t) in new[] { ("planTimes", r.Plan) })
        {
            json.WriteStartObject(name);
            json.WriteNumber("frames", t.Frames);
            json.WriteNumber("msAvg", Math.Round(t.MsAvg, 3));
            json.WriteNumber("msMax", Math.Round(t.MsMax, 3));
            json.WriteEndObject();
        }
        if (r.Observer is { } o)
        {
            json.WriteStartObject("observer");
            json.WriteNumber("frames", o.Frames);
            json.WriteBoolean("active", o.Active);
            json.WriteString("inputs", o.Inputs);
            json.WriteStartArray("origin");
            foreach (float f in o.Origin)
                json.WriteNumberValue(f);
            json.WriteEndArray();
            json.WriteStartArray("angles");
            foreach (float f in o.Angles)
                json.WriteNumberValue(f);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/render/compare[?mutate=1]</c>.</summary>
    public static string RenderRequest(DebugClientRenderRequest r) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteBoolean("accepted", r.Accepted);
        json.WriteString("reason", r.Reason);
        json.WriteNumber("sequence", r.Sequence);
        json.WriteEndObject();
    });

    /// <summary><c>/client/{slot}/particles</c>.</summary>
    public static string Particles(DebugClientParticles p) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("definitionReads", p.DefinitionReads);
        json.WriteNumber("count", p.Systems.Count);
        json.WriteNumber("withParticles", p.WithParticles);
        Counts(json, "queryUnanswered", p.QueryUnanswered);
        json.WriteStartArray("systems");
        foreach (var s in p.Systems)
        {
            json.WriteStartObject();
            json.WriteString("name", s.Name);
            if (s.Owner is { } owner)
                json.WriteNumber("owner", owner);
            else
                json.WriteNull("owner");
            json.WriteString("attachment", s.Attachment);
            json.WriteStartArray("followPoints");
            foreach (int point in s.FollowPoints)
                json.WriteNumberValue(point);
            json.WriteEndArray();
            json.WriteNumber("age", s.Age);
            json.WriteNumber("particles", s.Particles);
            ParticlePoint(json, "controlPoint0", s.ControlPoint0);
            ParticlePoint(json, "controlPoint1", s.ControlPoint1);
            ParticlePoint(json, "sortOrigin", s.SortOrigin);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    });

    private static void ParticlePoint(Utf8JsonWriter json, string name, DebugClientPoint p)
    {
        json.WriteStartArray(name);
        json.WriteNumberValue(p.X);
        json.WriteNumberValue(p.Y);
        json.WriteNumberValue(p.Z);
        json.WriteEndArray();
    }

    /// <summary><c>/client/{slot}/frame</c>.</summary>
    public static string Frame(DebugClientFrame f) => Write(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("frames", f.Frames);
        json.WriteNumber("commands", f.Commands);
        json.WriteString("stage", f.Stage);
        Counts(json, "stages", f.Stages);

        // What CFPSPanel drew, over the last second of wall time.
        json.WriteStartObject("window");
        json.WriteNumber("seconds", 1);
        json.WriteNumber("frames", f.WindowFrames);
        json.WriteNumber("frameMsMin", Math.Round(f.FrameMsMin, 3));
        json.WriteNumber("frameMsAvg", Math.Round(f.FrameMsAvg, 3));
        json.WriteNumber("frameMsMax", Math.Round(f.FrameMsMax, 3));
        json.WriteEndObject();

        // What cl_showpos drew.
        json.WriteStartObject("pose");
        json.WriteNumber("time", f.Time);
        json.WriteBoolean("known", f.PoseKnown);
        Vec3(json, "eye", f.Eye);
        json.WriteNumber("pitch", f.Pitch);
        json.WriteNumber("yaw", f.Yaw);
        json.WriteEndObject();

        json.WriteEndObject();
    });
}
