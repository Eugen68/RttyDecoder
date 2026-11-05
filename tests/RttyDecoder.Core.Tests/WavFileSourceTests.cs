using System.Text;
using NAudio.Wave;
using RttyDecoder.Audio;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

public class WavFileSourceTests
{
    /// <summary>WAV stereo 44.1 kHz cu RTTY doar pe canalul drept si zgomot slab pe stangul.</summary>
    private static string ScrieWavStereo(short[] dreapta)
    {
        string cale = Path.Combine(Path.GetTempPath(), $"rtty_{Guid.NewGuid():N}.wav");
        var stanga = Generate("", new Options { SnrDb = 0, Seed = 9, SampleRate = 44100 });
        using var w = new WaveFileWriter(cale, new WaveFormat(44100, 16, 2));
        var buf = new byte[4];
        for (int i = 0; i < dreapta.Length; i++)
        {
            short l = (short)(i < stanga.Length ? stanga[i] / 8 : 0);
            BitConverter.TryWriteBytes(buf.AsSpan(0, 2), l);
            BitConverter.TryWriteBytes(buf.AsSpan(2, 2), dreapta[i]);
            w.Write(buf, 0, 4);
        }
        return cale;
    }

    private static string Decodeaza(string cale, AudioChannel canal, Action<RttySettings>? setari = null)
    {
        using var rx = new RttyReceiver();
        if (setari != null) rx.Update(setari);
        var sb = new StringBuilder();
        rx.TextReceived += t => { lock (sb) sb.Append(t); };
        using var src = new WavFileSource(cale, realTime: false, canal);
        var gata = new ManualResetEventSlim();
        src.SamplesAvailable += (b, n, sr) => rx.Process(b.AsSpan(0, n), sr, waitForDecoder: true);
        src.Stopped += _ => { rx.Flush(); gata.Set(); };
        rx.Start();
        src.Start();
        Assert.True(gata.Wait(TimeSpan.FromSeconds(60)));
        Assert.Equal(1.0, src.Progress, 3);
        lock (sb) return sb.ToString();
    }

    [Fact]
    public void CanalulDreptDinStereo()
    {
        var semnal = Generate(Text, new Options { SnrDb = 10, Seed = 5, SampleRate = 44100 });
        string cale = ScrieWavStereo(semnal);
        try
        {
            using (var probe = new WavFileSource(cale, false))
            {
                Assert.Equal(44100, probe.SampleRate);
                Assert.Equal(2, probe.Channels);
            }
            string dreapta = Decodeaza(cale, AudioChannel.Right);
            Assert.True(Cer(Text, dreapta) < 0.02, dreapta);
            string stanga = Decodeaza(cale, AudioChannel.Left);
            Assert.True(Cer(Text, stanga) > 0.5, "pe canalul stang nu e semnal: " + stanga);
        }
        finally
        {
            File.Delete(cale);
        }
    }

    /// <summary>Fisierul de exemplu din samples/ (11025 Hz, Mark 1275 Hz, +3 dB) - cel folosit si de CI.</summary>
    [Fact]
    public void FisierulDeExemplu()
    {
        string cale = Path.Combine(AppContext.BaseDirectory, "samples", "rtty_sample_11025hz.wav");
        string text = Decodeaza(cale, AudioChannel.Left, s => s.MarkFrequencyHz = 1275);
        Assert.True(Cer(Text + Text, text) < 0.02, text);
    }
}
