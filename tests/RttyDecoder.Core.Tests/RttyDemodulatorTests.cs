using Xunit.Abstractions;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>
/// Bancul de test al demodulatorului clasic - port al RttyDemodulatorTest.java, cu aceleasi semnale
/// (acelasi generator si aceleasi seed-uri) si aceleasi praguri.
/// </summary>
public class RttyDemodulatorTests
{
    private readonly ITestOutputHelper _out;

    public RttyDemodulatorTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void SemnalCuratChebyshev()
    {
        double cer = Cer(Text, Decode(Text, double.PositiveInfinity, false, 1));
        Assert.True(cer < 0.02, $"CER Chebyshev pe semnal curat = {cer}");
    }

    [Fact]
    public void SemnalCuratMatchedFilter()
    {
        double cer = Cer(Text, Decode(Text, double.PositiveInfinity, true, 1));
        Assert.True(cer < 0.02, $"CER Matched Filter pe semnal curat = {cer}");
    }

    [Fact]
    public void SemnalPuternicCuZgomot()
    {
        double cer = Cer(Text, Decode(Text, 20.0, false, 1));
        Assert.True(cer < 0.05, $"CER Chebyshev la +20 dB = {cer}");
    }

    [Fact]
    public void SemnalSlabChebyshev()
    {
        double cer = 0;
        for (int seed = 1; seed <= 3; seed++) cer += Cer(Text, Decode(Text, -3.0, false, seed)) / 3;
        Assert.True(cer < 0.05, $"CER Chebyshev la -3 dB = {cer}");
    }

    [Fact]
    public void SemnalSlabMatchedFilter()
    {
        double cer = 0;
        for (int seed = 1; seed <= 3; seed++) cer += Cer(Text, Decode(Text, -3.0, true, seed)) / 3;
        Assert.True(cer < 0.05, $"CER Matched Filter la -3 dB = {cer}");
    }

    [Theory]
    [InlineData(15, Baud)]
    [InlineData(-15, Baud)]
    [InlineData(0, 45.0)]
    [InlineData(0, 45.9)]
    public void AcordSiVitezaImperfecte(double offsetHz, double baud)
    {
        var audio = Generate(Text, new Options { SnrDb = 0.0, Seed = 1, OffsetHz = offsetHz, Baud = baud });
        foreach (bool mf in new[] { false, true })
        {
            double cer = Cer(Text, DecodeClassic(audio, mf));
            Assert.True(cer < 0.05, $"CER {(mf ? "MF" : "Chebyshev")} offset {offsetHz:+0;-0} Hz baud {baud:F2} = {cer:F3}");
        }
    }

    [Fact]
    public void FadingSelectivCuAtc()
    {
        string text = Text + Text + Text;
        double faraAtc = 0, cuAtc = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            var a = Generate(text, new Options { SnrDb = 10.0, Seed = seed, FadingHz = 0.2 });
            faraAtc += Cer(text, DecodeClassic(a, false)) / 3;
            cuAtc += Cer(text, DecodeClassic(a, false, d => d.folosesteAtc = true)) / 3;
        }
        Assert.True(cuAtc < 0.03, $"CER fading 0.2 Hz cu ATC = {cuAtc}");
        Assert.True(cuAtc < faraAtc, $"ATC ({cuAtc}) trebuie sa bata decizia simpla ({faraAtc})");
    }

    [Fact]
    public void FadingRapidCuAtc()
    {
        string text = Text + Text + Text;
        double cer = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            var a = Generate(text, new Options { SnrDb = 20.0, Seed = seed, FadingHz = 1.0 });
            cer += Cer(text, DecodeClassic(a, false, d => d.folosesteAtc = true)) / 3;
        }
        Assert.True(cer < 0.10, $"CER fading 1 Hz cu ATC = {cer}");
    }

    [Fact]
    public void VolantFluxContinuuSlab()
    {
        string text = Text + Text;
        double fara = 0, cu = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            var a = Generate(text, new Options { SnrDb = -6.0, Seed = seed });
            fara += Cer(text, DecodeClassic(a, false, d => d.folosesteVolant = false)) / 3;
            cu += Cer(text, DecodeClassic(a, false)) / 3;
        }
        Assert.True(cu < 0.7 * fara, $"volant ({cu}) fata de detectia libera ({fara})");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VolantNuStricaPauzele(bool tastare)
    {
        string text = Text + Text;
        for (int seed = 1; seed <= 3; seed++)
        {
            var a = Generate(text, new Options { SnrDb = 10.0, Seed = seed, TypingPauses = tastare, BurstPauses = !tastare });
            double cer = Cer(text, DecodeClassic(a, false));
            Assert.True(cer < 0.005, $"{(tastare ? "tastare" : "rafale")} seed {seed} CER = {cer}");
        }
    }

    [Fact]
    public void ReverseSpaceSub()
    {
        var a = Generate(Text, new Options { SnrDb = 10.0, Seed = 1, SpaceBelow = true });
        double cer = Cer(Text, DecodeClassic(a, false, d => d.regimReverseSoftware = true));
        Assert.True(cer < 0.02, $"CER REV = {cer}");
    }

    /// <summary>Baleiaj de SNR: afiseaza CER-ul (medie pe 3 seed-uri) pentru ambii detectori.</summary>
    [Fact]
    public void RaportSnr()
    {
        double[] snrs = { 20, 10, 6, 3, 0, -3, -6 };
        var sb = new System.Text.StringBuilder("SNR (dB/2.7kHz) | CER Chebyshev | CER Matched Filter\n");
        foreach (double snr in snrs)
        {
            double cheb = 0, mf = 0;
            for (int seed = 1; seed <= 3; seed++)
            {
                cheb += Cer(Text, Decode(Text, snr, false, seed));
                mf += Cer(Text, Decode(Text, snr, true, seed));
            }
            sb.Append($"{snr,15:0} | {cheb / 3 * 100,12:0.0}% | {mf / 3 * 100,17:0.0}%\n");
        }
        _out.WriteLine(sb.ToString());
    }
}
