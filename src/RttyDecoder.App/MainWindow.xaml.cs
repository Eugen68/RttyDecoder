using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using HamCallsign;
using RttyDecoder.Core;
using RttyDecoder.Wpf;

namespace RttyDecoder.App;

public partial class MainWindow : Window
{
    private readonly RttyDecoderViewModel _vm;
    private readonly AppSettings _settings;
    private readonly string[]? _smoke;
    private readonly Task _callsignData;

    /// <summary>cty.dat si MASTER.SCP actualizate (%LOCALAPPDATA%\YO9VA\RttyDecoder); altfel cele incluse.</summary>
    private static readonly string CallsignCache =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YO9VA", "RttyDecoder");

    /// <summary>
    /// Bazele de indicative se incarca in fundal (pana atunci se recunoaste doar forma); apoi, tot in
    /// fundal, se descarca versiunile noi daca cele locale au peste 7 zile - folosite la pornirea urmatoare.
    /// </summary>
    private async Task LoadCallsignDataAsync()
    {
        try
        {
            string? cache = _smoke != null ? null : CallsignCache;
            var data = await Task.Run(() => ReferenceData.Load(cache));
            _vm.UseReferenceData(data);
            if (cache != null) await ReferenceData.UpdateAsync(cache);
        }
        catch
        {
            // fara baze: raman indicativele recunoscute dupa forma
        }
    }

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
        _vm.MonitorVolume = _settings.MonitorVolume;
        _vm.MyCall = _settings.MyCall ?? "";
        _vm.ShowStations = _settings.ShowStations;
        Decoder.DataContext = _vm;
        _callsignData = LoadCallsignDataAsync();

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
            await _callsignData;
            await _vm.OpenFileAsync(a[0]);
            var limita = DateTime.UtcNow.AddMinutes(3);
            while (_vm.IsRunning && DateTime.UtcNow < limita) await Task.Delay(200);
            await Task.Delay(300); // ultimul Drain al textului
            if (_vm.IsRunning) cod = 2;
            File.WriteAllText(a[1], Decoder.TerminalText + "\n[message] " + _vm.Message + "\n[stations]\n" + _vm.Callsigns.ToCsv());
        }
        catch (Exception e)
        {
            File.WriteAllText(a[1], "[exception] " + e);
            cod = 1;
        }
        Application.Current.Shutdown(cod);
    }

    // ------------------------------------------------------------------
    // Meniu
    // ------------------------------------------------------------------

    private void OnOpenWav(object sender, RoutedEventArgs e) => Decoder.OpenWavDialog();

    private void OnSaveText(object sender, RoutedEventArgs e) => Decoder.SaveTextDialog();

    private void OnSaveStations(object sender, RoutedEventArgs e) => Decoder.SaveStationsDialog();

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenFolder(CallsignCache);

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) => OpenFolder(AppSettings.Folder);

    /// <summary>Deschide folderul in Explorer (il creeaza daca inca nu exista).</summary>
    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _vm.Message = "Nu pot deschide folderul: " + ex.Message;
        }
    }

    /// <summary>Verifica acum pe internet bazele de indicative; cele noi se folosesc imediat.</summary>
    private async void OnUpdateData(object sender, RoutedEventArgs e)
    {
        UpdateDataItem.IsEnabled = false;
        _vm.Message = "Verific cty.dat, MASTER.SCP si LoTW...";
        try
        {
            int n = await ReferenceData.UpdateAsync(CallsignCache, TimeSpan.Zero);
            if (n > 0) _vm.UseReferenceData(await Task.Run(() => ReferenceData.Load(CallsignCache)));
            _vm.Message = n > 0
                ? $"Date de indicative actualizate: {n} {(n == 1 ? "fisier" : "fisiere")} nou{(n == 1 ? "" : "i")}, folosite de acum."
                : "Nimic nou: datele sunt la zi (sau nu e conexiune la internet).";
        }
        catch (Exception ex)
        {
            _vm.Message = "Actualizarea a esuat: " + ex.Message;
        }
        finally
        {
            UpdateDataItem.IsEnabled = true;
        }
    }

    private void OnDataStatus(object sender, RoutedEventArgs e)
    {
        var r = _vm.Callsigns.Recognizer;
        string Fisier(string name, string embedded)
        {
            string p = Path.Combine(CallsignCache, name);
            return File.Exists(p) ? $"descarcat {File.GetLastWriteTime(p):yyyy-MM-dd HH:mm}" : embedded;
        }
        string text =
            $"cty.dat: {r.Cty?.EntityCount ?? 0} entitati DXCC - {Fisier(ReferenceData.CtyFileName, "copia inclusa in aplicatie")}\n" +
            $"MASTER.SCP: {r.Scp?.Count ?? 0:N0} indicative - {Fisier(ReferenceData.MasterScpFileName, "copia inclusa in aplicatie")}\n" +
            $"LoTW: {r.Lotw?.Count ?? 0:N0} indicative active - {Fisier(ReferenceData.LotwFileName, "inca nedescarcata")}\n\n" +
            $"Folder: {CallsignCache}\n\n" +
            "cty.dat si MASTER.SCP se verifica saptamanal, LoTW zilnic, in fundal.";
        MessageBox.Show(this, text, "Starea datelor de indicative", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var a = Assembly.GetExecutingAssembly();
        string version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                         ?? a.GetName().Version?.ToString() ?? "";
        MessageBox.Show(this,
            $"RTTY Decoder {version}\n" +
            "Copyright (c) 2026 Eugen Trifu, YO9VA - licenta MIT\n\n" +
            "Decodor RTTY (Chebyshev, Matched Filter, DeepRTTY) portat din aplicatia Android Yaesu-FTX1-CAT-Control.\n" +
            "Indicative: biblioteca HamCallsign (cty.dat AD1C, MASTER.SCP, LoTW).\n\n" +
            "https://github.com/Eugen68/RttyDecoder",
            "Despre RTTY Decoder", MessageBoxButton.OK, MessageBoxImage.Information);
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
        _settings.MonitorVolume = _vm.MonitorVolume;
        _settings.MyCall = _vm.MyCall;
        _settings.ShowStations = _vm.ShowStations;
        _settings.Maximized = WindowState == WindowState.Maximized;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Width = b.Width;
        _settings.Height = b.Height;
        _settings.Save();
        _vm.Dispose();
        base.OnClosing(e);
    }
}
