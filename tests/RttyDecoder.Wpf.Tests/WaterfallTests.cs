using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RttyDecoder.Core;
using RttyDecoder.Core.Dsp;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

public class WaterfallTests
{
    private static Task Sta(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); done.SetResult(); }
                catch (Exception error) { done.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static SpectrumFrame Frame(long sequence, double amplitude = .5, double binHz = 15.625)
    {
        var magnitudes = Enumerable.Repeat(1e-5f, (int)(4000 / binHz) + 1).ToArray();
        magnitudes[(int)Math.Round(1250 / binHz)] = (float)amplitude;
        return new(100, sequence, sequence * .032, binHz, magnitudes);
    }
    private static void Feed(WaterfallControl control, long first = 1, int count = 125, double amplitude = .5, double binHz = 15.625)
    {
        for (int i = 0; i < count; i++)
        {
            control.PushFrame(Frame(first + i, amplitude, binHz));
            if (i % 16 == 15) control.ProcessPendingFrames();
        }
        control.ProcessPendingFrames();
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var result = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(result, bitmap.PixelWidth * 4, 0);
        return result;
    }
    private static void Artifact(BitmapSource bitmap, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("RTTY_UI_ARTIFACTS");
        if (folder == null) return;
        Directory.CreateDirectory(folder);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name)); png.Save(file);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    public async Task ZoomPastreazaIstoriculIarVarfulEsteAliniatInHz(int size)
    {
        await Sta(async () =>
        {
            var control = new WaterfallControl { Width = 1000, Height = 300, AutoFloor = false, FloorDb = -100,
                RangeDb = 100, HistorySeconds = 8, MarkFrequencyHz = 200, FollowSignal = false };
            var window = new Window { Content = control, SizeToContent = SizeToContent.WidthAndHeight };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Feed(control, binHz: 8000.0 / size);
                int count = control.HistoryCount;
                control.SetViewport(1000, 1000);
                var image = control.Capture();
                Assert.Equal(count, control.HistoryCount);
                var pixels = Pixels(image);
                int y = (int)Math.Round(70 * image.PixelHeight / control.ActualHeight);
                double expected = (38 + (1250d - 1000) / 1000 * (control.ActualWidth - 38 - 48)) * image.PixelWidth / control.ActualWidth;
                int best = 0, max = -1;
                for (int x = (int)expected - 20; x <= expected + 20; x++)
                {
                    int offset = (y * image.PixelWidth + x) * 4;
                    int brightness = pixels[offset] + pixels[offset + 1] + pixels[offset + 2];
                    if (brightness > max) { max = brightness; best = x; }
                }
                Assert.True(max > 500);
                Assert.InRange(best, expected - 2, expected + 2);
                Artifact(image, $"waterfall-zoom-{size}.png");
                control.FullView();
                Assert.Equal(count, control.HistoryCount);
                control.MarkFrequencyHz = 1250;
                Artifact(control.Capture(), $"waterfall-general-{size}.png");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task PauzaIngheataImagineaDarNuBufferulLive()
    {
        await Sta(async () =>
        {
            var control = new WaterfallControl { Width = 1000, Height = 300, AutoFloor = false, FloorDb = -100, RangeDb = 100 };
            var window = new Window { Content = control, SizeToContent = SizeToContent.WidthAndHeight };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Feed(control, 1, 50, .05);
                control.IsPaused = true;
                var frozen = Pixels(control.Capture());
                Feed(control, 51, 50, .5);
                Assert.Equal(100, control.HistoryCount);
                Assert.Equal(frozen, Pixels(control.Capture()));
                control.IsPaused = false;
                Assert.False(frozen.SequenceEqual(Pixels(control.Capture())));
                control.PushFrame(Frame(1000)); control.ProcessPendingFrames();
                control.MarkBoundary("QSY de proba");
                Artifact(control.Capture(), "waterfall-gap.png");
                control.Clear();
                Assert.Equal(0, control.HistoryCount);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task CoadaEsteLimitataSiNivelulPoateFiBlocat()
    {
        await Sta(async () =>
        {
            var control = new WaterfallControl { Width = 1000, Height = 300 };
            var window = new Window { Content = control, SizeToContent = SizeToContent.WidthAndHeight };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                control.PushFrame(Frame(1)); control.ProcessPendingFrames();
                double floor = control.CurrentAutoFloorDb;
                control.LockLevels = true;
                for (int i = 2; i <= 200; i++) control.PushFrame(Frame(i, .9));
                control.ProcessPendingFrames();
                Assert.True(control.DroppedFrames > 0);
                Assert.True(control.HistoryCount <= 65);
                Assert.Equal(floor, control.CurrentAutoFloorDb);
                control.AutoFloor = false;
                Assert.Equal(floor - 3, control.FloorDb, 5);
                var normal = WaterfallControl.BuildPalette(WaterfallPalette.Contrast, 1);
                var dark = WaterfallControl.BuildPalette(WaterfallPalette.Contrast, 2);
                int Brightness(int c) => (c & 255) + ((c >> 8) & 255) + ((c >> 16) & 255);
                Assert.True(Brightness(dark[96]) < Brightness(normal[96]));
                Assert.Equal(0, normal[0]);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task SetarileSeRestaureazaFaraPierdereaBindinguluiPodelei()
    {
        await Sta(async () =>
        {
            using var rx = new RttyReceiver((input, dims) => null);
            using var vm = new RttyDecoderViewModel(rx);
            var view = new RttyDecoderView { DataContext = vm };
            var window = new Window { Content = view, Width = 1280, Height = 950 };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                view.WaterfallSettings = new WaterfallSettings { AutoFloor = false, FloorDb = -40 };
                Assert.InRange(view.WaterfallSettings.FloorDb, -82.9, -82.7);
                var migrated = view.WaterfallSettings;
                view.WaterfallSettings = migrated;
                Assert.Equal(migrated.FloorDb, view.WaterfallSettings.FloorDb);
                view.WaterfallSettings = new WaterfallSettings
                {
                    LevelScaleVersion = 2, AutoFloor = true, LockLevels = true, FloorDb = -75,
                    SpectrumSize = 1024, HistorySeconds = 30, MinFrequencyHz = 1000, MaxFrequencyHz = 1800,
                    Height = 250, Palette = WaterfallPalette.Contrast, SnapToSignal = true
                };
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(1024, rx.SpectrumSize);
                Assert.Equal(1, ((ComboBox)view.FindName("WaterfallResolution")).SelectedIndex);
                Assert.Equal(2, ((ComboBox)view.FindName("WaterfallHistory")).SelectedIndex);
                var saved = view.WaterfallSettings;
                Assert.Equal(-75, saved.FloorDb);
                Assert.Equal(1000, saved.MinFrequencyHz);
                Assert.Equal(1800, saved.MaxFrequencyHz);
                Assert.True(saved.SnapToSignal);
                var control = (WaterfallControl)view.FindName("Waterfall");
                Feed(control, binHz: 7.8125);
                control.Capture();
                var slider = (Slider)view.FindName("FloorSlider");
                Assert.NotNull(BindingOperations.GetBindingExpression(slider, Slider.ValueProperty));
                control.AutoFloor = false;
                slider.Value = -83;
                Assert.Equal(-83, control.FloorDb);
                var image = new RenderTargetBitmap(1280, 950, 96, 96, PixelFormats.Pbgra32);
                window.UpdateLayout(); image.Render(view);
                Artifact(image, "decoder-window.png");
                ((Expander)view.FindName("WaterfallDisplayOptions")).IsExpanded = true;
                window.UpdateLayout();
                var expanded = new RenderTargetBitmap(1280, 950, 96, 96, PixelFormats.Pbgra32);
                expanded.Render(view);
                Artifact(expanded, "decoder-display-options.png");
            }
            finally { window.Close(); }
        });
    }
}
