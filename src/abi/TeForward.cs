namespace SourceSharp.Abi;

/// <summary>
/// One value per forwarder entry point in <c>shell_te_forwards.cpp</c> and
/// <c>shell_te_forwards_client.cpp</c> — the managed half of the contract in
/// <c>game/shared/tempent_forwards.h</c>.
///
/// <para>
/// A forward used to know three things C++ has no business knowing: which temp
/// entity class, which argument becomes which field, and what the field is
/// called. All three live here now, and what crosses the boundary is this id
/// plus the arguments the C++ signature already had, packed in declaration
/// order.
/// </para>
///
/// <para>
/// THE ORDER IS THE CONTRACT. These are array indices in all but name: a value
/// inserted in the middle renumbers everything after it and repoints every
/// forward past that point at a different effect, with nothing failing to
/// compile on either side. Append only — <c>TempEntForwardIdTests</c> reads the
/// C++ header off disk and compares, and the header names both sides so there
/// is no stripping rule to disagree about.
/// </para>
///
/// <para>
/// IT WAS FORTY-TWO. plan_shim.md Phase 1f dropped the twenty-four entry points
/// no caller on either link line reached and renumbered the rest once, in the
/// same commit on both sides; the C++ header carries the reason that renumber
/// was safe and why it is not a precedent. A removed value is NOT the same as a
/// removed temp entity: the managed class, its declaration and its send path
/// are untouched. What went is the arm that unpacked a NATIVE forward for it,
/// and there is no longer a native forward to unpack.
/// </para>
/// </summary>
/// <remarks>
/// RESERVED values (ArmorRicochet, BeamPoints, BubbleTrail, EnergySplash): the
/// server gc cull (2026-10-01) deleted their native forwarders, which nothing
/// called. The values stay so that no later id is renumbered; no dispatch arm
/// handles them.
/// </remarks>
public enum TeForward
{
    /// <summary>RESERVED: no native forwarder since the server gc cull; kept so no id moves.</summary>
    ArmorRicochet            = 0,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4f; kept so no id moves.</summary>
    BSPDecal                 = 1,
    /// <summary>RESERVED: no native forwarder since the server gc cull; kept so no id moves.</summary>
    BeamPoints               = 2,
    BloodStream              = 3,
    /// <summary>RESERVED: no native forwarder since the server gc cull; kept so no id moves.</summary>
    BubbleTrail              = 4,
    /// <summary>RESERVED: no native forwarder since lane M1-C (2026-10-25, the client's had no caller); kept so no id moves.</summary>
    Bubbles                  = 5,
    Decal                    = 6,
    TeDispatchEffect         = 7,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4b; kept so no id moves.</summary>
    Dust                     = 8,
    /// <summary>RESERVED: no native forwarder since the server gc cull; kept so no id moves.</summary>
    EnergySplash             = 9,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4d; kept so no id moves.</summary>
    Explosion                = 10,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4d; kept so no id moves.</summary>
    LargeFunnel              = 11,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4b; kept so no id moves.</summary>
    MetalSparks              = 12,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4b; kept so no id moves.</summary>
    MuzzleFlash              = 13,
    /// <summary>RESERVED: no native forwarder since plan_server_native_v2 T4f; kept so no id moves.</summary>
    ProjectDecal             = 14,
    Smoke                    = 15,
    Sparks                   = 16,
    WorldDecal               = 17,
}
