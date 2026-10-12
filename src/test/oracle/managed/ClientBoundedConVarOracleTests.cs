extern alias client;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The client's server-bounded convars (plan_client_novalve C8) against the
/// pristine game/client/cdll_bounded_cvars.cpp: each of stock's four rules is
/// found in the file as the expression BoundedConVarRules ports (so a Valve
/// change to a rule fails here), each rule is run at the edges the expression
/// names, and the native objects' names, defaults, flags and help are the file's.
/// </summary>
public class ClientBoundedConVarOracleTests
{
    private static string Stock() => Tight(ValveSdk.Read("src/game/client/cdll_bounded_cvars.cpp"));

    private static string Tight(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", "");
        return Regex.Replace(text, @"\s+", "");
    }

    [Fact]
    public void PredictIsTheServersUnlessMinusOne()
    {
        string s = Stock();
        Assert.Contains("if(pClientPredict&&pClientPredict->GetInt()!=-1){returnpClientPredict->GetFloat();}else{returnGetBaseFloatValue();}", s);
        Assert.Contains("if(g_bForceCLPredictOff)return0;", s);   // no writer on either link line: stays false
        Assert.Equal(0f, client::SourceSharp.Client.BoundedConVarRules.Predict(1f, 0));
        Assert.Equal(1f, client::SourceSharp.Client.BoundedConVarRules.Predict(1f, -1));
    }

    [Fact]
    public void InterpRatioIsClampedUnlessTheMinimumIsMinusOne()
    {
        Assert.Contains("if(pMin&&pMax&&pMin->GetFloat()!=-1){returnclamp(GetBaseFloatValue(),pMin->GetFloat(),pMax->GetFloat());}else{returnGetBaseFloatValue();}", Stock());
        Assert.Equal(3f, client::SourceSharp.Client.BoundedConVarRules.InterpRatio(2f, 3f, 5f));
        Assert.Equal(2f, client::SourceSharp.Client.BoundedConVarRules.InterpRatio(2f, -1f, 5f));
    }

    [Fact]
    public void InterpIsAtLeastTheMinimumRatioOverTheBoundedRate()
    {
        Assert.Contains("if(pUpdateRate&&pMin&&pMin->GetFloat()!=-1){constConVar_ServerBounded*pUpdateRateBounded=static_cast<constConVar_ServerBounded*>(pUpdateRate);returnMAX(GetBaseFloatValue(),pMin->GetFloat()/(pUpdateRateBounded?pUpdateRateBounded->GetFloat():pUpdateRate->GetFloat()));}", Stock());
        Assert.Equal(0.15f, client::SourceSharp.Client.BoundedConVarRules.Interp(0.1f, 3f, 20f), 6);
    }

    [Fact]
    public void TheInterpolationAmountIsTheLargerOfTheTwo()
    {
        string s = Stock();
        Assert.Contains("returnMAX(cl_interp->GetFloat(),cl_interp_ratio->GetFloat()/(pUpdateRateBounded?pUpdateRateBounded->GetFloat():pUpdateRate->GetFloat()));", s);
        Assert.Contains("return0.1;", s);
        Assert.Equal(0.1f, client::SourceSharp.Client.BoundedConVarRules.InterpAmount(0.05f, 2f, 20f), 6);
        Assert.Equal(0.1f, client::SourceSharp.Client.BoundedConVarRules.InterpAmount(0.3f, 2f, null));
    }

    [Fact]
    public void TheNativeObjectsAreStocksNamesDefaultsFlagsAndHelp()
    {
        string ours = Tight(File.ReadAllText(Path.Combine(ValveSdk.RepoRoot, "src/client/native/shell_bounded_cvars_client.cpp")));
        string stock = Stock();

        // Stock's #if DOD/CSTRIKE arm is not this game; its #else flags are ours.
        Assert.Contains("\"cl_predict\",\"1.0\",#ifdefined(DOD_DLL)||defined(CSTRIKE_DLL)FCVAR_USERINFO|FCVAR_CHEAT,#elseFCVAR_USERINFO|FCVAR_NOT_CONNECTED,#endif\"Performclientsideprediction.\"", stock);
        Assert.Contains("\"cl_predict\",\"1.0\",FCVAR_USERINFO|FCVAR_NOT_CONNECTED,\"Performclientsideprediction.\"", ours);

        const string ratio = "\"cl_interp_ratio\",\"2.0\",FCVAR_USERINFO|FCVAR_NOT_CONNECTED|FCVAR_ARCHIVE,\"Setstheinterpolationamount(finalamountiscl_interp_ratio/cl_updaterate).\"";
        Assert.Contains(ratio, stock);
        Assert.Contains(ratio, ours);

        const string interp = "\"cl_interp\",\"0.1\",FCVAR_USERINFO|FCVAR_NOT_CONNECTED|FCVAR_ARCHIVE,\"Setstheinterpolationamount(boundedonlowsidebyserverinterpratiosettings).\",true,0.0f,true,0.5f";
        Assert.Contains(interp, stock);
        Assert.Contains(interp, ours);
    }
}
