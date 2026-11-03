using System.Globalization;
using RttyDecoder.Core.Dsp;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>
/// Paritate cu aplicatia Android: Golden/android_parity.txt e iesirea codului Java original
/// (RttyDemodulator.java din Yaesu-FTX1-CAT-Control, rulat cu tools/android-parity/Parity.java) pe
/// aceleasi semnale sintetice. Portul C# trebuie sa dea EXACT acelasi text, inclusiv erorile de pe
/// semnalele slabe - orice diferenta inseamna ca algoritmul s-a schimbat.
/// </summary>
public class AndroidParityTests
{
    private static IEnumerable<string> GenereazaCazuri()
    {
        static string J(double d) => double.IsPositiveInfinity(d) ? "Infinity" : d.ToString("0.0", CultureInfo.InvariantCulture);
        static string E(string s) => s.Replace("\n", "\\n");
        foreach (double snr in new[] { double.PositiveInfinity, 10, 0, -3, -6, -9 })
            for (int seed = 1; seed <= 2; seed++)
                for (int mf = 0; mf < 2; mf++)
                    yield return $"CASE snr={J(snr)} seed={seed} mf={mf} |{E(Decode(Text, snr, mf == 1, seed))}|";

        var f = Generate(Text + Text, new Options { SnrDb = 10, Seed = 1, FadingHz = 0.2 });
        yield return "CASE atc |" + E(DecodeClassic(f, false, d => d.folosesteAtc = true)) + "|";

        var g = Generate(Text, new Options { SnrDb = -3, Seed = 2 });
        yield return "CASE nbdnr |" + E(DecodeClassic(g, false, d =>
        {
            d.folosesteNoiseBlanker = true;
            d.folosesteDnr = true;
            d.folosesteSnrGating = true;
            d.snrGatingK = 1.3;
        })) + "|";

        var h = Generate(Text + Text, new Options { SnrDb = 20, Seed = 1, OffsetHz = 12 });
        var dm = new RttyDemodulator { modUrmarireFrecventa = 2 };
        dm.Start(48000);
        var buf = new short[3840];
        for (int p = 0; p < h.Length; p += 3840)
        {
            int n = Math.Min(3840, h.Length - p);
            Array.Copy(h, p, buf, 0, n);
            dm.ProcessBlock(buf, n);
        }
        yield return "AFC mark=" + dm.MarkFrequencyHz.ToString("0.0###", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void IdenticCuJava()
    {
        string cale = Path.Combine(AppContext.BaseDirectory, "Golden", "android_parity.txt");
        var asteptat = File.ReadAllLines(cale);
        var obtinut = GenereazaCazuri().ToArray();
        Assert.Equal(asteptat.Length, obtinut.Length);
        for (int i = 0; i < asteptat.Length; i++) Assert.Equal(asteptat[i], obtinut[i]);
    }
}
