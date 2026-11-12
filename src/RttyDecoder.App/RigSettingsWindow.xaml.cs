using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HamRig;

namespace RttyDecoder.App;

/// <summary>
/// Setari > Conexiune statie: direct (CAT Yaesu) sau OmniRig, plus portul de manipulare si PTT.
/// PTT-ul se alege intr-un singur loc (pinul portului CAT sau al portului de manipulare); fara pin,
/// la conexiunea directa emisia se comanda prin CAT, la OmniRig nu exista PTT. Cat timp fereastra
/// e deschisa, MainWindow tine statia deconectata, ca "Testeaza" sa poata deschide porturile.
/// </summary>
public partial class RigSettingsWindow : Window
{
    private static readonly int[] Bauds = { 4800, 9600, 19200, 38400, 57600, 115200 };

    private static readonly (SerialPin Pin, string Text)[] Pins =
    {
        (SerialPin.None, "Fara"),
        (SerialPin.Rts, "RTS"),
        (SerialPin.Dtr, "DTR"),
    };

    private bool _busy;
    private bool _updating;

    public RigSettingsWindow(RigSettings settings)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        SizeChanged += (_, _) => FitToScreen();
        _updating = true;
        foreach (int b in Bauds)
        {
            BaudBox.Items.Add(b);
            KeyBaudBox.Items.Add(b);
            HamlibBaudBox.Items.Add(b);
        }
        foreach (var (_, text) in Pins)
        {
            CatPttBox.Items.Add(text);
            KeyPttBox.Items.Add(text);
            KeyPinBox.Items.Add(text);
        }
        OmniRigBox.Items.Add("Rig 1");
        OmniRigBox.Items.Add("Rig 2");
        FillPorts();

        CatPortBox.Text = settings.CatPort ?? (CatPortBox.Items.Count > 0 ? (string)CatPortBox.Items[0]! : "");
        BaudBox.SelectedItem = Bauds.Contains(settings.CatBaud) ? settings.CatBaud : 38400;
        CatPttBox.SelectedIndex = Index(settings.CatPttPin);
        OmniRigBox.SelectedIndex = settings.OmniRigNumber == 2 ? 1 : 0;
        KeyPortBox.Text = settings.KeyPort ?? "";
        KeyBaudBox.SelectedItem = Bauds.Contains(settings.KeyBaud) ? settings.KeyBaud : 4800;
        KeyPttBox.SelectedIndex = Index(settings.KeyPortPttPin);
        KeyPinBox.SelectedIndex = Index(settings.KeyPin);
        InvertFskBox.IsChecked = settings.InvertFsk;
        // Hamlib: portul si viteza statiei sunt CatPort / CatBaud (rigctld le foloseste)
        HamlibPortBox.Text = settings.CatPort ?? "";
        HamlibBaudBox.SelectedItem = Bauds.Contains(settings.CatBaud) ? settings.CatBaud : 38400;
        HamlibStartBox.IsChecked = settings.HamlibStartDaemon;
        HamlibPathBox.Text = settings.HamlibPath ?? "";
        HamlibModelBox.Text = settings.HamlibModel > 0 ? settings.HamlibModel.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        HamlibHostBox.Text = settings.HamlibHost;
        HamlibTcpPortBox.Text = settings.HamlibPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HamlibArgsBox.Text = settings.HamlibExtraArgs ?? "";
        (settings.Kind switch { RigKind.YaesuCat => KindDirect, RigKind.OmniRig => KindOmni, RigKind.Hamlib => KindHamlib, _ => KindNone }).IsChecked = true;
        _updating = false;
        UpdatePanels();
    }

    /// <summary>Setarile alese (dupa ShowDialog() == true).</summary>
    public RigSettings Result => Read();

    private static int Index(SerialPin pin) => Math.Max(0, Array.FindIndex(Pins, p => p.Pin == pin));

    private static SerialPin Pin(ComboBox box) => box.SelectedIndex >= 0 ? Pins[box.SelectedIndex].Pin : SerialPin.None;

    private static string? Port(ComboBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim().ToUpperInvariant();

    private void FillPorts()
    {
        string cat = CatPortBox.Text, key = KeyPortBox.Text, ham = HamlibPortBox.Text;
        CatPortBox.Items.Clear();
        KeyPortBox.Items.Clear();
        HamlibPortBox.Items.Clear();
        KeyPortBox.Items.Add("");
        foreach (string p in RigSettings.GetPortNames())
        {
            CatPortBox.Items.Add(p);
            KeyPortBox.Items.Add(p);
            HamlibPortBox.Items.Add(p);
        }
        CatPortBox.Text = cat;
        KeyPortBox.Text = key;
        HamlibPortBox.Text = ham;
    }

    private RigKind Kind => KindDirect.IsChecked == true ? RigKind.YaesuCat : KindOmni.IsChecked == true ? RigKind.OmniRig
        : KindHamlib.IsChecked == true ? RigKind.Hamlib : RigKind.None;

    /// <summary>Numarul modelului: inceputul textului ("1042 · Yaesu FTDX-10" sau doar "1042").</summary>
    private int HamlibModelId()
    {
        if (HamlibModelBox.SelectedItem is HamlibModel m && HamlibModelBox.Text == m.ToString()) return m.Id;
        string digits = new(HamlibModelBox.Text.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0;
    }

    private RigSettings Read() => new()
    {
        Kind = Kind,
        CatPort = Kind == RigKind.Hamlib ? Port(HamlibPortBox) : Port(CatPortBox),
        CatBaud = Kind == RigKind.Hamlib ? (HamlibBaudBox.SelectedItem is int hb ? hb : 38400) : BaudBox.SelectedItem is int b ? b : 38400,
        HamlibStartDaemon = HamlibStartBox.IsChecked == true,
        HamlibPath = string.IsNullOrWhiteSpace(HamlibPathBox.Text) ? null : HamlibPathBox.Text.Trim(),
        HamlibModel = HamlibModelId(),
        HamlibHost = string.IsNullOrWhiteSpace(HamlibHostBox.Text) ? "127.0.0.1" : HamlibHostBox.Text.Trim(),
        HamlibPort = int.TryParse(HamlibTcpPortBox.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int tcp) ? tcp : 0,
        HamlibExtraArgs = string.IsNullOrWhiteSpace(HamlibArgsBox.Text) ? null : HamlibArgsBox.Text.Trim(),
        // la OmniRig portul CAT e al lui OmniRig: pinul lui nu se foloseste
        CatPttPin = Kind == RigKind.YaesuCat ? Pin(CatPttBox) : SerialPin.None,
        OmniRigNumber = OmniRigBox.SelectedIndex == 1 ? 2 : 1,
        KeyPort = Port(KeyPortBox),
        KeyBaud = KeyBaudBox.SelectedItem is int kb ? kb : 4800,
        KeyPortPttPin = Pin(KeyPttBox),
        KeyPin = Pin(KeyPinBox),
        InvertFsk = InvertFskBox.IsChecked == true,
    };

    private void UpdatePanels()
    {
        if (_updating || DirectPanel == null) return;
        _updating = true;
        try
        {
            bool direct = Kind == RigKind.YaesuCat, omni = Kind == RigKind.OmniRig, hamlib = Kind == RigKind.Hamlib, any = Kind != RigKind.None;
            // doar sectiunea conexiunii alese; celelalte nu ocupa loc
            Show(DirectPanel, direct);
            Show(OmniPanel, omni);
            Show(HamlibPanel, hamlib);
            Show(KeyPanel, any);
            Show(KeyHeader, any);
            // rigctld deja pornit: modelul, portul si viteza sunt ale lui
            bool start = HamlibStartBox.IsChecked == true;
            HamlibPathBox.IsEnabled = HamlibModelBox.IsEnabled = HamlibPortBox.IsEnabled = HamlibBaudBox.IsEnabled = HamlibArgsBox.IsEnabled = start;
            if (hamlib && start && HamlibModelBox.Items.Count == 0 && !_modelsTried) _ = LoadHamlibModels(quiet: true);

            // PTT intr-un singur loc: daca e ales un pin intr-o parte, cealalta trece pe Fara si nu se mai poate alege
            bool catPtt = direct && Pin(CatPttBox) != SerialPin.None;
            bool keyPtt = Pin(KeyPttBox) != SerialPin.None;
            if (catPtt && keyPtt) KeyPttBox.SelectedIndex = 0;
            keyPtt = Pin(KeyPttBox) != SerialPin.None;
            CatPttBox.IsEnabled = direct && !keyPtt;
            KeyPttBox.IsEnabled = any && !catPtt;

            var s = Read();
            TestButton.IsEnabled = any && !_busy;
            // fara pin: la conexiunea directa PTT prin CAT, la Hamlib prin rigctld; la OmniRig nu exista PTT
            PttTestButton.IsEnabled = any && !_busy && (direct || hamlib || s.PttLine.Pin != SerialPin.None);
            KeyTestButton.IsEnabled = any && !_busy && s.HasKeying;
        }
        finally
        {
            _updating = false;
        }
    }

    private static void Show(UIElement panel, bool on) => panel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fereastra nu iese din ecran: inaltimea maxima = zona de lucru, iar marginea de jos ramane vizibila.</summary>
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        MaxHeight = area.Height;
        if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top, area.Bottom - ActualHeight);
        if (Top < area.Top) Top = area.Top;
    }

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        // conexiunea directa e doar Yaesu: pin activ = Mark
        if (!_updating && Kind == RigKind.YaesuCat) InvertFskBox.IsChecked = false;
        UpdatePanels();
    }

    /// <summary>Model ales din lista Hamlib: polaritatea FSK obisnuita a fabricantului (Icom = inversat).</summary>
    private void OnHamlibModelPicked(object? sender, EventArgs e)
    {
        if (HamlibModelBox.SelectedItem is HamlibModel m) InvertFskBox.IsChecked = RigSettings.DefaultInvertFsk(m.Manufacturer);
    }

    private void OnHamlibStartChanged(object sender, RoutedEventArgs e) => UpdatePanels();

    private bool _modelsTried;

    /// <summary>Lista modelelor Hamlib (rigctl -l), in fundal; numarul deja ales ramane selectat.</summary>
    private async Task LoadHamlibModels(bool quiet)
    {
        _modelsTried = true;
        string? path = string.IsNullOrWhiteSpace(HamlibPathBox.Text) ? Hamlib.FindRigctld() : HamlibPathBox.Text.Trim();
        if (path == null)
        {
            if (!quiet) Status("Nu gasesc rigctld: instaleaza Hamlib sau alege calea catre rigctld.exe.", false);
            return;
        }
        try
        {
            var (models, version) = await Task.Run(() => (Hamlib.ListModels(path), Hamlib.GetVersion(path)));
            int current = HamlibModelId();
            HamlibModelBox.Items.Clear();
            foreach (var m in models.OrderBy(m => m.Manufacturer, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Model, StringComparer.OrdinalIgnoreCase))
                HamlibModelBox.Items.Add(m);
            if (models.FirstOrDefault(m => m.Id == current) is { } selected) HamlibModelBox.SelectedItem = selected;
            if (string.IsNullOrWhiteSpace(HamlibPathBox.Text)) HamlibPathBox.Text = path;
            // versiune veche: avertisment si cand lista se incarca singura
            if (version != null && version < Hamlib.RecommendedVersion)
                Status($"Hamlib {version} - recomandat {Hamlib.RecommendedVersion} sau mai nou: corecturi de securitate in rigctld si suport FTX-1 (model 1051).", false);
            else if (!quiet) Status($"Hamlib {version?.ToString() ?? "?"} · {models.Count} modele.", true);
        }
        catch (RigException ex)
        {
            if (!quiet) Status("✘ " + ex.Message, false);
        }
    }

    private void OnHamlibModels(object sender, RoutedEventArgs e) => _ = LoadHamlibModels(quiet: false);

    private void OnHamlibPathChanged(object sender, RoutedEventArgs e) => _modelsTried = false;

    private void OnHamlibBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "rigctld.exe (Hamlib)", Filter = "rigctld|rigctld.exe|Programe|*.exe",
            FileName = HamlibPathBox.Text,
        };
        if (dialog.ShowDialog(this) != true) return;
        HamlibPathBox.Text = dialog.FileName;
        _ = LoadHamlibModels(quiet: false);
    }

    private void OnPinChanged(object sender, SelectionChangedEventArgs e) => UpdatePanels();

    private void OnRefreshPorts(object sender, RoutedEventArgs e) => FillPorts();

    private void Status(string text, bool? ok)
    {
        TestStatus.Text = text;
        TestStatus.Foreground = ok switch
        {
            true => (Brush)FindResource("Call.Known"),
            false => Brushes.IndianRed,
            null => (Brush)FindResource("Rtty.Muted"),
        };
    }

    private enum Test { Read, Ptt, Key }

    private void OnTest(object sender, RoutedEventArgs e) => _ = RunTest(Test.Read);

    private void OnTestPtt(object sender, RoutedEventArgs e) => _ = RunTest(Test.Ptt);

    private void OnTestKey(object sender, RoutedEventArgs e) => _ = RunTest(Test.Key);

    private async Task RunTest(Test test)
    {
        var s = Read();
        if (s.Validate() is { } problem)
        {
            Status(problem, false);
            return;
        }
        if (s.CreateRig() is not { } rig) return;
        _busy = true;
        UpdatePanels();
        Status("Se conecteaza...", null);
        try
        {
            await rig.ConnectAsync();
            await Task.Delay(300); // modul si starea TX vin imediat dupa frecventa
            var st = rig.State;
            string read = st.Connected
                ? $"✔ {RigMath.FormatFrequency(st.FrequencyHz)} {RigMath.ModeName(st.Mode)} - {rig.Description}"
                : "✘ " + st.StatusText;
            if (test == Test.Ptt && st.Connected)
            {
                rig.SetPtt(true);
                await Task.Delay(500);
                rig.SetPtt(false);
                var (port, pin) = s.PttLine;
                read += pin != SerialPin.None ? $"  |  PTT: {pin} pe {port} 0.5 s"
                    : s.Kind == RigKind.Hamlib ? "  |  PTT prin Hamlib (T 1) 0.5 s"
                    : "  |  PTT prin CAT (TX1;) 0.5 s";
            }
            if (test == Test.Key && st.Connected)
            {
                rig.SetKey(true);
                await Task.Delay(500);
                rig.SetKey(false);
                read += $"  |  manipulare: {s.KeyPin} pe {s.KeyPort} 0.5 s";
            }
            Status(read, st.Connected);
        }
        catch (Exception ex)
        {
            Status("✘ " + ex.Message, false);
        }
        finally
        {
            rig.Dispose();
            _busy = false;
            UpdatePanels();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Read().Validate() is { } problem)
        {
            Status(problem, false);
            return;
        }
        DialogResult = true;
    }
}
