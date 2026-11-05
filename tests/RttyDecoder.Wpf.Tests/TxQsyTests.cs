using HamRig;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

/// <summary>
/// QSY-ul de la dublu click pe A-TUNE: perechea semnalului ajunge pe 2125 / 2295. Cazurile din testul
/// pe statii (YO9VA, 2026-11-05): FTDX10 receptioneaza pe 3.505.000, FTX-1 emite pe 3.505.200.
/// Inainte, in RTTY-U, Mark-ul (tonul de sus) era dus pe 2125 si QSY-ul iesea 3.505.370 (+170 Hz).
/// </summary>
public class TxQsyTests
{
    [Fact]
    public void RttyLPerecheaCuTonulDeJosPe2125()
    {
        // RTTY-L ca LSB: statia cu 200 Hz mai sus se aude cu 200 Hz mai jos - tonul de jos (Mark) pe 1925
        Assert.Equal(3_505_200, RttyDecoderViewModel.TxDial(3_505_000, RigMode.RttyL, 1925));
    }

    [Fact]
    public void RttyUPerecheaCuTonulDeJosPe2125()
    {
        // RTTY-U ca USB: tonul de jos (Space) pe 2325, Mark-ul (REV) pe 2495
        Assert.Equal(3_505_200, RttyDecoderViewModel.TxDial(3_505_000, RigMode.RttyU, 2325));
        // greseala veche: Mark-ul pe 2125 -> 3.505.370, exact ce s-a vazut pe statie
        Assert.Equal(3_505_370, RigMath.QsyDial(3_505_000, RigMode.RttyU, 2495, RttyDecoderViewModel.FskMarkHz));
    }

    [Fact]
    public void PeAceeasiFrecventaNuSeMuta()
    {
        Assert.Equal(3_505_000, RttyDecoderViewModel.TxDial(3_505_000, RigMode.RttyL, 2125));
        Assert.Equal(3_505_000, RttyDecoderViewModel.TxDial(3_505_000, RigMode.RttyU, 2125));
    }

    [Fact]
    public void MarkulDupaQsy()
    {
        Assert.Equal(2125, RttyDecoderViewModel.MarkAfterQsy(reverse: false));
        Assert.Equal(2295, RttyDecoderViewModel.MarkAfterQsy(reverse: true));
    }
}
