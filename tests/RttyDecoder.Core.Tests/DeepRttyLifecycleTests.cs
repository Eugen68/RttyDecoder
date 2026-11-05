using System.Collections.Concurrent;
using RttyDecoder.Core.DeepRtty;

namespace RttyDecoder.Core.Tests;

public class DeepRttyLifecycleTests
{
    private static Task<T> OnThread<T>(Func<T> action) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static Task OnThread(Action action) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private sealed class SlowInference : IDisposable
    {
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();
        public int Calls;
        public int Active;
        public int MaxActive;

        public (float[] Data, long[] Dims)? Run(float[] input, long[] dims)
        {
            int active = Interlocked.Increment(ref Active);
            int previous;
            do { previous = MaxActive; }
            while (active > previous && Interlocked.CompareExchange(ref MaxActive, active, previous) != previous);
            int call = Interlocked.Increment(ref Calls);
            try
            {
                if (call == 1)
                {
                    Entered.Set();
                    if (!Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                }
                var data = Enumerable.Repeat(-100f, 33).ToArray();
                data[call == 1 ? 1 : 3] = 0; // un token diferit in sesiunea veche
                return (data, new long[] { 1, 1, 33 });
            }
            finally { Interlocked.Decrement(ref Active); }
        }

        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    private static void FeedBlocks(DeepRttyReceiver receiver, int blocks, bool wait = false)
    {
        var pcm = new short[DeepRttyReceiver.MaxFeedSamples];
        for (int i = 0; i < blocks; i++)
        {
            if (wait) Assert.True(receiver.WaitForCapacity(TimeSpan.FromSeconds(10)));
            receiver.Feed(pcm, pcm.Length, 2210, false);
        }
    }

    private static async Task<Task<bool>> BlockInference(DeepRttyReceiver receiver, SlowInference slow)
    {
        receiver.Start();
        FeedBlocks(receiver, 13); // 1.04 s: suficient pentru Finish/Preview
        var flush = OnThread(() => receiver.Flush(TimeSpan.FromSeconds(20)));
        Assert.True(await OnThread(() => slow.Entered.Wait(TimeSpan.FromSeconds(10))));
        return flush;
    }

    [Fact]
    public async Task StopStartNuSuprapuneInferentaSiNuPublicaTextulVechi()
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run);
        var codes = new ConcurrentQueue<int>();
        receiver.Confirmed += tokens => { foreach (int token in tokens) codes.Enqueue(token); };
        try
        {
            Task<bool> oldFlush = await BlockInference(receiver, slow);
            receiver.Stop();
            Assert.False(await oldFlush.WaitAsync(TimeSpan.FromSeconds(2)));
            receiver.Start();
            FeedBlocks(receiver, 13);
            Task<bool> nextFlush = OnThread(() => receiver.Flush(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, Volatile.Read(ref slow.Calls));
            slow.Release.Set();
            Assert.True(await nextFlush.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal(1, slow.MaxActive);
            Assert.NotEmpty(codes);
            Assert.All(codes, token => Assert.Equal(3, token));
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task DisposeNuBlocheazaSiCompletionAsteaptaInferentaNativa()
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run);
        try
        {
            var flush = await BlockInference(receiver, slow);
            await OnThread(receiver.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(await flush.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(receiver.Completion.IsCompleted);
            Assert.Throws<ObjectDisposedException>(receiver.Start);
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task ResetInvalideazaInferentaVechiiSesiuni()
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run);
        var codes = new ConcurrentQueue<int>();
        receiver.Confirmed += tokens => { foreach (int token in tokens) codes.Enqueue(token); };
        try
        {
            var oldFlush = await BlockInference(receiver, slow);
            receiver.Reset();
            Assert.False(await oldFlush.WaitAsync(TimeSpan.FromSeconds(2)));
            FeedBlocks(receiver, 13);
            var nextFlush = OnThread(() => receiver.Flush(TimeSpan.FromSeconds(10)));
            slow.Release.Set();
            Assert.True(await nextFlush.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.NotEmpty(codes);
            Assert.All(codes, token => Assert.Equal(3, token));
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task LivePastreazaBufferulLimitatSiRecupereazaDupaSuprasarcina()
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run, maxBufferedSeconds: 2);
        try
        {
            await BlockInference(receiver, slow);
            FeedBlocks(receiver, 100);
            Assert.InRange(receiver.QueueStatus.PendingAudioSeconds, 0, 2);
            Assert.True(receiver.QueueStatus.Overruns > 0);
            slow.Release.Set();
            Assert.True(await OnThread(() => receiver.Flush(TimeSpan.FromSeconds(10))));
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsteptareaBufferuluiEsteEliberataDeStopSauDispose(bool dispose)
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run, maxBufferedSeconds: 2);
        try
        {
            await BlockInference(receiver, slow);
            FeedBlocks(receiver, 25); // exact 2 s in asteptare, inferenta inca blocata
            Assert.Equal(2, receiver.QueueStatus.PendingAudioSeconds, 6);
            Assert.False(receiver.WaitForCapacity(TimeSpan.Zero));
            var waiting = OnThread(() => receiver.WaitForCapacity());
            if (dispose) receiver.Dispose(); else receiver.Stop();
            Assert.False(await waiting.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, receiver.QueueStatus.PendingAudioSeconds);
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task SursaFinitaAsteaptaCapacitateaFaraPierderi()
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run, maxBufferedSeconds: 2);
        try
        {
            await BlockInference(receiver, slow);
            FeedBlocks(receiver, 25);
            Assert.False(receiver.WaitForCapacity(TimeSpan.Zero));
            var producer = OnThread(() => FeedBlocks(receiver, 100, wait: true));
            slow.Release.Set();
            await producer.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(await OnThread(() => receiver.Flush(TimeSpan.FromSeconds(10))));
            Assert.Equal(0, receiver.QueueStatus.Overruns);
            Assert.Equal(0, receiver.QueueStatus.PendingAudioSeconds);
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task FlushTimeoutNuEsteRaportatCaSucces()
    {
        using var slow = new SlowInference();
        using var receiver = new DeepRttyReceiver(slow.Run);
        try
        {
            await BlockInference(receiver, slow);
            Assert.False(receiver.Flush(TimeSpan.FromMilliseconds(50)));
            slow.Release.Set();
            Assert.True(await OnThread(() => receiver.Flush(TimeSpan.FromSeconds(10))));
        }
        finally { slow.Release.Set(); receiver.Dispose(); await receiver.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
}
