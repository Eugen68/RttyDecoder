using RttyDecoder.Core.DeepRtty;
using static RttyDecoder.Core.Tests.Synthetic;

namespace RttyDecoder.Core.Tests;

public class ReceiverBackpressureTests : IClassFixture<DeepRttyModelFixture>
{
    private readonly DeepRttyModelFixture _fixture;
    public ReceiverBackpressureTests(DeepRttyModelFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task StopPoateIntrerupeProducatorulCareAsteaptaBufferul()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var rx = new RttyReceiver((input, dims) =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            return null;
        });
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        rx.Start();
        Task<bool>? flush = null;
        Task? producer = null;
        try
        {
            rx.Process48k(new short[49920], 49920, waitForDecoder: true);
            flush = Task.Run(() => rx.TryFlush(TimeSpan.FromSeconds(20)));
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            rx.Process48k(new short[24 * 48000], 24 * 48000);
            Assert.Equal(24, rx.DeepRttyQueueStatus.PendingAudioSeconds, 6);
            producer = Task.Run(() => rx.Process48k(new short[3840], 3840, waitForDecoder: true));
            // Flush trebuie sa respecte timeout-ul chiar daca bufferul este plin.
            Assert.False(rx.TryFlush(TimeSpan.FromMilliseconds(50)));
            await Task.Run(rx.Stop).WaitAsync(TimeSpan.FromSeconds(2));
            await producer.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(await flush.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(rx.IsRunning);
        }
        finally
        {
            release.Set();
            rx.Stop();
            if (producer != null) await producer.WaitAsync(TimeSpan.FromSeconds(10));
            if (flush != null) await flush.WaitAsync(TimeSpan.FromSeconds(10));
            await rx.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(44100)]
    public void FisierLungInUnSingurApelNuPierdeAudio(int sampleRate)
    {
        string expected = Text + Text;
        var audio = ToFloat(Generate(expected, new Options { SnrDb = 10, Seed = 4, SampleRate = sampleRate }));
        using var rx = new RttyReceiver(_fixture.Model.Run);
        var text = new System.Text.StringBuilder();
        rx.TextReceived += t => { lock (text) text.Append(t); };
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        rx.Start();
        rx.Process(audio, sampleRate, waitForDecoder: true);
        Assert.True(rx.TryFlush(TimeSpan.FromSeconds(30)));
        string decoded;
        lock (text) decoded = text.ToString();
        Assert.True(Cer(expected, decoded) < 0.02, decoded);
        Assert.Equal(0, rx.DeepRttyQueueStatus.Overruns);
        Assert.Equal(0, rx.DeepRttyQueueStatus.PendingAudioSeconds);
    }
}
