using RttyDecoder.Core.Baudot;
using RttyDecoder.Core.Tx;

namespace RttyDecoder.Core.Tests;

public class AfskTests
{
    /// <summary>Placa de sunet simulata: genereaza blocuri de 10 ms cam de 10 ori mai repede decat real.</summary>
    private sealed class FakeSink : IAfskSink
    {
        private Thread? _thread;
        private volatile bool _run;
        public List<float> Samples { get; } = new();
        public volatile int Starts, Stops;
        public int SampleRate => 48000;
        public double LatencySeconds => 0;
        public event Action<Exception?>? Failed;

        public void Start(AfskModulator modulator)
        {
            _run = true;
            _thread = new Thread(() =>
            {
                var block = new float[480];
                while (_run)
                {
                    modulator.Render(block);
                    lock (Samples) Samples.AddRange(block);
                    Thread.Sleep(1);
                }
            }) { IsBackground = true };
            _thread.Start();
            Starts++;
        }

        public void Stop()
        {
            Stops++;
            _run = false;
            if (_thread != Thread.CurrentThread) _thread?.Join();
        }

        public void Fail() => Failed?.Invoke(new IOException("scoasa"));

        public short[] Pcm()
        {
            lock (Samples) return Samples.Select(x => (short)Math.Round(x * 32767)).ToArray();
        }
    }

    [Fact]
    public void Bits_are_sample_exact_without_drift()
    {
        var mod = new AfskModulator(48000, 2125, 2295);
        for (int i = 0; i < 100; i++) mod.Enqueue(BaudotCodec.Ltrs);
        Assert.Equal(100, mod.PendingCodes);
        int n = 0;
        var one = new float[1];
        while (!mod.IsIdle) { mod.Render(one); n++; }
        double expected = 100 * (1 + 5 + RttyTxSettings.StopBits) * 48000 / RttyTxSettings.Baud;
        Assert.InRange(n, expected - 1, expected + 1);
        Assert.Equal(0, mod.PendingCodes);
    }

    [Fact]
    public void Phase_is_continuous_across_tone_changes()
    {
        var mod = new AfskModulator(48000, 2125, 2295, level: 1);
        foreach (int c in new[] { 0x00, 0x15, 0x0A, 0x1F, 0x01 }) mod.Enqueue(c);
        var buf = new float[48000];
        mod.Render(buf);
        // pasul maxim al unei sinusoide de 2295 Hz la 48 kHz
        double maxStep = 2 * Math.PI * 2295 / 48000 * 1.0001;
        for (int i = 300; i < buf.Length; i++) Assert.True(Math.Abs(buf[i] - buf[i - 1]) <= maxStep, $"salt la {i}");
        Assert.True(buf.Max() > 0.99f);
    }

    [Fact]
    public void Idle_output_is_mark()
    {
        var mod = new AfskModulator(48000, 2125, 2295);
        var buf = new float[4800];
        mod.Render(buf);
        // 0.1 s de Mark: 212.5 perioade => ~425 treceri prin zero
        int crossings = 0;
        for (int i = 1; i < buf.Length; i++) if ((buf[i - 1] < 0) != (buf[i] < 0)) crossings++;
        Assert.InRange(crossings, 420, 430);
    }

    [Theory]
    [InlineData(false, false, 2125, 2295)]
    [InlineData(true, false, 2295, 2125)]
    [InlineData(false, true, 2295, 2125)]
    [InlineData(true, true, 2125, 2295)]
    public void Polarity_follows_sideband(bool usb, bool invert, double mark, double space)
    {
        var t = AfskTones.ForSideband(2125, 170, usb, invert);
        Assert.Equal(mark, t.MarkHz);
        Assert.Equal(space, t.SpaceHz);
    }

    [Fact]
    public void Transmitted_audio_decodes()
    {
        var sink = new FakeSink();
        var ptt = new List<bool>();
        var output = new AfskOutput(sink, on => { lock (ptt) ptt.Add(on); }, () => new AfskTones(2125, 2295), () => 0.5);
        using var tx = new RttyFskTransmitter(output);
        tx.Settings.TailMs = 100;
        var done = new ManualResetEventSlim();
        string? reason = "nimic";
        tx.Stopped += r => { reason = r; done.Set(); };
        const string text = "CQ TEST DE YO9VA YO9VA 599 001 K";
        tx.Send(text);
        Assert.True(done.Wait(30000));
        Assert.Null(reason);
        Assert.Equal(new[] { true, false }, ptt);
        Assert.Equal(1, sink.Starts);
        Assert.Equal(1, sink.Stops);
        string decoded = Synthetic.DecodeClassic(sink.Pcm(), matchedFilter: false);
        Assert.Contains(text, Synthetic.Normalize(decoded));
    }

    [Fact]
    public void Stop_cuts_audio_and_ptt()
    {
        var sink = new FakeSink();
        var ptt = new List<bool>();
        var output = new AfskOutput(sink, on => { lock (ptt) ptt.Add(on); }, () => new AfskTones(2125, 2295), () => 0.5);
        using var tx = new RttyFskTransmitter(output);
        var done = new ManualResetEventSlim();
        string? reason = null;
        tx.Stopped += r => { reason = r; done.Set(); };
        tx.Send(new string('R', 200));
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (tx.QueuedCharacters > 190 && DateTime.UtcNow < limit) Thread.Sleep(5);
        tx.Stop();
        Assert.True(done.Wait(5000));
        Assert.Equal("oprit", reason);
        Assert.Equal(new[] { true, false }, ptt);
        Assert.Equal(1, sink.Stops);
        Assert.Null(output.Modulator);
    }

    [Fact]
    public void Sound_card_failure_stops_transmission()
    {
        var sink = new FakeSink();
        var output = new AfskOutput(sink, _ => { }, () => new AfskTones(2125, 2295), () => 0.5);
        using var tx = new RttyFskTransmitter(output);
        var done = new ManualResetEventSlim();
        string? reason = null;
        tx.Stopped += r => { reason = r; done.Set(); };
        tx.Send(new string('R', 200));
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (sink.Starts == 0 && DateTime.UtcNow < limit) Thread.Sleep(5);
        sink.Stop(); // placa nu mai cere esantioane
        sink.Fail();
        Assert.True(done.Wait(5000));
        Assert.StartsWith("eroare", reason);
    }
}
