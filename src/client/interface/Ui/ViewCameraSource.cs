namespace SourceSharp.Client.Ui;

/// <summary>What the second camera shows (lane camproto): nothing, the local player from behind, or the ground around them from above.</summary>
public enum ViewCameraMode : byte
{
    Off,
    ThirdPerson,
    Overview,
}

/// <summary>
/// The second camera's panel's data (lane camproto, ahead of plan_client_novalve
/// C7): the backend texture that samples the camera's render target, drawn with
/// <c>ui.Texture</c>, and which part of the target the picture fills. The camera service in the core implements it; the panel's
/// samples implement it with fixed data.
/// </summary>
public interface IViewCameraSource
{
    /// <summary><see cref="ViewCameraMode.Off"/>: the panel draws nothing.</summary>
    ViewCameraMode Mode { get; }

    /// <summary>The backend (vgui) texture id over the camera's render target; 0 before the game has made it.</summary>
    int Texture { get; }

    /// <summary>The part of the target the picture fills, 0..1 of its width and height.</summary>
    UiRect Uv { get; }

    /// <summary>The picture's width over its height, for the panel's shape.</summary>
    float Aspect { get; }
}
