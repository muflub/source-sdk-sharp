using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Part 6's Phase 5 gate: a screenshot comparison against a stock
/// <c>prop_dynamic</c> — same model, same origin, same frame.
///
/// <para>
/// THE STOCK PROP NO LONGER EXISTS BY ANY NAME, and this paragraph used to say
/// the opposite. All four of props.cpp's registrations — <c>prop_dynamic</c>,
/// <c>dynamic_prop</c>, <c>prop_dynamic_override</c> and
/// <c>prop_dynamic_ornament</c> — are behind <c>#ifndef SOURCESHARP</c> and
/// answer to managed classes here.
/// </para>
///
/// <para>
/// The ornament was the exception this comment described: it was left
/// unguarded because the managed hierarchy had no parenting to port it onto, so
/// <c>COrnamentProp</c> stayed reachable and was usable here as a stock-prop
/// fixture. Parenting landed and it was ported, which took the fixture with it.
/// It was a poor one anyway — <c>COrnamentProp::Spawn</c> adds
/// <c>EF_NODRAW</c>, so an unattached one is invisible by design and a
/// screenshot gate saw nothing.
/// </para>
///
/// <para>
/// This command is the FIXTURE, not the comparison. Its whole job is that the
/// two runs cannot differ in anything but the entity: one origin, one set of
/// angles, one model, held as constants here rather than typed twice into a
/// shell script. "Same model, same origin" is the gate's premise, and a premise
/// a driver has to remember to honour is one it will eventually not.
/// </para>
/// </summary>
public static class PropShot
{
    /// <summary>Where the prop stands. Open floor in ss_sandbox's back row.</summary>
    public static readonly Vector Origin = new(1600f, 1500f, 8f);

    /// <summary>
    /// Turned a little off square, so the comparison sees more than one face and
    /// a transform that is subtly wrong has somewhere to show.
    ///
    /// <para>
    /// AND IT IS A CONFOUND, which is why the yaw can now be named. The two
    /// hierarchies do not network angles the same way: DT_BaseEntity sends
    /// <c>m_angRotation</c> as a 13-bit quantised QAngle, while the standalone
    /// table sends it as a 32-bit float (SPROP_NOSCALE). So a stock prop told
    /// to stand at 35 degrees is DRAWN at 34.98046875 and a managed one at
    /// exactly 35 — the two are not in the same pose, and no amount of
    /// rendering fidelity can make their pixels agree. See
    /// <see cref="YawIsExactlyNetworked"/> and docs/divergences.md.
    /// </para>
    /// </summary>
    public static readonly Vector Angles = new(0f, DefaultYaw, 0f);

    /// <summary>The yaw the gate uses when it is not told otherwise.</summary>
    public const float DefaultYaw = 35f;

    /// <summary>
    /// Whether both wire formats carry this yaw EXACTLY, so that a stock prop
    /// and a managed one told to stand at it are drawn in the same pose.
    ///
    /// <para>
    /// Two encodings have to agree, and both are on the stock side — the
    /// standalone table sends the float untouched:
    /// </para>
    ///
    /// <para>
    /// <c>SendProxy_Angles</c> runs <c>anglemod</c>, which is
    /// <c>(360/65536) * ((int)(a * (65536/360)) &amp; 65535)</c> — a TRUNCATION
    /// to 1/65536 of a turn. Then <c>SendPropQAngles(..., 13, ...)</c> writes
    /// it with <c>WriteBitAngle</c>, another truncation, to 1/8192 of a turn.
    /// A yaw survives both only if it is a whole number of 8192ths of 360
    /// degrees: 0, 45, 90, 22.5, and so on.
    /// </para>
    ///
    /// <para>
    /// Computed rather than listed, and computed the way the engine computes
    /// it — the same double arithmetic, the same truncation — because the
    /// question is not "is 45 a nice number" but "does this exact float make
    /// the round trip". 35 does not: 35 * 65536/360 is 6371.55, which truncates
    /// to 6371 and comes back as 34.9969, and 13 bits then take it to
    /// 34.98046875.
    /// </para>
    /// </summary>
    public static bool YawIsExactlyNetworked(float yaw) => NetworkedYaw(yaw) == yaw;

    /// <summary>
    /// The yaw a STOCK entity is actually drawn at when it is told to stand at
    /// this one. <c>NetworkedYaw(35) == 34.98046875</c>.
    ///
    /// <para>
    /// The two truncations are not equally load-bearing, and saying so is
    /// cheaper than a test that pretends otherwise: anglemod's grid is 1/65536
    /// of a turn and WriteBitAngle's is 1/8192, so the finer one can never
    /// change an answer the coarser one would have given — no yaw exists that
    /// this function would judge differently with the anglemod line removed.
    /// It is here because the engine does it, and because the day a send table
    /// asks for more than 13 bits it becomes the one that decides.
    /// </para>
    /// </summary>
    public static float NetworkedYaw(float yaw)
    {
        // anglemod, as util.h spells it.
        float folded = (float)((360.0 / 65536) * ((int)(yaw * (65536.0 / 360.0)) & 65535));

        // WriteBitAngle / ReadBitAngle at 13 bits, as bitbuf.cpp spells them.
        int quantised = (int)((folded / 360.0) * 8192) & 8191;

        return (float)(quantised * (360.0 / 8192));
    }

    /// <summary>
    /// A model with an unmistakable silhouette and a well-known skin, so a
    /// difference reads as a difference rather than as a texture that failed to
    /// load in one run and not the other.
    /// </summary>
    public const string Model = "models/props_c17/oildrum001.mdl";

    /// <summary>
    /// A model with BONES THE POSE DOES NOT DECIDE: fourteen procedural helper
    /// bones, worked out from the bones around them rather than from the
    /// animation.
    ///
    /// <para>
    /// The default drum cannot show a procedural bone working, because it has
    /// none — it is one bone and a static-prop flag. That is not a small gap:
    /// a model with no such bones draws identically whether the feature works
    /// or not, so "is it working" and "can this model tell" share one answer,
    /// and a gate run against the drum passes on a build with no procedural
    /// bone code in it at all.
    /// </para>
    ///
    /// <para>
    /// <b>It has to be a model that ANIMATES, and that is the harder half.</b>
    /// A procedural helper bone is a function of the bones around it, and in
    /// the reference pose it agrees with the pose by construction — so a model
    /// standing at cycle 0 draws identically with the feature and without it.
    /// The obvious candidate, <c>male_01.mdl</c>, carries twelve of these bones
    /// and exactly ONE sequence, called "ragdoll": it cannot be posed at all,
    /// and a gate built on it measures nothing while looking thorough.
    /// <c>gman_high</c> has fourteen and seven sequences, of which
    /// <see cref="BonyAnimation"/> moves the arms.
    /// </para>
    /// </summary>
    public const string BonyModel = "models/gman_high.mdl";

    /// <summary>
    /// A sequence of <see cref="BonyModel"/> that bends the elbows, so the
    /// helper bones have something to help with. "lintpick" — he picks lint
    /// off his sleeve, which is one arm across the body and the other bent.
    /// </summary>
    public const string BonyAnimation = "lintpick";

    /// <summary>The name both fixtures take, so clearing does not need to know which was placed.</summary>
    public const string Name = "cs_propshot";
}

/// <summary>
/// <c>cs_sv_propshot &lt;classname&gt; [yaw]</c>, or <c>cs_sv_propshot clear</c>.
/// </summary>
[ConCommand("cs_sv_propshot",
    Help = "Phase 5's gate fixture: place one prop of <class> [model|bony] at the fixed " +
           "comparison spot at an optional yaw, or 'clear'")]
public sealed class PropShotCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineEntities _entities;
    private readonly IEngineEntityRender _render;

    public PropShotCommand(ILog log, IEngineEntities entities, IEngineEntityRender render)
    {

        _log = log;
        _entities = entities;
        _render = render;
    }

    public void Execute(string args)
    {
        // "<class> [model|bony] [yaw]", in any order after the class.
        //
        // Two lanes each added a second word at the same position -- one meant
        // the yaw, the other the model -- so position cannot decide it. A word
        // that parses as a number is the yaw and a word that does not is the
        // model, which is unambiguous for every value either harness passes
        // and leaves a bare "<class>" meaning exactly what it always did.
        string[] words = args.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string className = words.Length > 0 ? words[0] : string.Empty;

        float yaw = PropShot.DefaultYaw;
        string model = PropShot.Model;

        for (int i = 1; i < words.Length; i++)
        {
            if (float.TryParse(words[i],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float asked))
            {
                yaw = asked;
                continue;
            }

            // A word rather than a path, so a driver names WHICH fixture rather
            // than spelling a model twice: "bony" is the citizen with twelve
            // procedural bones, anything else is taken as a model path.
            model = string.Equals(words[i], "bony", StringComparison.OrdinalIgnoreCase)
                        ? PropShot.BonyModel
                        : words[i];
        }

        var angles = new Vector(0f, yaw, 0f);


        Clear();

        if (className.Length == 0 || className == "clear")
        {
            _log.Msg("[SourceSharp/PropShot] cleared\n");
            return;
        }

        var handle = _entities.Create(className);

        // Asked of the INJECTED engine, not EntityHandle.IsValid — that property
        // calls the static Engine, which reaches past every double a test can
        // hand this and answers false in a test process. PropShotTests found it
        // by failing on a fixture that had silently done nothing.
        if (!_entities.IsValid(handle))
        {
            _log.Warning($"[SourceSharp/PropShot] the engine created no '{className}'\n");
            return;
        }

        _entities.SetKeyValue(handle, "model", model);
        _entities.SetKeyValue(handle, "targetname", PropShot.Name);
        _entities.SetOrigin(handle, PropShot.Origin);
        _entities.SetAngles(handle, angles);
        _entities.Spawn(handle);

        // Nailed down before it is placed. The stock comparand is a physics prop
        // — see tools/prop-shot for why the dynamic ones are all managed — and a
        // physics prop that is still falling is at a different height in every
        // frame, so the comparison would be measuring the drop. Harmlessly
        // ignored by anything with no such input.
        _entities.FireInput(handle, "DisableMotion");

        // Placed AGAIN, last. A prop's Spawn can move it — dropping it to the
        // floor, snapping it to its own bounds — and a comparison whose two
        // subjects were moved by different amounts is comparing the moves rather
        // than the rendering.
        _entities.Teleport(handle, PropShot.Origin, angles, zeroVelocity: true);

        // "exact" or "quantised", and it is not decoration. A yaw the stock
        // wire format cannot carry puts the two props in DIFFERENT POSES before
        // a pixel is drawn, and every difference the comparison then reports is
        // partly that. The word is in the line the harness reads back so the
        // number it prints can never be read without it.
        string carried = PropShot.YawIsExactlyNetworked(yaw) ? "exact" : "quantised";

        _log.Msg($"[SourceSharp/PropShot] {className} "
              + $"index={_entities.GetIndex(handle)} origin={_entities.GetOrigin(handle)} "
              + $"angles={_entities.GetAngles(handle)} yaw={yaw} ({carried}) "
              + $"model='{_render.GetModelName(handle)}'\n");
    }

    private void Clear()
    {
        // Removing while walking is what NameMatches' snapshot is for.
        foreach (EntityHandle previous in _entities.Named(PropShot.Name))
            _entities.Remove(previous);
    }
}
