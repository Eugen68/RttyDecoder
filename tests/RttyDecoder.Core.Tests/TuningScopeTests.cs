using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Core.Tests;

public class TuningScopeTests
{
    private const int Rate = 11025;

    private static (double Mark, double Space, double PhaseDeg) Measure(double toneHz, double markHz = 2125, double spaceHz = 2295)
    {
        var scope = new TuningScope();
        scope.SetTones(markHz, spaceHz);
        var x = new float[Rate / 2];
        for (int i = 0; i < x.Length; i++) x[i] = (float)(0.3 * Math.Sin(2 * Math.PI * toneHz * i / Rate));
        scope.Process(x, Rate);
        var pts = new TuningPoint[4096];
        int n = scope.Read(pts);
        Assert.True(n > 200, $"{n} puncte");
        var p = pts[n - 1];
        double m = Math.Sqrt(p.MarkRe * p.MarkRe + p.MarkIm * p.MarkIm);
        double s = Math.Sqrt(p.SpaceRe * p.SpaceRe + p.SpaceIm * p.SpaceIm);
        double ph = (Math.Atan2(p.SpaceIm, p.SpaceRe) - Math.Atan2(p.MarkIm, p.MarkRe)) * 180 / Math.PI;
        ph = ((ph + 540) % 360) - 180;
        return (m, s, ph);
    }

    [Fact]
    public void TonMarkElipsaCulcata()
    {
        var (m, s, ph) = Measure(2125);
        Assert.InRange(m, 0.95, 1.01);           // normalizat la varf
        Assert.InRange(s, 0.15, 0.3);            // ~ -13 dB: elipsa subtire
        Assert.InRange(Math.Abs(ph), 65, 90);    // aproape perpendiculara
    }

    [Fact]
    public void TonSpaceElipsaInPicioare()
    {
        var (m, s, ph) = Measure(2295);
        Assert.InRange(s, 0.95, 1.01);
        Assert.InRange(m, 0.15, 0.3);
        Assert.InRange(Math.Abs(ph), 65, 90);
    }

    [Fact]
    public void DezacordatElipsaSeIngroasaSiSeInclina()
    {
        var acordat = Measure(2125);
        var dezacordat = Measure(2125 + 50);     // 50 Hz spre Space
        Assert.True(dezacordat.Space / dezacordat.Mark > 2 * acordat.Space / acordat.Mark);
        // inclinarea: faza relativa se departeaza de 90 de grade (elipsa nu mai e pe axe)
        Assert.True(Math.Abs(Math.Abs(dezacordat.PhaseDeg) - 90) > Math.Abs(Math.Abs(acordat.PhaseDeg) - 90) + 15);
    }

    [Fact]
    public void ReadDoarPuncteleNoi()
    {
        var scope = new TuningScope();
        scope.Process(new float[Rate / 10], Rate);
        var pts = new TuningPoint[4096];
        Assert.InRange(scope.Read(pts), 45, 55);  // 100 ms / 2 ms
        Assert.Equal(0, scope.Read(pts));
    }
}
