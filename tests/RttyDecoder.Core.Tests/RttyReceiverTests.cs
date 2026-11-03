using System.Text;
using RttyDecoder.Core.Dsp;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

/// <summary>Receptorul complet, cum il foloseste aplicatia: audio float la rata placii / fisierului.</summary>
public class RttyReceiverTests : IClassFixture<DeepRttyModelFixture>
{
    private readonly DeepRttyModelFixture _fixture;

    public RttyReceiverTests(DeepRttyModelFixture fixture) => _fixture = fixture;

    private static string Run(RttyReceiver rx, float[] audio, int sampleRate, int chunk = 1024)
    {
        var sb = new StringBuilder();
        rx.TextReceived += t => { lock (sb) sb.Append(t); };
        rx.Start();
        for (int pos = 0; pos < audio.Length; pos += chunk)
        {
            int n = Math.Min(chunk, audio.Length - pos);
            rx.Process(audio.AsSpan(pos, n), sampleRate);
        }
        rx.Flush();
        rx.Stop();
        lock (sb) return sb.ToString();
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(11025)]
    [InlineData(8000)]
    public void DecodeazaLaOriceRata(int sampleRate)
    {
        var audio = ToFloat(Generate(Text, new Options { SnrDb = 10, Seed = 2, SampleRate = sampleRate }));
        using var rx = new RttyReceiver();
        string text = Run(rx, audio, sampleRate);
        double cer = Cer(Text, text);
        Assert.True(cer < 0.02, $"{sampleRate} Hz: CER = {cer} ({text})");
    }

    [Theory]
    [InlineData(1450.0, false)]
    [InlineData(915.0, false)]
    [InlineData(2210.0, true)]
    public void AutoTuneGasesteSemnalul(double markHz, bool reverse)
    {
        var audio = ToFloat(Generate(Text, new Options { SnrDb = 10, Seed = 3, MarkHz = markHz, SpaceBelow = reverse }));
        using var rx = new RttyReceiver();
        rx.Update(s => { s.MarkFrequencyHz = 2125; s.Reverse = reverse; });
        rx.Start();
        // ~2 s de semnal, apoi A-TUNE
        rx.Process(audio.AsSpan(0, 96000), 48000);
        double? gasit = rx.AutoTune();
        Assert.NotNull(gasit);
        Assert.InRange(gasit!.Value, markHz - 5, markHz + 5);
        Assert.Equal(gasit.Value, rx.Settings.MarkFrequencyHz, 0.1);
    }

    [Fact]
    public void AutoTuneFaraSemnalNuMutaAcordul()
    {
        using var rx = new RttyReceiver();
        rx.Start();
        rx.Process(new float[96000], 48000);
        Assert.Null(rx.AutoTune());
        Assert.Equal(2125.0, rx.Settings.MarkFrequencyHz, 0.001);
    }

    [Fact]
    public void ToggleReversePastreazaTonurile()
    {
        using var rx = new RttyReceiver();
        rx.ToggleReverse();
        Assert.True(rx.Settings.Reverse);
        Assert.Equal(2295.0, rx.Settings.MarkFrequencyHz, 0.001);
        rx.ToggleReverse();
        Assert.False(rx.Settings.Reverse);
        Assert.Equal(2125.0, rx.Settings.MarkFrequencyHz, 0.001);
    }

    [Fact]
    public void ModificareFaraLegaturaNuMutaAcordul()
    {
        using var rx = new RttyReceiver();
        rx.Update(s => s.MarkFrequencyHz = 1487.5);
        rx.Update(s => s.NoiseBlanker = true);
        Assert.Equal(1487.5, rx.Settings.MarkFrequencyHz, 1e-9);
        Assert.True(rx.Settings.NoiseBlanker);
    }

    [Fact]
    public void DeepRttyPrinReceptor()
    {
        var audio = ToFloat(Generate(Text, new Options { SnrDb = 0, Seed = 4, SampleRate = 44100 }));
        using var rx = new RttyReceiver(_fixture.Model.Run);
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        string text = Run(rx, audio, 44100, chunk: 4410);
        double cer = Cer(Text, text);
        Assert.True(cer < 0.03, $"DeepRTTY prin receptor: CER = {cer} ({text})");
    }

    [Fact]
    public async Task ModelulInclusSeIncarcaPeFundal()
    {
        using var rx = new RttyReceiver();
        var mesaje = new List<string>();
        rx.Message += m => { lock (mesaje) mesaje.Add(m); };
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        await rx.WaitForModelAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(rx.IsDeepRttyReady);
        Assert.Equal(RttyDecoderType.DeepRtty, rx.Settings.Decoder);
    }

    [Fact]
    public async Task ModelIndisponibilRevinePeChebyshev()
    {
        using var rx = new RttyReceiver(modelFactory: () => throw new FileNotFoundException("lipsa"));
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        await rx.WaitForModelAsync().WaitAsync(TimeSpan.FromSeconds(10));
        for (int i = 0; i < 100 && rx.Settings.Decoder != RttyDecoderType.Chebyshev; i++) await Task.Delay(20);
        Assert.Equal(RttyDecoderType.Chebyshev, rx.Settings.Decoder);
    }
}

public class ResamplerTests
{
    [Theory]
    [InlineData(44100)]
    [InlineData(8000)]
    [InlineData(96000)]
    public void SinusulRamaneSinus(int inRate)
    {
        const double f = 1500;
        var input = new float[inRate];
        for (int i = 0; i < input.Length; i++) input[i] = (float)(0.5 * Math.Sin(2 * Math.PI * f * i / inRate));
        var r = new Resampler(inRate, 48000);
        var output = new float[r.MaxOutput(input.Length)];
        int n = 0;
        for (int pos = 0; pos < input.Length; pos += 1000)
            n += r.Process(input.AsSpan(pos, Math.Min(1000, input.Length - pos)), output.AsSpan(n));
        Assert.InRange(n, 48000 - 200, 48000 + 2);

        // Comparam cu sinusul ideal (fara intarziere: reesantionatorul e centrat), departe de capete.
        double err = 0, sig = 0;
        for (int i = 2000; i < n - 2000; i++)
        {
            double ideal = 0.5 * Math.Sin(2 * Math.PI * f * i / 48000);
            err += (output[i] - ideal) * (output[i] - ideal);
            sig += ideal * ideal;
        }
        double snr = 10 * Math.Log10(sig / err);
        Assert.True(snr > 60, $"{inRate} -> 48000: SNR = {snr:F1} dB");
    }
}
