using System.Text;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

public class SquelchTests
{
    private static string Decode(float[] audio, int fs, bool squelch)
    {
        using var rx = new RttyReceiver();
        rx.Update(s =>
        {
            s.MarkFrequencyHz = 2125;
            s.Squelch = squelch;
        });
        var sb = new StringBuilder();
        rx.TextReceived += t => { lock (sb) sb.Append(t); };
        rx.Start();
        for (int pos = 0; pos < audio.Length; pos += 1024) rx.Process(audio.AsSpan(pos, Math.Min(1024, audio.Length - pos)), fs);
        rx.Flush();
        rx.Stop();
        lock (sb) return sb.ToString();
    }

    /// <summary>
    /// Zgomot de banda ca dupa filtrul RTTY al statiei (~500 Hz in jurul tonurilor, 2210 Hz): tot
    /// zgomotul cade in banda decodorului, ca in eter - pe asta apare gunoiul pe ecran.
    /// </summary>
    private static float[] Noise(int n, int seed, double amp = 0.1, int fs = 11025)
    {
        var rnd = new Random(seed);
        // trece-banda biquad (RBJ), centru 2210 Hz, Q ~ 4
        double w0 = 2 * Math.PI * 2210 / fs, alpha = Math.Sin(w0) / (2 * 4.0);
        double b0 = alpha, b2 = -alpha, a0 = 1 + alpha, a1 = -2 * Math.Cos(w0), a2 = 1 - alpha;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        var a = new float[n];
        double sum = 0;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = Math.Sqrt(-2 * Math.Log(1 - rnd.NextDouble())) * Math.Cos(2 * Math.PI * rnd.NextDouble());
            double v = (b0 * x + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x; y2 = y1; y1 = v;
            y[i] = v;
            sum += v * v;
        }
        double rms = Math.Sqrt(sum / n);
        for (int i = 0; i < n; i++) a[i] = (float)(amp * y[i] / rms);
        return a;
    }

    private static float[] Signal(double snr, long seed, int fs)
    {
        var pcm = Generate(Text, new Options { SnrDb = snr, Seed = seed, SampleRate = fs });
        return pcm.Select(v => v / 32768f).ToArray();
    }

    private static int Visible(string s) => s.Count(c => !char.IsWhiteSpace(c));

    [Fact]
    public void ZgomotulNuMaiUmpleEcranul()
    {
        const int fs = 11025;
        var noise = Noise(30 * fs, 1);
        int fara = Visible(Decode(noise, fs, squelch: false));
        int cu = Visible(Decode(noise, fs, squelch: true));
        Assert.True(fara > 40, $"fara SQL: {fara} caractere (testul presupune ca zgomotul da gunoi)");
        Assert.True(cu <= fara / 10, $"cu SQL: {cu} caractere din {fara}");
    }

    [Fact]
    public void InceputulMesajuluiNuSePierde()
    {
        // 10 s de zgomot, apoi mesajul: SQL se deschide si scrie si inceputul ("CQ CQ CQ")
        const int fs = 11025;
        var sig = Signal(15, 4, fs);
        var noise = Noise(10 * fs, 2, 0.03);
        var audio = noise.Concat(sig).ToArray();
        string text = Decode(audio, fs, squelch: true);
        Assert.Contains("CQ CQ CQ DE YO9VA", text);
        Assert.Contains("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG", text);
        // gunoiul de dinaintea mesajului: aproape nimic
        int inainte = text.IndexOf("CQ CQ CQ", StringComparison.Ordinal);
        Assert.True(Visible(text[..inainte]) <= 5, $"inainte de mesaj: '{text[..inainte]}'");
    }

    [Theory]
    [InlineData(6.0)]
    [InlineData(3.0)]
    public void SemnalulSlabNuSePierde(double snr)
    {
        const int fs = 11025;
        var audio = Signal(snr, 7, fs);
        double fara = Cer(Text, Decode(audio, fs, squelch: false));
        double cu = Cer(Text, Decode(audio, fs, squelch: true));
        Assert.True(cu <= fara + 0.03, $"SNR {snr} dB: CER fara SQL {fara:0.000}, cu SQL {cu:0.000}");
    }

    [Fact]
    public void TineSiElibereazaSirulInRitm()
    {
        var iesit = new List<int>();
        var sq = new TextSquelch(iesit.Add);
        var rnd = new Random(3);
        double t = 0;
        // zgomot: caractere "valide" la intervale intamplatoare - ramane inchis, nimic nu iese
        for (int i = 0; i < 60; i++)
        {
            t += 9 + rnd.NextDouble() * 30;
            sq.Frame(true, 1, t);
        }
        Assert.False(sq.IsOpen);
        Assert.Empty(iesit);
        // semnal: caractere la 7.5 biti - se deschide si le scrie pe toate, de la primul
        t += 50;
        for (int i = 0; i < 10; i++)
        {
            sq.Frame(true, 100 + i, t);
            t += 7.5;
        }
        Assert.True(sq.IsOpen);
        Assert.Equal(Enumerable.Range(100, 10), iesit);
        // o pauza de tastare nu-l inchide; caracterul de dupa pauza iese odata cu urmatorul in ritm
        t += 40;
        sq.Frame(true, 200, t);
        Assert.True(sq.IsOpen);
        t += 7.5;
        sq.Frame(true, 201, t);
        Assert.Equal(new[] { 200, 201 }, iesit.Skip(10));
        // dupa semnal, zgomot: se inchide dupa cateva caractere
        for (int i = 0; i < 16; i++)
        {
            t += 9 + rnd.NextDouble() * 30;
            sq.Frame(true, 9, t);
        }
        Assert.False(sq.IsOpen);
        Assert.True(iesit.Count(c => c == 9) <= 8, $"{iesit.Count(c => c == 9)} caractere de zgomot dupa semnal");
        // oprit: tot ce e valid trece
        var tot = new List<int>();
        var off = new TextSquelch(tot.Add) { Enabled = false };
        off.Frame(true, 5, 0);
        off.Frame(false, 6, 7.5);
        Assert.Equal(new[] { 5 }, tot);
    }

    [Fact]
    public void CuPauzeDeTastareTextulIese()
    {
        const int fs = 11025;
        var pcm = Generate(Text, new Options { SnrDb = 10, Seed = 7, SampleRate = fs, TypingPauses = true });
        var audio = pcm.Select(v => v / 32768f).ToArray();
        double fara = Cer(Text, Decode(audio, fs, squelch: false));
        double cu = Cer(Text, Decode(audio, fs, squelch: true));
        Assert.True(cu <= fara + 0.03, $"CER fara SQL {fara:0.000}, cu SQL {cu:0.000}");
    }
}
