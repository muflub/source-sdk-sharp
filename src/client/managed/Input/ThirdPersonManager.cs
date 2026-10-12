//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Input;

/// <summary>A mutable float triple: the camera offset (pitch, yaw, distance), stock's Vector.</summary>
public struct Vector3f(float x, float y, float z)
{
    public float X = x, Y = y, Z = z;

    public override readonly string ToString() => $"({X}, {Y}, {Z})";
}

/// <summary>
/// cam_thirdperson.cpp's CThirdPersonManager, as far as the client uses it:
/// the camera offsets the input code moves, the desired offset the commands
/// set, the override and forced flags, and Update's rule for when third
/// person is allowed. The two distance fractions and PositionCamera went with
/// step 7c (the shim's third-person pass sweeps the hull); GetFinalCameraOffset
/// and GetDistanceFraction have no reader on this client.
/// </summary>
public sealed class ThirdPersonManager
{
    private readonly ClientInput _input;
    private readonly IInputHost _host;
    private readonly InputCvars _cvars;

    /// <summary>
    /// CGameRules::AllowThirdPersonCamera, as HL2MP answers it: the base's
    /// false, which HL2MP does not override. Stock asked GameRules(), which is
    /// NULL on this client, so its sv_cheats rule never ran; the rule is
    /// ported with the game's answer (memory: port dead stock arms).
    /// </summary>
    private readonly bool _gameRulesAllowThirdPerson;

    public ThirdPersonManager(ClientInput input, IInputHost host, InputCvars cvars, bool gameRulesAllowThirdPerson = false)
    {
        _input = input;
        _host = host;
        _cvars = cvars;
        _gameRulesAllowThirdPerson = gameRulesAllowThirdPerson;
    }

    /// <summary>What the current camera offset from the view origin is (pitch, yaw, distance).</summary>
    public Vector3f CameraOffset { get; set; }

    /// <summary>Distances from the centre (forward, right, up).</summary>
    public Vector3f DesiredCameraOffset { get; set; }

    public bool OverridingThirdPerson { get; set; }

    public bool ForcedThirdPerson { get; set; }

    /// <summary>CThirdPersonManager::Init (at level shutdown): the flags, and the camera back to the convars.</summary>
    public void Init()
    {
        OverridingThirdPerson = false;
        ForcedThirdPerson = false;
        _input.CamSetCameraThirdData(null, default);
    }

    /// <summary>
    /// CThirdPersonManager::Update, from CAM_Think: with cheats off and the
    /// game forbidding it, third person ends; otherwise cl_thirdperson (or a
    /// forced camera) decides, when the game allows it.
    /// </summary>
    public void Update()
    {
        string? cheats = _host.EngineConVar("sv_cheats");
        bool cheatsKnown = cheats is not null;
        bool cheatsOn = cheatsKnown && (int)(float)CRuntime.Atof(cheats) != 0;

        if (cheatsKnown && !cheatsOn && !_gameRulesAllowThirdPerson)
        {
            if (_input.CamIsThirdPerson())
                _input.CamToFirstPerson();
            return;
        }

        if (!OverridingThirdPerson)
        {
            if (_input.CamIsThirdPerson() != (_cvars.ThirdPerson.Bool || ForcedThirdPerson) && _gameRulesAllowThirdPerson)
                ToggleThirdPerson(ForcedThirdPerson || _cvars.ThirdPerson.Bool);
        }
    }

    /// <summary>ToggleThirdPerson: cl_thirdperson's change callback.</summary>
    public void ToggleThirdPerson(bool thirdPerson)
    {
        if (thirdPerson)
            CamToThirdPerson();
        else
            CamToFirstPerson();
    }

    /// <summary>The global CAM_ToThirdPerson (the <c>thirdperson</c> command).</summary>
    public void CamToThirdPerson()
    {
        if (!_cvars.ThirdPerson.Bool)
        {
            DesiredCameraOffset = new Vector3f(_cvars.CamIdealDist.Float, _cvars.CamIdealDistRight.Float, _cvars.CamIdealDistUp.Float);
            OverridingThirdPerson = true;
        }

        _input.CamToThirdPerson();

        // The viewer's own model comes into the leaf system this frame.
        _host.ViewerCameraModeChanged();
    }

    /// <summary>The global CAM_ToFirstPerson (the <c>firstperson</c> command).</summary>
    public void CamToFirstPerson()
    {
        // Stock first asked the player CanUseFirstPersonCommand, whose base
        // answer is true and which only TF's disguised spy overrides.
        if (!_cvars.ThirdPerson.Bool)
            OverridingThirdPerson = false;

        _input.CamToFirstPerson();

        // ...and the viewer's model goes back out of the leaf system.
        _host.ViewerCameraModeChanged();
    }

    /// <summary>The global CAM_ToOrthographic (<c>camortho</c>).</summary>
    public void CamToOrthographic() => _input.CamToOrthographic();
}
