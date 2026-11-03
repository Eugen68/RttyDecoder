using NAudio.Wave;
using RttyDecoder.Audio;

namespace RttyDecoder.Core.Tests;

public class AudioMonitorTests
{
    /// <summary>Iesire audio falsa: retine ce i s-a dat, fara placa de sunet.</summary>
    private sealed class IesireFalsa : IWavePlayer
    {
        public IWaveProvider? Sursa;
        public bool Porneste;
        public bool Eliberata;
        public bool AruncaLaInit;
        public float Volume { get; set; } = 1;
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat => Sursa!.WaveFormat;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider)
        {
            if (AruncaLaInit) throw new InvalidOperationException("nicio placa de iesire");
            Sursa = waveProvider;
        }

        public void Play() { Porneste = true; PlaybackState = PlaybackState.Playing; }
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Dispose() => Eliberata = true;
        public void Deconecteaza() => PlaybackStopped?.Invoke(this, new StoppedEventArgs(new IOException("placa deconectata")));

        public short[] Citeste(int n)
        {
            var b = new byte[n * 2];
            int r = Sursa!.Read(b, 0, b.Length);
            var s = new short[r / 2];
            Buffer.BlockCopy(b, 0, s, 0, r);
            return s;
        }
    }

    private static float[] Sinus(int n, int fs, double hz, double amp = 0.5) =>
        Enumerable.Range(0, n).Select(i => (float)(amp * Math.Sin(2 * Math.PI * hz * i / fs))).ToArray();

    [Fact]
    public void RedaEsantioaneleCuVolumul()
    {
        var iesiri = new List<IesireFalsa>();
        using var m = new AudioMonitor(() => { var o = new IesireFalsa(); iesiri.Add(o); return o; });
        m.Volume = 0.5f;
        var x = Sinus(4800, 48000, 1000);
        m.Write(x, x.Length, 48000); // oprit: nu se intampla nimic
        Assert.Empty(iesiri);

        m.Start();
        m.Write(x, x.Length, 48000);
        var o = Assert.Single(iesiri);
        Assert.True(o.Porneste);
        Assert.Equal(48000, o.Sursa!.WaveFormat.SampleRate);
        Assert.Equal(1, o.Sursa.WaveFormat.Channels);
        Assert.Equal(16, o.Sursa.WaveFormat.BitsPerSample);
        var y = o.Citeste(4800);
        for (int i = 0; i < y.Length; i++) Assert.InRange(y[i], x[i] * 0.5 * 32767 - 1.5, x[i] * 0.5 * 32767 + 1.5);
    }

    [Fact]
    public void SchimbareaDeRataRedeschideRedarea()
    {
        var iesiri = new List<IesireFalsa>();
        using var m = new AudioMonitor(() => { var o = new IesireFalsa(); iesiri.Add(o); return o; });
        m.Start();
        m.Write(Sinus(800, 8000, 500), 800, 8000);
        m.Write(Sinus(800, 8000, 500), 800, 8000);
        Assert.Single(iesiri);
        m.Write(Sinus(4410, 44100, 500), 4410, 44100);
        Assert.Equal(2, iesiri.Count);
        Assert.True(iesiri[0].Eliberata);
        Assert.Equal(44100, iesiri[1].Sursa!.WaveFormat.SampleRate);
        m.Stop();
        Assert.True(iesiri[1].Eliberata);
    }

    [Fact]
    public void IntarziereaNuCrestePeste400ms()
    {
        IesireFalsa? o = null;
        using var m = new AudioMonitor(() => o = new IesireFalsa());
        m.Start();
        // intrarea "merge mai repede" decat iesirea: 2 s scrise, nimic citit
        for (int k = 0; k < 20; k++) m.Write(Sinus(4800, 48000, 1000), 4800, 48000);
        var buf = (BufferedWaveProvider)o!.Sursa!;
        Assert.True(buf.BufferedDuration.TotalMilliseconds <= AudioMonitor.MaxBufferMs + 100, $"{buf.BufferedDuration.TotalMilliseconds} ms");
        Assert.True(m.BufferResets > 0);
    }

    [Fact]
    public void FaraPlacaDeIesireSeOpresteSiAnunta()
    {
        using var m = new AudioMonitor(() => new IesireFalsa { AruncaLaInit = true });
        Exception? err = null;
        m.Failed += e => err = e;
        m.Start();
        m.Write(Sinus(480, 48000, 1000), 480, 48000);
        Assert.NotNull(err);
        Assert.False(m.IsEnabled);
    }

    [Fact]
    public void PlacaDeconectataInTimpulRedarii()
    {
        IesireFalsa? o = null;
        using var m = new AudioMonitor(() => o = new IesireFalsa());
        Exception? err = null;
        m.Failed += e => err = e;
        m.Start();
        m.Write(Sinus(480, 48000, 1000), 480, 48000);
        o!.Deconecteaza();
        Assert.IsType<IOException>(err);
        Assert.False(m.IsEnabled);
        Assert.True(o.Eliberata);
        m.Write(Sinus(480, 48000, 1000), 480, 48000); // dupa eroare nu mai reda
    }
}
