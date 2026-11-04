using RttyDecoder.Core.Dsp;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

public class RttyPolarityTests
{
    private static float[] Audio(bool spaceBelow, double snr, int fs, long seed = 3, double markHz = 2125, bool typing = false)
    {
        var pcm = Generate(Text, new Options
        {
            SnrDb = snr, Seed = seed, SampleRate = fs, SpaceBelow = spaceBelow, MarkHz = markHz, TypingPauses = typing,
        });
        // ultimele ~5 s, ca la A-TUNE
        int n = Math.Min(pcm.Length, 5 * fs);
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = pcm[pcm.Length - n + i] / 32768f;
        return a;
    }

    [Theory]
    [InlineData(11025, 20.0)]
    [InlineData(48000, 20.0)]
    [InlineData(11025, 6.0)]
    public void MarkulEsteTonulDeJos(int fs, double snr)
    {
        // NORM: Mark 2125, Space 2295 (deasupra)
        var r = RttyPolarity.Detect(Audio(false, snr, fs), fs, 2125, 2295);
        Assert.True(r.MarkIsLow, $"scoruri {r.ScoreLow:0.00} / {r.ScoreHigh:0.00}, {r.Frames} caractere");
        Assert.True(r.ScoreLow > 0.8, r.ToString());
    }

    [Theory]
    [InlineData(11025, 20.0)]
    [InlineData(48000, 6.0)]
    public void MarkulEsteTonulDeSus(int fs, double snr)
    {
        // REV: Mark 2295, Space 2125 (dedesubt) - aceeasi pereche de tonuri, polaritate inversa
        var r = RttyPolarity.Detect(Audio(true, snr, fs, markHz: 2295), fs, 2125, 2295);
        Assert.False(r.MarkIsLow, $"scoruri {r.ScoreLow:0.00} / {r.ScoreHigh:0.00}, {r.Frames} caractere");
    }

    [Fact]
    public void CuPauzeDeTastare()
    {
        var r = RttyPolarity.Detect(Audio(false, 12, 11025, seed: 5, typing: true), 11025, 2125, 2295);
        Assert.True(r.MarkIsLow, r.ToString());
    }

    [Fact]
    public void ZgomotulNuDecide()
    {
        var rnd = new Random(1);
        var a = new float[5 * 11025];
        for (int i = 0; i < a.Length; i++) a[i] = (float)(0.1 * (rnd.NextDouble() * 2 - 1));
        Assert.Null(RttyPolarity.Detect(a, 11025, 2125, 2295).MarkIsLow);
        Assert.Null(RttyPolarity.Detect(new float[100], 11025, 2125, 2295).MarkIsLow);   // prea scurt
    }
}
