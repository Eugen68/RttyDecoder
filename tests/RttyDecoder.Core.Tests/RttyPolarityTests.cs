using RttyDecoder.Core.Dsp;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

public class RttyPolarityTests
{
    private const string RyText =
        "RYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRY\n" +
        "RYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRYRY\n";

    private static float[] Audio(bool spaceBelow, double snr, int fs, long seed = 3, double markHz = 2125, bool typing = false,
                                 string text = Text)
    {
        var pcm = Generate(text, new Options
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

    /// <summary>
    /// RYRY (biti alternanti) se incadreaza aproape la fel de bine si cu polaritatea inversata - pe
    /// inregistrarea reala W6IWI 14089 incadrarea singura gresea ~6% din decizii (testul lui Eugen in
    /// Android, 7ff7b6b). Duratele (stopul de 1.5 biti e pe Mark) decid corect.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ryry(bool rev)
    {
        foreach (double snr in new[] { double.PositiveInfinity, 10.0, 6.0 })
            for (long seed = 1; seed <= 3; seed++)
            {
                const int fs = 48000;
                var r = RttyPolarity.Detect(Audio(rev, snr, fs, seed, rev ? 2295 : 2125, text: RyText), fs, 2125, 2295);
                Assert.True(r.MarkIsLow == !rev,
                    $"{(rev ? "REV" : "NORM")} SNR {snr} seed {seed}: {r}");
            }
    }

    [Fact]
    public void ZgomotulNuDecide()
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            var rnd = new Random(seed);
            var a = new float[5 * 11025];
            for (int i = 0; i < a.Length; i++) a[i] = (float)(0.1 * (rnd.NextDouble() * 2 - 1));
            Assert.True(RttyPolarity.Detect(a, 11025, 2125, 2295).MarkIsLow == null, $"seed {seed}");
        }
        Assert.Null(RttyPolarity.Detect(new float[100], 11025, 2125, 2295).MarkIsLow);   // prea scurt
    }
}
