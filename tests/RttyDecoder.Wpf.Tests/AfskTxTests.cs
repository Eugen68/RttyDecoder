using HamRig;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

/// <summary>AFSK: tonurile = perechea decodorului, polaritatea standard din modul statiei.</summary>
public class AfskTxTests
{
    [Theory]
    // DATA-U / USB: Mark = tonul de sus (frecventa RF mai mare), indiferent de NORM/REV
    [InlineData(1000, false, RigMode.DataU, false, 1170, 1000)]
    [InlineData(1170, true, RigMode.Usb, false, 1170, 1000)]
    // DATA-L / LSB: Mark = tonul de jos
    [InlineData(2125, false, RigMode.DataL, false, 2125, 2295)]
    [InlineData(2295, true, RigMode.Lsb, false, 2125, 2295)]
    // inversat
    [InlineData(1000, false, RigMode.DataU, true, 1000, 1170)]
    // mod necunoscut: ca la receptie
    [InlineData(1500, false, RigMode.Unknown, false, 1500, 1670)]
    [InlineData(1670, true, RigMode.Unknown, false, 1670, 1500)]
    public void TonuriSiPolaritate(double mark, bool reverse, RigMode mode, bool invert, double txMark, double txSpace)
    {
        var t = RttyDecoderViewModel.AfskTonesFor(mark, reverse, mode, invert);
        Assert.Equal(txMark, t.MarkHz, 3);
        Assert.Equal(txSpace, t.SpaceHz, 3);
    }

    [Theory]
    [InlineData(RigMode.DataU, true)]
    [InlineData(RigMode.Usb, true)]
    [InlineData(RigMode.DataL, true)]
    [InlineData(RigMode.Lsb, true)]
    [InlineData(RigMode.Unknown, true)]
    [InlineData(RigMode.RttyL, false)]   // in RTTY statia nu ia tonurile de la placa
    [InlineData(RigMode.RttyU, false)]
    [InlineData(RigMode.CwU, false)]
    [InlineData(RigMode.Fm, false)]
    public void ModuriPermise(RigMode mode, bool ok)
    {
        Assert.Equal(ok, RttyDecoderViewModel.IsAfskMode(mode));
    }
}
