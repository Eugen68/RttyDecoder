using System.Windows.Threading;
using RttyDecoder.Core;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

public class SourceLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FisierulInAsteptareNuPornesteDupaStopSauDispose(bool dispose)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await Verify(dispose); completed.SetResult(); }
                catch (Exception e) { completed.SetException(e); }
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
        using var receiver = new RttyReceiver(modelFactory: () =>
        {
            if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            throw new FileNotFoundException("model de test indisponibil");
        });
        using var vm = new RttyDecoderViewModel(receiver);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        // WAV PCM mono valid, o secunda de liniste.
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write("RIFF"u8); writer.Write(36 + 16000); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(16000); writer.Write(new byte[16000]);
        }
        Task? open = null;
        try
        {
            vm.Decoder = RttyDecoderType.DeepRtty;
            open = vm.OpenFileAsync(path);
            Assert.False(open.IsCompleted);
            if (dispose) vm.Dispose(); else vm.StopSource();
            release.Set();
            await open;
            Assert.False(vm.IsRunning);
            Assert.False(receiver.IsRunning);
            Assert.False(vm.IsFile);
        }
        finally
        {
            release.Set();
            if (open != null) await open;
            File.Delete(path);
        }
    }
}
