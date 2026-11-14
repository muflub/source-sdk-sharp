using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client
// (plan_side_isolation §2.4): the town portal's pad, TF2's Engineer
// teleporter in look only (plan_descent_logic WO4, ruling L10).

namespace TF2.Descent.Shared;

/// <summary>
/// A <c>tf2_descent_teleporter</c> pad's networked state: which end it is,
/// the sequence its arms spin with, and the portal's number. Everyone is sent
/// it: a pad is no secret (the death plan's M3: everyone sees a teleporter).
/// </summary>
[StandaloneNetState(ClassName)]
public partial class PortalNetState
{
    public const string ClassName = "tf2_descent_teleporter";

    /// <summary>The portal this pad is an end of (the server's record number); 0 for none.</summary>
    public partial ushort Portal { get; set; }

    /// <summary><see cref="PortalEnd"/>: the entrance on a level or the exit in town.</summary>
    public partial byte End { get; set; }

    /// <summary>The model's spinning sequence (stock's <c>ACT_OBJ_RUNNING</c>), looked up on the server; -1 for none, which leaves it still.</summary>
    public partial short Sequence { get; set; }
}

/// <summary>Which end of a town portal a pad is.</summary>
public enum PortalEnd : byte
{
    /// <summary>On the level, where the scroll was read: standing on it goes up to town.</summary>
    Entrance = 1,

    /// <summary>In town: standing on it goes back down to the entrance, once.</summary>
    Exit = 2,
}

/// <summary>The pad's look: TF2's teleporter, from the mod's GAME path (the TF2 mount); no art of Descent's own.</summary>
public static class PortalLooks
{
    /// <summary>
    /// A built, working teleporter: stock swaps the building model for this one when the
    /// teleporter goes active (<c>TELEPORTER_MODEL_LIGHT</c>, <c>CObjectTeleporter::OnGoActive</c>);
    /// its looping <c>running</c> sequence is the arms' spin. The building model's only
    /// <see cref="SpinActivity"/> sequence is a still idle.
    /// </summary>
    public const string Model = "models/buildables/teleporter_light.mdl";

    /// <summary>The activity whose sequence spins the arms (stock's charged teleporter, <c>CObjectTeleporter::TeleporterThink</c>).</summary>
    public const string SpinActivity = "ACT_OBJ_RUNNING";
}
