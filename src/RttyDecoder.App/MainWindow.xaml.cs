using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using HamCallsign;
using HamRig;
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
            string? cache = _smoke != null || _contestSmoke != null ? null : CallsignCache;
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
        int contestSmoke = Array.IndexOf(args, "--contest-smoke");
        if (contestSmoke >= 0 && contestSmoke + 1 < args.Length) _contestSmoke = args[contestSmoke + 1];
        int i = Array.IndexOf(args, "--smoke");
        if (i >= 0 && i + 2 < args.Length) _smoke = args[(i + 1)..];
        _settings = _smoke != null || _contestSmoke != null ? new AppSettings() : AppSettings.Load();
        _vm = new RttyDecoderViewModel();
        if (_settings.Rtty != null) _vm.ImportSettings(_settings.Rtty);
        _vm.SelectDevice(_settings.DeviceId);
        _vm.Channel = _settings.Channel;
        _vm.RealTime = _settings.RealTime;
        _vm.MonitorVolume = _settings.MonitorVolume;
        _vm.MyCall = _settings.MyCall ?? "";
        _vm.ShowStations = _settings.ShowStations;
        // polaritatea FSK s-a mutat din Setari transmisie in Conexiune statie (tine de statie)
        if (_settings.Tx.InvertFsk && !_settings.Rig.InvertFsk)
        {
            _settings.Rig.InvertFsk = true;
            _settings.Tx.InvertFsk = false;
            _settings.Save();
        }
        _vm.TxSettings = _settings.Tx;
        Decoder.WaterfallSettings = _settings.Waterfall;
        _vm.ImportMacros(_settings.Macros);
        Decoder.MacrosChanged += () =>
        {
            _settings.Macros = _vm.ExportMacros();
            _settings.Save();
        };
        _vm.SetOnlineCredentials(_settings.QrzUsername, _settings.GetQrzPassword(), _settings.HamQthUsername, _settings.GetHamQthPassword());
        Decoder.DataContext = _vm;
        InitializeContest();
        _callsignData = LoadCallsignDataAsync();
        if (_smoke == null && _contestSmoke == null) Loaded += async (_, _) => await ConnectRigAsync();

        if (_settings.Width > 0 && _settings.Height > 0)
        {
            Width = Math.Max(MinWidth, _settings.Width);
            Height = Math.Max(MinHeight, _settings.Height);
        }
        if (_settings.Left is { } left && _settings.Top is { } top && IsOnScreen(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
        if (_smoke != null) Loaded += async (_, _) => await RunSmokeTest(_smoke);
        if (_contestSmoke != null) Loaded += async (_, _) => await RunContestSmoke(_contestSmoke);
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

    /// <summary>
    /// Fereastra salvata se vede macar in parte pe ecranele de acum (monitorul pe care era poate fi
    /// deconectat): cel putin 120 x 60 de pixeli din ea, cu bara de titlu in ecran.
    /// </summary>
    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, new Rect(left, top, width, height));
        return !visible.IsEmpty && visible.Width >= 120 && visible.Height >= 60
               && top >= screen.Top && top < screen.Bottom - 30;
    }

    // ------------------------------------------------------------------
    // Meniu
    // ------------------------------------------------------------------

    private void OnOpenWav(object sender, RoutedEventArgs e) => Decoder.OpenWavDialog();

    private void OnSaveText(object sender, RoutedEventArgs e) => Decoder.SaveTextDialog();

    private void OnSaveStations(object sender, RoutedEventArgs e) => Decoder.SaveStationsDialog();

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenFolder(CallsignCache);

    // ------------------------------------------------------------------
    // Statia (HamRig): aplicatia o detine, modulul o foloseste
    // ------------------------------------------------------------------

    private IRig? _rig;

    private async Task ConnectRigAsync()
    {
        DisconnectRig();
        if (_settings.Rig.CreateRig() is not { } rig)
        {
            _vm.AttachRig(null);
            return;
        }
        _rig = rig;
        _vm.RigFskInverted = _settings.Rig.InvertFsk;
        _vm.AttachRig(rig);
        try
        {
            await rig.ConnectAsync();
            _vm.Message = $"Statia: {rig.Description} - conectata.";
        }
        catch (Exception ex)
        {
            _vm.Message = "Statia: " + ex.Message + " (Setari > Reconecteaza statia)";
        }
    }

    private void DisconnectRig()
    {
        // emisia se opreste inainte ca statia sa fie deconectata (PTT si FSK pe 0)
        if (_vm.IsTransmitting || _vm.Tx.IsTransmitting) _vm.StopTx();
        var r = _rig;
        _rig = null;
        _vm.AttachRig(null);
        r?.Dispose();
    }

    private async void OnRigSettings(object sender, RoutedEventArgs e)
    {
        // portul trebuie eliberat, ca "Testeaza" din fereastra sa-l poata deschide
        DisconnectRig();
        var dlg = new RigSettingsWindow(_settings.Rig) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _settings.Rig = dlg.Result;
            _settings.Save();
        }
        await ConnectRigAsync();
    }

    private async void OnRigReconnect(object sender, RoutedEventArgs e) => await ConnectRigAsync();

    private void OnTxSettings(object sender, RoutedEventArgs e)
    {
        var dlg = new TxSettingsWindow(_vm.TxSettings) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _settings.Tx = dlg.Result;
        _vm.TxSettings = dlg.Result;
        _settings.Save();
    }

    private void OnLookupSettings(object sender, RoutedEventArgs e)
    {
        var dlg = new LookupSettingsWindow(_settings.QrzUsername ?? "", _settings.GetQrzPassword(),
            _settings.HamQthUsername ?? "", _settings.GetHamQthPassword()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _settings.QrzUsername = dlg.QrzUsername;
        _settings.SetQrzPassword(dlg.QrzPassword);
        _settings.HamQthUsername = dlg.HamQthUsername;
        _settings.SetHamQthPassword(dlg.HamQthPassword);
        _settings.Save();
        _vm.SetOnlineCredentials(_settings.QrzUsername, _settings.GetQrzPassword(), _settings.HamQthUsername, _settings.GetHamQthPassword());
        _vm.Message = _vm.OnlineVerificationEnabled
            ? "Verificare online activa: indicativele necunoscute local se verifica pe QRZ.com / HamQTH.com."
            : "Verificare online oprita (niciun cont completat).";
    }

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

    private RttyDecoder.Wpf.Help.HelpWindow? _helpWindow;

    /// <summary>Ajutor > Ajutor: fereastra nemodala, una singura; a doua comanda o aduce in fata.</summary>
    private void OnHelp(object sender, RoutedEventArgs e) => ShowHelp(null);

    /// <summary>Fereastra de ajutor (una singura), optional la un subiect (de ex. "10-contest").</summary>
    internal void ShowHelp(string? topic)
    {
        if (_helpWindow != null)
        {
            if (_helpWindow.WindowState == WindowState.Minimized) _helpWindow.WindowState = WindowState.Normal;
            if (topic != null) _helpWindow.ShowTopic(topic);
            _helpWindow.Activate();
            return;
        }
        _helpWindow = new RttyDecoder.Wpf.Help.HelpWindow(topic) { Owner = this };
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var a = Assembly.GetExecutingAssembly();
        string version = ApplicationVersion(a);
        MessageBox.Show(this,
            $"RTTY Decoder {version}\n" +
            "Copyright (c) 2026 Eugen Trifu, YO9VA - licenta MIT\n\n" +
            "Decodor RTTY (Chebyshev, Matched Filter, DeepRTTY) portat din aplicatia Android Yaesu-FTX1-CAT-Control.\n" +
            "Indicative: biblioteca HamCallsign (cty.dat AD1C, MASTER.SCP, LoTW).\n\n" +
            "https://github.com/Eugen68/RttyDecoder" +
            (BuildInfo(a) is { Length: > 0 } build ? "\n\n" + build : ""),
            "Despre RTTY Decoder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string ApplicationVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString() ?? "";

    /// <summary>
    /// "Build #57 · commit 82e01be · 06.11.2026 14:32" (ora locala), din metadatele puse la compilare
    /// (RttyDecoder.App.csproj, tinta RttyBuildInfo). Ce lipseste nu apare.
    /// </summary>
    internal static string BuildInfo(Assembly assembly)
    {
        string? Meta(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == key)?.Value is { Length: > 0 } v ? v : null;
        var parts = new List<string>();
        if (Meta("BuildNumber") is { } number) parts.Add("Build #" + number);
        if (Meta("BuildCommit") is { } commit) parts.Add("commit " + commit);
        if (Meta("BuildTime") is { } time && DateTime.TryParse(time, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            parts.Add(utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture));
        return string.Join(" · ", parts);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_contestModel != null && !_contestModel.Flush())
        {
            _vm.Message = _contestModel.Status; e.Cancel = true;
            MessageBox.Show(this, _contestModel.Status, "Salvare concurs", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (_smoke != null || _contestSmoke != null)
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
        _settings.Tx = _vm.TxSettings.Clone();
        _settings.Tx.InvertFsk = false;   // tine de statie (Rig.InvertFsk)
        _settings.Macros = _vm.ExportMacros();
        _settings.Waterfall = Decoder.WaterfallSettings;
        _settings.ShowStations = _vm.ShowStations;
        if (_contestWindow != null) SaveContestPlacement(_contestWindow);
        _settings.Maximized = WindowState == WindowState.Maximized;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Width = b.Width;
        _settings.Height = b.Height;
        if (!double.IsNaN(b.Left) && !double.IsInfinity(b.Left))
        {
            _settings.Left = b.Left;
            _settings.Top = b.Top;
        }
        _settings.Save();
        DisconnectRig();
        _vm.Dispose();
        base.OnClosing(e);
    }
}
