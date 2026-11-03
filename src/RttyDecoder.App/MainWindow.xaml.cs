using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using RttyDecoder.Core;
using RttyDecoder.Wpf;

namespace RttyDecoder.App;

public partial class MainWindow : Window
{
    private readonly RttyDecoderViewModel _vm;
    private readonly AppSettings _settings;
    private readonly string[]? _smoke;

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--smoke");
        if (i >= 0 && i + 2 < args.Length) _smoke = args[(i + 1)..];
        _settings = _smoke != null ? new AppSettings() : AppSettings.Load();
        _vm = new RttyDecoderViewModel();
        if (_settings.Rtty != null) _vm.ImportSettings(_settings.Rtty);
        _vm.SelectDevice(_settings.DeviceId);
        _vm.Channel = _settings.Channel;
        _vm.RealTime = _settings.RealTime;
        Decoder.DataContext = _vm;

        if (_settings.Width > 0 && _settings.Height > 0)
        {
            Width = _settings.Width;
            Height = _settings.Height;
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
        if (_smoke != null) Loaded += async (_, _) => await RunSmokeTest(_smoke);
    }

    /// <summary>
    /// Test de fum pentru CI: <c>RttyDecoder.exe --smoke intrare.wav iesire.txt [Chebyshev|MatchedFilter|DeepRtty] [markHz]</c>.
    /// Porneste interfata reala, decodeaza fisierul cat de repede se poate, scrie textul si iese.
    /// </summary>
    private async Task RunSmokeTest(string[] a)
    {
        int cod = 0;
        try
        {
            if (a.Length > 2) _vm.Decoder = Enum.Parse<RttyDecoderType>(a[2], ignoreCase: true);
            if (a.Length > 3) _vm.MarkFrequencyHz = double.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture);
            _vm.RealTime = false;
            await _vm.OpenFileAsync(a[0]);
            var limita = DateTime.UtcNow.AddMinutes(3);
            while (_vm.IsRunning && DateTime.UtcNow < limita) await Task.Delay(200);
            await Task.Delay(300); // ultimul Drain al textului
            if (_vm.IsRunning) cod = 2;
            File.WriteAllText(a[1], Decoder.TerminalText + "\n[message] " + _vm.Message + "\n");
        }
        catch (Exception e)
        {
            File.WriteAllText(a[1], "[exception] " + e);
            cod = 1;
        }
        Application.Current.Shutdown(cod);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_smoke != null)
        {
            _vm.Dispose();
            base.OnClosing(e);
            return;
        }
        _settings.Rtty = _vm.ExportSettings();
        _settings.DeviceId = _vm.SelectedDevice?.Id;
        _settings.Channel = _vm.Channel;
        _settings.RealTime = _vm.RealTime;
        _settings.Maximized = WindowState == WindowState.Maximized;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Width = b.Width;
        _settings.Height = b.Height;
        _settings.Save();
        _vm.Dispose();
        base.OnClosing(e);
    }
}
