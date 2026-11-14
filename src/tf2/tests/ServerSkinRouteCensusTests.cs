using System;
using System.Reflection;
using SourceSharp.Tests;
using TF2.Server.Buildings;
using Xunit;

namespace TF2.Tests;

/// <summary>
/// The server-side classes wear their entity through <see cref="SourceSharp.Abi.IServerSkins"/>,
/// whose write reaches the client's <c>m_nSkin</c>: the render seam's
/// <c>SetSkin</c> answers nothing on the server (EngineBinding.Server), so a
/// class that skins itself there leaves its entity in the client's first skin
/// (the 2026-10-19 ruling EngineApiCensusTests records). One census, all the
/// server classes at once.
/// </summary>
public class ServerSkinRouteCensusTests
{
    private static Assembly Server => typeof(TFBaseObject).Assembly;

    [Fact]
    public void NoServerClassSetsItsSkinThroughTheRenderSeam()
    {
        string[] callers = IlCallCensus.Callers(
            m => m.DeclaringType?.FullName == "SourceSharp.Abi.IEngineEntityRender" && m.Name == "SetSkin", Server);
        Assert.True(callers.Length == 0,
                    $"server skins must reach the client through IServerSkins; still calling the empty render seam: {string.Join(", ", callers)}");
    }
}
