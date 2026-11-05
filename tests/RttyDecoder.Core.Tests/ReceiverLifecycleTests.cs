using RttyDecoder.Core.DeepRtty;

namespace RttyDecoder.Core.Tests;

public class ReceiverLifecycleTests
{
    [Fact]
    public async Task DisposeInTimpulIncarcariiNuPublicaModelul()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var rx = new RttyReceiver(modelFactory: () =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            return DeepRttyModel.LoadEmbedded();
        });
        int messages = 0;
        rx.Message += _ => Interlocked.Increment(ref messages);
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        Task loading = rx.WaitForModelAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            rx.Dispose();
        }
        finally { release.Set(); }
        await loading.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(rx.IsDeepRttyReady);
        Assert.False(rx.IsRunning);
        Assert.Equal(0, messages);
        Assert.Throws<ObjectDisposedException>(() => rx.Start());
        Assert.Throws<ObjectDisposedException>(() => rx.Update(s => s.Squelch = false));
    }

    [Fact]
    public async Task EroareaUneiIncarcariVechiNuSchimbaDecodorulAles()
    {
        using var release = new ManualResetEventSlim();
        using var rx = new RttyReceiver(modelFactory: () =>
        {
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            throw new FileNotFoundException("model lipsa");
        });
        rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
        Task loading = rx.WaitForModelAsync();
        try { rx.Update(s => s.Decoder = RttyDecoderType.MatchedFilter); }
        finally { release.Set(); }
        await loading.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RttyDecoderType.MatchedFilter, rx.Settings.Decoder);
    }

    [Fact]
    public async Task IncarcareaEsuataPoateFiReincercata()
    {
        int calls = 0;
        using var rx = new RttyReceiver(modelFactory: () =>
        {
            Interlocked.Increment(ref calls);
            throw new FileNotFoundException("model lipsa");
        });
        for (int i = 0; i < 2; i++)
        {
            rx.Update(s => s.Decoder = RttyDecoderType.DeepRtty);
            await rx.WaitForModelAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(RttyDecoderType.Chebyshev, rx.Settings.Decoder);
        }
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData((short)16384, -6.0206)]
    [InlineData(short.MinValue, 0)]
    [InlineData((short)0, -120)]
    public void Process48kRaporteazaNivelulPcm(short sample, double expectedDb)
    {
        using var rx = new RttyReceiver();
        var levels = new List<double>();
        rx.StatusUpdated += s => levels.Add(s.InputLevelDbfs);
        rx.Start();
        var pcm = Enumerable.Repeat(sample, 3840).ToArray();
        for (int i = 0; i < 20; i++) rx.Process48k(pcm, pcm.Length);
        Assert.NotEmpty(levels);
        Assert.All(levels, level => Assert.InRange(level, expectedDb - 0.01, expectedDb + 0.01));
    }
}
