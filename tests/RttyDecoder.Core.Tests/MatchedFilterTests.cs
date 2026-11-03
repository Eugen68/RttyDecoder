using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>
/// Matched Filter imbunatatit (vezi comentariul din RttyDemodulator): fading selectiv si faza
/// instabila (flutter). Varianta Android avea 24-32% CER pe fading si pierdea caractere pe
/// inregistrarile W6IWI.
/// </summary>
public class MatchedFilterTests
{
    private static double Medie(string text, Options o, Action<RttyDecoder.Core.Dsp.RttyDemodulator>? cfg = null)
    {
        double s = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            o.Seed = seed;
            s += Cer(text, DecodeClassic(Generate(text, o), true, cfg)) / 3;
        }
        return s;
    }

    [Theory]
    [InlineData(10.0, 0.2, 0.02)]
    [InlineData(20.0, 1.0, 0.10)]
    public void FadingSelectiv(double snr, double fadingHz, double prag)
    {
        double cer = Medie(Text + Text, new Options { SnrDb = snr, FadingHz = fadingHz });
        Assert.True(cer < prag, $"MF fading {fadingHz} Hz la {snr} dB: CER {cer}");
    }

    /// <summary>Normalizarea nu are voie sa strice semnalele slabe fara fading (unde MF e cel mai bun).</summary>
    [Fact]
    public void SemnalSlabFaraFading()
    {
        double cer = Medie(Text + Text, new Options { SnrDb = -6 });
        Assert.True(cer < 0.02, $"MF la -6 dB: CER {cer}");
    }

    /// <summary>
    /// Flutter (fading complex de 10 Hz, faza se roteste in interiorul bitului): MF trece singur pe
    /// jumatati necoerente. CER-ul ramane mare - limita cunoscuta a MF; Chebyshev e mult mai bun aici.
    /// </summary>
    [Fact]
    public void FlutterTrecePeJumatati()
    {
        string text = Text + Text;
        var pcm = Generate(text, new Options { SnrDb = 10, Seed = 1, FadingHz = 10, FadingComplex = true });
        RttyDecoder.Core.Dsp.RttyDemodulator? d = null;
        DecodeClassic(pcm, true, x => d = x);
        Assert.True(d!.mfNecoerent, $"coerenta {d.coerentaMedie:F3} - trebuia sa treaca pe jumatati");
        Assert.True(Cer(text, DecodeClassic(pcm, false)) < 0.06, "Chebyshev la flutter");
    }

    [Fact]
    public void FazaContinuaRamaneCoerent()
    {
        var pcm = Generate(Text + Text, new Options { SnrDb = -6, Seed = 2 });
        RttyDecoder.Core.Dsp.RttyDemodulator? d = null;
        DecodeClassic(pcm, true, x => d = x);
        Assert.False(d!.mfNecoerent, $"coerenta {d.coerentaMedie:F3}");
    }

    /// <summary>
    /// Regresie (gasita de Eugen, YO9VA): la fiecare corectie AFC/PLL corelatorul glisant pastra in
    /// suma o eroare (esantionul care iesea din fereastra era scazut cu alta referinta de faza decat
    /// cea cu care intrase) care nu mai disparea. Cu AFC-ul care chiar corecteaza, erorile se adunau.
    /// </summary>
    [Theory]
    [InlineData(RttyTracking.Afc, 12.0)]
    [InlineData(RttyTracking.Pll, 5.0)]
    [InlineData(RttyTracking.Pll, -20.0)]
    public void AfcNuStricaCorelatorul(RttyTracking mod, double offset)
    {
        string text = Text + Text + Text + Text;
        var pcm = Generate(text, new Options { SnrDb = 6, Seed = 3, OffsetHz = offset });
        RttyDecoder.Core.Dsp.RttyDemodulator? d = null;
        double cer = Cer(text, DecodeClassic(pcm, true, x => { x.modUrmarireFrecventa = (int)mod; d = x; }));
        Assert.True(d!.EroareCorelatorMf() < 1e-6, $"eroarea sumelor corelatorului: {d.EroareCorelatorMf():E2}");
        Assert.True(cer < 0.01, $"MF + {mod}, acord gresit cu {offset} Hz: CER {cer}");
    }
}
