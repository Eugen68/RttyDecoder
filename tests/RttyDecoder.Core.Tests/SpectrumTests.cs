using RttyDecoder.Core.Dsp;

namespace RttyDecoder.Core.Tests;

public class SpectrumTests
{
    private static short[] Tone(int count, double frequency, double amplitude = .5) =>
        Enumerable.Range(0, count).Select(i => (short)Math.Round(32767 * amplitude * Math.Sin(2 * Math.PI * frequency * i / 48000))).ToArray();

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    public void TonulAreNivelDbfsSiTimpAudioCorect(int size)
    {
        var spectrum = new SpectrumAnalyzer(size);
        var frames = new List<SpectrumFrame>();
        spectrum.FrameAvailable += frames.Add;
        var pcm = Tone(48000, 1000);
        spectrum.Process(pcm, pcm.Length);
        var last = frames[^1];
        Assert.InRange(SpectralHistory.SampleDb(last, 1000), -6.04, -6.00);
        Assert.Equal(8000.0 / size, last.BinHz);
        Assert.True(frames[0].AudioSeconds >= size / 8000.0);
        Assert.Equal(.032, frames[1].AudioSeconds - frames[0].AudioSeconds, 8);
        Assert.InRange(last.AudioSeconds, .968, 1);
    }

    [Fact]
    public void ModificareaFftPastreazaTimpulSiResetCreeazaEpocaNoua()
    {
        var spectrum = new SpectrumAnalyzer();
        SpectrumFrame? last = null;
        spectrum.FrameAvailable += row => last = row;
        var pcm = Tone(48000, 1000);
        spectrum.Process(pcm, pcm.Length);
        var old = last!;
        spectrum.SetSize(1024);
        spectrum.Process(pcm, pcm.Length);
        Assert.Equal(old.Epoch, last!.Epoch);
        Assert.InRange(last.AudioSeconds, 1.968, 2);
        Assert.Equal(7.8125, last.BinHz);
        spectrum.Reset();
        spectrum.Process(pcm, pcm.Length);
        Assert.NotEqual(old.Epoch, last.Epoch);
        Assert.InRange(last.AudioSeconds, .968, 1);
    }

    [Theory]
    [InlineData(512, 915.0)]
    [InlineData(1024, 1450.0)]
    public void AtuneIdentificaPerecheaLaOriceRezolutie(int size, double lower)
    {
        var spectrum = new SpectrumAnalyzer(size);
        var low = Tone(96000, lower, .2);
        var high = Tone(96000, lower + 170, .15);
        var pcm = low.Zip(high, (a, b) => (short)(a + b)).ToArray();
        spectrum.Process(pcm, pcm.Length);
        var candidate = AutoTune.FindCandidate(spectrum.AverageSpectrum()!, spectrum.BinWidthHz);
        Assert.NotNull(candidate);
        Assert.InRange(candidate.Value.LowerToneHz, lower - 3, lower + 3);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    public void AtuneRespingePurtatoareaUnicaSiZgomotul(int size)
    {
        var spectrum = new SpectrumAnalyzer(size);
        var pcm = Tone(96000, 1234.5);
        spectrum.Process(pcm, pcm.Length);
        Assert.Null(AutoTune.FindLowerTone(spectrum.AverageSpectrum()!, spectrum.BinWidthHz));
        var random = new Random(1234);
        pcm = Enumerable.Range(0, 96000).Select(_ => (short)random.Next(-2000, 2001)).ToArray();
        spectrum.Reset(); spectrum.Process(pcm, pcm.Length);
        Assert.Null(AutoTune.FindLowerTone(spectrum.AverageSpectrum()!, spectrum.BinWidthHz));
    }

    [Fact]
    public void IstoriculPastreazaGolurileSiDatelePentruZoom()
    {
        var history = new SpectralHistory();
        var amplitudes = new float[] { 0, .5f, 0 };
        history.Add(new(1, 1, .064, 10, amplitudes));
        history.Add(new(1, 4, .160, 10, amplitudes));
        var frozen = history.Snapshot();
        Assert.NotNull(frozen.Rows[^1].GapStart);
        Assert.Equal(-6.0206, SpectralHistory.SampleDb(frozen.Rows[0].Frame, 10), 3);
        Assert.True(SpectralHistory.SampleDb(frozen.Rows[0].Frame, 15) < -6);
        Assert.False(history.Add(new(1, 3, .128, 10, amplitudes)));
        for (int i = 5; i < 1100; i++) history.Add(new(1, i, .032 * i, 10, amplitudes));
        Assert.True(history.Count <= 940);
        Assert.Equal(2, frozen.Rows.Length);
        history.Mark("QSY");
        history.Add(new(2, 1, .064, 10, amplitudes));
        Assert.Contains(history.Snapshot().Boundaries, b => b.Label.Contains("QSY"));
        history.Clear();
        Assert.Equal(0, history.Count);
    }
}
