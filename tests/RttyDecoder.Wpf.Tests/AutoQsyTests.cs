using HamRig;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

/// <summary>
/// QSY-ul automat inainte de raspuns (RttyTxSettings.AutoQsy): macro/text care nu incepe cu CQ, dupa
/// un A-TUNE care a calculat alta frecventa decat a statiei, statia in RTTY-L / RTTY-U.
/// </summary>
public class AutoQsyTests
{
    [Fact]
    public void DupaATuneMutaStatia()
    {
        // testul pe statii: FTDX10 pe 3.505.000, A-TUNE a calculat 3.505.200 (FTX-1)
        Assert.Equal(3_505_200, RttyDecoderViewModel.AutoQsyPlan(3_505_200, 3_505_000, 3_505_000, RigMode.RttyL));
        Assert.Equal(3_505_200, RttyDecoderViewModel.AutoQsyPlan(3_505_200, 3_505_000, 3_505_000, RigMode.RttyU));
    }

    [Fact]
    public void FaraATuneSauStatiaMutataNuMuta()
    {
        Assert.Null(RttyDecoderViewModel.AutoQsyPlan(null, 0, 3_505_000, RigMode.RttyL));                // fara A-TUNE
        Assert.Null(RttyDecoderViewModel.AutoQsyPlan(3_505_200, 3_505_000, 3_506_000, RigMode.RttyL));   // statia s-a mutat
        Assert.Null(RttyDecoderViewModel.AutoQsyPlan(3_505_000, 3_505_000, 3_505_000, RigMode.RttyL));   // deja pe frecventa
    }

    [Fact]
    public void DoarInRtty()
    {
        Assert.Null(RttyDecoderViewModel.AutoQsyPlan(14_085_200, 14_085_000, 14_085_000, RigMode.Usb));
        Assert.Null(RttyDecoderViewModel.AutoQsyPlan(14_085_200, 14_085_000, 14_085_000, RigMode.DataU));
    }

    [Theory]
    [InlineData("CQ CQ CQ DE YO9VA YO9VA K", true)]
    [InlineData("  cq test yo9va", true)]
    [InlineData("CQ", true)]
    [InlineData("YO9ABC DE YO9VA UR 599 K", false)]
    [InlineData("QRZ? DE YO9VA K", false)]
    [InlineData("CQD", false)]                       // nu e cuvantul CQ
    [InlineData("TNX FER CQ, 73", false)]            // CQ nu e la inceput
    public void IncepeCuCq(string text, bool cq)
    {
        Assert.Equal(cq, RttyDecoderViewModel.IsCq(text));
    }
}

/// <summary>Dublu click pe A-TUNE: calculul afisat inainte, nu cel refacut de primul click.</summary>
public class AtuneDoubleClickTests
{
    private static readonly TimeSpan Dbl = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void FolosesteCalcululAfisat()
    {
        // testul pe statii: calcul 3.501.795, emisia s-a oprit, primul click a recalculat 3.501.861
        var t = RttyDecoderViewModel.DoubleClickTarget((3_501_795, 3_501_500, true), TimeSpan.FromMilliseconds(200), 3_501_500, Dbl);
        Assert.Equal((3_501_795L, 3_501_500L, true), t);
    }

    [Fact]
    public void FaraCalculInainteSeFolosesteCelNou()
    {
        Assert.Null(RttyDecoderViewModel.DoubleClickTarget(null, TimeSpan.FromMilliseconds(200), 3_501_500, Dbl));
    }

    [Fact]
    public void StatiaMutataSauClickVechi()
    {
        Assert.Null(RttyDecoderViewModel.DoubleClickTarget((3_501_795, 3_501_500, false), TimeSpan.FromMilliseconds(200), 3_502_000, Dbl));
        Assert.Null(RttyDecoderViewModel.DoubleClickTarget((3_501_795, 3_501_500, false), TimeSpan.FromSeconds(3), 3_501_500, Dbl));
    }
}
