using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a prop_dynamic tells the client: the sequence it started, at what rate
/// and cycle, and when. Stock's multiplayer CDynamicProp animates client-side
/// (UseClientSideAnimation): the server networks the sequence and playback
/// rate, and the client advances the cycle itself. Here the client gets the
/// start of the clock and runs it; <see cref="Starts"/> tells a restart of the
/// same sequence from an update that kept it.
/// </summary>
[StandaloneNetState(EntityNames.PropDynamic)]
public partial class PropDynamicNetState
{
    /// <summary>The model's sequence index; read once <see cref="Starts"/> has moved.</summary>
    public partial int Sequence { get; set; }

    /// <summary>1 forwards, -1 for a transition played backwards.</summary>
    public partial float PlaybackRate { get; set; }

    /// <summary>The cycle it started at: 0, or 0.999 backwards.</summary>
    public partial float StartCycle { get; set; }

    /// <summary>The server time it started at.</summary>
    public partial float StartTime { get; set; }

    /// <summary>How many sequences have been started: a restart moves it.</summary>
    public partial int Starts { get; set; }
}
