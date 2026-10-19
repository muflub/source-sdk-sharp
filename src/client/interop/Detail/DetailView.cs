using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>What a view is drawn with: stock's viewOrigin/Forward/Right/Up and CurrentViewOrigin().</summary>
public readonly record struct DetailView(Vector Origin, Vector Forward, Vector Right, Vector Up, Vector CurrentViewOrigin);

/// <summary>The switches a draw is made under, as native reads them: r_DrawDetailProps, ShouldDrawInWireFrameMode(), mat_fullbright.</summary>
public readonly record struct DetailSwitches(int DrawDetailProps, bool Wireframe, int Fullbright);
