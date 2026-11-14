using SourceSharp.Abi;

namespace TF2.Descent.Client.Ui;

/// <summary>The vendor's portrait (<see cref="IDescentVendorPortrait"/>): not drawn. Live when the engine renders the vendor's face to a surface.</summary>
[SideService(Side.Client, As = [typeof(IDescentVendorPortrait)])]
public sealed class VendorPortraitSource : IDescentVendorPortrait
{
    public bool Active => false;

    public int Texture => 0;
}
