using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using RttyDecoder.Core;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

public class BufferedSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopSauInchidereCuWavSiBufferPlinNuAsteaptaInferenta(bool dispose)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await Verify(dispose); completed.SetResult(); }
                catch (Exception error) { completed.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static async Task Verify(bool dispose)
    {
        using var release = new ManualResetEventSlim();
        using var rx = new RttyReceiver((input, dims) =>
        {
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            return null;
        });
        using var vm = new RttyDecoderViewModel(rx);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            const int bytes = 40 * 8000 * 2;
            writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        try
        {
            vm.Decoder = RttyDecoderType.DeepRtty;
            vm.RealTime = false;
            await vm.OpenFileAsync(path);
            var clock = Stopwatch.StartNew();
            while (rx.DeepRttyQueueStatus.PendingAudioSeconds < 23.9 && clock.Elapsed < TimeSpan.FromSeconds(15))
                await Task.Delay(10);
            Assert.True(rx.DeepRttyQueueStatus.PendingAudioSeconds >= 23.9);
            Assert.Equal(0, rx.DeepRttyQueueStatus.Overruns);
            clock.Restart();
            if (dispose) vm.Dispose(); else vm.StopSource();
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Oprirea a durat {clock.Elapsed}.");
            Assert.False(vm.IsRunning);
            Assert.False(rx.IsRunning);
        }
        finally
        {
            release.Set();
            vm.StopSource();
            await rx.DisposeAsync();
            File.Delete(path);
        }
    }
}
