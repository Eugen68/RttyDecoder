using System.Text;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>
/// AFC / PLL. Varianta Android avea semnul erorii inversat (acordul fugea de semnal) si era blocata
/// pe semnal stabil; aceste teste fixeaza comportamentul corect.
/// </summary>
public class AfcTests
{
    private static (string Text, double Mark) Run(short[] pcm, Action<RttySettings> setari)
    {
        using var rx = new RttyReceiver();
        var sb = new StringBuilder();
        rx.TextReceived += t => sb.Append(t);
        rx.Update(setari);
        rx.Start();
        var audio = ToFloat(pcm);
        for (int p = 0; p < audio.Length; p += 2048) rx.Process(audio.AsSpan(p, Math.Min(2048, audio.Length - p)), Fs);
        rx.Stop();
        return (sb.ToString(), rx.Settings.MarkFrequencyHz);
    }

    [Theory]
    [InlineData(12, RttyTracking.Afc)]
    [InlineData(-12, RttyTracking.Afc)]
    [InlineData(25, RttyTracking.Pll)]
    [InlineData(-25, RttyTracking.Pll)]
    public void ConvergePeSemnal(double offset, RttyTracking mod)
    {
        var pcm = Generate(Text + Text, new Options { SnrDb = 10, Seed = 1, OffsetHz = offset });
        var (text, mark) = Run(pcm, s => s.Tracking = mod);
        Assert.InRange(mark, MarkHz + offset - 1.5, MarkHz + offset + 1.5);
        Assert.True(Cer(Text + Text, text) < 0.03, text);
    }

    [Fact]
    public void ConvergeInRev()
    {
        var pcm = Generate(Text + Text, new Options { SnrDb = 10, Seed = 2, OffsetHz = -15, SpaceBelow = true });
        var (text, mark) = Run(pcm, s => { s.Reverse = true; s.Tracking = RttyTracking.Afc; });
        Assert.InRange(mark, MarkHz - 15 - 1.5, MarkHz - 15 + 1.5);
        Assert.True(Cer(Text + Text, text) < 0.03, text);
    }

    /// <summary>Deriva de ~120 Hz pe durata textului (1.6 Hz/s): fara urmarire decodarea se strica.</summary>
    [Fact]
    public void UrmaresteDeriva()
    {
        string text = Text + Text + Text;
        var pcm = Generate(text, new Options { SnrDb = 6, Seed = 3, DriftHzPerSec = 1.6 });
        var fara = Run(pcm, s => s.Tracking = RttyTracking.Off);
        var cu = Run(pcm, s => s.Tracking = RttyTracking.Afc);
        double cerFara = Cer(text, fara.Text), cerCu = Cer(text, cu.Text);
        Assert.True(cerCu < 0.03, $"cu AFC: {cerCu}");
        Assert.True(cerCu < cerFara, $"cu AFC {cerCu} fata de fara {cerFara}");
        double durata = pcm.Length / (double)Fs;
        Assert.InRange(cu.Mark, MarkHz + 1.6 * durata - 5, MarkHz + 1.6 * durata + 1);
    }

    /// <summary>Acord corect pe semnal slab: AFC-ul nu are voie sa rataceasca sau sa strice decodarea.</summary>
    [Fact]
    public void NuRatacestePeSemnalSlab()
    {
        double fara = 0, cu = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            var pcm = Generate(Text, new Options { SnrDb = -3, Seed = seed });
            fara += Cer(Text, Run(pcm, s => s.Tracking = RttyTracking.Off).Text) / 3;
            var r = Run(pcm, s => s.Tracking = RttyTracking.Pll);
            cu += Cer(Text, r.Text) / 3;
            Assert.InRange(r.Mark, MarkHz - 3, MarkHz + 3);
        }
        Assert.True(cu <= fara + 0.01, $"PLL {cu} fata de fara {fara}");
    }

    [Fact]
    public void ZgomotulNuMutaAcordul()
    {
        var pcm = Generate("", new Options { SnrDb = -20, Seed = 4 }); // practic doar zgomot (~1.6 s)
        var lung = new short[pcm.Length * 30];
        for (int k = 0; k < 30; k++) Array.Copy(Generate("", new Options { SnrDb = -20, Seed = 10 + k }), 0, lung, k * pcm.Length, pcm.Length);
        var (_, mark) = Run(lung, s => s.Tracking = RttyTracking.Pll);
        Assert.InRange(mark, MarkHz - 3, MarkHz + 3);
    }
}
