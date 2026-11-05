using HamRig;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

/// <summary>Frecventa RF la cursorul waterfall-ului (RttyDecoderViewModel.AudioToRf).</summary>
public class WaterfallRfTests
{
    [Fact]
    public void InRttyPeMarkArataFrecventaCorespondentului()
    {
        // testul pe statii: FTDX10 pe 3.505.000, FTX-1 pe 3.505.200
        // RTTY-L: Mark-ul FTX-1 se aude pe 1925 (ca LSB)
        Assert.Equal(3_505_200, RttyDecoderViewModel.AudioToRf(3_505_000, RigMode.RttyL, 1925));
        // RTTY-U: Mark-ul (tonul de sus, REV) pe 2495 (ca USB)
        Assert.Equal(3_505_200, RttyDecoderViewModel.AudioToRf(3_505_000, RigMode.RttyU, 2495));
        // o statie de pe aceeasi frecventa
        Assert.Equal(3_505_000, RttyDecoderViewModel.AudioToRf(3_505_000, RigMode.RttyL, 2125));
        Assert.Equal(3_505_000, RttyDecoderViewModel.AudioToRf(3_505_000, RigMode.RttyU, 2295));
    }

    [Fact]
    public void InSsbCaRigMath()
    {
        Assert.Equal(14_085_000 + 1500, RttyDecoderViewModel.AudioToRf(14_085_000, RigMode.Usb, 1500));
        Assert.Equal(7_043_000 - 1500, RttyDecoderViewModel.AudioToRf(7_043_000, RigMode.Lsb, 1500));
        Assert.Null(RttyDecoderViewModel.AudioToRf(0, RigMode.Usb, 1500));
        Assert.Null(RttyDecoderViewModel.AudioToRf(14_085_000, RigMode.Fm, 1500));
    }
}
